namespace Nucs.JsonSettings.Modulation {
    /// <summary>
    ///     In a case of an invalid action, what should be the course.
    /// </summary>
    public enum VersioningResultAction {
        /// <summary>
        ///     Throw an <see cref="InvalidVersionException"/> on loading.
        /// </summary>
        Throw,

        /// <summary>
        ///     Will append the version to the end of the file's name and load the default settings.
        /// </summary>
        RenameAndReload,

        /// <summary>
        ///     Incase of invalid version, default settings will be loaded without touching the existing file until next save.
        /// </summary>
        LoadDefaultSilently,

        /// <summary>
        ///     Incase of invalid version, default settings will be loaded and saved to disk immediately.
        /// </summary>
        OverrideDefault,
    }
}