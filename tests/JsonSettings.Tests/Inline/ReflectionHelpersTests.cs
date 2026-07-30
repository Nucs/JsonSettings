using System;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework.Utilities;

namespace Nucs.JsonSettings.Tests.Inline {
    /// <summary>
    ///     Unit tests for the vendored <see cref="ReflectionHelpers.IsConcreteClass"/> predicate. It is
    ///     reached indirectly by the serializer's type handling, but was never exercised by a test that
    ///     pins each branch: object, concrete class, abstract class, interface, value type and null.
    /// </summary>
    [TestClass]
    public class ReflectionHelpersTests {
        [TestMethod]
        public void IsConcreteClass_Null_Throws() {
            //The method documents this contract explicitly: it raises rather than returning false so a
            //caller cannot silently treat "no type" as "not concrete".
            new Action(() => ReflectionHelpers.IsConcreteClass(null))
                .Should().Throw<NullReferenceException>();
        }

        [TestMethod]
        public void IsConcreteClass_Object_IsFalse() {
            //object is special-cased: it is a concrete class but never a meaningful concrete target.
            ReflectionHelpers.IsConcreteClass(typeof(object)).Should().BeFalse();
        }

        [TestMethod]
        public void IsConcreteClass_ConcreteClass_IsTrue() {
            ReflectionHelpers.IsConcreteClass(typeof(ConcreteSample)).Should().BeTrue();
        }

        [TestMethod]
        public void IsConcreteClass_AbstractClass_IsFalse() {
            ReflectionHelpers.IsConcreteClass(typeof(AbstractSample)).Should().BeFalse();
        }

        [TestMethod]
        public void IsConcreteClass_Interface_IsFalse() {
            //An interface is not IsClass, so it must not count as a concrete class.
            ReflectionHelpers.IsConcreteClass(typeof(ISample)).Should().BeFalse();
        }

        [TestMethod]
        public void IsConcreteClass_ValueType_IsFalse() {
            //A struct is not a class either.
            ReflectionHelpers.IsConcreteClass(typeof(int)).Should().BeFalse();
            ReflectionHelpers.IsConcreteClass(typeof(StructSample)).Should().BeFalse();
        }

        private interface ISample { }
        private abstract class AbstractSample { }
        private sealed class ConcreteSample { }
        private struct StructSample { }
    }
}
