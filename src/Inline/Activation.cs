using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.Xna.Framework.Utilities;

namespace nucs.JsonSettings {
    public static class Activation {
        /// <summary>
        ///     Does the type have public/private/protected/internal.
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public static bool HasDefaultConstructor(this Type t) {
            var ctrs = t.GetConstructors().Concat(t.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)).Where(c=>c.GetParameters().Length == 0 || c.GetParameters().All(p => p.IsOptional)).ToArray();
            return ReflectionHelpers.IsValueType(t) || (ctrs.Any(c => c.GetParameters().Length == 0 || c.GetParameters().All(p => p.IsOptional)));
        }

        public static object CreateInstance(this Type t) {
            var ctrs = t.GetConstructors().Concat(t.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)).Where(c=>c.GetParameters().Length == 0 || c.GetParameters().All(p => p.IsOptional)).ToArray();
            if (ReflectionHelpers.IsValueType(t) || ctrs.Any(c => c.IsPublic)) //is valuetype or has public constractor.
                return Activator.CreateInstance(t);
            var prv = ctrs.FirstOrDefault(c => c.IsAssembly ||c.IsFamily || c.IsPrivate); //check protected/internal/private constructor
            if (prv == null)
                throw new ReflectiveException($"Type {t.FullName} does not have empty constructor (public or private)");
#if NETSTANDARD1_6
            return prv.Invoke(null);
#else
            return prv.Invoke(null);
#endif
        }
    }

    public class ReflectiveException : Exception {
        public ReflectiveException() { }
        public ReflectiveException(string message) : base(message) { }
        public ReflectiveException(string message, Exception inner) : base(message, inner) { }
    }
}