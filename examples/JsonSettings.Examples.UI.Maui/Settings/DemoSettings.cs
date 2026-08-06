using System;
using System.IO;
using Microsoft.Maui.Storage;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.NotifyChanges;

namespace Nucs.JsonSettings.Examples.UI.Maui;

/// <summary>
///     The one settings class the MAUI demo persists — and, since it carries
///     <c>[NotifyChangesMixin]</c>, also the page's entire <c>BindingContext</c>. At compile
///     time AspectInjector weaves every setter twice over: <c>[Autosave]</c> makes each
///     assignment write the whole object back to <see cref="JsonSettings.FileName"/>, and the
///     mixin injects <c>INotifyPropertyChanged</c> and raises it from the same setters, which is
///     what lets the XAML <c>{Binding}</c>s below refresh with no code-behind. There is no proxy
///     and no runtime code generation — properties need not be <c>virtual</c>, and it all works
///     under Native AOT.
///
///     Marking the class <c>[Autosave]</c> is mandatory: <c>EnableAutosave()</c> throws if the
///     type carries no attribute, because an unwoven class would accept writes and silently
///     never save.
/// </summary>
[Autosave, NotifyChangesMixin]
public sealed class DemoSettings : JsonSettings {
    public override string FileName { get; set; } = "jsonsettings-maui-demo.json";

    /// <summary>Bumped once per process start, in <see cref="MainPage"/>'s constructor.</summary>
    public int LaunchCount { get; set; }

    /// <summary>
    ///     Bumped by the button. The attribute fans the notification out to the computed
    ///     <see cref="ClickLabel"/>, so the button's bound caption re-renders on every click —
    ///     the counterpart of CommunityToolkit's NotifyPropertyChangedFor, with zero MVVM
    ///     scaffolding.
    /// </summary>
    [NotifyChangesFor(nameof(ClickLabel))]
    public int ClickCount { get; set; }

    /// <summary>Computed and get-only: never saved, never woven, refreshed via the fan-out above.</summary>
    public string ClickLabel => ClickCount == 1 ? "Clicked 1 time" : $"Clicked {ClickCount} times";

    /// <summary>Free text from the entry; saved on every keystroke through the two-way binding.</summary>
    [NotifyChangesFor(nameof(NoteEchoText))]
    public string Note { get; set; } = "";

    /// <summary>Computed echo of <see cref="Note"/>, bound one-way under the entry.</summary>
    public string NoteEchoText => string.IsNullOrEmpty(Note) ? "(saved note is empty)" : $"saved note: \"{Note}\"";

    public DemoSettings() { }
    public DemoSettings(string fileName) : base(fileName) { }

    // Lazy so the file is opened on first use (from the UI thread, once MAUI is running) rather
    // than at type-load: FileSystem.AppDataDirectory is a platform service and is only meaningful
    // after app start.
    private static readonly Lazy<DemoSettings> _instance = new(LoadFromAppData);

    /// <summary>Process-wide, file-backed, autosaving, change-notifying settings instance.</summary>
    public static DemoSettings Instance => _instance.Value;

    private static DemoSettings LoadFromAppData() {
        var path = Path.Combine(FileSystem.Current.AppDataDirectory, "jsonsettings-maui-demo.json");
        // Load reads the file if present (or writes defaults if not), then EnableAutosave attaches
        // the module the woven setters call. The returned reference is the same instance, not a
        // proxy, so DemoSettings.Instance autosaves everywhere it is used.
        return JsonSettings.Load<DemoSettings>(path).EnableAutosave();
    }
}
