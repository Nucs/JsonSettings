using System;
using System.Collections.Generic;

namespace Nucs.JsonSettings.Tests.CrossVersion {
    /// <summary>
    ///     The settings type whose serialized form is pinned once per producer framework and then
    ///     read back on every framework by <see cref="CrossVersionCompatibilityTests"/>.
    /// </summary>
    /// <remarks>
    ///     Every member is a CONCRETE type, never <see cref="object"/> or an interface. That is the
    ///     one rule that makes a fixture captured on one runtime load on another: under
    ///     <c>TypeNameHandling.Auto</c> (the library default) Json.NET writes a <c>$type</c>
    ///     discriminator only for a value whose runtime type differs from its declared slot, and that
    ///     discriminator is assembly-qualified - <c>mscorlib</c> on .NET Framework,
    ///     <c>System.Private.CoreLib</c> on .NET - so a polymorphic slot serialized on net48 fails to
    ///     resolve on net10.0 even when nothing else is wrong. A <see cref="SettingsBag"/>, whose
    ///     backing store is a <c>Dictionary&lt;string, object&gt;</c>, hits exactly this and is the
    ///     reason <see cref="EncryptionCompatibilityTests"/> pins a plain class rather than a bag. With
    ///     concrete slots no <c>$type</c> is ever emitted, so the format is portable.
    /// </remarks>
    public class CrossVersionSettings : JsonSettings {
        public override string FileName { get; set; }

        public CrossVersionPayload Data { get; set; }
    }

    public enum CrossVersionMode {
        None = 0,
        Fast = 1,
        Slow = 2,
        Custom = 40
    }

    public class CrossVersionNested {
        public string Label { get; set; }
        public int Order { get; set; }
        public double Weight { get; set; }
    }

    /// <summary>
    ///     A deliberately wide spread of value types, chosen for the ones whose textual form drifted
    ///     between .NET Framework and modern .NET - floating point above all, where net48 renders a
    ///     round-trippable double with <c>"R"</c> (up to 17 digits) while .NET Core 3.0+ renders the
    ///     shortest string that still round-trips. The two texts differ; both must parse back to the
    ///     identical bits, which is precisely what the cross-version matrix asserts.
    /// </summary>
    public class CrossVersionPayload {
        // Integers, including the boundaries that a naive int/long confusion would truncate.
        public int Int32Min { get; set; }
        public int Int32Max { get; set; }
        public long Int64Min { get; set; }
        public long Int64Max { get; set; }
        public ulong UInt64Max { get; set; }
        public short Int16 { get; set; }
        public byte Byte { get; set; }

        // Floating point - the primary reason this test exists. See the class remark.
        public double DoubleSimple { get; set; }
        public double DoubleThird { get; set; }
        public double DoubleNegative { get; set; }
        public double DoubleSmallExponent { get; set; }
        public double DoubleHuge { get; set; }
        public float SingleThird { get; set; }
        public float SingleSimple { get; set; }
        public decimal DecimalMax { get; set; }
        public decimal DecimalPi { get; set; }

        public bool BoolTrue { get; set; }
        public bool BoolFalse { get; set; }

        // Text: ASCII, escapes, multi-byte/emoji/RTL, empty and null.
        public string Ascii { get; set; }
        public string WithEscapes { get; set; }
        public string Unicode { get; set; }
        public string Empty { get; set; }
        public string NullString { get; set; }

        // Temporal. Only machine-INDEPENDENT shapes: Utc (an absolute instant), Unspecified (no zone
        // at all) and DateTimeOffset (an explicit offset). A DateTimeKind.Local value is deliberately
        // absent - it serializes with the PRODUCING machine's offset and reads back adjusted to the
        // CONSUMING machine's local zone, so the fixture would encode the timezone of whoever
        // regenerated it and fail on a runner in another zone. That is a property of local time, not a
        // cross-version defect, and has no place in this matrix.
        public DateTime DateUtc { get; set; }
        public DateTime DateUnspecified { get; set; }
        public DateTimeOffset DateOffset { get; set; }
        public TimeSpan Span { get; set; }
        public TimeSpan SpanNegative { get; set; }

        public Guid Guid { get; set; }
        public CrossVersionMode Mode { get; set; }
        public int? NullableSet { get; set; }
        public int? NullableNull { get; set; }

        // Concrete collections only - no object slots, so no assembly-qualified $type (see the class
        // remark). Covers a string list, primitive arrays, a dictionary and nested objects.
        public List<string> Strings { get; set; }
        public int[] Ints { get; set; }
        public double[] Doubles { get; set; }
        public Dictionary<string, int> Map { get; set; }
        public CrossVersionNested Child { get; set; }
        public List<CrossVersionNested> Children { get; set; }

        /// <summary>
        ///     The single value every fixture encodes and every load is checked against. It is built
        ///     from fixed literals with no <c>DateTime.Now</c>, no culture-sensitive parse and no
        ///     randomness, so it is byte-for-byte reproducible on any machine, in any timezone, on any
        ///     framework. Change this and the committed fixtures must be regenerated - the running
        ///     framework's self-check in <see cref="CrossVersionCompatibilityTests"/> fails loudly and
        ///     tells you how.
        /// </summary>
        public static CrossVersionPayload Canonical() {
            return new CrossVersionPayload {
                Int32Min = int.MinValue,
                Int32Max = int.MaxValue,
                Int64Min = long.MinValue,
                Int64Max = long.MaxValue,
                UInt64Max = ulong.MaxValue,
                Int16 = -12345,
                Byte = 200,

                DoubleSimple = 0.1d,
                DoubleThird = 1.0d / 3.0d,
                DoubleNegative = -98765.4321d,
                DoubleSmallExponent = 1.5e-10d,
                DoubleHuge = double.MaxValue,
                SingleThird = 1.0f / 3.0f,
                SingleSimple = 0.1f,
                DecimalMax = decimal.MaxValue,
                DecimalPi = 3.1415926535897932384626433832795m,

                BoolTrue = true,
                BoolFalse = false,

                Ascii = "plain ascii value",
                WithEscapes = "tab\tquote\"backslash\\newline\ncarriage\r",
                Unicode = "héllo wörld 日本語 🎉 ‮reversed‬",
                Empty = "",
                NullString = null,

                DateUtc = new DateTime(2020, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc),
                DateUnspecified = new DateTime(1999, 12, 31, 23, 59, 58, DateTimeKind.Unspecified),
                DateOffset = new DateTimeOffset(2021, 6, 7, 8, 9, 10, 111, TimeSpan.FromHours(2)),
                Span = new TimeSpan(1, 2, 3, 4, 5),
                SpanNegative = new TimeSpan(0, -5, -30, 0),

                Guid = Guid.Parse("11223344-5566-7788-99aa-bbccddeeff00"),
                Mode = CrossVersionMode.Custom,
                NullableSet = 42,
                NullableNull = null,

                Strings = new List<string> { "alpha", "béta", "" },
                Ints = new[] { -1, 0, 1, int.MaxValue },
                Doubles = new[] { 0.1d, 1.0d / 3.0d, -2.5d },
                Map = new Dictionary<string, int> { { "one", 1 }, { "two", 2 }, { "three", 3 } },
                Child = new CrossVersionNested { Label = "child", Order = 7, Weight = 1.0d / 7.0d },
                Children = new List<CrossVersionNested> {
                    new CrossVersionNested { Label = "first", Order = 1, Weight = 0.25d },
                    new CrossVersionNested { Label = "second", Order = 2, Weight = 2.0d / 3.0d },
                },
            };
        }
    }
}
