using System.Collections.Generic;
using System.ComponentModel;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.NotifyChanges;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Autosave {
    /// <summary>
    ///     The INotifyPropertyChanging side of the aspect: <c>[NotifyChanges]</c> raises
    ///     <c>PropertyChanging</c> before the assignment (through the notifying base, or a convention
    ///     raiser), and <c>[NotifyChangesMixin]</c> injects and raises it too.
    /// </summary>
    [TestClass]
    public class NotifyChangingTests {
        /// <summary>Records both edges as "changing:Name" / "changed:Name" in the order they fire.</summary>
        private static List<string> RecordBoth(object source) {
            var log = new List<string>();
            ((INotifyPropertyChanging) source).PropertyChanging += (_, e) => log.Add("changing:" + e.PropertyName);
            ((INotifyPropertyChanged) source).PropertyChanged += (_, e) => log.Add("changed:" + e.PropertyName);
            return log;
        }

        [TestMethod]
        public void NotifiyingBase_RaisesChanging_ThenChanged() {
            using var f = new TempFile();
            var o = JsonSettings.Load<ChangingSettings>(f.FileName);
            var log = RecordBoth(o);

            o.Name = "a";

            log.Should().Equal(new[] { "changing:Name", "changed:Name" });
        }

        [TestMethod]
        public void Changing_FiresBeforeAssignment_Changed_After() {
            using var f = new TempFile();
            var o = JsonSettings.Load<ChangingSettings>(f.FileName);
            o.Name = "old";

            var observed = new List<string>();
            ((INotifyPropertyChanging) o).PropertyChanging += (_, _) => observed.Add("changing sees: " + o.Name);
            o.PropertyChanged += (_, _) => observed.Add("changed sees: " + o.Name);

            o.Name = "new";

            observed.Should().Equal(new[] { "changing sees: old", "changed sees: new" },
                "PropertyChanging must fire while the property still holds the old value");
        }

        [TestMethod]
        public void OnlyChanged_NoOpWrite_RaisesNeitherEdge() {
            using var f = new TempFile();
            var o = JsonSettings.Load<ChangingSettings>(f.FileName);
            o.Name = "a";
            var log = RecordBoth(o);

            o.Name = "a"; //same value

            log.Should().BeEmpty("the change guard suppresses both the changing and the changed notification");
        }

        [TestMethod]
        public void ConventionRaiser_RaisePropertyChanging_IsUsed() {
            using var f = new TempFile();
            var o = JsonSettings.Load<ChangingConventionSettings>(f.FileName);
            var log = RecordBoth(o);

            o.Title = "hello";

            log.Should().Equal(new[] { "changing:Title", "changed:Title" },
                "a class exposing a conventional RaisePropertyChanging(string) is driven through it");
        }

        [TestMethod]
        public void Mixin_ImplementsAndRaises_INotifyPropertyChanging() {
            using var f = new TempFile();
            var o = JsonSettings.Load<ChangingMixinSettings>(f.FileName);

            o.Should().BeAssignableTo<INotifyPropertyChanging>("the mixin injects INotifyPropertyChanging alongside INotifyPropertyChanged");

            var log = RecordBoth(o);
            o.Value = "x";

            log.Should().Equal(new[] { "changing:Value", "changed:Value" });
        }

        #region settings types

        [NotifyChanges]
        public class ChangingSettings : NotifiyingJsonSettings {
            public override string FileName { get; set; } = "changing.jsn";
            public string Name { get; set; }

            public ChangingSettings() { }
            public ChangingSettings(string fileName) : base(fileName) { }
        }

        /// <summary>Implements both interfaces by hand with Prism-style convention raisers, no base.</summary>
        [NotifyChanges]
        public class ChangingConventionSettings : JsonSettings, INotifyPropertyChanged, INotifyPropertyChanging {
            public override string FileName { get; set; } = "changingconv.jsn";
            public string Title { get; set; }

            public event PropertyChangedEventHandler PropertyChanged;
            public event PropertyChangingEventHandler PropertyChanging;

            protected void RaisePropertyChanged(string propertyName) {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

            protected void RaisePropertyChanging(string propertyName) {
                PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
            }

            public ChangingConventionSettings() { }
            public ChangingConventionSettings(string fileName) : base(fileName) { }
        }

        [NotifyChangesMixin]
        public sealed class ChangingMixinSettings : JsonSettings {
            public override string FileName { get; set; } = "changingmixin.jsn";
            public string Value { get; set; }

            public ChangingMixinSettings() { }
            public ChangingMixinSettings(string fileName) : base(fileName) { }
        }

        #endregion
    }
}
