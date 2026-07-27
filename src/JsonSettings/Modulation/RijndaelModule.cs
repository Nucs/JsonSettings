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
        }

        public override void Deattach(JsonSettings socket) {
            base.Deattach(socket);
            socket.Encrypt -= EncryptInternal;
            socket.Decrypt -= DecryptInternal;
        }

        protected virtual void EncryptInternal(JsonSettings sender, ref byte[] data) {
            data = Rijndael.Encrypt(data, Password.ToRawString(), Rng.GenerateRandomBytes(Rijndael.InitializationVectorSize), KeySize);
        }

        protected virtual void DecryptInternal(JsonSettings sender, ref byte[] data) {
            try {
                data = Rijndael.DecryptBytes(data, Password.ToRawString(), KeySize);
            } catch (CryptographicException inner) {
                throw new JsonSettingsException("Password appears to be invalid.", inner);
            }

            //A CryptographicException alone is not a reliable wrong-password signal. CBC
            //decryption with the wrong key yields random bytes, and PKCS7 padding validation
            //accepts those by chance roughly once in 256 attempts - measured at 0.40% over 3000
            //wrong-password loads. When it does, decryption "succeeds", the garbage reaches the
            //JSON parser, and the user is told "Unable to parse file" with a
            //JsonReaderException about an unexpected character, which points at file corruption
            //rather than at the password they actually got wrong.
            //
            //What this library encrypts is always UTF-8 JSON, so plaintext that is not even
            //valid UTF-8 did not come from this password. That closes almost all of the
            //remaining gap: random bytes surviving both the padding check and UTF-8 validation
            //is vanishingly unlikely.
            //
            //This is a diagnostic improvement, not an integrity guarantee - only an
            //authenticated construction would be that, and switching to one would change the
            //on-disk format and make every existing encrypted file unreadable.
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