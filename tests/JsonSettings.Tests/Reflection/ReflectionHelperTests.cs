using System;
using System.Reflection;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Reflection;

namespace Nucs.JsonSettings.Tests.Reflection {
    /// <summary>
    ///     Unit coverage for <see cref="ReflectionHelper"/> -- the accessor cache the notification hot path
    ///     uses in place of <see cref="PropertyInfo.GetValue(object)"/> and
    ///     <see cref="MethodBase.Invoke(object,object[])"/>.
    /// </summary>
    /// <remarks>
    ///     These run on the JIT test host, so <see cref="ReflectionHelper.CanCompile"/> is true and they
    ///     exercise the compiled path. The reflective fallback taken under Native AOT is behaviourally
    ///     identical -- same delegate shape, same values -- and is what the harness in docs/AOT.md measures
    ///     end to end. The non-public cases below pin the parity that matters most: the old hot path used
    ///     <see cref="PropertyInfo.GetValue(object)"/>, which ignores accessibility, so the compiled
    ///     accessor must read a private getter and call a non-public method just the same.
    /// </remarks>
    [TestClass]
    public class ReflectionHelperTests {
        private enum Color { None = 0, Red = 1, Blue = 2 }

        private class Sample {
            public string Text { get; set; } = "init";
            public int Number { get; set; } = 7;
            public Color Shade { get; set; } = Color.Red;
            private string Hidden { get; set; } = "hidden";
            public string SetOnly { set { /* no getter -- BuildGetter must reject this */ } }

            public string Observed = "<unset>";
            public void Record(string value) => Observed = value;
            private void RecordPrivately(string value) => Observed = "p:" + value;

            public static PropertyInfo HiddenProperty =>
                typeof(Sample).GetProperty(nameof(Hidden), BindingFlags.NonPublic | BindingFlags.Instance);
            public static MethodInfo RecordPrivatelyMethod =>
                typeof(Sample).GetMethod(nameof(RecordPrivately), BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private class VirtualBase {
            public virtual string Label => "base";
        }

        private class VirtualDerived : VirtualBase {
            public override string Label => "derived";
        }

        private static PropertyInfo Prop(string name) => typeof(Sample).GetProperty(name);

        [TestMethod]
        public void Getter_ReadsReferenceProperty() {
            ReflectionHelper.Getter(Prop(nameof(Sample.Text)))(new Sample { Text = "hello" })
                          .Should().Be("hello");
        }

        [TestMethod]
        public void Getter_ReadsAndBoxesValueType() {
            ReflectionHelper.Getter(Prop(nameof(Sample.Number)))(new Sample { Number = 42 })
                          .Should().Be(42);
        }

        [TestMethod]
        public void Getter_ReadsEnum() {
            ReflectionHelper.Getter(Prop(nameof(Sample.Shade)))(new Sample { Shade = Color.Blue })
                          .Should().Be(Color.Blue);
        }

        [TestMethod]
        public void Getter_ReadsNonPublicGetter_ParityWithReflection() {
            var property = Sample.HiddenProperty;
            var sample = new Sample();
            //PropertyInfo.GetValue -- the mechanism the hot path used to use -- ignores accessibility; the
            //compiled accessor must read the same private getter to the same value.
            ReflectionHelper.Getter(property)(sample).Should().Be(property.GetValue(sample)).And.Be("hidden");
        }

        [TestMethod]
        public void Getter_VirtualProperty_DispatchesToMostDerived() {
            //Built from the base declaration but invoked on a derived instance: callvirt must resolve the override.
            ReflectionHelper.Getter(typeof(VirtualBase).GetProperty(nameof(VirtualBase.Label)))(new VirtualDerived())
                          .Should().Be("derived");
        }

        [TestMethod]
        public void Getter_IsCachedPerProperty() {
            var property = Prop(nameof(Sample.Text));
            ((object) ReflectionHelper.Getter(property)).Should().BeSameAs(ReflectionHelper.Getter(property),
                "the delegate is built once per PropertyInfo and reused");
        }

        [TestMethod]
        public void Getter_NullProperty_Throws() {
            Action act = () => ReflectionHelper.Getter(null);
            act.Should().Throw<ArgumentNullException>();
        }

        [TestMethod]
        public void Getter_SetOnlyProperty_Throws() {
            Action act = () => ReflectionHelper.Getter(Prop(nameof(Sample.SetOnly)));
            act.Should().Throw<ArgumentException>("a property with no get accessor cannot produce a getter");
        }

        [TestMethod]
        public void StringActionInvoker_CallsPublicVoidStringMethod() {
            var sample = new Sample();
            ReflectionHelper.StringActionInvoker(typeof(Sample).GetMethod(nameof(Sample.Record)))(sample, "hi");
            sample.Observed.Should().Be("hi");
        }

        [TestMethod]
        public void StringActionInvoker_CallsNonPublicMethod() {
            var sample = new Sample();
            ReflectionHelper.StringActionInvoker(Sample.RecordPrivatelyMethod)(sample, "x");
            sample.Observed.Should().Be("p:x");
        }

        [TestMethod]
        public void StringActionInvoker_IsCachedPerMethod() {
            var method = typeof(Sample).GetMethod(nameof(Sample.Record));
            ((object) ReflectionHelper.StringActionInvoker(method)).Should().BeSameAs(ReflectionHelper.StringActionInvoker(method));
        }

        [TestMethod]
        public void StringActionInvoker_NullMethod_Throws() {
            Action act = () => ReflectionHelper.StringActionInvoker(null);
            act.Should().Throw<ArgumentNullException>();
        }

        [TestMethod]
        public void CanCompile_IsTrueOnJitHost() {
            ReflectionHelper.CanCompile.Should().BeTrue(
                "the test host is a JIT runtime, so accessors compile rather than fall back to reflection");
        }
    }
}
