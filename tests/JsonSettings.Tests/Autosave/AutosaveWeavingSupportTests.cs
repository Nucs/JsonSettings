using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Autosave {
    /// <summary>
    ///     Covers the class shapes that Castle.DynamicProxy could not support and compile-time
    ///     weaving can.
    /// </summary>
    /// <remarks>
    ///     Every test here is a scenario that either threw, or silently did nothing, under the
    ///     proxy-based implementation. A class proxy can only intervene on members it is allowed
    ///     to override, so the old library demanded `virtual` on every property, refused sealed
    ///     classes outright, and returned a different object than the one it was given. Weaving
    ///     rewrites the setter where it is declared, so none of those constraints survive.
    /// </remarks>
    [TestClass]
    public class AutosaveWeavingSupportTests {
        /// <summary>
        ///     A plain class with plain properties. This is the headline change: the old
        ///     implementation threw JsonSettingsException here because nothing was virtual.
        /// </summary>
        [TestMethod]
        public void PlainNonVirtualClass_Autosaves() {
            using var f = new TempFile();
            var o = JsonSettings.Load<PlainSettings>(f.FileName).EnableAutosave();

            o.Name = "changed";
            o.Number = 42;

            var reloaded = JsonSettings.Load<PlainSettings>(f.FileName);
            reloaded.Name.Should().Be("changed");
            reloaded.Number.Should().Be(42);
        }

        /// <summary>
        ///     A sealed class cannot be subclassed, so a class proxy for it was impossible.
        /// </summary>
        [TestMethod]
        public void SealedClass_Autosaves() {
            using var f = new TempFile();
            var o = JsonSettings.Load<SealedSettings>(f.FileName).EnableAutosave();

            o.Value = "sealed-ok";

            JsonSettings.Load<SealedSettings>(f.FileName).Value.Should().Be("sealed-ok");
        }

        /// <summary>
        ///     A property with a public getter and a private setter, written from inside the class,
        ///     is detected and persisted.
        /// </summary>
        /// <remarks>
        ///     Note what this does NOT assert: that the value comes back on the next Load. It does
        ///     not, and that is a Newtonsoft rule rather than an autosave one -- Json.NET serialises
        ///     a public getter but will not populate a non-public setter unless the property opts in
        ///     with [JsonProperty]. The distinction matters because the two halves fail in different
        ///     places: autosave's job is to notice the write and get it onto disk, which is asserted
        ///     here against the file's actual contents. See
        ///     <see cref="PrivateSetter_WithJsonProperty_RoundTrips"/> for the complete round-trip.
        /// </remarks>
        [TestMethod]
        public void PrivateSetter_IsDetectedAndPersisted() {
            using var f = new TempFile();
            var o = JsonSettings.Load<PrivateSetterSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            o.Promote("elevated");

            saves.Should().Be(1, "a private setter is still a setter and is woven");
            o.Value.Should().Be("elevated");
            File.ReadAllText(JsonSettings.ResolvePath(f)).Should().Contain("elevated",
                "the write must reach the file even though the setter is private");
        }

        /// <summary>
        ///     The same property round-trips once it carries [JsonProperty].
        /// </summary>
        [TestMethod]
        public void PrivateSetter_WithJsonProperty_RoundTrips() {
            using var f = new TempFile();
            var o = JsonSettings.Load<PrivateSetterJsonPropertySettings>(f.FileName).EnableAutosave();

            o.Promote("elevated");

            JsonSettings.Load<PrivateSetterJsonPropertySettings>(f.FileName).Value.Should().Be("elevated");
        }

        /// <summary>
        ///     Because there is no proxy, a reference captured before EnableAutosave is the same
        ///     object and autosaves too.
        /// </summary>
        /// <remarks>
        ///     Under Castle this was the sharpest edge in the library. EnableAutosave returned a
        ///     proxy, so the original reference kept pointing at an object that did not autosave,
        ///     and the two could drift apart silently. Anyone who wrote
        ///     `var s = Load&lt;T&gt;(); s.EnableAutosave(); s.Foo = 1;` -- discarding the return
        ///     value -- got no autosaving at all and no diagnostic.
        /// </remarks>
        [TestMethod]
        public void AliasedReference_AlsoAutosaves() {
            using var f = new TempFile();
            var original = JsonSettings.Load<PlainSettings>(f.FileName);
            var returned = original.EnableAutosave();

            ReferenceEquals(original, returned).Should().BeTrue();

            original.Name = "written-through-the-original-reference";

            JsonSettings.Load<PlainSettings>(f.FileName).Name
                        .Should().Be("written-through-the-original-reference");
        }

        /// <summary>
        ///     Value-typed and nullable properties round-trip like any other.
        /// </summary>
        [TestMethod]
        public void ValueTypeProperties_Autosave() {
            using var f = new TempFile();
            var o = JsonSettings.Load<PlainSettings>(f.FileName).EnableAutosave();

            o.When = new DateTime(2026, 7, 28, 1, 2, 3, DateTimeKind.Utc);
            o.Maybe = 7;

            var reloaded = JsonSettings.Load<PlainSettings>(f.FileName);
            reloaded.When.Should().Be(new DateTime(2026, 7, 28, 1, 2, 3, DateTimeKind.Utc));
            reloaded.Maybe.Should().Be(7);
        }

        /// <summary>
        ///     The two opt-out attributes still work on a non-virtual class.
        /// </summary>
        [TestMethod]
        public void IgnoredProperties_DoNotAutosave() {
            using var f = new TempFile();
            var o = JsonSettings.Load<PlainSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            o.NotAutosaved = "ignored";
            saves.Should().Be(0, "[IgnoreAutosave] properties must not trigger a save");

            o.NotSerialized = "ignored too";
            saves.Should().Be(0, "[JsonIgnore] properties must not trigger a save");

            o.Name = "real";
            saves.Should().Be(1, "an ordinary property still saves");
        }

        /// <summary>
        ///     A woven setter is inert until EnableAutosave attaches a module.
        /// </summary>
        /// <remarks>
        ///     This matters more than it looks. The attribute is woven into the TYPE, so the
        ///     advice runs on every instance -- including the one Newtonsoft populates while
        ///     deserializing inside Load, before any module exists. If the advice were not inert
        ///     there, loading a settings file would re-save it once per property, and worse, would
        ///     do so from inside the load.
        /// </remarks>
        [TestMethod]
        public void WritesBeforeEnableAutosave_DoNotSave() {
            using var f = new TempFile();
            var o = JsonSettings.Load<PlainSettings>(f.FileName);
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            o.Name = "written-before-enabling";
            saves.Should().Be(0, "no module is attached yet");

            File.ReadAllText(JsonSettings.ResolvePath(f)).Should().NotContain("written-before-enabling");

            o.EnableAutosave();
            o.Name = "written-after-enabling";
            saves.Should().Be(1);
        }

        /// <summary>
        ///     Suspension works the same on a plain class as it did on a proxied one.
        /// </summary>
        [TestMethod]
        public void SuspendAutosave_WorksOnPlainClass() {
            using var f = new TempFile();
            var o = JsonSettings.Load<PlainSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            using (o.SuspendAutosave()) {
                o.Name = "a";
                o.Number = 1;
                saves.Should().Be(0, "writes are deferred while suspended");
            }

            saves.Should().Be(1, "exactly one save is committed on resume");
            JsonSettings.Load<PlainSettings>(f.FileName).Name.Should().Be("a");
        }

        /// <summary>
        ///     [Autosave] is not inherited, and weaving follows the declaration, not the hierarchy.
        /// </summary>
        /// <remarks>
        ///     This test pins a real limitation rather than a feature. A setter is rewritten in
        ///     the assembly and type that DECLARES it, so marking only the derived class leaves
        ///     properties declared on the base untouched. ResolveMonitoredProperties walks
        ///     GetProperties, which does include inherited members, so the module considers
        ///     FromBase monitored -- but nothing ever calls in for it. Marking the base class too
        ///     is the fix, and that is asserted here so the behaviour is documented and cannot
        ///     drift silently.
        /// </remarks>
        [TestMethod]
        public void Inheritance_OnlyDeclaringTypeIsWoven() {
            using var f = new TempFile();
            var o = JsonSettings.Load<DerivedSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            o.FromDerived = "derived";
            saves.Should().Be(1, "the derived class carries [Autosave], so its own setters are woven");

            o.FromBase = "base";
            saves.Should().Be(1, "the base class does not carry [Autosave], so its setters were never woven");
        }

        /// <summary>
        ///     Marking the base class as well covers properties it declares.
        /// </summary>
        [TestMethod]
        public void Inheritance_MarkingBothTypesCoversTheWholeHierarchy() {
            using var f = new TempFile();
            var o = JsonSettings.Load<DerivedOfMarkedBaseSettings>(f.FileName).EnableAutosave();
            var saves = 0;
            o.AfterSave += (s, d) => saves++;

            o.FromDerived = "derived";
            o.FromMarkedBase = "base";

            saves.Should().Be(2, "both declaring types are woven");
            JsonSettings.Load<DerivedOfMarkedBaseSettings>(f.FileName)
                        .FromMarkedBase.Should().Be("base");
        }

        /// <summary>
        ///     The shipped Autosave assembly must not depend on Castle.Core any more, and must
        ///     depend on the AspectInjector broker instead.
        /// </summary>
        /// <remarks>
        ///     A regression guard for the whole point of the rewrite. Castle.DynamicProxy builds
        ///     types at runtime with System.Reflection.Emit, which Native AOT cannot execute; if a
        ///     reference to it ever came back, `EnableAutosave` would start throwing
        ///     PlatformNotSupportedException on AOT again and no other test here would notice.
        /// </remarks>
        [TestMethod]
        public void AutosaveAssembly_DoesNotReferenceCastle() {
            var referenced = typeof(AutosaveAttribute).Assembly
                                                      .GetReferencedAssemblies()
                                                      .Select(a => a.Name)
                                                      .ToList();

            referenced.Should().NotContain(n => n != null && n.StartsWith("Castle", StringComparison.OrdinalIgnoreCase),
                                           "DynamicProxy is Reflection.Emit and cannot run under Native AOT");
            referenced.Should().Contain("AspectInjector.Broker");
        }

        #region settings types

        [Autosave]
        public class PlainSettings : JsonSettings {
            public override string FileName { get; set; } = "plain.jsn";

            //deliberately NOT virtual - this is what the old implementation rejected.
            public string Name { get; set; }
            public int Number { get; set; }
            public DateTime When { get; set; }
            public int? Maybe { get; set; }

            [IgnoreAutosave]
            public string NotAutosaved { get; set; }

            [JsonIgnore]
            public string NotSerialized { get; set; }

            public PlainSettings() { }
            public PlainSettings(string fileName) : base(fileName) { }
        }

        [Autosave]
        public sealed class SealedSettings : JsonSettings {
            public override string FileName { get; set; } = "sealed.jsn";
            public string Value { get; set; }

            public SealedSettings() { }
            public SealedSettings(string fileName) : base(fileName) { }
        }

        [Autosave]
        public class PrivateSetterSettings : JsonSettings {
            public override string FileName { get; set; } = "private.jsn";
            public string Value { get; private set; }

            public void Promote(string value) {
                Value = value;
            }

            public PrivateSetterSettings() { }
            public PrivateSetterSettings(string fileName) : base(fileName) { }
        }

        [Autosave]
        public class PrivateSetterJsonPropertySettings : JsonSettings {
            public override string FileName { get; set; } = "privatejp.jsn";

            //[JsonProperty] is what makes Json.NET willing to write through a non-public setter
            //on deserialization; without it the value serialises out but never comes back.
            [JsonProperty]
            public string Value { get; private set; }

            public void Promote(string value) {
                Value = value;
            }

            public PrivateSetterJsonPropertySettings() { }
            public PrivateSetterJsonPropertySettings(string fileName) : base(fileName) { }
        }

        public class BaseSettings : JsonSettings {
            public override string FileName { get; set; } = "base.jsn";
            public string FromBase { get; set; }

            public BaseSettings() { }
            public BaseSettings(string fileName) : base(fileName) { }
        }

        [Autosave]
        public class DerivedSettings : BaseSettings {
            public string FromDerived { get; set; }

            public DerivedSettings() { }
            public DerivedSettings(string fileName) : base(fileName) { }
        }

        [Autosave]
        public class MarkedBaseSettings : JsonSettings {
            public override string FileName { get; set; } = "markedbase.jsn";
            public string FromMarkedBase { get; set; }

            public MarkedBaseSettings() { }
            public MarkedBaseSettings(string fileName) : base(fileName) { }
        }

        [Autosave]
        public class DerivedOfMarkedBaseSettings : MarkedBaseSettings {
            public string FromDerived { get; set; }

            public DerivedOfMarkedBaseSettings() { }
            public DerivedOfMarkedBaseSettings(string fileName) : base(fileName) { }
        }

        #endregion
    }
}
