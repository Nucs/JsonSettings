namespace Nucs.JsonSettings.Examples.UI.Maui;

public partial class MainPage : ContentPage {
    private readonly DemoSettings _settings = DemoSettings.Instance;

    public MainPage() {
        InitializeComponent();

        // A woven setter: this single assignment writes the file. No Save() call anywhere.
        _settings.LaunchCount++;

        LaunchLabel.Text = $"You have launched this app {_settings.LaunchCount} time(s).";
        CounterBtn.Text = ClickText(_settings.ClickCount);
        NoteEntry.Text = _settings.Note;   // restored from disk
        NoteEcho.Text = EchoText(_settings.Note);
        PathLabel.Text = $"file: {_settings.FileName}";
    }

    private void OnCounterClicked(object? sender, EventArgs e) {
        _settings.ClickCount++;            // woven setter -> autosaves
        CounterBtn.Text = ClickText(_settings.ClickCount);
        SemanticScreenReader.Announce(CounterBtn.Text);
    }

    private void OnNoteChanged(object? sender, TextChangedEventArgs e) {
        _settings.Note = e.NewTextValue;   // woven setter -> autosaves on every keystroke
        NoteEcho.Text = EchoText(_settings.Note);
    }

    private static string ClickText(int n) => n == 1 ? "Clicked 1 time" : $"Clicked {n} times";

    private static string EchoText(string note) =>
        string.IsNullOrEmpty(note) ? "(saved note is empty)" : $"saved note: \"{note}\"";
}
