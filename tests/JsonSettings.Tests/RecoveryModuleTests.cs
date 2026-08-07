using System;
using System.IO;
using System.Text;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Modulation.Recovery;
using Nucs.JsonSettings.Tests.Utils;


namespace Nucs.JsonSettings.Tests {
    [TestClass]
    public class RecoveryModuleTests {
        TempFile FindFile(string baseFile, Version version) {
            baseFile = Path.GetFullPath(baseFile);
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(baseFile), Path.HasExtension(baseFile) ? "*" + Path.GetFileNameWithoutExtension(baseFile) + "*" : "*.*", SearchOption.TopDirectoryOnly)) {
                if (Path.GetFileName(file).Contains("." + version))
                    return new TempFile(file);
            }

            throw new FileNotFoundException(version.ToString());
        }

        [TestMethod]
        public void LoadDefault_ReloadOnCorruption() {
            using var f = new TempFile(false);
            //load
            var cfg = JsonSettings.Configure<RecoverySettings>(f)
                                  .WithRecovery(RecoveryAction.LoadDefault)
                                  .LoadNow();

            cfg.Value = 5;
            cfg.Save();

            //load
            var cfgg = JsonSettings.Configure<ThrowingRecoverySettings>(f)
                                   .WithRecovery(RecoveryAction.LoadDefault)
                                   .LoadNow();

            cfgg.Value.Should().BeNullOrEmpty();
        }

        [TestMethod]
        public void Throw_Case1() {
            using var f = new TempFile(false);
            //assert 
            var cfg = JsonSettings.Configure<RecoverySettings>(f)
                                  .WithVersioning(new Version(1, 0, 0, 2), VersioningResultAction.DoNothing)
                                  .LoadNow();

            cfg.Version.Should().Be(new Version(1, 0, 0, 2));

            new Action(() => JsonSettings.Configure<ThrowingRecoverySettings>(f)
                                         .WithRecovery(RecoveryAction.Throw)
                                         .WithVersioning(new Version(1, 0, 0, 2), VersioningResultAction.RenameAndLoadDefault)
                                         .LoadNow()).Should().Throw<JsonSettingsRecoveryException>();
        }

        [TestMethod]
        public void LoadDefaultAndSave_Case1() {
            using var f = new TempFile(false);
            //load
            var cfg = JsonSettings.Configure<RecoverySettings>(f)
                                  .WithVersioning(new Version(1, 0, 0, 0), VersioningResultAction.Throw)
                                  .LoadNow();

            //assert
            cfg.Version.Should().Be(new Version(1, 0, 0, 0));

            //load
            cfg = JsonSettings.Configure<RecoverySettings>(f)
                              .WithVersioning(new Version(1, 2, 0, 0), VersioningResultAction.LoadDefaultAndSave)
                              .LoadNow();
            cfg.Version.Should().Be(new Version(1, 2, 0, 0));

            new Action(() => {
                cfg = JsonSettings.Configure<RecoverySettings>(f)
                                  .WithVersioning(new Version(1, 2, 0, 0), VersioningResultAction.Throw)
                                  .LoadNow();
            }).Should().NotThrow();
        }

        [TestMethod]
        public void RenameAndLoadDefault_Case1() {
            using var f = new TempFile(false);
            //load
            var cfg = JsonSettings.Configure<RecoverySettings>(f)
                                  .WithRecovery(RecoveryAction.Throw)
                                  .WithVersioning(new Version(1, 0, 0, 0), VersioningResultAction.Throw)
                                  .LoadNow();
            cfg.Value = 1;
            cfg.Save();
            cfg.Version.Should().Be(new Version(1, 0, 0, 0));
            //assert

            //load
            var cfg2 = JsonSettings.Configure<ThrowingRecoverySettings>(f)
                                   .WithRecovery(RecoveryAction.RenameAndLoadDefault)
                                   .WithVersioning(new Version(1, 2, 0, 0), VersioningResultAction.RenameAndLoadDefault)
                                   .LoadNow();

            cfg2.Version.Should().Be(new Version(1, 2, 0, 0));
            using var _1_0_0_0 = FindFile(f, new Version(1, 0, 0, 0));
            /*
                new Action(() => {
                    cfg = JsonSettings.Configure<RecoverySettings>(f)
                                      .WithVersioning(new Version(1, 1, 0, 0), VersioningResultAction.Throw)
                                      .LoadNow();
                }).Should().Throw<InvalidVersionException>();*/
        }

        [TestMethod]
        public void RenameAndLoadDefault_Case2() {
            using var f = new TempFile(false);
            var Settings = JsonSettings.Configure<RecoveryWithoutVersionSettings>(f)
                                       .WithRecovery(RecoveryAction.RenameAndLoadDefault)
                                       .LoadNow();
            
            Settings.Type = "Hello"; //Boom! saves.
            //someone changed the file manually and messed up the json.
            File.WriteAllText(f, File.ReadAllText(f, Encoding.UTF8).Replace("Hello", "Hi\"}\n:={}"), Encoding.UTF8); //some random text breaking the json
            
            //after some changes and development, you decide to upgrade to 1.0.0.7
            Settings = JsonSettings.Configure<RecoveryWithoutVersionSettings>(f)
                                   .WithRecovery(RecoveryAction.RenameAndLoadDefault)
                                   .LoadNow();

            Console.WriteLine(f.FileName);

            File.Exists(f.FileName.Replace(".json", ".0.json")).Should().BeTrue();
            Settings.Type.Should().Be("Hi");
        }

        /// <summary>
        ///     RecoveryModule.RenameAndLoadDefault shares the version-in-name rename logic with
        ///     VersioningModule and carried the same defect: a file named e.g. "data.1.2.3.4.json"
        ///     made the numeric capture empty and <c>int.Parse("")</c> threw a raw
        ///     <see cref="FormatException"/> instead of the recovery completing as a
        ///     <see cref="JsonSettingsException"/>-catchable operation.
        /// </summary>
        [TestMethod]
        public void RenameAndLoadDefault_FileNameContainsVersionSegment_RecoversInsteadOfThrowing() {
            var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jsrec_" + Guid.NewGuid().ToString("N")));
            try {
                var path = Path.Combine(dir.FullName, "data.1.2.3.4.json");
                File.WriteAllText(path, "{ this is not valid json");

                RecoveryWithoutVersionSettings cfg = null;
                new Action(() => {
                    cfg = JsonSettings.Configure<RecoveryWithoutVersionSettings>(path)
                                      .WithRecovery(RecoveryAction.RenameAndLoadDefault)
                                      .LoadNow();
                }).Should().NotThrow("a version segment in the file name must not crash the recovery rename parser");

                cfg.Type.Should().Be("Hi");
            } finally {
                try { dir.Delete(true); } catch { /* best effort cleanup */ }
            }
        }
    }

    public class RecoverySettings : JsonSettings, IVersionable {
        #region Overrides of JsonSettings

        public override string FileName { get; set; }

        #endregion

        public Version Version { get; set; } = new Version(1, 0, 0, 0);

        public virtual int Value { get; set; }
    }


    public class RecoveryWithoutVersionSettings : JsonSettings {
        #region Overrides of JsonSettings

        public override string FileName { get; set; }

        #endregion

        public virtual string Type { get; set; } = "Hi";
        
        public virtual int Value { get; set; }
    }

    public class ThrowingRecoverySettings : JsonSettings, IVersionable {
        #region Overrides of JsonSettings

        public override string FileName { get; set; }

        #endregion

        public Version Version { get; set; } = new Version(1, 0, 0, 0);

        public virtual int[] Value { get; set; }
    }
}