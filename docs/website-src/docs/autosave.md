# Autosave

Autosaving detects changes in all `virtual` properties by creating a proxy wrapper using
`Castle.Core`. For a class to be autosaved, **all public properties must be `virtual`** and the class
must be non-sealed. Any property that is not marked `virtual` will not work properly (it won't just
fail to autosave), so a `JsonSettingsException` is thrown if a non-virtual property is detected during
proxy generation.

Autosave lives in the separate `Nucs.JsonSettings.Autosave` package.

## Requirements

- All public properties must be `virtual`.
- Install the `Nucs.JsonSettings.Autosave` NuGet package.
- Call `mySettings.EnableAutosave()` (or `EnableIAutosave<TSettings, TInterface>()`) **after** calling `Load`.

```csharp
using Nucs.JsonSettings;
using Nucs.JsonSettings.Autosave;

Settings x  = JsonSettings.Load<Settings>().EnableAutosave(); //call after loading
//or return the proxy typed as an interface the class implements (settings type first, then the interface):
ISettings y = JsonSettings.Load<Settings>().EnableIAutosave<Settings, ISettings>();

x.Property = "value"; //Saved!
```

For the [dynamic `SettingsBag`](dynamic-settings-bag.md), `EnableAutosave()` uses a built-in
implementation and does not require `virtual` properties.

## Attributes

- Mark a property with `IgnoreAutosaveAttribute` (Json.NET's `[JsonIgnore]` works too) to exclude it
  from the monitored set.
- Every generated proxy class carries `ProxyGeneratedAttribute`.

## Suspend Autosave

Sometimes several related changes happen close together, which would normally trigger several saves.
Create a `SuspendAutosave` scope to postpone saving until the scope is disposed (or `Resume` is
called). If nothing changed within the scope, no save happens.

> [!NOTE]
> `SuspendAutosave()` resolves the object's `AutosaveModule`, so call `EnableAutosave()` on the
> object first.

```csharp
using (settings.SuspendAutosave()) {
    settings.A = 1;   // does not save yet
    settings.B = 2;   // does not save yet
}                     // one save here, on dispose (only if something changed)
```

## WPF support with INotifyPropertyChanged / INotifyCollectionChanged

Any settings class can turn into a ViewModel with full autosave support, making window settings and
state persistence much simpler.

When your settings class inherits `INotifyPropertyChanged` (the library ships
`NotifiyingJsonSettings`, in the `Nucs.JsonSettings.Examples` namespace, as a convenient base),
calling `EnableAutosave` attaches a different
interceptor backed by a `NotificationBinder`. It listens to:

- the settings class's own `PropertyChanged` event;
- every property that implements `INotifyPropertyChanged` (binds to its `PropertyChanged`);
- every property that implements `INotifyCollectionChanged`, such as `ObservableCollection<T>` (binds
  to its `CollectionChanged`);
- all other virtual properties.

Objects nested *inside* an `ObservableCollection` (or other non-settings properties) are not
themselves monitored for changes.

```csharp
using System.Collections.ObjectModel;
using Nucs.JsonSettings;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Examples; // NotifiyingJsonSettings lives in this namespace

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

    // Not virtual -> not autosaved (see note below)
    private ObservableCollection<object> _nonAutosavingProperty;
    public ObservableCollection<object> NonAutosavingProperty {
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
settings.NonAutosavingProperty = new ObservableCollection<object>(); //doesn't save (not virtual)
settings.NonAutosavingProperty.Add("Jim");                //doesn't save
settings.Street += "-1";                                  //Boom! saves.
settings.AutoProperty = "Hello";                          //Boom! saves.
settings.IgnoredFromAutosaving = "Hello";                 //doesn't save ([IgnoreAutosave])
```

### Requirements (WPF)

- Settings class inherits `INotifyPropertyChanged` (e.g. via `NotifiyingJsonSettings`).
- All public properties must be `virtual`.
- Install the `Nucs.JsonSettings.Autosave` NuGet package.
- Call `mySettings.EnableAutosave()` after calling `Load`.

## Throttled Save

> [!NOTE]
> Interval-based throttling &mdash; coalescing many rapid changes (for example a slider bound in WPF)
> into one write per interval &mdash; is a planned feature and is not implemented yet. For now, use a
> [`SuspendAutosave`](#suspend-autosave) scope to batch a known burst of changes into a single save.
