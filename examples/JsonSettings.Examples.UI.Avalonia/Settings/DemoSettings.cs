using System.Collections.ObjectModel;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.NotifyChanges;

namespace Nucs.JsonSettings.Examples.UI.Avalonia;

/// <summary>
///     The one settings class the Avalonia demo persists — and also its entire "view model".
///     <c>[Autosave]</c> weaves every setter to save the object on assignment;
///     <c>[NotifyChangesMixin]</c> injects <c>INotifyPropertyChanged</c>/<c>INotifyPropertyChanging</c>
///     and weaves every setter to raise them — no base class, no hand-written
///     <c>OnPropertyChanged</c>, and nothing generated at runtime. Avalonia's compiled bindings
///     find the injected interface on the instance at runtime and subscribe like for any other
///     observable object, even though this source file never declares it.
/// </summary>
[Autosave, NotifyChangesMixin]
public sealed class DemoSettings : JsonSettings {
    public override string FileName { get; set; } = "avalonia-demo.json";

    /// <summary>
    ///     Two-way bound to a TextBox. The attribute fans the notification out to
    ///     <see cref="Greeting"/> so the computed, get-only property refreshes its binding on
    ///     every keystroke — the counterpart of CommunityToolkit's NotifyPropertyChangedFor.
    /// </summary>
    [NotifyChangesFor(nameof(Greeting))]
    public string Name { get; set; } = "";

    /// <summary>Computed and get-only: never saved, never woven, refreshed via the fan-out above.</summary>
    public string Greeting => string.IsNullOrWhiteSpace(Name) ? "Hello, stranger." : $"Hello, {Name}!";

    /// <summary>Two-way bound to a ToggleSwitch; App.axaml.cs listens and swaps the theme live.</summary>
    public bool DarkMode { get; set; }

    /// <summary>
    ///     Incremented from a background thread by the demo button. With
    ///     <c>EnableNotificationMarshaling()</c> the PropertyChanged it raises is posted back to
    ///     the UI thread, so the bound TextBlock updates legally.
    /// </summary>
    public int Counter { get; set; }

    /// <summary>
    ///     A nested observable collection: EnableAutosave() also binds
    ///     INotifyCollectionChanged properties, so Add/Remove persist without any setter running.
    /// </summary>
    public ObservableCollection<string> Tags { get; set; } = new();

    /// <summary>Window bounds, written in one batch on close under SuspendAutosave().</summary>
    public double WindowWidth { get; set; } = 560;
    public double WindowHeight { get; set; } = 680;

    public DemoSettings() { }
    public DemoSettings(string fileName) : base(fileName) { }

    // Lazy so the file is first opened from application code once the framework is up, not at
    // type-load. ApplicationData maps to %APPDATA% on Windows and ~/.config on Linux/macOS, so
    // the same line is the cross-platform path story.
    private static readonly Lazy<DemoSettings> _instance = new(LoadFromAppData);

    /// <summary>Process-wide, file-backed, autosaving, change-notifying settings instance.</summary>
    public static DemoSettings Instance => _instance.Value;

    private static DemoSettings LoadFromAppData() {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                               "JsonSettings.Examples");
        Directory.CreateDirectory(dir);
        return JsonSettings.Load<DemoSettings>(Path.Combine(dir, "avalonia-demo.json")).EnableAutosave();
    }
}
