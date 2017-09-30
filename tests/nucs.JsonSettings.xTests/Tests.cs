using System;
using System.IO;
using System.Security;
using FluentAssertions;
using nucs.JsonSettings.Fluent;
using nucs.JsonSettings.Modulation;
using nucs.JsonSettings.xTests.Utils;
using Xunit;
using Xunit.Sdk;

namespace nucs.JsonSettings.xTests {
    public class Tests {
        [Fact]
        public void SettingsBag_WithEncryption_Autosave() {
            using (var f = new TempfileLife()) {
                var o = JsonSettings.Configure<SettingsBag>().WithEncryption("swag").WithFileName(f.FileName).LoadNow().EnableAutosave();
                o.Autosave = true;
                o["lol"] = "xoxo";
                o["loly"] = 2;
                var x = JsonSettings.Configure<SettingsBag>().WithEncryption("swag").WithFileName(f.FileName).LoadNow();
                x["lol"].ShouldBeEquivalentTo("xoxo");
                x["loly"].ShouldBeEquivalentTo(2);
            }
        }

        [Fact]
        public void SettingsBag_WithEncryption_RegularSave() {
            using (var f = new TempfileLife()) {
                var o = JsonSettings.Configure<SettingsBag>().WithEncryption("swag").WithFileName(f.FileName).LoadNow();
                o.Autosave = false;
                o["lol"] = "xoxo";
                o["loly"] = 2;
                o.Save();
                var x = JsonSettings.Configure<SettingsBag>().WithEncryption("swag").WithFileName(f.FileName).LoadNow();
                x["lol"].ShouldBeEquivalentTo("xoxo");
                x["loly"].ShouldBeEquivalentTo(2);
            }
        }

        [Fact]
        public void SettingsBag_Passless() {
            using (var f = new TempfileLife()) {
                var o = JsonSettings.Configure<SettingsBag>().WithEncryption((string)null).WithFileName(f.FileName).LoadNow();
                ((RijndaelModule) o.Modulation.Modules[0]).Password.ShouldBeEquivalentTo(SecureStringExt.EmptyString);
                o.Autosave = false;
                o["lol"] = "xoxo";
                o["loly"] = 2;
                o.Save();
                var x = JsonSettings.Configure<SettingsBag>().WithEncryption((string)null).WithFileName(f.FileName).LoadNow();
                x["lol"].ShouldBeEquivalentTo("xoxo");
                x["loly"].ShouldBeEquivalentTo(2);
            }
        }

        [Fact]
        public void SettingsBag_InvalidPassword() {
            using (var f = new TempfileLife()) {
                var o = JsonSettings.Configure<SettingsBag>().WithEncryption("yoyo").WithFileName(f.FileName).LoadNow();
                o["lol"] = "xoxo";
                o["loly"] = 2;
                o.Save();
                Action func = () => JsonSettings.Configure<SettingsBag>().WithEncryption("invalidpass").WithFileName(f.FileName).LoadNow();
                func.ShouldThrow<JsonSettingsException>("Password is invalid").Where(e => e.Message.StartsWith("Password", StringComparison.OrdinalIgnoreCase));
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

        [Fact]
        public void JsonSettings_FileNameIsNullByDefault() {
            Assert.Throws<JsonSettingsException>(() => { JsonSettings.Load<FilenamelessSettings>(); });
        }

        [Fact]
        public void JsonSettings_ModuleLoader() {
            using (var f = new TempfileLife()) {
                var o = JsonSettings.Load<ModuleLoadingSttings>(f);
                o.someprop = "1";
                o.Save();
                var x = JsonSettings.Load<ModuleLoadingSttings>(f);
                x.someprop.ShouldBeEquivalentTo("1");
            }
        }

        class FilterFileNameSettings : JsonSettings {
            public override string FileName { get; set; }
            public FilterFileNameSettings() { }
            public FilterFileNameSettings(string fileName) : base(fileName) { }
        }

        class ModuleLoadingSttings : JsonSettings {
            public override string FileName { get; set; }
            public string someprop { get; set; }
            public ModuleLoadingSttings() { }
            public ModuleLoadingSttings(string fileName) : base(fileName) { }
        }

        class FilenamelessSettings : JsonSettings {
            public override string FileName { get; set; } = null;
            public string someprop { get; set; }

            public FilenamelessSettings() { }
            public FilenamelessSettings(string fileName) : base(fileName) { }
        }


        public class MySettings : JsonSettings {
            public override string FileName { get; set; } = "TheDefaultFilename"; //for loading and saving.

            #region Settings

            public string SomeProperty { get; set; }
            public int SomeNumberWithDefaultValue { get; set; } = 1;

            #endregion

            public MySettings() { }
            public MySettings(string fileName) : base(fileName) { }

            public void test() {
                var settings1 = JsonSettings.Load<MySettings>(); //Load from FileName value in a newly constructed MySettings.
                var settings = JsonSettings.Load<MySettings>("C:/folder/somefile.extension"); //Load from specific location.
                settings.SomeProperty = "somevalue";
                settings.Save();
            }
        }

    }
}