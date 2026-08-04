using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.NotifyChanges;

namespace Nucs.JsonSettings.Examples.UI {
    /// <summary>
    ///     One window, one tab per integration. All the interesting behaviour lives in the settings
    ///     classes and their attributes; this code-behind only wires counters, the activity log and
    ///     the button clicks that exercise each path. Nothing here calls OnPropertyChanged — every
    ///     notification in the log was raised by the woven setters.
    /// </summary>
    public partial class MainWindow : Window {
        private readonly Demos _demos;
        private readonly ObservableCollection<string> _log = new ObservableCollection<string>();
        private readonly Dictionary<JsonSettings, string> _lastSavedPath = new Dictionary<JsonSettings, string>();

        private SuspendAutosave? _windowSuspension; //a readonly struct, so the field is nullable to model "no scope open"
        private int _queryNotifyCount, _tickNotifyCount, _filterNotifyCount, _searchNotifyCount, _proxyNotifyCount;
        private int _queryWrites, _filterWrites, _bookCounter;

        public MainWindow() {
            _demos = new Demos();
            //DataContext before InitializeComponent, so the chrome bindings (Left/Top/Width/Height/
            //Title) restore the saved geometry as the XAML is parsed rather than after a flicker.
            DataContext = _demos;
            InitializeComponent();

            LogList.ItemsSource = _log;

            //The mixin injected the interface at compile time; the (object) hop is required because
            //the compiler cannot see an interface on a sealed class that never declares it.
            MixinInterfaceText.Text = ((object) _demos.Quick is INotifyPropertyChanged).ToString();

            //A write through the interface goes through the same woven setter: notifies + autosaves.
            _demos.App.LaunchCount++;

            WirePreview();
            WireSaveCounters();
            WireActivityLog();

            Log("ready — every counter and log line below is driven by woven setters, not hand-written OnPropertyChanged calls");
        }

        #region plumbing

        private void OnUi(Action action) {
            if (Dispatcher.CheckAccess())
                action();
            else
                Dispatcher.BeginInvoke(action);
        }

        private void Log(string message) => OnUi(() => AppendLog(message));

        private void AppendLog(string message) {
            _log.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {message}");
            while (_log.Count > 250)
                _log.RemoveAt(_log.Count - 1);
        }

        /// <summary>Evaluated on the raising thread — call it before marshalling into the log.</summary>
        private string ThreadStamp() {
            return Dispatcher.CheckAccess()
                ? $"thread {Environment.CurrentManagedThreadId}, UI"
                : $"thread {Environment.CurrentManagedThreadId}, background";
        }

        private static string ResolvePath(JsonSettings settings) {
            //Load/Save resolve a relative FileName against the executing directory; mirror that for
            //the preview, preferring the exact destination the last AfterSave reported.
            return Path.IsPathRooted(settings.FileName)
                ? settings.FileName
                : Path.Combine(AppContext.BaseDirectory, settings.FileName);
        }

        #endregion

        #region wiring

        private sealed class PreviewChoice {
            public string Label;
            public JsonSettings Settings;
            public override string ToString() => Label;
        }

        private void WirePreview() {
            PreviewFileCombo.ItemsSource = new[] {
                new PreviewChoice { Label = "ui.window.json", Settings = _demos.Window },
                new PreviewChoice { Label = "ui.guards.json", Settings = _demos.Guards },
                new PreviewChoice { Label = "ui.profile.json", Settings = _demos.Profile },
                new PreviewChoice { Label = "ui.library.json", Settings = _demos.Library },
                new PreviewChoice { Label = "ui.quick.json", Settings = _demos.Quick },
                new PreviewChoice { Label = "ui.proxy.json", Settings = _demos.Proxy },
                new PreviewChoice { Label = "ui.worker.json", Settings = _demos.Worker },
                new PreviewChoice { Label = "ui.app.json", Settings = (AppSettings) _demos.App }, //same instance, interface off
            };
            PreviewFileCombo.SelectedIndex = 0;
        }

        /// <summary>
        ///     Counts <c>AfterSave</c> per object. AfterSave fires on whatever thread wrote the
        ///     property (autosave saves on the writing thread), so everything UI-bound is marshalled.
        /// </summary>
        private void TrackSaves(JsonSettings settings, TextBlock counter, string logAs) {
            int count = 0;
            settings.AfterSave += (sender, destination) => {
                var stamp = ThreadStamp();
                OnUi(() => {
                    count++;
                    if (counter != null)
                        counter.Text = count.ToString();
                    _lastSavedPath[settings] = destination;
                    if (ReferenceEquals((PreviewFileCombo.SelectedItem as PreviewChoice)?.Settings, settings))
                        RefreshPreview();
                    if (logAs != null)
                        AppendLog($"saved {logAs}  [{stamp}]");
                });
            };
        }

