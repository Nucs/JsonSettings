# Modulation API

Every optional behavior in the library &mdash; [encryption](encryption.md), Base64,
[versioning](versioning.md), [recovery](recovery.md) and [autosave](autosave.md) &mdash; is a
**module**. Modules attach to a per-object **socket** and subscribe to events on the load/save
pipeline.

## Key points

- All modules are stored inside `JsonSettings.Modulation` (a `ModuleSocket`).
- The `ModuleSocket` stores every module attached to that `JsonSettings` object.
- Every settings object gets its own module instances; nothing is shared between objects.
- Attaching modules is done via the fluent static extensions in
  [`FluentJsonSettings`](https://github.com/Nucs/JsonSettings/blob/master/src/JsonSettings/Fluent/FluentJsonSettings.cs)
  (`WithModule`, `WithEncryption`, `WithBase64`, `WithVersioning`, `WithRecovery`).
- All modules provided by the library have properties and methods suited for inheritance, so
  extending is easy.

```csharp
// Attach a library module by type, or your own instance:
JsonSettings.Configure<MySettings>("config.json")
            .WithModule<MySettings, MyModule>(/* constructor args */)
            .LoadNow();

JsonSettings.Configure<MySettings>("config.json")
            .WithModule(new MyModule())
            .LoadNow();
```

## Execution order

The events are intentionally many, to allow as much interception as possible. Handlers do not return
data; instead they receive a reference to the object that can be modified and is then used in the next
stage.

### Loading

```csharp
event BeforeLoadHandler       BeforeLoad(JsonSettings sender, ref string source); // the file that will be loaded
event DecryptHandler          Decrypt(JsonSettings sender, ref byte[] data);
event AfterDecryptHandler     AfterDecrypt(JsonSettings sender, ref byte[] data);
event BeforeDeserializeHandler BeforeDeserialize(JsonSettings sender, ref string data);
event BeforeRepopulateHandler BeforeRepopulate(JsonSettings sender);
event AfterRepopulateHandler  AfterRepopulate(JsonSettings sender, bool successfulPopulate);
event AfterDeserializeHandler AfterDeserialize(JsonSettings sender);
event AfterLoadHandler        AfterLoad(JsonSettings sender);
```

`BeforeRepopulate`/`AfterRepopulate` bracket the JSON populate itself and are the only
**per-populate** signal: they also fire for `LoadDefault()`, versioning/recovery reloads and direct
`LoadJson()` calls, where the rest of the loading events do not. `AfterRepopulate` fires from a
`finally` and reports `successfulPopulate: false` when the populate threw halfway (the recovery
path), in which case the object may hold a mix of old and file values &mdash; check the flag before
acting on loaded data. Do not call `Save()` between the two events: the instance is mid-rewrite.
The library itself rides this pair &mdash; the autosave module suppresses save-on-write during the
populate through it, and the `NotificationBinder` rebinds replaced collections after it.

And, in the case of a `JsonException` during `LoadJson`:

```csharp
// recovered marks whether recovery succeeded; handled prevents further modules from attempting to recover.
// If recovered is returned false, a JsonSettingsException is thrown with the original exception as inner.
event TryingRecoverHandler TryingRecover(JsonSettings sender, string fileName, JsonException? exception, ref bool recovered, ref bool handled);
event RecoveredHandler     Recovered(JsonSettings sender);
```

### Saving

```csharp
event BeforeSaveHandler      BeforeSave(JsonSettings sender, ref string destinition);
event BeforeSerializeHandler BeforeSerialize(JsonSettings sender);
event AfterSerializeHandler  AfterSerialize(JsonSettings sender, ref string data);
event EncryptHandler         Encrypt(JsonSettings sender, ref byte[] data);
event AfterEncryptHandler    AfterEncrypt(JsonSettings sender, ref byte[] data);
event AfterSaveHandler       AfterSave(JsonSettings sender, string destinition);
```

## Cryptography / encoding ordering

When a handler attaches to `Encrypt`, it is pushed to the **end** of the event queue &mdash; it
receives the data after everything attached before it. When a handler attaches to `Decrypt`, it is
pushed to the **beginning** of the queue. This way encryption/encoding and decryption/decoding
automatically run in the correct, mirrored order, so you can stack (for example)
[`WithBase64().WithEncryption(...)`](encryption.md) and have decode/decrypt unwind correctly.

## Writing your own module

Inherit `Module`, override `Attach`/`Deattach`, and subscribe/unsubscribe to the pipeline events. The
library's own `Base64Module` is a complete, minimal example:

```csharp
using System;
using Nucs.JsonSettings.Modulation;

/// <summary>Will convert text to base64, not pure json.</summary>
public class Base64Module : Module {
    public override void Attach(JsonSettings socket) {
        base.Attach(socket);
        socket.Encrypt += _Encrypt;
        socket.Decrypt += _Decrypt;
    }

    public override void Deattach(JsonSettings socket) {
        base.Deattach(socket);
        socket.Encrypt -= _Encrypt;
        socket.Decrypt -= _Decrypt;
    }

    protected virtual void _Encrypt(JsonSettings sender, ref byte[] data) {
        data = JsonSettings.Encoding.GetBytes(Convert.ToBase64String(data));
    }

    protected virtual void _Decrypt(JsonSettings sender, ref byte[] data) {
        data = Convert.FromBase64String(JsonSettings.Encoding.GetString(data));
    }
}
```

Attach it like any other module:

```csharp
var settings = JsonSettings.Configure<MySettings>("config.json")
                           .WithModule(new Base64Module())
                           .LoadNow();
```

> [!NOTE]
> `Module.Deattach` is called automatically on `Dispose`, and the socket is held through a
> `WeakReference<JsonSettings>` so a module never keeps a settings object alive.
