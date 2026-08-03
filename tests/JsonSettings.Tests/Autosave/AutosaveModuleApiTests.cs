using System.IO;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Autosave {
    /// <summary>
    ///     Direct coverage for <see cref="AutosaveModule.TryTriggerSave"/>, the public entry point a
    ///     suspension's fallback uses to force a save through the module's (weak) socket. The round-trip
    ///     tests always go through a woven setter, so the method itself was never called directly.
    /// </summary>
    [TestClass]
    public class AutosaveModuleApiTests {
        [TestMethod]
        public void TryTriggerSave_PersistsTheCurrentStateThroughTheSocket() {
            using var f = new TempFile();
            var o = JsonSettings.Load<TriggerSettings>(f.FileName).EnableAutosave();
            o.Text = "persisted"; //autosaves as usual

            //Remove the file behind the library's back, then force a save straight through the module.
            File.Delete(f.FileName);
            var module = o.Modulation.GetModule<AutosaveModule>();

            module.TryTriggerSave();

            File.Exists(f.FileName).Should().BeTrue("TryTriggerSave resolves the live socket and calls Save");
            JsonSettings.Load<TriggerSettings>(f.FileName).Text
                        .Should().Be("persisted", "and the save wrote the instance's current state");
        }

        [Autosave]
        public class TriggerSettings : JsonSettings {
            public override string FileName { get; set; }
            public string Text { get; set; }
            public TriggerSettings() { }
            public TriggerSettings(string fileName) : base(fileName) { }
        }
    }
}
