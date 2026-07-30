using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Examples;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Autosave {
    /// <summary>
    ///     A class carrying <em>both</em> <c>[Autosave]</c> and a notification aspect
    ///     (<c>[NotifyChanges]</c> or <c>[NotifyChangesMixin]</c>) on the same setters: it persists and
    ///     it notifies. These tests pin how the two compose -- counts, round-trips, suspension, the
    ///     independent opt-outs, and the one place their change semantics deliberately differ.
    /// </summary>
    [TestClass]
    public class NotifyAutosaveCompositionTests {
        private static List<string> Record(object source) {
            var names = new List<string>();
            ((INotifyPropertyChanged) source).PropertyChanged += (_, e) => names.Add(e.PropertyName);
            return names;
        }

        // ---- [Autosave] + [NotifyChanges] on a NotifiyingJsonSettings ----------------------------

        [TestMethod]
        public void SingleAndMultipleWrites_SaveAndNotify_AndPersist() {
            using var f = new TempFile();
            var o = JsonSettings.Load<BothSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;
            var raised = Record(o);

            o.Name = "hello";
            o.Number = 42;
            o.When = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            o.Flag = true;

            saves.Should().Be(4, "each monitored write commits its own save");
            raised.Should().Equal(new[] { "Name", "Number", "When", "Flag" }, "and each raises PropertyChanged in order");

            var reloaded = JsonSettings.Load<BothSettings>(f.FileName);
            reloaded.Name.Should().Be("hello");
            reloaded.Number.Should().Be(42);
            reloaded.When.Should().Be(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            reloaded.Flag.Should().BeTrue();
        }

        /// <summary>
        ///     The one place the two aspects differ on purpose: autosave has no change-guard and
        ///     persists even a write of the same value, while <c>[NotifyChanges]</c>'s default
        ///     <c>OnlyChanged</c> suppresses it. So a no-op write can save without notifying.
        /// </summary>
        [TestMethod]
        public void NoOpWrite_Autosaves_ButDoesNotNotify() {
            using var f = new TempFile();
            var o = JsonSettings.Load<BothSettings>(f.FileName).EnableAutosave();
            o.Name = "a";
            var saves = 0;
            o.AfterSave += (s, d) => saves++;
            var raised = Record(o);

            o.Name = "a"; //same value

            saves.Should().Be(1, "autosave has no change-guard, so it still persists a no-op write");
            raised.Should().BeEmpty("but OnlyChanged suppresses the notification, so the View is not disturbed");
        }

        /// <summary>
        ///     <c>SuspendAutosave</c> batches saves but does not suspend notifications: the View stays
        ///     live while the disk writes coalesce into one.
        /// </summary>
        [TestMethod]
        public void SuspendAutosave_BatchesSaves_ButNotificationsStillFirePerWrite() {
            using var f = new TempFile();
            var o = JsonSettings.Load<BothSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;
            var raised = Record(o);

            using (o.SuspendAutosave()) {
                o.Name = "a";
                o.Number = 1;
                o.Flag = true;
                saves.Should().Be(0, "saves are deferred while suspended");
                raised.Should().Equal(new[] { "Name", "Number", "Flag" }, "notifications are not suspended");
            }

            saves.Should().Be(1, "exactly one batched save is committed on dispose");
            raised.Should().Equal(new[] { "Name", "Number", "Flag" }, "no extra notification is produced on dispose");

            var reloaded = JsonSettings.Load<BothSettings>(f.FileName);
            reloaded.Name.Should().Be("a");
            reloaded.Number.Should().Be(1);
            reloaded.Flag.Should().BeTrue();
        }

        [TestMethod]
        public void IgnoreAutosaveProperty_NotifiesButIsNotPersistedOnItsOwn() {
            using var f = new TempFile();
            var o = JsonSettings.Load<BothSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;
            var raised = Record(o);

            o.ViewOnly = "ui-state";

            saves.Should().Be(0, "[IgnoreAutosave] triggers no save");
            raised.Should().Equal(new[] { "ViewOnly" }, "but it still notifies the View");
            JsonSettings.Load<BothSettings>(f.FileName).ViewOnly.Should().BeNull("nothing was written to disk");
        }

        [TestMethod]
        public void IgnoreNotifyProperty_PersistsButDoesNotNotify() {
            using var f = new TempFile();
            var o = JsonSettings.Load<BothSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;
            var raised = Record(o);

            o.Silent = "kept";

            saves.Should().Be(1, "[IgnoreNotify] does not affect autosave");
            raised.Should().BeEmpty("but it is silent to the View");
            JsonSettings.Load<BothSettings>(f.FileName).Silent.Should().Be("kept");
        }

        [TestMethod]
        public void AlwaysGuardProperty_NotifiesAndSavesEveryWrite() {
            using var f = new TempFile();
            var o = JsonSettings.Load<BothSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;
            var raised = Record(o);

            o.Pulse = "x";
            o.Pulse = "x";
            o.Pulse = "x";

            saves.Should().Be(3, "autosave persists every write");
            raised.Should().Equal(new[] { "Pulse", "Pulse", "Pulse" }, "and the Always guard notifies every write");
        }

        [TestMethod]
        public void FrameworkFileName_IsNeitherNotifiedNorLeakedBySave() {
            using var f = new TempFile();
            var o = JsonSettings.Load<BothSettings>(f.FileName).EnableAutosave();
            var raised = Record(o);

            o.Name = "x"; //triggers Save(), which assigns o.FileName internally

            raised.Should().Equal(new[] { "Name" }, "Save()'s internal FileName write must not surface as a notification");
        }

        [TestMethod]
        public void Collection_Reassign_SavesAndNotifies_ThenMutate_SavesOnly() {
            using var f = new TempFile();
            var o = JsonSettings.Load<BothSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;
            var raised = Record(o);

            o.Items = new ObservableCollection<string> { "seed" };
            saves.Should().Be(1, "reassigning the collection saves once via the woven setter");
            raised.Should().Equal(new[] { "Items" }, "and notifies once");

            o.Items.Add("more");
            saves.Should().Be(2, "the rebound collection saves on in-place mutation");
            raised.Should().Equal(new[] { "Items" }, "an in-place Add is not a setter, so it raises no PropertyChanged on the settings");

            JsonSettings.Load<BothSettings>(f.FileName).Items.Should().BeEquivalentTo(new[] { "seed", "more" });
        }

        // ---- [Autosave] + [NotifyChangesMixin] on a bare JsonSettings ----------------------------

        [TestMethod]
        public void Mixin_SingleWrite_SavesAndNotifies_AndPersists() {
            using var f = new TempFile();
            var o = JsonSettings.Load<BothMixinSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;
            var raised = Record(o);

            o.Name = "hello";
            o.Number = 7;

            saves.Should().Be(2);
            raised.Should().Equal(new[] { "Name", "Number" });

            var reloaded = JsonSettings.Load<BothMixinSettings>(f.FileName);
            reloaded.Name.Should().Be("hello");
            reloaded.Number.Should().Be(7);
        }

        [TestMethod]
        public void Mixin_NoOpWrite_Autosaves_ButDoesNotNotify() {
            using var f = new TempFile();
            var o = JsonSettings.Load<BothMixinSettings>(f.FileName).EnableAutosave();
            o.Name = "a";
            var saves = 0;
            o.AfterSave += (s, d) => saves++;
            var raised = Record(o);

            o.Name = "a";

            saves.Should().Be(1, "autosave has no change-guard");
            raised.Should().BeEmpty("OnlyChanged suppresses the mixin's notification");
        }

        [TestMethod]
        public void Mixin_SuspendAutosave_BatchesSaves_ButNotificationsStillFire() {
            using var f = new TempFile();
            var o = JsonSettings.Load<BothMixinSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;
            var raised = Record(o);

            using (o.SuspendAutosave()) {
                o.Name = "a";
                o.Number = 1;
            }

            saves.Should().Be(1, "one batched save on dispose");
            raised.Should().Equal(new[] { "Name", "Number" }, "notifications fired live during suspension");
        }

        #region settings types

        [Autosave]
        [NotifyChanges]
        public class BothSettings : NotifiyingJsonSettings {
            public override string FileName { get; set; } = "both.jsn";

            public string Name { get; set; }
            public int Number { get; set; }
            public bool Flag { get; set; }
            public DateTime When { get; set; }

            [NotifyChanges(Guard = NotificationGuard.Always)]
            public string Pulse { get; set; }

            [IgnoreAutosave]
            public string ViewOnly { get; set; }

            [IgnoreNotify]
            public string Silent { get; set; }

            public ObservableCollection<string> Items { get; set; } = new ObservableCollection<string>();

            public BothSettings() { }
            public BothSettings(string fileName) : base(fileName) { }
        }

        [Autosave]
        [NotifyChangesMixin]
        public sealed class BothMixinSettings : JsonSettings {
            public override string FileName { get; set; } = "bothmixin.jsn";
            public string Name { get; set; }
            public int Number { get; set; }

            public BothMixinSettings() { }
            public BothMixinSettings(string fileName) : base(fileName) { }
        }

        #endregion
    }
}
