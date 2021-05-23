using System;

namespace Nucs.JsonSettings.Modulation {
    /// <summary>
    ///     This attribute used to specify the default version that the loaded config must be when using <see cref="VersioningModule"/>
    /// </summary>
    /// <remarks>In-case of inheritence, it'll use the first attribute returnd which is the child's class attribute.</remarks>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class EnforcedVersionAttribute : Attribute {
        /// <summary>
        ///     The expected version of the loaded settings
        /// </summary>
        public Version Version { get; set; }
        
        /// <param name="version">Version.Parse(version)</param>
        public EnforcedVersionAttribute(string version) : this(Version.Parse(version)) { }

        public EnforcedVersionAttribute(Version version) {
            Version = version;
        }
    }
}