using System.IO;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings;


namespace Nucs.JsonSettings.Tests {
    [TestClass]
    public class CreatingAndOverridingTests {
        [TestMethod]
        public void OverrideExistingBySmallerSettingsFile() {
            var path = JsonSettings.ResolvePath(new SettingsLarger(), "swaggy.json", true);
            using (var f = new TempfileLife(path)) {
                if (File.Exists(f))
                    File.Delete(f);
                File.Exists(f).Should().BeFalse();

                var o = JsonSettings.Load<SettingsLarger>(f);
                o.Str = o.Str2 = o.Str3 = "lol";
                o.Save();

                File.Exists(f).Should().BeTrue();
                var o2 = JsonSettings.Load<Settings>(f);
                o2.Str.ShouldBeEquivalentTo("lol");
            }
        }

        [TestMethod]
        public void CreateNonExistingSettings() {
            var path = JsonSettings.ResolvePath(new Settings(), "swag.json", true);
            using (var f = new TempfileLife(path)) {
                if (File.Exists(f))
                    File.Delete(f);
                File.Exists(f).Should().BeFalse();
                var o = JsonSettings.Load<Settings>(f);
                o.Str = "lol";
                File.Exists(f).Should().BeTrue();
                o.Save();
                File.Exists(f).Should().BeTrue();
            }
        }

        class Settings : JsonSettings {
            #region Overrides of JsonSettings

            /// <summary>
            ///     Serves as a reminder where to save or from where to load (if it is loaded on construction and doesnt change between constructions).<br></br>
            ///     Can be relative to executing file's directory.
            /// </summary>
            public override string FileName { get; set; } = "swag.json";

            #endregion

            public string Str { get; set; }
        }

        class SettingsLarger : JsonSettings {
            #region Overrides of JsonSettings

            /// <summary>
            ///     Serves as a reminder where to save or from where to load (if it is loaded on construction and doesnt change between constructions).<br></br>
            ///     Can be relative to executing file's directory.
            /// </summary>
            public override string FileName { get; set; } = "swag.json";

            #endregion

            public string Str { get; set; }
            public string Str2 { get; set; }
            public string Str3 { get; set; }
        }
    }
}