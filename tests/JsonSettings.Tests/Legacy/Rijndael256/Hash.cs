/*
 * Rijndael256
 * Copyright (C)2013 2Toad, LLC.
 * licensing@2toad.com
 * 
 * https://github.com/2Toad/Rijndael256
 */

using System;
using System.Security.Cryptography;
using System.Text;


namespace Rijndael256
{
    /// <summary>
    /// Cryptographic hash functions.
    /// </summary>
    internal static class Hash
    {
        /// <summary>
        /// Generates a SHA-512 hash from the specified <paramref name="data"/>.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <returns>The hash.</returns>
        public static string Sha512(string data)
        {
            var hash = SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(data));
            return BitConverter.ToString(hash).Replace("-", "");
        }

        /// <summary>
        /// Generates a raw SHA-512 hash from the specified <paramref name="data"/>. Used to seed the
        /// salt for a byte[] password, where the string overload's hex-string form is not wanted.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <returns>The 64-byte hash.</returns>
        public static byte[] Sha512(byte[] data)
        {
            using (var sha = SHA512.Create())
                return sha.ComputeHash(data);
        }

        /// <summary>
        /// Generates a PBKDF2 hash from the specified <paramref name="data"/>.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <param name="salt">The salt.</param>
        /// <param name="iterations">The number of iterations to derive the hash.</param>
        /// <param name="size">The size of the hash.</param>
        /// <returns>The hash.</returns>
        public static byte[] Pbkdf2(string data, string salt, int iterations, int size = 64)
        {
            return Pbkdf2(data, Encoding.UTF8.GetBytes(salt), iterations, size);
        }

        /// <summary>
        /// Generates a PBKDF2 hash from the specified <paramref name="data"/>.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <param name="salt">The salt.</param>
        /// <param name="iterations">The number of iterations to derive the hash.</param>
        /// <param name="size">The size of the hash.</param>
        /// <returns>The hash.</returns>
        public static byte[] Pbkdf2(string data, byte[] salt, int iterations, int size = 64)
        {
            return Pbkdf2(Encoding.UTF8.GetBytes(data), salt, iterations, size);
        }

        /// <summary>
        /// Generates a PBKDF2 hash from the specified <paramref name="data"/>.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <param name="salt">The salt.</param>
        /// <param name="iterations">The number of iterations to derive the hash.</param>
        /// <param name="size">The size of the hash.</param>
        /// <returns>The hash.</returns>
        internal static byte[] Pbkdf2(byte[] data, byte[] salt, int iterations, int size = 64)
        {
            // SHA-1 is the pseudorandom function here and must STAY SHA-1. It is not a choice
            // being made now: the parameterless-PRF constructor this used to call defaults to
            // SHA-1, so every settings file this library has ever encrypted has a key derived
            // with it. Switching the PRF would leave all of them undecryptable with no error
            // beyond a padding failure. This is a file-format constant, not a style preference.
            //
            // Spelling it out also clears SYSLIB0041, which flags that constructor precisely
            // because its defaults are invisible at the call site, and SYSLIB0060 on net10.0,
            // which asks for the static one-shot instead of the instance type.
            //
            // The three arms below are byte-for-byte identical in output; only the API used to
            // ask for it differs, because the newer spellings do not exist on the older
            // contracts. Rfc2898DeriveBytes.Pbkdf2 is .NET 6+, and the constructor overload
            // taking a HashAlgorithmName exists on .NET Framework 4.7.2+ but not in
            // netstandard2.0, which is why the fallback keeps the obsolete form.
#if NET6_0_OR_GREATER
            return Rfc2898DeriveBytes.Pbkdf2(data, salt, iterations, HashAlgorithmName.SHA1, size);
#elif NET472_OR_GREATER
            return (new Rfc2898DeriveBytes(data, salt, iterations, HashAlgorithmName.SHA1)).GetBytes(size);
#else
            return (new Rfc2898DeriveBytes(data, salt, iterations)).GetBytes(size);
#endif
        }
    }
}