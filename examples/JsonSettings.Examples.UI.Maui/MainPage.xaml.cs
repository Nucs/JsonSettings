using Nucs.JsonSettings.Autosave;

namespace Nucs.JsonSettings.Examples.UI.Maui;

public partial class MainPage : ContentPage {
    private readonly DemoSettings _settings = DemoSettings.Instance;
    private int _saveCount;

    public MainPage() {
        InitializeComponent();

        // The settings object IS the view model: [NotifyChangesMixin] injected
        // INotifyPropertyChanged into it at build time, so every {Binding} in the XAML
        // refreshes on its own. The old version of this page updated each label by hand.
        BindingContext = _settings;

        // A woven setter: this single assignment writes the file — and, through the mixin,
        // notifies the LaunchCount binding that just rendered.
        _settings.LaunchCount++;

        // AfterSave is a plain settings event; all writes on this page happen on the UI
        // thread, so it is safe to touch controls directly. It makes the batching demo
        // measurable: watch the counter go up by ONE when Reset writes TWO properties.
        _settings.AfterSave += (_, destination) =>
            SaveStatus.Text = $"save #{++_saveCount}";
    }

    private void OnCounterClicked(object? sender, EventArgs e) {
        _settings.ClickCount++;            // woven setter -> autosaves, notifies ClickLabel
        SemanticScreenReader.Announce(_settings.ClickLabel);
    }

    private void OnReset(object? sender, EventArgs e) {
        // Two woven setters, ONE file save: SuspendAutosave() batches until the scope closes.
        // Both bindings still refresh immediately — notifications are not suspended, saving is.
        using (_settings.SuspendAutosave()) {
            _settings.ClickCount = 0;
            _settings.Note = "";
        }
    }
}
