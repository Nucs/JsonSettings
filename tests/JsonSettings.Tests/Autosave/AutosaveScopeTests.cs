using System;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Autosave {
    /// <summary>
    ///     What the reflection-driven autosave actually affects — proving it does not save on
    ///     writes that come from the framework rather than the user.
    /// </summary>
    /// <remarks>
    ///     Autosave monitors every public settable property, so two categories of write leak in:
    ///     the framework populating the object during a load (every property setter fires), and
    ///     the versioning module writing <c>Version</c>. Neither is a user edit; neither must save.
    /// </remarks>
    [TestClass]
    public class AutosaveScopeTests {

        // ---- FIXED: a load after enable saved once per populated property -----------------------

        /// <summary>
        ///     Reloading an autosave-enabled instance from disk must not save — the writes come
        ///     from the file, not the user.
        /// </summary>
        /// <remarks>
        ///     Load() populates through the woven setters; with a module attached each of those was
        ///     an autosave, so a reload wrote the half-loaded object back to disk once per property.
        ///     LoadJson now suppresses autosave while populating.
        /// </remarks>
        [TestMethod]
        public void Reload_AfterEnable_DoesNotAutosave() {
            using var f = new TempFile();
            var o = JsonSettings.Load<TwoProp>(f.FileName).EnableAutosave();
            o.A = "a";
            o.B = "b";
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            o.Load();   // reload from disk into this instance

            saves.Should().Be(0, "populating from disk is not a user edit");
            o.A.Should().Be("a");
            o.B.Should().Be("b");
        }

        [TestMethod]
        public void LoadDefault_AfterEnable_DoesNotAutosave() {
            using var f = new TempFile();
            var o = JsonSettings.Load<TwoProp>(f.FileName).EnableAutosave();
            o.A = "a";
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            o.LoadDefault();

            saves.Should().Be(0);
        }

        /// <summary>
        ///     Autosave must resume normally after a load — the loading guard has to clear.
        /// </summary>
        [TestMethod]
        public void Autosave_StillWorks_AfterAReload() {
            using var f = new TempFile();
            var o = JsonSettings.Load<TwoProp>(f.FileName).EnableAutosave();
            o.A = "first";
            o.Load();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            o.B = "edited-after-reload";

            saves.Should().Be(1, "the IsLoading guard must clear once the load completes");
            JsonSettings.Load<TwoProp>(f.FileName).B.Should().Be("edited-after-reload");
        }

        // ---- FIXED: IVersionable.Version was monitored ------------------------------------------

        /// <summary>
        ///     Writing <c>Version</c> on an <see cref="IVersionable"/> class must not autosave; it is
        ///     framework metadata the versioning module manages, not a user setting.
        /// </summary>
        [TestMethod]
        public void VersionableVersion_IsNotMonitored() {
            using var f = new TempFile();
            var o = JsonSettings.Configure<Versioned>(f.FileName)
                                .WithVersioning(new Version(1, 0, 0, 0), VersioningResultAction.LoadDefault)
                                .LoadNow()
                                .EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            o.Version = new Version(2, 0, 0, 0);
            saves.Should().Be(0, "Version is framework metadata, not a monitored user property");

            o.Data = "user-edit";
            saves.Should().Be(1, "an ordinary property still autosaves");
        }

        /// <summary>
        ///     A versioned reload must not autosave even when the version normalises.
        /// </summary>
        [TestMethod]
        public void VersionedReload_DoesNotAutosave() {
            using var f = new TempFile();
            var o = JsonSettings.Configure<Versioned>(f.FileName)
                                .WithVersioning(new Version(1, 0, 0, 0), VersioningResultAction.LoadDefault)
                                .LoadNow()
                                .EnableAutosave();
            o.Data = "d";
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            o.Load();

            saves.Should().Be(0);
        }

        /// <summary>
        ///     The Version exclusion is scoped to <see cref="IVersionable"/>: a property coincidentally
        ///     named "Version" on a class that does not implement it is still an ordinary monitored
        ///     property.
        /// </summary>
        [TestMethod]
        public void PropertyNamedVersion_OnNonVersionableClass_IsStillMonitored() {
            using var f = new TempFile();
            var o = JsonSettings.Load<CoincidentalVersion>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            o.Version = "1.2.3";

            saves.Should().Be(1, "a non-IVersionable 'Version' is user data and must autosave");
            JsonSettings.Load<CoincidentalVersion>(f.FileName).Version.Should().Be("1.2.3");
        }

        #region settings types

        [Autosave]
        public class TwoProp : JsonSettings {
            public override string FileName { get; set; } = "twoprop-scope.jsn";
            public string A { get; set; }
            public string B { get; set; }
            public TwoProp() { }
            public TwoProp(string fileName) : base(fileName) { }
        }

        [Autosave]
        public class Versioned : JsonSettings, IVersionable {
            public override string FileName { get; set; } = "versioned-scope.jsn";
            public Version Version { get; set; } = new Version(1, 0, 0, 0);
            public string Data { get; set; }
            public Versioned() { }
            public Versioned(string fileName) : base(fileName) { }
        }

        //a plain string 'Version' with no IVersionable — must not be swept up by the exclusion.
        [Autosave]
        public class CoincidentalVersion : JsonSettings {
            public override string FileName { get; set; } = "coincidental.jsn";
            public string Version { get; set; }
            public CoincidentalVersion() { }
            public CoincidentalVersion(string fileName) : base(fileName) { }
        }

        #endregion
    }
}
