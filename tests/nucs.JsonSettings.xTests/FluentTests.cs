using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using FluentAssertions;
using nucs.JsonSettings.Fluent;
using nucs.JsonSettings.xTests.Utils;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace nucs.JsonSettings.xTests {
    public class FluentTests {
        private readonly ITestOutputHelper output;

        public FluentTests(ITestOutputHelper output) {
            this.output = output;
        }

        [Fact]
        public void Fluent_WithFileNameAndEncryptionAndAutosave() {
            using (var f = new TempfileLife()) {
                Func<SettingsBag> gen = () => new SettingsBag().WithFileName((string) f).WithEncryption("qweqwe").LoadNow().EnableAutosave();
                var o = gen();
                o["lol"] = "xoxo";
                o["loly"] = 2;
                var x = gen();
                x["lol"].ShouldBeEquivalentTo("xoxo");
                x["loly"].ShouldBeEquivalentTo(2);
            }
        }

        [Fact]
        public void Fluent_WithBas64() {
            using (var f = new TempfileLife()) {
                Func<SettingsBag> gen = () => new SettingsBag().WithFileName((string) f).WithBase64().LoadNow().EnableAutosave();
                var o = gen();
                o["lol"] = "xoxo";
                o["loly"] = 2;
                var x = gen();
                x["lol"].ShouldBeEquivalentTo("xoxo");
                x["loly"].ShouldBeEquivalentTo(2);
            }
        }

        [Fact]
        public void Fluent_WithEncryptionAndWithBas64() {
            using (var f = new TempfileLife()) {
                Func<SettingsBag> gen = () => new SettingsBag().WithFileName((string) f).WithEncryption("qweqwe").WithBase64().LoadNow().EnableAutosave();
                var o = gen();
                o["lol"] = "xoxo";
                o["loly"] = 2;
                var x = gen();
                x["lol"].ShouldBeEquivalentTo("xoxo");
                x["loly"].ShouldBeEquivalentTo(2);
            }
        }

        [Fact]
        public void Fluent_WithhBas64AndEncryption() {
            using (var f = new TempfileLife()) {
                Func<SettingsBag> gen = () => new SettingsBag().WithFileName((string) f).WithBase64().WithEncryption("qweqwe").LoadNow().EnableAutosave();
                var o = gen();
                o["lol"] = "xoxo";
                o["loly"] = 2;
                var x = gen();
                x["lol"].ShouldBeEquivalentTo("xoxo");
                x["loly"].ShouldBeEquivalentTo(2);
            }
        }

        [Fact]
        public void Fluent_SimpleLoad() {
            using (var f = new TempfileLife()) {
                Func<SettingsBag> gen = () => new SettingsBag().WithFileName((string) f).LoadNow().EnableAutosave();
                var o = gen();
                o["lol"] = "xoxo";
                o["loly"] = 2;
                var x = gen();
                x["lol"].ShouldBeEquivalentTo("xoxo");
                x["loly"].ShouldBeEquivalentTo(2);
            }
        }

        [Fact]
        public void Fluent_SimpleSave() {
            using (var f = new TempfileLife()) {
                Func<SettingsBag> gen = () => new SettingsBag().WithFileName((string) f).LoadNow();
                var o = gen();
                o["lol"] = "xoxo";
                o["loly"] = 2;
                o.Save();
            }
        }

        [Fact]
        public void Fluent_SavingWithBase64_LoadingWithout() {
            using (var f = new TempfileLife()) {
                //used for autodelete file after test ends
                var o = JsonSettings.Configure<CasualExampleSettings>(f.FileName).WithBase64().LoadNow();
                o.SomeNumeralProperty = 1;
                o.SomeProperty = "with some value";
                o.SomeClassProperty = new SmallClass() {Name = "Small", Value = "Class"};
                o.Save();

                //validate
                Assert.Throws<JsonReaderException>(() => { o = JsonSettings.Configure<CasualExampleSettings>(f.FileName).LoadNow(); });
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
        public void Fluent_ConstructorFileNameComparison() {
            using (var f = new TempfileLife()) {
                var o = JsonSettings.Configure<CasualExampleSettings>(f.FileName).WithBase64().WithEncryption("SuperPassword").LoadNow();

                //validate
                o.FileName.Should().EndWith(f.FileName).And.Contain("\\");
                output.WriteLine($"{f.FileName} -> {o.FileName}");
            }
        }
        [Fact]
        public void Fluent_ConstructorFileNameVsWithFilenameComparison() {
            using (var f = new TempfileLife()) {
                var o = JsonSettings.Configure<CasualExampleSettings>(f.FileName).WithBase64().WithEncryption("SuperPassword").LoadNow();
                var n = JsonSettings.Configure<CasualExampleSettings>().WithFileName(f.FileName).WithBase64().WithEncryption("SuperPassword").LoadNow();

                //validate
                o.FileName.Should().Be(n.FileName);
                output.WriteLine($"{o.FileName} <-> {n.FileName}");
            }
        }
        [Fact]
        public void Fluent_PostSaveFilenameComparison() {
            using (var f = new TempfileLife()) {
                var o = JsonSettings.Configure<CasualExampleSettings>(f.FileName).WithBase64().WithEncryption("SuperPassword").LoadNow();
                var n = JsonSettings.Configure<CasualExampleSettings>().WithFileName(f.FileName).WithBase64().WithEncryption("SuperPassword").LoadNow();
                //validate
                o.FileName.Should().Be(n.FileName);
                o.Save();
                o.FileName.Should().Be(n.FileName);
                n.Save();
                o.FileName.Should().Be(n.FileName);

                o.FileName.Should().EndWith(f.FileName);
                n.FileName.Should().EndWith(f.FileName);
                output.WriteLine($"{o.FileName} <-> {n.FileName}");
            }
        }
        [Fact]
        public void Fluent_WithFileName() {
            using (var f = new TempfileLife()) {
                var o = JsonSettings.Configure<CasualExampleSettings>().WithFileName(f.FileName).WithBase64().WithEncryption("SuperPassword").LoadNow();

                //validate
                o.FileName.Should().EndWith(f.FileName);
                output.WriteLine($"{f.FileName} -> {o.FileName}");
            }
        }

        [Fact]
        public void JsonSettings_FileNameIsNullByDefault() {
            Assert.Throws<JsonSettingsException>(() => { JsonSettings.Load<FilenamelessSettings>(); });
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
            public EncryptedFilenamelessSettings(string password, string fileName = "<DEFAULT>") : base(password, fileName) { }
            public EncryptedFilenamelessSettings(SecureString password) : base(password) { }
            public EncryptedFilenamelessSettings(SecureString password, string fileName = "<DEFAULT>") : base(password, fileName) { }
        }

        public class MySettings : JsonSettings {
            public override string FileName { get; set; } = "TheDefaultFilename"; //for loading and saving.

            #region Settings

            public string SomeProperty { get; set; }
            public int SomeNumberWithDefaultValue { get; set; } = 1;

            #endregion

            public MySettings() { }
            public MySettings(string fileName) : base(fileName) { }
        }
    }
}