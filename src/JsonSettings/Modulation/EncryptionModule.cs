using System;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Nucs.JsonSettings.Modulation.Encryption;

namespace Nucs.JsonSettings.Modulation {
    /// <summary>
    ///     Encrypts the serialized configuration with a symmetric algorithm from the .NET base class
    ///     library (<c>System.Security.Cryptography</c>). The algorithm is selected with
    ///     <see cref="Algorithm"/>; the default, <see cref="EncryptionAlgorithm.AesCbc"/>, is
    ///     byte-for-byte compatible with every file this library has ever written.
    /// </summary>
    /// <remarks>
    ///     The secret can be supplied three ways:
    ///     <list type="bullet">
    ///         <item>a text password (<see cref="string"/>/<see cref="SecureString"/>), stored as a
    ///             <see cref="SecureString"/> and stretched into a key with PBKDF2;</item>
    ///         <item>a binary password (<see cref="byte"/>[], via <see cref="EncryptionModule(byte[])"/>),
    ///             stretched with the same PBKDF2 construction - a distinct credential from the text
    ///             password with the same bytes;</item>
    ///         <item>a raw key (<see cref="byte"/>[], via <see cref="FromRawKey(byte[])"/>), used
    ///             verbatim with no derivation - its length must match the chosen algorithm.</item>
    ///     </list>
    ///     Exactly one source is active per module. There is no algorithm or key-size marker in the
    ///     file, so decrypting requires configuring the same <see cref="Algorithm"/>, <see cref="KeySize"/>
    ///     and secret the file was written with - exactly as the password has always had to match.
    /// </remarks>
    public class EncryptionModule : Module {
        public static readonly SecureString EmptyString = "".ToSecureString();

        private Func<SecureString>? _fetcher;

        //Set instead of _fetcher when the credential is binary. Exactly one of the three sources is
        //ever non-null; ResolveKey applies them in the order rawKey > passwordBytes > text password.
        private readonly Func<byte[]>? _passwordBytesFetcher;
        private readonly Func<byte[]>? _rawKeyFetcher;

        /// <summary>
        ///     The AES key size for the AES-based algorithms, by default <see cref="KeySize.Aes256"/>.
        ///     Ignored by <see cref="EncryptionAlgorithm.ChaCha20Poly1305"/>, which is always 256-bit.
        /// </summary>
        public virtual KeySize KeySize { get; set; } = KeySize.Aes256;

        /// <summary>
        ///     The symmetric algorithm used, by default <see cref="EncryptionAlgorithm.AesCbc"/> - the
        ///     historical, on-disk-compatible format.
        /// </summary>
        public virtual EncryptionAlgorithm Algorithm { get; set; } = EncryptionAlgorithm.AesCbc;

        /// <summary>
        ///     The password passed during constructor stored as a <see cref="SecureString"/> in memory.
        /// </summary>
        public virtual SecureString Password {
            get => _fetcher?.Invoke() ?? EmptyString;
            set { _fetcher = () => value; }
        }

        public EncryptionModule(string password) : this(password?.ToSecureString()) { }

        public EncryptionModule(SecureString password) : this(() => password) { }

        public EncryptionModule(Func<string> passwordFetcher) : this(() => passwordFetcher?.Invoke()?.ToSecureString()) { }

        public EncryptionModule(Func<SecureString> passwordFetcher) {
            _fetcher = () => {
                var ret = passwordFetcher() ?? EmptyString;
                if (!ret.IsReadOnly())
                    ret.MakeReadOnly();
                return ret;
            };
        }

        /// <summary>
        ///     A binary password. Run through the same PBKDF2 derivation as a text password - stretched
        ///     and salted - but a DIFFERENT credential from the text password whose UTF-8 bytes happen
        ///     to match.
        /// </summary>
        public EncryptionModule(byte[] password)
            : this(CloneFetcher(password ?? throw new ArgumentNullException(nameof(password))), rawKey: false) { }

        /// <summary>
        ///     A binary password resolved on demand. See <see cref="EncryptionModule(byte[])"/>.
        /// </summary>
        public EncryptionModule(Func<byte[]> passwordFetcher)
            : this(passwordFetcher ?? throw new ArgumentNullException(nameof(passwordFetcher)), rawKey: false) { }

        //Shared target for the two binary constructions. protected rather than private so the
        //backward-compatibility RijndaelModule shim can forward its own raw-key factory here; a
        //second parameter rather than a public byte[] overload for the key, because that signature is
        //already the binary PASSWORD - the raw key is reached through FromRawKey so intent is explicit.
        protected EncryptionModule(Func<byte[]> fetcher, bool rawKey) {
            if (rawKey)
                _rawKeyFetcher = fetcher;
            else
                _passwordBytesFetcher = fetcher;
        }

        /// <summary>
        ///     Builds a module that uses <paramref name="key"/> verbatim as the key, with no key
        ///     derivation. For the default AES algorithms the key must be 16, 24 or 32 bytes; other
        ///     algorithms validate their own length when the module runs.
        /// </summary>
        public static EncryptionModule FromRawKey(byte[] key) {
            if (key is null) throw new ArgumentNullException(nameof(key));
            //Eager AES validation so the common misuse still surfaces at configuration time, as it did
            //before. Non-AES algorithms re-validate against their own length at resolve time.
            AesKeyLengths.Validate(key);
            return new EncryptionModule(CloneFetcher(key), rawKey: true);
        }

        /// <summary>
        ///     Builds a module that uses the key returned by <paramref name="keyFetcher"/> verbatim,
        ///     with no key derivation. The key length must match the chosen algorithm each time it is
        ///     resolved.
        /// </summary>
        public static EncryptionModule FromRawKey(Func<byte[]> keyFetcher) {
            if (keyFetcher is null) throw new ArgumentNullException(nameof(keyFetcher));
            return new EncryptionModule(keyFetcher, rawKey: true);
        }

