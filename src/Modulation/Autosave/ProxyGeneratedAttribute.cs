using System;

namespace Nucs.JsonSettings.Autosave {
    /// <summary>Distinguishes a proxy-generated element from a user-generated element. This class cannot be inherited.</summary>
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    public sealed class ProxyGeneratedAttribute : Attribute { }
}