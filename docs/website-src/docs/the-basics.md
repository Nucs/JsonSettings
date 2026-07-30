# The Basics

`JsonSettings` is the base abstract class serving as the base class for all settings objects you
define. Creation and loading are done through the static API; saving is done through the settings
object's own API. Here is a self-explanatory quick tour.

## Hardcoded settings

```csharp
using System.Collections.Generic;
using Newtonsoft.Json;
using Nucs.JsonSettings;

//Step 1: create a class and inherit JsonSettings
class MySettings : JsonSettings {
    //Step 2: override a default FileName or keep it empty. Just make sure to specify it when calling Load!
    //This is used for default saving and loading so you won't have to specify the filename/path every time.
    //Putting just a filename without a folder will put it inside the executing file's directory.
    public override string FileName { get; set; } = "TheDefaultFilename.extension"; //for loading and saving.

    #region Settings

    public string SomeProperty { get; set; }
    public Dictionary<string, object> Dictionary { get; set; } = new Dictionary<string, object>();
    public int SomeNumberWithDefaultValue { get; set; } = 1;
    [JsonIgnore] public char ImIgnoredAndIWontBeSavedOrLoaded { get; set; }

    #endregion

    //Step 3: Override the parent's constructors
    public MySettings() { }
    public MySettings(string fileName) : base(fileName) { }
}
```

```csharp
//Step 4: Load
public MySettings Settings = JsonSettings.Load<MySettings>("config.json"); //relative path to executing file.
//or create a new empty one in memory:
public MySettings Settings = JsonSettings.Construct<MySettings>("config.json");

//Step 5: Introduce changes and save.
Settings.SomeProperty = "ok";
Settings.Save();
```

> [!NOTE]
> `Load<T>` reads the file if it exists, otherwise it creates one from the type's default values.
> `Construct<T>` only builds the in-memory object and never touches disk until you `Save()`.

## Dynamic settings

The dynamic [`SettingsBag`](dynamic-settings-bag.md):

- Automatically creates new keys.
- Accepts any type Json.NET can serialize.
- Returns [`ValueType`s](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/value-types)
  as `Nullable<T>`, so if a key doesn't exist a `null` is returned.

```csharp
//Step 1: Just load it, it'll be created if it doesn't exist.
public SettingsBag Settings = JsonSettings.Load<SettingsBag>("config.json");
//Step 2: use!
Settings["key"]  = "dat value tho";
Settings["key2"] = 123;
dynamic dyn = Settings.AsDynamic();
if ((int?)dyn.key2 == 123)
    Console.WriteLine("explode");
dyn.Save(); /* or */ Settings.Save();
```

## Encrypted settings

- Uses AES / Rijndael.
- Can be applied to any settings class, because it is a [module](modulation-api.md).

```csharp
MySettings Settings  = JsonSettings.Load<MySettings>("config.json", q => q.WithEncryption("mysecretpassword"));
SettingsBag Settings = JsonSettings.Load<SettingsBag>("config.json", q => q.WithEncryption("mysecretpassword"));
//or
MySettings Settings = JsonSettings.Configure<MySettings>("config.json")
                     .WithEncryption("mysecretpassword")
               //or: .WithModule<MySettings, RijndaelModule>("pass");
                     .LoadNow();

SettingsBag Settings = JsonSettings.Configure<SettingsBag>("config.json")
                     .WithEncryption("mysecretpassword")
               //or: .WithModule<SettingsBag, RijndaelModule>("pass");
                     .LoadNow();
```

See [Encryption](encryption.md) for password fetchers and combining with Base64.

## Hardcoded settings with autosave

- Automatic save occurs when changes are detected on virtual properties.
- All properties have to be `virtual`.
- Requires the `Nucs.JsonSettings.Autosave` package (which uses `Castle.Core`).

```csharp
Settings x  = JsonSettings.Load<Settings>().EnableAutosave(); //call after loading
//or:
ISettings x = JsonSettings.Load<Settings>().EnableIAutosave<Settings, ISettings>(); //Settings implements interface ISettings

x.Property = "value"; //Saved!
```

## Dynamic settings with autosave

- Automatic save occurs when changes are detected.
- Note: `SettingsBag` has its own implementation of `EnableAutosave()`.

```csharp
//Step 1:
SettingsBag Settings = JsonSettings.Load<SettingsBag>("config.json").EnableAutosave(); //call after loading
//Step 2:
Settings.AsDynamic().key = "wow"; //Saved!
Settings["key"] = "wow two";      //Saved!
```

See [Autosave](autosave.md) for the full picture, including WPF `INotifyPropertyChanged` support.

## Loading with configuration

Every `Load<T>` overload accepts an optional configuration lambda, and `Configure<T>(...).LoadNow()`
is the equivalent explicit form. The following are all real, tested usages:

```csharp
// Most basic
var o = JsonSettings.Load<CasualExampleSettings>(fileName);

// Configure fluently, then load
var o = JsonSettings.Configure<CasualExampleSettings>(fileName)
                    .WithBase64()
                    .WithEncryption("SuperPassword")
                    .LoadNow();

// Same thing, expressed through Load's configuration lambda
var o = JsonSettings.Load<CasualExampleSettings>(fileName,
            s => s.WithBase64().WithEncryption("SuperPassword"));

// Pull the password from one of the settings' own properties, and pass constructor args
var o = JsonSettings.Load<CasualExampleSettings>(fileName,
            s => s.WithBase64().WithEncryption(set => set.SomeProperty),
            new object[] { "SuperPassword" });
```

> [!TIP]
> Anything you can do with `Load<T>(file, configure)` you can also do with
> `Configure<T>(file).…​.LoadNow()`. Use whichever reads better at the call site.
