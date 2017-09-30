using System.Security;

namespace nucs.JsonSettings {
    public interface IEncryptedSavable : ISavable {
        /// <summary>
        ///     The password which is used to encrypt and decrypt the file.
        /// </summary>
        SecureString Password { get; }
    }
}