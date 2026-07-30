using System;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Rijndael256;
using Rijndael = Rijndael256.Rijndael;

namespace Nucs.JsonSettings.Modulation {
    /// <summary>
    ///     This module encrypts the configuration with Rijndael Algorithm, aka AES256.
    /// </summary>
    /// <remarks>
    ///     This module uses an internal class to perform the encryption and is not publicly exposed.
    ///     <br></br>
    ///     The secret can be supplied three ways:
    ///     <list type="bullet">
    ///         <item>a text password (<see cref="string"/>/<see cref="SecureString"/>), stored as a
    ///             <see cref="SecureString"/> and stretched into a key with PBKDF2;</item>
    ///         <item>a binary password (<see cref="byte"/>[], via <see cref="RijndaelModule(byte[])"/>),
    ///             stretched with the same PBKDF2 construction - a distinct credential from the text
    ///             password with the same bytes;</item>
    ///         <item>a raw AES key (<see cref="byte"/>[], via <see cref="FromRawKey(byte[])"/>), used
    ///             verbatim with no derivation - it must be 16, 24 or 32 bytes.</item>
    ///     </list>
    ///     Exactly one source is active per module; the text path is unchanged and on-disk compatible
    ///     with every earlier version.
    /// </remarks>
    public class RijndaelModule : Module {
        public static readonly SecureString EmptyString = "".ToSecureString();

        private Func<SecureString>? _fetcher;

        //Set instead of _fetcher when the credential is binary. Exactly one of the three sources is
        //ever non-null; ResolveKey applies them in the order rawKey > passwordBytes > text password.
        private readonly Func<byte[]>? _passwordBytesFetcher;
        private readonly Func<byte[]>? _rawKeyFetcher;

        /// <summary>
        ///     The key-size for the AES encryption, by default <see cref="KeySize.Aes256"/>
        /// </summary>
        public virtual KeySize KeySize { get; set; } = KeySize.Aes256;

        /// <summary>
        ///     The password passed during constructor stored as a <see cref="SecureString"/> in memory.
        /// </summary>
        public virtual SecureString Password {
            get => _fetcher?.Invoke() ?? EmptyString;
            set { _fetcher = () => value; }
        }

        public RijndaelModule(string password) : this(password?.ToSecureString()) { }

        public RijndaelModule(SecureString password) : this(() => password) { }

        public RijndaelModule(Func<string> passwordFetcher) : this(() => passwordFetcher?.Invoke()?.ToSecureString()) { }

        public RijndaelModule(Func<SecureString> passwordFetcher) {
            _fetcher = () => {
                var ret = passwordFetcher() ?? EmptyString;
                if (!ret.IsReadOnly())
                    ret.MakeReadOnly();
                return ret;
            };
        }

        /// <summary>
        ///     A binary password. Run through the same PBKDF2 derivation as a text password
        ///     (see <see cref="Rijndael.GenerateKey(byte[],KeySize)"/>) - stretched and salted - but a
        ///     DIFFERENT credential from the text password whose UTF-8 bytes happen to match.
        /// </summary>
        public RijndaelModule(byte[] password)
            : this(CloneFetcher(password ?? throw new ArgumentNullException(nameof(password))), rawKey: false) { }

        /// <summary>
        ///     A binary password resolved on demand. See <see cref="RijndaelModule(byte[])"/>.
        /// </summary>
        public RijndaelModule(Func<byte[]> passwordFetcher)
            : this(passwordFetcher ?? throw new ArgumentNullException(nameof(passwordFetcher)), rawKey: false) { }

        //Shared target for the two binary constructions. A second parameter rather than a public
        //RijndaelModule(byte[]) overload for the key, because that signature is already the binary
        //PASSWORD - the raw key is reached through FromRawKey so intent is explicit at the call site.
        private RijndaelModule(Func<byte[]> fetcher, bool rawKey) {
            if (rawKey)
                _rawKeyFetcher = fetcher;
            else
                _passwordBytesFetcher = fetcher;
        }

        /// <summary>
        ///     Builds a module that uses <paramref name="key"/> verbatim as the AES key, with no key
        ///     derivation. The key must be 16, 24 or 32 bytes (AES-128/192/256).
        /// </summary>
        public static RijndaelModule FromRawKey(byte[] key) {
            if (key == null) throw new ArgumentNullException(nameof(key));
            ValidateRawKey(key);
            return new RijndaelModule(CloneFetcher(key), rawKey: true);
        }

        /// <summary>
        ///     Builds a module that uses the key returned by <paramref name="keyFetcher"/> verbatim,
        ///     with no key derivation. The key must be 16, 24 or 32 bytes each time it is resolved.
        /// </summary>
        public static RijndaelModule FromRawKey(Func<byte[]> keyFetcher) {
            if (keyFetcher == null) throw new ArgumentNullException(nameof(keyFetcher));
            return new RijndaelModule(keyFetcher, rawKey: true);
        }

        //Copies the material once so a caller mutating (or clearing) its array afterwards cannot
        //change the key this module resolves. The text path gets this for free via SecureString.
        private static Func<byte[]> CloneFetcher(byte[] material) {
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
            var iv = Rng.GenerateRandomBytes(Rijndael.InitializationVectorSize);
            var key = ResolveKey();
            data = key != null
                ? Rijndael.Encrypt(data, key, iv)
                : Rijndael.Encrypt(data, Password.ToRawString(), iv, KeySize);
        }