        //Copies the material once so a caller mutating (or clearing) its array afterwards cannot
        //change the key this module resolves. The text path gets this for free via SecureString.
        protected static Func<byte[]> CloneFetcher(byte[] material) {
            var copy = (byte[]) material.Clone();
            return () => copy;
        }

        public override void Attach(JsonSettings socket) {
            base.Attach(socket);
            socket.Encrypt += EncryptInternal;
            socket.Decrypt += DecryptInternal;
            //The wrong-password UTF-8 heuristic is hooked on AfterDecrypt, not carried out inside
            //DecryptInternal, so that it inspects the FINAL plaintext once every module has decrypted
            //rather than this module's still-encoded intermediate output. See AfterDecryptInternal.
            socket.AfterDecrypt += AfterDecryptInternal;
        }

        public override void Deattach(JsonSettings socket) {
            base.Deattach(socket);
            socket.Encrypt -= EncryptInternal;
            socket.Decrypt -= DecryptInternal;
            socket.AfterDecrypt -= AfterDecryptInternal;
        }

        protected virtual void EncryptInternal(JsonSettings sender, ref byte[] data) {
            var engine = CipherEngineFactory.Create(Algorithm, KeySize);
            data = engine.Encrypt(data, ResolveKey(engine));
        }

        protected virtual void DecryptInternal(JsonSettings sender, ref byte[] data) {
            var engine = CipherEngineFactory.Create(Algorithm, KeySize);

            //A file shorter than this algorithm's fixed overhead carries no ciphertext at all, so there
            //is nothing to decrypt and nothing that could decrypt to garbage. Pre-2.1.0 such a file
            //decrypted to an empty payload, which the empty-file branch in JsonSettings.Load then handed
            //to RecoveryModule - or reported as "The settings file is empty!" when no recovery was
            //configured. Treat it as empty here, restoring that path, rather than letting a length or
            //authentication error escape the recovery hook as it did in 2.1.0.
            if (data is null || data.Length < engine.MinimumLength) {
                data = new byte[0];
                return;
            }

            try {
                data = engine.Decrypt(data, ResolveKey(engine));
            } catch (CryptographicException inner) {
                throw new JsonSettingsException("Password appears to be invalid.", inner);
            }
        }

        /// <summary>
        ///     Resolves the key for <paramref name="engine"/>: a raw key verbatim (validated for the
        ///     engine), a PBKDF2-stretched binary password, or a PBKDF2-stretched text password.
        /// </summary>
        private byte[] ResolveKey(CipherEngine engine) {
            if (_rawKeyFetcher != null) {
                var key = _rawKeyFetcher() ?? throw new ArgumentNullException(nameof(_rawKeyFetcher), "The raw-key fetcher returned null.");
                engine.ValidateRawKey(key);
                return key;
            }

            if (_passwordBytesFetcher != null)
                return KeyDerivation.FromBytes(_passwordBytesFetcher() ?? Array.Empty<byte>(), engine.KeyBytes);

            return KeyDerivation.FromText(Password.ToRawString(), engine.KeyBytes);
        }

        /// <summary>
        ///     Wrong-password heuristic for the unauthenticated <see cref="EncryptionAlgorithm.AesCbc"/>
        ///     path. Runs once the whole decrypt chain has completed, so <paramref name="data"/> is the
        ///     final plaintext this library is about to read as JSON.
        /// </summary>
        /// <remarks>
        ///     WHY ONLY FOR AES-CBC. Every other algorithm authenticates during
        ///     <see cref="DecryptInternal"/> (a GCM/CCM/ChaCha20 tag, or the HMAC of AES-CBC-HMAC), so a
        ///     wrong key or a tampered file already failed there with a real integrity error and never
        ///     reaches here. AES-CBC alone cannot tell a wrong key from a right one: CBC decryption with
        ///     the wrong key yields random bytes, and PKCS7 padding validation accepts those by chance
        ///     roughly once in 256 attempts. When it does, decryption "succeeds", the garbage reaches
        ///     the JSON parser, and the user is told the file is corrupt rather than that the password
        ///     was wrong. Checking the final plaintext is valid UTF-8 is a diagnostic for that case.
        ///
        ///     WHY AFTER THE CHAIN AND NOT INSIDE <see cref="DecryptInternal"/>. The test rests on "what
        ///     this library encrypts is always UTF-8 JSON", which holds only for the FINAL plaintext. A
        ///     caller may attach another module inside the encryption layer - a compressor, an encoder -
        ///     and then this module's own output is that module's still-encoded input, with no reason to
        ///     be UTF-8. Deferring to AfterDecrypt inspects the plaintext no matter how many layers
        ///     produced it.
        ///
        ///     This is a diagnostic improvement for AES-CBC, not an integrity guarantee - the
        ///     authenticated algorithms are what provide that.
        /// </remarks>
        protected virtual void AfterDecryptInternal(JsonSettings sender, ref byte[] data) {
            if (CipherEngineFactory.IsAuthenticated(Algorithm))
                return;

            if (!IsValidUtf8(data))
                throw new JsonSettingsException("Password appears to be invalid.");
        }

        /// <summary>
        ///     True when <paramref name="data"/> decodes as UTF-8 without invalid sequences.
        /// </summary>
        private static bool IsValidUtf8(byte[] data) {
            if (data is null || data.Length == 0)
                return true;

            try {
                //GetCharCount rather than GetString: it performs the same validation without
                //allocating a string that is immediately discarded.
                new UTF8Encoding(false, true).GetCharCount(data);
                return true;
            } catch (ArgumentException) {
                //DecoderFallbackException derives from ArgumentException.
                return false;
            }
        }
    }
}
