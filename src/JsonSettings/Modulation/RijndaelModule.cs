using System;
using System.Security;
using Nucs.JsonSettings.Modulation.Encryption;

namespace Nucs.JsonSettings.Modulation {
    /// <summary>
    ///     Obsolete alias for <see cref="EncryptionModule"/>. Retained only so existing
    ///     <c>new RijndaelModule(...)</c> and <c>RijndaelModule.FromRawKey(...)</c> call sites keep
    ///     compiling; every member forwards to <see cref="EncryptionModule"/>, which now runs on
    ///     <c>System.Security.Cryptography</c> directly. The default algorithm and the on-disk
    ///     format are unchanged, so a module built through this shim reads and writes exactly what it
    ///     did before.
    /// </summary>
    [Obsolete("RijndaelModule was renamed to EncryptionModule when encryption moved onto System.Security.Cryptography directly. This shim forwards to EncryptionModule and will be removed in a future major version.")]
    public class RijndaelModule : EncryptionModule {
        public RijndaelModule(string password) : base(password) { }

        public RijndaelModule(SecureString password) : base(password) { }

        public RijndaelModule(Func<string> passwordFetcher) : base(passwordFetcher) { }

        public RijndaelModule(Func<SecureString> passwordFetcher) : base(passwordFetcher) { }

        public RijndaelModule(byte[] password) : base(password) { }

        public RijndaelModule(Func<byte[]> passwordFetcher) : base(passwordFetcher) { }

        //Forwards the raw-key factory to the protected base constructor, returning a RijndaelModule so
        //a caller that assigned the result to a RijndaelModule-typed variable still compiles.
        protected RijndaelModule(Func<byte[]> fetcher, bool rawKey) : base(fetcher, rawKey) { }

        /// <summary>Obsolete alias for <see cref="EncryptionModule.FromRawKey(byte[])"/>.</summary>
        public new static RijndaelModule FromRawKey(byte[] key) {
            if (key is null) throw new ArgumentNullException(nameof(key));
            AesKeyLengths.Validate(key);
            return new RijndaelModule(CloneFetcher(key), rawKey: true);
        }

        /// <summary>Obsolete alias for <see cref="EncryptionModule.FromRawKey(System.Func{byte[]})"/>.</summary>
        public new static RijndaelModule FromRawKey(Func<byte[]> keyFetcher) {
            if (keyFetcher is null) throw new ArgumentNullException(nameof(keyFetcher));
            return new RijndaelModule(keyFetcher, rawKey: true);
        }
    }
}
