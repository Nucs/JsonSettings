using System;
using System.IO;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Autosave {
    /// <summary>
    ///     SettingsBag and DynamicSettingsBag autosave. The bag has its own dictionary-backed
    ///     autosave (no weaving) driven by its own SettingsBagAutosaveModule, which shares the
    ///     SuspensionModule state machine with the woven path — so the same reentrancy and
    ///     suspension guarantees have to hold here too.
    /// </summary>
    [TestClass]
    public class SettingsBagAutosaveTests {

        // ---- FIXED: writing the bag inside AfterSave recursed until stack overflow --------------

        /// <summary>
        ///     Mutating the bag from inside an <c>AfterSave</c> handler must not recurse.
        /// </summary>
        /// <remarks>
        ///     SettingsBag.TrySave called Save() directly and did not consult the module's IsSaving
        ///     guard, so it had the same unbounded-recursion crash the woven path had before its
        ///     fix. It now uses the same guard.
        /// </remarks>
        [TestMethod]
        public void Reentrancy_WritingBagInAfterSave_DoesNotRecurse() {
            using var f = new TempFile();
            var o = JsonSettings.Load<SettingsBag>(f.FileName).EnableAutosave();
            int depth = 0, maxDepth = 0, saves = 0;
            o.AfterSave += (s, d) => {
                saves++;
                depth++;
                maxDepth = Math.Max(maxDepth, depth);
                if (depth < 50)
                    o["counter"] = depth;   // would recurse without the guard
                depth--;
            };

            o["trigger"] = "x";

            maxDepth.Should().Be(1, "the bag's autosave must not re-enter itself");
            saves.Should().Be(1);
        }

        // ---- Coverage: Remove / RemoveWhere autosave --------------------------------------------

        [TestMethod]
        public void Remove_Autosaves() {
            using var f = new TempFile();
            var o = JsonSettings.Load<SettingsBag>(f.FileName).EnableAutosave();
            o["k"] = "v";
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            o.Remove("k").Should().BeTrue();

            saves.Should().Be(1);
            JsonSettings.Load<SettingsBag>(f.FileName).Data.ContainsKey("k").Should().BeFalse();
        }

        [TestMethod]
        public void RemoveWhere_AutosavesOnceForTheBatch() {
            using var f = new TempFile();
            var o = JsonSettings.Load<SettingsBag>(f.FileName).EnableAutosave();
            o["a"] = 1;
            o["b"] = 2;
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            o.RemoveWhere(kv => true).Should().Be(2);

            saves.Should().Be(1, "a batch removal commits a single save");
            JsonSettings.Load<SettingsBag>(f.FileName).Data.Count.Should().Be(0);
        }

        [TestMethod]
        public void RemoveWhere_NoMatch_DoesNotSave() {
            using var f = new TempFile();
            var o = JsonSettings.Load<SettingsBag>(f.FileName).EnableAutosave();
            o["a"] = 1;
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            o.RemoveWhere(kv => false).Should().Be(0);

            saves.Should().Be(0, "removing nothing must not save");
        }

        // ---- Coverage: toggling Autosave off stops saving --------------------------------------

        [TestMethod]
        public void TogglingAutosaveOff_StopsSaving() {
            using var f = new TempFile();
            var o = JsonSettings.Load<SettingsBag>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            o["a"] = 1;
            saves.Should().Be(1);

            o.Autosave = false;
            o["b"] = 2;
            saves.Should().Be(1, "no save after autosave is turned off");
        }

        // ---- FIXED: nested suspension on the bag batched into one save --------------------------

        [TestMethod]
        public void NestedSuspend_OnBag_BatchesIntoOneSave() {
            using var f = new TempFile();
            var o = JsonSettings.Load<SettingsBag>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            using (o.SuspendAutosave()) {
                using (o.SuspendAutosave()) {
                    o["a"] = 1;
                }
                saves.Should().Be(0, "the inner scope closing must not end suspension");
                o["b"] = 2;
                saves.Should().Be(0);
            }

            saves.Should().Be(1, "one batched save on the outermost close");
            var reloaded = JsonSettings.Load<SettingsBag>(f.FileName);
            reloaded["a"].Should().Be(1L);
            reloaded["b"].Should().Be(2L);
        }

        // ---- FIXED: EnableAutosave() via a base-typed reference threw --------------------------

        /// <summary>
        ///     Calling the <c>EnableAutosave()</c> extension on a <see cref="JsonSettings"/>-typed
        ///     reference to a bag must enable the bag's own autosave, the same as calling the
        ///     instance method on a SettingsBag-typed reference.
        /// </summary>
        /// <remarks>
        ///     The instance method hides the extension only when the static type is SettingsBag; a
        ///     base-typed reference resolved to the extension, which tried to validate weaving and
        ///     threw. The extension now routes SettingsBag to its dictionary-backed autosave.
        /// </remarks>
        [TestMethod]
        public void EnableAutosave_ExtensionOnBaseTypedReference_EnablesBagAutosave() {
            using var f = new TempFile();
            JsonSettings settings = JsonSettings.Load<SettingsBag>(f.FileName);

            settings.EnableAutosave();   // extension, not the instance method

            ((SettingsBag) settings).Autosave.Should().BeTrue();
            var saves = 0;
            settings.AfterSave += (s, d) => saves++;
            ((SettingsBag) settings)["k"] = "v";
            saves.Should().Be(1, "the routed bag autosave persists writes");
            JsonSettings.Load<SettingsBag>(f.FileName)["k"].Should().Be("v");
        }

        // ---- Coverage: dynamic access autosaves ------------------------------------------------

        [TestMethod]
        public void Dynamic_MemberAndIndexWrites_Autosave() {
            using var f = new TempFile();
            var bag = JsonSettings.Load<SettingsBag>(f.FileName).EnableAutosave();
            var saves = 0;
            bag.AfterSave += (s, d) => saves++;

            dynamic d = bag.AsDynamic();
            d.Member = "m";
            saves.Should().Be(1, "a dynamic member write goes through the bag and autosaves");
            d["Index"] = "i";
            saves.Should().Be(2, "a dynamic index write autosaves too");

            var reloaded = JsonSettings.Load<SettingsBag>(f.FileName);
            reloaded["Member"].Should().Be("m");
            reloaded["Index"].Should().Be("i");
        }

        // ---- FIXED: using a disposed dynamic bag surfaced a bare NullReferenceException ---------

        [TestMethod]
        public void Dynamic_UsedAfterDispose_ThrowsObjectDisposed() {
            using var f = new TempFile();
            var bag = JsonSettings.Load<SettingsBag>(f.FileName);
            dynamic d = bag.AsDynamic();
            ((IDisposable) d).Dispose();

            new Action(() => { d.X = "y"; }).Should().Throw<ObjectDisposedException>();
        }
    }
}
