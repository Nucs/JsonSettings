#nullable enable
using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
#if NET6_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

namespace Nucs.JsonSettings.Reflection {
    /// <summary>
    ///     A process-wide cache of fast member accessors that stays Native-AOT-safe.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The runtime half of the notification aspects has to read a property's current value and,
    ///     for convention classes, call a raiser method -- on the hot path of every woven setter. The
    ///     callers already resolve and cache the <see cref="PropertyInfo"/>/<see cref="MethodInfo"/>;
    ///     what remained was the <em>invocation</em>, and <see cref="PropertyInfo.GetValue(object)"/> /
    ///     <see cref="MethodBase.Invoke(object,object[])"/> are an order of magnitude slower than a
    ///     direct call and allocate an <c>object[]</c> per call.
    ///     </para>
    ///     <para>
    ///     This compiles each accessor into a delegate <em>once</em> and reuses it. Compilation is gated
    ///     on <see cref="CanCompile"/>: on a JIT runtime an <see cref="Expression"/>-compiled delegate is
    ///     a near-direct call, and under Native AOT -- where <c>RuntimeFeature.IsDynamicCodeSupported</c>
    ///     is <see langword="false"/> and compiling would throw or drop to a slow interpreter -- it never
    ///     compiles and falls back to the reflective invoke instead. Both paths return behaviourally
    ///     identical delegates; only speed differs, so nothing here reintroduces the runtime code
    ///     generation the library dropped together with <c>Castle.DynamicProxy</c>. The reflection it
    ///     still performs under AOT needs the settings model preserved under trimming exactly as the
    ///     serializer does.
    ///     </para>
    /// </remarks>
    public static class ReflectionHelper {
        /// <summary>
        ///     Whether the current runtime can execute the delegates <see cref="Expression{TDelegate}.Compile()"/>
        ///     produces. <see langword="true"/> on every JIT runtime; <see langword="false"/> only under
        ///     Native AOT, where emitting code is not supported and the reflective fallback is used instead.
        /// </summary>
        public static readonly bool CanCompile =
#if NET6_0_OR_GREATER
            RuntimeFeature.IsDynamicCodeSupported;
#else
            true; //netstandard2.0 / net48 legs run only on JIT runtimes (.NET Framework, Mono, or a Core
                  //host that resolves the net6.0+ asset instead), where Expression.Compile always works.
#endif

        //Keyed by the MemberInfo itself, not (Type, name): the callers already resolve and cache the
        //PropertyInfo/MethodInfo, and reflection hands back the same instance for the same member, so a
        //single delegate is built per member and shared across every caller (e.g. the getter used by
        //both NotifyChangesRuntime and NotificationBinder for the same property).
        private static readonly ConcurrentDictionary<PropertyInfo, Func<object, object?>> _getters =
            new ConcurrentDictionary<PropertyInfo, Func<object, object?>>();

        private static readonly ConcurrentDictionary<MethodInfo, Action<object, string>> _stringInvokers =
            new ConcurrentDictionary<MethodInfo, Action<object, string>>();

        //Cached factory delegates so GetOrAdd on the hot path does not allocate a delegate per call (a
        //method-group argument is converted to a fresh delegate every time; a static field is not).
        private static readonly Func<PropertyInfo, Func<object, object?>> _buildGetter = BuildGetter;
        private static readonly Func<MethodInfo, Action<object, string>> _buildStringInvoker = BuildStringInvoker;

        /// <summary>
        ///     A delegate that reads <paramref name="property"/>'s value from an instance, boxing value
        ///     types. Built once and cached; falls back to a reflective read under Native AOT.
        /// </summary>
        /// <param name="property">The property to read; must have a get accessor (public or not).</param>
        /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="property"/> has no get accessor.</exception>
        public static Func<object, object?> Getter(PropertyInfo property) {
            if (property is null)
                throw new ArgumentNullException(nameof(property));
            return _getters.GetOrAdd(property, _buildGetter);
        }

        /// <summary>
        ///     A delegate that calls a <c>void M(string)</c> method on an instance. Built once and cached;
        ///     falls back to a reflective invoke under Native AOT. Used for the ecosystem raiser
        ///     conventions (<c>OnPropertyChanged</c>/<c>RaisePropertyChanged</c>/<c>NotifyOfPropertyChange</c>).
        /// </summary>
        /// <param name="method">A one-string-parameter, void-returning method.</param>
        /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
        public static Action<object, string> StringActionInvoker(MethodInfo method) {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            return _stringInvokers.GetOrAdd(method, _buildStringInvoker);
        }

        private static Func<object, object?> BuildGetter(PropertyInfo property) {
            var getMethod = property.GetGetMethod(true);
            if (getMethod is null)
                throw new ArgumentException($"Property '{property.DeclaringType?.FullName}.{property.Name}' has no get accessor.", nameof(property));

            if (CanCompile) {
                try {
                    //(object instance) => (object)(({DeclaringType})instance).{Property}
                    var instance = Expression.Parameter(typeof(object), "instance");
                    var typed = Expression.Convert(instance, getMethod.DeclaringType!);
                    Expression access = Expression.Call(typed, getMethod); //callvirt, so overrides dispatch correctly
                    var boxed = Expression.Convert(access, typeof(object));
                    return Expression.Lambda<Func<object, object?>>(boxed, instance).Compile();
                } catch (Exception) {
                    //An unexpected member shape the expression builder rejects: fall through to the
                    //reflective path below, which is always valid, rather than fail the caller.
                }
            }

            return instance => getMethod.Invoke(instance, null);
        }

        private static Action<object, string> BuildStringInvoker(MethodInfo method) {
            if (CanCompile) {
                try {
                    var instance = Expression.Parameter(typeof(object), "instance");
                    var arg = Expression.Parameter(typeof(string), "arg");
                    Expression call = method.IsStatic
                        ? Expression.Call(method, arg)
                        : Expression.Call(Expression.Convert(instance, method.DeclaringType!), method, arg);
                    return Expression.Lambda<Action<object, string>>(call, instance, arg).Compile();
                } catch (Exception) {
                    //fall through to the reflective invoke below
                }
            }

            return (instance, arg) => method.Invoke(instance, new object[] { arg });
        }
    }
}
