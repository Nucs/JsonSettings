# <img src="https://i.imgur.com/BOExs52.png" width="25" style="margin: 5px 0px 0px 10px"/> JsonSettings
[![Nuget downloads](https://img.shields.io/nuget/vpre/Nucs.JsonSettings.svg)](https://www.nuget.org/packages/nucs.JsonSettings/)
[![NuGet](https://img.shields.io/nuget/dt/Nucs.JsonSettings.svg)](https://github.com/Nucs/JsonSettings)
[![GitHub license](https://img.shields.io/github/license/mashape/apistatus.svg)](https://github.com/Nucs/JsonSettings/blob/master/LICENSE)

This library aims to simplify the process of creating configuration for your C# app/service 
by utilizing the serialization capabilities of [Json.NET](https://www.newtonsoft.com/json/help/html/SerializationGuide.htm)
to serialize nested (custom) objects, dictionaries and lists as simply as by creating a `POCO` and inheriting `JsonSettings` class.<br/>


### Installation
```sh
PM> Install-Package nucs.JsonSettings
```

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
- [Dynamic Settings](#dynamic-settings)
- [Modulation Api](#modulation-api)
- [Changing JsonSerializerSettings](#changing-jsonserializersettings)
- [Converters](#converters)
- [License](https://github.com/Nucs/JsonSettings/blob/master/LICENSE)


Features Overview
---
 - Initialized in a fluent static API <span style='font-size:11px; padding-left: 3px' >[read more](#the-basics)</span>
 - Cross-platform targeting `netstandard2.0`
 - Modularity allowing easy extension and high control over behavior on a per-object level  <span style='font-size:11px; padding-left: 3px' >[read more](#modulation-api)</span>
 - Autosaving on changes  <span style='font-size:11px; padding-left: 3px' >[read more](#autosave)</span>
   - Via `INotificationChanged`/`INotificationCollectionChanged` allowing WPF binding (with interval throttling support to avoid cpu overload)  <span style='font-size:11px; padding-left: 3px' >[read more](#inotificationchanged-and-wpf-support)</span>
   - Via `Castle.DynamicProxy` generated wrapper  <span style='font-size:11px; padding-left: 3px' >[read more](#proxification)</span>
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
    * Automatic save will occur when changes detected on virtual properties
    * All properties are required
    * Requires package `nucs.JsonSettings.Autosave` that uses `Castle.Core`.
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
Autosaving detects changes in all virtual properties by creating a proxy wrapper using Castle.Core. <br/>
The requirement for the class to be autosaved is for all public properties have to be virtual and the class to be non-sealed.
Any properties that are not marked virtual will not work properly (not just won't autosave), therefore an `JsonSettingsException` is thrown if during proxification a non-virtual property is detected.

#### Attributes
Properties can be marked with `IgnoreAutosaveAttribute`  (`IgnoreJsonAttribute` will also work) to be excluded from the monitored properties for changes.<br/>
All proxy wrapper classes generated with `ProxyGeneratedAttribute`.

#### Requirements
- All public properties must be virtual
- Install `nucs.JsonSettings.Autosave` nuget package
- Call `mySettings.EnableAutosave()` extension after calling `Load`

//TODO: example

#### Suspend Autosave
In some scenarios, there might be multiple close changes to the configuration object. Normally that would trigger multiple save calls.

To prevent that, the developer can create a `SuspendAutosave` object which will postpone the save to when `SuspendAutosave` will be disposed or `Resume` called.
If there were no changes between the allocation of `SuspendAutosave` object and disposal/resume then save won't be called.

//TODO: example

WPF Support with INotificationChanged/INotificationCollectionChanged
---
Any settings class can turn into a ViewModel with full autosave support making window settings and state persistence much simpler.

When your settings class inherits `INotifyPropertyChanged`, upon calling `EnableAutosave`, 
a different interceptor with `NotificationBinder` will be attached to the generated proxy object that'll listen to the settings class's:
- `event PropertyChanged` calls
- All properties that implement `INotifyPropertyChanged` will bind to their `event PropertyChanged`
- All properties that implement `INotificationCollectionChanged` such as `ObservableCollection<T>`  will bind to their `event CollectionChanged`
- All virtual properties that do not answer to the criteria above.

So evidently, objects inside ObservableCollection or other nested properties that are not in the settings class are not monitored for changes.<br/><br/>
Any properties that are not marked virtual will not work properly (not just won't autosave), therefore a `JsonSettingsException` is thrown if during proxification a non-virtual property is detected.

#### Requirements
- Settings class inherit `INotifyPropertyChanged`
- All public properties must be virtual
- Install `nucs.JsonSettings.Autosave` nuget package
- Call `mySettings.EnableAutosave()` extension after calling `Load`

Throttled Save
---
Upcoming feature...

Dynamic Settings
---
SettingsBag internally stores a key-value dictionary. 
Any type of Value can be passed as long as Json.NET knows how to serialize it. <br/>
SettingsBag has built-in feature for autosaving that can be enabled by calling EnableAutosave without WPF binding support. <br/>

//TODO: add example

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
By far the easiest way to specify a converter is by specifying a `JsonConverterAttribute` the property and Json.NET will do the rest.
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
