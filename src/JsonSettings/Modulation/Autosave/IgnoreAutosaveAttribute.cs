using System;

namespace Nucs.JsonSettings.Autosave {
    
    /// <summary>
    ///     Given Property will not be monitored for changes and autosave.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class IgnoreAutosaveAttribute : Attribute { }
}