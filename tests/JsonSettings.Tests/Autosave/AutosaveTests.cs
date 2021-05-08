using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.xTests.Utils;
using NUnit.Framework;

namespace Nucs.JsonSettings.xTests.Autosave {
    [TestFixture]
    public class AutosaveTests {
        /// <summary>Initializes a new instance of the <see cref="T:System.Object" /> class.</summary>
        public AutosaveTests() { }

        [SetUp]
        public void Setup() {
            Console.SetOut(TestContext.Out);
        }

        [Test]
        public void ClassWithoutInterfacesOrVirtuals() {
            using (var f = new TempfileLife()) {
                var o = JsonSettings.Load<InvalidSettings>(f.FileName);
                new Action(() => o.EnableAutosave()).ShouldThrow<JsonSettingsException>();
            }
        }

        [Test]
        public void ClassWithInterfacesOrVirtuals() {
            using (var f = new TempfileLife()) {
                var o = JsonSettings.Load<Settings>(f.FileName).EnableAutosave();
                o.GetType().Namespace.Should().Be("Castle.Proxies");
            }
        }

        [Test]
        public void Saving() {
            using (var f = new TempfileLife()) {
                var rpath = JsonSettings.ResolvePath(f);

                bool saved = false;
                var o = JsonSettings.Load<Settings>(f.FileName).EnableAutosave();
                o.AfterSave += (s, destinition) => { saved = true; };
                o.property.ShouldBeEquivalentTo(null);
                Console.WriteLine(File.ReadAllText(rpath));

                o.property = "test";
                saved.ShouldBeEquivalentTo(true);
                var o2 = JsonSettings.Load<Settings>(f.FileName).EnableAutosave();
                o2.property.ShouldBeEquivalentTo("test");
                var jsn = File.ReadAllText(rpath);
                jsn.Contains("\"test\"").Should().BeTrue();
                Console.WriteLine(jsn);
            }
        }

        [Test]
        public void IgnoreSavingWhenAbstractPropertyChanges() {
            using (var f = new TempfileLife()) {
                bool saved = false;
                var o = JsonSettings.Load<Settings>(f.FileName).EnableAutosave();
                o.AfterSave += (s, destinition) => { saved = true; };

                o.FileName = "test.jsn";
                saved.ShouldBeEquivalentTo(false);
            }
        }

        [Test]
        public void AccessingAfterLoadingAndMarkingAutosave() {
            using (var f = new TempfileLife()) {
                Console.WriteLine(Path.GetFullPath(f.FileName));
                var o = JsonSettings.Load<Settings>(f.FileName).EnableAutosave();
                o.property.ShouldBeEquivalentTo(null);
                o.property = "test";
                var o2 = JsonSettings.Load<Settings>(f.FileName).EnableAutosave();
                o2.property.ShouldBeEquivalentTo("test");
            }
        }

        [Test]
        public void SavingInterface() {
            using (var f = new TempfileLife()) {
                var rpath = JsonSettings.ResolvePath(f);
                ISettings o = JsonSettings.Load<InterfacedSettings>(f.FileName).EnableIAutosave<InterfacedSettings, ISettings>();

                Console.WriteLine(File.ReadAllText(rpath));
                o.property.ShouldBeEquivalentTo(null);
                o.property = "test";
                var o2 = JsonSettings.Load<InterfacedSettings>(f.FileName);
                o2.property.ShouldBeEquivalentTo("test");

                var jsn = File.ReadAllText(rpath);
                jsn.Contains("\"test\"").Should().BeTrue();
                Console.WriteLine(jsn);
            }
        }
        [Test]
        public void SavingInterface_NonVirtual() {
            using (var f = new TempfileLife()) {
                var rpath = JsonSettings.ResolvePath(f);
                ISettings o = JsonSettings.Load<NonVirtualSettings>(f.FileName).EnableIAutosave<NonVirtualSettings, ISettings>();

                Console.WriteLine(File.ReadAllText(rpath));
                o.property.ShouldBeEquivalentTo(null);
                o.property = "test";
                var o2 = JsonSettings.Load<InterfacedSettings>(f.FileName);
                o2.property.ShouldBeEquivalentTo("test");

                var jsn = File.ReadAllText(rpath);
                jsn.Contains("\"test\"").Should().BeTrue();
                Console.WriteLine(jsn);
            }
        }

