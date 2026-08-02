#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Nucs.JsonSettings.Examples;
using Nucs.JsonSettings.Modulation;

namespace Nucs.JsonSettings.NotifyChanges {
    /// <summary>
    ///     Marks an aspect attribute that carries a <see cref="NotificationGuard"/>, so the runtime
    ///     can resolve the configured guard from whichever notification attribute a type or property
    ///     is annotated with, without knowing which of the two it is.
    /// </summary>
    internal interface IHasNotificationGuard {
        NotificationGuard Guard { get; }
    }

    /// <summary>
    ///     The runtime half shared by <see cref="NotifyChangesAttribute"/> (advice-only) and
    ///     <see cref="NotifyChangesMixinAttribute"/> (mixin). Infrastructure -- called from woven
    ///     setters, not intended to be called directly.
    /// </summary>
    /// <remarks>
    ///     The two aspects differ only in <em>where the notification is raised</em>: the advice-only
    ///     one calls the class's own raiser (a <see cref="NotifiyingJsonSettings"/> base, or an
    ///     ecosystem convention method), the mixin one raises the event it injected into the class.
    ///     Everything before that -- resolving the guard, capturing the old value, and deciding
    ///     whether the assignment counts as a change worth announcing -- is identical, and lives
    ///     here so the two paths cannot drift.
    ///
    ///     Like <c>AutosaveRuntime</c>, this is on the hot path of every write to a woven
    ///     type, so per-type/per-property reflection is resolved once and cached. Nothing here emits
    ///     runtime code, so it stays Native-AOT-safe the same way the weaving does; the reflection it
    ///     does perform (reading a getter, reading an attribute) needs the settings model preserved
    ///     under trimming exactly as the serializer already does.
    /// </remarks>
    public static class NotifyChangesRuntime {
        private static readonly ConcurrentDictionary<(Type Type, string Property), NotificationGuard> _guardCache =
            new ConcurrentDictionary<(Type, string), NotificationGuard>();

        private static readonly ConcurrentDictionary<(Type Type, string Property), PropertyInfo?> _propertyCache =
            new ConcurrentDictionary<(Type, string), PropertyInfo?>();

        private static readonly ConcurrentDictionary<Type, MethodInfo?> _raiserCache =
            new ConcurrentDictionary<Type, MethodInfo?>();

        private static readonly ConcurrentDictionary<(Type Type, string Property), bool> _ignoreCache =
            new ConcurrentDictionary<(Type, string), bool>();

        //Ecosystem raiser conventions, most-specific first. Covers the common MVVM bases so a class
        //that already derives from one of them gets automatic notifications through [NotifyChanges]
        //without a JsonSettings-specific base:
        //  OnPropertyChanged        - CommunityToolkit.Mvvm ObservableObject, our NotifiyingJsonSettings
        //  RaisePropertyChanged     - Prism BindableBase, MvvmLight/CommunityToolkit legacy ViewModelBase
        //  NotifyOfPropertyChange   - Caliburn.Micro PropertyChangedBase
        private static readonly string[] _raiserConventions = {
            "OnPropertyChanged", "RaisePropertyChanged", "NotifyOfPropertyChange"
        };

        //The INotifyPropertyChanging-side raiser, resolved and cached exactly like _raiserCache.
        private static readonly ConcurrentDictionary<Type, MethodInfo?> _changingRaiserCache =
            new ConcurrentDictionary<Type, MethodInfo?>();

        //Changing-side conventions, mirroring _raiserConventions. Caliburn.Micro has no
        //"NotifyOfPropertyChanging", so only the two that exist in the wild are listed:
        //  OnPropertyChanging     - our NotifiyingJsonSettings, CommunityToolkit ObservableObject
        //  RaisePropertyChanging  - Prism BindableBase
        private static readonly string[] _changingRaiserConventions = {
            "OnPropertyChanging", "RaisePropertyChanging"
        };

        //Per (type, property) list of OTHER property names to also raise when this property changes,
        //declared with [NotifyChangesFor]. Empty array = none; resolved once, then a hash lookup.
        private static readonly ConcurrentDictionary<(Type Type, string Property), string[]> _dependentsCache =
            new ConcurrentDictionary<(Type, string), string[]>();

        //Opt-in marshalling target per settings instance (see EnableNotificationMarshaling). A
        //ConditionalWeakTable keys on the instance without keeping it alive and needs no core change,
        //so it works uniformly for the notifying base, a convention class, and a mixin class alike.
        private static readonly ConditionalWeakTable<object, SynchronizationContext> _marshalContexts =
            new ConditionalWeakTable<object, SynchronizationContext>();

