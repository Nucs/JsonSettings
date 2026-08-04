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
