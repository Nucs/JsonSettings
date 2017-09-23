using System;
using System.IO;
using System.Security;
using FluentAssertions;
using nucs.JsonSettings.xTests.Utils;
using Xunit;
using Xunit.Sdk;

namespace nucs.JsonSettings.xTests {
    public class UtilsTests {
        [Fact]
        public void Activation_PrivateConst() {
            var a = Activation.CreateInstance(typeof(PrivateConst));
            a.Should().BeOfType<PrivateConst>();
        }
        [Fact]
        public void Activation_ProtectedConst() {
            var a = Activation.CreateInstance(typeof(ProtectedConst));
            a.Should().BeOfType<ProtectedConst>();
        }
        [Fact]
        public void Activation_PublicConst() {
            var a = Activation.CreateInstance(typeof(PublicConst));
            a.Should().BeOfType<PublicConst>();
        }

        [Fact]
        public void Activation_InternalConst() {
            var a = Activation.CreateInstance(typeof(InternalConst));
            a.Should().BeOfType<InternalConst>();
        }

        [Fact]
        public void Activation_NoEmptyConst() {
            Action a = ()=> Activation.CreateInstance(typeof(NoEmptyConst));
            a.ShouldThrow<ReflectiveException>();
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