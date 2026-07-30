/*
 * Rijndael256
 * Copyright (C)2013 2Toad, LLC.
 * licensing@2toad.com
 * 
 * https://github.com/2Toad/Rijndael256
 */

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Rijndael256 {
    /// <summary>
    /// AES implementation of the Rijndael symmetric-key cipher.
    /// </summary>
    internal class Rijndael {
        internal const int InitializationVectorSize = 16;
        internal const CipherMode BlockCipherMode = CipherMode.CBC;

        /// <summary>
        /// Encrypts plaintext using the Rijndael cipher in CBC mode with a password derived HMAC SHA-512 salt.
        /// A random 128-bit Initialization Vector is generated for the cipher.
        /// </summary>
        /// <param name="plaintext">The plaintext to encrypt.</param>
        /// <param name="password">The password to encrypt the plaintext with.</param>
        /// <param name="keySize">The cipher key size. 256-bit is stronger, but slower.</param>
        /// <returns>The Base64 encoded ciphertext.</returns>
        public static string Encrypt(string plaintext, string password, KeySize keySize) {
            return Encrypt(Encoding.UTF8.GetBytes(plaintext), password, keySize);
        }

        /// <summary>
        /// Encrypts plaintext using the Rijndael cipher in CBC mode with a password derived HMAC SHA-512 salt.
        /// A random 128-bit Initialization Vector is generated for the cipher.
        /// </summary>
        /// <param name="plaintext">The plaintext to encrypt.</param>
        /// <param name="password">The password to encrypt the plaintext with.</param>
        /// <param name="keySize">The cipher key size. 256-bit is stronger, but slower.</param>
        /// <returns>The Base64 encoded ciphertext.</returns>
        public static string Encrypt(byte[] plaintext, string password, KeySize keySize) {
            // Generate a random IV
            var iv = Rng.GenerateRandomBytes(InitializationVectorSize);

            // Encrypt the plaintext
            var ciphertext = Encrypt(plaintext, password, iv, keySize);

            // Encode the ciphertext
            return Convert.ToBase64String(ciphertext);
        }

        /// <summary>
        /// Encrypts plaintext using the Rijndael cipher in CBC mode with a password derived HMAC SHA-512 salt.
        /// </summary>
        /// <param name="plaintext">The plaintext to encrypt.</param>
        /// <param name="password">The password to encrypt the plaintext with.</param>
        /// <param name="iv">The initialization vector. Must be 128-bits.</param>
        /// <param name="keySize">The cipher key size. 256-bit is stronger, but slower.</param>
        /// <returns>The ciphertext.</returns>
        public static byte[] Encrypt(byte[] plaintext, string password, byte[] iv, KeySize keySize) {
            if (iv.Length != InitializationVectorSize) throw new ArgumentOutOfRangeException(nameof(iv), "AES requires an Initialization Vector of 128-bits.");

            byte[] ciphertext;
            using (var ms = new MemoryStream()) {
                // Insert IV at beginning of ciphertext
                ms.Write(iv, 0, iv.Length);

                // Create a CryptoStream to encrypt the plaintext
                using (var cs = new CryptoStream(ms, CreateEncryptor(password, iv, keySize), CryptoStreamMode.Write)) {
                    // Encrypt the plaintext
                    cs.Write(plaintext, 0, plaintext.Length);
                    cs.FlushFinalBlock();
                }

                ciphertext = ms.ToArray();
            }

            // IV + Cipher
            return ciphertext;
        }

        /// <summary>
        /// Encrypts a plaintext file using the Rijndael cipher in CBC mode with a password derived HMAC SHA-512 salt.
        /// A random 128-bit Initialization Vector is generated for the cipher.
        /// </summary>
        /// <param name="plaintextFile">The plaintext file to encrypt.</param>
        /// <param name="ciphertextFile">The resulting ciphertext file.</param>
        /// <param name="password">The password to encrypt the plaintext file with.</param>
        /// <param name="keySize">The cipher key size. 256-bit is stronger, but slower.</param>
        public static void Encrypt(string plaintextFile, string ciphertextFile, string password, KeySize keySize) {
            // Create a new ciphertext file to write the ciphertext to
            using (var fsc = new FileStream(ciphertextFile, FileMode.Create, FileAccess.Write)) {
                // Store the IV at the beginning of the ciphertext file
                var iv = Rng.GenerateRandomBytes(InitializationVectorSize);
                fsc.Write(iv, 0, iv.Length);

                // Create a CryptoStream to encrypt the plaintext
                using (var cs = new CryptoStream(fsc, CreateEncryptor(password, iv, keySize), CryptoStreamMode.Write)) {
                    // Open the plaintext file
                    using (var fsp = new FileStream(plaintextFile, FileMode.Open, FileAccess.Read)) {
                        // Create a buffer to process the plaintext file in chunks
                        // Reading the whole file into memory can cause 
                        // Out of Memory exceptions if the file is large
                        var buffer = new byte[4096];

                        // Read a chunk from the plaintext file
                        int bytesRead;
                        while ((bytesRead = fsp.Read(buffer, 0, buffer.Length)) > 0) {
                            // Encrypt the plaintext and write it to the ciphertext file
                            cs.Write(buffer, 0, bytesRead);
                        }

                        // Finalize encryption
                        cs.FlushFinalBlock();
                    }
                }
            }
        }

        /// <summary>
        /// Decrypts ciphertext using the Rijndael cipher in CBC mode with a password derived HMAC SHA-512 salt.
        /// </summary>
        /// <param name="ciphertext">The Base64 encoded ciphertext to decrypt.</param>
        /// <param name="password">The password to decrypt the ciphertext with.</param>
        /// <param name="keySize">The size of the cipher key used to create the ciphertext.</param>
        /// <returns>The plaintext.</returns>
        public static string Decrypt(string ciphertext, string password, KeySize keySize) {
            return Decrypt(Convert.FromBase64String(ciphertext), password, keySize);
        }

        /// <summary>
        /// Decrypts ciphertext using the Rijndael cipher in CBC mode with a password derived HMAC SHA-512 salt.
        /// </summary>
        /// <param name="ciphertext">The ciphertext to decrypt.</param>
        /// <param name="password">The password to decrypt the ciphertext with.</param>
        /// <param name="keySize">The size of the cipher key used to create the ciphertext.</param>
        /// <returns>The plaintext.</returns>
        public static string Decrypt(byte[] ciphertext, string password, KeySize keySize) {
            using (var ms = new MemoryStream(ciphertext)) {
                // Extract the IV from the ciphertext.
                // Same discarded-return-value shape as the file overload below; a MemoryStream
                // will not short-read, but it WILL happily return fewer bytes when the buffer it
                // wraps is smaller than the IV, and a truncated file is exactly the input that
                // gets here. Reject it explicitly instead of decrypting with a zero-padded IV.
                var iv = new byte[InitializationVectorSize];
                if (ms.Read(iv, 0, iv.Length) != iv.Length)
                    throw new EndOfStreamException($"Ciphertext is {ciphertext.Length} bytes, shorter than its {iv.Length}-byte initialization vector.");

                // Create a CryptoStream to decrypt the ciphertext
                using (var cs = new CryptoStream(ms, CreateDecryptor(password, iv, keySize), CryptoStreamMode.Read)) {
                    // Decrypt the ciphertext
                    using (var sr = new StreamReader(cs, Encoding.UTF8)) return sr.ReadToEnd();
                }
            }
        }

        /// <summary>
        /// Decrypts ciphertext using the Rijndael cipher in CBC mode with a password derived HMAC SHA-512 salt.
        /// </summary>
        /// <param name="ciphertext">The ciphertext to decrypt.</param>
        /// <param name="password">The password to decrypt the ciphertext with.</param>
        /// <param name="keySize">The size of the cipher key used to create the ciphertext.</param>
        /// <returns>The plaintext.</returns>
        public static byte[] DecryptBytes(byte[] ciphertext, string password, KeySize keySize) {
            byte[] ReadAllBytes(Stream instream) {
                if (instream is MemoryStream)
                    return ((MemoryStream) instream).ToArray();

                using (var memoryStream = new MemoryStream()) {
                    instream.CopyTo(memoryStream);
                    return memoryStream.ToArray();
                }
            }

            using (var ms = new MemoryStream(ciphertext)) {
                // Extract the IV from the ciphertext; see the Decrypt overload above for why the
                // length is checked rather than discarded.
                var iv = new byte[InitializationVectorSize];
                if (ms.Read(iv, 0, iv.Length) != iv.Length)
                    throw new EndOfStreamException($"Ciphertext is {ciphertext.Length} bytes, shorter than its {iv.Length}-byte initialization vector.");

                // Create a CryptoStream to decrypt the ciphertext
                using (var cs = new CryptoStream(ms, CreateDecryptor(password, iv, keySize), CryptoStreamMode.Read)) {
                    return ReadAllBytes(cs);
                }
            }
        }

        /// <summary>
        /// Decrypts ciphertext using the Rijndael cipher in CBC mode with a password derived HMAC SHA-512 salt.
        /// </summary>
        /// <param name="ciphertextFile">The ciphertext file to decrypt.</param>
        /// <param name="plaintextFile">The resulting plaintext file.</param>
        /// <param name="password">The password to decrypt the ciphertext file with.</param>
        /// <param name="keySize">The size of the cipher key used to create the ciphertext file.</param>
        public static void Decrypt(string ciphertextFile, string plaintextFile, string password, KeySize keySize) {
            // Open the ciphertext file
            using (var fsc = new FileStream(ciphertextFile, FileMode.Open, FileAccess.Read)) {
                // Read the IV from the beginning of the ciphertext file.
                // Stream.Read is permitted to return fewer bytes than asked for, and the return
                // value used to be discarded here (CA2022). A short read left the tail of the IV
                // as zeros, which does not throw -- it silently derives the wrong transform and
                // produces garbage plaintext or a misleading padding error much further on. Read
                // until the IV is full, and treat a truncated file as the error it is.
                var iv = new byte[InitializationVectorSize];
                var ivRead = 0;
                while (ivRead < iv.Length) {
                    var read = fsc.Read(iv, ivRead, iv.Length - ivRead);
                    if (read == 0)
                        throw new EndOfStreamException($"Ciphertext file '{ciphertextFile}' ended after {ivRead} bytes, before its {iv.Length}-byte initialization vector was complete.");
                    ivRead += read;
                }

                // Create a new plaintext file to write the plaintext to
                using (var fsp = new FileStream(plaintextFile, FileMode.Create, FileAccess.Write)) {
                    // Create a CryptoStream to decrypt the ciphertext
                    using (var cs = new CryptoStream(fsp, CreateDecryptor(password, iv, keySize), CryptoStreamMode.Write)) {
                        // Create a buffer to process the plaintext file in chunks
                        // Reading the whole file into memory can cause 
                        // Out of Memory exceptions if the file is large
                        var buffer = new byte[4096];

                        // Read a chunk from the ciphertext file
                        int bytesRead;
                        while ((bytesRead = fsc.Read(buffer, 0, buffer.Length)) > 0) {
                            // Decrypt the ciphertext and write it to the plaintext file
                            cs.Write(buffer, 0, bytesRead);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Generates a cryptographic key from a password.
        /// </summary>
        /// <param name="password">The password.</param>
        /// <param name="keySize">The cipher key size. 256-bit is stronger, but slower.</param>
        /// <returns>The cryptographic key.</returns>
        public static byte[] GenerateKey(string password, KeySize keySize) {
            // Create a salt to help prevent rainbow table attacks
            var salt = Hash.Pbkdf2(password, Hash.Sha512(password + password.Length), Rijndael256Settings.HashIterations);

            // Generate a key from the password and salt
            return Hash.Pbkdf2(password, salt, Rijndael256Settings.HashIterations, (int) keySize / 8);
        }

        /// <summary>
        /// Generates a cryptographic key from a binary password, using the same PBKDF2 construction
        /// as the string overload (salt derived from the password, SHA-1 PRF, HashIterations rounds).
        /// </summary>
        /// <param name="password">The password bytes.</param>
        /// <param name="keySize">The cipher key size.</param>
        /// <returns>The cryptographic key.</returns>
        /// <remarks>
        ///     This is a DIFFERENT credential space from <see cref="GenerateKey(string,KeySize)"/> and
        ///     that is deliberate. The string overload seeds its salt from
        ///     <c>Sha512(password + password.Length)</c> — the char count and a string concatenation,
        ///     neither of which a raw byte[] carries — so there is no bytes-in that reproduces a given
        ///     string's key. The salt here is seeded from the bytes followed by their length, which is
        ///     the byte-domain analogue and is self-consistent: the same bytes always derive the same
        ///     key. The PRF stays SHA-1 for the same file-format reason spelled out in Hash.Pbkdf2.
        /// </remarks>
        public static byte[] GenerateKey(byte[] password, KeySize keySize) {
            if (password == null) throw new ArgumentNullException(nameof(password));

            var lengthTag = BitConverter.GetBytes(password.Length);
            var seedInput = new byte[password.Length + lengthTag.Length];
            Buffer.BlockCopy(password, 0, seedInput, 0, password.Length);
            Buffer.BlockCopy(lengthTag, 0, seedInput, password.Length, lengthTag.Length);

            var salt = Hash.Pbkdf2(password, Hash.Sha512(seedInput), Rijndael256Settings.HashIterations);
            return Hash.Pbkdf2(password, salt, Rijndael256Settings.HashIterations, (int) keySize / 8);
        }

        /// <summary>
        /// Encrypts plaintext with a key used verbatim (no key derivation). The IV is prepended to the
        /// ciphertext exactly as the password overloads do, so the two produce the same on-disk shape.
        /// </summary>
        /// <param name="plaintext">The plaintext to encrypt.</param>
        /// <param name="key">The AES key. Must be 16, 24 or 32 bytes.</param>
        /// <param name="iv">The initialization vector. Must be 128-bits.</param>
        /// <returns>The ciphertext (IV + cipher).</returns>
        public static byte[] Encrypt(byte[] plaintext, byte[] key, byte[] iv) {
            if (iv.Length != InitializationVectorSize) throw new ArgumentOutOfRangeException(nameof(iv), "AES requires an Initialization Vector of 128-bits.");

            byte[] ciphertext;
            using (var ms = new MemoryStream()) {
                // Insert IV at beginning of ciphertext
                ms.Write(iv, 0, iv.Length);

                using (var cs = new CryptoStream(ms, CreateEncryptor(key, iv), CryptoStreamMode.Write)) {
                    cs.Write(plaintext, 0, plaintext.Length);
                    cs.FlushFinalBlock();
                }

                ciphertext = ms.ToArray();
            }

            // IV + Cipher
            return ciphertext;
        }

        /// <summary>
        /// Decrypts ciphertext with a key used verbatim (no key derivation).
        /// </summary>
        /// <param name="ciphertext">The ciphertext to decrypt (IV + cipher).</param>
        /// <param name="key">The AES key the ciphertext was created with.</param>
        /// <returns>The plaintext bytes.</returns>
        public static byte[] DecryptBytes(byte[] ciphertext, byte[] key) {
            byte[] ReadAllBytes(Stream instream) {
                if (instream is MemoryStream)
                    return ((MemoryStream) instream).ToArray();

                using (var memoryStream = new MemoryStream()) {
                    instream.CopyTo(memoryStream);
                    return memoryStream.ToArray();
                }
            }

            using (var ms = new MemoryStream(ciphertext)) {
                // Extract the IV from the ciphertext; see the password DecryptBytes overload for why
                // the length is checked rather than discarded.
                var iv = new byte[InitializationVectorSize];
                if (ms.Read(iv, 0, iv.Length) != iv.Length)
                    throw new EndOfStreamException($"Ciphertext is {ciphertext.Length} bytes, shorter than its {iv.Length}-byte initialization vector.");

                using (var cs = new CryptoStream(ms, CreateDecryptor(key, iv), CryptoStreamMode.Read)) {
                    return ReadAllBytes(cs);
                }
            }
        }

        /// <summary>
        /// Creates a symmetric Rijndael encryptor.
        /// </summary>
        /// <param name="password">The password to encrypt the plaintext with.</param>
        /// <param name="iv">The initialization vector. Must be 128-bits.</param>
        /// <param name="keySize">The cipher key size. 256-bit is stronger, but slower.</param>
        /// <returns>The symmetric encryptor.</returns>
        public static ICryptoTransform CreateEncryptor(string password, byte[] iv, KeySize keySize) {
            // Aes, not RijndaelManaged (obsolete as of SYSLIB0022). This is not a cipher change:
            // AES *is* Rijndael restricted to a 128-bit block, RijndaelManaged defaults BlockSize
            // to 128, and nothing here ever set it otherwise -- the IV is fixed at
            // InitializationVectorSize = 16 bytes, which only a 128-bit block accepts. The class
            // has always documented itself as "AES implementation of the Rijndael symmetric-key
            // cipher". Ciphertext written by older versions decrypts unchanged.
            using var aes = Aes.Create();
            aes.Mode = BlockCipherMode;
            return aes.CreateEncryptor(GenerateKey(password, keySize), iv);
        }

        /// <summary>
        /// Creates a symmetric Rijndael decryptor.
        /// </summary>
        /// <param name="password">The password to decrypt the ciphertext with.</param>
        /// <param name="iv">The initialization vector. Must be 128-bits.</param>
        /// <param name="keySize">The cipher key size.</param>
        /// <returns>The symmetric decryptor.</returns>
        public static ICryptoTransform CreateDecryptor(string password, byte[] iv, KeySize keySize) {
            //see CreateEncryptor for why Aes is a drop-in for RijndaelManaged here.
            using var aes = Aes.Create();
            aes.Mode = BlockCipherMode;
            return aes.CreateDecryptor(GenerateKey(password, keySize), iv);
        }

        /// <summary>
        /// Creates a symmetric encryptor from a key used verbatim. The key length selects the AES
        /// variant (16/24/32 bytes -> AES-128/192/256), so no <see cref="KeySize"/> is taken here.
        /// </summary>
        public static ICryptoTransform CreateEncryptor(byte[] key, byte[] iv) {
            using var aes = Aes.Create();
            aes.Mode = BlockCipherMode;
            return aes.CreateEncryptor(key, iv);
        }

        /// <summary>
        /// Creates a symmetric decryptor from a key used verbatim.
        /// </summary>
        public static ICryptoTransform CreateDecryptor(byte[] key, byte[] iv) {
            using var aes = Aes.Create();
            aes.Mode = BlockCipherMode;
            return aes.CreateDecryptor(key, iv);
        }
    }
}