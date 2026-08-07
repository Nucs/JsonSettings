# Recovery

The `RecoveryModule` provides handling for a `JsonException` thrown while `JsonSettings` parses the
file during loading. On failure, one of the following `RecoveryAction` values decides what happens:

| `RecoveryAction` | Behavior |
|------------------|----------|
| `Throw` | Throw a `JsonSettingsRecoveryException` with the real exception as its inner exception. |
| `RenameAndLoadDefault` | Append the version and a collision counter to the faulty file's name, load defaults, and save them to disk. e.g. `myfile.json` versioned `1.0.0.5` is renamed to `myfile.1.0.0.5-0.json`, and a fresh default `myfile.json` is written. (A non-versioned settings class uses just the counter, e.g. `myfile.0.json`.) |
| `LoadDefault` | Load default settings without touching the existing file until the next save. |
| `LoadDefaultAndSave` | Load default settings and save them to disk immediately. |

All recovery properties and methods are suited for inheritance, so extending is easy.

## Attaching it

Use the `WithRecovery` fluent extension (from `Nucs.JsonSettings.Modulation.Recovery`):

```csharp
using Nucs.JsonSettings;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Modulation.Recovery;

public class RecoverableSettings : JsonSettings {
    public override string FileName { get; set; } = "somename.json";
    public virtual string AutoProperty { get; set; } = "Hi";

    public RecoverableSettings() { }
    public RecoverableSettings(string fileName) : base(fileName) { }
}
```

```csharp
var settings = JsonSettings.Configure<RecoverableSettings>("abouttofail.jsn")
                           .WithRecovery(RecoveryAction.RenameAndLoadDefault)
                           .LoadNow()
                           .EnableAutosave();

settings.AutoProperty = "Hello"; //Boom! saves.

// ...later the file on disk gets corrupted (edited by hand, truncated, etc.)

// Loading again does not throw: the corrupt file is renamed aside and defaults are loaded.
settings = JsonSettings.Configure<RecoverableSettings>("abouttofail.jsn")
                       .WithRecovery(RecoveryAction.RenameAndLoadDefault)
                       .LoadNow()
                       .EnableAutosave();

Console.WriteLine(settings.AutoProperty); // "Hi" — the default, because the corrupt file was
                                          // renamed to abouttofail.<version>.jsn and a fresh
                                          // default abouttofail.jsn was written.
```

> [!NOTE]
> Recovery handles **parse failures**. If instead you want to react to a **schema/version change**
> in a file that still parses, use [Versioning](versioning.md). The two compose: versioning runs on
> a successful parse, recovery catches the parse itself failing.
