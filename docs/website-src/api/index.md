---
uid: api-index
---

# Nucs.JsonSettings API Reference

This reference is generated from the source of the `Nucs.JsonSettings` and
`Nucs.JsonSettings.Autosave` assemblies. It is organized below by area; use the table of contents on
the left to browse every namespace and type.

---

## Core types

The essentials for defining and loading settings.

| Type | Description |
|------|-------------|
| @Nucs.JsonSettings.JsonSettings | The abstract base class you inherit for a typed, hardcoded settings POCO. Exposes `Load`, `Construct`, `Configure`, `Save`, and the load/save event pipeline. |
| @Nucs.JsonSettings.SettingsBag | A ready-made dynamic key/value settings object; no class to define. |
| @Nucs.JsonSettings.DynamicSettingsBag | The `dynamic` view returned by `SettingsBag.AsDynamic()`. |
| @Nucs.JsonSettings.JsonSettingsException | Base exception type thrown by the library. |

### Interfaces

| Type | Description |
|------|-------------|
| @Nucs.JsonSettings.ISavable | The savable contract (`FileName`, `Save`, load hooks). |
| @Nucs.JsonSettings.IEncryptedSavable | An `ISavable` that carries a key for encryption. |

### Quick example

```csharp
using Nucs.JsonSettings;

class MySettings : JsonSettings {
    public override string FileName { get; set; } = "config.json";
    public string Name { get; set; } = "default";
}

var settings = JsonSettings.Load<MySettings>("config.json");
settings.Name = "ok";
settings.Save();
```

---

## Fluent configuration

The extension methods that build a configured settings object. See [The Basics](../docs/the-basics.md).

| Type | Description |
|------|-------------|
| @Nucs.JsonSettings.Fluent.FluentJsonSettings | `WithFileName`, `WithModule`, `WithEncryption`, `WithBase64`, `WithVersioning`, `WithRecovery`, `WithDefaultValues`, `LoadNow`. |

---

## Modulation

The per-object module system. See the [Modulation API](../docs/modulation-api.md).

| Type | Description |
|------|-------------|
| @Nucs.JsonSettings.Modulation.Module | Abstract base for a module; override `Attach`/`Deattach`. |
| @Nucs.JsonSettings.Modulation.ModuleSocket | Holds the modules attached to a settings object (`JsonSettings.Modulation`). |
| @Nucs.JsonSettings.Modulation.ISocket | The socket contract modules attach to. |
| @Nucs.JsonSettings.Modulation.Base64Module | Encodes the payload as Base64. |
| @Nucs.JsonSettings.Modulation.RijndaelModule | AES-256 encryption of the payload. See [Encryption](../docs/encryption.md). |
| @Nucs.JsonSettings.Modulation.ModularityException | Thrown on invalid module attachment/state. |

---

## Versioning

Enforce a schema version. See [Versioning](../docs/versioning.md).

| Type | Description |
|------|-------------|
| @Nucs.JsonSettings.Modulation.IVersionable | Contributes a `Version Version { get; set; }` property. |
| `VersioningModule<T>` | The module that enforces a version policy (`Nucs.JsonSettings.Modulation`). |
| @Nucs.JsonSettings.Modulation.VersioningResultAction | What to do on a version mismatch: `DoNothing`, `Throw`, `RenameAndLoadDefault`, `LoadDefault`, `LoadDefaultAndSave`. |
| @Nucs.JsonSettings.Modulation.EnforcedVersionAttribute | Declares the enforced version on the `Version` property. |
| @Nucs.JsonSettings.Modulation.InvalidVersionException | Thrown when the action is `Throw`. |

---

## Recovery

Handle parse failures. See [Recovery](../docs/recovery.md).

| Type | Description |
|------|-------------|
| @Nucs.JsonSettings.Modulation.Recovery.RecoveryModule | Recovers when the file fails to parse. |
| @Nucs.JsonSettings.Modulation.Recovery.RecoveryAction | `Throw`, `RenameAndLoadDefault`, `LoadDefault`, `LoadDefaultAndSave`. |
| @Nucs.JsonSettings.Modulation.Recovery.JsonSettingsRecoveryException | Wraps the underlying parse exception. |

---

## Autosave

Save automatically on change (ships in `Nucs.JsonSettings.Autosave`). See [Autosave](../docs/autosave.md).

| Type | Description |
|------|-------------|
| @Nucs.JsonSettings.Autosave.JsonSettingsAutosaveExtensions | `EnableAutosave` / `EnableIAutosave` entry points. |
| @Nucs.JsonSettings.Autosave.AutosaveModule | The module that drives change detection. |
| @Nucs.JsonSettings.Autosave.IgnoreAutosaveAttribute | Excludes a property from autosave monitoring. |
| @Nucs.JsonSettings.Autosave.ProxyGeneratedAttribute | Marks proxy-generated classes. |
| @Nucs.JsonSettings.Autosave.NotificationBinder | Binds `INotifyPropertyChanged`/`INotifyCollectionChanged` sources for WPF-style autosave. |

`NotifiyingJsonSettings` (in the `Nucs.JsonSettings.Examples` namespace) is the convenient
`INotifyPropertyChanged` base class for WPF-style settings.

---

## Helpers

| Type | Description |
|------|-------------|
| @Nucs.JsonSettings.Inline.Paths | Path resolution helpers used when resolving `FileName`. |

---

## See also

- [User documentation](../docs/intro.md) &mdash; tutorials and guides.
- [GitHub repository](https://github.com/Nucs/JsonSettings) &mdash; source and issues.
