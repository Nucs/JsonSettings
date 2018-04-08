using FluentAssertions;
using nucs.JsonSettings.xTests.Utils;
using Xunit;

namespace nucs.JsonSettings.xTests {
    public class DynamicSettingsBagTests {
        [Fact]
        public void DynamicSettingsBag_DynamicAccess_Index() {
            using (var f = new TempfileLife()) {
                var o = JsonSettings.Load<SettingsBag>(f);
                dynamic d = o.AsDynamic();
                d.SomeProp = "Works";
                Assert.True(d["SomeProp"]=="Works");
                d.Num = 1;
                Assert.True(d.Num==1);
                SettingsBag bag = d.AsBag();
                bag.Save();

                o = JsonSettings.Load<SettingsBag>(f);
                o["SomeProp"].Should().Be("Works");
                o["Num"].Should().Be(1L); //newtonsoft deserializes numbers as long.
            }
        }

        [Fact]
        public void DynamicSettingsBag_DynamicAccess_Direct() {
            using (var f = new TempfileLife()) {
                var o = JsonSettings.Load<SettingsBag>(f);
                dynamic d = o.AsDynamic();
                d.SomeProp = "Works";
                Assert.True(d.SomeProp=="Works");
                d.Num = 1;
                Assert.True(d.Num==1);
                SettingsBag bag = d.AsBag();
                bag.Save();

                o = JsonSettings.Load<SettingsBag>(f);
                ((string)d.SomeProp).Should().Be("Works");
                ((long)d.Num).Should().Be(1L); //newtonsoft deserializes numbers as long.
            }
        }
    }
}