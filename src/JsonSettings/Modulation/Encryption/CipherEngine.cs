using System;
using System.IO;
using System.Security.Cryptography;

namespace Nucs.JsonSettings.Modulation.Encryption {
    /// <summary>
    ///     A symmetric cipher, built entirely on <c>System.Security.Cryptography</c>, that turns
    ///     a plaintext byte[] into the exact bytes written to disk and back. One concrete engine per
    ///     <see cref="EncryptionAlgorithm"/>. An engine carries only its configured <see cref="KeySize"/>
    ///     and holds no key material of its own; the module passes the resolved key into every call.
    /// </summary>
    internal abstract class CipherEngine {
        /// <summary>
        ///     True when <see cref="Decrypt"/> verifies an authentication tag, so a wrong key or a
        ///     tampered file fails with a real integrity error. False only for <see cref="AesCbcEngine"/>.
        /// </summary>
        internal abstract bool IsAuthenticated { get; }

        /// <summary>How many key bytes this engine needs for its configured key size.</summary>
        internal abstract int KeyBytes { get; }

        /// <summary>
        ///     The smallest input <see cref="Decrypt"/> could possibly consume (the fixed overhead: IV
        ///     and/or nonce and/or tag). Files shorter than this carry no ciphertext at all and the
        ///     module treats them as an empty payload rather than a wrong password.
        /// </summary>
        internal abstract int MinimumLength { get; }

        /// <summary>Throws if <paramref name="key"/> is not a valid verbatim key length for this engine.</summary>
        internal abstract void ValidateRawKey(byte[] key);

        internal abstract byte[] Encrypt(byte[] plaintext, byte[] key);

        /// <summary>Decrypts, throwing <see cref="CryptographicException"/> on a wrong key / failed authentication.</summary>
        internal abstract byte[] Decrypt(byte[] ciphertext, byte[] key);

        protected static byte[] RandomBytes(int size) {
            var bytes = new byte[size];
            CryptoRandom.Fill(bytes);
            return bytes;
        }

        protected static byte[] Concat(byte[] a, byte[] b, byte[] c) {
            var result = new byte[a.Length + b.Length + c.Length];
            Buffer.BlockCopy(a, 0, result, 0, a.Length);
            Buffer.BlockCopy(b, 0, result, a.Length, b.Length);
            Buffer.BlockCopy(c, 0, result, a.Length + b.Length, c.Length);
            return result;
        }
    }

    /// <summary>Shared, thread-safe cryptographic RNG for IVs and nonces.</summary>
    internal static class CryptoRandom {
        private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

        internal static void Fill(byte[] buffer) {
            Rng.GetBytes(buffer);
        }
    }

    /// <summary>Validation for a verbatim AES key: 16, 24 or 32 bytes.</summary>
    internal static class AesKeyLengths {
        internal static void Validate(byte[] key) {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (key.Length != 16 && key.Length != 24 && key.Length != 32)
                throw new ArgumentException($"A raw AES key must be 16, 24 or 32 bytes (AES-128/192/256); got {key.Length}.", nameof(key));
        }
    }

    /// <summary>
    ///     AES-CBC with PKCS7 padding, IV prepended. The default and the historical on-disk format:
    ///     <c>IV(16) || ciphertext</c>. Unauthenticated - the module pairs it with a UTF-8 plaintext
    ///     heuristic to guess at a wrong password.
    /// </summary>
    internal sealed class AesCbcEngine : CipherEngine {
        internal const int IvSize = 16;
        private readonly KeySize _keySize;

        internal AesCbcEngine(KeySize keySize) {
            _keySize = keySize;
        }

        internal override bool IsAuthenticated => false;
        internal override int KeyBytes => (int) _keySize / 8;
        internal override int MinimumLength => IvSize; // matches the legacy InitializationVectorSize guard
        internal override void ValidateRawKey(byte[] key) => AesKeyLengths.Validate(key);

