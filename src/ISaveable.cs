namespace nucs.Settings {
    public interface ISaveable {
        /// <summary>
        ///     The filename for this ISaveable
        /// </summary>
        string FileName { get; set; }

        /// <summary>
        ///     Save the settings file to a specific location!
        /// </summary>
        /// <param name="filename"></param>
        void Save(string filename);

        /// <summary>
        ///     Save the settings file to a predefined location <see cref="FileName" />
        /// </summary>
        void Save();

        /// <summary>
        ///     Invoked after loading to this new object. <br></br>
        ///     Can be used to validate the inputted data.
        /// </summary>
        void AfterLoad();

        /// <summary>
        ///     Invoked before saving this object.
        /// </summary>
        void BeforeSave();

        /// <summary>
        ///     Invoked after saving this object.
        /// </summary>
        void AfterSave();
    }
}