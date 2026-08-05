using System;
using System.IO;
using Microsoft.Maui.Storage;
using Nucs.JsonSettings.Autosave;

namespace Nucs.JsonSettings.Examples.UI.Maui;

/// <summary>
///     The one settings class the MAUI demo persists. <c>[Autosave]</c> is what makes it work:
///     at compile time AspectInjector weaves every setter of this class so that each assignment
///     writes the whole object back to <see cref="JsonSettings.FileName"/>. There is no proxy and
///     no runtime code generation — which is also why the properties need not be <c>virtual</c>
///     any more (that requirement belonged to the old Castle.DynamicProxy implementation) and why
///     it works under Native AOT.
///
///     Marking the class is mandatory: <c>EnableAutosave()</c> throws if the type carries no
///     <c>[Autosave]</c>, because an unwoven class would accept writes and silently never save.
/// </summary>
[Autosave]
public sealed class DemoSettings : JsonSettings {
    public override string FileName { get; set; } = "jsonsettings-maui-demo.json";

    /// <summary>Bumped once per process start, in <see cref="MainPage"/>'s constructor.</summary>
    public int LaunchCount { get; set; }

    /// <summary>Bumped by the button; proves a setter commit round-trips across launches.</summary>
    public int ClickCount { get; set; }

    /// <summary>Free text from the entry; saved on every keystroke.</summary>
    public string Note { get; set; } = "";

    public DemoSettings() { }
    public DemoSettings(string fileName) : base(fileName) { }

    // Lazy so the file is opened on first use (from the UI thread, once MAUI is running) rather
    // than at type-load: FileSystem.AppDataDirectory is a platform service and is only meaningful
    // after app start.
    private static readonly Lazy<DemoSettings> _instance = new(LoadFromAppData);

    /// <summary>Process-wide, file-backed, autosaving settings instance.</summary>
    public static DemoSettings Instance => _instance.Value;

    // Named LoadFromAppData (not Load) so it does not hide the inherited JsonSettings.Load().
    private static DemoSettings LoadFromAppData() {
        var path = Path.Combine(FileSystem.Current.AppDataDirectory, "jsonsettings-maui-demo.json");
        // Load reads the file if present (or writes defaults if not), then EnableAutosave attaches
        // the module the woven setters call. The returned reference is the same instance, not a
        // proxy, so DemoSettings.Instance autosaves everywhere it is used.
        return JsonSettings.Load<DemoSettings>(path).EnableAutosave();
    }
}
