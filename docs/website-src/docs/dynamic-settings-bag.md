# Dynamic Settings Bag

`SettingsBag` internally stores a key/value dictionary. Any type of value can be passed as long as
Json.NET knows how to serialize it. `SettingsBag` has a built-in feature for autosaving that can be
enabled by calling `EnableAutosave()` without WPF binding support.

It is a `sealed` class in the `Nucs.JsonSettings` namespace, so there is no class to define &mdash;
just load it and go.

## Basic usage

```csharp
using Nucs.JsonSettings;

var settings = JsonSettings.Load<SettingsBag>("config.json"); //created if it doesn't exist

settings["somekey"]        = "with some value";
settings["someotherkey"]   = 1;
settings["somekeyforclass"] = new SmallClass { Name = "Small", Value = "Class" };
settings.Save();

//validate — reload from disk
settings = JsonSettings.Load<SettingsBag>("config.json");
Console.WriteLine(settings["somekey"]);      // with some value
Console.WriteLine(settings["someotherkey"]); // 1
```

## Nullable value-type semantics

Value types come back as `Nullable<T>`, so a missing key returns `null` rather than throwing or
returning a default:

```csharp
dynamic dyn = settings.AsDynamic();
if ((int?)dyn.someotherkey == 1)
    Console.WriteLine("matched");

var missing = (int?)dyn.doesNotExist; // null, not 0
```

## Typed retrieval with `Get<T>`

`Get<T>(key, @default)` reads a value and converts it to `T`. Because Json.NET deserializes a JSON
integer back as `long`, a value stored as `int` is a boxed `long` once reloaded; `Get<int>` bridges
that width through `Convert.ChangeType` instead of throwing. A missing key &mdash; or a stored
`null` &mdash; returns the `@default` you pass (or `default(T)`):

```csharp
settings["count"] = 3;
settings.Save();
settings = JsonSettings.Load<SettingsBag>("config.json");

int count   = settings.Get<int>("count");      // 3, even though it reloaded as Int64
int retries = settings.Get<int>("retries", 5); // 5 — key absent, so the default is used
```

## The dynamic view

`AsDynamic()` returns a `dynamic` wrapper (`DynamicSettingsBag`) so you can use member syntax
instead of the indexer:

```csharp
dynamic dyn = settings.AsDynamic();
dyn.key = "dat value tho";  // same as settings["key"] = "dat value tho";
dyn.Save();                 // same as settings.Save();
```

## Autosave

`SettingsBag` has its own dictionary-backed autosave implementation (separate from the `[Autosave]`
weaving hardcoded classes use), so a plain `EnableAutosave()` is enough:

```csharp
var settings = JsonSettings.Load<SettingsBag>("config.json").EnableAutosave();

settings["somekey"] = "with some value"; //Saved!
settings.AsDynamic().another = 42;        //Saved!
```

See [Autosave](autosave.md) for suspension and the hardcoded-class story.
