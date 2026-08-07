using System.ComponentModel;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Autosave {
    /// <summary>
    ///     Covers <see cref="NotificationBinder"/>'s handling of a nested property whose value implements
    ///     <see cref="INotifyPropertyChanged"/> (as opposed to the collection case the existing tests
    ///     cover). This exercises Subscribe/Unsubscribe for a nested notifier, the save-on-nested-change
    ///     handler, rebinding when the property is replaced, unbinding on dispose, and
    ///     <see cref="NotificationBinder.CanHandleProperty"/>.
    /// </summary>
    [TestClass]
    public class NestedNotifierTests {
        private static NotificationBinder BinderOf(NotifyingParent p) =>
            (NotificationBinder) p.Modulation.GetModule<AutosaveModule>().NotificationsHandler!;

        [TestMethod]
        public void NestedPropertyChanged_TriggersSave() {
            using var f = new TempFile();
            var o = JsonSettings.Load<NotifyingParent>(f.FileName).EnableAutosave();

            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            //Mutating the nested notifier (not the parent property) must save through the binder.
            o.Child.Name = "changed";
            saves.Should().Be(1);
        }

        [TestMethod]
        public void ReplacingNestedProperty_RebindsToNewValue_AndDropsOld() {
            using var f = new TempFile();
            var o = JsonSettings.Load<NotifyingParent>(f.FileName).EnableAutosave();

            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            var oldChild = o.Child;
            o.Child = new NestedChild(); //woven setter saves once; binder rebinds subscriptions
            saves.Should().Be(1);

            //The freshly assigned child is now watched.
            o.Child.Name = "new";
            saves.Should().Be(2);

            //The replaced child is no longer watched, so mutating it does nothing.
            oldChild.Name = "stale";
            saves.Should().Be(2, "the old nested notifier was unsubscribed when the property was replaced");
        }

        [TestMethod]
        public void Dispose_UnbindsNestedNotifier() {
            using var f = new TempFile();
            var o = JsonSettings.Load<NotifyingParent>(f.FileName).EnableAutosave();

            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            var child = o.Child;
            o.Dispose();

            child.Name = "after-dispose";
            saves.Should().Be(0, "disposing the settings unbinds the nested notifier handlers");
        }

        [TestMethod]
        public void CanHandleProperty_ReflectsBindableSet() {
            using var f = new TempFile();
            var o = JsonSettings.Load<NotifyingParent>(f.FileName).EnableAutosave();

            var binder = BinderOf(o);
            binder.CanHandleProperty("Child").Should().BeTrue();
            binder.CanHandleProperty("NoSuchProperty").Should().BeFalse();
        }

        public class NestedChild : INotifyPropertyChanged {
            private string _name;

            public string Name {
                get => _name;
                set {
                    if (_name == value) return;
                    _name = value;
                    OnPropertyChanged();
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected void OnPropertyChanged([CallerMemberName] string propertyName = null) {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        [Autosave]
        public class NotifyingParent : NotifiyingJsonSettings {
            public override string FileName { get; set; } = "nested.notifier.jsn";
            private NestedChild _child = new NestedChild();

            public virtual NestedChild Child {
                get => _child;
                set {
                    if (Equals(value, _child)) return;
                    _child = value;
                    OnPropertyChanged();
                }
            }

            public NotifyingParent() { }
            public NotifyingParent(string fileName) : base(fileName) { }
        }
    }
}
