using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Tests;


namespace JsonSettingss.Tests {
    [TestClass]
    public class ConfigurableTests {
        [TestMethod]
        public void OnConfigure_AddSingleConfig() {
            using (var f = new TempfileLife()) {
                //used for autodelete file after test ends
                var o = JsonSettings.Configure<CasualConfiguredSettings>(f.FileName).LoadNow();
                o.Modulation.Modules.Should().ContainItemsAssignableTo<RijndaelModule>();
                Assert.IsTrue(o.Modulation.Modules.Count == 1, "o.Modules.Count == 1");
            }
        }

        [TestMethod]
        public void OnConfigure_AddSingleConfig_PriorToLoadNow() {
            using (var f = new TempfileLife()) {
                //used for autodelete file after test ends
                var o = JsonSettings.Configure<CasualConfiguredSettings>(f.FileName);
                o.Modulation.Modules.Should().ContainItemsAssignableTo<RijndaelModule>();
                Assert.IsTrue(o.Modulation.Modules.Count == 1, "o.Modules.Count == 1");
            }
        }

        [TestMethod]
        public void OnConfigure_Only_WithEncyption() {
            using (var f = new TempfileLife()) {
                //used for autodelete file after test ends
                var o = JsonSettings.Configure<CasualConfiguredSettings>(f.FileName).LoadNow();
                o.Modulation.Modules.Should().ContainItemsAssignableTo<RijndaelModule>();
                Assert.IsTrue(o.Modulation.Modules.Count == 1, "o.Modules.Count == 1");

                o.SomeNumeralProperty = 1;
                o.SomeProperty = "with some value";
                o.SomeClassProperty = new SmallClass() {Name = "Small", Value = "Class"};
                o.Save();

                //validate
                o = JsonSettings.Configure<CasualConfiguredSettings>(f.FileName).LoadNow();
                o.Modulation.Modules.Should().ContainItemsAssignableTo<RijndaelModule>();
                o.SomeProperty.Should().Be("with some value");
                o.SomeNumeralProperty.ShouldBeEquivalentTo(1);
                o.SomeClassProperty.Should().BeOfType(typeof(SmallClass)).And.Match(obj => (obj as SmallClass).Name == "Small");
            }
        }

        [TestMethod]
        public void OnConfigure_Only_WithEncyption_CheckBeforeLoadNow() {
            using (var f = new TempfileLife()) {
                //used for autodelete file after test ends
                var o = JsonSettings.Configure<CasualConfiguredSettings>(f.FileName);
                o.Modulation.Modules.Should().ContainItemsAssignableTo<RijndaelModule>();
                Assert.IsTrue(o.Modulation.Modules.Count == 1, "o.Modules.Count == 1");
                o.LoadNow();
                o.SomeNumeralProperty = 1;
                o.SomeProperty = "with some value";
                o.SomeClassProperty = new SmallClass() {Name = "Small", Value = "Class"};
                o.Save();

                //validate
                o = JsonSettings.Configure<CasualConfiguredSettings>(f.FileName).LoadNow();
                o.Modulation.Modules.Should().ContainItemsAssignableTo<RijndaelModule>();
                o.SomeProperty.Should().Be("with some value");
                o.SomeNumeralProperty.ShouldBeEquivalentTo(1);
                o.SomeClassProperty.Should().BeOfType(typeof(SmallClass)).And.Match(obj => (obj as SmallClass).Name == "Small");
            }
        }
    }

    public class CasualConfiguredSettings : CasualExampleSettings {
        protected override void OnConfigure() {
            base.OnConfigure(); //Important!
            this.WithEncryption("password");
        }

        private CasualConfiguredSettings() { }
        public CasualConfiguredSettings(string someprop) : base(someprop) { }
    }
}