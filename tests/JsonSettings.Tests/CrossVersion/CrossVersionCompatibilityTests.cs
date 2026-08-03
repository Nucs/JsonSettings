using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.CrossVersion {
    /// <summary>
    ///     Proves a settings file is portable across every framework this library ships for: a JSON
    ///     document serialized by one target loads, unchanged, on all of them. The concern is real and
    ///     framework-specific - .NET Framework and modern .NET render the same <see cref="double"/> as
    ///     different text, format dates through different code, and (were a polymorphic member present)
    ///     stamp a different assembly name into a Json.NET <c>$type</c> - so "it round-trips on my
    ///     machine" is not evidence it round-trips on a consumer's.
    ///
    ///     THE MATRIX. One fixture is captured per PRODUCER framework - the exact bytes that framework's
    ///     runtime plus its Newtonsoft.Json build write for <see cref="CrossVersionPayload.Canonical"/> -
    ///     and committed under Fixtures/. <see cref="LoadsFixtureFromEveryProducer"/> is data-driven over
    ///     all producers and runs on whatever framework is executing (the CONSUMER), so the producer x
    ///     consumer grid is covered by the test project's own multi-targeting: the suite is built and run
    ///     for net472, net48, net6.0, net8.0 and net10.0, and each of those consumers reads all five
    ///     producer fixtures. Five consumers x five producers = the full 25-cell matrix on Windows; the
    ///     Linux CI leg runs the three .NET consumers against all five producers, which is where a
    ///     Framework-written file is proven to load on a non-Windows runtime.
    ///
    ///     net472 stands in for the netstandard2.0 asset. The library has no net472 build, so a net472
    ///     consumer resolves lib/netstandard2.0 - the fallback the majority of real consumers receive -
    ///     which is exactly why the test project carries the row (see its &lt;TargetFrameworks&gt; note).
    ///
    ///     THE FIXTURES ARE NOT HAND-WRITTEN. Each was produced by running this very suite on that
    ///     framework with the regeneration switch on; see <see cref="SelfCheck_TodaysSerialization_MatchesTheCommittedFixture"/>
    ///     for the switch and the one-liner that rebuilds them. That self-check, which runs on every
    ///     ordinary test pass, is what keeps a committed fixture honest: if the model or the serializer
    ///     output changes, the running framework's own fixture stops matching and the suite says so.
    /// </summary>
    [TestClass]
    public class CrossVersionCompatibilityTests {
        /// <summary>
        ///     Every framework the two packages ship an asset for, named as the test project targets it.
        ///     net472 is the runnable stand-in for lib/netstandard2.0 (the library has no net472 build).
        ///     This is the producer axis of the matrix AND the set of fixtures that must exist; adding a
        ///     shipped target means adding it here and regenerating.
        /// </summary>
        public static readonly string[] ReleaseFrameworks = { "net472", "net48", "net6.0", "net8.0", "net10.0" };

        /// <summary>The framework THIS assembly was compiled for - the consumer in every row below.</summary>
        internal const string CurrentFramework =
#if NET10_0
            "net10.0";
#elif NET8_0
            "net8.0";
#elif NET6_0
            "net6.0";
#elif NET48
            "net48";
#elif NET472
            "net472";
#else
            "unknown";
#endif

        /// <summary>
        ///     Set this environment variable to any non-empty value and run the suite to (re)write the
        ///     running framework's fixture to source instead of verifying it. See the self-check test.
        /// </summary>
        private const string RegenerateEnvVar = "JSONSETTINGS_REGEN_CROSSVERSION";

        private static bool Regenerating => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(RegenerateEnvVar));

        // ---------------------------------------------------------------- the matrix

        /// <summary>
        ///     The core of the matrix: the fixture written by <paramref name="producerFramework"/>
        ///     deserializes on the running (consumer) framework to a value indistinguishable from
        ///     <see cref="CrossVersionPayload.Canonical"/>. One row per producer; the consumer is
        ///     whichever framework is executing this assembly.
        /// </summary>
        [DataTestMethod]
        [DataRow("net472")]
        [DataRow("net48")]
        [DataRow("net6.0")]
        [DataRow("net8.0")]
        [DataRow("net10.0")]
        public void LoadsFixtureFromEveryProducer(string producerFramework) {
            if (Regenerating) {
                Assert.Inconclusive($"Regenerating fixtures ({RegenerateEnvVar} is set); the matrix is not asserted on a regeneration run.");
                return;
            }

            TryReadFixture(producerFramework, out var json)
                .Should().BeTrue($"the {producerFramework} fixture must be committed so the {producerFramework} -> {CurrentFramework} cell is covered. " +
                                 $"Generate it by running the suite on {producerFramework} with {RegenerateEnvVar}=1.");

            var loaded = LoadThroughLibrary(json);

            loaded.Data.Should().BeEquivalentTo(CrossVersionPayload.Canonical(),
                $"a settings file serialized by {producerFramework} must load identically on {CurrentFramework}");
        }

        /// <summary>
        ///     Keeps the committed fixture for the running framework honest, and doubles as the
        ///     generator. On an ordinary run it asserts that what this framework serializes TODAY is
        ///     byte-for-byte (newlines aside) what its committed fixture holds, so any change to the
        ///     model or to Json.NET's output is caught here with a pointer to the fix rather than as a
        ///     baffling failure in some other consumer's row.
        ///
        ///     REGENERATING. Run the suite on each framework with the switch on; every leg rewrites its
        ///     own fixture to source, then rebuild so the files re-embed:
        ///
        ///         JSONSETTINGS_REGEN_CROSSVERSION=1 dotnet test tests/JsonSettings.Tests \
        ///             --filter FullyQualifiedName~CrossVersion
        ///
        ///     On Windows that regenerates all five; a Linux run regenerates only net6.0/net8.0/net10.0.
        /// </summary>
        [TestMethod]
        public void SelfCheck_TodaysSerialization_MatchesTheCommittedFixture() {
            CurrentFramework.Should().NotBe("unknown",
                "the running framework must be one of the release targets; add a branch to CurrentFramework if this list grew");

            var fresh = SerializeThroughLibrary(CrossVersionPayload.Canonical());

            if (Regenerating) {
                var path = Path.Combine(SourceFixtureDirectory(), FixtureFileName(CurrentFramework));
                File.WriteAllText(path, fresh, new UTF8Encoding(false));
                Assert.Inconclusive($"Regenerated {CurrentFramework} fixture at {path} ({fresh.Length} chars).");
                return;
            }

            TryReadFixture(CurrentFramework, out var committed)
                .Should().BeTrue($"the running framework {CurrentFramework} must have a committed fixture; run with {RegenerateEnvVar}=1 to create it");

            Normalize(committed).Should().Be(Normalize(fresh),
                $"the committed {CurrentFramework} fixture must equal what {CurrentFramework} serializes today. " +
                $"If you changed CrossVersionPayload or the serializer, regenerate with {RegenerateEnvVar}=1.");
        }

        /// <summary>
        ///     Guards the matrix against silently shrinking: every release framework must have a fixture,
        ///     and no fixture may exist for a framework not in the release set. Without this a deleted or
        ///     never-generated fixture would just be one row that quietly does nothing.
        /// </summary>
        [TestMethod]
        public void EveryReleaseFramework_HasExactlyOneFixture() {
            if (Regenerating) {
                Assert.Inconclusive($"Regenerating fixtures ({RegenerateEnvVar} is set); the set is expected to be incomplete mid-regeneration.");
                return;
            }

            foreach (var framework in ReleaseFrameworks)
                TryReadFixture(framework, out _)
                    .Should().BeTrue($"a fixture for release framework {framework} is missing; generate it by running the suite on {framework} with {RegenerateEnvVar}=1");

            EmbeddedFixtureFrameworks().Should().BeEquivalentTo(ReleaseFrameworks,
                "the committed fixtures must be exactly the release frameworks - no orphan left by a dropped target, none missing");
        }

        // ---------------------------------------------------------------- library round-trip helpers

        /// <summary>
        ///     Serializes through the real save path so the captured bytes are exactly what a consumer's
        ///     <c>Save()</c> writes: <see cref="JsonSettings.Save(string)"/> -> file -> read the file
        ///     back. Not <c>ToJson()</c> shortcut, so the on-disk encoding is what is pinned.
        /// </summary>
        private static string SerializeThroughLibrary(CrossVersionPayload payload) {
            using var file = new TempFile();
            var settings = JsonSettings.Construct<CrossVersionSettings>();
            settings.Data = payload;
            settings.Save(file.FileName);
            return File.ReadAllText(file.FileName);
        }

        /// <summary>
        ///     Loads through the real load path: write the fixture bytes to a file exactly as they were
        ///     committed, then <see cref="JsonSettings.Load{T}(string)"/> it, so the deserialization,
        ///     the encoding handling and the contract resolver are all the library's own.
        /// </summary>
        private static CrossVersionSettings LoadThroughLibrary(string json) {
            using var file = new TempFile();
            File.WriteAllText(file.FileName, json, new UTF8Encoding(false));
            return JsonSettings.Load<CrossVersionSettings>(file.FileName);
        }

        // ---------------------------------------------------------------- fixture storage

        private static string FixtureFileName(string framework) => $"settings.{framework}.json";

        /// <summary>The <c>.Fixtures.settings.{framework}.json</c> tail of an embedded fixture's logical name.</summary>
        private static string FixtureResourceSuffix(string framework) => $".Fixtures.{FixtureFileName(framework)}";

        /// <summary>
        ///     Reads a committed fixture from the assembly's embedded resources. Embedded rather than
        ///     copied-to-output so a load never depends on the test host's working directory or on
        ///     whether it shadow-copies - the fixtures travel inside the test assembly, identically on
        ///     every framework and OS.
        /// </summary>
        private static bool TryReadFixture(string framework, out string json) {
            var assembly = typeof(CrossVersionCompatibilityTests).Assembly;
            var suffix = FixtureResourceSuffix(framework);
            var resource = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal));
            if (resource is null) {
                json = null;
                return false;
            }

            using var stream = assembly.GetManifestResourceStream(resource);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            json = reader.ReadToEnd();
            return true;
        }

        /// <summary>The frameworks that actually have an embedded fixture, parsed back out of the resource names.</summary>
        private static IEnumerable<string> EmbeddedFixtureFrameworks() {
            const string mid = ".Fixtures.settings.";
            const string end = ".json";
            foreach (var name in typeof(CrossVersionCompatibilityTests).Assembly.GetManifestResourceNames()) {
                var at = name.IndexOf(mid, StringComparison.Ordinal);
                if (at < 0 || !name.EndsWith(end, StringComparison.Ordinal))
                    continue;
                var start = at + mid.Length;
                yield return name.Substring(start, name.Length - end.Length - start);
            }
        }

        /// <summary>
        ///     The source Fixtures/ directory, resolved from this file's compile-time path so
        ///     regeneration writes next to the source rather than into bin/. Used only by the
        ///     regeneration branch, which by definition runs from this checkout.
        /// </summary>
        private static string SourceFixtureDirectory([CallerFilePath] string thisFile = "") =>
            Path.Combine(Path.GetDirectoryName(thisFile)!, "Fixtures");

        /// <summary>
        ///     Collapses newline style so the self-check compares content, not line endings. Json.NET's
        ///     indented writer emits <c>Environment.NewLine</c>, so a fixture generated on Windows holds
        ///     CRLF while a Linux run produces LF; that difference is not what this test is about.
        /// </summary>
        private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n');
    }
}
