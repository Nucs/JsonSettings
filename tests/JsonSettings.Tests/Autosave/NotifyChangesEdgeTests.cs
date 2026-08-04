using System;
using System.Collections.Generic;
using System.ComponentModel;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.NotifyChanges;
using Nucs.JsonSettings.Examples;

namespace Nucs.JsonSettings.Tests.Autosave {
    /// <summary>
    ///     Edge cases in <see cref="NotifyChangesRuntime"/> that the main <c>NotifyChanges</c> suite does
    ///     not reach: a write-only property (no getter to read the old value), a <c>new</c>-shadowed
    ///     property (ambiguous reflection lookup), <see cref="NotificationGuard.SkipNullOrDefault"/> on a
    ///     value type (boxed-default comparison), and a getter that throws during the pre-assignment read.
    /// </summary>
    [TestClass]
    public class NotifyChangesEdgeTests {
        private static List<string> Record(INotifyPropertyChanged source) {
            var names = new List<string>();
            source.PropertyChanged += (_, e) => names.Add(e.PropertyName);
            return names;
        }

        [TestMethod]
        public void WriteOnlyProperty_OnlyChanged_BehavesAsAlways() {
            var o = new WriteOnlyNotify();
            var raised = Record(o);

            //OnlyChanged wants to read the old value, but a write-only property has no getter, so the
            //guard falls back to "always" -- each write notifies even with the same value.
            o.WriteOnly = "same";
            o.WriteOnly = "same";

            raised.Should().Equal(new[] { "WriteOnly", "WriteOnly" });
        }

        [TestMethod]
        public void ShadowedNewProperty_ResolvesMostDerived_AndNotifies() {
            var o = new DerivedShadow();
            var raised = Record(o);

            //Setting the 'new'-shadowed property drives an ambiguous reflection lookup (base + derived
            //declare 'Shadowed'); the runtime must resolve the most-derived one and still notify.
            o.Shadowed = "value";

            raised.Should().ContainSingle().Which.Should().Be("Shadowed");
        }

        [TestMethod]
        public void SkipNullOrDefault_OnValueType_SuppressesDefaultButNotOthers() {
            var o = new ValueTypeNotify();
            var raised = Record(o);

            o.Count = 5;   //non-default value -> fires
            o.Count = 0;   //the type's default (0) -> suppressed by SkipNullOrDefault

            raised.Should().Equal(new[] { "Count" },
                "SkipNullOrDefault drops a write of the value type's default via the boxed-default comparison");
        }

        [TestMethod]
        public void ThrowingGetter_DuringPreRead_IsSwallowed_AndStillNotifies() {
            var o = new ThrowingGetterNotify();
            var raised = Record(o);

            //OnlyChanged reads the getter before assigning; a throwing getter must not crash the setter.
            //The read falls back to "no old value", so the write counts as a change and notifies.
            new Action(() => o.Volatile = "x").Should().NotThrow();
            raised.Should().ContainSingle().Which.Should().Be("Volatile");
        }

        // ---- settings types -------------------------------------------------------------------

        [NotifyChanges]
        public class WriteOnlyNotify : NotifiyingJsonSettings {
            public override string FileName { get; set; } = "wo.notify.jsn";
            private string _w;

            [JsonIgnore]
            public string WriteOnly {
                set { _w = value; }
            }
        }

        [NotifyChanges]
        public class BaseShadow : NotifiyingJsonSettings {
            public override string FileName { get; set; } = "shadow.notify.jsn";
            [JsonIgnore] public virtual object Shadowed { get; set; }
        }

        [NotifyChanges]
        public class DerivedShadow : BaseShadow {
            //A 'new' property of the same name and a different type: reflection sees both and the
            //property lookup is ambiguous.
            [JsonIgnore] public new string Shadowed { get; set; }
        }

        [NotifyChanges]
        public class ValueTypeNotify : NotifiyingJsonSettings {
            public override string FileName { get; set; } = "vt.notify.jsn";

            [NotifyChanges(Guard = NotificationGuard.SkipNullOrDefault)]
            public int Count { get; set; }
        }

        [NotifyChanges]
        public class ThrowingGetterNotify : NotifiyingJsonSettings {
            public override string FileName { get; set; } = "tg.notify.jsn";
            private string _v;

            [JsonIgnore]
            public string Volatile {
                get => throw new InvalidOperationException("getter deliberately throws");
                set => _v = value;
            }
        }
    }
}
