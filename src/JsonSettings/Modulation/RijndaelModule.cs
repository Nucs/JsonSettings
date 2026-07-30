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
    /// <remarks>This module uses internal class to perform the encryption and is not publicly exposed.<br></br>The password is stored as <see cref="SecureString"/> in memory.</remarks>
    public class RijndaelModule : Module {
        public static readonly SecureString EmptyString = "".ToSecureString();

        private Func<SecureString>? _fetcher;

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
            data = Rijndael.Encrypt(data, Password.ToRawString(), Rng.GenerateRandomBytes(Rijndael.InitializationVectorSize), KeySize);
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
                data = Rijndael.DecryptBytes(data, Password.ToRawString(), KeySize);
            } catch (CryptographicException inner) {
                throw new JsonSettingsException("Password appears to be invalid.", inner);
            }
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