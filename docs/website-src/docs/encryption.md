# Encryption

Encryption is applied as a [module](modulation-api.md), so it works with any settings class,
hardcoded or [dynamic](dynamic-settings-bag.md). The serialized JSON is encoded to UTF-8 bytes and
encrypted with a symmetric algorithm from the .NET base class library
(`System.Security.Cryptography`) &mdash; no third-party cryptography is involved. By default the
algorithm is **AES-256-CBC**, which is byte-for-byte compatible with every file this library has
ever written.

## Attaching it

The `WithEncryption` fluent extension attaches an `EncryptionModule`. The simplest form takes a
password string:

```csharp
using Nucs.JsonSettings;

var settings = JsonSettings.Load<MySettings>("config.json", q => q.WithEncryption("mysecretpassword"));
// or, explicitly:
var settings = JsonSettings.Configure<MySettings>("config.json")
                           .WithEncryption("mysecretpassword")
                     //or: .WithModule<MySettings, EncryptionModule>("pass");
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

The secret can also be supplied as bytes. A `byte[]` **password** is stretched into the key with the
same PBKDF2 derivation as a text password; a **raw key** is used verbatim and must match the
algorithm's key length (16/24/32 bytes for AES, 32 for ChaCha20-Poly1305):

```csharp
// Binary password - PBKDF2-derived. NOTE: this is a DIFFERENT credential from the text password
// whose UTF-8 encoding equals these bytes, because the text derivation folds in the string's
// character length. Pick one form per file.
q.WithEncryption(passwordBytes);
q.WithEncryption(() => GetPasswordBytesFromVault());

// Raw key - used as-is, no derivation. You own the key's quality, so supply high-entropy
// material (e.g. RandomNumberGenerator.GetBytes(32)), not a low-entropy value.
q.WithEncryptionRawKey(key32);
q.WithEncryptionRawKey(() => GetKeyFromVault());
```

The property-fetcher form is handy when part of your settings (loaded via a constructor argument, for
example) is itself the key:

```csharp
// SomeProperty is supplied as a constructor argument and then used as the password
var o = JsonSettings.Load<CasualExampleSettings>(fileName,
            s => s.WithEncryption(set => set.SomeProperty),
            new object[] { "SuperPassword" });
```

## Choosing an algorithm

The default is AES-256-CBC. To pick another algorithm, pass an `EncryptionAlgorithm` (and optionally a
`KeySize`) to `WithEncryption`, `WithEncryption(byte[], ...)` or `WithEncryptionRawKey(byte[], ...)`:

```csharp
q.WithEncryption("password", EncryptionAlgorithm.AesGcm);
q.WithEncryption("password", EncryptionAlgorithm.AesCbc, KeySize.Aes128);
q.WithEncryptionRawKey(key32, EncryptionAlgorithm.ChaCha20Poly1305);
```

| `EncryptionAlgorithm` | Authenticated | Layout | Availability |
|---|---|---|---|
| `AesCbc` *(default)* | No (UTF-8 heuristic only) | `IV(16) ‖ ciphertext` | All targets |
| `AesCbcHmac` | Yes (HMAC-SHA256, Encrypt-then-MAC) | `IV(16) ‖ ciphertext ‖ tag(32)` | All targets |
| `AesGcm` | Yes (AEAD) | `nonce(12) ‖ ciphertext ‖ tag(16)` | .NET 6.0+ |
| `AesCcm` | Yes (AEAD) | `nonce(12) ‖ ciphertext ‖ tag(16)` | .NET 6.0+, OS support |
| `ChaCha20Poly1305` | Yes (AEAD) | `nonce(12) ‖ ciphertext ‖ tag(16)` | .NET 6.0+, OS support |

The AEAD algorithms only exist in the BCL on .NET 6.0 and later; when the library is used from
`netstandard2.0` or `net48` those enum members are not present, and `AesCbc`/`AesCbcHmac` are the
options. `ChaCha20Poly1305` and `AesCcm` additionally require OS support.

> [!IMPORTANT]
> There is no algorithm marker in the file. As with the password and key size, a file must be read
> back with the same `EncryptionAlgorithm` and `KeySize` it was written with. Only the default,
> `AesCbc`, is guaranteed to read files from older versions of this library.

## Combining with Base64

`WithBase64()` attaches a `Base64Module`. Because modules order themselves correctly on the
encrypt/decrypt pipeline (see the [Modulation API](modulation-api.md)), you can stack them:

```csharp
var o = JsonSettings.Configure<CasualExampleSettings>(fileName)
                    .WithBase64()
                    .WithEncryption("SuperPassword")
                    .LoadNow();
```

A file that is not valid base64 (truncated, or edited by hand) is treated as a damaged file, exactly
like a short encrypted one: it surfaces as a catchable `JsonSettingsException`, and a
[`RecoveryModule`](recovery.md) absorbs it &mdash; rather than a raw `FormatException` escaping the
decode stage ahead of the recovery hook.

## Wrong passwords, authentication and file format

- With the default `AesCbc`, a wrong password is reported as a wrong password. Decryption verifies the
  padding **and** checks that the decrypted payload is valid UTF-8, so a bad key surfaces as a
  decryption failure rather than as a misleading "corrupt file" JSON error. This is a **diagnostic**,
  not an integrity guarantee: AES-CBC does not authenticate its data.
- The authenticated algorithms (`AesCbcHmac`, `AesGcm`, `AesCcm`, `ChaCha20Poly1305`) verify an
  authentication tag when decrypting. A wrong key **or a tampered file** fails with a real integrity
  error, not a heuristic. Choose one of these if you need to detect modification of the file, not only
  keep its contents confidential.
- The on-disk format of the default is stable. Files written by earlier versions stay readable; the
  cipher, the IV layout and the PBKDF2-SHA1 key derivation are unchanged. The move onto
  `System.Security.Cryptography` did not change any bytes &mdash; it is verified against a ciphertext
  captured from a pre-migration build and against an independent, BCL-only reimplementation of the
  format.
