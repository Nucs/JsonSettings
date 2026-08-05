using System.ComponentModel;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.NotifyChanges;

namespace Nucs.JsonSettings.Examples.UI.WinForms;

/// <summary>
///     The one settings class the WinForms demo persists. <c>[Autosave]</c> weaves every setter
///     to save the object on assignment; <c>[NotifyChangesMixin]</c> injects
///     <c>INotifyPropertyChanged</c> and weaves every setter to raise it — which is what lets a
///     <c>BindingSource</c> refresh bound controls when the SAME property is edited elsewhere
///     (the PropertyGrid, for instance) with no glue code.
///
///     The System.ComponentModel attributes below are for the PropertyGrid: it groups by
///     [Category] and shows [Description] in its help pane, so the grid reads like a real
///     settings dialog. The grid also lists the two properties inherited from JsonSettings —
///     FileName (where this object saves; editing it in the grid redirects future saves) and the
///     read-only Modulation socket.
/// </summary>
[Autosave, NotifyChangesMixin]
public sealed class AppSettings : JsonSettings {
    public override string FileName { get; set; } = "winforms-demo.json";

    [Category("Profile")]
    [Description("Shown in the window title. Bound two-way to the TextBox AND edited via the PropertyGrid — each keeps the other in sync through the woven INotifyPropertyChanged.")]
    public string DisplayName { get; set; } = "";

    [Category("Profile")]
    [Description("Free-form e-mail. Saved on every keystroke through the bound TextBox.")]
    public string Email { get; set; } = "";

    [Category("Sync")]
    [Description("Toggles the sync feature. Bound to the CheckBox.")]
    public bool EnableSync { get; set; }

    [Category("Sync")]
    [Description("Minutes between refreshes. Bound to the NumericUpDown.")]
    public int RefreshMinutes { get; set; } = 15;

    [Category("Server")]
    [Description("Written together with Port in one batched save — see the Apply button.")]
    public string Server { get; set; } = "localhost";

    [Category("Server")]
    [Description("Written together with Server in one batched save — see the Apply button.")]
    public int Port { get; set; } = 8080;

    public AppSettings() { }
    public AppSettings(string fileName) : base(fileName) { }

    private static readonly Lazy<AppSettings> _instance = new(LoadFromAppData);

    /// <summary>Process-wide, file-backed, autosaving, change-notifying settings instance.</summary>
    public static AppSettings Instance => _instance.Value;

    private static AppSettings LoadFromAppData() {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                               "JsonSettings.Examples");
        Directory.CreateDirectory(dir);
        return JsonSettings.Load<AppSettings>(Path.Combine(dir, "winforms-demo.json")).EnableAutosave();
    }
}
