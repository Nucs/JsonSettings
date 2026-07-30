using System.ComponentModel;
using System.Collections.Generic;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Autosave {
    /// <summary>
    ///     The mixin notification layer: <c>[NotifyChangesMixin]</c> injects
    ///     <see cref="INotifyPropertyChanged"/> into a class that declares no notification base and
    ///     raises it from the woven setters.
    /// </summary>
    [TestClass]
    public class NotifyChangesMixinTests {
        private static List<string> Record(object source) {
            var names = new List<string>();
            ((INotifyPropertyChanged) source).PropertyChanged += (_, e) => names.Add(e.PropertyName);
            return names;
        }

        [TestMethod]
        public void MixinClass_ImplementsINotifyPropertyChanged() {
            using var f = new TempFile();
            var o = JsonSettings.Load<MixinSettings>(f.FileName);

            o.Should().BeAssignableTo<INotifyPropertyChanged>(
                "the aspect mixes the interface into a class that never declared it");
        }

        [TestMethod]
        public void Mixin_RaisesPropertyChanged_OnWovenSetter() {
            using var f = new TempFile();
            var o = JsonSettings.Load<MixinSettings>(f.FileName);
            var raised = Record(o);

            o.Name = "a";
            raised.Should().ContainSingle().Which.Should().Be("Name");

            o.Name = "a"; //OnlyChanged default
            raised.Should().ContainSingle("a no-op write is suppressed by the default guard");
        }

        [TestMethod]
        public void Mixin_IsPerInstance_EachObjectHasItsOwnSubscribers() {
            using var fa = new TempFile();
            using var fb = new TempFile();
            var a = JsonSettings.Load<MixinSettings>(fa.FileName);
            var b = JsonSettings.Load<MixinSettings>(fb.FileName);
            var raisedOnA = Record(a);

            b.Name = "changed-on-b";
            raisedOnA.Should().BeEmpty("Scope.PerInstance must give each instance its own event, not a shared singleton");

            a.Name = "changed-on-a";
            raisedOnA.Should().ContainSingle().Which.Should().Be("Name");
        }

        [TestMethod]
        public void Mixin_ComposesWithAutosave() {
            using var f = new TempFile();
            var o = JsonSettings.Load<MixinSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;
            var raised = Record(o);

            o.Number = 42;

            saves.Should().Be(1);
            raised.Should().ContainSingle().Which.Should().Be("Number");
            JsonSettings.Load<MixinSettings>(f.FileName).Number.Should().Be(42);
        }

        [TestMethod]
        public void Mixin_SealedClass_IsSupported() {
            using var f = new TempFile();
            var o = JsonSettings.Load<SealedMixinSettings>(f.FileName);
            var raised = Record(o);

            o.Value = "sealed";
            raised.Should().ContainSingle().Which.Should().Be("Value");
        }

        #region settings types

        [Autosave]
        [NotifyChangesMixin]
        public class MixinSettings : JsonSettings {
            public override string FileName { get; set; } = "mixin.jsn";
            public string Name { get; set; }
            public int Number { get; set; }

            public MixinSettings() { }
            public MixinSettings(string fileName) : base(fileName) { }
        }

        [NotifyChangesMixin]
        public sealed class SealedMixinSettings : JsonSettings {
            public override string FileName { get; set; } = "sealedmixin.jsn";
            public string Value { get; set; }

            public SealedMixinSettings() { }
            public SealedMixinSettings(string fileName) : base(fileName) { }
        }

        #endregion
    }
}
