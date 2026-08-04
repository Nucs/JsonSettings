using System;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nucs.JsonSettings.Tests.Inline {
    /// <summary>
    ///     Covers the <see cref="Activation.CreateInstance(Type,object[])"/> disambiguation fallback: when
    ///     <see cref="Activator.CreateInstance(Type,object[])"/> raises <see cref="System.Reflection.AmbiguousMatchException"/>,
    ///     the helper scans constructors and picks the one whose parameter types match the supplied
    ///     arguments exactly (treating a null argument as a wildcard). The existing UtilsTests only reach
    ///     the all-null-arguments shortcut; the per-argument type-matching loop and its no-match outcome
    ///     were uncovered.
    /// </summary>
    [TestClass]
    public class ActivationSmartMatchTests {
        [TestMethod]
        public void AmbiguousConstructor_ExactTypeMatchWithNullWildcard_IsPicked() {
            //Both constructors are applicable for {string, null}, so Activator is ambiguous. The smart
            //match then prefers the (string,string) constructor: the first argument's type matches
            //exactly and the null second argument is treated as a wildcard.
            var result = Activation.CreateInstance(typeof(MixedAmbiguous), new object[] { "x", null });
            result.Should().BeOfType<MixedAmbiguous>();
            ((MixedAmbiguous) result).Picked.Should().Be("string,string");
        }

        [TestMethod]
        public void AmbiguousConstructor_NoExactMatch_ReturnsNull() {
            //Both constructors take interface parameters, so a concrete string argument matches neither
            //by exact type. Activator is ambiguous (string satisfies both interfaces), and the smart
            //match finds no exact-type constructor -> the documented fallback yields null.
            var result = Activation.CreateInstance(typeof(InterfaceAmbiguous), new object[] { "x", "y" });
            result.Should().BeNull();
        }

        [TestMethod]
        public void AmbiguousConstructor_AllNullArguments_MatchesFirstOfRightArity() {
            //Both constructors are applicable for {null, null}, so Activator is ambiguous. With every
            //argument null there is no type information to discriminate on, so the smart match's all-null
            //shortcut accepts the first constructor of the right arity -- a null wildcard, not a failure.
            var result = Activation.CreateInstance(typeof(InterfaceAmbiguous), new object[] { null, null });
            result.Should().BeOfType<InterfaceAmbiguous>();
        }

        public class MixedAmbiguous {
            public string Picked;
            public MixedAmbiguous(string a, string b) { Picked = "string,string"; }
            public MixedAmbiguous(object a, object b) { Picked = "object,object"; }
        }

        public class InterfaceAmbiguous {
            public InterfaceAmbiguous(object a, IComparable b) { }
            public InterfaceAmbiguous(IComparable a, object b) { }
        }
    }
}
