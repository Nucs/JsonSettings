# Encryption

Encryption is applied as a [module](modulation-api.md), so it works with any settings class,
hardcoded or [dynamic](dynamic-settings-bag.md). The serialized JSON is encoded to UTF-8 bytes,
encrypted with AES-256 (Rijndael), and then written as a Base64 string. Base64 is used so the result
is easy to copy around as text.

Special thanks to [Rijndael256](https://github.com/2Toad/Rijndael256) for their AES implementation.

## Attaching it

The `WithEncryption` fluent extension attaches a `RijndaelModule`. The simplest form takes a password
string:

```csharp
using Nucs.JsonSettings;
using Nucs.JsonSettings.Fluent;

var settings = JsonSettings.Load<MySettings>("config.json", q => q.WithEncryption("mysecretpassword"));
// or, explicitly:
var settings = JsonSettings.Configure<MySettings>("config.json")
                           .WithEncryption("mysecretpassword")
                     //or: .WithModule<RijndaelModule>("pass");
                           .LoadNow();
```

## Password sources

`WithEncryption` has overloads for every practical way of supplying a key. The password can be a
constant, or a fetcher that is invoked when needed &mdash; and the fetcher can read from the settings
object itself:

```csharp
// A plain string or SecureString
q.WithEncryption("mysecretpassword");
q.WithEncryption(secureString);

// A getter/generator, evaluated lazily
q.WithEncryption(() => GetPasswordFromVault());
q.WithEncryption(() => GetSecureStringFromVault());

// Derive the password from a property on the settings object being loaded
q.WithEncryption(set => set.SomeProperty);
```

The property-fetcher form is handy when part of your settings (loaded via a constructor argument, for
example) is itself the key:

```csharp
// SomeProperty is supplied as a constructor argument and then used as the password
var o = JsonSettings.Load<CasualExampleSettings>(fileName,
            s => s.WithEncryption(set => set.SomeProperty),
            new object[] { "SuperPassword" });
```

## Combining with Base64

`WithBase64()` attaches a `Base64Module`. Because modules order themselves correctly on the
encrypt/decrypt pipeline (see the [Modulation API](modulation-api.md)), you can stack them:

```csharp
var o = JsonSettings.Configure<CasualExampleSettings>(fileName)
                    .WithBase64()
                    .WithEncryption("SuperPassword")
                    .LoadNow();
```

## Wrong passwords and file format

- A wrong password is reported as a wrong password. Decryption verifies the padding **and** checks
  that the decrypted payload is valid UTF-8, so a bad key surfaces as a decryption failure rather
  than as a misleading "corrupt file" JSON error.
- The on-disk format is stable. Files written by earlier versions stay readable; the cipher and key
  derivation are unchanged.

> [!WARNING]
> The wrong-password check is a **diagnostic**, not an integrity guarantee. Encryption protects
> confidentiality of the file's contents; it does not authenticate them.