        [Test]
        public void SuspendAutosaving_Case1() {
            using (var f = new TempfileLife()) {
                var o = JsonSettings.Load<SettingsBag>(f);

                dynamic d = o.AsDynamic();

                d.SomeProp = "Works";
                d.Num = 1;
                Assert.True(d["SomeProp"] == "Works");
                Assert.True(d.Num == 1);

                o.Save();
                o = JsonSettings.Configure<SettingsBag>(f)
                                .LoadNow()
                                .EnableAutosave();

                o["SomeProp"].Should().Be("Works");
                o["Num"].Should().Be(1L); //newtonsoft deserializes numbers as long.
                StrongBox<int> a = new StrongBox<int>();
                o.AfterSave += (sender, destinition) => { a.Value++; };

                using (o.SuspendAutosave()) {
                    o["SomeProp"] = "Works2";
                    o["Num"] = 2;
                    a.Value.Should().Be(0);
                    var k = JsonSettings.Load<SettingsBag>(f);
                    k["SomeProp"].Should().Be("Works");
                    k["Num"].Should().Be(1L); //newtonsoft deserializes numbers as long.
                    a.Value.Should().Be(0);
                }

                a.Value.Should().Be(1);

                var kk = JsonSettings.Load<SettingsBag>(f);
                kk["SomeProp"].Should().Be("Works2");
                kk["Num"].Should().Be(2L); //newtonsoft deserializes numbers as long.
            }
        }

        public interface ISettings {
            string property { get; set; }

            void Method();
        }

        public class InvalidSettings : JsonSettings {
            #region Overrides of JsonSettings

            /// <summary>
            ///     Serves as a reminder where to save or from where to load (if it is loaded on construction and doesnt change between constructions).<br></br>
            ///     Can be relative to executing file's directory.
            /// </summary>
            public override string FileName { get; set; } = "somename.jsn";

            public string property { get; set; }

            public void Method() { }

            public InvalidSettings() { }
            public InvalidSettings(string fileName) : base(fileName) { }

            #endregion
        }

        public class Settings : JsonSettings {
            #region Overrides of JsonSettings

            /// <summary>
            ///     Serves as a reminder where to save or from where to load (if it is loaded on construction and doesnt change between constructions).<br></br>
            ///     Can be relative to executing file's directory.
            /// </summary>
            public override string FileName { get; set; } = "somename.jsn";

            public virtual string property { get; set; }

            public virtual void Method() { }

            public Settings() { }
            public Settings(string fileName) : base(fileName) { }

            #endregion
        }

        public class NonVirtualSettings : JsonSettings, ISettings {
            #region Overrides of JsonSettings

            /// <summary>
            ///     Serves as a reminder where to save or from where to load (if it is loaded on construction and doesnt change between constructions).<br></br>
            ///     Can be relative to executing file's directory.
            /// </summary>
            public override string FileName { get; set; } = "somename.jsn";

            public string property { get; set; }
            public void Method() { }

            public NonVirtualSettings() { }
            public NonVirtualSettings(string fileName) : base(fileName) { }

            #endregion
        }

        public class InterfacedSettings : JsonSettings, ISettings {
            #region Overrides of JsonSettings

            /// <summary>
            ///     Serves as a reminder where to save or from where to load (if it is loaded on construction and doesnt change between constructions).<br></br>
            ///     Can be relative to executing file's directory.
            /// </summary>
            public override string FileName { get; set; } = "somename.jsn";

            public virtual string property { get; set; }

            public virtual void Method() { }

            public InterfacedSettings() { }
            public InterfacedSettings(string fileName) : base(fileName) { }

            #endregion
        }
    }
}