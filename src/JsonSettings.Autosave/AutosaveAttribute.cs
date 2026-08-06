using System;
using AspectInjector.Broker;

namespace Nucs.JsonSettings.Autosave {
    /// <summary>
    ///     Marks a settings class (or a single property) whose setters should commit a save.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     This is a compile-time aspect. Applying it causes AspectInjector to append a call to
    ///     <see cref="AutosaveRuntime.OnPropertySet"/> to the end of every instance setter in the
    ///     annotated scope, in the assembly that declares the class. Nothing is generated at
    ///     runtime, so this works under Native AOT, where the previous
    ///     <c>Castle.DynamicProxy</c> implementation could not: DynamicProxy builds proxy types
    ///     with <c>System.Reflection.Emit</c>, and an AOT binary ships no runtime code generator.
    ///     </para>
    ///     <para>
    ///     Because there is no proxy, there is also no requirement that properties be
    ///     <c>virtual</c>, no second object to keep in sync with the loaded one, and no change of
    ///     type: <c>EnableAutosave()</c> returns the very instance it was given. Ordinary classes,
    ///     sealed classes and non-virtual properties are all supported.
    ///     </para>
    ///     <para>
    ///     The attribute is not inherited. Weaving happens where a setter is *declared*, so a
    ///     property declared on a base class is woven only if that base class carries its own
    ///     <see cref="AutosaveAttribute"/>. Apply it to every type in a settings hierarchy that
    ///     declares properties you want saved.
    ///     </para>
    ///     <para>
    ///     Writing to a property is not the same as saving: the woven call is inert until
    ///     <c>EnableAutosave()</c> attaches an <see cref="AutosaveModule"/>, and it consults that
    ///     module for the monitored-property set and the suspension state. Marking a class costs
    ///     nothing until autosave is actually enabled on an instance.
    ///     </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [Autosave]
    /// public class MySettings : JsonSettings {
    ///     public override string FileName { get; set; } = "config.json";
    ///     public string Name { get; set; }          // no 'virtual' needed
    /// }
    ///
    /// var settings = JsonSettings.Load&lt;MySettings&gt;("config.json").EnableAutosave();
    /// settings.Name = "changed";                     // saved
    /// </code>
    /// </example>
    [Aspect(Scope.Global)]
    [Injection(typeof(AutosaveAttribute))]
    //The weave-witness: the same pass that wires the advice below also makes the annotated class
    //implement the empty IAutosaveWoven, which is how EnableAutosave() can tell a woven class from
    //one whose build silently skipped AspectInjector (the attribute alone cannot -- it is present
    //either way). See IAutosaveWoven for the failure mode this closes.
    [Mixin(typeof(IAutosaveWoven))]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public sealed class AutosaveAttribute : Attribute, IAutosaveWoven {
        /// <summary>
        ///     Appended to every instance setter in the annotated scope.
        /// </summary>
        /// <remarks>
        ///     <see cref="Target.AnyAccess"/> rather than <see cref="Target.Public"/> so that a
        ///     <c>public string Foo { get; private set; }</c> is covered too; which of the woven
        ///     setters actually saves is decided at runtime by
        ///     <see cref="AutosaveModule.IsMonitored"/>, not by what was woven.
        ///
        ///     <see cref="Source.Name"/> is resolved at weave time to the *property* name
        ///     ("Foo", not "set_Foo") and baked into the call site as a constant, so the advice
        ///     needs no reflection to identify the member.
        /// </remarks>
        [Advice(Kind.After, Targets = Target.Setter | Target.AnyAccess | Target.Instance)]
        public void AfterSetter([Argument(Source.Instance)] object instance,
                                [Argument(Source.Name)] string propertyName) {
            AutosaveRuntime.OnPropertySet(instance, propertyName);
        }
    }
}
