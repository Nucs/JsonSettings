using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using FluentAssertions;
using nucs.JsonSettings.Fluent;
using nucs.JsonSettings.Modulation;
using nucs.JsonSettings.xTests.Utils;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace nucs.JsonSettings.xTests {
    public class ConfigurableTests {
        [Fact]
        public void OnConfigure_AddSingleConfig() {
            using (var f = new TempfileLife()) {
                //used for autodelete file after test ends
                var o = JsonSettings.Configure<CasualConfiguredSettings>(f.FileName).LoadNow();
                o.Modules.Should().ContainItemsAssignableTo<RijndaelModule>();
                Assert.True(o.Modules.Count == 1, "o.Modules.Count == 1");
            }
        }

        [Fact]
        public void OnConfigure_AddSingleConfig_PriorToLoadNow() {
            using (var f = new TempfileLife()) {
                //used for autodelete file after test ends
                var o = JsonSettings.Configure<CasualConfiguredSettings>(f.FileName);
                o.Modules.Should().ContainItemsAssignableTo<RijndaelModule>();
                Assert.True(o.Modules.Count == 1, "o.Modules.Count == 1");
            }
        }

        [Fact]
        public void OnConfigure_Only_WithEncyption() {
            using (var f = new TempfileLife()) {
                //used for autodelete file after test ends
                var o = JsonSettings.Configure<CasualConfiguredSettings>(f.FileName).LoadNow();
                o.Modules.Should().ContainItemsAssignableTo<RijndaelModule>();
                Assert.True(o.Modules.Count == 1, "o.Modules.Count == 1");

                o.SomeNumeralProperty = 1;
                o.SomeProperty = "with some value";
                o.SomeClassProperty = new SmallClass() {Name = "Small", Value = "Class"};
                o.Save();

                //validate
                o = JsonSettings.Configure<CasualConfiguredSettings>(f.FileName).LoadNow();
                o.Modules.Should().ContainItemsAssignableTo<RijndaelModule>();
                o.SomeProperty.Should().Be("with some value");
                o.SomeNumeralProperty.ShouldBeEquivalentTo(1);
                o.SomeClassProperty.Should().BeOfType(typeof(SmallClass)).And.Match(obj => (obj as SmallClass).Name == "Small");
            }
        }

        [Fact]
        public void OnConfigure_Only_WithEncyption_CheckBeforeLoadNow() {
            using (var f = new TempfileLife()) {
                //used for autodelete file after test ends
                var o = JsonSettings.Configure<CasualConfiguredSettings>(f.FileName);
                o.Modules.Should().ContainItemsAssignableTo<RijndaelModule>();
                Assert.True(o.Modules.Count == 1, "o.Modules.Count == 1");
                o.LoadNow();
                o.SomeNumeralProperty = 1;
                o.SomeProperty = "with some value";
                o.SomeClassProperty = new SmallClass() {Name = "Small", Value = "Class"};
                o.Save();

                //validate
                o = JsonSettings.Configure<CasualConfiguredSettings>(f.FileName).LoadNow();
                o.Modules.Should().ContainItemsAssignableTo<RijndaelModule>();
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
    }
}