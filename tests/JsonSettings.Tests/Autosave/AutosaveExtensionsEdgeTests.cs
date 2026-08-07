using System;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Autosave;

namespace Nucs.JsonSettings.Tests.Autosave {
    /// <summary>
    ///     Guard-clause coverage for the autosave extension entry points and the
    ///     <see cref="NotifiyingJsonSettings"/> filename constructor, neither of which the behavioural
    ///     suites reach: <c>EnableAutosave(null)</c>, <c>EnableIAutosave(null)</c>, <c>EnableIAutosave</c>
    ///     with a non-interface target type, and constructing a notifying settings object with a filename.
    /// </summary>
    [TestClass]
    public class AutosaveExtensionsEdgeTests {
        [TestMethod]
        public void EnableAutosave_NullSettings_Throws() {
            new Action(() => ((AutosaveFoo) null!).EnableAutosave())
                .Should().Throw<ArgumentNullException>();
        }

        [TestMethod]
        public void EnableIAutosave_NullSettings_Throws() {
            new Action(() => ((AutosaveFoo) null!).EnableIAutosave<AutosaveFoo, IAutosaveFoo>())
                .Should().Throw<ArgumentNullException>();
        }

        [TestMethod]
        public void EnableIAutosave_NonInterfaceTargetType_Throws() {
            var foo = JsonSettings.Construct<AutosaveFoo>();
            //ISettings is a concrete class here, not an interface -> rejected before any weaving check.
            new Action(() => foo.EnableIAutosave<AutosaveFoo, AutosaveFoo>())
                .Should().Throw<ArgumentException>().WithMessage("*interface*");
        }

        [TestMethod]
        public void NotifiyingJsonSettings_FileNameConstructor_SetsFileName() {
            //Exercises the NotifiyingJsonSettings(string) constructor, which Load never uses (it
            //constructs parameterless and assigns FileName afterwards).
            var n = new NotifyingFoo("explicit.jsn");
            n.FileName.Should().Be("explicit.jsn");
        }

        public interface IAutosaveFoo {
            string Value { get; set; }
        }

        [Autosave]
        public class AutosaveFoo : JsonSettings, IAutosaveFoo {
            public override string FileName { get; set; } = "af.jsn";
            public string Value { get; set; }
            public AutosaveFoo() { }
            public AutosaveFoo(string fileName) : base(fileName) { }
        }

        public class NotifyingFoo : NotifiyingJsonSettings {
            public override string FileName { get; set; } = "nf.jsn";
            public NotifyingFoo() { }
            public NotifyingFoo(string fileName) : base(fileName) { }
        }
    }
}
