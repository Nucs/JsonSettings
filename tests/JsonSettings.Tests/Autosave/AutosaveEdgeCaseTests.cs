using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Examples;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Autosave {
    /// <summary>
    ///     Edge cases and regressions found while hunting the AspectInjector rewrite. Each test
    ///     names the failure it locks; the ones marked FIXED reproduced a real defect before the
    ///     corresponding change.
    /// </summary>
    [TestClass]
    public class AutosaveEdgeCaseTests {

        // ---- FIXED: nested SuspendAutosave committed a save when the inner scope closed --------

        /// <summary>
        ///     Nested suspensions must batch into a single save, not save when the inner scope ends.
        /// </summary>
        /// <remarks>
        ///     SuspendAutosave used to write the state flag directly, so an inner using-block's
        ///     Dispose reset it to Running -- ending the outer scope's suspension halfway through and
        ///     committing a save it was meant to be batching. Suspension is reference-counted now.
        /// </remarks>
        [TestMethod]
        public void NestedSuspend_BatchesIntoOneSave() {
            using var f = new TempFile();
            var o = JsonSettings.Load<AutosaveTests.Settings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            using (o.SuspendAutosave()) {
                using (o.SuspendAutosave()) {
                    o.property = "a";
                }
                saves.Should().Be(0, "the inner scope closing must not end suspension");
                o.property = "b";
                saves.Should().Be(0, "the outer scope is still suspending");
            }

            saves.Should().Be(1, "exactly one batched save on the outermost close");
            JsonSettings.Load<AutosaveTests.Settings>(f.FileName).property.Should().Be("b");
        }

        /// <summary>Single-level suspension is unchanged by the reference-counting.</summary>
        [TestMethod]
        public void SingleSuspend_StillCommitsExactlyOnce() {
            using var f = new TempFile();
            var o = JsonSettings.Load<AutosaveTests.Settings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            using (o.SuspendAutosave()) {
                o.property = "x";
                o.property = "y";
                saves.Should().Be(0);
            }

            saves.Should().Be(1);
        }

        // ---- FIXED: [IgnoreAutosave] collection initialised inline still saved on mutation ------

        /// <summary>
        ///     A collection property marked <see cref="IgnoreAutosaveAttribute"/> must not save when
        ///     its contents change, even when it is initialised at construction.
        /// </summary>
        /// <remarks>
        ///     The NotificationBinder used to bind every private INotify field regardless of the
        ///     property's attributes, so an ignored collection that was non-null at enable time was
        ///     still subscribed and saved on mutation. It now binds by opted-in property.
        /// </remarks>
        [TestMethod]
        public void IgnoredCollection_InitialisedInline_DoesNotSaveOnMutation() {
            using var f = new TempFile();
            var o = JsonSettings.Load<IgnoredInlineCollectionSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            o.Ignored.Add("x");

            saves.Should().Be(0, "[IgnoreAutosave] must be honoured for inline-initialised collections");
        }

        // ---- No regression: get-only collection (the idiomatic exposure) still autosaves --------

        /// <summary>
        ///     A get-only <see cref="ObservableCollection{T}"/> — no setter, mutated in place — must
        ///     still autosave on content change. This is the common way to expose a settings list.
        /// </summary>
        /// <remarks>
        ///     Guards against over-narrowing the binder: the fix for the ignored-collection bug must
        ///     not also drop read-only collections, which have no setter but are exactly what people
        ///     mutate.
        /// </remarks>
        [TestMethod]
        public void GetOnlyCollection_StillAutosavesOnMutation() {
            using var f = new TempFile();
            var o = JsonSettings.Load<GetOnlyCollectionSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            o.Items.Add("added");

            saves.Should().Be(1, "a get-only collection's contents changing must save");
            File.ReadAllText(JsonSettings.ResolvePath(f)).Should().Contain("added");
        }

        // ---- FIXED: non-virtual notifying collection did not rebind its replacement -------------

        /// <summary>
        ///     Reassigning a non-virtual collection property on a notifying class must rebind the new
        ///     collection so its later mutations still save.
        /// </summary>
        /// <remarks>
        ///     The binder required <c>virtual</c> where the save path did not, so a non-virtual
        ///     collection property saved on assignment but its replacement was never subscribed. Both
        ///     now share one filter (<see cref="AutosaveModule.IsNotificationBindable"/>).
        /// </remarks>
        [TestMethod]
        public void NonVirtualNotifyingCollection_RebindsReplacement() {
            using var f = new TempFile();
            var o = JsonSettings.Load<NonVirtualNotifyingCollectionSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            o.Items.Add("a");           // initial collection bound
            var afterFirst = saves;
            o.Items = new ObservableCollection<string>();  // woven setter saves, and must rebind
            o.Items.Add("b");           // new collection must save too

            afterFirst.Should().Be(1);
            saves.Should().Be(3, "reassigned non-virtual collection must be rebound");
        }

        // ---- FIXED: NotificationBinder leaked its handlers past Dispose -------------------------

        /// <summary>
        ///     After the settings are disposed, mutating a collection that had been bound must not
        ///     save through the disposed instance.
        /// </summary>
        /// <remarks>
        ///     NotificationBinder.Dispose unsubscribed the settings' own PropertyChanged but not the
        ///     CollectionChanged handlers it had attached to nested collections, so a collection held
        ///     elsewhere kept the settings alive and kept saving through it after disposal.
        /// </remarks>
        [TestMethod]
        public void Dispose_UnbindsNestedCollectionHandlers() {
            using var f = new TempFile();
            var o = JsonSettings.Load<ExampleNotifyingSettings>(f).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;
            var boundCollection = o.Residents;

            o.Dispose();
            boundCollection.Add("after-dispose");

            saves.Should().Be(0, "a disposed settings' binder must not still be saving on collection change");
        }

        // ---- FIXED: setting a property inside AfterSave recursed until stack overflow -----------

        /// <summary>
        ///     Writing a monitored property from inside an <c>AfterSave</c> handler must not recurse.
        /// </summary>
        /// <remarks>
        ///     The woven setter's save fires AfterSave; a handler that wrote a monitored property
        ///     re-entered the save and recursed without bound — an uncatchable stack overflow. The
        ///     module now suppresses autosave while it is already saving.
        /// </remarks>
        [TestMethod]
        public void WritingMonitoredPropertyInAfterSave_DoesNotRecurse() {
            using var f = new TempFile();
            var o = JsonSettings.Load<TwoProps>(f.FileName).EnableAutosave();
            int depth = 0, maxDepth = 0, saves = 0;
            o.AfterSave += (s, d) => {
                saves++;
                depth++;
                maxDepth = Math.Max(maxDepth, depth);
                if (depth < 50)
                    o.B = depth.ToString();   // would recurse without the guard
                depth--;
            };

            o.A = "trigger";

            maxDepth.Should().Be(1, "autosave must not re-enter itself");
            saves.Should().Be(1, "the re-entrant write is suppressed, not saved again");
        }

        /// <summary>The in-memory value written during AfterSave is kept, just not re-saved.</summary>
        [TestMethod]
        public void WriteInAfterSave_KeepsValueInMemory() {
            using var f = new TempFile();
            var o = JsonSettings.Load<TwoProps>(f.FileName).EnableAutosave();
            o.AfterSave += (s, d) => { if (o.B is null) o.B = "set-during-save"; };

            o.A = "trigger";

            o.B.Should().Be("set-during-save", "the value is assigned in memory even though it did not re-save");
        }

        // ---- Documented behaviour: EnableAutosave requires the concrete type to be woven --------

        /// <summary>
        ///     A derived class whose base is <c>[Autosave]</c> but which is not itself marked must be
        ///     rejected, rather than silently autosaving only the base's properties.
        /// </summary>
        /// <remarks>
        ///     <c>[Autosave]</c> is not inherited and weaving follows declaration, so the derived
        ///     type's own setters would not be woven. Enabling anyway would drop writes to the
        ///     derived properties silently, so <see cref="TypeValidation"/> throws instead — the same
        ///     silent-loss guard the missing-attribute case has.
        /// </remarks>
        [TestMethod]
        public void DerivedOfMarkedBase_ButItselfUnmarked_Throws() {
            using var f = new TempFile();
            var o = JsonSettings.Load<UnmarkedDerivedOfMarkedBase>(f.FileName);

            new Action(() => o.EnableAutosave()).Should().Throw<JsonSettingsException>()
                                                .WithMessage("*is not marked with*");
        }

        // ---- Documented behaviour: init-only properties compile, weave, and never autosave ------

#if NET6_0_OR_GREATER
        /// <summary>
        ///     An <c>init</c>-only property is harmless: it is set only during construction (before a
        ///     module exists) and cannot be reassigned afterwards, so it simply never autosaves,
        ///     while ordinary mutable properties on the same class do.
        /// </summary>
        /// <remarks>
        ///     net6.0+ only: <c>init</c> needs <c>System.Runtime.CompilerServices.IsExternalInit</c>,
        ///     which the net48/net472 reference assemblies do not carry. The product code targets no
        ///     lower bar for this feature, so the coverage gap is only in the test.
        /// </remarks>
        [TestMethod]
        public void InitOnlyProperty_IsInertAndMutableSiblingSaves() {
            using var f = new TempFile();
            var o = JsonSettings.Load<InitOnlySettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            o.Mutable = "changed";

            saves.Should().Be(1);
            JsonSettings.Load<InitOnlySettings>(f.FileName).Mutable.Should().Be("changed");
        }
#endif

        // ---- Documented behaviour: a throwing Save surfaces at the assignment -------------------

        /// <summary>
        ///     If the save triggered by a write fails, the exception surfaces at the assignment, and
        ///     the new value is already in memory (the setter body ran before the advice).
        /// </summary>
        [TestMethod]
        public void FailingSave_PropagatesToTheAssignment() {
            using var f = new TempFile();
            var o = JsonSettings.Load<TwoProps>(f.FileName).EnableAutosave();
            o.BeforeSave += (JsonSettings s, ref string dest) => throw new InvalidOperationException("disk full");

            new Action(() => o.A = "x").Should().Throw<InvalidOperationException>().WithMessage("disk full");
            o.A.Should().Be("x", "the setter body ran and set the value before the failing save");
        }

        // ---- Documented behaviour: an indexer is not treated as a monitored property ------------

        /// <summary>
        ///     Writing through an indexer must not autosave, while a normal property on the same
        ///     class does.
        /// </summary>
        /// <remarks>
        ///     An indexer is not a serializable settings property, and its backing store is often not
        ///     serialized at all, so saving on <c>settings[key] = value</c> would write nothing
        ///     useful and could be surprisingly costly on a hot indexer. Indexers are excluded from
        ///     monitoring; use a normal property or call Save() explicitly.
        /// </remarks>
        [TestMethod]
        public void Indexer_DoesNotAutosave_ButNormalPropertyDoes() {
            using var f = new TempFile();
            var o = JsonSettings.Load<IndexerSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            o["key"] = "value";
            saves.Should().Be(0, "an indexer write is not a monitored-property change");

            o.Normal = "n";
            saves.Should().Be(1, "a normal property still autosaves");
        }

        // ---- Documented behaviour: SuspendAutosave before EnableAutosave throws -----------------

        /// <summary>
        ///     SuspendAutosave needs the module EnableAutosave attaches; calling it first throws
        ///     rather than silently doing nothing.
        /// </summary>
        [TestMethod]
        public void SuspendAutosave_BeforeEnable_Throws() {
            using var f = new TempFile();
            var o = JsonSettings.Load<TwoProps>(f.FileName);

            new Action(() => { using (o.SuspendAutosave()) { } }).Should().Throw<Exception>();
        }

        #region settings types

        [Autosave]
        public class TwoProps : JsonSettings {
            public override string FileName { get; set; } = "twoprops.jsn";
            public string A { get; set; }
            public string B { get; set; }
            public TwoProps() { }
            public TwoProps(string fileName) : base(fileName) { }
        }

        [Autosave]
        public class IgnoredInlineCollectionSettings : NotifiyingJsonSettings {
            public override string FileName { get; set; } = "ignored-inline.jsn";
            private ObservableCollection<string> _ignored = new ObservableCollection<string>();

            [IgnoreAutosave]
            public virtual ObservableCollection<string> Ignored {
                get => _ignored;
                set { _ignored = value; OnPropertyChanged(); }
            }

            public IgnoredInlineCollectionSettings() { }
            public IgnoredInlineCollectionSettings(string fileName) : base(fileName) { }
        }

        [Autosave]
        public class GetOnlyCollectionSettings : NotifiyingJsonSettings {
            public override string FileName { get; set; } = "getonly.jsn";
            //no setter: exposed read-only, mutated in place.
            public ObservableCollection<string> Items { get; } = new ObservableCollection<string>();
            public GetOnlyCollectionSettings() { }
            public GetOnlyCollectionSettings(string fileName) : base(fileName) { }
        }

        [Autosave]
        public class NonVirtualNotifyingCollectionSettings : NotifiyingJsonSettings {
            public override string FileName { get; set; } = "nonvirtual-coll.jsn";
            private ObservableCollection<string> _items = new ObservableCollection<string>();
            //NON-virtual settable collection property.
            public ObservableCollection<string> Items {
                get => _items;
                set { _items = value; OnPropertyChanged(); }
            }
            public NonVirtualNotifyingCollectionSettings() { }
            public NonVirtualNotifyingCollectionSettings(string fileName) : base(fileName) { }
        }

        [Autosave]
        public class MarkedBaseForInheritance : JsonSettings {
            public override string FileName { get; set; } = "marked-base-inh.jsn";
            public string BaseProp { get; set; }
            public MarkedBaseForInheritance() { }
            public MarkedBaseForInheritance(string fileName) : base(fileName) { }
        }

        //deliberately NOT marked [Autosave].
        public class UnmarkedDerivedOfMarkedBase : MarkedBaseForInheritance {
            public string DerivedProp { get; set; }
            public UnmarkedDerivedOfMarkedBase() { }
            public UnmarkedDerivedOfMarkedBase(string fileName) : base(fileName) { }
        }

        [Autosave]
        public class IndexerSettings : JsonSettings {
            public override string FileName { get; set; } = "indexer.jsn";
            private readonly System.Collections.Generic.Dictionary<string, string> _store = new System.Collections.Generic.Dictionary<string, string>();
            public string Normal { get; set; }
            public string this[string key] {
                get => _store.TryGetValue(key, out var v) ? v : null;
                set => _store[key] = value;
            }
            public IndexerSettings() { }
            public IndexerSettings(string fileName) : base(fileName) { }
        }

#if NET6_0_OR_GREATER
        [Autosave]
        public class InitOnlySettings : JsonSettings {
            public override string FileName { get; set; } = "initonly.jsn";
            public string Mutable { get; set; }
            public string InitOnly { get; init; }
            public InitOnlySettings() { }
            public InitOnlySettings(string fileName) : base(fileName) { }
        }
#endif

        #endregion
    }
}
