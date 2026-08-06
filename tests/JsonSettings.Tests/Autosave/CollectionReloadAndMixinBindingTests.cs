using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Examples;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Modulation.Recovery;
using Nucs.JsonSettings.NotifyChanges;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Autosave {
    /// <summary>
    ///     Locks in three behaviours that shipped broken before 2.3.0 and were caught by attacking
    ///     the weave fix: (1) EnableAutosave binds nested collections on ANY INotifyPropertyChanged
    ///     settings -- a [NotifyChangesMixin] class included -- not only on the NotifiyingJsonSettings
    ///     base; (2) reloading an instance replaces collection contents instead of appending the
    ///     file's items to what is already in memory (Json.NET's Auto creation handling); (3) a
    ///     RenameAndLoadDefault recovery genuinely resets collections to defaults, and the binder is
    ///     resynced to the replacement instances so later in-place edits still save.
    /// </summary>
    [TestClass]
    public class CollectionReloadAndMixinBindingTests {
        [TestMethod]
        public void MixinClass_CollectionAdd_SavesAndPersists() {
            using var f = new TempFile();
            var rpath = JsonSettings.ResolvePath(f);

            var saved = new StrongBox<int>(0);
            var o = JsonSettings.Load<MixinCollectionSettings>(f.FileName).EnableAutosave();
            o.AfterSave += (s, destinition) => { saved.Value++; };

            //no setter runs here; only the collection binding can commit this write. Before
            //2.3.0 the binder was keyed to the NotifiyingJsonSettings base class, which a
            //mixin class can never be, so this add compiled, looked bound and never saved.
            o.Tags.Add("one");
            saved.Value.Should().Be(1, "an in-place Add on a mixin class must save through the collection binding");
            File.ReadAllText(rpath).Should().Contain("one");
        }

        [TestMethod]
        public void WovenClass_CarriesWeaveMarker_UnwovenDoesNot() {
            //the weave's witness: the [Autosave] aspect mixes IAutosaveWoven into every class it
            //processes, which is how EnableAutosave can refuse a build that silently skipped
            //AspectInjector (the attribute alone survives such a build; the interface cannot).
            typeof(IAutosaveWoven).IsAssignableFrom(typeof(MixinCollectionSettings)).Should().BeTrue();
            typeof(IAutosaveWoven).IsAssignableFrom(typeof(ReloadCollectionSettings)).Should().BeTrue();
            typeof(IAutosaveWoven).IsAssignableFrom(typeof(SeededCollectionSettings)).Should().BeFalse("this class has no [Autosave] and is never woven");
        }

        [TestMethod]
        public void Reload_DoesNotDuplicateCollectionItems() {
            using var f = new TempFile();
            var o = JsonSettings.Load<ReloadCollectionSettings>(f.FileName).EnableAutosave();
            o.Tags.Add("a"); //saves ["a"]
            o.Tags.Count.Should().Be(1);

            //file: ["a"], memory: ["a"]. Under Json.NET's default Auto creation handling the
            //populate REUSED the in-memory collection and appended, so this used to yield
            //["a","a"] -- and a collection with non-empty defaults grew on every start.
            o.Load();
            o.Tags.Count.Should().Be(1, "populating must replace the collection, not append the file's copy to it");
            o.Tags[0].Should().Be("a");
        }

        [TestMethod]
        public void Reload_RebindsReplacedCollection_SubsequentAddSaves() {
            using var f = new TempFile();
            var rpath = JsonSettings.ResolvePath(f);
            var o = JsonSettings.Load<ReloadCollectionSettings>(f.FileName).EnableAutosave();
            o.Tags.Add("a");

            var before = o.Tags;
            o.Load();
            ReferenceEquals(before, o.Tags).Should().BeFalse("Replace semantics deserialize into a fresh instance");

            //ReloadCollectionSettings raises no PropertyChanged from its auto-setters, so the
            //binder cannot learn of the swap through the event pipe -- only the explicit resync
            //the load pipeline performs can keep it current. This is the regression test for it.
            var saved = new StrongBox<int>(0);
            o.AfterSave += (s, destinition) => { saved.Value++; };
            o.Tags.Add("b");
            saved.Value.Should().Be(1, "the binder must follow the instance the reload created");
            File.ReadAllText(rpath).Should().Contain("b");
        }

        [TestMethod]
        public void RecoveryLoadDefault_ResetsCollections_AndKeepsThemBound() {
            using var f = new TempFile(false);
            try {
                var o = JsonSettings.Configure<ReloadCollectionSettings>(f)
                                    .WithRecovery(RecoveryAction.RenameAndLoadDefault)
                                    .LoadNow()
                                    .EnableAutosave();
                o.Tags.Add("precious");

                File.WriteAllText(f.FileName, "{ definitely not valid json :::");
                o.Load(); //recovery renames the corrupt file aside and loads defaults

                //defaults have ZERO tags. Under Auto creation handling the populate appended
                //nothing to the still-live ["precious"] and recovery's own Save() then wrote the
                //stale items back out as the new "defaults" file.
                o.Tags.Should().BeEmpty("RenameAndLoadDefault must actually reset collection contents to defaults");
                File.ReadAllText(f.FileName).Should().NotContain("precious", "the recovered defaults file must not resurrect stale items");

                var saved = new StrongBox<int>(0);
                o.AfterSave += (s, destinition) => { saved.Value++; };
                o.Tags.Add("fresh");
                saved.Value.Should().Be(1, "collection binding must survive the recovery reload");
                File.ReadAllText(f.FileName).Should().Contain("fresh");
            } finally {
                //the recovery renamed the corrupt file to an archive sibling TempFile does not own
                var dir = Path.GetDirectoryName(f.FileName)!;
                var stem = Path.GetFileNameWithoutExtension(f.FileName);
                foreach (var leftover in Directory.GetFiles(dir, stem + "*"))
                    if (!string.Equals(leftover, f.FileName, StringComparison.OrdinalIgnoreCase))
                        File.Delete(leftover);
            }
        }

        [TestMethod]
        public void SeededDefaultCollection_DoesNotGrowAcrossLoads() {
            using var f = new TempFile();
            //first load: no file yet, defaults (["seed"]) are saved out
            var first = JsonSettings.Load<SeededCollectionSettings>(f.FileName);
            first.Tags.Count.Should().Be(1);

            //every later "application start" populates an instance whose default already holds
            //"seed" from a file that also holds "seed" -- the classic doubling reload.
            var second = JsonSettings.Load<SeededCollectionSettings>(f.FileName);
            second.Tags.Count.Should().Be(1, "a default-seeded collection must not gain a copy per load");
            var third = JsonSettings.Load<SeededCollectionSettings>(f.FileName);
            third.Tags.Count.Should().Be(1);
        }

        [Autosave, NotifyChangesMixin]
        public sealed class MixinCollectionSettings : JsonSettings {
            public override string FileName { get; set; } = "mixin-collection.jsn";
            public string Name { get; set; } = "";
            public ObservableCollection<string> Tags { get; set; } = new ObservableCollection<string>();

            public MixinCollectionSettings() { }
            public MixinCollectionSettings(string fileName) : base(fileName) { }
        }

        //auto-properties on purpose: nothing raises PropertyChanged during a populate, so any
        //rebinding these tests observe is the load pipeline's explicit resync, not the event pipe.
        [Autosave]
        public class ReloadCollectionSettings : NotifiyingJsonSettings {
            public override string FileName { get; set; } = "reload-collection.jsn";
            public string Name { get; set; } = "";
            public ObservableCollection<string> Tags { get; set; } = new ObservableCollection<string>();

            public ReloadCollectionSettings() { }
            public ReloadCollectionSettings(string fileName) : base(fileName) { }
        }

        //deliberately NOT [Autosave]: pure serialization behaviour, and the negative case for the
        //weave-marker test above.
        public class SeededCollectionSettings : JsonSettings {
            public override string FileName { get; set; } = "seeded-collection.jsn";
            public ObservableCollection<string> Tags { get; set; } = new ObservableCollection<string> { "seed" };

            public SeededCollectionSettings() { }
            public SeededCollectionSettings(string fileName) : base(fileName) { }
        }
    }
}
