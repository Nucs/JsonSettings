using System;
using System.IO;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Modulation.Recovery;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests {
    /// <summary>
    ///     Fills in the versioning/recovery module branches the existing suites skip: the
    ///     <c>DoNothing</c> mismatch action, the constructing-parameters constructor, the static
    ///     <c>DefaultPolicy</c> setter, module de-attachment, <c>RecoveryAction.LoadDefaultAndSave</c>,
    ///     and the already-recovered short-circuit when two recovery modules are chained.
    /// </summary>
    [TestClass]
    public class ModuleActionsCoverageTests {
        // ---- VersioningModule -----------------------------------------------------------------

        [TestMethod]
        public void Versioning_DoNothing_KeepsMismatchedVersionAndData() {
            using var f = new TempFile();
            var cfg = JsonSettings.Configure<VersionedSettings>(f)
                                  .WithVersioning(new Version(1, 0, 0, 0), VersioningResultAction.Throw)
                                  .LoadNow();
            cfg.Value = 5;
            cfg.Save();

            //A mismatch under DoNothing leaves the loaded (old) version and data untouched.
            var cfg2 = JsonSettings.Configure<VersionedSettings>(f)
                                   .WithVersioning(new Version(2, 0, 0, 0), VersioningResultAction.DoNothing)
                                   .LoadNow();

            cfg2.Version.Should().Be(new Version(1, 0, 0, 0), "DoNothing keeps whatever successfully parsed");
            cfg2.Value.Should().Be(5);
        }

        [TestMethod]
        public void Versioning_ConstructingParametersConstructor_Works() {
            using var f = new TempFile();
            var module = new VersioningModule<VersionedSettings>(
                VersioningResultAction.DoNothing,
                new Version(1, 0, 0, 0),
                VersioningModule<VersionedSettings>.DefaultPolicy,
                Array.Empty<object>());

            module.ConstructingParameters.Should().BeEmpty();
            module.ExpectedVersion.Should().Be(new Version(1, 0, 0, 0));

            var cfg = JsonSettings.Configure<VersionedSettings>(f).WithModule(module).LoadNow();
            cfg.Version.Should().Be(new Version(1, 0, 0, 0));
        }

        [TestMethod]
        public void Versioning_DefaultPolicySetter_IsHonoured() {
            var original = VersioningModule<PolicyProbeSettings>.DefaultPolicy;
            try {
                VersioningPolicyHandler custom = (current, expected) => true;
                VersioningModule<PolicyProbeSettings>.DefaultPolicy = custom;
                VersioningModule<PolicyProbeSettings>.DefaultPolicy.Should().BeSameAs(custom);
            } finally {
                //Restore so the global generic-static does not leak into other tests.
                VersioningModule<PolicyProbeSettings>.DefaultPolicy = original;
            }
        }

        [TestMethod]
        public void Versioning_NoExplicitVersionAndNoAttribute_Throws() {
            using var f = new TempFile();
            //VersionedSettings carries no [EnforcedVersion]; the attribute-less WithVersioning overload
            //then has nothing to resolve ExpectedVersion from and must fail clearly rather than default
            //to some arbitrary version. This also drives DefaultVersionCache's no-attribute branch.
            new Action(() => JsonSettings.Configure<VersionedSettings>(f)
                                         .WithVersioning(VersioningResultAction.Throw)
                                         .LoadNow())
                .Should().Throw<InvalidVersionException>();
        }

        [TestMethod]
        public void Versioning_Deattach_RemovesModule() {
            using var f = new TempFile();
            var s = JsonSettings.Configure<VersionedSettings>(f).LoadNow();
            var module = new VersioningModule<VersionedSettings>(
                VersioningResultAction.DoNothing, new Version(1, 0, 0, 0), VersioningModule<VersionedSettings>.DefaultPolicy);

            s.Modulation.Attach(module);
            s.Modulation.IsAttachedOfType(typeof(VersioningModule<VersionedSettings>)).Should().BeTrue();

            s.Modulation.Deattach(module);
            s.Modulation.IsAttachedOfType(typeof(VersioningModule<VersionedSettings>)).Should().BeFalse();
        }

        // ---- RecoveryModule -------------------------------------------------------------------

        [TestMethod]
        public void Recovery_LoadDefaultAndSave_RepairsFileOnDisk() {
            using var f = new TempFile();
            var s = JsonSettings.Configure<RecoveryWithoutVersionSettings>(f)
                                .WithRecovery(RecoveryAction.LoadDefault)
                                .LoadNow();
            s.Type = "changed";
            s.Save();

            //Corrupt the file, then load with LoadDefaultAndSave: defaults are restored AND written back.
            File.WriteAllText(f.FileName, "{ this is broken json");

            var recovered = JsonSettings.Configure<RecoveryWithoutVersionSettings>(f)
                                        .WithRecovery(RecoveryAction.LoadDefaultAndSave)
                                        .LoadNow();
            recovered.Type.Should().Be("Hi");

            //A subsequent plain load (no recovery configured) must now succeed, proving the file was rewritten.
            var reread = JsonSettings.Configure<RecoveryWithoutVersionSettings>(f).LoadNow();
            reread.Type.Should().Be("Hi");
        }

        [TestMethod]
        public void Recovery_Deattach_RemovesModule() {
            using var f = new TempFile();
            var s = JsonSettings.Configure<RecoveryWithoutVersionSettings>(f).LoadNow();
            var module = new RecoveryModule(RecoveryAction.LoadDefault);

            s.Modulation.Attach(module);
            s.Modulation.IsAttachedOfType<RecoveryModule>().Should().BeTrue();

            s.Modulation.Deattach(module);
            s.Modulation.IsAttachedOfType<RecoveryModule>().Should().BeFalse();
        }

        [TestMethod]
        public void Recovery_TwoModulesChained_SecondSeesAlreadyRecovered() {
            using var f = new TempFile();
            File.WriteAllText(f.FileName, "{ broken json here");

            //Two recovery modules subscribe to TryingRecover. The first recovers and marks it handled;
            //the second must observe that and short-circuit instead of recovering a second time.
            var s = JsonSettings.Configure<RecoveryWithoutVersionSettings>(f)
                                .WithRecovery(RecoveryAction.LoadDefault)
                                .WithRecovery(RecoveryAction.LoadDefault)
                                .LoadNow();

            s.Type.Should().Be("Hi");
        }

        public class PolicyProbeSettings : JsonSettings, IVersionable {
            public override string FileName { get; set; }
            public Version Version { get; set; } = new Version(1, 0, 0, 0);
        }
    }
}
