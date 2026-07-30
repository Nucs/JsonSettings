# <img src="assets/icon.png" width="25" style="margin: 5px 0px 0px 10px"/> JsonSettings
[![Nuget version](https://img.shields.io/nuget/vpre/Nucs.JsonSettings.svg)](https://www.nuget.org/packages/nucs.JsonSettings/)
[![Nuget downloads](https://osscdn.nucs.workers.dev/jsonsettings-downloads-ujVrxmtCZN.svg)](https://www.nuget.org/packages/nucs.JsonSettings/)
[![GitHub license](https://img.shields.io/github/license/Nucs/JsonSettings.svg)](https://github.com/Nucs/JsonSettings/blob/master/LICENSE)

This library aims to simplify the process of creating configuration for your C# app/service 
by utilizing the serialization capabilities of [Json.NET](https://www.newtonsoft.com/json/help/html/SerializationGuide.htm)
to serialize nested (custom) objects, dictionaries and lists as simply as by creating a `POCO` and inheriting `JsonSettings` class.<br/>


### Installation
```sh
dotnet add package Nucs.JsonSettings
dotnet add package Nucs.JsonSettings.Autosave   # optional, only for EnableAutosave()
```
```sh
PM> Install-Package Nucs.JsonSettings
PM> Install-Package Nucs.JsonSettings.Autosave
```

Both packages target `netstandard2.0`, `net48`, `net6.0`, `net8.0` and `net10.0`.
The `netstandard2.0` asset covers everything without an exact match, including
`net472`+, `netcoreapp3.1`, `net5.0`, `net7.0`, `net9.0`, Unity and Xamarin.

> **Native AOT / trimming:** neither package is trim-safe yet. Under `PublishTrimmed` or
> `PublishAot` a settings file can still be silently written back as `{}` with no exception,
> because Newtonsoft.Json's reflection is invisible to the trimmer.
> `Nucs.JsonSettings.Autosave` no longer blocks AOT on its own: it is built on compile-time
> IL weaving and emits nothing at runtime, where it previously used `Castle.DynamicProxy`
> and threw `PlatformNotSupportedException`.
> See [docs/AOT.md](https://github.com/Nucs/JsonSettings/blob/master/docs/AOT.md) for the
> measurements, the causes and the workarounds.

### Strong naming

Both packages are strong-named, so they can be referenced from a strong-named assembly:

```
Nucs.JsonSettings,          PublicKeyToken=cc7b13ffcd2ddd51
Nucs.JsonSettings.Autosave, PublicKeyToken=cc7b13ffcd2ddd51
```

The key is Microsoft's [published open-source signing key](https://github.com/dotnet/arcade/blob/main/src/Microsoft.DotNet.Arcade.Sdk/tools/snk/Open.snk)
— the same one `netstandard`, `System.Memory` and `System.Buffers` carry. Microsoft publishes
its private half so that open-source projects can ship strong-named assemblies without running
signing infrastructure.

> **This is identity, not authenticity.** Anyone can sign an assembly with that key, so a
> strong name here tells the runtime which assembly this is and lets it bind versions — it does
> **not** attest that the file came from this project. Neither package is Authenticode-signed or
> NuGet author-signed, and `InternalsVisibleTo` is not an access control. If you need to verify
> origin, verify the SHA-256 checksums published with each
> [GitHub release](https://github.com/Nucs/JsonSettings/releases).

Versions before 2.1.0 shipped **unsigned** (`PublicKeyToken=null`). Upgrading across that
boundary changes assembly identity, so a binding redirect written against the old unsigned
identity will not match — remove it rather than editing it.

See [docs/SIGNING.md](https://github.com/Nucs/JsonSettings/blob/master/docs/SIGNING.md) for how
to verify it yourself and what the build enforces.

## Table of Contents
- [Features Overview](#features-overview)
- [The Basics](#the-basics)
- [Modules](#recovery)
    - [Recovery](#recovery)
    - [Versioning](#versioning)
    - [Encryption](#encryption)
    - [Autosave](#autosave)
      - [Suspend Saving](#suspend-autosave)
      - [WPF Support with INotificationChanged/INotificationCollectionChanged](#wpf-support-with-inotificationchangedinotificationcollectionchanged)
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
   - Via `INotificationChanged`/`INotificationCollectionChanged` allowing WPF binding (with interval throttling support to avoid cpu overload)  <span style='font-size:11px; padding-left: 3px' >[read more](#inotificationchanged-and-wpf-support)</span>
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
dyn.Save(); /* or */ Settings.Save();
```
* **Encrypted settings**
    * Uses AES/Rijndael
    * Can be applied to any settings class because it is a module.
```C#
MySettings Settings = JsonSettings.Load<MySettings>("config.json", q=>q.WithEncryption("mysecretpassword"));
SettingsBag Settings = JsonSettings.Load<SettingsBag>("config.json", q=>q.WithEncryption("mysecretpassword"));
//or
MySettings Settings = JsonSettings.Configure<MySettings>("config.json")
                     .WithEncryption("mysecretpassword")
               //or: .WithModule<RijndaelModule>("pass");
                     .LoadNow();

SettingsBag Settings = JsonSettings.Configure<SettingsBag>("config.json")
                     .WithEncryption("mysecretpassword")
               //or: .WithModule<RijndaelModule>("pass");
                     .LoadNow();
```

* **Hardcoded Settings with Autosave**
    * Automatic save will occur when any property changes
    * Works on any property — `virtual` is not required (as of 2.2.0); opt a property out with `[IgnoreAutosave]`
    * Requires package `nucs.JsonSettings.Autosave` and an `[Autosave]` attribute on the class.
```C#
Settings x  = JsonSettings.Load<Settings>().EnableAutosave(); //call after loading
//or:
ISettings x = JsonSettings.Load<Settings>().EnableIAutosave<ISettings>(); //Settings implements interface ISettings

x.Property = "value"; //Saved!
```

* **Dynamic Settings with Autosave**
    * Automatic save will occur when changes detected
    * note: SettingsBag has it's own implementation of EnableAutosave().
```C#
//Step 1:
SettingsBag Settings = JsonSettings.Load<SettingsBag>("config.json").EnableAutosave(); //call after loading
//Unavailable for hardcoded settings yet! (ty netstandard2.0 for not being awesome on proxies)
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
  i.e. `myfile.json` versioned `1.0.0.5` will be renamed to `myfile.1.0.0.5.json` if it fails on parsing and the new default settings will be saved as the original filename.

All recovery properties and methods are suited for inheritance so extending is quite easy.

//TODO: add example

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
  i.e. `myfile.json` versioned `1.0.0.5` will be renamed to `myfile.1.0.0.5.json` if it fails on parsing and the new default settings will be saved as the original filename.

There are two ways to specify which version to enforce.
1. Pass the version when calling `WithVersioning`.
2. Add `[EnforcedVersion("1.0.0.0")]` attribute to your `IVersionable.Version` property definition.<br/>
    When dealing with inheritance/virtual override, the attribute of the lowest inherited class will be used.


//TODO: example

#### Policy
A comparison between versions is done by the `Policy` which is a `Func<Version, Version, bool>` passed during the construction of `VersioningModule<T>` or fallbacks to `static VersioningModule.DefaultPolicy` which can be changed.<br/>
It is possible to change the static default policy by changing `VersioningModule.DefaultPolicy` although each `VersioningModule<T>` can be assigned its own policy.<br/>
By default the versions must match exactly:<br/>
```C# 
static bool DefaultEqualPolicy(Version version, Version expectedVersion) {
    return expectedVersion?.Equals(version) != false;
}
```
Encryption
---
The encryption used is AES256, the parsed json is decoded to UTF8 bytes, converted to encrypted bytes and then to base64 string encoding.<br/>
The decision to save it as base64 is to make it easily copiable as a string.

//TODO: example

Special thanks to [Rijndael256](https://github.com/2Toad/Rijndael256) for their AES encryption implementation. 

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

#### Requirements
- Install `nucs.JsonSettings.Autosave` nuget package
- Mark the settings class `[Autosave]`
- Call `mySettings.EnableAutosave()` extension after calling `Load`

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

//TODO: example

WPF Support with INotificationChanged/INotificationCollectionChanged
---
Any settings class can turn into a ViewModel with full autosave support making window settings and state persistence much simpler.

When your settings class inherits `INotifyPropertyChanged`, upon calling `EnableAutosave`,
a `NotificationBinder` is attached to the settings object that'll listen to the settings class's:
- `event PropertyChanged` calls
- All properties that implement `INotifyPropertyChanged` will bind to their `event PropertyChanged`
- All properties that implement `INotificationCollectionChanged` such as `ObservableCollection<T>`  will bind to their `event CollectionChanged`
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

Throttled Save
---
Upcoming feature...

Dynamic Settings Bag
---
SettingsBag internally stores a key-value dictionary. 
Any type of Value can be passed as long as Json.NET knows how to serialize it. <br/>
SettingsBag has built-in feature for autosaving that can be enabled by calling EnableAutosave without WPF binding support. <br/>

//TODO: add example

Changing JsonSerializerSettings
---
The default settings are defined on `static JsonSettings.SerializationSettings`.
```C#
public static JsonSerializerSettings SerializationSettings { get; set; } = new JsonSerializerSettings {
    Formatting = Formatting.Indented, 
    ReferenceLoopHandling = ReferenceLoopHandling.Ignore, 
    NullValueHandling = NullValueHandling.Include, 
    ContractResolver = new FileNameIgnoreResolver(), 
    TypeNameHandling = TypeNameHandling.Auto
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
- Attaching modules is done via static extensions <span style='font-size:11px; padding-left: 3px' >[read more](https://github.com/Nucs/JsonSettings/blob/master/src/Fluent/FluentJsonSettings.cs) </span>
- All modules provided by the library have properties and methods that are suited for inheritance so extending is easy.

//TODO: example + example with Construct

### Execution Order
The events are many to allow as much interception as possible.<br>
The event handlers do not return any data but instead they receive a reference of the object that can be modified and will be used in the next stage.<br>
**Loading**
```C#
event BeforeLoadHandler BeforeLoad(JsonSettings sender, ref string source); //source is the file that will be loaded.
event DecryptHandler Decrypt(JsonSettings sender, ref byte[] data);
event AfterDecryptHandler AfterDecrypt(JsonSettings sender, ref byte[] data);
event BeforeDeserializeHandler BeforeDeserialize(JsonSettings sender, ref string data);
event AfterDeserializeHandler AfterDeserialize(JsonSettings sender);
event AfterLoadHandler AfterLoad(JsonSettings sender);
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

