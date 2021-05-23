namespace Nucs.JsonSettings.Modulation {
    /// <summary>
    ///     In a case of an invalid action, what should be the course.
    /// </summary>
    public enum VersioningResultAction {
        /// <summary>
        ///     Ignore it.
        /// </summary>
        DoNothing,
        
        /// <summary>
        ///     Throw an <see cref="InvalidVersionException"/> on loading.
        /// </summary>
        Throw,

        /// <summary>
        ///     Will append the version to the end of the faulty file's name and load the default settings and save to disk.<br/>
        ///     i.e. 'myfile.json' versioned 1.0.0.5 will be renamed to myfile.1.0.0.5.json if it fails on version parsing.
        /// </summary>
        RenameAndLoadDefault,

        /// <summary>
        ///     Incase of invalid version, default settings will be loaded without touching the existing file until next save.
        /// </summary>
        LoadDefault,

        /// <summary>
        ///     Incase of invalid version, default settings will be loaded and saved to disk immediately.
        /// </summary>
        LoadDefaultAndSave,
    }
}