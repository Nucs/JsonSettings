using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.NotifyChanges;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Autosave {
    /// <summary>
    ///     Opt-in <see cref="SynchronizationContext"/> marshalling: <c>EnableNotificationMarshaling</c>
    ///     routes the change notifications a woven setter raises to a captured context, so a write off
    ///     the UI thread still raises <c>PropertyChanged</c> on it.
    /// </summary>
    [TestClass]
    public class NotifyChangesMarshalingTests {
        /// <summary>A context that queues posts instead of running them, so a test can assert the post
        /// happened and then drain it deliberately -- standing in for a UI message loop.</summary>
        private sealed class RecordingSyncContext : SynchronizationContext {
            public int PostCount;
            private readonly List<(SendOrPostCallback Callback, object State)> _queue = new();

            public override void Post(SendOrPostCallback d, object state) {
                PostCount++;
                _queue.Add((d, state));
            }

            public void Drain() {
                foreach (var (callback, state) in _queue.ToArray())
                    callback(state);
                _queue.Clear();
            }
        }

        private static List<string> Record(INotifyPropertyChanged source) {
            var names = new List<string>();
            source.PropertyChanged += (_, e) => names.Add(e.PropertyName);
            return names;
        }

        [TestMethod]
        public void MarshaledRaise_IsPosted_ThenDeliveredOnDrain() {
            using var f = new TempFile();
            var ctx = new RecordingSyncContext();
            var o = JsonSettings.Load<MarshalSettings>(f.FileName).EnableNotificationMarshaling(ctx);
            var raised = Record(o);

            o.Name = "x"; //ctx is not the current context on this thread, so the raise is posted

            raised.Should().BeEmpty("the notification was posted to the context, not delivered inline");
            ctx.PostCount.Should().Be(1);

            ctx.Drain();
            raised.Should().Equal(new[] { "Name" }, "draining the context delivers the marshalled notification");
        }

        [TestMethod]
        public void Dependents_AreMarshalled_InTheSameBatch() {
            using var f = new TempFile();
            var ctx = new RecordingSyncContext();
            var o = JsonSettings.Load<MarshalSettings>(f.FileName).EnableNotificationMarshaling(ctx);
            var raised = Record(o);

            o.First = "a";

            ctx.PostCount.Should().Be(1, "the source and its [NotifyChangesFor] dependents post as one batch");
            ctx.Drain();
            raised.Should().Equal(new[] { "First", "FullName" });
        }

        [TestMethod]
        public void OnTheCapturedThread_RaisesInline_WithoutPosting() {
            using var f = new TempFile();
            var ctx = new RecordingSyncContext();
            var previous = SynchronizationContext.Current;
            try {
                SynchronizationContext.SetSynchronizationContext(ctx);
                var o = JsonSettings.Load<MarshalSettings>(f.FileName).EnableNotificationMarshaling(); //captures ctx as Current
                var raised = Record(o);

                o.Name = "x"; //same thread: Current == captured context, so no post

                ctx.PostCount.Should().Be(0);
                raised.Should().Equal(new[] { "Name" }, "a write on the captured thread raises inline");
            } finally {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        [TestMethod]
        public void Disable_RestoresInlineRaising() {
            using var f = new TempFile();
            var ctx = new RecordingSyncContext();
            var o = JsonSettings.Load<MarshalSettings>(f.FileName).EnableNotificationMarshaling(ctx);
            o.DisableNotificationMarshaling();
            var raised = Record(o);

            o.Name = "x";

            ctx.PostCount.Should().Be(0, "marshalling was disabled");
            raised.Should().Equal(new[] { "Name" }, "so the notification is raised inline again");
        }

        [TestMethod]
        public void Enable_WithoutAmbientContext_Throws() {
            using var f = new TempFile();
            var previous = SynchronizationContext.Current;
            try {
                SynchronizationContext.SetSynchronizationContext(null);
                var o = JsonSettings.Load<MarshalSettings>(f.FileName);

                var act = () => o.EnableNotificationMarshaling();

                act.Should().Throw<JsonSettingsException>("there is no UI-thread context to capture");
            } finally {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        #region settings types

        [NotifyChanges]
        public class MarshalSettings : NotifiyingJsonSettings {
            public override string FileName { get; set; } = "marshal.jsn";

            public string Name { get; set; }

            [NotifyChangesFor(nameof(FullName))]
            public string First { get; set; }

            public string FullName => First + " Last";

            public MarshalSettings() { }
            public MarshalSettings(string fileName) : base(fileName) { }
        }

        #endregion
    }
}
