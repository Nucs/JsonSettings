using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Examples;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Autosave {
    /// <summary>
    ///     The contracts of the 2.3.0 module split: the base package owns only the neutral
    ///     save-suspension state (<see cref="SuspensionModule"/>) and the bag's own
    ///     <see cref="SettingsBagAutosaveModule"/>, while <see cref="AutosaveModule"/> — the woven
    ///     path's module — lives in Nucs.JsonSettings.Autosave. The two packages meet only through
    ///     the shared base type and the BeforeRepopulate/AfterRepopulate events the load
    ///     pipeline raises around every populate, which replaced both the load pipeline's direct
    ///     IsLoading bracketing and the INotificationsHandler resync tunnel.
    /// </summary>
    [TestClass]
    public class ModuleIsolationAndRepopulateTests {

        // ---- A. Package isolation: where the types live and how they relate ---------------------

        /// <summary>
        ///     The woven-path module must live in the Autosave assembly; the neutral suspension
        ///     state and the bag's module must live in the base assembly. This is the observable
        ///     witness of "the base package knows nothing about weaving".
        /// </summary>
        [TestMethod]
        public void ModuleTypes_LiveInTheirOwnPackages() {
            var baseAssembly = typeof(JsonSettings).Assembly;
            var autosaveAssembly = typeof(AutosaveRuntime).Assembly;

            typeof(SuspensionModule).Assembly.Should().BeSameAs(baseAssembly, "the neutral suspension state is base-package material — SettingsBag drives it standalone");
            typeof(SettingsBagAutosaveModule).Assembly.Should().BeSameAs(baseAssembly, "the bag's autosave is dictionary-backed and must work with the base package alone");
            typeof(AutosaveModule).Assembly.Should().BeSameAs(autosaveAssembly, "the woven path's module belongs to the package that wires it up");
        }

        /// <summary>
        ///     Both concrete modules derive from <see cref="SuspensionModule"/> — the single shared
        ///     implementation of the gates and the reference-counted suspension machine, so the two
        ///     autosave paths cannot drift apart on re-entrancy/suspension semantics.
        /// </summary>
        [TestMethod]
        public void BothModules_ShareTheSuspensionPrimitive() {
            typeof(SuspensionModule).IsAssignableFrom(typeof(AutosaveModule)).Should().BeTrue();
            typeof(SuspensionModule).IsAssignableFrom(typeof(SettingsBagAutosaveModule)).Should().BeTrue();
        }

        // ---- B. Bag isolation: SettingsBag attaches its own module ------------------------------

        /// <summary>
        ///     Enabling autosave on a bag attaches a <see cref="SettingsBagAutosaveModule"/>, not
        ///     the woven path's <see cref="AutosaveModule"/> — so the woven advice and the
        ///     NotificationBinder, which resolve modules with an <c>is AutosaveModule</c> scan,
        ///     can never confuse a bag's module for a woven one.
        /// </summary>
        [TestMethod]
        public void BagAutosave_AttachesItsOwnModule_NotTheWovenOne() {
            using var f = new TempFile();
            var bag = JsonSettings.Load<SettingsBag>(f.FileName).EnableAutosave();

            bag.Modulation.IsAttachedOfType<SettingsBagAutosaveModule>().Should().BeTrue();
            bag.Modulation.IsAttachedOfType<AutosaveModule>().Should().BeFalse("a bag is never woven; the woven module must not appear on it");

            //the shared base type resolves the bag's module — this is what SuspendAutosave targets.
            bag.Modulation.GetModule<SuspensionModule>().Should().BeSameAs(bag.Modulation.GetModule<SettingsBagAutosaveModule>());
        }

        [TestMethod]
        public void BagAutosave_ToggledOff_DetachesItsModule() {
            using var f = new TempFile();
            var bag = JsonSettings.Load<SettingsBag>(f.FileName).EnableAutosave();

            bag.Autosave = false;

            bag.Modulation.IsAttachedOfType<SettingsBagAutosaveModule>().Should().BeFalse();
        }

        /// <summary>
        ///     A woven settings class still resolves its module under both the concrete and the
        ///     shared base type — one instance, two views.
        /// </summary>
        [TestMethod]
        public void WovenSettings_ResolveTheSameModule_ViaBaseAndConcreteType() {
            using var f = new TempFile();
            var o = JsonSettings.Load<WovenProbe>(f.FileName).EnableAutosave();

            o.Modulation.GetModule<SuspensionModule>().Should().BeSameAs(o.Modulation.GetModule<AutosaveModule>());
        }

        /// <summary>
        ///     <see cref="SuspendAutosave"/> is constructible from the shared base type directly,
        ///     so suspension drives a bag's module and a woven module through the very same struct.
        /// </summary>
        [TestMethod]
        public void SuspendAutosaveStruct_DrivesTheBagModule_ThroughTheBaseType() {
            using var f = new TempFile();
            var bag = JsonSettings.Load<SettingsBag>(f.FileName).EnableAutosave();
            var saves = 0;
            bag.AfterSave += (s, d) => saves++;

            using (new SuspendAutosave(bag.Modulation.GetModule<SuspensionModule>())) {
                bag["k"] = "v";
                saves.Should().Be(0, "the change is batched while suspended");
            }

            saves.Should().Be(1, "the owed save commits when the scope closes");
            JsonSettings.Load<SettingsBag>(f.FileName)["k"].Should().Be("v");
        }

        // ---- C. Repopulate events: the load pipeline's only signal ------------------------------

        /// <summary>
        ///     Every populate — a direct <see cref="JsonSettings.LoadJson"/> call and a reload from
        ///     disk alike — raises BeforeRepopulate then AfterRepopulate on the instance, whether or
        ///     not any autosave machinery is attached.
        /// </summary>
        [TestMethod]
        public void RepopulateEvents_FireAroundEveryPopulate() {
            using var f = new TempFile();
            var o = JsonSettings.Load<PlainSettings>(f.FileName);
            int before = 0, after = 0;
            o.BeforeRepopulate += s => {
                s.Should().BeSameAs(o);
                before++;
                after.Should().Be(before - 1, "Before must precede After for each populate");
            };
            o.AfterRepopulate += (s, successful) => {
                s.Should().BeSameAs(o);
                successful.Should().BeTrue("these populates all run to completion");
                after++;
            };

            o.LoadJson(@"{""A"":""json""}");
            before.Should().Be(1);
            after.Should().Be(1);
            o.A.Should().Be("json");

            o.Load(); //reload from disk funnels through the same populate
            before.Should().Be(2);
            after.Should().Be(2);

            o.LoadDefault();
            before.Should().Be(3);
            after.Should().Be(3);
        }

        /// <summary>
        ///     Where the pair sits in the load pipeline of a file <c>Load()</c>: nested immediately
        ///     inside BeforeDeserialize/AfterDeserialize (which fire only for file loads), with
        ///     AfterLoad outermost. On <c>LoadDefault()</c> and direct <c>LoadJson()</c> calls the
        ///     repopulate pair fires alone — it is the only per-populate signal.
        /// </summary>
        [TestMethod]
        public void RepopulateEvents_NestDirectlyInsideTheDeserializePair_OnAFileLoad() {
            using var f = new TempFile();
            var o = JsonSettings.Load<PlainSettings>(f.FileName);
            o.A = "persisted";
            o.Save();
            var order = new List<string>();
            o.BeforeDeserialize += (JsonSettings s, ref string data) => order.Add("BeforeDeserialize");
            o.BeforeRepopulate += s => order.Add("BeforeRepopulate");
            o.AfterRepopulate += (s, successful) => order.Add("AfterRepopulate");
            o.AfterDeserialize += s => order.Add("AfterDeserialize");
            o.AfterLoad += (s, successful) => order.Add("AfterLoad");

            o.Load();

            order.Should().Equal("BeforeDeserialize", "BeforeRepopulate", "AfterRepopulate", "AfterDeserialize", "AfterLoad");
        }

        /// <summary>
        ///     AfterRepopulate must fire even when the populate throws: a populate that died halfway
        ///     (the recovery path) has still replaced some values, so subscribers — the binder's
        ///     resync, the modules' IsLoading release — must run on the unwind as well.
        /// </summary>
        [TestMethod]
        public void AfterRepopulate_FiresEvenWhenThePopulateThrows() {
            using var f = new TempFile();
            var o = JsonSettings.Load<PlainSettings>(f.FileName);
            int before = 0, after = 0;
            bool? reportedSuccess = null;
            o.BeforeRepopulate += s => before++;
            o.AfterRepopulate += (s, successful) => {
                after++;
                reportedSuccess = successful;
            };

            new Action(() => o.LoadJson(@"{""A"": <not json>")).Should().Throw<JsonException>();

            before.Should().Be(1);
            after.Should().Be(1, "the finally must release subscribers even on a failed populate");
            reportedSuccess.Should().BeFalse("a populate that threw must report failure so data-consuming subscribers can tell a torn graph from a loaded one");
        }

        /// <summary>
        ///     The module brackets its own IsLoading via the events — the load pipeline no longer
        ///     reaches into it. Observed from handlers subscribed after EnableAutosave, so the
        ///     module's own handlers (subscribed on Attach) have already run when these fire.
        /// </summary>
        [TestMethod]
        public void WovenModule_BracketsItsOwnIsLoading_AroundThePopulate() {
            using var f = new TempFile();
            var o = JsonSettings.Load<WovenProbe>(f.FileName).EnableAutosave();
            o.A = "seed"; //autosaved; gives the reload something to populate
            var module = o.Modulation.GetModule<AutosaveModule>();
            bool? duringBefore = null, duringAfter = null;
            o.BeforeRepopulate += s => duringBefore = module.IsLoading;
            o.AfterRepopulate += (s, successful) => duringAfter = module.IsLoading;

            o.Load();

            duringBefore.Should().BeTrue("the module must raise its loading gate before the populate writes properties");
            duringAfter.Should().BeFalse("the gate must drop once the populate is over");
            module.IsLoading.Should().BeFalse();
        }

        /// <summary>
        ///     The bag's module gets the same event-driven bracketing: a <c>Set</c> made while the
        ///     populate is running (from a mid-load handler) must not save the half-loaded file,
        ///     and autosave must resume once the load completes.
        /// </summary>
        [TestMethod]
        public void BagModule_SuppressesMidLoadWrites_AndResumesAfter() {
            using var f = new TempFile();
            var bag = JsonSettings.Load<SettingsBag>(f.FileName).EnableAutosave();
            bag["seed"] = 1; //autosaved; gives the reload something to populate
            var saves = 0;
            bag.AfterSave += (s, d) => saves++;
            bag.BeforeRepopulate += s => bag["mid-load"] = 2; //a write from inside the load window

            bag.Load();

            saves.Should().Be(0, "a write during the populate window must not commit the half-loaded file");
            bag["post-load"] = 3;
            saves.Should().Be(1, "the loading gate must clear once the load completes");
        }

        // ---- D. Binder self-subscription: resync without the load pipeline knowing --------------

        /// <summary>
        ///     A stand-alone <see cref="NotificationBinder"/> — constructed directly, no
        ///     EnableAutosave, no module — now resyncs after a populate too: collection properties
        ///     deserialize with Replace semantics, and the binder hears about the replacement
        ///     through AfterRepopulate rather than through the module slot the load pipeline used
        ///     to pattern-match. Before the events this binder stayed bound to the pre-load
        ///     instance and every in-place edit after a load silently never saved.
        /// </summary>
        [TestMethod]
        public void StandaloneBinder_IsResyncedByThePopulate_SoTheReplacedCollectionStillSaves() {
            using var f = new TempFile();
            var o = JsonSettings.Load<SilentCollectionSettings>(f.FileName);
            using var binder = new NotificationBinder(o);
            var saves = 0;
            o.AfterSave += (s, d) => saves++;
            var oldItems = o.Items;

            //Items is an auto-property on a notifying base: the populate's replace assignment
            //raises no PropertyChanged, so only the repopulate resync can move the subscription.
            o.LoadJson(@"{""Items"":[""from-disk""]}");
            o.Items.Should().NotBeSameAs(oldItems, "writable collections deserialize with Replace semantics");

            o.Items.Add("edited-after-load");
            saves.Should().Be(1, "the binder must be watching the collection instance the property holds now");

            oldItems.Add("stale");
            saves.Should().Be(1, "the replaced collection must have been unsubscribed");
        }

        /// <summary>
        ///     Disposing the binder must also sever it from the populate pipeline: a later load may
        ///     not resurrect its subscriptions, and edits after dispose never save.
        /// </summary>
        [TestMethod]
        public void DisposedBinder_IsNotResynced_AndNoLongerSaves() {
            using var f = new TempFile();
            var o = JsonSettings.Load<SilentCollectionSettings>(f.FileName);
            var binder = new NotificationBinder(o);
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            binder.Dispose();
            o.LoadJson(@"{""Items"":[""from-disk""]}");

            o.Items.Add("after-dispose");
            saves.Should().Be(0, "a disposed binder must not be rebound by a populate");
        }

        #region settings types

        //No [Autosave]: the repopulate events are a base-package lifecycle fact, present with no
        //autosave machinery in sight.
        public class PlainSettings : JsonSettings {
            public override string FileName { get; set; } = "plain-repopulate.jsn";
            public string A { get; set; }
            public PlainSettings() { }
            public PlainSettings(string fileName) : base(fileName) { }
        }

        [Autosave]
        public class WovenProbe : JsonSettings {
            public override string FileName { get; set; } = "woven-probe.jsn";
            public string A { get; set; }
            public WovenProbe() { }
            public WovenProbe(string fileName) : base(fileName) { }
        }

        //Notifying BASE (so the binder accepts it) but a plain auto-property (so the populate's
        //replace assignment raises no PropertyChanged) — the shape only the repopulate resync can
        //keep bound. No [Autosave]: this class exercises the binder stand-alone, without a module.
        public class SilentCollectionSettings : NotifiyingJsonSettings {
            public override string FileName { get; set; } = "silent-collection.jsn";
            public ObservableCollection<string> Items { get; set; } = new ObservableCollection<string>();
            public SilentCollectionSettings() { }
            public SilentCollectionSettings(string fileName) : base(fileName) { }
        }

        #endregion
    }
}
