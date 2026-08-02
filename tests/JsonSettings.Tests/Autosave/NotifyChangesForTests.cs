using System.Collections.Generic;
using System.ComponentModel;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Nucs.JsonSettings.NotifyChanges;
using Nucs.JsonSettings.Examples;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Autosave {
    /// <summary>
    ///     Dependent-property notification: <c>[NotifyChangesFor]</c> on a source property fans the
    ///     change notification out to the named computed properties, so their bindings refresh too.
    /// </summary>
    [TestClass]
    public class NotifyChangesForTests {
        private static List<string> Record(INotifyPropertyChanged source) {
            var names = new List<string>();
            source.PropertyChanged += (_, e) => names.Add(e.PropertyName);
            return names;
        }

        [TestMethod]
        public void ChangingSource_AlsoRaisesDependent_AfterIt() {
            using var f = new TempFile();
            var o = JsonSettings.Load<DependentSettings>(f.FileName);
            var raised = Record(o);

            o.First = "Grover";

            raised.Should().Equal(new[] { "First", "FullName" },
                "the source raises first, then each [NotifyChangesFor] dependent in declared order");
        }

        [TestMethod]
        public void MultipleTargets_AllRaised_InOrder() {
            using var f = new TempFile();
            var o = JsonSettings.Load<DependentSettings>(f.FileName);
            var raised = Record(o);

            o.Source = "x";

            raised.Should().Equal(new[] { "Source", "Alpha", "Beta" });
        }

        [TestMethod]
        public void NoOpWrite_UnderOnlyChanged_RaisesNoDependents() {
            using var f = new TempFile();
            var o = JsonSettings.Load<DependentSettings>(f.FileName);
            o.First = "Bert";
            var raised = Record(o);

            o.First = "Bert"; //same value: the source guard suppresses, so nothing fans out

            raised.Should().BeEmpty("dependents are gated by the source's change guard");
        }

        [TestMethod]
        public void SelfReference_And_IgnoreNotifyTarget_AreSkipped() {
            using var f = new TempFile();
            var o = JsonSettings.Load<DependentSettings>(f.FileName);
            var raised = Record(o);

            o.SelfRef = "y";

            raised.Should().Equal(new[] { "SelfRef" },
                "a property naming itself is dropped, and an [IgnoreNotify] target is not resurrected");
        }

        [TestMethod]
        public void StackedAttributes_MergeAndDedupe() {
            using var f = new TempFile();
            var o = JsonSettings.Load<DependentSettings>(f.FileName);
            var raised = Record(o);

            o.Multi = "z";

            raised.Should().Equal(new[] { "Multi", "Alpha", "Beta" },
                "several [NotifyChangesFor] on one property merge their targets and de-duplicate");
        }

        [TestMethod]
        public void Mixin_Dependent_IsRaisedOnInjectedEvent() {
            using var f = new TempFile();
            var o = JsonSettings.Load<MixinDependentSettings>(f.FileName);
            var raised = Record((INotifyPropertyChanged) (object) o);

            o.Name = "Elmo";

            raised.Should().Equal(new[] { "Name", "DisplayName" },
                "dependents fan out through the mixin's injected PropertyChanged too");
        }

        #region settings types

        [NotifyChanges]
        public class DependentSettings : NotifiyingJsonSettings {
            public override string FileName { get; set; } = "dependent.jsn";

            [NotifyChangesFor(nameof(FullName))]
            public string First { get; set; }

            [NotifyChangesFor(nameof(FullName))]
            public string Last { get; set; }

            [JsonIgnore]
            public string FullName => $"{First} {Last}";       // computed, get-only: no setter to weave

            [NotifyChangesFor(nameof(Alpha), nameof(Beta))]
            public string Source { get; set; }

            //two attributes stacked, with an overlap (Alpha) to prove dedupe
            [NotifyChangesFor(nameof(Alpha))]
            [NotifyChangesFor(nameof(Beta), nameof(Alpha))]
            public string Multi { get; set; }

            [JsonIgnore] public string Alpha => Source + "A";
            [JsonIgnore] public string Beta => Source + "B";

            //names itself (dropped) and an [IgnoreNotify] target (not resurrected)
            [NotifyChangesFor(nameof(SelfRef), nameof(Silent))]
            public string SelfRef { get; set; }

            [IgnoreNotify]
            public string Silent { get; set; }

            public DependentSettings() { }
            public DependentSettings(string fileName) : base(fileName) { }
        }

        [NotifyChangesMixin]
        public sealed class MixinDependentSettings : JsonSettings {
            public override string FileName { get; set; } = "mixindep.jsn";

            [NotifyChangesFor(nameof(DisplayName))]
            public string Name { get; set; }

            [JsonIgnore] public string DisplayName => "Mr. " + Name;

            public MixinDependentSettings() { }
            public MixinDependentSettings(string fileName) : base(fileName) { }
        }

        #endregion
    }
}
