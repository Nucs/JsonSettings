namespace Nucs.JsonSettings {
    /// <summary>
    ///     AES key sizes, in bits. Selects the AES variant for every AES-based
    ///     <see cref="EncryptionAlgorithm"/> (CBC, CBC-HMAC, GCM, CCM).
    /// </summary>
    /// <remarks>
    ///     This enum moved here from a third-party namespace when the encryption module was migrated
    ///     onto <c>System.Security.Cryptography</c> directly. The values are unchanged, so
    ///     <c>KeySize.Aes256</c> still means a 256-bit key.
    /// </remarks>
    public enum KeySize {
        /// <summary>128-bit key (AES-128).</summary>
        Aes128 = 128,

        /// <summary>192-bit key (AES-192).</summary>
        Aes192 = 192,

        /// <summary>256-bit key (AES-256). The default.</summary>
        Aes256 = 256
    }
}
