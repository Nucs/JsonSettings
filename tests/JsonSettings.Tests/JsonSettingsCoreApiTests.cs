using System;
using System.IO;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests {
    /// <summary>
    ///     Coverage for the many static/instance <see cref="JsonSettings"/> entry-point overloads and
    ///     their guard clauses that the feature-focused suites never call directly: the args-carrying
    ///     Load overloads, the typed/boxed Save overloads and their argument validation, the
    ///     no-default-constructor guard, <c>LoadDefault</c>, the null-argument guards and
    ///     <c>Construct(Type, ...)</c>.
    /// </summary>
    [TestClass]
    public class JsonSettingsCoreApiTests {
        // ---- Construction guard ---------------------------------------------------------------

        [TestMethod]
        public void SettingsWithoutDefaultConstructor_Throws() {
            //The base constructor rejects a settings type that has no parameterless constructor at all,
            //because the framework must be able to build a default instance (for LoadDefault etc.).
            new Action(() => new NoDefaultCtorSettings(1))
                .Should().Throw<JsonSettingsException>().WithMessage("*empty public constructor*");
        }

        // ---- Save overloads -------------------------------------------------------------------

        [TestMethod]
        public void SaveGeneric_WritesFile() {
            using var f = new TempFile();
            var s = JsonSettings.Load<ArgSettings>(f.FileName);
            s.Tag = "typed-save";
            JsonSettings.Save(s, f.FileName);

            JsonSettings.Load<ArgSettings>(f.FileName).Tag.Should().Be("typed-save");
        }

        [TestMethod]
        public void SaveBoxed_NonJsonSettings_Throws() {
            using var f = new TempFile();
            new Action(() => JsonSettings.Save(typeof(string), "not a settings object", f.FileName))
                .Should().Throw<ArgumentException>();
        }

        [TestMethod]
        public void SaveBoxed_EmptyFileName_Throws() {
            var s = JsonSettings.Construct<ArgSettings>();
            new Action(() => JsonSettings.Save(typeof(ArgSettings), s, ""))
                .Should().Throw<ArgumentException>();
        }

        // ---- Load guards ----------------------------------------------------------------------

        [TestMethod]
        public void InstanceLoad_NullFileName_Throws() {
            var s = JsonSettings.Construct<ArgSettings>();
            new Action(() => s.Load((string) null!)).Should().Throw<ArgumentNullException>();
        }

        [TestMethod]
        public void StaticLoad_NullInstance_Throws() {
            new Action(() => JsonSettings.Load((object) null!, null))
                .Should().Throw<ArgumentNullException>();
        }

        // ---- LoadDefault ----------------------------------------------------------------------

        [TestMethod]
        public void LoadDefault_ResetsToDefaults() {
            using var f = new TempFile();
            var s = JsonSettings.Load<ArgSettings>(f.FileName);
            s.Tag = "mutated";

            s.LoadDefault();
            s.Tag.Should().BeNull("LoadDefault repopulates from a freshly constructed instance");
        }

        [TestMethod]
        public void LoadDefaultGeneric_ResetsToDefaults() {
            using var f = new TempFile();
            var s = JsonSettings.Load<ArgSettings>(f.FileName);
            s.Tag = "mutated";

            s.LoadDefault<ArgSettings>();
            s.Tag.Should().BeNull();
        }

        // ---- Instance Load with configure -----------------------------------------------------

        [TestMethod]
        public void InstanceLoad_WithConfigure_RunsConfigure() {
            using var f = new TempFile();
            var s = JsonSettings.Construct<ArgSettings>();
            var configured = false;
            s.Load(f.FileName, _ => configured = true);
            configured.Should().BeTrue();
            s.FileName.Should().EndWith(Path.GetFileName(f.FileName));
        }

        // ---- Static Load overloads carrying constructor args ----------------------------------

        [TestMethod]
        public void LoadGeneric_WithArgs_ConstructsAndLoads() {
            using var f = new TempFile();
            var s = JsonSettings.Load<ArgSettings>(new object[] { f.FileName });
            s.Should().BeOfType<ArgSettings>();
            s.FileName.Should().EndWith(Path.GetFileName(f.FileName));
        }

        [TestMethod]
        public void LoadType_WithArgs_ConstructsAndLoads() {
            using var f = new TempFile();
            var s = JsonSettings.Load(typeof(ArgSettings), new object[] { f.FileName });
            s.Should().BeOfType<ArgSettings>();
        }

        [TestMethod]
        public void LoadType_WithFileNameAndArgs_ConstructsAndLoads() {
            using var f = new TempFile();
            var s = JsonSettings.Load(typeof(ArgSettings), f.FileName, new object[] { f.FileName });
            s.Should().BeOfType<ArgSettings>();
        }

        [TestMethod]
        public void LoadGeneric_WithConfigureAndFileName_RunsConfigure() {
            using var f = new TempFile();
            var s = JsonSettings.Load<ArgSettings>(cfg => cfg.Tag = "cfg", f.FileName);
            s.Should().BeOfType<ArgSettings>();
            s.Tag.Should().Be("cfg");
        }

        [TestMethod]
        public void LoadType_WithConfigureFileNameAndArgs_Loads() {
            using var f = new TempFile();
            var configured = false;
            var s = JsonSettings.Load(typeof(ArgSettings), () => configured = true, f.FileName, new object[] { f.FileName });
            s.Should().BeOfType<ArgSettings>();
            configured.Should().BeTrue();
        }

        // ---- Construct(Type, ...) -------------------------------------------------------------

        [TestMethod]
        public void ConstructType_ValidType_ReturnsInstance() {
            var s = JsonSettings.Construct(typeof(ArgSettings));
            s.Should().BeOfType<ArgSettings>();
        }

        [TestMethod]
        public void ConstructType_NonSavableType_Throws() {
            new Action(() => JsonSettings.Construct(typeof(string)))
                .Should().Throw<ArgumentException>().WithMessage("*ISavable*");
        }

        // ---- ResolvePath ----------------------------------------------------------------------

        [TestMethod]
        public void ResolvePath_EmptyOrNull_Throws() {
            new Action(() => JsonSettings.ResolvePath("")).Should().Throw<JsonSettingsException>();
            new Action(() => JsonSettings.ResolvePath((string) null!)).Should().Throw<JsonSettingsException>();
        }

        // ---- helpers --------------------------------------------------------------------------

        public class ArgSettings : JsonSettings {
            public override string FileName { get; set; }
            public string Tag { get; set; }
            public ArgSettings() { }
            public ArgSettings(string fileName) : base(fileName) { }
        }

        public class NoDefaultCtorSettings : JsonSettings {
            public override string FileName { get; set; }
            //Deliberately no parameterless constructor.
            public NoDefaultCtorSettings(int required) { _ = required; }
        }
    }
}