        internal override byte[] Encrypt(byte[] plaintext, byte[] key) {
            var iv = RandomBytes(IvSize);
            using (var aes = Aes.Create()) {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (var encryptor = aes.CreateEncryptor(key, iv))
                using (var ms = new MemoryStream()) {
                    ms.Write(iv, 0, iv.Length);
                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write)) {
                        cs.Write(plaintext, 0, plaintext.Length);
                        cs.FlushFinalBlock();
                    }

                    return ms.ToArray();
                }
            }
        }

        internal override byte[] Decrypt(byte[] ciphertext, byte[] key) {
            var iv = new byte[IvSize];
            Buffer.BlockCopy(ciphertext, 0, iv, 0, IvSize);
            var body = new byte[ciphertext.Length - IvSize];
            Buffer.BlockCopy(ciphertext, IvSize, body, 0, body.Length);

            using (var aes = Aes.Create()) {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (var decryptor = aes.CreateDecryptor(key, iv))
                using (var ms = new MemoryStream(body))
                using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                using (var output = new MemoryStream()) {
                    cs.CopyTo(output);
                    return output.ToArray();
                }
            }
        }
    }

    /// <summary>
    ///     AES-CBC then HMAC-SHA256 over the whole IV+ciphertext (Encrypt-then-MAC). Layout:
    ///     <c>IV(16) || ciphertext || HMAC(32)</c>. The key is an AES key followed by a 32-byte MAC key.
    ///     Authenticated, and available on every target framework (unlike the AEAD engines).
    /// </summary>
    internal sealed class AesCbcHmacEngine : CipherEngine {
        internal const int MacSize = 32; // HMAC-SHA256
        private readonly KeySize _keySize;

        internal AesCbcHmacEngine(KeySize keySize) {
            _keySize = keySize;
        }

        internal override bool IsAuthenticated => true;
        internal override int KeyBytes => (int) _keySize / 8 + MacSize;
        internal override int MinimumLength => AesCbcEngine.IvSize + MacSize;

        internal override void ValidateRawKey(byte[] key) {
            if (key is null) throw new ArgumentNullException(nameof(key));
            var cipherLen = key.Length - MacSize;
            if (cipherLen != 16 && cipherLen != 24 && cipherLen != 32)
                throw new ArgumentException($"A raw AES-CBC-HMAC key must be a 16/24/32-byte AES key followed by a {MacSize}-byte HMAC key (48, 56 or 64 bytes total); got {key.Length}.", nameof(key));
        }

        private static void Split(byte[] key, out byte[] cipherKey, out byte[] macKey) {
            var cipherLen = key.Length - MacSize;
            cipherKey = new byte[cipherLen];
            macKey = new byte[MacSize];
            Buffer.BlockCopy(key, 0, cipherKey, 0, cipherLen);
            Buffer.BlockCopy(key, cipherLen, macKey, 0, MacSize);
        }

        internal override byte[] Encrypt(byte[] plaintext, byte[] key) {
            Split(key, out var cipherKey, out var macKey);
            var ivAndCipher = new AesCbcEngine(_keySize).Encrypt(plaintext, cipherKey);
            byte[] mac;
            using (var hmac = new HMACSHA256(macKey))
                mac = hmac.ComputeHash(ivAndCipher);

            var result = new byte[ivAndCipher.Length + mac.Length];
            Buffer.BlockCopy(ivAndCipher, 0, result, 0, ivAndCipher.Length);
            Buffer.BlockCopy(mac, 0, result, ivAndCipher.Length, mac.Length);
            return result;
        }

        internal override byte[] Decrypt(byte[] ciphertext, byte[] key) {
            Split(key, out var cipherKey, out var macKey);
            if (ciphertext.Length < MinimumLength)
                throw new CryptographicException("Authenticated ciphertext is shorter than its own overhead.");

            var bodyLen = ciphertext.Length - MacSize;
            var body = new byte[bodyLen];
            Buffer.BlockCopy(ciphertext, 0, body, 0, bodyLen);
            var mac = new byte[MacSize];
            Buffer.BlockCopy(ciphertext, bodyLen, mac, 0, MacSize);

            byte[] expected;
            using (var hmac = new HMACSHA256(macKey))
                expected = hmac.ComputeHash(body);

            if (!FixedTimeEquals(expected, mac))
                throw new CryptographicException("Authentication failed: the file was tampered with or the key is wrong.");

            return new AesCbcEngine(_keySize).Decrypt(body, cipherKey);
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b) {
#if NET6_0_OR_GREATER
            return CryptographicOperations.FixedTimeEquals(a, b);
#else
            if (a.Length != b.Length)
                return false;
            var diff = 0;
            for (var i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
#endif
        }
    }

#if NET6_0_OR_GREATER
    /// <summary>Common shape for the BCL AEAD ciphers: <c>nonce(12) || ciphertext || tag(16)</c>.</summary>
    internal abstract class AeadEngine : CipherEngine {
        internal const int NonceSize = 12;
        internal const int TagSize = 16;

        internal sealed override bool IsAuthenticated => true;
        internal sealed override int MinimumLength => NonceSize + TagSize;

        protected abstract void EncryptCore(byte[] nonce, byte[] plaintext, byte[] ciphertext, byte[] tag, byte[] key);
        protected abstract void DecryptCore(byte[] nonce, byte[] ciphertext, byte[] tag, byte[] plaintext, byte[] key);

        internal sealed override byte[] Encrypt(byte[] plaintext, byte[] key) {
            var nonce = RandomBytes(NonceSize);
            var cipher = new byte[plaintext.Length];
            var tag = new byte[TagSize];
            EncryptCore(nonce, plaintext, cipher, tag, key);
            return Concat(nonce, cipher, tag);
        }

        internal sealed override byte[] Decrypt(byte[] ciphertext, byte[] key) {
            if (ciphertext.Length < MinimumLength)
                throw new CryptographicException("Authenticated ciphertext is shorter than its own overhead.");

            var nonce = new byte[NonceSize];
            var cipher = new byte[ciphertext.Length - NonceSize - TagSize];
            var tag = new byte[TagSize];
            Buffer.BlockCopy(ciphertext, 0, nonce, 0, NonceSize);
            Buffer.BlockCopy(ciphertext, NonceSize, cipher, 0, cipher.Length);
            Buffer.BlockCopy(ciphertext, NonceSize + cipher.Length, tag, 0, TagSize);

            var plaintext = new byte[cipher.Length];
            DecryptCore(nonce, cipher, tag, plaintext, key);
            return plaintext;
        }
    }

    /// <summary>AES-GCM (AEAD). Key size selects the AES variant.</summary>
    internal sealed class AesGcmEngine : AeadEngine {
        private readonly KeySize _keySize;

        internal AesGcmEngine(KeySize keySize) {
            _keySize = keySize;
        }

        internal override int KeyBytes => (int) _keySize / 8;
        internal override void ValidateRawKey(byte[] key) => AesKeyLengths.Validate(key);

        protected override void EncryptCore(byte[] nonce, byte[] plaintext, byte[] ciphertext, byte[] tag, byte[] key) {
#if NET8_0_OR_GREATER
            using (var gcm = new AesGcm(key, TagSize))
#else
            using (var gcm = new AesGcm(key))
#endif
                gcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        protected override void DecryptCore(byte[] nonce, byte[] ciphertext, byte[] tag, byte[] plaintext, byte[] key) {
#if NET8_0_OR_GREATER
            using (var gcm = new AesGcm(key, TagSize))
#else
            using (var gcm = new AesGcm(key))
#endif
                gcm.Decrypt(nonce, ciphertext, tag, plaintext);
        }
    }

    /// <summary>AES-CCM (AEAD). Key size selects the AES variant.</summary>
    internal sealed class AesCcmEngine : AeadEngine {
        private readonly KeySize _keySize;

        internal AesCcmEngine(KeySize keySize) {
            _keySize = keySize;
        }

        internal override int KeyBytes => (int) _keySize / 8;
        internal override void ValidateRawKey(byte[] key) => AesKeyLengths.Validate(key);

        protected override void EncryptCore(byte[] nonce, byte[] plaintext, byte[] ciphertext, byte[] tag, byte[] key) {
            using (var ccm = new AesCcm(key))
                ccm.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        protected override void DecryptCore(byte[] nonce, byte[] ciphertext, byte[] tag, byte[] plaintext, byte[] key) {
            using (var ccm = new AesCcm(key))
                ccm.Decrypt(nonce, ciphertext, tag, plaintext);
        }
    }

    /// <summary>ChaCha20-Poly1305 (AEAD). Fixed 256-bit key regardless of <see cref="KeySize"/>.</summary>
    internal sealed class ChaCha20Poly1305Engine : AeadEngine {
        private const int KeyLength = 32;

        internal override int KeyBytes => KeyLength;

        internal override void ValidateRawKey(byte[] key) {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (key.Length != KeyLength)
                throw new ArgumentException($"A raw ChaCha20-Poly1305 key must be {KeyLength} bytes; got {key.Length}.", nameof(key));
        }

        protected override void EncryptCore(byte[] nonce, byte[] plaintext, byte[] ciphertext, byte[] tag, byte[] key) {
            using (var chacha = new ChaCha20Poly1305(key))
                chacha.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        protected override void DecryptCore(byte[] nonce, byte[] ciphertext, byte[] tag, byte[] plaintext, byte[] key) {
            using (var chacha = new ChaCha20Poly1305(key))
                chacha.Decrypt(nonce, ciphertext, tag, plaintext);
        }
    }
#endif

    /// <summary>Builds the <see cref="CipherEngine"/> for a chosen algorithm and key size.</summary>
    internal static class CipherEngineFactory {
        internal static CipherEngine Create(EncryptionAlgorithm algorithm, KeySize keySize) {
            switch (algorithm) {
                case EncryptionAlgorithm.AesCbc: return new AesCbcEngine(keySize);
                case EncryptionAlgorithm.AesCbcHmac: return new AesCbcHmacEngine(keySize);
#if NET6_0_OR_GREATER
                case EncryptionAlgorithm.AesGcm: return new AesGcmEngine(keySize);
                case EncryptionAlgorithm.AesCcm: return new AesCcmEngine(keySize);
                case EncryptionAlgorithm.ChaCha20Poly1305: return new ChaCha20Poly1305Engine();
#endif
                default:
                    throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported encryption algorithm on this target framework.");
            }
        }

        /// <summary>
        ///     Whether an algorithm authenticates on decrypt. Only <see cref="EncryptionAlgorithm.AesCbc"/>
        ///     does not, which is the single case that still needs the UTF-8 wrong-password heuristic.
        /// </summary>
        internal static bool IsAuthenticated(EncryptionAlgorithm algorithm) {
            return algorithm != EncryptionAlgorithm.AesCbc;
        }
    }
}
