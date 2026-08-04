# Converters

Defining converters (or changing serialization settings globally) can be done by adding a converter to
the static `JsonSettings.SerializationSettings`:

```csharp
//call during app startup
JsonSettings.SerializationSettings.Converters.Add(new Newtonsoft.Json.Converters.VersionConverter());
```

Alternatively, per-object settings can be applied by setting or inheriting the
`OverrideSerializerSettings` property &mdash; but be sure to also specify the default configuration so
that `JsonSettings`' behavior stays consistent (see
[Changing JsonSerializerSettings](serialization-settings.md)).

## JsonConverterAttribute

By far the easiest way to specify a converter is a `JsonConverterAttribute` on the property; Json.NET
does the rest:

```csharp
[JsonConverter(typeof(ExchangeConverter))]
public ExchangeType Exchange { get; set; }
```

`JsonConverterAttribute` can also be specified on an **interface** property, as `IVersionable` does,
and it applies to any class inheriting the interface. This is the best approach for libraries: by
specifying the attribute, no matter what `JsonSerializerSettings` the consumer provides, Json.NET
always serializes the property with the given converter.

```csharp
public interface IVersionable {
    [JsonConverter(typeof(Newtonsoft.Json.Converters.VersionConverter))]
    public Version Version { get; set; }
}
```
