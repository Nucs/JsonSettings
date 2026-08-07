using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Module = Nucs.JsonSettings.Modulation.Module;

namespace Nucs.JsonSettings {
    public static class Activation {
        //Constructor metadata never changes for a loaded Type, so the reflective discovery these two
        //methods do -- two GetConstructors() calls plus a LINQ scan -- is resolved once per Type and
        //reused. It is not free per call: HasDefaultConstructor runs in the JsonSettings base constructor
        //(i.e. on every settings instantiation) and CreateInstance on every Load/Configure/Construct.
        //This caches reflection RESULTS; it emits no code, so it stays Native-AOT-safe.
        private static readonly ConcurrentDictionary<Type, bool> _hasDefaultConstructor = new ConcurrentDictionary<Type, bool>();

        //The resolved plan for the parameterless CreateInstance: either hand the type to Activator (value
        //type, or a public empty constructor) or Invoke the located non-public empty constructor. A null
        //Constructor with UseActivator=false encodes "no empty constructor at all", reproducing the
        //original throw without re-scanning.
        private static readonly ConcurrentDictionary<Type, (bool UseActivator, ConstructorInfo? Constructor)> _parameterlessPlan =
            new ConcurrentDictionary<Type, (bool UseActivator, ConstructorInfo? Constructor)>();

        public static IEnumerable<ConstructorInfo> GetAllConstructors(this Type t) => t.GetConstructors().Concat(t.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance));
        /// <summary>
        ///     Does the type have public/private/protected/internal.
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public static bool HasDefaultConstructor(this Type t) {
            return _hasDefaultConstructor.GetOrAdd(t, static type => {
                var ctrs = type.GetAllConstructors();
                ctrs = ctrs.Where(c => c.GetParameters().Length == 0 || c.GetParameters().All(p => p.IsOptional)).ToArray();
                return type.IsValueType || (ctrs.Any(c => c.GetParameters().Length == 0 || c.GetParameters().All(p => p.IsOptional)));
            });
        }

        public static object CreateInstance(this Type t) {
            var plan = _parameterlessPlan.GetOrAdd(t, static type => {
                var ctrs = type.GetAllConstructors().Where(c => c.GetParameters().Length == 0 || c.GetParameters().All(p => p.IsOptional)).ToArray();
                if (type.IsValueType || ctrs.Any(c => c.IsPublic)) //is valuetype or has public constractor.
                    return (true, (ConstructorInfo?) null);
                var prv = ctrs.FirstOrDefault(c => c.IsAssembly || c.IsFamily || c.IsPrivate); //check protected/internal/private constructor
                return (false, prv);
            });

            if (plan.UseActivator)
                return Activator.CreateInstance(t);
            var ctor = plan.Constructor ?? throw new ReflectiveException($"Type {t.FullName} does not have empty constructor (public or private)");
            return ctor.Invoke(null);
        }

        public static object CreateInstance(this Type t, object[] args) {
            if (args is null || args.Length==0) return t.CreateInstance();
            try {
                return Activator.CreateInstance(t, args);
            } catch (AmbiguousMatchException) {
                return t.GetAllConstructors().Where(ci => {
                    //todo test and check for constructors with default values too.
                    var p = ci.GetParameters();
                    if (p.Length != args.Length)
                        return false;

                    //all args are null
                    if (args.All(arg => arg is null))
                        return true;

                    //smart match
                    for (int i = 0; i < p.Length; i++) {
                        var arg = args[i];
                        var param = p[i];
                        if (arg is null)
                            continue;
                        if (arg.GetType() != param.ParameterType)
                            goto _nomatch;
                    }

                    return true;

                    _nomatch:
                    return false;
                }).FirstOrDefault()?.Invoke(args);
            }
        }
    }

    public class ReflectiveException : Exception {
        public ReflectiveException() { }
        public ReflectiveException(string message) : base(message) { }
        public ReflectiveException(string message, Exception inner) : base(message, inner) { }
    }
}