        protected virtual void DecryptInternal(JsonSettings sender, ref byte[] data) {
            //A file shorter than a single initialization vector carries no ciphertext at all, so
            //there is nothing to decrypt and nothing that could decrypt to garbage. Pre-2.1.0 such
            //a file decrypted to an empty payload, which the empty-file branch in JsonSettings.Load
            //then handed to RecoveryModule - or reported as "The settings file is empty!" when no
            //recovery was configured. 2.1.0 began raising EndOfStreamException from the IV read
            //instead, and that is raised from inside the decrypt stage, BEFORE Load consults the
            //recovery hook: a RecoveryModule the caller explicitly attached never saw the file, and
            //the exception - an IOException, not a JsonSettingsException - escaped every catch a
            //caller had placed around the load. Both are regressions against 2.0.x for the single
            //most common damaged-file case, a zero-byte file left by an interrupted save.
            //
            //Treat it as empty here, restoring the pre-2.1.0 path. This is NOT the silent-garbage
            //case the IV length check guards against: that needs a full IV plus ciphertext, which is
            //at least InitializationVectorSize bytes and never reaches this branch. A short-but-
            //nonempty file that does contain a complete IV still fails padding validation below,
            //exactly as before.
            if (data == null || data.Length < Rijndael.InitializationVectorSize) {
                data = new byte[0];
                return;
            }

            try {
                var key = ResolveKey();
                data = key != null
                    ? Rijndael.DecryptBytes(data, key)
                    : Rijndael.DecryptBytes(data, Password.ToRawString(), KeySize);
            } catch (CryptographicException inner) {
                throw new JsonSettingsException("Password appears to be invalid.", inner);
            }
        }

        /// <summary>
        ///     Resolves the AES key when this module was given a binary password or raw key material;
        ///     returns null when it holds a text password, for which the existing string path applies.
        /// </summary>
        protected virtual byte[]? ResolveKey() {
            if (_rawKeyFetcher != null) {
                var key = _rawKeyFetcher() ?? throw new ArgumentNullException(nameof(_rawKeyFetcher), "The raw-key fetcher returned null.");
                ValidateRawKey(key);
                return key;
            }

            if (_passwordBytesFetcher != null)
                return Rijndael.GenerateKey(_passwordBytesFetcher() ?? Array.Empty<byte>(), KeySize);

            return null;
        }

        private static void ValidateRawKey(byte[] key) {
            if (key.Length != 16 && key.Length != 24 && key.Length != 32)
                throw new ArgumentException($"A raw AES key must be 16, 24 or 32 bytes (AES-128/192/256); got {key.Length}.", nameof(key));
        }

        /// <summary>
        ///     Wrong-password heuristic. Runs once the whole decrypt chain has completed, so
        ///     <paramref name="data"/> is the final plaintext this library is about to read as JSON.
        /// </summary>
        /// <remarks>
        ///     WHY AFTER THE CHAIN AND NOT INSIDE <see cref="DecryptInternal"/>. The test below rests
        ///     on "what this library encrypts is always UTF-8 JSON, so a decryption that is not even
        ///     valid UTF-8 did not come from this password". That holds only for the FINAL plaintext.
        ///     A caller may attach another module inside the encryption layer - a compressor, an
        ///     encoder, anything that hooks Encrypt/Decrypt - and then this module's own output is
        ///     that module's still-encoded input, with no reason to be UTF-8. 2.1.0 ran the check on
        ///     that intermediate output and so rejected a CORRECT password whenever a non-UTF-8 module
        ///     sat inside the encryption (gzip, raw bytes, ...), a chain that round-tripped in 2.0.x.
        ///     Deferring to AfterDecrypt - which <see cref="JsonSettings"/>.Load raises once every
        ///     module has decrypted - inspects the plaintext no matter how many layers produced it, so
        ///     the heuristic keeps working for the common encryption-only case without breaking chains.
        ///
        ///     WHY THE HEURISTIC EXISTS. A <see cref="CryptographicException"/> alone is not a reliable
        ///     wrong-password signal. CBC decryption with the wrong key yields random bytes, and PKCS7
        ///     padding validation accepts those by chance roughly once in 256 attempts - measured at
        ///     0.40% over 3000 wrong-password loads. When it does, decryption "succeeds", the garbage
        ///     reaches the JSON parser, and the user is told "Unable to parse file" with a
        ///     JsonReaderException about an unexpected character, which points at file corruption
        ///     rather than at the password they actually got wrong.
        ///
        ///     This is a diagnostic improvement, not an integrity guarantee - only an authenticated
        ///     construction would be that, and switching to one would change the on-disk format and
        ///     make every existing encrypted file unreadable.
        /// </remarks>
        protected virtual void AfterDecryptInternal(JsonSettings sender, ref byte[] data) {
            if (!IsValidUtf8(data))
                throw new JsonSettingsException("Password appears to be invalid.");
        }

        /// <summary>
        ///     True when <paramref name="data"/> decodes as UTF-8 without invalid sequences.
        /// </summary>
        private static bool IsValidUtf8(byte[] data) {
            if (data == null || data.Length == 0)
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