        /// <summary>
        ///     Captured at the start of a woven setter, before the assignment runs. Carries whatever
        ///     the guard will need after the assignment to decide if a notification is owed.
        /// </summary>
        public readonly struct NotifyDecision {
            internal readonly NotificationGuard Guard;
            internal readonly object? OldValue;

            internal NotifyDecision(NotificationGuard guard, object? oldValue) {
                Guard = guard;
                OldValue = oldValue;
            }
        }

        /// <summary>
        ///     Whether a woven setter must stay silent: framework-managed properties, indexers, and
        ///     anything the user marked <see cref="IgnoreNotifyAttribute"/>. Checked before
        ///     <see cref="Prepare"/> so an ignored write pays for neither the guard nor the getter read.
        /// </summary>
        /// <remarks>
        ///     Notification opt-out is deliberately <em>independent</em> of persistence opt-out: this
        ///     does NOT consult <see cref="Newtonsoft.Json.JsonIgnoreAttribute"/> or
        ///     <see cref="IgnoreAutosaveAttribute"/>. A property can be saved but silent
        ///     (<c>[IgnoreNotify]</c>) or observable but never persisted (<c>[JsonIgnore]</c> /
        ///     <c>[IgnoreAutosave]</c> with a setter). Only the framework's own writes are excluded,
        ///     mirroring <see cref="AutosaveModule"/>: <see cref="JsonSettings.FileName"/> is assigned
        ///     by <c>Save()</c> itself (so notifying on it would fire on every save),
        ///     <see cref="JsonSettings.Modulation"/> is plumbing, and <see cref="IVersionable.Version"/>
        ///     is written by the versioning module during load and recovery.
        /// </remarks>
        public static bool IsNotifyIgnored(Type type, string propertyName) {
            return _ignoreCache.GetOrAdd((type, propertyName), static key => {
                var (t, name) = key;

                if (name == nameof(JsonSettings.FileName) || name == nameof(JsonSettings.Modulation))
                    return true;
                if (name == nameof(IVersionable.Version) && typeof(IVersionable).IsAssignableFrom(t))
                    return true;

                var property = GetProperty(t, name);
                if (property == null)
                    return false;

                if (property.GetIndexParameters().Length != 0)
                    return true; //an indexer: PropertyChanged has no single property name to carry

                return property.GetCustomAttribute<IgnoreNotifyAttribute>(true) != null;
            });
        }

        /// <summary>
        ///     Runs at the top of a woven setter, before the assignment. Resolves the guard for the
        ///     property and, only when the guard needs it, reads the current value so it can be
        ///     compared to the incoming one after the setter runs.
        /// </summary>
        public static NotifyDecision Prepare(object instance, string propertyName) {
            var guard = ResolveGuard(instance.GetType(), propertyName);
            object? old = (guard & NotificationGuard.OnlyChanged) != 0
                ? TryReadCurrentValue(instance, propertyName)
                : null;
            return new NotifyDecision(guard, old);
        }

        /// <summary>
        ///     Runs after the assignment. Applies the guard captured by <see cref="Prepare"/> to
        ///     decide whether this write should raise a notification.
        /// </summary>
        /// <param name="decision">The value returned by <see cref="Prepare"/> for this setter call.</param>
        /// <param name="arguments">
        ///     The woven setter's arguments. For an ordinary property this is <c>[value]</c>; for an
        ///     indexer it is <c>[index, ..., value]</c>, so the assigned value is always the last one.
        /// </param>
        public static bool ShouldRaise(NotifyDecision decision, object[] arguments) {
            object? newValue = arguments != null && arguments.Length > 0
                ? arguments[arguments.Length - 1]
                : null;

            if ((decision.Guard & NotificationGuard.OnlyChanged) != 0 && Equals(decision.OldValue, newValue))
                return false;

            if ((decision.Guard & NotificationGuard.SkipNullOrDefault) != 0 && IsNullOrDefault(newValue))
                return false;

            return true;
        }

        /// <summary>
        ///     Raises the change notification for a class that owns its own event -- either the
        ///     shipped <see cref="NotifiyingJsonSettings"/> base or any class exposing a conventional
        ///     raiser method (<c>OnPropertyChanged</c>, <c>RaisePropertyChanged</c>,
        ///     <c>NotifyOfPropertyChange</c>). Used by the advice-only <see cref="NotifyChangesAttribute"/>.
        /// </summary>
        /// <remarks>
        ///     A class that implements <see cref="System.ComponentModel.INotifyPropertyChanged"/> with
        ///     nothing but the event -- no raiser method -- cannot be driven from here, because the
        ///     event can only be raised from inside the declaring type. That is exactly the case
        ///     <see cref="NotifyChangesMixinAttribute"/> exists for.
        /// </remarks>
        public static void RaiseViaBaseOrConvention(object instance, string propertyName) {
            if (instance is NotifiyingJsonSettings notifying) {
                notifying.OnPropertyChanged(propertyName);
                return;
            }

            var raiser = _raiserCache.GetOrAdd(instance.GetType(), ResolveRaiser);
            raiser?.Invoke(instance, new object[] { propertyName });
        }

