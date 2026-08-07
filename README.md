# <img src="assets/icon.png" width="25" style="margin: 5px 0px 0px 10px"/> JsonSettings
[![Nuget version](https://img.shields.io/nuget/vpre/Nucs.JsonSettings.svg)](https://www.nuget.org/packages/nucs.JsonSettings/)
[![Nuget downloads](https://osscdn.nucs.workers.dev/jsonsettings-downloads-ujVrxmtCZN.svg)](https://www.nuget.org/packages/nucs.JsonSettings/)
[![GitHub license](https://img.shields.io/github/license/Nucs/JsonSettings.svg)](https://github.com/Nucs/JsonSettings/blob/master/LICENSE)
[![Documentation](https://img.shields.io/badge/docs-nucs.github.io%2FJsonSettings-2563eb)](https://nucs.github.io/JsonSettings)

This library aims to simplify the process of creating configuration for your C# app/service 
by utilizing the serialization capabilities of [Json.NET](https://www.newtonsoft.com/json/help/html/SerializationGuide.htm)
to serialize nested (custom) objects, dictionaries and lists as simply as by creating a `POCO` and inheriting `JsonSettings` class.<br/>

> 📖 **Full documentation & API reference:** [nucs.github.io/JsonSettings](https://nucs.github.io/JsonSettings)


### Installation
```sh
dotnet add package Nucs.JsonSettings
dotnet add package Nucs.JsonSettings.Autosave        # optional, for [AutoSave] and EnableAutosave()
dotnet add package Nucs.JsonSettings.NotifyChanges   # optional, for [NotifyChanges] data binding
```

All packages are signed and supported runtimes are: `netstandard2.0`, `net48`, `net6.0`, `net8.0` and `net10.0`.

## Table of Contents
- [📖 Documentation Website](https://nucs.github.io/JsonSettings)
- [Features Overview](#features-overview)
- [The Basics](#the-basics)
- [Modules](#recovery)
    - [Recovery](#recovery)
    - [Versioning](#versioning)
    - [Encryption](#encryption)
    - [Autosave](#autosave)
      - [Suspend Saving](#suspend-autosave)
      - [WPF Support with INotifyPropertyChanged/INotifyCollectionChanged](#wpf-support-with-inotifypropertychangedinotifycollectionchanged)
      - [Throttled Save](#throttled-save)
- [Dynamic Settings Bag](#dynamic-settings-bag)
- [Changing JsonSerializerSettings](#changing-jsonserializersettings)
- [Converters](#converters)
- [Modulation Api](#modulation-api)
- [Native AOT and Trimming](https://github.com/Nucs/JsonSettings/blob/master/docs/AOT.md)
- [License](https://github.com/Nucs/JsonSettings/blob/master/LICENSE)


Features Overview
---
 - Initialized in a fluent static API <span style='font-size:11px; padding-left: 3px' >[read more](#the-basics)</span>
 - Cross-platform, multi-targeting `netstandard2.0`, `net48`, `net6.0`, `net8.0` and `net10.0`
 - Modularity allowing easy extension and high control over behavior on a per-object level  <span style='font-size:11px; padding-left: 3px' >[read more](#modulation-api)</span>
 - Autosaving on changes  <span style='font-size:11px; padding-left: 3px' >[read more](#autosave)</span>
   - Via `INotifyPropertyChanged`/`INotifyCollectionChanged` allowing WPF binding  <span style='font-size:11px; padding-left: 3px' >[read more](#wpf-support-with-inotifypropertychangedinotifycollectionchanged)</span>
   - Via compile-time IL weaving of the property setters, marked with `[Autosave]`  <span style='font-size:11px; padding-left: 3px' >[read more](#autosave)</span>
 - Versioning control  <span style='font-size:11px; padding-left: 3px' >[read more](#versioning)</span>
   - Offers protection mechanisms such as renaming file and loading default
   - By changing version, it allows to introduce any kind of changes to the settings class
 - Customizable control over recovering from parsing exceptions  <span style='font-size:11px; padding-left: 3px' >[read more](#recovery)</span>
 - AES256 Encryption via a key  <span style='font-size:11px; padding-left: 3px' >[read more](#encryption)</span>
 - Fully extensible with [Json.NET](https://www.newtonsoft.com/json/) 's capabilities, attributes and settings
   - It'll be accurate to say that this library is built around [Json.NET](https://www.newtonsoft.com/json/)
 - `SettingsBag`, a `dynamic` option that uses a ConcurrentDictionary<string,object> eliminating the need for hardcoding POCO class <span style='font-size:11px; padding-left: 3px' >[read more](#dynamic-settings)</span> 

The Basics
---
Test project: https://github.com/Nucs/JsonSettings/tree/master/tests/JsonSettings.Tests <br>
Serialization Guide: https://www.newtonsoft.com/json/help/html/SerializationGuide.htm </br>

`JsonSettings` is the base abstract class serving as the base class for all settings objects the user defines. <br>
Creation, loading is done through static API where saving is through the settings object API.

Here is a self explanatory quicky of to how and what:

* **Hardcoded settings**
```C#
//Step 1: create a class and inherit JsonSettings
class MySettings : JsonSettings {
    //Step 2: override a default FileName or keep it empty. Just make sure to specify it when calling Load!
    //This is used for default saving and loading so you won't have to specify the filename/path every time.
    //Putting just a filename without folder will put it inside the executing file's directory.
    public override string FileName { get; set; } = "TheDefaultFilename.extension"; //for loading and saving.

    #region Settings

    public string SomeProperty { get; set; }
    public Dictionary<string, object> Dictionary { get; set; } = new Dictionary<string, object>();
    public int SomeNumberWithDefaultValue { get; set; } = 1;
    [JsonIgnore] public char ImIgnoredAndIWontBeSavedOrLoaded { get; set; }
    
    #endregion
    
    //Step 3: Override parent's constructors
    public MySettings() { }
    public MySettings(string fileName) : base(fileName) { }
}

//Step 4: Load
public MySettings Settings = JsonSettings.Load<MySettings>("config.json"); //relative path to executing file.
//or create a new empty
public MySettings Settings = JsonSettings.Construct<MySettings>("config.json");

//Step 5: Introduce changes and save.
Settings.SomeProperty = "ok";
Settings.Save();
```

* **Dynamic settings**
    * Dynamic settings will automatically create new keys.
    * Can accept any Type that Json.NET can serialize
    * [`ValueType`s](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/value-types) are returned as `Nullable<Type>`, therefore if a key doesn't exist - a null is returned.    
```C#
//Step 1: Just load it, it'll be created if doesn't exist.
public SettingsBag Settings = JsonSettings.Load<SettingsBag>("config.json");
//Step 2: use!
Settings["key"]  = "dat value tho";
Settings["key2"] = 123;
dynamic dyn = Settings.AsDynamic();
if ((int?)dyn.key2==123)
    Console.WriteLine("explode");
Settings.Save();
```
* **Encrypted settings**
    * Uses AES via `System.Security.Cryptography` (the .NET BCL); optional AES-GCM, AES-CCM, ChaCha20-Poly1305 or authenticated AES-CBC-HMAC.
    * Can be applied to any settings class because it is a module.
    * The secret can be a text password, a binary password, or a raw key.
```C#
MySettings Settings = JsonSettings.Load<MySettings>("config.json", q=>q.WithEncryption("mysecretpassword"));
SettingsBag Settings = JsonSettings.Load<SettingsBag>("config.json", q=>q.WithEncryption("mysecretpassword"));
//or
MySettings Settings = JsonSettings.Configure<MySettings>("config.json")
                     .WithEncryption("mysecretpassword")
               //or: .WithModule<EncryptionModule>("pass");
                     .LoadNow();

SettingsBag Settings = JsonSettings.Configure<SettingsBag>("config.json")
                     .WithEncryption("mysecretpassword")
               //or: .WithModule<EncryptionModule>("pass");
                     .LoadNow();
```
The secret can also be supplied as bytes. A `byte[]` **password** is stretched into the key with
the same PBKDF2 derivation as a text password (salted and iterated); a **raw key** is used verbatim
and must be 16, 24 or 32 bytes (AES-128/192/256):
```C#
// binary password - PBKDF2-derived, like a text password but with arbitrary bytes.
// Note: NOT the same credential as the text password whose UTF-8 bytes equal these.
byte[] password = Encoding.UTF8.GetBytes("mysecretpassword");
var a = JsonSettings.Configure<MySettings>("config.json").WithEncryption(password).LoadNow();

// raw AES key - used as-is, no derivation. You own the key's quality.
byte[] key = RandomNumberGenerator.GetBytes(32); // or from an env var / HSM / another KDF
var b = JsonSettings.Configure<MySettings>("config.json").WithEncryptionRawKey(key).LoadNow();

// both accept a fetcher, incl. one that receives the instance:
var c = JsonSettings.Configure<MySettings>("config.json")
                    .WithEncryptionRawKey(() => LoadKeyFromVault())
                    .LoadNow();
```

* **Hardcoded Settings with Autosave**
    * Automatic save will occur when any property changes
    * Works on any property — `virtual` is not required (as of 2.2.0); opt a property out with `[IgnoreAutosave]`
    * Requires package `nucs.JsonSettings.Autosave` and an `[Autosave]` attribute on the class.
```C#
Settings x  = JsonSettings.Load<Settings>().EnableAutosave(); //call after loading
//or:
ISettings x = JsonSettings.Load<Settings>().EnableIAutosave<Settings, ISettings>(); //Settings implements interface ISettings

x.Property = "value"; //Saved!
```

* **Dynamic Settings with Autosave**
    * Automatic save will occur when changes detected
    * note: SettingsBag has it's own implementation of EnableAutosave().
```C#
//Step 1:
SettingsBag Settings = JsonSettings.Load<SettingsBag>("config.json").EnableAutosave(); //call after loading
//Step 2:
Settings.AsDynamic().key = "wow"; //Saved!
Settings["key"] = "wow two"; //Saved!
```

Recovery
---
`RecoveryModule` provides handling for `JsonException` when calling `JsonSettings.LoadJson` during the loading process.
On a scenario of exception/failure, one of the following actions can take place:

- **RecoveryAction.Throw**<br/>
  Will throw JsonSettingsRecoveryException with the real exception as inner exception.
- **RecoveryAction.LoadDefault**<br/>
  Default settings will be loaded without touching the existing file until next save.
- **RecoveryAction.LoadDefaultAndSave**<br/>
  Default settings will be loaded and saved to disk immediately.
- **RecoveryAction.RenameAndLoadDefault**<br/>
  Will append the version to the end of the faulty file's name and load the default settings and save to disk.<br/>
  i.e. `myfile.json` versioned `1.0.0.5` will be renamed to `myfile.1.0.0.5-0.json` if it fails on parsing (the trailing `-0` is a collision counter — a second archive becomes `-1`, and so on) and the new default settings will be saved as the original filename.

All recovery properties and methods are suited for inheritance so extending is quite easy.

```C#
using Nucs.JsonSettings;
using Nucs.JsonSettings.Modulation.Recovery;

//attach RecoveryModule via the fluent extension and pick what happens on a parse failure:
var settings = JsonSettings.Configure<MySettings>("config.json")
                           .WithRecovery(RecoveryAction.RenameAndLoadDefault)
                           .LoadNow();

settings.SomeProperty = "hello";
settings.Save();

//...later config.json is corrupted on disk (hand-edited, truncated, a half-finished write).
//Loading again does NOT throw: the corrupt file is renamed aside and defaults are loaded.
settings = JsonSettings.Configure<MySettings>("config.json")
                       .WithRecovery(RecoveryAction.RenameAndLoadDefault)
                       .LoadNow();
//config.json is now the freshly-saved default; the corrupt copy is preserved next to it as
//config.<version>-0.json (or config.0.json when the class is not IVersionable).
```

Recovery composes with [versioning](#versioning): versioning runs when the file parses, recovery
catches the parse itself failing.

Versioning
---
`VersioningModule<T>` provides the ability to enforce a specific version so when new changes are introduced to your Settings class (scheme),
a user-defined action can take place. Any of the following actions can be taken:
- **VersioningResultAction.DoNothing**<br/>
  Will keep the old version if it was parsed by Json.NET successfully. otherwise RecoveryModule will handle the failure of loading.
- **VersioningResultAction.Throw**<br/>
  Will throw JsonSettingsRecoveryException with the real exception as inner exception.
- **VersioningResultAction.LoadDefault**<br/>
  Default settings will be loaded without touching the existing file until next save.
- **VersioningResultAction.LoadDefaultAndSave**<br/>
  Default settings will be loaded and saved to disk immediately.
- **VersioningResultAction.RenameAndLoadDefault**<br/>
  Will append the version to the end of the faulty file's name and load the default settings and save to disk.<br/>
  i.e. `myfile.json` versioned `1.0.0.5` will be renamed to `myfile.1.0.0.5-0.json` if it fails on parsing (the trailing `-0` is a collision counter — a second archive becomes `-1`, and so on) and the new default settings will be saved as the original filename.

There are two ways to specify which version to enforce.
1. Pass the version when calling `WithVersioning`.
2. Add `[EnforcedVersion("1.0.0.0")]` attribute to your `IVersionable.Version` property definition.<br/>
    When dealing with inheritance/virtual override, the attribute of the lowest inherited class will be used.

```C#
using Nucs.JsonSettings;
using Nucs.JsonSettings.Modulation;

//The settings class must implement IVersionable (contributes `Version Version { get; set; }`).
class MySettings : JsonSettings, IVersionable {
    public override string FileName { get; set; } = "config.json";
    public virtual Version Version { get; set; } = new Version(1, 0, 0, 0);
    public string Theme { get; set; } = "dark";

    public MySettings() { }
    public MySettings(string fileName) : base(fileName) { }
}

//1) Pass the enforced version explicitly:
var settings = JsonSettings.Configure<MySettings>("config.json")
                           .WithVersioning("1.0.0.0", VersioningResultAction.RenameAndLoadDefault)
                           .LoadNow();

//Later you ship a new scheme and bump the enforced version. A file still written as 1.0.0.0 no
//longer matches, so it is renamed to config.1.0.0.0-0.json and a fresh default config.json is saved.
settings = JsonSettings.Configure<MySettings>("config.json")
                       .WithVersioning("2.0.0.0", VersioningResultAction.RenameAndLoadDefault)
                       .LoadNow();

//2) ...or bake the version into the class with [EnforcedVersion] and use the version-less overload:
//     [EnforcedVersion("2.0.0.0")]
//     public virtual Version Version { get; set; } = new Version(1, 0, 0, 0);
var byAttribute = JsonSettings.Configure<MySettings>("config.json")
                              .WithVersioning(VersioningResultAction.RenameAndLoadDefault)
                              .LoadNow();
```

#### Policy
A comparison between versions is done by the `Policy` which is a `VersioningPolicyHandler` delegate (`(Version, Version) => bool`) passed during the construction of `VersioningModule<T>` or falls back to `static VersioningModule<T>.DefaultPolicy` which can be changed.<br/>
It is possible to change the static default policy by changing `VersioningModule<T>.DefaultPolicy` although each `VersioningModule<T>` can be assigned its own policy.<br/>
By default the versions must match exactly:<br/>
```C# 
static bool DefaultEqualPolicy(Version version, Version expectedVersion) {
    return expectedVersion?.Equals(version) != false;
}
```
Encryption
---
The default is **AES-256-CBC** over the serialized JSON (UTF-8 bytes), using only `System.Security.Cryptography` (the .NET base class library) &mdash; no third-party cryptography. The file holds a random IV followed by the AES-CBC ciphertext. Add `WithBase64()` to additionally store the result as copy-pasteable base64 text.

The secret comes in three forms:

| Call | Secret | How it becomes the key |
|---|---|---|
| `WithEncryption(string)` / `WithEncryption(SecureString)` | text password | PBKDF2 (salted, iterated) |
| `WithEncryption(byte[])` | binary password | the same PBKDF2 derivation, over the raw bytes |
| `WithEncryptionRawKey(byte[])` | raw AES key (16/24/32 bytes) | used verbatim, no derivation |

Each also has `Func<...>` and `Func<T, ...>` overloads for resolving the secret lazily (e.g. from a
vault or an environment variable).

Notes:
- A `byte[]` **password** is a *different credential* from the text password whose UTF-8 encoding
  equals those bytes — the text derivation folds in the string's character length, which raw bytes
  do not carry. Pick one form per file.
- A **raw key** skips PBKDF2, so its strength is entirely the key you provide; supply high-entropy
  key material (e.g. `RandomNumberGenerator.GetBytes(32)`), not a low-entropy value.
- The on-disk format is identical across all three (a random IV followed by AES-CBC blocks), and the
  text-password path is byte-for-byte compatible with every earlier version.

```C#
// text password (classic)
JsonSettings.Configure<MySettings>("config.json").WithEncryption("mysecretpassword").LoadNow();

// binary password (PBKDF2-derived)
JsonSettings.Configure<MySettings>("config.json").WithEncryption(passwordBytes).LoadNow();

// raw key (verbatim, 16/24/32 bytes for AES)
JsonSettings.Configure<MySettings>("config.json").WithEncryptionRawKey(key32).LoadNow();
```

The default `AesCbc` is unauthenticated and on-disk compatible with every earlier version. Pass an
`EncryptionAlgorithm` to choose another &mdash; including authenticated algorithms that detect a
tampered file, not only keep it confidential:

```C#
// authenticated AEAD (.NET 6.0+)
JsonSettings.Configure<MySettings>("config.json").WithEncryption("password", EncryptionAlgorithm.AesGcm).LoadNow();
JsonSettings.Configure<MySettings>("config.json").WithEncryptionRawKey(key32, EncryptionAlgorithm.ChaCha20Poly1305).LoadNow();
```

`AesCbc` and `AesCbcHmac` are available on every target framework; `AesGcm`, `AesCcm` and
`ChaCha20Poly1305` require .NET 6.0+. There is no algorithm marker in the file, so read it back with
the same algorithm it was written with; only `AesCbc` reads files from older versions. Encryption runs
entirely on `System.Security.Cryptography` &mdash; there is no third-party cryptographic dependency.

Autosave
---
Autosaving appends a save to the end of every property setter of a class marked `[Autosave]`.
This happens at compile time, via IL weaving ([AspectInjector](https://github.com/pamidur/aspect-injector)),
in the assembly that declares the class. Nothing is generated at runtime.

```C#
[Autosave]
public class MySettings : JsonSettings {
    public override string FileName { get; set; } = "config.json";
    public string Name { get; set; }        // no 'virtual' required
    public int    Count { get; set; }
}

var settings = JsonSettings.Load<MySettings>("config.json").EnableAutosave();
settings.Name = "changed";   // saved
```

#### What changed in 2.2.0
Autosave used to build a runtime proxy with `Castle.Core`, which forced three restrictions
that are now gone:

| Before (Castle.DynamicProxy) | Now (compile-time weaving) |
|---|---|
| Every public property had to be `virtual` | Ordinary properties work; `virtual` is irrelevant |
| The class could not be `sealed` | `sealed` classes work |
| `EnableAutosave()` returned a **different** object, so a reference captured beforehand silently did not autosave | Returns the same instance; every reference to it autosaves |
| Impossible under Native AOT (`System.Reflection.Emit`) | No runtime codegen at all |

In exchange there is one new requirement: the class must carry `[Autosave]`. Calling
`EnableAutosave()` on a class without it throws `JsonSettingsException` rather than
silently doing nothing.

`[Autosave]` is **not inherited**. A setter is woven where it is declared, so every class in
a settings hierarchy that declares properties you want saved needs its own attribute.

Two smaller behavioural notes for anyone migrating from 2.1.0:

- **`virtual` is no longer an opt-out.** Under the proxy, a non-virtual property was silently
  skipped; some code relied on that to keep a property out of autosaving. Every setter is now
  woven regardless of `virtual`, so a property that must **not** autosave has to say so with
  `[IgnoreAutosave]` (or `[JsonIgnore]`).
- **`EnableAutosave()` is idempotent.** Calling it twice on the same instance returns that
  instance and does not attach a second autosave module.

The Castle-era `JsonSettingsAutosaveExtensions.Options` field (a `Castle.DynamicProxy.ProxyGenerationOptions`)
is removed, since the type it exposed no longer exists in the dependency graph.

#### Attributes
Properties can be marked with `IgnoreAutosaveAttribute` (`JsonIgnoreAttribute` will also work)
to be excluded from the monitored properties for changes. This applies to collections too: an
`[IgnoreAutosave]` `ObservableCollection` does not save when its contents change.

#### Behaviour notes
- **Indexers are not monitored.** Writing `settings[key] = value` does not autosave — an indexer
  is not a serializable property. Use a normal property or call `Save()`.
- **Reentrancy is safe.** Writing a monitored property from inside an `AfterSave` handler does not
  trigger another save (it would otherwise recurse); the value is kept in memory and persists on
  the next save.
- **`SuspendAutosave` nests.** Nested suspension scopes are reference-counted and collapse into a
  single save when the outermost scope closes; an inner scope closing does not end suspension.
- **A failing save surfaces at the assignment.** If the triggered `Save()` throws, the exception
  propagates out of the property assignment (the new value is already set in memory).
- **Disposing the settings unbinds autosave**, including handlers attached to nested collections.
- **Loading does not autosave.** `Load()`, `LoadDefault()` and a versioning reload populate the
  object from disk through its setters; those writes are not user edits and do not save back
  (autosave resumes normally afterward).
- **`IVersionable.Version` is not monitored.** It is framework metadata managed by the versioning
  module and rides along in every ordinary save, so changing it does not by itself autosave. (A
  property named `Version` on a class that does *not* implement `IVersionable` is ordinary user
  data and is monitored.)

#### Requirements
- Install `nucs.JsonSettings.Autosave` nuget package
- Mark the settings class `[Autosave]`
- Call `mySettings.EnableAutosave()` extension after calling `Load`

#### How the weave runs (out of process, since 2.3.0)
AspectInjector's stock in-process MSBuild task leaks file handles into the MSBuild node, which
deterministically failed small **executable** consumers at the SDK's `CreateAppHost` step
(`MSB4018` / *"The process cannot access the file '&lt;App&gt;.dll' because it is being used by
another process"*) — merely referencing the package was enough. Since 2.3.0 the shipped build
targets run the identical weaver task in a short-lived child MSBuild process instead, so every
leaked handle is closed at child exit before `CreateAppHost` runs; weaving behaviour and
incrementality are unchanged. Opt back into the in-process weave with
`<NucsJsonSettingsOutOfProcWeave>false</NucsJsonSettingsOutOfProcWeave>`. See
`docs/aspectinjector-2.9.0-apphost-lock.md` for the full forensics.

#### Strong-named consumers
IL weaving rewrites the assembly after the compiler has signed it, and AspectInjector 2.9.0
[retired its re-signing feature](https://github.com/pamidur/aspect-injector/releases/tag/2.9.0).
The package therefore ships MSBuild targets that re-sign the assembly with your own
`$(AssemblyOriginatorKeyFile)` after the weave. If `sn.exe` cannot be found the build warns
(`NJS1001`) rather than failing; opt out entirely with
`<NucsAutosaveResignAfterWeaving>false</NucsAutosaveResignAfterWeaving>`.

#### Suspend Autosave
In some scenarios, there might be multiple close changes to the configuration object. Normally that would trigger multiple save calls.

To prevent that, the developer can create a `SuspendAutosave` object which will postpone the save to when `SuspendAutosave` will be disposed or `Resume` called.
If there were no changes between the allocation of `SuspendAutosave` object and disposal/resume then save won't be called.

```C#
var settings = JsonSettings.Load<MySettings>("config.json").EnableAutosave();

using (settings.SuspendAutosave()) {
    settings.Width  = 800;    // does not save yet
    settings.Height = 600;    // does not save yet
    settings.Title  = "App";  // does not save yet
}                             // one save here on dispose — and only if something changed

//or drive it manually instead of a using-block:
var suspender = settings.SuspendAutosave();
settings.Width = 1024;
suspender.Resume();  // commits the single pending save (same as Dispose); a second call is a no-op
```

`SuspendAutosave()` resolves the object's suspension module — the `AutosaveModule` on a woven
class, the bag's own `SettingsBagAutosaveModule` on a `SettingsBag` — so call `EnableAutosave()` first. Scopes
are reference-counted and **nest** — only the outermost one commits, once.

WPF Support with INotifyPropertyChanged/INotifyCollectionChanged
---
Any settings class can turn into a ViewModel with full autosave support making window settings and state persistence much simpler.

When your settings class inherits `INotifyPropertyChanged`, upon calling `EnableAutosave`,
a `NotificationBinder` is attached to the settings object that'll listen to the settings class's:
- `event PropertyChanged` calls
- All properties that implement `INotifyPropertyChanged` will bind to their `event PropertyChanged`
- All properties that implement `INotifyCollectionChanged` such as `ObservableCollection<T>`  will bind to their `event CollectionChanged`
- All other properties save through their woven setter (`virtual` is not required as of 2.2.0).

So evidently, objects inside ObservableCollection or other nested properties that are not in the settings class are not monitored for changes.<br/><br/>
Saving on a plain property write is handled by the woven setter, so a hand-written setter that
raises `OnPropertyChanged` and an auto-implemented one behave identically. The
`NotificationBinder` is what re-binds nested `INotifyPropertyChanged` /
`INotifyCollectionChanged` objects when the property holding them is replaced.

#### Requirements
- Settings class inherit `INotifyPropertyChanged` (e.g. by deriving `NotifiyingJsonSettings`)
- Mark the settings class `[Autosave]`
- Install `nucs.JsonSettings.Autosave` nuget package
- Call `mySettings.EnableAutosave()` extension after calling `Load`

#### Producing notifications for the View — `[NotifyChanges]`
The above makes autosave *react* to `PropertyChanged`. To make a setter *raise* it — so a binding
(WPF, WinForms, Avalonia, WinUI, MAUI, Uno) refreshes — without hand-writing `OnPropertyChanged()` in
every setter, including on **auto-properties** (which otherwise save but never notify), install the
separate **`Nucs.JsonSettings.NotifyChanges`** package and mark the class `[NotifyChanges]`:

```C#
[Autosave, NotifyChanges]                     // [Autosave] from Nucs.JsonSettings.Autosave,
public class WindowSettings : NotifiyingJsonSettings {   // [NotifyChanges] from Nucs.JsonSettings.NotifyChanges
    public override string FileName { get; set; } = "window.json";
    public double Width { get; set; }   // binds two-way, saves, and notifies — no boilerplate
    public string Title { get; set; }
}
```

- Compile-time weave like `[Autosave]`, **not inherited**, and composes with it (one write saves and
  notifies once). Put it on auto-properties — a hand-written setter that already calls
  `OnPropertyChanged()` would notify twice. Framework-neutral: depends only on `System.ComponentModel`,
  not on WPF.
- `NotificationGuard` controls when it fires, per class or per property: `OnlyChanged` (default),
  `SkipNullOrDefault`, `Always` — and they combine (`[Flags]`).
- Silence a property with `[IgnoreNotify]` (independent of `[IgnoreAutosave]` — a property can save
  without notifying, or the reverse); framework `FileName`/`Modulation`/`Version` never notify.
- The class must own the event: `NotifiyingJsonSettings`, or an MVVM base recognised by convention
  (`OnPropertyChanged` / `RaisePropertyChanged` / `NotifyOfPropertyChange`). For a class with **no**
  base, `[NotifyChangesMixin]` injects `INotifyPropertyChanged` for you (per-instance; best for a single
  class — a hierarchy should use `NotifiyingJsonSettings` + `[NotifyChanges]`).
- Also raises **`INotifyPropertyChanging`** before the change (on `NotifiyingJsonSettings`, a convention
  raiser, or the mixin), fans a change out to a computed property with **`[NotifyChangesFor(nameof(…))]`**,
  and marshals notifications onto the UI thread for off-thread writes via
  **`EnableNotificationMarshaling()`**.

See the [Notifications & Data Binding guide](docs/website-src/docs/notifications.md) for the guard
details, the mixin, `INotifyPropertyChanging`, `[NotifyChangesFor]`, `SynchronizationContext`
marshalling, nested-collection autosave, threading, and a comparison with Fody `PropertyChanged`,
CommunityToolkit.Mvvm and ReactiveUI.

For a runnable tour, [`examples/JsonSettings.Examples.UI`](examples/JsonSettings.Examples.UI) is a
WPF app in which every control is a bound settings property — the window's own position/size/title
persist through the binding, and one tab per integration (guards, `[NotifyChangesFor]`, the
opt-outs, nested collections, the mixin, raiser conventions, `EnableIAutosave`, marshalling) shows
its save/notification counters, an activity log and the JSON file on disk, live:

```sh
dotnet run --project examples/JsonSettings.Examples.UI -f net8.0-windows
```

Throttled Save
---
Upcoming feature...

Dynamic Settings Bag
---
SettingsBag internally stores a key-value dictionary. 
Any type of Value can be passed as long as Json.NET knows how to serialize it. <br/>
SettingsBag has built-in feature for autosaving that can be enabled by calling EnableAutosave without WPF binding support. <br/>

```C#
var bag = JsonSettings.Load<SettingsBag>("bag.json").EnableAutosave();
bag["Name"] = "value";           // saved
bag.Remove("Name");              // saved
dynamic d = bag.AsDynamic();
d.Other = 42;                    // saved (routes through the bag)
```

This is a **separate** autosave from the `[Autosave]` weaving used for typed classes — it is
dictionary-backed, needs no attribute, and is what `SettingsBag.EnableAutosave()` (the instance
method) turns on. Its own `SettingsBagAutosaveModule` shares the `SuspensionModule` state
machine with the woven path, so it inherits the same guarantees:
`SuspendAutosave()` (including nesting), reentrancy safety (writing the bag inside an `AfterSave`
handler does not recurse), and `Remove`/`RemoveWhere` autosave like an index write.

Notes:
- Calling the `EnableAutosave()` extension on a `JsonSettings`-typed reference to a bag routes to
  the bag's own autosave, so it behaves the same as calling the instance method.
- `AsDynamic()` returns a disposable wrapper; using it after `Dispose()` throws
  `ObjectDisposedException`.

Changing JsonSerializerSettings
---
The default settings are defined on `static JsonSettings.SerializationSettings`.
```C#
public static JsonSerializerSettings SerializationSettings { get; set; } = new JsonSerializerSettings {
    Formatting = Formatting.Indented, 
    ReferenceLoopHandling = ReferenceLoopHandling.Ignore, 
    NullValueHandling = NullValueHandling.Include, 
    ContractResolver = new FileNameIgnoreResolver(), 
    TypeNameHandling = TypeNameHandling.Auto, 
    MaxDepth = 128
};
```

To alter the `JsonSerializerSettings`, it's best to understand how the library is resolving which settings to use during serialization/deserialization as follows:
```C#
/// <summary>
///     Returns configuration based on the following fallback: <br/>
///     settings ?? this.OverrideSerializerSettings ?? JsonSettings.SerializationSettings ?? JsonConvert.DefaultSettings?.Invoke()
///              ?? throw new JsonSerializationException("Unable to resolve JsonSerializerSettings to serialize this JsonSettings");
/// </summary>
/// <param name="settings">If passed a non-null, This is the settings intended to use, not any of the fallbacks.</param>
/// <exception cref="JsonSerializationException">When no configuration valid was found.</exception>
protected virtual JsonSerializerSettings ResolveConfiguration(JsonSerializerSettings? settings = null) {
    return settings
           ?? this.OverrideSerializerSettings
           ?? JsonSettings.SerializationSettings
           ?? JsonConvert.DefaultSettings?.Invoke()
           ?? throw new JsonSerializationException("Unable to resolve JsonSerializerSettings to serialize this JsonSettings");
}
```
1. `settings` parameter is an internal mechanism when handling defaults. If passed a non-null, This is the settings intended to use, not any of the following fallbacks.
2. `this.OverrideSerializerSettings` is a property in every class inheriting `JsonSettings` allowing personalized settings per object.
   The `OverrideSerializerSettings` property and `ResolveConfiguration` method are both `virtual` and can be overriden to redirect the resolving to where ever you see fit or with what-ever predefined value.
3. `static JsonSettings.SerializationSettings` is the default for all `JsonSettings` objects.
4. `static JsonConvert.DefaultSettings` is the default settings defined on a Json.NET level.


Converters
---
Defining converters or changing the serialization settings globally can be done by adding a converter to `static JsonSettings.SerializationSettings` as follows:<br/>
```C#
//call during app startup
JsonSettings.SerializationSettings.Converters.Add(new Newtonsoft.Json.Converters.VersionConverter());
```
Alternatively per object setting can be done by setting or inheriting `JsonSettings.OverrideSerializerSettings` property but
it is important to also specify the default configuration so `JsonSettings` behavior will remain persistent ([see more](#changing-jsonserializersettings)) .

### JsonConverterAttribute
By far the easiest way to specify a converter is by specifying a `JsonConverterAttribute` on the property and Json.NET will do the rest.
```C#
[JsonConverter(typeof(ExchangeConverter))]
public ExchangeType Exchange { get; set; }
```

`JsonConverterAttribute` can also be specified on an interface property as it is used in `IVersionable` and will apply to any class inheriting it.
<br/>This is the best approach for other libraries because by specifying an attribute, no matter what `JsonSerializerSettings` will be specified by the developer, Json.NET will always serialize this property with the specified converter.  
```C#
public interface IVersionable {
    [JsonConverter(typeof(Newtonsoft.Json.Converters.VersionConverter))]
    public Version Version { get; set; }
}
```


Modulation Api
---
Key points
- All modules are stored inside `JsonSettings`.`ModuleSocket Modulation { get; }`.
- `ModuleSocket` stores all modules attached to this `JsonSettings` object.
- Every settings object gets a new module object allocated for every module configured.
- Attaching modules is done via static extensions <span style='font-size:11px; padding-left: 3px' >[read more](https://github.com/Nucs/JsonSettings/blob/master/src/JsonSettings/Fluent/FluentJsonSettings.cs) </span>
- All modules provided by the library have properties and methods that are suited for inheritance so extending is easy.

```C#
using Nucs.JsonSettings;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Modulation.Recovery;

//Attach a module fluently — by instance, or by type with constructor arguments:
var settings = JsonSettings.Configure<MySettings>("config.json")
                           .WithModule(new Base64Module())                                 // your own instance
                           .WithModule<MySettings, RecoveryModule>(RecoveryAction.LoadDefault) // constructed for you
                           .LoadNow();
```

`WithEncryption`, `WithBase64`, `WithVersioning` and `WithRecovery` are all thin wrappers over
`WithModule` — e.g. `WithRecovery(action)` is exactly `WithModule(new RecoveryModule(action))`.

**With `Construct`.** `Construct<T>(args)` is the constructor-args sibling of `Configure<T>(filename)`:
both hand back a fresh, fully-configured instance that has **not** read the file yet, so you can wire
up modules (or seed defaults in memory) before an explicit `Load`/`Save`:

```C#
//build a fresh instance, attach modules, then load explicitly:
var settings = JsonSettings.Construct<MySettings>("config.json") // ctor args go to your constructor
                           .WithModule(new Base64Module())
                           .WithEncryption("password")
                           .LoadNow();

//or use it purely in-memory — seed defaults and write them without reading an existing file:
var seeded = JsonSettings.Construct<MySettings>("config.json");
seeded.SomeProperty = "default";
seeded.Save();
```

### Execution Order
The events are many to allow as much interception as possible.<br>
The event handlers do not return any data but instead they receive a reference of the object that can be modified and will be used in the next stage.<br>
**Loading**
```C#
event BeforeLoadHandler BeforeLoad(JsonSettings sender, ref string source); //source is the file that will be loaded.
event DecryptHandler Decrypt(JsonSettings sender, ref byte[] data);
event AfterDecryptHandler AfterDecrypt(JsonSettings sender, ref byte[] data);
event BeforeDeserializeHandler BeforeDeserialize(JsonSettings sender, ref string data);
event BeforeRepopulateHandler BeforeRepopulate(JsonSettings sender); //brackets the populate itself; fires on EVERY populate incl. LoadDefault and direct LoadJson
event AfterRepopulateHandler AfterRepopulate(JsonSettings sender, bool successfulPopulate); //from a finally; false when the populate threw halfway
event AfterDeserializeHandler AfterDeserialize(JsonSettings sender);
event AfterLoadHandler AfterLoad(JsonSettings sender, bool successfulLoad);
```
And in a case of `JsonException` during `LoadJson`
```C#
//recovered marks if a recovery from failure was successful, handled will prevent any further modules from attempting to recover.
//if recovered is returned false, JsonSettingsException will be thrown with the original exception as inner exception
event TryingRecoverHandler TryingRecover(JsonSettings sender, string fileName, JsonException? exception, ref bool recovered, ref bool handled);
event RecoveredHandler Recovered(JsonSettings sender);
```
**Saving**
```C#
event BeforeSaveHandler BeforeSave(JsonSettings sender, ref string destinition);
event BeforeSerializeHandler BeforeSerialize(JsonSettings sender);
event AfterSerializeHandler AfterSerialize(JsonSettings sender, ref string data);
event EncryptHandler Encrypt(JsonSettings sender, ref byte[] data);
event AfterEncryptHandler AfterEncrypt(JsonSettings sender, ref byte[] data);
event AfterSaveHandler AfterSave(JsonSettings sender, string destinition);
```

#### Cryptography / Encoding Decoding
When attaching to `OnEncrypt` event, it'll push to the end of the event queue - meaning it will receive the data after all the events/modules that were attached to it before.<br>
When attaching to `OnDecrypt`, it is pushed to the beginning of the event queue.<br>
Hence encryption/encoding and decryption/decoding is automatically in the right order.<br>

