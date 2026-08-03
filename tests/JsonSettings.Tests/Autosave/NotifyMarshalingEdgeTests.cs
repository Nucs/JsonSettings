using System;
using System.Threading;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.NotifyChanges;

namespace Nucs.JsonSettings.Tests.Autosave {
    /// <summary>
    ///     Argument-guard and no-op coverage for <see cref="NotifyMarshalingExtensions"/>. The behavioural
    ///     marshalling tests exercise the happy path (post / inline / disable / no-ambient-context); the
    ///     null-argument guards on all three methods and the "disable when never enabled" no-op were not
    ///     reached.
    /// </summary>
    [TestClass]
    public class NotifyMarshalingEdgeTests {
        [TestMethod]
        public void Enable_NullSettings_Throws() {
            //Guarded before the context is even read, so it is the null settings that is reported.
            new Action(() => NotifyMarshalingExtensions.EnableNotificationMarshaling<MarshalEdgeSettings>(null!))
                .Should().Throw<ArgumentNullException>();
        }

        [TestMethod]
        public void EnableWithExplicitContext_NullSettings_Throws() {
            new Action(() => NotifyMarshalingExtensions.EnableNotificationMarshaling<MarshalEdgeSettings>(null!, new SynchronizationContext()))
                .Should().Throw<ArgumentNullException>();
        }

        [TestMethod]
        public void EnableWithExplicitContext_NullContext_Throws() {
            var s = JsonSettings.Construct<MarshalEdgeSettings>();
            new Action(() => s.EnableNotificationMarshaling((SynchronizationContext) null!))
                .Should().Throw<ArgumentNullException>();
        }

        [TestMethod]
        public void Disable_NullSettings_Throws() {
            new Action(() => NotifyMarshalingExtensions.DisableNotificationMarshaling<MarshalEdgeSettings>(null!))
                .Should().Throw<ArgumentNullException>();
        }

        [TestMethod]
        public void Disable_WhenNeverEnabled_IsNoOpReturningSameInstance() {
            var s = JsonSettings.Construct<MarshalEdgeSettings>();
            //Documented no-op: disabling something that was never enabled must neither throw nor swap the
            //instance out from under a fluent chain.
            s.DisableNotificationMarshaling().Should().BeSameAs(s);
        }

        public class MarshalEdgeSettings : JsonSettings {
            public override string FileName { get; set; }
            public string Name { get; set; }
            public MarshalEdgeSettings() { }
            public MarshalEdgeSettings(string fileName) : base(fileName) { }
        }
    }
}
