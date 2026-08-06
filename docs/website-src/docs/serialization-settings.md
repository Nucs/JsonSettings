# Changing JsonSerializerSettings

The default settings are defined on the static `JsonSettings.SerializationSettings`:

```csharp
public static JsonSerializerSettings SerializationSettings { get; set; } = new JsonSerializerSettings {
    Formatting = Formatting.Indented,
    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
    NullValueHandling = NullValueHandling.Include,
    ContractResolver = new FileNameIgnoreResolver(),
    TypeNameHandling = TypeNameHandling.Auto
};
```

Since 2.3.0 the default `FileNameIgnoreResolver` additionally marks every **writable collection
property** (a property, not a field, whose type is `IEnumerable` and not `string`) with
`ObjectCreationHandling.Replace`. Loading populates the *existing* instance, and Json.NET's default
(`Auto`) **reuses** a non-null collection and *appends* the file's items to it — so every reload
duplicated collection contents, a collection with non-empty defaults grew by one copy per
application start, and a recovery-to-defaults silently kept the stale pre-corruption items. With
`Replace` the deserializer assigns a fresh collection through the property's (woven) setter, and the
load pipeline [resyncs the `NotificationBinder`](notifications.md#reacting-to-notifications--autosave-on-nested-changes)
so the replacement stays bound. Get-only collections cannot be replaced and keep the append
semantics — prefer a settable property where reload contents must be exact. Supplying your own
`ContractResolver` (via either override point below) replaces this rule together with the resolver.

To alter the `JsonSerializerSettings`, it helps to understand how the library resolves which settings
to use during serialization/deserialization:

```csharp
/// <summary>
///     Returns configuration based on the following fallback: <br/>
///     settings ?? this.OverrideSerializerSettings ?? JsonSettings.SerializationSettings ?? JsonConvert.DefaultSettings?.Invoke()
///              ?? throw new JsonSerializationException("Unable to resolve JsonSerializerSettings to serialize this JsonSettings");
/// </summary>
/// <param name="settings">If passed a non-null, this is the settings intended to use, not any of the fallbacks.</param>
/// <exception cref="JsonSerializationException">When no valid configuration was found.</exception>
protected virtual JsonSerializerSettings ResolveConfiguration(JsonSerializerSettings? settings = null) {
    return settings
           ?? this.OverrideSerializerSettings
           ?? JsonSettings.SerializationSettings
           ?? JsonConvert.DefaultSettings?.Invoke()
           ?? throw new JsonSerializationException("Unable to resolve JsonSerializerSettings to serialize this JsonSettings");
}
```

The resolution order is:

1. **`settings` parameter** &mdash; an internal mechanism used when handling defaults. If passed a
   non-null value, it is used directly and none of the fallbacks apply.
2. **`this.OverrideSerializerSettings`** &mdash; a property on every class inheriting `JsonSettings`,
   allowing personalized settings per object. Both `OverrideSerializerSettings` and
   `ResolveConfiguration` are `virtual`, so you can redirect resolution wherever you see fit.
3. **`static JsonSettings.SerializationSettings`** &mdash; the default for all `JsonSettings` objects.
4. **`static JsonConvert.DefaultSettings`** &mdash; the default defined at the Json.NET level.

> [!TIP]
> If you set `OverrideSerializerSettings` on an object, it fully replaces the defaults for that
> object. Start from a copy of `JsonSettings.SerializationSettings` so the library's expected
> behavior (indented output, the file-name-ignoring resolver, and so on) stays consistent.