        private void WireSaveCounters() {
            TrackSaves(_demos.Window, WindowSavesText, null); //not logged: dragging would flood the log
            TrackSaves(_demos.Guards, GuardsSavesText, "ui.guards.json");
            TrackSaves(_demos.Profile, ProfileSavesText, "ui.profile.json");
            TrackSaves(_demos.Quick, QuickSavesText, "ui.quick.json");
            TrackSaves(_demos.Proxy, ProxySavesText, "ui.proxy.json");
            TrackSaves(_demos.Library, LibrarySavesText, null); //not logged: the slider burst would flood the log
            TrackSaves(_demos.Worker, WorkerSavesText, "ui.worker.json");
            TrackSaves((AppSettings) _demos.App, null, "ui.app.json");
        }

        private void WireActivityLog() {
            //Guards: per-property notification counters. The save counter next to them is what makes
            //the guard visible — a suppressed notification still saves.
            _demos.Guards.PropertyChanged += (s, e) => {
                var line = $"Guards.{e.PropertyName} changed  [{ThreadStamp()}]";
                OnUi(() => {
                    switch (e.PropertyName) {
                        case nameof(GuardSettings.Query): QueryNotifies.Text = (++_queryNotifyCount).ToString(); break;
                        case nameof(GuardSettings.RefreshTick): TickNotifies.Text = (++_tickNotifyCount).ToString(); break;
                        case nameof(GuardSettings.Filter): FilterNotifies.Text = (++_filterNotifyCount).ToString(); break;
                    }
                    AppendLog(line);
                });
            };

            //Profile: the changing edge runs before the assignment, so reading the property here
            //still sees the OLD value — the log shows both edges of every change, and the
            //[NotifyChangesFor] fan-out appears as a second changed line for FullName.
            _demos.Profile.PropertyChanging += (s, e) => {
                var old = s.GetType().GetProperty(e.PropertyName)?.GetValue(s);
                Log($"Profile.{e.PropertyName} changing (old value: '{old}')  [{ThreadStamp()}]");
            };
            _demos.Profile.PropertyChanged += (s, e) => {
                var line = $"Profile.{e.PropertyName} changed  [{ThreadStamp()}]";
                OnUi(() => {
                    if (e.PropertyName == nameof(ProfileSettings.SearchText))
                        SearchNotifies.Text = (++_searchNotifyCount).ToString();
                    AppendLog(line);
                });
            };

            //Quick: the event only exists because the mixin injected it.
            ((INotifyPropertyChanged) (object) _demos.Quick).PropertyChanged +=
                (s, e) => Log($"Quick.{e.PropertyName} changed (raised by the injected event)  [{ThreadStamp()}]");

            //Proxy: raised through BindableSettings.RaisePropertyChanged, found by convention.
            _demos.Proxy.PropertyChanged += (s, e) => {
                var line = $"Proxy.{e.PropertyName} changed (raised via RaisePropertyChanged by convention)  [{ThreadStamp()}]";
                OnUi(() => {
                    ProxyNotifies.Text = (++_proxyNotifyCount).ToString();
                    AppendLog(line);
                });
            };

            //Library: collection replacement notifies; Zoom is skipped because the slider drag would
            //flood the log (its burst is already visible on the save counter).
            _demos.Library.PropertyChanged += (s, e) => {
                if (e.PropertyName != nameof(LibrarySettings.Zoom))
                    Log($"Library.{e.PropertyName} changed — the NotificationBinder rebinds the new collection  [{ThreadStamp()}]");
            };

            //Worker: both edges with thread stamps. Changing is never marshalled (it must precede
            //the write); changed follows the marshalling toggle.
            _demos.Worker.PropertyChanging += (s, e) => Log($"Worker.{e.PropertyName} changing  [{ThreadStamp()}]");
            _demos.Worker.PropertyChanged += (s, e) => Log($"Worker.{e.PropertyName} changed  [{ThreadStamp()}]");

            //App: written through the IAppSettings interface, same woven instance.
            _demos.App.PropertyChanged += (s, e) => Log($"App.{e.PropertyName} changed (via the IAppSettings seam)  [{ThreadStamp()}]");
        }

        #endregion

        #region window & autosave tab

        private void SuspendWindow_Checked(object sender, RoutedEventArgs e) {
            _windowSuspension = _demos.Window.SuspendAutosave();
            Log("SuspendAutosave() opened on ui.window.json — drag the window: bindings stay live, no saves");
        }

        private void SuspendWindow_Unchecked(object sender, RoutedEventArgs e) {
            _windowSuspension?.Dispose();
            _windowSuspension = null;
            Log("SuspendAutosave() disposed — the whole burst committed as one save (none if nothing changed)");
        }

        #endregion

        #region guards tab

        private void QueryNew_Click(object sender, RoutedEventArgs e) {
            _demos.Guards.Query = $"query #{++_queryWrites}";
        }

