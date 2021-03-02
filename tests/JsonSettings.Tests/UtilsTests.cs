using System;
using System.IO;
using System.Reflection;
using System.Security;
using FluentAssertions;
using JsonSettings.xTests.Utils;
using NUnit.Framework;


namespace JsonSettings.xTests {
    [TestFixture]
    public class UtilsTests {
        [Test]
        public void Activation_PrivateConst() {
            var a = Activation.CreateInstance(typeof(PrivateConst));
            a.Should().BeOfType<PrivateConst>();
        }

        [Test]
        public void Activation_ProtectedConst() {
            var a = Activation.CreateInstance(typeof(ProtectedConst));
            a.Should().BeOfType<ProtectedConst>();
        }

        [Test]
        public void Activation_PublicConst() {
            var a = Activation.CreateInstance(typeof(PublicConst));
            a.Should().BeOfType<PublicConst>();
        }

        [Test]
        public void Activation_InternalConst() {
            var a = Activation.CreateInstance(typeof(InternalConst));
            a.Should().BeOfType<InternalConst>();
        }

        [Test]
        public void Activation_NoEmptyConst() {
            Action a = () => Activation.CreateInstance(typeof(NoEmptyConst));
            a.ShouldThrow<ReflectiveException>();
        }

        [Test]
        public void Activation_FallbackWhenNullArgs() {
            var a = Activation.CreateInstance(typeof(PublicConst), null);
            a.Should().BeOfType<PublicConst>();
        }

        [Test]
        public void Activation_ObjectConstructor() {
            Func<object> a = () => Activation.CreateInstance(typeof(AmbiousClass), new object[] {null});
            a().Should().BeOfType<AmbiousClass>();
        }

        [Test]
        public void Activation_AmbiousDefaultClass() {
            Func<object> a = () => Activation.CreateInstance(typeof(AmbiousDefaultClass), new object[] {null});
            a().Should().BeOfType<AmbiousDefaultClass>();
        }

        [Test]
        public void Activation_AmbiousDefaultWithSameClass() {
            Func<object> a = () => Activation.CreateInstance(typeof(AmbiousDefaultWithSameClass), new object[] {null, null});
            a().Should().BeOfType<AmbiousDefaultWithSameClass>();
        }

        [Test]
        public void Activation_NoMatchClass() {
            Action a = () => Activation.CreateInstance(typeof(NoMatchClass), new object[] {null});
            a.ShouldThrow<MissingMethodException>();
        }

        class AmbiousClass {
            public AmbiousClass(string s) { }
            public AmbiousClass(object s) { }
        }

        class AmbiousDefaultClass {
            public AmbiousDefaultClass(string s, string a = null) { }
            public AmbiousDefaultClass(string s) { }
            public AmbiousDefaultClass(object s) { }
        }

        class AmbiousDefaultWithSameClass {
            public AmbiousDefaultWithSameClass(string s, string a) { }
            public AmbiousDefaultWithSameClass(string s, string a, string n = null) { }
            public AmbiousDefaultWithSameClass(string s, object n) { }
        }

        class NoMatchClass {
            public NoMatchClass(string s, string a) { }
        }

        class PrivateConst {
            private PrivateConst() { }
        }

        class InternalConst {
            internal InternalConst() { }
        }

        class ProtectedConst {
            protected ProtectedConst() { }
        }

        class PublicConst {
            public PublicConst() { }
        }

        class NoEmptyConst {
            public NoEmptyConst(string s) { }
            protected NoEmptyConst(int s) { }
            private NoEmptyConst(int s, string ss) { }
        }
    }
}