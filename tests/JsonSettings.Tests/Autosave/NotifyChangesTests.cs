using System.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Examples;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Autosave {
    /// <summary>
    ///     The advice-only notification layer: <c>[NotifyChanges]</c> raises <c>PropertyChanged</c>
    ///     from woven setters for a class that already owns the event (a
    ///     <see cref="NotifiyingJsonSettings"/> base, or an ecosystem raiser convention).
    /// </summary>
    [TestClass]
    public class NotifyChangesTests {
        private static List<string> Record(INotifyPropertyChanged source) {
            var names = new List<string>();
            source.PropertyChanged += (_, e) => names.Add(e.PropertyName);
            return names;
        }

        [TestMethod]
        public void AutoProperty_RaisesPropertyChanged_WithPropertyName() {
            using var f = new TempFile();
            var o = JsonSettings.Load<NotifyingSettings>(f.FileName);
            var raised = Record(o);

            o.Name = "a";

            raised.Should().ContainSingle().Which.Should().Be("Name",
                "a woven auto-property setter raises PropertyChanged with the property name, no hand-written OnPropertyChanged");
        }

        [TestMethod]
        public void OnlyChanged_IsTheDefault_AndSuppressesNoOpWrites() {
            using var f = new TempFile();
            var o = JsonSettings.Load<NotifyingSettings>(f.FileName);
            o.Name = "a";
            var raised = Record(o);

            o.Name = "a"; //same value
            raised.Should().BeEmpty("the default OnlyChanged guard suppresses a write that does not change the value");

            o.Name = "b";
            raised.Should().ContainSingle().Which.Should().Be("Name");
        }

        [TestMethod]
        public void OnlyChanged_ValueType_FiresWhenChangedIncludingBackToDefault() {
            using var f = new TempFile();
            var o = JsonSettings.Load<NotifyingSettings>(f.FileName);
            o.Number = 5;
            var raised = Record(o);

            o.Number = 5;
            raised.Should().BeEmpty("no change");

            o.Number = 0; //back to default is still a change under OnlyChanged (no SkipNullOrDefault)
            raised.Should().ContainSingle().Which.Should().Be("Number");
        }

        [TestMethod]
        public void Always_RaisesEvenWhenValueIsUnchanged() {
            using var f = new TempFile();
            var o = JsonSettings.Load<NotifyingSettings>(f.FileName);
            var raised = Record(o);

            o.Ticks = "x";
            o.Ticks = "x";
            o.Ticks = "x";

            raised.Should().HaveCount(3, "the per-property Always guard fires on every setter access");
        }

        [TestMethod]
        public void SkipNullOrDefault_SuppressesClearingToNull() {
            using var f = new TempFile();
            var o = JsonSettings.Load<NotifyingSettings>(f.FileName);
            var raised = Record(o);

            o.Optional = "value";   //null -> value: a real, non-default change
            o.Optional = null;      //value -> null: suppressed by SkipNullOrDefault
            o.Optional = "again";   //null -> value: fires

            raised.Should().Equal(new[] { "Optional", "Optional" },
                "SkipNullOrDefault drops the write that sets the property back to null");
        }

        [TestMethod]
        public void Notify_ComposesWithAutosave_OneWriteSavesAndNotifiesOnce() {
            using var f = new TempFile();
            var o = JsonSettings.Load<NotifyingSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;
            var raised = Record(o);

            o.Name = "combined";

            saves.Should().Be(1, "[Autosave] and [NotifyChanges] weave the same setter and each fire once");
            raised.Should().ContainSingle().Which.Should().Be("Name");
            JsonSettings.Load<NotifyingSettings>(f.FileName).Name.Should().Be("combined");
        }

        [TestMethod]
        public void PerPropertyOverride_DoesNotDoubleWeaveTheClassDefault() {
            using var f = new TempFile();
            var o = JsonSettings.Load<NotifyingSettings>(f.FileName);
            var raised = Record(o);

            //Ticks carries its own [NotifyChanges(Always)] while the class carries [NotifyChanges].
            //If both triggers wove the advice, this single write would raise twice.
            o.Ticks = "once";

            raised.Should().ContainSingle("class-level and property-level triggers must not stack the advice on one setter");
        }

        [TestMethod]
        public void EcosystemConvention_RaiserMethodIsUsed_WithoutTheNotifyingBase() {
            using var f = new TempFile();
            var o = JsonSettings.Load<ConventionSettings>(f.FileName);
            var raised = Record(o);

            o.Title = "hello";

            raised.Should().ContainSingle().Which.Should().Be("Title",
                "a class exposing a conventional RaisePropertyChanged(string) is driven through it");
        }

        [TestMethod]
        public void NoRaiserAndNoInterface_IsAHarmlessNoOp() {
            using var f = new TempFile();
            var o = JsonSettings.Load<NoRaiserSettings>(f.FileName);

            //no INotifyPropertyChanged, no raiser convention: the advice runs and finds nothing to
            //call. It must not throw -- the write simply produces no notification.
            var act = () => o.Value = "written";
            act.Should().NotThrow();
            o.Value.Should().Be("written");
        }

        [TestMethod]
        public void IgnoreNotify_SuppressesNotification_ButStillAutosaves() {
            using var f = new TempFile();
            var o = JsonSettings.Load<NotifyingSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;
            var raised = Record(o);

            o.Silent = "value";

            raised.Should().BeEmpty("[IgnoreNotify] silences the notification");
            saves.Should().Be(1, "but the property still autosaves — notification and persistence opt-outs are independent");
        }

        [TestMethod]
        public void IgnoreAutosave_DoesNotSuppressNotification() {
            using var f = new TempFile();
            var o = JsonSettings.Load<NotifyingSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;
            var raised = Record(o);

            o.ViewOnly = "value";

            raised.Should().Equal(new[] { "ViewOnly" }, "[IgnoreAutosave] governs saving, not notifying");
            saves.Should().Be(0, "and the property does not autosave");
        }

        [TestMethod]
        public void FrameworkFileName_IsNeverNotified() {
            using var f = new TempFile();
            var o = JsonSettings.Load<NotifyingSettings>(f.FileName);
            var raised = Record(o);

            o.FileName = "renamed.jsn";

            raised.Should().BeEmpty("FileName is framework-managed and excluded from notifications");
        }

        [TestMethod]
        public void Save_DoesNotSpuriouslyNotifyFileName() {
            using var f = new TempFile();
            var o = JsonSettings.Load<NotifyingSettings>(f.FileName).EnableAutosave();
            var raised = Record(o);

            //Save() assigns o.FileName internally; without the framework exclusion this write would
            //leak a PropertyChanged("FileName") on every autosave.
            o.Name = "x";

            raised.Should().Equal(new[] { "Name" }, "the only notification is the user's write, not Save()'s internal FileName assignment");
        }

        [TestMethod]
        public void Notify_And_Autosave_CollectionReassignThenMutate_NoDoubleSave() {
            using var f = new TempFile();
            var o = JsonSettings.Load<CollectionNotifyingSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;
            var raised = Record(o);

            o.Items = new ObservableCollection<string>();
            saves.Should().Be(1, "reassigning the collection saves once via the woven setter");
            raised.Should().Equal(new[] { "Items" }, "and notifies once; the binder rebinds without a second save");

            o.Items.Add("a");
            saves.Should().Be(2, "mutating the rebound collection saves once via CollectionChanged");
            raised.Should().Equal(new[] { "Items" }, "an in-place Add is not a setter, so it raises no PropertyChanged on the settings");
        }

        #region settings types

        [Autosave]
        [NotifyChanges]
        public class NotifyingSettings : NotifiyingJsonSettings {
            public override string FileName { get; set; } = "notify.jsn";

            public string Name { get; set; }
            public int Number { get; set; }

            [NotifyChanges(Guard = NotificationGuard.Always)]
            public string Ticks { get; set; }

            [NotifyChanges(Guard = NotificationGuard.OnlyChanged | NotificationGuard.SkipNullOrDefault)]
            public string Optional { get; set; }

            [IgnoreNotify]
            public string Silent { get; set; }         // saves (class [Autosave]) but never notifies

            [IgnoreAutosave]
            public string ViewOnly { get; set; }       // notifies (class [NotifyChanges]) but never saves

            public NotifyingSettings() { }
            public NotifyingSettings(string fileName) : base(fileName) { }
        }

        /// <summary>
        ///     Implements <see cref="INotifyPropertyChanged"/> by hand with a conventional raiser,
        ///     the shape CommunityToolkit.Mvvm / Prism / Caliburn bases take -- no JsonSettings
        ///     notifying base involved.
        /// </summary>
        [NotifyChanges]
        public class ConventionSettings : JsonSettings, INotifyPropertyChanged {
            public override string FileName { get; set; } = "convention.jsn";
            public string Title { get; set; }

            public event PropertyChangedEventHandler PropertyChanged;

            //deliberately named like Prism's BindableBase; the runtime resolves it by convention.
            protected void RaisePropertyChanged(string propertyName) {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

            public ConventionSettings() { }
            public ConventionSettings(string fileName) : base(fileName) { }
        }

        [NotifyChanges]
        public class NoRaiserSettings : JsonSettings {
            public override string FileName { get; set; } = "noraiser.jsn";
            public string Value { get; set; }

            public NoRaiserSettings() { }
            public NoRaiserSettings(string fileName) : base(fileName) { }
        }

        [Autosave]
        [NotifyChanges]
        public class CollectionNotifyingSettings : NotifiyingJsonSettings {
            public override string FileName { get; set; } = "collnotify.jsn";
            public ObservableCollection<string> Items { get; set; } = new ObservableCollection<string>();

            public CollectionNotifyingSettings() { }
            public CollectionNotifyingSettings(string fileName) : base(fileName) { }
        }

        #endregion
    }
}
