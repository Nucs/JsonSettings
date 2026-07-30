using System;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Modulation.Recovery;

namespace Nucs.JsonSettings.Tests {
    /// <summary>
    ///     Constructor coverage for the library's exception types. Only the single-argument message
    ///     constructors were exercised by behavioural tests; the parameterless and message+inner
    ///     constructors (which callers rely on for wrapping and rethrow) were not, and the type
    ///     relationships (e.g. recovery derives from the base exception) were unpinned.
    /// </summary>
    [TestClass]
    public class ExceptionTypesTests {
        private static readonly Exception Inner = new InvalidOperationException("inner");

        [TestMethod]
        public void JsonSettingsException_AllConstructors() {
            new JsonSettingsException().Should().BeAssignableTo<Exception>();
            new JsonSettingsException("msg").Message.Should().Be("msg");

            var withInner = new JsonSettingsException("msg", Inner);
            withInner.Message.Should().Be("msg");
            withInner.InnerException.Should().BeSameAs(Inner);
        }

        [TestMethod]
        public void ModularityException_AllConstructors() {
            new ModularityException().Should().BeAssignableTo<Exception>();
            new ModularityException("m").Message.Should().Be("m");

            var withInner = new ModularityException("m", Inner);
            withInner.InnerException.Should().BeSameAs(Inner);
        }

        [TestMethod]
        public void InvalidVersionException_AllConstructors() {
            new InvalidVersionException().Should().BeAssignableTo<Exception>();
            new InvalidVersionException("v").Message.Should().Be("v");

            var withInner = new InvalidVersionException("v", Inner);
            withInner.InnerException.Should().BeSameAs(Inner);
        }

        [TestMethod]
        public void JsonSettingsRecoveryException_AllConstructors_AndInheritsBase() {
            new JsonSettingsRecoveryException().Should().BeAssignableTo<JsonSettingsException>();
            new JsonSettingsRecoveryException("r").Message.Should().Be("r");

            var withInner = new JsonSettingsRecoveryException("r", Inner);
            withInner.InnerException.Should().BeSameAs(Inner);
            //A recovery failure must be catchable as the base library exception.
            withInner.Should().BeAssignableTo<JsonSettingsException>();
        }

        [TestMethod]
        public void ReflectiveException_AllConstructors() {
            new ReflectiveException().Should().BeAssignableTo<Exception>();
            new ReflectiveException("x").Message.Should().Be("x");

            var withInner = new ReflectiveException("x", Inner);
            withInner.InnerException.Should().BeSameAs(Inner);
        }
    }
}
