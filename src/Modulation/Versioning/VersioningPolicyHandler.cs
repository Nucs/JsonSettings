using System;

namespace Nucs.JsonSettings.Modulation {
    /// <summary>
    ///     Returns true when version is matching. False if invalid.
    /// </summary>
    public delegate bool VersioningPolicyHandler(Version currentVersion, Version expectedVersion);
}