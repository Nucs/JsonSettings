using System;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Autosave;

[TestClass]
public class AutosaveSuspensionTests {
    [TestMethod]
    public void Case1() {
        using var f = new CreateTempFile();
        var saved = new StrongBox<bool>(false);
        var o = JsonSettings.Load<AutosaveTests.Settings>(f.FileName)
                            .EnableAutosave();
        o.AfterSave += (s, destinition) => { saved.Value = true; };
        var module = o.Modulation.GetModule<AutosaveModule>();

        //act
        module.AutosavingState.Should().Be(AutosavingState.Running);

        using (o.SuspendAutosave()) {
            module.AutosavingState.Should().Be(AutosavingState.Suspended);

            saved.Value.ShouldBeEquivalentTo(false);
            o.property = "hi";
            saved.Value.ShouldBeEquivalentTo(false);
            module.AutosavingState.Should().Be(AutosavingState.SuspendedChanged);
            var oo = JsonSettings.Load<AutosaveTests.Settings>(f.FileName);
            oo.property.Should().NotBe("hi", "It should not have saved.");
        }

        saved.Value.ShouldBeEquivalentTo(true);
        //test

        o = JsonSettings.Load<AutosaveTests.Settings>(f.FileName);

        o.property.Should().Be("hi", "It should not have saved.");
    }

    [TestMethod]
    public void Case2() {
        using var f = new CreateTempFile();
        var saved = new StrongBox<bool>(false);
        var o = JsonSettings.Load<AutosaveTests.Settings>(f.FileName)
                            .EnableAutosave();
        o.AfterSave += (s, destinition) => { saved.Value = true; };
        var module = o.Modulation.GetModule<AutosaveModule>();

        //act
        module.AutosavingState.Should().Be(AutosavingState.Running);

        var suspender = o.SuspendAutosave();
        module.AutosavingState.Should().Be(AutosavingState.Suspended);

        saved.Value.ShouldBeEquivalentTo(false);
        o.property = "hi";
        saved.Value.ShouldBeEquivalentTo(false);
        module.AutosavingState.Should().Be(AutosavingState.SuspendedChanged);
        suspender.Resume();

        //resuming/disposing twice should have any effect
        saved.Value.ShouldBeEquivalentTo(true);
        saved.Value = false;
        suspender.Resume();
        saved.Value.ShouldBeEquivalentTo(false);
    }

    /// <summary>
    ///     A suspension that owes a save must commit it even if nothing else in the program still
    ///     references the settings instance by the time the scope closes.
    /// </summary>
    /// <remarks>
    ///     SuspendAutosave used to reach the settings only through AutosaveModule's
    ///     WeakReference socket, and a module does not keep its settings alive. A collection
    ///     during the scope therefore dropped the pending write silently -- no exception, no
    ///     partial file, just a save that never happened.
    ///
    ///     The JIT is free to consider a local dead after its last use, so this is reachable from
    ///     ordinary code: in AutosaveTests.SuspendAutosaving_Case1 the settings variable is last
    ///     touched several statements before the scope ends, and that test failed roughly half the
    ///     time once five target frameworks began running their hosts concurrently and raised
    ///     memory pressure. Entering the scope in a frame that returns reproduces the same
    ///     unrooted state deterministically instead of waiting for the GC to lose the race.
    /// </remarks>
    [TestMethod]
    public void CommitsOwedSaveAfterSettingsBecomeUnreachable() {
        using var f = new CreateTempFile();
        var saved = new StrongBox<bool>(false);

        var suspender = OpenSuspensionAndChange(f.FileName, saved);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        saved.Value.ShouldBeEquivalentTo(false);
        suspender.Dispose();
        saved.Value.ShouldBeEquivalentTo(true);

        var reloaded = JsonSettings.Load<AutosaveTests.Settings>(f.FileName);
        reloaded.property.Should().Be("hi", "the write owed by the suspension must survive the collection");
    }

    /// <summary>
    ///     Opens a suspension, dirties the settings, and returns while holding no reference to
    ///     them -- so the instance is provably unrooted for the caller's collection.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SuspendAutosave OpenSuspensionAndChange(string fileName, StrongBox<bool> saved) {
        var o = JsonSettings.Load<AutosaveTests.Settings>(fileName)
                            .EnableAutosave();
        o.AfterSave += (s, destinition) => { saved.Value = true; };

        var suspender = o.SuspendAutosave();
        o.property = "hi";
        return suspender;
    }
}