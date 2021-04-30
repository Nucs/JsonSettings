using System;
using System.Reflection;

namespace Microsoft.Xna.Framework.Utilities {
    internal static partial class ReflectionHelpers {
        /// <summary>
        /// Returns true if the given type represents a non-object type that is not abstract.
        /// </summary>
        public static bool IsConcreteClass(Type t) {
            if (t == null) {
                throw new NullReferenceException("Must supply the t (type) parameter");
            }

            if (t == typeof(object))
                return false;
            if (t.IsClass && !t.IsAbstract)
                return true;
            return false;
        }
    }
}