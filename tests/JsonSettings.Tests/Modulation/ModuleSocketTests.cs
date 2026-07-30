using System;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Modulation;

namespace Nucs.JsonSettings.Tests.Modulation {
    /// <summary>
    ///     Unit tests for <see cref="ModuleSocket"/> and the <see cref="Module"/> attach/deattach
    ///     lifecycle. The socket's query surface (GetModule/GetModules/IsAttached*/Attach&lt;T&gt;) and
    ///     the disposed-socket and double-attach guards were only partially exercised by the higher-level
    ///     round-trip tests.
    /// </summary>
    [TestClass]
    public class ModuleSocketTests {
        private static PlainSettings NewSettings() => new PlainSettings();

        [TestMethod]
        public void GetModule_Found_ReturnsIt_Missing_Throws() {
            var s = NewSettings();
            var mod = new Base64Module();
            s.Modulation.Attach(mod);

            s.Modulation.GetModule<Base64Module>().Should().BeSameAs(mod);

            new Action(() => s.Modulation.GetModule<EncryptionModule>())
                .Should().Throw<ModularityException>().WithMessage("*was not found*");
        }

        [TestMethod]
        public void GetModules_ReturnsAllOfType() {
            var s = NewSettings();
            var a = new Base64Module();
            var b = new Base64Module();
            s.Modulation.Attach(a);
            s.Modulation.Attach(b);

            System.Linq.Enumerable.ToArray(s.Modulation.GetModules<Base64Module>())
                .Should().HaveCount(2).And.Contain(new[] { a, b });
        }

        [TestMethod]
        public void IsAttached_ByPredicate() {
            var s = NewSettings();
            s.Modulation.IsAttached(m => m is Base64Module).Should().BeFalse();

            s.Modulation.Attach(new Base64Module());
            s.Modulation.IsAttached(m => m is Base64Module).Should().BeTrue();
        }

        [TestMethod]
        public void IsAttachedOfType_GenericAndType() {
            var s = NewSettings();
            s.Modulation.IsAttachedOfType<Base64Module>().Should().BeFalse();
            s.Modulation.IsAttachedOfType(typeof(Base64Module)).Should().BeFalse();

            s.Modulation.Attach(new Base64Module());

            s.Modulation.IsAttachedOfType<Base64Module>().Should().BeTrue();
            s.Modulation.IsAttachedOfType(typeof(Base64Module)).Should().BeTrue();
            s.Modulation.IsAttachedOfType<EncryptionModule>().Should().BeFalse();
            s.Modulation.IsAttachedOfType(typeof(EncryptionModule)).Should().BeFalse();
        }

        [TestMethod]
        public void AttachGeneric_ConstructsAndAttaches() {
            var s = NewSettings();
            var mod = s.Modulation.Attach<Base64Module>();
            mod.Should().NotBeNull();
            s.Modulation.Modules.Should().Contain(mod);
        }

        [TestMethod]
        public void Attach_AfterDispose_Throws() {
            var s = NewSettings();
            s.Modulation.Dispose();

            new Action(() => s.Modulation.Attach(new Base64Module()))
                .Should().Throw<ObjectDisposedException>();
        }

        [TestMethod]
        public void Dispose_IsIdempotent() {
            var s = NewSettings();
            s.Modulation.Attach(new Base64Module());

            //A second dispose is a documented no-op, not a re-run of teardown.
            s.Modulation.Dispose();
            new Action(() => s.Modulation.Dispose()).Should().NotThrow();
        }

        // ---- Module base lifecycle ------------------------------------------------------------

        [TestMethod]
        public void Module_Attach_NullSocket_Throws() {
            new Action(() => new Base64Module().Attach(null!))
                .Should().Throw<ArgumentNullException>();
        }

        [TestMethod]
        public void Module_AttachedTwice_Throws() {
            var s1 = NewSettings();
            var s2 = NewSettings();
            var mod = new Base64Module();
            s1.Modulation.Attach(mod);

            //A module instance belongs to a single socket; attaching it elsewhere is a modularity error.
            new Action(() => s2.Modulation.Attach(mod))
                .Should().Throw<ModularityException>().WithMessage("*already attached*");
        }

        [TestMethod]
        public void Module_Deattach_WhenNeverAttached_Throws() {
            var s = NewSettings();
            new Action(() => new Base64Module().Deattach(s))
                .Should().Throw<ModularityException>().WithMessage("*not attached*");
        }

        [TestMethod]
        public void Base64Module_Deattach_RemovesFromSocket() {
            var s = NewSettings();
            var mod = new Base64Module();
            s.Modulation.Attach(mod);
            s.Modulation.IsAttachedOfType<Base64Module>().Should().BeTrue();

            s.Modulation.Deattach(mod);
            s.Modulation.IsAttachedOfType<Base64Module>().Should().BeFalse();
        }

        internal class PlainSettings : JsonSettings {
            public override string FileName { get; set; }
            public string Value { get; set; }
            public PlainSettings() { }
            public PlainSettings(string fileName) : base(fileName) { }
        }
    }
}
