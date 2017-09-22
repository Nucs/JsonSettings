using System;
using System.IO;
using FluentAssertions;
using nucs.JsonSettings.xTests.Utils;
using Xunit;
using Xunit.Sdk;

namespace nucs.JsonSettings.xTests {
    public class Tests {
        [Fact]
        public void EncryptedSettingsBag_Autosave() {
            using (var f = new TempfileLife()) {
                var o = EncryptedJsonSettings.Load<EncryptedSettingsBag>("swag", f);
                o.Autosave = true;
                o["lol"] = "xoxo";
                o["loly"] = 2;
                var x = EncryptedJsonSettings.Load<EncryptedSettingsBag>("swag", f);
                x["lol"].ShouldBeEquivalentTo("xoxo");
                x["loly"].ShouldBeEquivalentTo(2);
            }
        }

        [Fact]
        public void EncryptedSettingsBag_RegularSave() {
            using (var f = new TempfileLife()) {
                var o = EncryptedJsonSettings.Load<EncryptedSettingsBag>("swag", f);
                o.Autosave = false;
                o["lol"] = "xoxo";
                o["loly"] = 2;
                o.Save();
                var x = EncryptedJsonSettings.Load<EncryptedSettingsBag>("swag", f);
                x["lol"].ShouldBeEquivalentTo("xoxo");
                x["loly"].ShouldBeEquivalentTo(2);
            }
        }

        [Fact]
        public void SettingsBag_RegularSave() {
            using (var f = new TempfileLife()) {
                var o = JsonSettings.Load<SettingsBag>(f);
                o.Autosave = false;
                o["lol"] = "xoxo";
                o["loly"] = 2;
                o.Save();
                var x = JsonSettings.Load<SettingsBag>(f);
                x["lol"].ShouldBeEquivalentTo("xoxo");
                x["loly"].ShouldBeEquivalentTo(2);
            }
        }

        [Fact]
        public void SettingsBag_Autosave() {
            using (var f = new TempfileLife()) {
                var o = JsonSettings.Load<SettingsBag>(f);
                o.Autosave = true;
                o["lol"] = "xoxo";
                o["loly"] = 2;
                var x = JsonSettings.Load<SettingsBag>(f);
                x["lol"].ShouldBeEquivalentTo("xoxo");
                x["loly"].ShouldBeEquivalentTo(2);
            }
        }

        [Fact]
        public void FilterFileNameProperty() {
            using (var f = new TempfileLife()) {
                var n = new FilterFileNameSettings(f);
                n.Save();
                File.ReadAllText(n.FileName).IndexOf("FileName", StringComparison.OrdinalIgnoreCase).ShouldBeEquivalentTo(-1);
            }
        }

        class FilterFileNameSettings : JsonSettings {
            public override string FileName { get; set; }
            public FilterFileNameSettings() { }
            public FilterFileNameSettings(string fileName) : base(fileName) { }
        }
    }
}