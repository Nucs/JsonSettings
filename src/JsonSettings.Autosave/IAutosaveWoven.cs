namespace Nucs.JsonSettings.Autosave {
    /// <summary>
    ///     Compile-time proof that AspectInjector actually processed a class: the
    ///     <see cref="AutosaveAttribute"/> aspect mixes this empty interface into every class it is
    ///     applied to, in the same weave pass that appends the save advice to the setters. Source
    ///     code never implements it -- the build does.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Exists because a silently skipped weave is the worst failure mode this library has:
    ///     the [Autosave] class compiles, <c>EnableAutosave()</c> finds the attribute and succeeds,
    ///     and nothing is ever saved. The attribute cannot witness the weave -- it is plain metadata,
    ///     present whether or not AspectInjector ran (that is exactly why it survives an unwoven
    ///     build) -- but an interface implementation ADDED BY the weave can. It has been measured to
    ///     happen in the wild through no fault of the user: a direct AspectInjector reference with
    ///     <c>ExcludeAssets="build"</c>, or a single-pass <c>msbuild -t:Restore;Build</c> that
    ///     evaluates the project before the package targets exist. <c>EnableAutosave()</c> therefore
    ///     refuses a class that carries the attribute without carrying this interface.
    ///     </para>
    ///     <para>
    ///     Deliberately empty: nothing to implement, nothing for the mixin to delegate to, zero
    ///     runtime cost -- the check is a single type test. Public only because woven consumer
    ///     classes end up implementing it. Inherited like any CLR interface, so it witnesses that
    ///     the weave ran over the hierarchy at all (the build-level failure above), not per-class
    ///     advice coverage -- which setters save is still governed by where [Autosave] is applied,
    ///     as documented on the attribute.
    ///     </para>
    /// </remarks>
    public interface IAutosaveWoven { }
}
