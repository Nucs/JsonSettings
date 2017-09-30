using System;
using System.Security;
using System.Security.Cryptography;
using Rijndael256;
using Rijndael = Rijndael256.Rijndael;

namespace nucs.JsonSettings.Modulation {
    public class RijndaelModule : Module {
        public static SecureString EmptyString { get; } = "".ToSecureString();
        internal KeySize KeySize { get; set; } = KeySize.Aes256;

        public SecureString Password {
            get => _fetcher?.Invoke();
            set { _fetcher = () => value; }
        }

        private Func<SecureString> _fetcher;
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
            socket.Encrypt += _Encrypt;
            socket.Decrypt += _Decrypt;
        }

        public override void Deattach(JsonSettings socket) {
            base.Deattach(socket);
            socket.Encrypt -= _Encrypt;
            socket.Decrypt -= _Decrypt;
        }

        protected void _Encrypt(ref byte[] data) {
            data = Rijndael.Encrypt(data, Password.ToRawString(), Rng.GenerateRandomBytes(Rijndael.InitializationVectorSize), KeySize);
        }

        protected void _Decrypt(ref byte[] data) {
            try {
                data = Rijndael.DecryptBytes(data, Password.ToRawString(), KeySize);
            } catch (CryptographicException inner) {
                throw new JsonSettingsException("Password appears to be invalid.", inner);
            }
        }
    }
}