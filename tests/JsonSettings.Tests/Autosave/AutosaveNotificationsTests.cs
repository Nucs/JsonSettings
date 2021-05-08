using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Examples;
using Nucs.JsonSettings.xTests.Utils;
using NUnit.Framework;

namespace Nucs.JsonSettings.xTests.Autosave {
    [TestFixture]
    public class AutosaveNotificationsTests {
        /// <summary>Initializes a new instance of the <see cref="T:System.Object" /> class.</summary>
        public AutosaveNotificationsTests() { }

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
        public void Saving_Example() {
            using (var f = new TempfileLife()) {
                var rpath = JsonSettings.ResolvePath(f);

                StrongBox<int> saved = new StrongBox<int>(0);
                var o = JsonSettings.Load<ExampleNotifyingSettings>(f).EnableAutosave();
                saved.Value.Should().Be(0);
                o.AfterSave += (s, destinition) => { saved.Value++; };
                o.Residents.Add("Cookie Monster"); //Boom! saves.
                saved.Value.Should().Be(1);
                o.Residents = new ObservableCollection<string>(); //Boom! saves.
                saved.Value.Should().Be(2);
                o.Residents.Add("Cookie Monster"); //Boom! saves.
                saved.Value.Should().Be(3);
                o.NonAutosavingProperty = new ObservableCollection<object>(); //doesn't save
                o.NonAutosavingProperty.Add("Jim"); //doesn't save
                saved.Value.Should().Be(3);
                o.Street += "-1"; //Boom! saves.
                saved.Value.Should().Be(4);
                o.AutoProperty = "Hello"; //Boom! saves.
                saved.Value.Should().Be(5);
                o.IgnoredFromAutosaving = "Hello"; //doesn't save
                saved.Value.Should().Be(5);
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
                var o = JsonSettings.Load<InterfacedSettings>(f.FileName).EnableIAutosave<InterfacedSettings, ISettings>();

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

        public interface ISettings {
            string property { get; set; }

            void Method();
        }

        public class InvalidSettings : NotifiyingJsonSettings {
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

        public class Settings : NotifiyingJsonSettings {
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

        public class InterfacedSettings : NotifiyingJsonSettings, ISettings {
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

    public class ExampleNotifyingSettings : NotifiyingJsonSettings {
        public override string FileName { get; set; } = "some.default.just.in.case.jsn";
        private string _street = "Sesamee Street 123";
        private ObservableCollection<object> _nonAutosavingProperty;
        private ObservableCollection<string> _residents = new ObservableCollection<string>();

        [IgnoreAutosave]
        public virtual ObservableCollection<object> NonAutosavingProperty {
            get => _nonAutosavingProperty;
            set {
                if (Equals(value, _nonAutosavingProperty)) return;
                _nonAutosavingProperty = value;
                OnPropertyChanged();
            }
        }

        public virtual string Street {
            get => _street;
            set {
                if (value == _street) return;
                _street = value;
                OnPropertyChanged();
            }
        }

        public virtual string AutoProperty { get; set; }


        [IgnoreAutosave]
        public virtual string IgnoredFromAutosaving {
            get;
            set;
        }
        
        public virtual ObservableCollection<string> Residents {
            get => _residents;
            set {
                if (Equals(value, _residents)) return;
                _residents = value;
                OnPropertyChanged();
            }
        }

        public ExampleNotifyingSettings() { }
        public ExampleNotifyingSettings(string fileName) : base(fileName) { }
    }
}