        private void QuerySame_Click(object sender, RoutedEventArgs e) {
            _demos.Guards.Query = _demos.Guards.Query; //saves, but OnlyChanged suppresses the notification
        }

        private void TickSame_Click(object sender, RoutedEventArgs e) {
            _demos.Guards.RefreshTick = _demos.Guards.RefreshTick; //no-op write, Always still notifies
        }

        private void FilterNew_Click(object sender, RoutedEventArgs e) {
            _demos.Guards.Filter = $"filter #{++_filterWrites}";
        }

        private void FilterNull_Click(object sender, RoutedEventArgs e) {
            _demos.Guards.Filter = null; //saves null to disk, SkipNullOrDefault raises nothing
        }

        #endregion

        #region computed & opt-outs tab

        private void Stamp_Click(object sender, RoutedEventArgs e) {
            _demos.Profile.LastSavedBy = $"{Environment.UserName} at {DateTime.Now:T}";
            Log($"Profile.LastSavedBy = '{_demos.Profile.LastSavedBy}' — saved with no notification, so its binding stays stale");
        }

        #endregion

        #region collections & bursts tab

        private void AddBook_Click(object sender, RoutedEventArgs e) {
            _demos.Library.Books.Add($"Book #{++_bookCounter}");
            Log("Books.Add(...) — nested CollectionChanged triggered a save");
        }

        private void RemoveBook_Click(object sender, RoutedEventArgs e) {
            if (BooksList.SelectedItem is string book) {
                _demos.Library.Books.Remove(book);
                Log("Books.Remove(...) — nested CollectionChanged triggered a save");
            }
        }

        private void ReplaceBooks_Click(object sender, RoutedEventArgs e) {
            _demos.Library.Books = new ObservableCollection<string> { "A fresh shelf" };
            Log("Books REPLACED — saved once, and the binder rebound the new instance so future adds keep saving");
        }

        private void AddBatch_Click(object sender, RoutedEventArgs e) {
            using (_demos.Library.SuspendAutosave()) {
                for (int i = 0; i < 20; i++)
                    _demos.Library.Books.Add($"Batch book #{++_bookCounter}");
            } //one save commits here
            Log("20 adds inside SuspendAutosave() — the list updated 20 times, the file was written once");
        }

        #endregion

        #region no base & conventions tab

        private void ProxySave_Click(object sender, RoutedEventArgs e) {
            _demos.Proxy.Save(); //notify-only class: persisting is explicit
        }

        #endregion

        #region threading tab

        private void MarshalToggle_Checked(object sender, RoutedEventArgs e) {
            _demos.Worker.EnableNotificationMarshaling(); //captures this (UI) thread's SynchronizationContext
            Log("EnableNotificationMarshaling() — Worker's changed-notifications now post to the UI thread");
        }

        private void MarshalToggle_Unchecked(object sender, RoutedEventArgs e) {
            _demos.Worker.DisableNotificationMarshaling();
            Log("DisableNotificationMarshaling() — Worker's notifications now run inline on the writing thread");
        }

        private async void RunJob_Click(object sender, RoutedEventArgs e) {
            RunJobButton.IsEnabled = false;
            try {
                await Task.Run(() => {
                    _demos.Worker.Status = "working...";
                    for (int i = 0; i <= 100; i += 20) {
                        _demos.Worker.Progress = i; //woven setter: notifies (marshalled or not) and autosaves, off-thread
                        Thread.Sleep(150);
                    }
                    _demos.Worker.Status = $"done at {DateTime.Now:T}";
                });
            } finally {
                RunJobButton.IsEnabled = true;
            }
        }

        private async void ReplaceResults_Click(object sender, RoutedEventArgs e) {
            await Task.Run(() => {
                var results = new ObservableCollection<string>();
                for (int i = 1; i <= 5; i++)
                    results.Add($"result {i} computed at {DateTime.Now:T}");
                _demos.Worker.Results = results; //a replacement, not an in-place mutation — see the tab note
            });
        }

        #endregion

        #region shared bottom pane

        private void PreviewFileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshPreview();

        private void RefreshPreview() {
            if (!(PreviewFileCombo?.SelectedItem is PreviewChoice choice) || PreviewText == null)
                return;
            var path = _lastSavedPath.TryGetValue(choice.Settings, out var saved) ? saved : ResolvePath(choice.Settings);
            try {
                PreviewText.Text = File.Exists(path) ? File.ReadAllText(path) : "(no file on disk yet)";
            } catch (IOException) {
                //a save is mid-write; the AfterSave that follows refreshes the preview anyway
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e) {
            Process.Start(new ProcessStartInfo("explorer.exe", AppContext.BaseDirectory) { UseShellExecute = true });
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e) => _log.Clear();

        #endregion

        protected override void OnClosed(EventArgs e) {
            _windowSuspension?.Dispose();
            _demos.Dispose(); //unbinds autosave, including the nested-collection subscriptions
            base.OnClosed(e);
        }
    }
}
