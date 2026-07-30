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
> `SuspendAutosave()` resolves the object's `AutosaveModule`, so call `EnableAutosave()` on the
> object first.

## Behaviour notes

- **Loading does not autosave.** `Load()`, `LoadDefault()` and a versioning reload populate the
  object from disk through its setters; those writes are not user edits and do not save back.
  Autosave resumes normally afterward.
- **Reentrancy is safe.** Writing a monitored property from inside an `AfterSave` handler does not
  trigger another save (it would otherwise recurse); the value is kept and persists on the next
  save.
- **A failing save surfaces at the assignment.** If the triggered `Save()` throws, the exception
  propagates out of the property assignment (the new value is already set in memory).
- **`EnableAutosave()` is idempotent** &mdash; calling it twice returns the same instance and does
  not attach a second autosave module.
- **Disposing the settings unbinds autosave**, including handlers attached to nested collections.

## WPF support with INotifyPropertyChanged / INotifyCollectionChanged

Any settings class can become a ViewModel with full autosave support, making window settings and
state persistence simpler.

When your settings class inherits `INotifyPropertyChanged` (the library ships
`NotifiyingJsonSettings`, in the `Nucs.JsonSettings.Examples` namespace, as a convenient base),
`EnableAutosave` additionally attaches a `NotificationBinder` that watches nested notifiers, so
autosave also fires when:

- a property that implements `INotifyPropertyChanged` raises `PropertyChanged`;
- a property that implements `INotifyCollectionChanged`, such as `ObservableCollection<T>`, raises
  `CollectionChanged` &mdash; including a get-only collection you mutate in place;
- a nested collection is replaced &mdash; the new instance is rebound automatically.

Objects nested *inside* an `ObservableCollection` (or other non-settings properties) are not
themselves monitored.

```csharp
using System.Collections.ObjectModel;
using Nucs.JsonSettings;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Examples; // NotifiyingJsonSettings lives in this namespace

[Autosave]
public class NotifyingSettings : NotifiyingJsonSettings {
    public override string FileName { get; set; } = "some.default.just.in.case.jsn";

    private string _street = "Sesamee Street 123";
    public virtual string Street {
        get => _street;
        set { if (value == _street) return; _street = value; OnPropertyChanged(); }
    }

    public virtual string AutoProperty { get; set; }

    [IgnoreAutosave]
    public virtual string IgnoredFromAutosaving { get; set; }

    private ObservableCollection<string> _residents = new ObservableCollection<string>();
    public virtual ObservableCollection<string> Residents {
        get => _residents;
        set { if (Equals(value, _residents)) return; _residents = value; OnPropertyChanged(); }
    }

    // Excluded via [IgnoreAutosave] — note: being non-virtual is no longer what excludes it.
    private ObservableCollection<object> _nonAutosavingProperty = new ObservableCollection<object>();
    [IgnoreAutosave]
    public virtual ObservableCollection<object> NonAutosavingProperty {
        get => _nonAutosavingProperty;
        set { if (Equals(value, _nonAutosavingProperty)) return; _nonAutosavingProperty = value; OnPropertyChanged(); }
    }

    public NotifyingSettings() { }
    public NotifyingSettings(string fileName) : base(fileName) { }
}
```

```csharp
var settings = JsonSettings.Load<NotifyingSettings>("observable.jsn").EnableAutosave();

settings.Residents.Add("Cookie Monster");                 //Boom! saves.
settings.Residents = new ObservableCollection<string>();  //Boom! saves.
settings.Residents.Add("Cookie Monster");                 //Boom! saves (new collection is bound too).
settings.NonAutosavingProperty.Add("Jim");                //doesn't save ([IgnoreAutosave])
settings.Street += "-1";                                  //Boom! saves.
settings.AutoProperty = "Hello";                          //Boom! saves.
settings.IgnoredFromAutosaving = "Hello";                 //doesn't save ([IgnoreAutosave])
```

## Producing notifications from setters &mdash; `[NotifyChanges]`

The section above makes autosave *react* to `PropertyChanged`. The reverse &mdash; making a setter
*raise* `PropertyChanged` so a WPF binding refreshes &mdash; is the `[NotifyChanges]` aspect. It closes
the one gap the hand-written approach leaves: an **auto-property** autosaves but never notifies, so a
binding to it goes stale unless you expand it into a manual `get/set` that calls `OnPropertyChanged()`.

`[NotifyChanges]` weaves that call in for you, at compile time, alongside `[Autosave]`:

```csharp
[Autosave]        // save on change
[NotifyChanges]   // and raise PropertyChanged on change
public class WindowSettings : NotifiyingJsonSettings {
    public override string FileName { get; set; } = "window.json";

    public double Width  { get; set; }   // auto-property: binds two-way, saves, and notifies
    public double Height { get; set; }
    public string Title  { get; set; }
}
```

