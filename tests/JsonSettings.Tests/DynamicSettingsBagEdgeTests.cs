using System;
using AwesomeAssertions;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests {
    /// <summary>
    ///     Edge cases for <see cref="DynamicSettingsBag"/>'s index binding. A null or empty string index
    ///     makes <c>TryGetIndex</c>/<c>TrySetIndex</c> return <c>false</c>, which the DLR surfaces to the
    ///     caller as a <see cref="RuntimeBinderException"/>. These false-return branches were uncovered.
    /// </summary>
    [TestClass]
    public class DynamicSettingsBagEdgeTests {
        [TestMethod]
        public void SetIndex_EmptyStringKey_IsRejected() {
            using var f = new TempFile();
            var bag = JsonSettings.Load<SettingsBag>(f);
            dynamic d = bag.AsDynamic();

            new Action(() => d[""] = "value")
                .Should().Throw<RuntimeBinderException>("an empty index key is rejected by TrySetIndex");
        }

        [TestMethod]
        public void GetIndex_EmptyStringKey_IsRejected() {
            using var f = new TempFile();
            var bag = JsonSettings.Load<SettingsBag>(f);
            dynamic d = bag.AsDynamic();

            new Action(() => { var _ = d[""]; })
                .Should().Throw<RuntimeBinderException>("an empty index key is rejected by TryGetIndex");
        }

        [TestMethod]
        public void GetIndex_NonStringKey_IsRejected() {
            using var f = new TempFile();
            var bag = JsonSettings.Load<SettingsBag>(f);
            dynamic d = bag.AsDynamic();

            //A non-string index cannot address the string-keyed bag; the binder declines it.
            new Action(() => { var _ = d[123]; })
                .Should().Throw<RuntimeBinderException>();
        }

        [TestMethod]
        public void SetIndex_NonStringKey_IsRejected() {
            using var f = new TempFile();
            var bag = JsonSettings.Load<SettingsBag>(f);
            dynamic d = bag.AsDynamic();

            new Action(() => d[123] = "value")
                .Should().Throw<RuntimeBinderException>();
        }

        [TestMethod]
        public void ValidStringIndex_StillWorks() {
            using var f = new TempFile();
            var bag = JsonSettings.Load<SettingsBag>(f);
            dynamic d = bag.AsDynamic();

            d["ok"] = "value";
            ((string) d["ok"]).Should().Be("value");
            //AsBag round-trips back to the same underlying bag.
            SettingsBag same = d.AsBag();
            same.Should().BeSameAs(bag);
        }
    }
}
