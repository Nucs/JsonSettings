using System;
using System.Security.Cryptography;
using System.Text;

namespace Nucs.JsonSettings.Modulation.Encryption {
    /// <summary>
    ///     Turns a text or binary password into an AES/ChaCha key with PBKDF2, using only
    ///     <c>System.Security.Cryptography</c>. This reproduces, byte-for-byte, the key
    ///     derivation the library has always used (previously provided by a vendored third-party
    ///     helper), so a file written by any earlier version decrypts unchanged.
    /// </summary>
    /// <remarks>
    ///     The PBKDF2 pseudorandom function is SHA-1 and MUST stay SHA-1: every settings file this
    ///     library has ever encrypted has its key derived with it, because the parameterless-PRF
    ///     constructor the original code called defaults to SHA-1. Changing it would leave every
    ///     existing file undecryptable with nothing but a padding failure to show for it. This is a
    ///     file-format constant, not a modern-crypto choice - the surrounding algorithms are what the
    ///     newer <see cref="EncryptionAlgorithm"/> members are for.
    /// </remarks>
    internal static class KeyDerivation {
        /// <summary>PBKDF2 iteration count. An inherited file-format constant; changing it makes every existing file undecryptable.</summary>
        internal const int Iterations = 10000;

        /// <summary>
        ///     Derives <paramref name="keyBytes"/> of key material from a text password. The salt is
        ///     seeded from the uppercase hex of <c>SHA512(password + password.Length)</c>, matching the
        ///     original string key path exactly.
        /// </summary>
        internal static byte[] FromText(string password, int keyBytes) {
            if (password == null)
                password = string.Empty;

            var pwd = Encoding.UTF8.GetBytes(password);
            var seed = Encoding.UTF8.GetBytes(password + password.Length);
            var saltSeed = Encoding.UTF8.GetBytes(ToHex(Sha512(seed)));
            var salt = Pbkdf2(pwd, saltSeed, Iterations, 64);
            return Pbkdf2(pwd, salt, Iterations, keyBytes);
        }

        /// <summary>
        ///     Derives <paramref name="keyBytes"/> of key material from a binary password. The salt is
        ///     seeded from the raw <c>SHA512(password || length)</c> - the byte-domain analogue of the
        ///     text path, and a deliberately distinct credential from the text password whose UTF-8
        ///     bytes happen to equal <paramref name="password"/>.
        /// </summary>
        internal static byte[] FromBytes(byte[] password, int keyBytes) {
            if (password == null)
                password = Array.Empty<byte>();

            var lengthTag = BitConverter.GetBytes(password.Length);
            var seedInput = new byte[password.Length + lengthTag.Length];
            Buffer.BlockCopy(password, 0, seedInput, 0, password.Length);
            Buffer.BlockCopy(lengthTag, 0, seedInput, password.Length, lengthTag.Length);

            var salt = Pbkdf2(password, Sha512(seedInput), Iterations, 64);
            return Pbkdf2(password, salt, Iterations, keyBytes);
        }

        private static byte[] Sha512(byte[] data) {
            using (var sha = SHA512.Create())
                return sha.ComputeHash(data);
        }

        /// <summary>
        ///     Uppercase hex without separators. Byte-for-byte the legacy
        ///     <c>BitConverter.ToString(hash).Replace("-", "")</c> the salt seed depends on.
        /// </summary>
        private static string ToHex(byte[] data) {
            return BitConverter.ToString(data).Replace("-", "");
        }

        private static byte[] Pbkdf2(byte[] data, byte[] salt, int iterations, int size) {
            // SHA-1 is the PRF and must stay SHA-1 (see the type remarks). The three arms are
            // byte-for-byte identical in output; only the API differs, because the newer spellings do
            // not exist on the older contracts. Rfc2898DeriveBytes.Pbkdf2 is .NET 6+, the
            // HashAlgorithmName constructor overload exists on .NET Framework 4.7.2+ but not in
            // netstandard2.0, and spelling the PRF out clears SYSLIB0041/SYSLIB0060.
#if NET6_0_OR_GREATER
            return Rfc2898DeriveBytes.Pbkdf2(data, salt, iterations, HashAlgorithmName.SHA1, size);
#elif NET472_OR_GREATER
            using (var kdf = new Rfc2898DeriveBytes(data, salt, iterations, HashAlgorithmName.SHA1))
                return kdf.GetBytes(size);
#else
            using (var kdf = new Rfc2898DeriveBytes(data, salt, iterations))
                return kdf.GetBytes(size);
#endif
        }
    }
}
