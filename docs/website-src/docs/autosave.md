# Autosave

Autosave persists your settings automatically whenever a property changes &mdash; no explicit
`Save()` call. Mark a settings class `[Autosave]` and every property setter commits a save.

```csharp
using Nucs.JsonSettings;
using Nucs.JsonSettings.Autosave;

[Autosave]
public class MySettings : JsonSettings {
    public override string FileName { get; set; } = "config.json";
    public string Name  { get; set; }   // no 'virtual' required
    public int    Count { get; set; }
}

var settings = JsonSettings.Load<MySettings>("config.json").EnableAutosave();
settings.Name = "changed";   // saved
settings.Count = 42;         // saved
```

Autosave lives in the separate `Nucs.JsonSettings.Autosave` package.

## How it works

`[Autosave]` is a **compile-time aspect**. When you build, an IL weaver
([AspectInjector](https://github.com/pamidur/aspect-injector)) appends a save to the end of every
instance setter in the annotated class, in the assembly that declares it. Nothing is generated at
runtime, so autosave works under **Native AOT** &mdash; where the previous `Castle.DynamicProxy`
implementation could not run at all.

Because there is no proxy:

- **`virtual` is not required.** Ordinary properties, `sealed` classes and non-virtual members all
  work.
- **`EnableAutosave()` returns the same instance** it was given, not a wrapper. Any other reference
  to that object autosaves too.

## Requirements

- Install the `Nucs.JsonSettings.Autosave` NuGet package.
- Mark the settings class `[Autosave]`.
- Call `mySettings.EnableAutosave()` **after** `Load`.

> [!IMPORTANT]
> `[Autosave]` is **not inherited**. A setter is woven where it is *declared*, so every class in a
> settings hierarchy that declares properties you want saved needs its own `[Autosave]`. Enabling
> autosave on a class that is not marked throws `JsonSettingsException` rather than silently doing
> nothing.

## What is monitored

Every public property with a setter is monitored, **except**:

- properties marked `[IgnoreAutosave]` or Json.NET's `[JsonIgnore]`;
- indexers (`this[...]`) &mdash; use a normal property or call `Save()`;
- the framework's own `FileName` and `Modulation`;
- `IVersionable.Version`, which is metadata the [versioning](versioning.md) module manages (it still
  rides along in every ordinary save, so changing it alone does not trigger one). A property named
  `Version` on a class that does **not** implement `IVersionable` is ordinary user data and *is*
  monitored.

```csharp
[Autosave]
public class MySettings : JsonSettings {
    public override string FileName { get; set; } = "config.json";
    public string Kept { get; set; }               // saved

    [IgnoreAutosave] public string Scratch { get; set; }   // never saves
    [JsonIgnore]     public string Computed { get; set; }  // never saves (also not serialized)
}
```

## Migrating from 2.1.0 and earlier

Autosave used to build a runtime proxy with `Castle.Core`, which forced restrictions that are gone
in 2.2.0:

| Before (`Castle.DynamicProxy`) | Now (compile-time weaving) |
|---|---|
| Every public property had to be `virtual` | Ordinary properties work; `virtual` is irrelevant |
| The class could not be `sealed` | `sealed` classes work |
| `EnableAutosave()` returned a **different** object | Returns the same instance |
| Could not run under Native AOT | No runtime code generation |

Two behavioural notes for migrators:

- **`virtual` is no longer an opt-out.** Under the proxy a non-virtual property was silently
  skipped; some code relied on that. Every setter is woven now, so a property that must **not**
  autosave has to say so with `[IgnoreAutosave]`.
- The Castle-era `JsonSettingsAutosaveExtensions.Options` field is removed, and
  `EnableIAutosave<TSettings, TInterface>()` is retained only for source compatibility &mdash; it now
  just returns the instance typed as the interface (there is no interface proxy any more).

## Suspend Autosave

Several related changes close together would normally trigger several saves. A `SuspendAutosave`
scope postpones saving until the scope is disposed (or `Resume` is called). If nothing changed
within the scope, no save happens. Scopes **nest**: only the outermost one commits, once.

```csharp
using (settings.SuspendAutosave()) {
    settings.A = 1;   // does not save yet
    settings.B = 2;   // does not save yet
}                     // one save here, on dispose (only if something changed)
```

> [!NOTE]
> `SuspendAutosave()` resolves the object's suspension module — the `AutosaveModule` on a woven
> class, the bag's own `SettingsBagAutosaveModule` on a `SettingsBag` — so call `EnableAutosave()`
> on the object first.

## Behaviour notes

- **Loading does not autosave.** `Load()`, `LoadDefault()` and a versioning reload populate the
  object from disk through its setters; those writes are not user edits and do not save back.
  Autosave resumes normally afterward. The populate is bracketed by the
  `BeforeRepopulate`/`AfterRepopulate` events (see
  [Modulation API](modulation-api.md#execution-order)) &mdash; how the module knows to suppress,
  and why it resumes even when a load throws halfway.
- **Reentrancy is safe.** Writing a monitored property from inside an `AfterSave` handler does not
  trigger another save (it would otherwise recurse); the value is kept and persists on the next
  save. The same holds for mutating a bound collection or nested object from inside the handler —
  nested changes take the same gated path as setter writes.
- **A failing save surfaces at the assignment.** If the triggered `Save()` throws, the exception
  propagates out of the property assignment (the new value is already set in memory).
- **`EnableAutosave()` is idempotent** &mdash; calling it twice returns the same instance and does
  not attach a second autosave module.
- **Disposing the settings unbinds autosave**, including handlers attached to nested collections.

## Notifications and WPF

A settings class can become a WPF-bindable ViewModel: raise `PropertyChanged` from its setters for
the View, and have autosave react to nested `INotifyPropertyChanged` / `INotifyCollectionChanged`
changes. When a class implements `INotifyPropertyChanged` &mdash; the `NotifiyingJsonSettings` base,
a `[NotifyChangesMixin]`-woven class, or a hand-written implementation &mdash; `EnableAutosave()`
attaches a `NotificationBinder` so autosave also fires on nested collection edits; and
`[NotifyChanges]` / `[NotifyChangesMixin]` weave the `PropertyChanged` raise into your setters so
even auto-properties notify, with configurable change guards.

See the dedicated **[Notifications &amp; WPF](notifications.md)** guide for the full treatment &mdash;
producing notifications, the guard modes, the mixin, the ways a settings class can implement the
interface, nested-collection autosave, threading, and a comparison with Fody / CommunityToolkit.Mvvm
/ ReactiveUI.

## SettingsBag

The dynamic [`SettingsBag`](dynamic-settings-bag.md) has its **own** dictionary-backed autosave &mdash;
it needs no `[Autosave]` attribute and no weaving. Its instance `EnableAutosave()` turns it on, and
it shares the same suspension and reentrancy guarantees described above.

```csharp
var bag = JsonSettings.Load<SettingsBag>("bag.json").EnableAutosave();
bag["Name"] = "value";      // saved
bag.Remove("Name");         // saved
bag.AsDynamic().Other = 42; // saved
```

## How the weave runs (out of process, since 2.3.0)

AspectInjector's stock in-process MSBuild task leaks file handles into the MSBuild node that hosts
it, which deterministically broke small **executable** consumers: the SDK's `CreateAppHost` step
could not read the still-locked `obj\...\<App>.dll` and the build failed with
`MSB4018` / *"The process cannot access the file ... because it is being used by another process"*
&mdash; merely referencing the package was enough, no `[Autosave]` class required. Since 2.3.0 the
package's shipped targets suppress that in-process task and run the **identical** weaver task in a
short-lived child MSBuild process instead; every leaked handle is closed by the OS when the child
exits, before `CreateAppHost` runs. Weaving behaviour, parameters and incrementality are unchanged,
and a project referencing both this package and `Nucs.JsonSettings.NotifyChanges` still weaves
exactly once. Opt back into the stock in-process weave with
`<NucsJsonSettingsOutOfProcWeave>false</NucsJsonSettingsOutOfProcWeave>`;
`<AspectInjector_Enabled>false</AspectInjector_Enabled>` still disables weaving entirely (only safe
if no class is marked `[Autosave]`/`[NotifyChanges]` &mdash; they would compile and never work).

## Strong-named consumers

IL weaving rewrites the compiled assembly after it is signed, and AspectInjector 2.9.0
[retired its re-signing feature](https://github.com/pamidur/aspect-injector/releases/tag/2.9.0). The
package therefore ships MSBuild targets that re-sign your assembly with your own
`AssemblyOriginatorKeyFile` after the weave, so a strong-named consumer stays valid. If `sn.exe`
cannot be found the build warns (`NJS1001`) rather than failing; opt out with
`<NucsAutosaveResignAfterWeaving>false</NucsAutosaveResignAfterWeaving>`. These targets flow to
projects that reference the package transitively, so a settings class declared in a downstream
assembly is woven and re-signed correctly.

## Native AOT and trimming

Autosave itself emits no runtime code and is AOT-safe. The serializer underneath (Newtonsoft.Json)
is not trim-annotated, however, so a fully trimmed or `PublishAot` application still has to preserve
its settings model like any other Newtonsoft consumer. See the repository's `docs/AOT.md` for the
detailed status.

## Throttled Save

> [!NOTE]
> Interval-based throttling &mdash; coalescing many rapid changes (for example a slider bound in WPF)
> into one write per interval &mdash; is a planned feature and is not implemented yet. For now, use a
> [`SuspendAutosave`](#suspend-autosave) scope to batch a known burst of changes into a single save.
