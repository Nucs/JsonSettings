using System;
using System.IO;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Modulation.Recovery;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Modulation {
    /// <summary>
    ///     Guard-clause coverage for the versioning and recovery modules: attaching a
    ///     <see cref="VersioningModule{T}"/> to a socket that is not <see cref="IVersionable"/>, and the
    ///     defensive <c>default</c> arms of the mismatch/recovery switches when handed an action value
    ///     outside the declared enum.
    /// </summary>
    [TestClass]
    public class ModuleGuardTests {
        [TestMethod]
        public void Versioning_AttachedToNonVersionableSocket_ThrowsInvalidOperationException() {
            //VersioningModule<T> constrains T to IVersionable, but the socket it is attached to is a
            //separate object; attaching it to a settings instance that is not IVersionable must fail
            //loudly at attach time rather than misbehave during load.
            //No file is touched: Attach validates the socket type and throws before any I/O.
            var plain = new GuardPlainSettings { FileName = "guard-nonversionable.json" };
            var module = new VersioningModule<GuardVersionedSettings>(
                VersioningResultAction.DoNothing, new Version(1, 0, 0, 0), VersioningModule<GuardVersionedSettings>.DefaultPolicy);

            new Action(() => plain.Modulation.Attach(module))
                .Should().Throw<InvalidOperationException>().WithMessage("*does not implement IVersionable*");
        }

        [TestMethod]
        public void Versioning_UnknownMismatchAction_ThrowsArgumentOutOfRange() {
            using var f = new TempFile();
            //Seed a file at 1.0.0.0.
            JsonSettings.Configure<GuardVersionedSettings>(f)
                        .WithVersioning(new Version(1, 0, 0, 0), VersioningResultAction.DoNothing)
                        .LoadNow();

            //Load expecting 2.0.0.0 (a mismatch) with an action value outside the enum -> the switch's
            //defensive default fires.
            new Action(() => JsonSettings.Configure<GuardVersionedSettings>(f)
                                         .WithVersioning(new Version(2, 0, 0, 0), (VersioningResultAction) 999)
                                         .LoadNow())
                .Should().Throw<ArgumentOutOfRangeException>();
        }

        [TestMethod]
        public void Recovery_UnknownAction_ThrowsArgumentOutOfRange() {
            using var f = new TempFile(create: true);
            File.WriteAllText(f.FileName, "{ this is not valid json");

            new Action(() => JsonSettings.Configure<GuardPlainSettings>(f)
                                         .WithRecovery((RecoveryAction) 999)
                                         .LoadNow())
                .Should().Throw<ArgumentOutOfRangeException>();
        }

        public class GuardPlainSettings : JsonSettings {
            public override string FileName { get; set; }
            public string Value { get; set; }
            public GuardPlainSettings() { }
            public GuardPlainSettings(string fileName) : base(fileName) { }
        }

        public class GuardVersionedSettings : JsonSettings, IVersionable {
            public override string FileName { get; set; }
            public Version Version { get; set; } = new Version(1, 0, 0, 0);
            public int Value { get; set; }
            public GuardVersionedSettings() { }
            public GuardVersionedSettings(string fileName) : base(fileName) { }
        }
    }
}