        /// <summary>
        ///     Raises the "changed" notification for <paramref name="propertyName"/> and then for every
        ///     property that named it in a <see cref="NotifyChangesForAttribute"/>, routing the whole
        ///     batch through the instance's marshalling context if one was captured. This is the single
        ///     entry point both aspects use for the post-assignment raise.
        /// </summary>
        /// <param name="raiseOne">
        ///     How to raise one property name on the instance: the advice-only aspect passes
        ///     <see cref="RaiseViaBaseOrConvention"/>; the mixin passes its injected event. The
        ///     dependents raise the same way as the source, so a computed property notifies through
        ///     whatever channel the class already uses.
        /// </param>
        /// <remarks>
        ///     Primary and dependents are raised inside one <see cref="Dispatch"/> so that, when
        ///     marshalling is on, they arrive on the UI thread together and in declared order rather
        ///     than as separate posts. Dependents fire unconditionally once the source raised -- they
        ///     are derived values, so the source's change guard is the only gate; a target that is
        ///     itself framework-managed, an indexer, or <c>[IgnoreNotify]</c> is skipped by
        ///     <see cref="GetDependents"/>.
        /// </remarks>
        public static void RaiseChangedAndDependents(object instance, string propertyName, Action<object, string> raiseOne) {
            Dispatch(instance, () => {
                raiseOne(instance, propertyName);
                var dependents = GetDependents(instance.GetType(), propertyName);
                for (int i = 0; i < dependents.Length; i++)
                    raiseOne(instance, dependents[i]);
            });
        }

        /// <summary>
        ///     Raises the "changing" notification (before the assignment) for a class that supports it
        ///     -- the shipped <see cref="NotifiyingJsonSettings"/> base or a class exposing a
        ///     conventional <c>OnPropertyChanging</c> / <c>RaisePropertyChanging</c> raiser. A class
        ///     that implements neither is a harmless no-op, exactly like the changed side. Used by the
        ///     advice-only <see cref="NotifyChangesAttribute"/>.
        /// </summary>
        /// <remarks>
        ///     Deliberately NOT routed through <see cref="Dispatch"/>: a "changing" event that means
        ///     "the value is about to change" must fire synchronously before the setter body runs, and
        ///     posting it to another thread would deliver it after the change. Marshalling targets the
        ///     binding-relevant changed notification; <see cref="System.ComponentModel.INotifyPropertyChanging"/>
        ///     consumers (change trackers, validators) are not the cross-thread-collection case it
        ///     exists for.
        /// </remarks>
        public static void RaiseChangingViaBaseOrConvention(object instance, string propertyName) {
            if (instance is NotifiyingJsonSettings notifying) {
                notifying.OnPropertyChanging(propertyName);
                return;
            }

            var raiser = _changingRaiserCache.GetOrAdd(instance.GetType(), ResolveChangingRaiser);
            raiser?.Invoke(instance, new object[] { propertyName });
        }

        /// <summary>
        ///     Runs <paramref name="raise"/> on the instance's captured marshalling context when one was
        ///     set (see <c>EnableNotificationMarshaling</c>) and the caller is not already on it;
        ///     otherwise runs it inline. Off by default -- with no captured context this is a direct call.
        /// </summary>
        private static void Dispatch(object instance, Action raise) {
            if (_marshalContexts.TryGetValue(instance, out var context) && context != null && context != SynchronizationContext.Current)
                context.Post(static state => ((Action) state!).Invoke(), raise);
            else
                raise();
        }

        /// <summary>
        ///     Records the <see cref="SynchronizationContext"/> that woven setters should post their
        ///     change notifications to for <paramref name="instance"/>. Replaces any previous one.
        /// </summary>
        internal static void SetMarshalContext(object instance, SynchronizationContext context) {
            //No AddOrUpdate on netstandard2.0's ConditionalWeakTable; remove-then-add is the portable
            //form. Capture is a one-time setup call, so the tiny window between the two is immaterial.
            _marshalContexts.Remove(instance);
            _marshalContexts.Add(instance, context);
        }

        /// <summary>
        ///     Stops marshalling notifications for <paramref name="instance"/>; subsequent raises run
        ///     inline again. Returns whether a context was actually removed.
        /// </summary>
        internal static bool RemoveMarshalContext(object instance) {
            return _marshalContexts.Remove(instance);
        }

