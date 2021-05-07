namespace Nucs.JsonSettings.Modulation.Recovery {
    /// <summary>
    ///     In a case of an invalid action, what should be the course.
    /// </summary>
    public enum RecoveryAction {
        /// <summary>
        ///     Throw an <see cref="JsonSettingsRecoveryException"/> on loading.
        /// </summary>
        Throw,

        /// <summary>
        ///     Will append the version to the end of the file's name and load the default settings.<br/>
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