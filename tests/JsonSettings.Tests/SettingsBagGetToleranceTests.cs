using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests {
    /// <summary>
    ///     <see cref="SettingsBag.Get{T}"/> tolerance for enum-typed values. Regression: a JSON round-trip
    ///     stores an enum as the boxed <c>Int64</c> its numeric form deserializes to, and the tolerant
    ///     getter converted through <c>Convert.ChangeType</c>, which throws <c>InvalidCastException</c>
    ///     converting an integral to an enum. Enum settings therefore worked in memory but threw after a
    ///     save/load. The getter now coerces integral and string forms back to the enum, the same way it
    ///     already bridges <c>long</c> to <c>int</c>.
    /// </summary>
    [TestClass]
    public class SettingsBagGetToleranceTests {
        public enum Mode {
            Off = 0,
            Slow = 1,
            Fast = 2,
        }

        [TestMethod]
        public void Get_Enum_FromBoxedLong_Coerces() {
            var bag = new SettingsBag();
            bag["mode"] = (long) Mode.Fast; //exactly what Newtonsoft deserializes an enum-as-int back to
            bag.Get<Mode>("mode").Should().Be(Mode.Fast);
        }

        [TestMethod]
        public void Get_Enum_ExactValue_Unchanged() {
            var bag = new SettingsBag();
            bag["mode"] = Mode.Slow;
            //Still stored as the enum itself -> the exact-type fast path returns it untouched.
            bag.Get<Mode>("mode").Should().Be(Mode.Slow);
        }

        [TestMethod]
        public void Get_NullableEnum_FromLong_Coerces() {
            var bag = new SettingsBag();
            bag["mode"] = (long) Mode.Fast;
            bag.Get<Mode?>("mode").Should().Be(Mode.Fast);
        }

        [TestMethod]
        public void Get_Enum_MissingKey_ReturnsProvidedDefault() {
            var bag = new SettingsBag();
            bag.Get<Mode>("absent", Mode.Slow).Should().Be(Mode.Slow);
        }

        [TestMethod]
        public void Get_Enum_FromName_Parses() {
            var bag = new SettingsBag();
            bag["mode"] = "Fast"; //a string-enum converter, or a hand-written string, stores the name
            bag.Get<Mode>("mode").Should().Be(Mode.Fast);
        }

        [TestMethod]
        public void Get_Enum_SurvivesFileRoundTrip() {
            using var f = new TempFile();
            var o = JsonSettings.Load<SettingsBag>(f);
            o["mode"] = Mode.Fast;
            o.Save();

            //After reload the value is a boxed long; the typed Get<Mode> must still work, as Get<int> does.
            var x = JsonSettings.Load<SettingsBag>(f);
            x.Get<Mode>("mode").Should().Be(Mode.Fast, "an enum setting must survive a save/load through Get<TEnum>");
        }
    }
}
