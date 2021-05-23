
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Tests.Autosave;

[TestClass]
public class AutosaveSuspensionTests {
    [TestMethod]
    public void Case1() {
        using (var f = new TempfileLife()) {
            StrongBox<bool> saved = new StrongBox<bool>(false);
            var o = AutosaveTests.Settings.Load<AutosaveTests.Settings>(f.FileName)
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
            }

            saved.Value.ShouldBeEquivalentTo(true);
            //test
        }
    }

    [TestMethod]
    public void Case2() {
        using (var f = new TempfileLife()) {
            StrongBox<bool> saved = new StrongBox<bool>(false);
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
    }
}