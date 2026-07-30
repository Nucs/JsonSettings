# Versioning

`VersioningModule<T>` gives you the ability to enforce a specific version, so that when new changes
are introduced to your settings class (its scheme), a user-defined action can take place. Any of the
following `VersioningResultAction` values can be taken:

| `VersioningResultAction` | Behavior |
|--------------------------|----------|
| `DoNothing` | Keep the old version if Json.NET parsed it successfully. Otherwise [Recovery](recovery.md) handles the load failure. |
| `Throw` | Throw an `InvalidVersionException` on loading. |
| `RenameAndLoadDefault` | Append the version to the faulty file's name, load defaults, and save them to disk. e.g. `myfile.json` versioned `1.0.0.5` is renamed to `myfile.1.0.0.5.json`. |
| `LoadDefault` | Load default settings without touching the existing file until the next save. |
| `LoadDefaultAndSave` | Load default settings and save them to disk immediately. |

## Making a settings class versionable

The settings type must implement `IVersionable`, which contributes a `Version Version { get; set; }`
property:

```csharp
using System;
using Nucs.JsonSettings;
using Nucs.JsonSettings.Modulation;

public class VersioningSettings : JsonSettings, IVersionable {
    public override string FileName { get; set; } = "somename.jsn";
    public virtual Version Version { get; set; } = new Version(1, 0, 0, 6);
    public virtual string AutoProperty { get; set; } = "Hi";

    public VersioningSettings() { }
    public VersioningSettings(string fileName) : base(fileName) { }
}
```

## Enforcing a version

There are two ways to specify which version to enforce.

**1. Pass the version to `WithVersioning`:**

```csharp
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Modulation;

// Load version 1.0.0.6
var settings = JsonSettings.Configure<VersioningSettings>("versioning.jsn")
                           .WithVersioning("1.0.0.6", VersioningResultAction.RenameAndLoadDefault)
                           .LoadNow()
                           .EnableAutosave();

settings.AutoProperty = "Hello"; //Boom! saves.

// After some development you bump the enforced version to 1.0.0.7
settings = JsonSettings.Configure<VersioningSettings>("versioning.jsn")
                       .WithVersioning("1.0.0.7", VersioningResultAction.RenameAndLoadDefault)
                       .LoadNow()
                       .EnableAutosave();

Console.WriteLine(settings.AutoProperty); // "Hi"
// The versions mismatch, and the action is RenameAndLoadDefault, so the old file is preserved as
// versioning.1.0.0.6.jsn and a new default versioning.jsn is written.
```

**2. Add an `[EnforcedVersion]` attribute to the `Version` property:**

```csharp
[EnforcedVersion("1.0.0.0")]
public virtual Version Version { get; set; } = new Version(1, 0, 0, 0);
```

When dealing with inheritance / virtual overrides, the attribute of the lowest inherited class is
used. With the attribute in place you can use the `WithVersioning(invalidAction)` overload that takes
no explicit version.

## Policy

A comparison between versions is done by the **Policy**, a `VersioningPolicyHandler`
(`Func<Version, Version, bool>`) passed during construction of `VersioningModule<T>`. If none is
given it falls back to `VersioningModule.DefaultPolicy`, which you can also change globally. Each
`VersioningModule<T>` can be assigned its own policy.

By default the versions must match exactly:

```csharp
static bool DefaultEqualPolicy(Version version, Version expectedVersion) {
    return expectedVersion?.Equals(version) != false;
}
```

Pass your own policy as the optional last argument to `WithVersioning` to, for example, accept any
file whose version is less than or equal to the expected one.
