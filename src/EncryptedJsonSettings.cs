using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using nucs.JsonSettings.Inline;
using Newtonsoft.Json;
using Rijndael256;
using Rijndael = Rijndael256.Rijndael;

namespace nucs.JsonSettings {
    public abstract class EncryptedJsonSettings : JsonSettings, IEncryptedSavable {
        public static SecureString EmptyString { get; } = "".ToSecureString();

        [JsonIgnore]
        public SecureString Password { get; set; }

        protected EncryptedJsonSettings() : base() { }

        protected EncryptedJsonSettings(string password) : this(password, "##DEFAULT##") { }

        protected EncryptedJsonSettings(string password, string fileName = "##DEFAULT##") : this(password?.ToSecureString(), fileName) { }

        protected EncryptedJsonSettings(SecureString password) : this(password, "##DEFAULT##") { }

        protected EncryptedJsonSettings(SecureString password, string fileName = "##DEFAULT##") : base(fileName) {
            ChangePassword(password);
        }

        public void ChangePassword(string pass) {
            ChangePassword(pass?.ToSecureString());
        }

        public void ChangePassword(SecureString pass) {
            setpass(pass);
        }

        private void setpass(SecureString password) {
            Password = password ?? EmptyString;
            if (!Password.IsReadOnly())
                Password.MakeReadOnly();
        }

        public override string FileName { get; set; }

        #region Cryptography

        public override void Decrypt(ref byte[] data) {
            base.Decrypt(ref data);
            try {
                data = Rijndael.DecryptBytes(data, Password.ToRawString(), KeySize.Aes256);
            } catch (CryptographicException inner) {
                throw new JsonSettingsException("Password appears to be invalid.", inner);
            }
        }

        public override void Encrypt(ref byte[] data) {
            base.Encrypt(ref data);
            data = Rijndael.Encrypt(data, Password.ToRawString(), Rng.GenerateRandomBytes(Rijndael.InitializationVectorSize), KeySize.Aes256);
        }

        #endregion

        #region Loading Saving

        /// <summary>
        ///     Saves settings to a given path using custom password.
        /// </summary>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name. <br></br>Without path the file will be located at the executing directory</param>
        /// <param name="intype">The type to save <paramref name="pSettings"/> as.</param>
        /// <param name="pSettings">The settings file to save</param>
        /// <param name="password">The password for decrypting</param>
        public static void Save(Type intype, IEncryptedSavable pSettings, SecureString password, string filename = "##DEFAULT##") {
            pSettings.Password = password;
            JsonSettings.Save(intype, pSettings, filename);
        }

        /// <summary>
        ///     Saves settings to a given path using custom password.
        /// </summary>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name. <br></br>Without path the file will be located at the executing directory</param>
        /// <param name="intype">The type to save <paramref name="pSettings"/> as.</param>
        /// <param name="pSettings">The settings file to save</param>
        /// <param name="password">The password for decrypting</param>
        public static void Save(Type intype, IEncryptedSavable pSettings, string password, string filename = "##DEFAULT##") {
            Save(intype, pSettings, password?.ToSecureString(), filename);
        }

        /// <summary>
        ///     Saves settings to a given path using custom password.
        /// </summary>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name. <br></br>Without path the file will be located at the executing directory</param>
        /// <param name="pSettings">The settings file to save</param>
        /// <param name="password">The password for decrypting</param>
        public static void Save<T>(T pSettings, string password, string filename = "##DEFAULT##") where T : IEncryptedSavable {
            Save(typeof(T), pSettings, password, filename);
        }

        /// <summary>
        ///     Saves settings to a given path with the password inside <paramref name="pSettings"/>.
        /// </summary>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name. <br></br>Without path the file will be located at the executing directory</param>
        /// <param name="intype">The type to save <paramref name="pSettings"/> as.</param>
        /// <param name="pSettings">The settings file to save</param>
        public static void Save(Type intype, IEncryptedSavable pSettings, string filename = "##DEFAULT##") {
            Save(intype, pSettings, pSettings.Password, filename);
        }


        /// <summary>
        ///     Saves settings to a given path.
        /// </summary>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name. <br></br>Without path the file will be located at the executing directory</param>
        /// <param name="pSettings">The settings file to save</param>
        public new static void Save<T>(T pSettings, string filename = "##DEFAULT##") where T : IEncryptedSavable {
            Save(typeof(T), pSettings, ((IEncryptedSavable) pSettings).Password, filename);
        }

        /// <summary>
        ///     Loads a settings file or creates a new settings file with custom password.
        /// </summary>
        /// <param name="instance">The instance inwhich to load into</param>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name. <br></br>Without path the file will be located at the executing directory</param>
        /// <param name="password">The password for decrypting</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static T Load<T>(T instance, string password, string filename = "##DEFAULT##") where T : IEncryptedSavable {
            return Load(instance, password?.ToSecureString(), filename);
        }

        /// <summary>
        ///     Loads a settings file or creates a new settings file with the password inside the <paramref name="instance"/>.
        /// </summary>
        /// <param name="instance">The instance inwhich to load into</param>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.<br></br>Without path the file will be located at the executing directory</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public new static T Load<T>(T instance, string filename = "##DEFAULT##") where T : IEncryptedSavable {
            return Load(instance, instance.Password, filename);
        }

        /// <summary>
        ///     Loads a settings file or creates a new settings file.
        /// </summary>
        /// <param name="intype">The type of this object</param>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.<br></br>Without path the file will be located at the executing directory</param>
        /// <param name="password">The password for decrypting</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static object Load(Type intype, string password, string filename = "##DEFAULT##") {
            return Load((IEncryptedSavable) Activator.CreateInstance(intype), password?.ToSecureString(), filename);
        }

        /// <summary>
        ///     Loads or creates a settings file.
        /// </summary>
        /// <param name="password">The password for decrypting</param>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static T Load<T>(string password, string filename = "##DEFAULT##") where T : IEncryptedSavable, new() {
            return (T) Load(typeof(T), password?.ToSecureString(), filename);
        }

        /// <summary>
        ///     Loads a settings file or creates a new settings file.
        /// </summary>
        /// <param name="instance">The instance inwhich to load into</param>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.<br></br>Without path the file will be located at the executing directory</param>
        /// <param name="password">The password for decrypting</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static T Load<T>(T instance, SecureString password, string filename = "##DEFAULT##") where T : IEncryptedSavable {
            instance.Password = password ?? EmptyString;
            return JsonSettings.Load(instance, filename);
        }

        /// <summary>
        ///     Loads a settings file or creates a new settings file.
        /// </summary>
        /// <param name="intype">The type of this object</param>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.<br></br>Without path the file will be located at the executing directory</param>
        /// <param name="password">The password for decrypting</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static object Load(Type intype, SecureString password, string filename = "##DEFAULT##") {
            return Load((IEncryptedSavable) Activator.CreateInstance(intype), password, filename);
        }

        /// <summary>
        ///     Loads or creates a settings file.
        /// </summary>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.</param>
        /// <param name="password">The password for decrypting</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static T Load<T>(SecureString password, string filename = "##DEFAULT##") where T : IEncryptedSavable, new() {
            return (T) Load(typeof(T), password, filename);
        }

        #endregion
    }
}