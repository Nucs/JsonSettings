using System;
using System.IO;
using System.Security;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Tests.Utils;


namespace Nucs.JsonSettings.Tests {
    [TestClass]
    public class Tests {
        [TestMethod]
        public void SettingsBag_WithEncryption_Autosave() {
            using var f = new CreateTempFile();
            var o = JsonSettings.Configure<SettingsBag>().WithEncryption("swag").WithFileName(f.FileName).LoadNow().EnableAutosave();
            o.Autosave = true;
            o["lol"] = "xoxo";
            o["loly"] = 2;
            var x = JsonSettings.Configure<SettingsBag>().WithEncryption("swag").WithFileName(f.FileName).LoadNow();
            x["lol"].Should().Be("xoxo");
            x["loly"].Should().Be(2);
        }

        [TestMethod]
        public void SettingsBag_WithEncryption_RegularSave() {
            using var f = new CreateTempFile();
            var o = JsonSettings.Configure<SettingsBag>().WithEncryption("swag").WithFileName(f.FileName).LoadNow();
            o.Autosave = false;
            o["lol"] = "xoxo";
            o["loly"] = 2;
            o.Save();
            var x = JsonSettings.Configure<SettingsBag>().WithEncryption("swag").WithFileName(f.FileName).LoadNow();
            x["lol"].Should().Be("xoxo");
            x["loly"].Should().Be(2);
        }

        [TestMethod]
        public void SettingsBag_Passless() {
            using var f = new CreateTempFile();
            var o = JsonSettings.Configure<SettingsBag>().WithEncryption((string)null).WithFileName(f.FileName).LoadNow();
            ((RijndaelModule) o.Modulation.Modules[0]).Password.Should().BeEquivalentTo(SecureStringExt.EmptyString);
            o.Autosave = false;
            o["lol"] = "xoxo";
            o["loly"] = 2;
            o.Save();
            var x = JsonSettings.Configure<SettingsBag>().WithEncryption((string)null).WithFileName(f.FileName).LoadNow();
            x["lol"].Should().Be("xoxo");
            x["loly"].Should().Be(2);
        }

        [TestMethod]
        public void SettingsBag_InvalidPassword() {
            using var f = new CreateTempFile();
            var o = JsonSettings.Configure<SettingsBag>().WithEncryption("yoyo").WithFileName(f.FileName).LoadNow();
            o["lol"] = "xoxo";
            o["loly"] = 2;
            o.Save();
            Action func = () => JsonSettings.Configure<SettingsBag>().WithEncryption("invalidpass").WithFileName(f.FileName).LoadNow();
            func.Should().Throw<JsonSettingsException>("Password is invalid").Where(e => e.Message.StartsWith("Password", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void SettingsBag_RegularSave() {
            using var f = new CreateTempFile();
            var o = JsonSettings.Load<SettingsBag>(f);
            o.Autosave = false;
            o["lol"] = "xoxo";
            o["loly"] = 2;
            o.Save();
            var x = JsonSettings.Load<SettingsBag>(f);
            x["lol"].Should().Be("xoxo");
            x["loly"].Should().Be(2);
        }

        [TestMethod]
        public void SettingsBag_Autosave() {
            using var f = new CreateTempFile();
            var o = JsonSettings.Load<SettingsBag>(f);
            o.Autosave = true;
            o["lol"] = "xoxo";
            o["loly"] = 2;
            var x = JsonSettings.Load<SettingsBag>(f);
            x["lol"].Should().Be("xoxo");
            x["loly"].Should().Be(2);
        }

        [TestMethod]
        public void FilterFileNameProperty() {
            using var f = new CreateTempFile();
            var n = new FilterFileNameSettings(f);
            n.Save();
            File.ReadAllText(n.FileName).IndexOf("FileName", StringComparison.OrdinalIgnoreCase).Should().Be(-1);
        }

        [TestMethod]
        public void JsonSettings_FileNameIsNullByDefault() {
            new Action(() => { JsonSettings.Load<FilenamelessSettings>(); }).Should().Throw<JsonSettingsException>();
        }

        [TestMethod]
        public void JsonSettings_ModuleLoader() {
            using var f = new CreateTempFile();
            var o = JsonSettings.Load<ModuleLoadingSttings>(f);
            o.someprop = "1";
            o.Save();
            var x = JsonSettings.Load<ModuleLoadingSttings>(f);
            x.someprop.Should().Be("1");
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