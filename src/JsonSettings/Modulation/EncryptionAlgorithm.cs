namespace Nucs.JsonSettings {
    /// <summary>
    ///     Selects the symmetric algorithm <see cref="Nucs.JsonSettings.Modulation.EncryptionModule"/>
    ///     uses. Every algorithm is provided by the .NET base class library
    ///     (<c>System.Security.Cryptography</c>); no third-party crypto is involved.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The default is <see cref="AesCbc"/> - the historical format this library has always
    ///         written, so files produced by any earlier version stay readable and nothing about the
    ///         on-disk bytes changes for callers who do not opt into another algorithm.
    ///     </para>
    ///     <para>
    ///         The authenticated algorithms below (everything except <see cref="AesCbc"/>) verify an
    ///         authentication tag when decrypting, so a wrong key or a tampered file fails with a real
    ///         integrity error rather than the best-effort UTF-8 heuristic the unauthenticated
    ///         <see cref="AesCbc"/> path relies on.
    ///     </para>
    ///     <para>
    ///         AEAD ciphers (<c>AesGcm</c>, <c>AesCcm</c>, <c>ChaCha20Poly1305</c>) are only present in
    ///         the BCL on .NET 6.0 and later, so those members do not exist when the library is built
    ///         for <c>netstandard2.0</c> or <c>net48</c>; those targets offer <see cref="AesCbc"/> and
    ///         <see cref="AesCbcHmac"/> only.
    ///     </para>
    ///     <para>
    ///         There is no algorithm marker in the file. As with the password and key size, decrypting
    ///         requires configuring the same algorithm the file was written with.
    ///     </para>
    /// </remarks>
    public enum EncryptionAlgorithm {
        /// <summary>
        ///     AES in CBC mode with PKCS7 padding (unauthenticated). Layout: <c>IV(16) || ciphertext</c>.
        ///     This is the default and is byte-for-byte compatible with every version of this library.
        /// </summary>
        AesCbc = 0,

        /// <summary>
        ///     AES-CBC with an HMAC-SHA256 tag in Encrypt-then-MAC order (authenticated). Layout:
        ///     <c>IV(16) || ciphertext || HMAC(32)</c>. Available on every target framework.
        /// </summary>
        AesCbcHmac = 1,

#if NET6_0_OR_GREATER
        /// <summary>
        ///     AES-GCM authenticated encryption (AEAD). Layout: <c>nonce(12) || ciphertext || tag(16)</c>.
        ///     Requires .NET 6.0 or later.
        /// </summary>
        AesGcm = 2,

        /// <summary>
        ///     AES-CCM authenticated encryption (AEAD). Layout: <c>nonce(12) || ciphertext || tag(16)</c>.
        ///     Requires .NET 6.0 or later and OS support for CCM.
        /// </summary>
        AesCcm = 3,

        /// <summary>
        ///     ChaCha20-Poly1305 authenticated encryption (AEAD). Uses a fixed 256-bit key regardless of
        ///     <see cref="KeySize"/>. Layout: <c>nonce(12) || ciphertext || tag(16)</c>. Requires .NET 6.0
        ///     or later and OS support for ChaCha20-Poly1305.
        /// </summary>
        ChaCha20Poly1305 = 4,
#endif
    }
}
