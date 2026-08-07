using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Tests.CrossVersion;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.CrossCulture {
    /// <summary>
    ///     Proves a settings file is portable across machines whose OS locale differs: a document
    ///     written under one culture loads, unchanged, under any other. This is the sibling concern to
    ///     <see cref="CrossVersion.CrossVersionCompatibilityTests"/> (which pins portability across
    ///     target frameworks) - here the axis is <see cref="CultureInfo.CurrentCulture"/>.
    ///
    ///     WHY IT IS A REAL RISK. The same <see cref="double"/>, <see cref="decimal"/> or
    ///     <see cref="DateTime"/> renders to different TEXT under different locales - "1,5" under German,
    ///     "1٫5" (U+066B) under Arabic/Persian, a Buddhist/Persian/Umm-al-Qura calendar year for a date -
    ///     and case folding is locale-specific too (Turkish "i".ToUpper() is "İ", not "I"). Any of those
    ///     leaking into the on-disk form, a derived encryption key, an archive file name or a path
    ///     comparison would make a file written on one machine unreadable on another. The library defends
    ///     against this by never routing its own bookkeeping through culture-sensitive formatting and by
    ///     leaning on Json.NET, whose default <c>Culture</c> is <see cref="CultureInfo.InvariantCulture"/>.
    ///
    ///     THE HOSTILE SET. <see cref="HostileCultures"/> is chosen for maximum spread: comma decimals
    ///     (de-DE, az), the Arabic decimal separator and non-Gregorian calendars (ar-SA, fa-IR), the
    ///     dotless-i case rules (tr-TR, az), the Buddhist calendar (th-TH) and Indian digit grouping
    ///     (hi-IN), against the invariant and en-US baselines.
    ///
    ///     THE ORACLE IS THE LIBRARY, NOT THIS TEST. Fidelity is judged by re-serializing the loaded
    ///     value through the library's own <see cref="JsonSettings.Save(string)"/> and comparing bytes to
    ///     the invariant baseline - deliberately NOT by any hand-rolled numeric formatting, because
    ///     <c>StringBuilder.Append(long)</c> itself prepends U+061C/U+200E and uses the real minus sign
    ///     U+2212 under Arabic/Persian. A naive verifier fails there while the library does not; the
    ///     re-save oracle cannot, since <see cref="Save_ProducesIdenticalBytes_InEveryCulture"/> proves
    ///     that save path is byte-stable across every locale first.
    /// </summary>
    [TestClass]
    public class CrossCultureCompatibilityTests {
        /// <summary>Empty string denotes <see cref="CultureInfo.InvariantCulture"/>; the rest are the hostile spread.</summary>
        public static readonly string[] HostileCultures = {
            "", "en-US", "de-DE", "tr-TR", "az-Latn-AZ", "ar-SA", "fa-IR", "th-TH", "hi-IN"
        };

        // ------------------------------------------------------------------ the matrix

        /// <summary>
        ///     The core guarantee: the exact bytes <see cref="JsonSettings.Save(string)"/> writes for the
        ///     canonical payload do not depend on the thread culture. If a comma, an Arabic separator or a
        ///     non-Gregorian date ever leaked into the JSON, this cell's hash would diverge from the
        ///     invariant baseline captured on the same framework.
        /// </summary>
        [DataTestMethod]
        [DataRow("")]
        [DataRow("en-US")]
        [DataRow("de-DE")]
        [DataRow("tr-TR")]
        [DataRow("az-Latn-AZ")]
        [DataRow("ar-SA")]
        [DataRow("fa-IR")]
        [DataRow("th-TH")]
        [DataRow("hi-IN")]
        public void Save_ProducesIdenticalBytes_InEveryCulture(string culture) {
            var baseline = Hash(RunUnder("", () => SaveCanonicalBytes()));
            var underCulture = Hash(RunUnder(culture, () => SaveCanonicalBytes()));

            underCulture.Should().Be(baseline,
                $"the settings file written under '{Name(culture)}' must be byte-for-byte identical to the invariant one");
        }

        /// <summary>
        ///     A file written and read back under the SAME hostile culture must preserve every bit -
        ///     doubles, decimals, dates, the enum, nested collections. Judged by re-saving the loaded
        ///     value and comparing to the invariant baseline.
        /// </summary>
        [DataTestMethod]
        [DataRow("")]
        [DataRow("en-US")]
        [DataRow("de-DE")]
        [DataRow("tr-TR")]
        [DataRow("az-Latn-AZ")]
        [DataRow("ar-SA")]
        [DataRow("fa-IR")]
        [DataRow("th-TH")]
        [DataRow("hi-IN")]
        public void RoundTrip_PreservesAllBits_InEveryCulture(string culture) {
            var baseline = Hash(RunUnder("", () => SaveCanonicalBytes()));

            var reSaved = Hash(RunUnder(culture, () => {
                using var f = new TempFile(false);
                var s = JsonSettings.Construct<CrossVersionSettings>();
                s.Data = CrossVersionPayload.Canonical();
                s.Save(f.FileName);
                var loaded = JsonSettings.Load<CrossVersionSettings>(f.FileName);
                return SaveBytes(loaded.Data);
            }));

            reSaved.Should().Be(baseline,
                $"a save+load under '{Name(culture)}' must round-trip every value unchanged");
        }

        /// <summary>
        ///     The producer x consumer grid: a file saved under EVERY culture loads identically under
        ///     EVERY culture. This is the literal "written on one machine, read on another" scenario, all
        ///     N^2 pairs in one pass.
        /// </summary>
        [TestMethod]
        public void CrossCulture_ProducerConsumer_FullGrid() {
            var available = HostileCultures.Where(IsAvailable).ToArray();
            var baseline = Hash(RunUnder("", () => SaveCanonicalBytes()));

            // One file per producer locale.
            var byProducer = new Dictionary<string, string>();
            var temps = new List<TempFile>();
            try {
                foreach (var producer in available) {
                    var f = new TempFile(false);
                    temps.Add(f);
                    RunUnder(producer, () => {
                        var s = JsonSettings.Construct<CrossVersionSettings>();
                        s.Data = CrossVersionPayload.Canonical();
                        s.Save(f.FileName);
                        return 0;
                    });
                    byProducer[producer] = f.FileName;
                }

                var failures = new List<string>();
                foreach (var producer in available)
                    foreach (var consumer in available) {
                        var reSaved = RunUnder(consumer, () => {
                            var loaded = JsonSettings.Load<CrossVersionSettings>(byProducer[producer]);
                            return Hash(SaveBytes(loaded.Data));
                        });
                        if (reSaved != baseline)
                            failures.Add($"{Name(producer)} -> {Name(consumer)}");
                    }

                failures.Should().BeEmpty(
                    "every producer-locale file must load identically on every consumer-locale; drifted pairs: "
                    + string.Join(", ", failures));
            } finally {
                foreach (var t in temps) t.Dispose();
            }
        }

        /// <summary>
        ///     An encrypted file written under one locale must decrypt under every other. This exercises
        ///     the PBKDF2 key derivation, whose salt seed concatenates the password with its length
        ///     (<c>password + password.Length</c>) - a string+int concat that would localize the number
        ///     under a digit-substituting culture and silently change the key. The password carries
        ///     Turkish dotted/dotless I and other non-ASCII to stress the text path.
        /// </summary>
        [DataTestMethod]
        [DataRow("")]
        [DataRow("tr-TR")]
        [DataRow("ar-SA")]
        [DataRow("fa-IR")]
        [DataRow("th-TH")]
        [DataRow("de-DE")]
        public void Encryption_WrittenUnderCulture_DecryptsUnderEveryOther(string producer) {
            const string password = "Pä$$w0rd-İıØ-42"; // İ (U+0130), ı (U+0131), Ø (U+00D8)
            var plainBaseline = Hash(RunUnder("", () => SaveCanonicalBytes()));

            using var f = new TempFile(false);
            RunUnder(producer, () => {
                var s = JsonSettings.Configure<CrossVersionSettings>(f.FileName).WithEncryption(password);
                s.Data = CrossVersionPayload.Canonical();
                s.Save();
                return 0;
            });

            foreach (var consumer in HostileCultures.Where(IsAvailable)) {
                var reSaved = RunUnder(consumer, () => {
                    var loaded = JsonSettings.Configure<CrossVersionSettings>(f.FileName).WithEncryption(password).LoadNow();
                    return Hash(SaveBytes(loaded.Data));
                });

                reSaved.Should().Be(plainBaseline,
                    $"a file encrypted under '{Name(producer)}' must decrypt to the identical value under '{Name(consumer)}' " +
                    "(the derived AES key must not depend on locale)");
            }
        }

        /// <summary>
        ///     The version-mismatch archive file name is derived from <see cref="Version.ToString()"/> and
        ///     an integer counter through string interpolation. Both must be ASCII and locale-independent,
        ///     so the side-file a <see cref="VersioningResultAction.RenameAndLoadDefault"/> leaves behind
        ///     has the same name on every machine.
        /// </summary>
        [DataTestMethod]
        [DataRow("")]
        [DataRow("en-US")]
        [DataRow("de-DE")]
        [DataRow("tr-TR")]
        [DataRow("az-Latn-AZ")]
        [DataRow("ar-SA")]
        [DataRow("fa-IR")]
        [DataRow("th-TH")]
        [DataRow("hi-IN")]
        public void VersionArchiveName_IsCultureInvariant(string culture) {
            var baseline = RunUnder("", () => VersionArchiveSuffix());
            var underCulture = RunUnder(culture, () => VersionArchiveSuffix());

            underCulture.Should().Be(baseline,
                $"the archived side-file name under '{Name(culture)}' must equal the invariant one");
            underCulture.Should().Be(".1.2.3.4-0.json",
                "the archive suffix is built from Version.ToString() + an int counter, both ASCII on every locale");
        }

        /// <summary>
        ///     The dynamic <see cref="SettingsBag"/> stores an enum by name and reads it back with
        ///     <c>Enum.Parse(..., ignoreCase: true)</c> - a case-insensitive parse that Turkish's dotless-i
        ///     could derail - alongside a double through the untyped path. Both must survive every locale.
        /// </summary>
        [DataTestMethod]
        [DataRow("")]
        [DataRow("tr-TR")]
        [DataRow("az-Latn-AZ")]
        [DataRow("ar-SA")]
        [DataRow("fa-IR")]
        [DataRow("de-DE")]
        public void SettingsBag_EnumAndDouble_SurviveCulture(string culture) {
            var result = RunUnder(culture, () => {
                using var f = new TempFile(false);
                var bag = new SettingsBag(f.FileName);
                bag["mode"] = CrossVersionMode.Custom; // persisted as the string "Custom"
                bag["ratio"] = 1.0d / 3.0d;            // a double through the untyped store
                bag.Save();

                var reopened = new SettingsBag(f.FileName);
                reopened.Load();
                return (mode: reopened.Get<CrossVersionMode>("mode"), ratio: reopened.Get<double>("ratio"));
            });

            result.mode.Should().Be(CrossVersionMode.Custom, $"the enum must parse back under '{Name(culture)}'");
            result.ratio.Should().BeApproximately(1.0d / 3.0d, 1e-15, $"the double must round-trip under '{Name(culture)}'");
        }

        /// <summary>
        ///     A direct, readable guard that no localized numeral or bidi control leaks into the JSON: the
        ///     canonical double serializes as the ASCII "0.3333333333333333" and the file contains none of
        ///     the Arabic decimal separator (U+066B), the real minus sign (U+2212) or the Arabic/LTR marks
        ///     (U+061C/U+200E) that a locale-sensitive formatter would emit.
        /// </summary>
        [DataTestMethod]
        [DataRow("ar-SA")]
        [DataRow("fa-IR")]
        [DataRow("de-DE")]
        [DataRow("th-TH")]
        public void SavedJson_ContainsNoLocalizedNumerals(string culture) {
            var json = RunUnder(culture, () => {
                using var f = new TempFile(false);
                var s = JsonSettings.Construct<CrossVersionSettings>();
                s.Data = CrossVersionPayload.Canonical();
                s.Save(f.FileName);
                return File.ReadAllText(f.FileName);
            });

            json.Should().Contain("0.3333333333333333", "the third renders with an ASCII dot regardless of locale");
            json.IndexOfAny(new[] { '٫', '−', '؜', '‎', '٬' }).Should().Be(-1,
                $"no localized separator/sign/bidi mark may appear in a file written under '{Name(culture)}'");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Saves <see cref="CrossVersionPayload.Canonical"/> through the real Save path and returns the on-disk bytes.</summary>
        private static byte[] SaveCanonicalBytes() => SaveBytes(CrossVersionPayload.Canonical());

        /// <summary>Serializes a payload through the library's own Save (byte-stable across locales, see the class remark).</summary>
        private static byte[] SaveBytes(CrossVersionPayload data) {
            using var f = new TempFile(false);
            var s = JsonSettings.Construct<CrossVersionSettings>();
            s.Data = data;
            s.Save(f.FileName);
            return File.ReadAllBytes(f.FileName);
        }

        /// <summary>
        ///     Writes a v1.2.3.4 file, then loads it expecting v2.0.0.0 with RenameAndLoadDefault so the
        ///     old file is archived, and returns the culture-dependent tail of the archive's name (the
        ///     part after the random stem, e.g. ".1.2.3.4-0.json").
        /// </summary>
        private static string VersionArchiveSuffix() {
            using var f = new TempFile(false);
            var stem = Path.GetFileNameWithoutExtension(f.FileName);
            var dir = Path.GetDirectoryName(f.FileName)!;

            var v1 = JsonSettings.Construct<VersionedSettings>();
            v1.Version = new Version(1, 2, 3, 4);
            v1.Value = 5;
            v1.Save(f.FileName);

            JsonSettings.Configure<VersionedSettings>(f.FileName)
                        .WithVersioning(new Version(2, 0, 0, 0), VersioningResultAction.RenameAndLoadDefault)
                        .LoadNow();

            var archive = Directory.GetFiles(dir, stem + ".*")
                                   .Select(Path.GetFileName)
                                   .FirstOrDefault(n => !string.Equals(n, Path.GetFileName(f.FileName), StringComparison.Ordinal));

            if (archive != null)
                try { File.Delete(Path.Combine(dir, archive)); } catch { /* best-effort cleanup */ }

            return archive == null ? "(none)" : archive.Substring(stem.Length);
        }

        /// <summary>Runs <paramref name="body"/> with the thread (and default-thread) culture set to <paramref name="cultureId"/>, restoring afterwards. Empty id = invariant. Skips (inconclusive) if the OS lacks the locale.</summary>
        private static T RunUnder<T>(string cultureId, Func<T> body) {
            CultureInfo ci;
            try {
                ci = Resolve(cultureId);
            } catch (CultureNotFoundException) {
                Assert.Inconclusive($"Culture '{cultureId}' is not available on this OS/runtime.");
                return default!;
            }

            var thread = Thread.CurrentThread;
            var prevCulture = thread.CurrentCulture;
            var prevUiCulture = thread.CurrentUICulture;
            var prevDefault = CultureInfo.DefaultThreadCurrentCulture;
            var prevDefaultUi = CultureInfo.DefaultThreadCurrentUICulture;
            try {
                thread.CurrentCulture = ci;
                thread.CurrentUICulture = ci;
                CultureInfo.DefaultThreadCurrentCulture = ci;   // any thread the library might spawn inherits it too
                CultureInfo.DefaultThreadCurrentUICulture = ci;
                return body();
            } finally {
                thread.CurrentCulture = prevCulture;
                thread.CurrentUICulture = prevUiCulture;
                CultureInfo.DefaultThreadCurrentCulture = prevDefault;
                CultureInfo.DefaultThreadCurrentUICulture = prevDefaultUi;
            }
        }

        private static CultureInfo Resolve(string cultureId) =>
            cultureId.Length == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(cultureId);

        private static bool IsAvailable(string cultureId) {
            try {
                Resolve(cultureId);
                return true;
            } catch (CultureNotFoundException) {
                return false;
            }
        }

        private static string Name(string cultureId) => cultureId.Length == 0 ? "invariant" : cultureId;

        private static string Hash(byte[] bytes) {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }
    }
}