Like `[Autosave]`, it is a compile-time aspect ([AspectInjector](https://github.com/pamidur/aspect-injector),
no proxy, Native-AOT-safe), it is **not inherited** (mark every class in a hierarchy that declares
notifiable properties), and it composes with `[Autosave]` on the same setter &mdash; one write saves
once and notifies once.

> [!IMPORTANT]
> Put `[NotifyChanges]` on **auto-properties**. If you keep a hand-written setter that already calls
> `OnPropertyChanged()`, the woven call raises a second time &mdash; the aspect exists precisely so you
> don't hand-write those setters.

### Where the event comes from

`[NotifyChanges]` produces the notification but does not supply the event; the class must already own
one:

| Your class&hellip; | What raises the event |
|---|---|
| inherits `NotifiyingJsonSettings` | its `OnPropertyChanged` (the intended path) |
| inherits an MVVM base (CommunityToolkit `ObservableObject`, Prism `BindableBase`, Caliburn `PropertyChangedBase`, &hellip;) | resolved by convention: `OnPropertyChanged` / `RaisePropertyChanged` / `NotifyOfPropertyChange` |
| implements nothing | use `[NotifyChangesMixin]` (below), which injects the interface too |

A class with neither a base nor a convention raiser is a harmless no-op: the setter runs, nothing is
raised.

### Change guards

By default a setter notifies only when the value actually changes. `NotificationGuard` controls this,
per class or per property (a property-level `[NotifyChanges]` overrides the class):

| `NotificationGuard` | Notifies when |
|---|---|
| `OnlyChanged` *(default)* | the new value differs from the current one |
| `SkipNullOrDefault` | the new value is not `null` / `default(T)` |
| `Always` | every setter write, no filtering ("any setter access") |

They combine (`[Flags]`): `OnlyChanged | SkipNullOrDefault` means "only on a change to a non-default
value".

```csharp
[Autosave, NotifyChanges]  // class default: OnlyChanged
public class SearchSettings : NotifiyingJsonSettings {
    public override string FileName { get; set; } = "search.json";

    public string Name { get; set; }                                   // OnlyChanged

    [NotifyChanges(Guard = NotificationGuard.Always)]
    public int Ticks { get; set; }                                     // every write

    [NotifyChanges(Guard = NotificationGuard.OnlyChanged | NotificationGuard.SkipNullOrDefault)]
    public string Filter { get; set; }                                 // changed AND non-null
}
```

> [!NOTE]
> `OnlyChanged` reads the property's getter before the assignment to compare old and new, so it relies
> on the model being preserved under trimming/AOT &mdash; the same requirement the Newtonsoft serializer
> already imposes. A write-only property has no getter and behaves as `Always`.

### No base class &mdash; `[NotifyChangesMixin]`

`[NotifyChangesMixin]` uses AspectInjector's *mixin* to **inject `INotifyPropertyChanged`** into a class
that declares no notification base, and raises that injected event from every setter. The class becomes
WPF-bindable with no base type and no boilerplate, `sealed` classes included:

```csharp
[Autosave]
[NotifyChangesMixin]                       // implements INotifyPropertyChanged for you
public sealed class AppSettings : JsonSettings {
    public override string FileName { get; set; } = "app.json";
    public string Name { get; set; }
}

var s = JsonSettings.Load<AppSettings>("app.json").EnableAutosave();
((INotifyPropertyChanged) s).PropertyChanged += (_, e) => { /* e.PropertyName == "Name" */ };
s.Name = "changed";                        // saved and notified
```

The aspect is **per-instance**, so each settings object has its own subscriber list. Use it for a
**single** settings class; for a hierarchy prefer `NotifiyingJsonSettings` + `[NotifyChanges]`, because
the interface can only be injected once and a derived setter cannot reach a base's injected event. Note
too the boundary with the autosave binder above: `EnableAutosave()` only watches nested
`ObservableCollection`s on a `NotifiyingJsonSettings`, so a mixin-only class saves on its own property
writes but not when a nested collection is mutated in place &mdash; use the notifying base if you need
that.

### How it compares

`[NotifyChanges]` / `[NotifyChangesMixin]` are deliberately small and settings-focused; they are not a
general MVVM framework. Where they sit next to the usual `INotifyPropertyChanged` approaches:

| Approach | Mechanism | Auto-property notify | Adds the interface | AOT / trim |
|---|---|---|---|---|
| Hand-written `OnPropertyChanged()` | source | no (one per setter) | you write it | yes |
| **`[NotifyChanges]`** | IL weave (AspectInjector) | yes | no (needs a base/convention) | yes |
| **`[NotifyChangesMixin]`** | IL weave + mixin | yes | yes | yes |
| Fody `PropertyChanged` | IL weave (Fody) | yes | yes | weaver-dependent |
| CommunityToolkit.Mvvm `[ObservableProperty]` | source generator | yes (partial field) | via `ObservableObject` | yes |
| ReactiveUI `RaiseAndSetIfChanged` | runtime helper | no (explicit call) | via `ReactiveObject` | yes |

The distinguishing point: these reuse the **same weaving pipeline as `[Autosave]`**, so one attribute
set covers save *and* notify with no extra dependency, no source-generator partial-class requirement,
and the same Native-AOT story. The strong-name re-signing described below covers them automatically.

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
