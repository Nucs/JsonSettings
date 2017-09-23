using System;
using System.IO;
using System.Security;
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
        public void EncryptedSettingsBag_Passless() {
            using (var f = new TempfileLife()) {
                var o = EncryptedJsonSettings.Load<EncryptedSettingsBag>((string) null, f);
                o.Password.ShouldBeEquivalentTo(EncryptedSettingsBag.EmptyString);
                o.Autosave = false;
                o["lol"] = "xoxo";
                o["loly"] = 2;
                o.Save();
                var x = EncryptedJsonSettings.Load<EncryptedSettingsBag>((string) null, f);
                x["lol"].ShouldBeEquivalentTo("xoxo");
                x["loly"].ShouldBeEquivalentTo(2);
            }
        }

        [Fact]
        public void EncryptedSettingsBag_InvalidPassword() {
            using (var f = new TempfileLife()) {
                var o = EncryptedJsonSettings.Load<EncryptedSettingsBag>("Yo", f);
                o.Autosave = false;
                o["lol"] = "xoxo";
                o["loly"] = 2;
                o.Save();
                Action func = () => EncryptedJsonSettings.Load<EncryptedSettingsBag>("Yo2", f);
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
        public void EncryptedJsonSettings_FileNameIsNullByDefault() {
            Assert.Throws<JsonSettingsException>(() => { EncryptedJsonSettings.Load<EncryptedFilenamelessSettings>("password", "##DEFAULT##"); });
            Assert.Throws<JsonSettingsException>(() => { EncryptedJsonSettings.Load<EncryptedFilenamelessSettings>("password"); });
        }

        class FilterFileNameSettings : JsonSettings {
            public override string FileName { get; set; }
            public FilterFileNameSettings() { }
            public FilterFileNameSettings(string fileName) : base(fileName) { }
        }

        class FilenamelessSettings : JsonSettings {
            public override string FileName { get; set; } = null;
            public string someprop { get; set; }

            public FilenamelessSettings() { }
            public FilenamelessSettings(string fileName) : base(fileName) { }
        }

        class EncryptedFilenamelessSettings : EncryptedJsonSettings {
            public override string FileName { get; set; } = null;
            public string someprop { get; set; }
            public EncryptedFilenamelessSettings() { }
            public EncryptedFilenamelessSettings(string password) : base(password) { }
            public EncryptedFilenamelessSettings(string password, string fileName = "##DEFAULT##") : base(password, fileName) { }
            public EncryptedFilenamelessSettings(SecureString password) : base(password) { }
            public EncryptedFilenamelessSettings(SecureString password, string fileName = "##DEFAULT##") : base(password, fileName) { }
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

class MyEncryptedSettings : EncryptedJsonSettings {
    public override string FileName { get; set; } = "TheDefaultFilename"; //for loading and saving.

    #region Settings

    public string SomeProperty { get; set; }
    public int SomeNumberWithDefaultValue { get; set; } = 1;

    #endregion

    public MyEncryptedSettings() { }
    public MyEncryptedSettings(string password) : base(password) { }
    public MyEncryptedSettings(string password, string fileName = "##DEFAULT##") : base(password, fileName) { }
    public MyEncryptedSettings(SecureString password) : base(password) { }
    public MyEncryptedSettings(SecureString password, string fileName = "##DEFAULT##") : base(password, fileName) { }

    public void test()
    {
var settings1 = EncryptedJsonSettings.Load<EncryptedSettingsBag>("password").EnableAutosave();
var settings = JsonSettings.Load<SettingsBag>().EnableAutosave();
settings["key"] = 123; //Already saved to file.
        if ((int?) settings["key"] != 123) ;
        //do something
    }
        }
    }
}