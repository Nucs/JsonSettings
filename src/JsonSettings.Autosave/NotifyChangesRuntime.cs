#nullable enable
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Nucs.JsonSettings.Examples;

namespace Nucs.JsonSettings.Autosave {
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
    ///     Like <see cref="AutosaveRuntime"/>, this is on the hot path of every write to a woven
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

        //Ecosystem raiser conventions, most-specific first. Covers the common MVVM bases so a class
        //that already derives from one of them gets automatic notifications through [NotifyChanges]
        //without a JsonSettings-specific base:
        //  OnPropertyChanged        - CommunityToolkit.Mvvm ObservableObject, our NotifiyingJsonSettings
        //  RaisePropertyChanged     - Prism BindableBase, MvvmLight/CommunityToolkit legacy ViewModelBase
        //  NotifyOfPropertyChange   - Caliburn.Micro PropertyChangedBase
        private static readonly string[] _raiserConventions = {
            "OnPropertyChanged", "RaisePropertyChanged", "NotifyOfPropertyChange"
        };

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