        /// <summary>
        ///     The other property names to raise when <paramref name="propertyName"/> changes, from the
        ///     <see cref="NotifyChangesForAttribute"/>(s) on that property. De-duplicated, in declared
        ///     order, with self-references and non-notifiable targets (framework-managed, indexer,
        ///     <c>[IgnoreNotify]</c>) removed. Resolved once per (type, property), then a hash lookup.
        /// </summary>
        internal static string[] GetDependents(Type type, string propertyName) {
            return _dependentsCache.GetOrAdd((type, propertyName), key => {
                var (t, name) = key;
                var property = GetProperty(t, name);
                if (property == null)
                    return Array.Empty<string>();

                var declared = property.GetCustomAttributes<NotifyChangesForAttribute>(true)
                                       .SelectMany(a => a.OtherProperties ?? Array.Empty<string>());

                var result = new List<string>();
                foreach (var target in declared) {
                    if (string.IsNullOrEmpty(target) || target == name)
                        continue; //ignore empties and a property naming itself
                    if (IsNotifyIgnored(t, target))
                        continue; //do not resurrect a framework / indexer / [IgnoreNotify] target
                    if (!result.Contains(target))
                        result.Add(target);
                }

                return result.Count == 0 ? Array.Empty<string>() : result.ToArray();
            });
        }

        /// <summary>
        ///     The guard in effect for a property: its own <c>[NotifyChanges]</c>/<c>[NotifyChangesMixin]</c>
        ///     if it carries one, otherwise the nearest one on the class hierarchy, otherwise the
        ///     default <see cref="NotificationGuard.OnlyChanged"/>.
        /// </summary>
        internal static NotificationGuard ResolveGuard(Type type, string propertyName) {
            return _guardCache.GetOrAdd((type, propertyName), static key => {
                var (t, name) = key;

                var property = GetProperty(t, name);
                if (property != null) {
                    var perProperty = property.GetCustomAttributes(true).OfType<IHasNotificationGuard>().FirstOrDefault();
                    if (perProperty != null)
                        return perProperty.Guard;
                }

                for (var declaring = t; declaring != null; declaring = declaring.BaseType) {
                    var perClass = declaring.GetCustomAttributes(false).OfType<IHasNotificationGuard>().FirstOrDefault();
                    if (perClass != null)
                        return perClass.Guard;
                }

                return NotificationGuard.OnlyChanged;
            });
        }

        private static object? TryReadCurrentValue(object instance, string propertyName) {
            var property = _propertyCache.GetOrAdd((instance.GetType(), propertyName), static key => GetProperty(key.Type, key.Property));
            if (property == null || !property.CanRead)
                return null;
            try {
                return property.GetValue(instance);
            } catch {
                //an indexer, or a getter that throws mid-construction: fall back to "no old value",
                //which makes OnlyChanged behave as Always for this write rather than crash the setter.
                return null;
            }
        }

        private static PropertyInfo? GetProperty(Type type, string propertyName) {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
            try {
                return type.GetProperty(propertyName, flags);
            } catch (AmbiguousMatchException) {
                //a shadowed ("new") property of the same name; take the most-derived declaration.
                for (var declaring = type; declaring != null; declaring = declaring.BaseType) {
                    var property = declaring.GetProperty(propertyName, flags | BindingFlags.DeclaredOnly);
                    if (property != null)
                        return property;
                }
                return null;
            }
        }

        private static MethodInfo? ResolveRaiser(Type type) {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
            foreach (var name in _raiserConventions) {
                var method = type.GetMethod(name, flags, binder: null, types: new[] { typeof(string) }, modifiers: null);
                if (method != null && method.ReturnType == typeof(void))
                    return method;
            }
            return null;
        }

        private static MethodInfo? ResolveChangingRaiser(Type type) {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
            foreach (var name in _changingRaiserConventions) {
                var method = type.GetMethod(name, flags, binder: null, types: new[] { typeof(string) }, modifiers: null);
                if (method != null && method.ReturnType == typeof(void))
                    return method;
            }
            return null;
        }

        private static bool IsNullOrDefault(object? value) {
            if (value == null)
                return true;

            var type = value.GetType();
            if (!type.IsValueType)
                return false; //a non-null reference is never "default"

            //A boxed value type: compare to a boxed default of the same type. A one-element array of
            //the type is default-initialised and needs no Activator/Reflection.Emit, so this stays
            //AOT-safe. (A boxed Nullable<T> is either null -- handled above -- or a boxed T.)
            var boxedDefault = Array.CreateInstance(type, 1).GetValue(0);
            return value.Equals(boxedDefault);
        }
    }
}
