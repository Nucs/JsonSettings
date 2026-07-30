using System;
using System.IO;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Tests.Utils;


namespace Nucs.JsonSettings.Tests.Autosave {
    [TestClass]
    public class AutosaveTests {
        /// <summary>Initializes a new instance of the <see cref="T:System.Object" /> class.</summary>
        public AutosaveTests() { }


        /// <summary>
        ///     Enabling autosave on a class that was never woven has to fail loudly.
        /// </summary>
        /// <remarks>
        ///     This test used to assert that a class with no virtual properties was rejected,
        ///     because a Castle class proxy silently ignored writes to non-virtual members. That
        ///     restriction is gone (see NonVirtualSettings below, which now works), but the failure
        ///     it protected against has an exact analogue: a class with no [Autosave] is never
        ///     woven, so EnableAutosave would attach a module that nothing ever calls and every
        ///     write would be silently dropped. Same silent-data-loss shape, so it still throws.
        /// </remarks>
        [TestMethod]
        public void ClassNotMarkedAutosave_Throws() {
            using var f = new TempFile();
            var o = JsonSettings.Load<InvalidSettings>(f.FileName);
            new Action(() => o.EnableAutosave()).Should().Throw<JsonSettingsException>()
                                                .WithMessage("*is not marked with*");
        }

        /// <summary>
        ///     EnableAutosave returns the instance it was handed, not a proxy wrapping it.
        /// </summary>
        /// <remarks>
        ///     This replaces an assertion that the returned object's namespace was
        ///     "Castle.Proxies". That was the most consequential thing about the old
        ///     implementation: the returned object was a DIFFERENT object, so any reference taken
        ///     before EnableAutosave kept pointing at an instance that did not autosave, and
        ///     non-virtual members read the proxy's own default-initialised fields rather than the
        ///     loaded values. Weaving rewrites the setters in place, so there is exactly one object
        ///     and that whole class of bug is gone.
        /// </remarks>
        [TestMethod]
        public void EnableAutosave_ReturnsTheSameInstance() {
            using var f = new TempFile();
            var loaded = JsonSettings.Load<Settings>(f.FileName);
            var enabled = loaded.EnableAutosave();

            ReferenceEquals(loaded, enabled).Should().BeTrue("weaving does not introduce a second object");
            enabled.GetType().Should().Be<Settings>("no proxy type is generated");
        }

        [TestMethod]
        public void Saving() {
            using var f = new TempFile();
            var rpath = JsonSettings.ResolvePath(f);

            var saved = false;
            var o = JsonSettings.Load<Settings>(f.FileName).EnableAutosave();
            o.AfterSave += (s, destinition) => { saved = true; };
            o.property.Should().BeNull();
            Console.WriteLine(File.ReadAllText(rpath));

            o.property = "test";
            saved.Should().Be(true);
            var o2 = JsonSettings.Load<Settings>(f.FileName).EnableAutosave();
            o2.property.Should().Be("test");
            var jsn = File.ReadAllText(rpath);
            jsn.Contains("\"test\"").Should().BeTrue();
            Console.WriteLine(jsn);
        }

        [TestMethod]
        public void IgnoreSavingWhenAbstractPropertyChanges() {
            using var f = new TempFile();
            var saved = false;
            var o = JsonSettings.Load<Settings>(f.FileName).EnableAutosave();
            o.AfterSave += (s, destinition) => { saved = true; };

            o.FileName = "test.jsn";
            saved.Should().Be(false);
        }

        [TestMethod]
        public void AccessingAfterLoadingAndMarkingAutosave() {
            using var f = new TempFile();
            Console.WriteLine(Path.GetFullPath(f.FileName));
            var o = JsonSettings.Load<Settings>(f.FileName).EnableAutosave();
            o.property.Should().BeNull();
            o.property = "test";
            var o2 = JsonSettings.Load<Settings>(f.FileName).EnableAutosave();
            o2.property.Should().Be("test");
        }

        [TestMethod]
        public void SavingInterface() {
            using var f = new TempFile();
            var rpath = JsonSettings.ResolvePath(f);
            var o = JsonSettings.Load<InterfacedSettings>(f.FileName).EnableIAutosave<InterfacedSettings, ISettings>();

            Console.WriteLine(File.ReadAllText(rpath));
            o.property.Should().BeNull();
            o.property = "test";
            var o2 = JsonSettings.Load<InterfacedSettings>(f.FileName);
            o2.property.Should().Be("test");

            var jsn = File.ReadAllText(rpath);
            jsn.Contains("\"test\"").Should().BeTrue();
            Console.WriteLine(jsn);
        }
        [TestMethod]
        public void SavingInterface_NonVirtual() {
            using var f = new TempFile();
            var rpath = JsonSettings.ResolvePath(f);
            var o = JsonSettings.Load<NonVirtualSettings>(f.FileName).EnableIAutosave<NonVirtualSettings, ISettings>();

            Console.WriteLine(File.ReadAllText(rpath));
            o.property.Should().BeNull();
            o.property = "test";
            var o2 = JsonSettings.Load<InterfacedSettings>(f.FileName);
            o2.property.Should().Be("test");

            var jsn = File.ReadAllText(rpath);
            jsn.Contains("\"test\"").Should().BeTrue();
            Console.WriteLine(jsn);
        }

        [TestMethod]
        public void SuspendAutosaving_Case1() {
            using var f = new TempFile();
            var o = JsonSettings.Load<SettingsBag>(f);

            var d = o.AsDynamic();

            d.SomeProp = "Works";
            d.Num = 1;
            Assert.IsTrue(d["SomeProp"] == "Works");
            Assert.IsTrue(d.Num == 1);

            o.Save();
            o = JsonSettings.Configure<SettingsBag>(f)
                            .LoadNow()
                            .EnableAutosave();

            o["SomeProp"].Should().Be("Works");
            o["Num"].Should().Be(1L); //newtonsoft deserializes numbers as long.
            var a = new StrongBox<int>();
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

        [Autosave]
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

        [Autosave]
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

        [Autosave]
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