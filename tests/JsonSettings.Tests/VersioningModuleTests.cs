using System;
using System.IO;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Tests.Utils;


namespace Nucs.JsonSettings.Tests {
    [TestClass]
    public class VersioningModuleTests {
        TempFile FindFile(string baseFile, Version version) {
            baseFile = Path.GetFullPath(baseFile);
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(baseFile), Path.HasExtension(baseFile) ? "*" + Path.GetFileNameWithoutExtension(baseFile) + "*" : "*.*", SearchOption.TopDirectoryOnly)) {
                if (Path.GetFileName(file).Contains("." + version))
                    return new TempFile(file);
            }

            throw new FileNotFoundException(version.ToString());
        }

        [TestMethod]
        public void RenameAndLoadDefault_Case1() {
            using var f = new TempFile(false);
            //load
            var cfg = JsonSettings.Configure<VersionedSettings>(f)
                                  .WithVersioning(new Version(1, 0, 0, 0), VersioningResultAction.RenameAndLoadDefault)
                                  .LoadNow();

            //assert
            cfg.Version.Should().Be(new Version(1, 0, 0, 0));

            //load
            cfg = JsonSettings.Configure<VersionedSettings>(f)
                              .WithVersioning(new Version(1, 2, 0, 0), VersioningResultAction.RenameAndLoadDefault)
                              .LoadNow();

            using var _1_0_0_0 = FindFile(f, new Version(1, 0, 0, 0));

            //change version and save
            cfg.Version = new Version("1.0.0.1");
            cfg.Save();

            //assert 
            cfg.Version.Should().Be(new Version(1, 0, 0, 1));
            cfg = JsonSettings.Configure<VersionedSettings>(f)
                              .WithVersioning(new Version(1, 0, 0, 1), VersioningResultAction.RenameAndLoadDefault)
                              .LoadNow();
            cfg.Version.Should().Be(new Version(1, 0, 0, 1));

            //assert 
            cfg = JsonSettings.Configure<VersionedSettings>(f)
                              .WithVersioning(new Version(1, 0, 0, 2), VersioningResultAction.RenameAndLoadDefault)
                              .LoadNow();
            using var _1_0_0_1 = FindFile(f, new Version(1, 0, 0, 1));

            cfg.Version.Should().Be(new Version(1, 0, 0, 2));
        }

        [TestMethod]
        public void Throw_Case1() {
            using var f = new TempFile(false);
            //load
            var cfg = JsonSettings.Configure<VersionedSettings>(f)
                                  .WithVersioning(new Version(1, 0, 0, 0), VersioningResultAction.Throw)
                                  .LoadNow();

            //assert
            cfg.Version.Should().Be(new Version(1, 0, 0, 0));

            //load
            new Action(() => {
                cfg = JsonSettings.Configure<VersionedSettings>(f)
                                  .WithVersioning(new Version(1, 2, 0, 0), VersioningResultAction.Throw)
                                  .LoadNow();
            }).Should().Throw<InvalidVersionException>();
        }

        [TestMethod]
        public void LoadDefaultAndSave_Case1() {
            using var f = new TempFile(false);
            //load
            var cfg = JsonSettings.Configure<VersionedSettings>(f)
                                  .WithVersioning(new Version(1, 0, 0, 0), VersioningResultAction.Throw)
                                  .LoadNow();

            //assert
            cfg.Version.Should().Be(new Version(1, 0, 0, 0));

            //load
            cfg = JsonSettings.Configure<VersionedSettings>(f)
                              .WithVersioning(new Version(1, 2, 0, 0), VersioningResultAction.LoadDefaultAndSave)
                              .LoadNow();
            cfg.Version.Should().Be(new Version(1, 2, 0, 0));

            new Action(() => {
                cfg = JsonSettings.Configure<VersionedSettings>(f)
                                  .WithVersioning(new Version(1, 2, 0, 0), VersioningResultAction.Throw)
                                  .LoadNow();
            }).Should().NotThrow();
        }

        [TestMethod]
        public void LoadDefault_Case1() {
            using var f = new TempFile(false);
            //load
            var cfg = JsonSettings.Configure<VersionedSettings>(f)
                                  .WithVersioning(new Version(1, 0, 0, 0), VersioningResultAction.Throw)
                                  .LoadNow();

            //assert
            cfg.Version.Should().Be(new Version(1, 0, 0, 0));

            //load
            cfg = JsonSettings.Configure<VersionedSettings>(f)
                              .WithVersioning(new Version(1, 2, 0, 0), VersioningResultAction.LoadDefault)
                              .LoadNow();

            cfg.Version.Should().Be(new Version(1, 2, 0, 0));

            new Action(() => {
                cfg = JsonSettings.Configure<VersionedSettings>(f)
                                  .WithVersioning(new Version(1, 1, 0, 0), VersioningResultAction.Throw)
                                  .LoadNow();
            }).Should().Throw<InvalidVersionException>();
        }

        [TestMethod]
        public void LoadDefault_Case2() {
            using var f = new TempFile(false);
            //load
            var cfg = JsonSettings.Configure<VersionedWithAttrSettings>(f)
                                  .WithVersioning(VersioningResultAction.Throw)
                                  .LoadNow();

            //assert
            cfg.Version.Should().Be(new Version(1, 2, 0, 0));

            //load
            cfg = JsonSettings.Configure<VersionedWithAttrSettings>(f)
                              .WithVersioning(VersioningResultAction.LoadDefault)
                              .LoadNow();

            cfg.Version.Should().Be(new Version(1, 2, 0, 0));

            new Action(() => {
                cfg = JsonSettings.Configure<VersionedWithAttrSettings>(f)
                                  .WithVersioning(new Version(1, 0, 0, 0), VersioningResultAction.Throw)
                                  .LoadNow();
            }).Should().Throw<InvalidVersionException>();
        }

        [TestMethod]
        public void LoadDefault_Case3() {
            using var f = new TempFile(false);
            //load
            var cfg = JsonSettings.Configure<VersionedWithAttrInheritedSettings>(f)
                                  .WithVersioning(VersioningResultAction.Throw)
                                  .LoadNow();

            //assert
            cfg.Version.Should().Be(new Version(1, 3, 0, 0));

            //load
            cfg = JsonSettings.Configure<VersionedWithAttrInheritedSettings>(f)
                              .WithVersioning(VersioningResultAction.LoadDefault)
                              .LoadNow();

            cfg.Version.Should().Be(new Version(1, 3, 0, 0));

            new Action(() => {
                cfg = JsonSettings.Configure<VersionedWithAttrInheritedSettings>(f)
                                  .WithVersioning(new Version(1, 0, 0, 0), VersioningResultAction.Throw)
                                  .LoadNow();
            }).Should().Throw<InvalidVersionException>();
        }

        /// <summary>
        ///     A settings file whose NAME already carries a version segment (e.g. "app.1.0.0.5.json" -
        ///     a common convention, and the very shape the docs describe an archived file taking) must
        ///     still be handled by RenameAndLoadDefault.
        /// </summary>
        /// <remarks>
        ///     The rename parser matched the version through the <c>VersionMatcher</c> regex's
        ///     lookahead branch (the '.' before the extension), which leaves the numeric capture group
        ///     empty, and <c>int.Parse("")</c> threw a raw <see cref="FormatException"/> - not a
        ///     <see cref="JsonSettingsException"/> - straight out of Load. Every load failure this
        ///     library produces is supposed to be catchable as JsonSettingsException.
        /// </remarks>
        [TestMethod]
        public void RenameAndLoadDefault_FileNameContainsVersionSegment_LoadsDefaultInsteadOfThrowing() {
            var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jsver_" + Guid.NewGuid().ToString("N")));
            try {
                var path = Path.Combine(dir.FullName, "app.1.0.0.5.json");
                File.WriteAllText(path, "{\"Version\":\"1.0.0.5\",\"Value\":7}");

                VersionedSettings cfg = null;
                new Action(() => {
                    cfg = JsonSettings.Configure<VersionedSettings>(path)
                                      .WithVersioning(new Version(2, 0, 0, 0), VersioningResultAction.RenameAndLoadDefault)
                                      .LoadNow();
                }).Should().NotThrow("a version segment in the file name must not crash the rename parser");

                cfg.Version.Should().Be(new Version(2, 0, 0, 0));
                cfg.Value.Should().Be(0);
            } finally {
                try { dir.Delete(true); } catch { /* best effort cleanup */ }
            }
        }

        /// <summary>
        ///     The rename path computed its insertion index from <c>loadedPath.Length</c> while
        ///     inserting into the SHORTER cleanName (loadedPath with the version segment stripped), so
        ///     an extensionless, version-named file under a dot-free directory drove
        ///     <see cref="string.Insert(int,string)"/> out of range.
        /// </summary>
        [TestMethod]
        public void RenameAndLoadDefault_ExtensionlessVersionedFile_DoesNotThrowOutOfRange() {
            var tempBase = Path.GetTempPath();
            if (tempBase.Contains("."))
                Assert.Inconclusive("Temp path contains '.', so the dot-free-path branch this guards cannot be exercised here.");

            var dir = Directory.CreateDirectory(Path.Combine(tempBase, "jsbug2_" + Guid.NewGuid().ToString("N")));
            try {
                //Extensionless and already carrying the module's own ".{version}-{seq}" archive shape,
                //so the numeric capture parses and execution reaches the faulty Insert.
                var path = Path.Combine(dir.FullName, "config.1.0.0.5-0");
                File.WriteAllText(path, "{\"Version\":\"1.0.0.5\",\"Value\":3}");
                //cleanName ("<dir>/config") must already exist for the rename branch to execute.
                File.WriteAllText(Path.Combine(dir.FullName, "config"), "seed");

                VersionedSettings cfg = null;
                new Action(() => {
                    cfg = JsonSettings.Configure<VersionedSettings>(path)
                                      .WithVersioning(new Version(2, 0, 0, 0), VersioningResultAction.RenameAndLoadDefault)
                                      .LoadNow();
                }).Should().NotThrow();

                cfg.Version.Should().Be(new Version(2, 0, 0, 0));
            } finally {
                try { dir.Delete(true); } catch { /* best effort cleanup */ }
            }
        }
    }

    public class VersionedSettings : JsonSettings, IVersionable {
        #region Overrides of JsonSettings

        public override string FileName { get; set; }

        #endregion

        public Version Version { get; set; } = new Version(1, 0, 0, 0);

        public virtual int Value { get; set; }
    }

    public class VersionedWithAttrSettings : JsonSettings, IVersionable {
        #region Overrides of JsonSettings

        public override string FileName { get; set; }

        #endregion

        [EnforcedVersion("1.2.0.0")]
        public virtual Version Version { get; set; } = new Version(1, 0, 0, 0);

        public virtual int Value { get; set; }
    }

    public class VersionedWithAttrInheritedSettings : VersionedWithAttrSettings {
        #region Overrides of JsonSettings

        public override string FileName { get; set; }

        #endregion

        #region Overrides of VersionedWithAttrSettings
        [EnforcedVersion("1.3.0.0")]
        public override Version Version { get; set; }

        #endregion
    }

    public class ChangedVersionedSettings : JsonSettings, IVersionable {
        #region Overrides of JsonSettings

        public override string FileName { get; set; }

        #endregion

        public Version Version { get; set; } = new Version(1, 0, 0, 0);

        public virtual string Value { get; set; }
    }
}