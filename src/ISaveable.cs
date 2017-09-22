namespace nucs.JsonSettings {
    public interface ISaveable {
        /// <summary>
        ///     The path for this ISaveable file, relative pathing allowed to current executing file.
        /// </summary>
        string FileName { get; set; }

        /// <summary>
        ///     Save the settings file to a specific location!
        /// </summary>
        /// <param name="filename"></param>
        void Save(string filename);

        /// <summary>
        ///     Save the settings file to a predefined location and name <see cref="FileName" />
        /// </summary>
        void Save();

        /// <summary>
        ///     Populate the data in this object from <see cref="FileName"/>.
        /// </summary>
        void Load();

        /// <summary>
        ///     Populate the data in this object from given <paramref name="filename"/>.
        /// </summary>
        /// <param name="filename">The path to the file inwhich to load, relative pathing allowed to current executing file.</param>
        void Load(string filename);

        #region Loading

        /// <summary>
        ///     Invoked after path has been resolved and before reading. <br></br>
        ///     FileInfo can be modified now.
        /// </summary>
        void BeforeLoad(ref string destinition);
        /// <summary>
        ///     Called during loading right after <see cref="BeforeLoad"/> to decrypt the readed bytes, if <see cref="Encrypt"/> is not implemented - no reason to perform decryption.
        /// </summary>
        /// <param name="data">The data that was read from the file.</param>
        void Decrypt(ref byte[] data);
        /// <summary>
        ///     Called after <see cref="Decrypt"/>.
        /// </summary>
        /// <param name="data"></param>
        void AfterDecrypt(ref byte[] data);

        /// <summary>
        ///     Invoked after file was read and decrypted successfully right before deserializing into an object.
        /// </summary>
        /// <param name="data"></param>
        void BeforeDeserialize(ref string data);

        /// <summary>
        ///     Invoked after deserialization of <see cref="this"/> was successful.
        /// </summary>
        void AfterDeserialize();


        /// <summary>
        ///     Invoked at the end of the loading progress.
        /// </summary>
        void AfterLoad();
        #endregion

        #region Saving

        /// <summary>
        ///     Invoked before saving this object.
        /// </summary>
        void BeforeSave(ref string destinition);

        void BeforeSerialize();

        void AfterSerialize(ref string data);

        void Encrypt(ref byte[] data);

        void AfterEncrypt(ref byte[] data);

        /// <summary>
        ///     Invoked after saving this object.
        /// </summary>
        void AfterSave(string destinition);

        #endregion
    }
}