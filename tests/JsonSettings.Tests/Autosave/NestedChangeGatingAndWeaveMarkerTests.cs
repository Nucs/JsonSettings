using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Autosave {
    /// <summary>
    ///     Two behaviours hardened after adversarially attacking the 2.3.0 weave fix:
    ///     (1) nested-change saves (collection edits, nested INotifyPropertyChanged writes) honour
    ///     the same autosave gates as woven setter writes -- they used to call Save() directly,
    ///     which saved half-loaded files from inside Load(), recursed when an AfterSave handler
    ///     mutated a bound collection, and ignored SuspendAutosave() scopes; (2) EnableAutosave()
    ///     refuses an [Autosave] class whose assembly was never actually woven (the IAutosaveWoven
    ///     marker the weave mixes in is absent), instead of silently persisting nothing.
    /// </summary>
    [TestClass]
    public class NestedChangeGatingAndWeaveMarkerTests {
        [TestMethod]
        public void SuspendAutosave_BatchesInPlaceCollectionEdits() {
            using var f = new TempFile();
            var rpath = JsonSettings.ResolvePath(f);
            var o = JsonSettings.Load<GatedSettings>(f.FileName).EnableAutosave();
            var saved = new StrongBox<int>(0);
            o.AfterSave += (s, destinition) => { saved.Value++; };

            using (o.SuspendAutosave()) {
                //no setter runs for an in-place Add; only the binder sees these. It used to save
                //immediately, once per Add, straight through the suspension.
                o.Tags.Add("x");
                o.Tags.Add("y");
                saved.Value.Should().Be(0, "a suspension scope batches nested changes like it batches setter writes");
            }

            saved.Value.Should().Be(1, "the owed save commits once, on resume");
            var jsn = File.ReadAllText(rpath);
            jsn.Should().Contain("x").And.Contain("y");
        }

        [TestMethod]
        public void AfterSaveHandler_MutatingBoundCollection_DoesNotRecurse() {
            using var f = new TempFile();
            var o = JsonSettings.Load<GatedSettings>(f.FileName).EnableAutosave();
            var saved = new StrongBox<int>(0);
            var mutatedFromHandler = false;
            o.AfterSave += (s, destinition) => {
                saved.Value++;
                //mutating a BOUND collection from inside the save used to re-enter Save without
                //bound - an uncatchable stack overflow, not an assertable exception - so this test
                //passing at all is the proof.
                if (!mutatedFromHandler) {
                    mutatedFromHandler = true;
                    o.Tags.Add("from-handler");
                }
            };

            o.Tags.Add("first");
            saved.Value.Should().Be(1, "the nested change made inside the save must not re-enter it");
            o.Tags.Should().Contain("from-handler", "the handler's write is kept in memory, same as a woven-setter write from AfterSave");
        }

        [TestMethod]
        public void Populate_DoesNotSaveThroughNestedInpcBinding() {
            using var f = new TempFile();
            var rpath = JsonSettings.ResolvePath(f);
            var o = JsonSettings.Load<GatedSettings>(f.FileName).EnableAutosave();
            o.Profile.DisplayName = "on-disk"; //binder save through the nested INPC binding

            //diverge the file from memory so the populate genuinely writes a CHANGED value
            //through NestedProfile's raising setter while Load() is running.
            File.WriteAllText(rpath, File.ReadAllText(rpath).Replace("on-disk", "from-disk"));

            var saved = new StrongBox<int>(0);
            o.AfterSave += (s, destinition) => { saved.Value++; };
            var profileBefore = o.Profile;

            o.Load();

            saved.Value.Should().Be(0, "a PropertyChanged raised by the populate itself must not save the half-loaded file");
            o.Profile.DisplayName.Should().Be("from-disk");
            ReferenceEquals(profileBefore, o.Profile).Should().BeTrue("a non-collection nested object is populated in place, keeping bindings valid");

            o.Profile.DisplayName = "post-load";
            saved.Value.Should().Be(1, "the nested binding must still be live after the reload");
            File.ReadAllText(rpath).Should().Contain("post-load");
        }

        [TestMethod]
        public void GetOnlyCollection_LoadsInPlace_WithoutSavingDuringLoad() {
            using var f = new TempFile();
            var o = JsonSettings.Load<GatedSettings>(f.FileName).EnableAutosave();
            o.Pinned.Add("keep"); //binder save; file now holds ["keep"]

            var saved = new StrongBox<int>(0);
            o.AfterSave += (s, destinition) => { saved.Value++; };

            o.Load();

            //a get-only collection cannot be replaced (marking it Replace would make Json.NET skip
            //it entirely), so it keeps populate-in-place semantics: the file's items are APPENDED.
            //That in-place Add fires CollectionChanged mid-load - which used to save per item.
            saved.Value.Should().Be(0, "CollectionChanged raised by the populate must not save mid-load");
            o.Pinned.Count.Should().Be(2, "get-only collections keep the documented append-on-load semantics");
            o.Pinned.Should().Contain("keep");

            o.Pinned.Add("more");
            saved.Value.Should().Be(1, "the get-only collection stays bound after the reload");
        }

        [TestMethod]
        public void NotificationBinder_RequiresINotifyPropertyChanged() {
            using var f = new TempFile();
            var o = JsonSettings.Load<PlainSettings>(f.FileName);
            new Action(() => new NotificationBinder(o)).Should().Throw<ArgumentException>()
                                                       .WithMessage("*INotifyPropertyChanged*");
        }

        [TestMethod]
        public void UnwovenAttributedType_TripwireThrows_AndEscapeHatchLetsItThrough() {
            //every [Autosave] class COMPILED into this test assembly is woven (and must be - the
            //suite builds with the packaged targets), so the unwoven shape can only exist as a
            //runtime-emitted type: [Autosave] metadata present, no weave, no IAutosaveWoven.
            //This is exactly what a consumer build that silently skipped AspectInjector produces.
            var unwovenType = BuildUnwovenAutosaveType();
            typeof(IAutosaveWoven).IsAssignableFrom(unwovenType).Should().BeFalse();

            var instance = (JsonSettings) Activator.CreateInstance(unwovenType)!;
            new Action(() => instance.EnableAutosave()).Should().Throw<JsonSettingsException>()
                                                       .WithMessage("*never IL-woven*");

            //the documented escape hatch for assemblies woven by <2.3.0 (no marker stamped):
            //validation of the marker is skipped, everything else stays on. In one test method,
            //sequentially, because the flag is static.
            JsonSettingsAutosaveExtensions.RequireWeaveMarker = false;
            try {
                var second = (JsonSettings) Activator.CreateInstance(unwovenType)!;
                new Action(() => second.EnableAutosave()).Should().NotThrow();
            } finally {
                JsonSettingsAutosaveExtensions.RequireWeaveMarker = true;
            }
        }

        /// <summary>
        ///     Emits: <c>[Autosave] public class UnwovenSettings : JsonSettings</c> with the abstract
        ///     FileName overridden by a plain field-backed property and a public default constructor
        ///     -- everything EnableAutosave needs, minus the weave.
        /// </summary>
        private static Type BuildUnwovenAutosaveType() {
            var asm = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("UnwovenProbeAssembly"), AssemblyBuilderAccess.Run);
            var module = asm.DefineDynamicModule("UnwovenProbeModule");
            var tb = module.DefineType("UnwovenSettings", TypeAttributes.Public | TypeAttributes.Class, typeof(JsonSettings));

            var field = tb.DefineField("_fileName", typeof(string), FieldAttributes.Private);
            var prop = tb.DefineProperty("FileName", PropertyAttributes.None, typeof(string), null);
            const MethodAttributes accessorAttributes = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.SpecialName;

            var getter = tb.DefineMethod("get_FileName", accessorAttributes, typeof(string), Type.EmptyTypes);
            var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ret);

            var setter = tb.DefineMethod("set_FileName", accessorAttributes, typeof(void), new[] { typeof(string) });
            il = setter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, field);
            il.Emit(OpCodes.Ret);

            prop.SetGetMethod(getter);
            prop.SetSetMethod(setter);
            tb.DefineMethodOverride(getter, typeof(JsonSettings).GetProperty(nameof(JsonSettings.FileName))!.GetGetMethod()!);
            tb.DefineMethodOverride(setter, typeof(JsonSettings).GetProperty(nameof(JsonSettings.FileName))!.GetSetMethod()!);

            tb.SetCustomAttribute(new CustomAttributeBuilder(typeof(AutosaveAttribute).GetConstructor(Type.EmptyTypes)!, Array.Empty<object>()));
            tb.DefineDefaultConstructor(MethodAttributes.Public);

            return tb.CreateTypeInfo()!.AsType();
        }

        #region settings types

        [Autosave]
        public class GatedSettings : NotifiyingJsonSettings {
            public override string FileName { get; set; } = "gated.jsn";
            public NestedProfile Profile { get; set; } = new NestedProfile();
            public ObservableCollection<string> Tags { get; set; } = new ObservableCollection<string>();

            //get-only on purpose: the one collection shape that keeps populate-in-place semantics.
            public ObservableCollection<string> Pinned { get; } = new ObservableCollection<string>();

            public GatedSettings() { }
            public GatedSettings(string fileName) : base(fileName) { }
        }

        /// <summary>A nested observable object whose setter raises, the way a populate would observe.</summary>
        public class NestedProfile : INotifyPropertyChanged {
            private string _displayName = "";

            public string DisplayName {
                get => _displayName;
                set {
                    if (value == _displayName) return;
                    _displayName = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        //woven ([Autosave]) but deliberately NOT notification-capable in any way.
        [Autosave]
        public class PlainSettings : JsonSettings {
            public override string FileName { get; set; } = "plain-gate.jsn";
            public string Name { get; set; } = "";
            public PlainSettings() { }
            public PlainSettings(string fileName) : base(fileName) { }
        }

        #endregion
    }
}
