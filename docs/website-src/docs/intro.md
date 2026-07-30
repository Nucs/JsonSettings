# Introduction

**Nucs.JsonSettings** simplifies creating configuration for your C# app or service. It uses
the serialization capabilities of [Json.NET](https://www.newtonsoft.com/json/help/html/SerializationGuide.htm)
to serialize nested (custom) objects, dictionaries and lists as simply as creating a `POCO` and
inheriting the `JsonSettings` class &mdash; no mapping to write.

## Installation

```sh
dotnet add package Nucs.JsonSettings
dotnet add package Nucs.JsonSettings.Autosave   # optional, only for EnableAutosave()
```

```sh
PM> Install-Package Nucs.JsonSettings
PM> Install-Package Nucs.JsonSettings.Autosave
```

Both packages target `netstandard2.0`, `net48`, `net6.0`, `net8.0` and `net10.0`. The
`netstandard2.0` asset covers everything without an exact match, including `net472`+,
`netcoreapp3.1`, `net5.0`, `net7.0`, `net9.0`, Unity and Xamarin.

## Features Overview

- Initialized in a fluent static API &mdash; see [The Basics](the-basics.md).
- Cross-platform, multi-targeting `netstandard2.0`, `net48`, `net6.0`, `net8.0` and `net10.0`.
- Modularity allowing easy extension and high control over behavior on a per-object level &mdash;
  see the [Modulation API](modulation-api.md).
- Autosaving on changes &mdash; see [Autosave](autosave.md).
  - Via `INotifyPropertyChanged`/`INotifyCollectionChanged` allowing WPF binding.
  - Via `Castle.DynamicProxy` generated wrapper.
- Versioning control &mdash; see [Versioning](versioning.md).
  - Offers protection mechanisms such as renaming the file and loading defaults.
  - By changing version, it allows introducing any kind of change to the settings class.
- Customizable control over recovering from parsing exceptions &mdash; see [Recovery](recovery.md).
- AES-256 encryption via a key &mdash; see [Encryption](encryption.md).
- Fully extensible with [Json.NET](https://www.newtonsoft.com/json/)'s capabilities, attributes and
  settings. It'll be accurate to say that this library is built around Json.NET.
- `SettingsBag`, a `dynamic` option that uses a `ConcurrentDictionary<string, object>`, eliminating
  the need for a hardcoded POCO class &mdash; see [Dynamic Settings Bag](dynamic-settings-bag.md).

## Core concepts

`JsonSettings` is the base abstract class serving as the base class for all settings objects you
define. **Creation and loading** are done through a static API, while **saving** is done through the
settings object's own API.

| Concept | What it is |
|---------|------------|
| `JsonSettings` | The abstract base class you inherit for a typed, hardcoded settings POCO. |
| `SettingsBag` | A ready-made dynamic key/value settings object; no class to define. |
| `JsonSettings.Load<T>(...)` | Load an existing file (or create it from defaults if missing). |
| `JsonSettings.Construct<T>(...)` | Create a fresh in-memory instance without reading from disk. |
| `JsonSettings.Configure<T>(...)` | Begin a fluent configuration chain, finished with `LoadNow()`. |
| `Module` | A unit of behavior (encryption, base64, versioning, recovery, autosave) attached per object. |

### Which type do I use?

- Reach for a **hardcoded** class (inherit `JsonSettings`) when your settings have a known shape.
- Reach for the **[dynamic `SettingsBag`](dynamic-settings-bag.md)** when keys are open-ended or you
  don't want to declare a class.
- Add **[encryption](encryption.md)**, **[versioning](versioning.md)**, **[recovery](recovery.md)**
  or **[autosave](autosave.md)** as needed &mdash; each is a module you opt into per object.

Continue to [The Basics](the-basics.md) for runnable examples of each.

## References

- Test project: <https://github.com/Nucs/JsonSettings/tree/master/tests/JsonSettings.Tests>
- Json.NET Serialization Guide: <https://www.newtonsoft.com/json/help/html/SerializationGuide.htm>
