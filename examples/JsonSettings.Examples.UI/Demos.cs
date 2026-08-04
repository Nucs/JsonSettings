using System;
using Nucs.JsonSettings.Autosave;

namespace Nucs.JsonSettings.Examples.UI {
    /// <summary>
    ///     Loads every demo settings object once and serves as the window's <c>DataContext</c>, so
    ///     XAML reaches each integration as <c>{Binding Guards.Query}</c>, <c>{Binding Window.Title}</c>
    ///     and so on. Note what is deliberately different per object: <see cref="Proxy"/> never calls
    ///     <c>EnableAutosave()</c> (notify-only), and <see cref="App"/> is exposed as its interface via
    ///     <c>EnableIAutosave</c>.
    /// </summary>
    public sealed class Demos : IDisposable {
        public WindowSettings Window { get; }
        public GuardSettings Guards { get; }
        public ProfileSettings Profile { get; }
        public QuickSettings Quick { get; }
        public ProxySettings Proxy { get; }
        public LibrarySettings Library { get; }
        public WorkerSettings Worker { get; }
        public IAppSettings App { get; }

        private readonly AppSettings _app; //the concrete instance behind App, kept for Dispose

        public Demos() {
            Window = JsonSettings.Load<WindowSettings>("ui.window.json").EnableAutosave();
            Guards = JsonSettings.Load<GuardSettings>("ui.guards.json").EnableAutosave();
            Profile = JsonSettings.Load<ProfileSettings>("ui.profile.json").EnableAutosave();
            Quick = JsonSettings.Load<QuickSettings>("ui.quick.json").EnableAutosave();
            Proxy = JsonSettings.Load<ProxySettings>("ui.proxy.json"); //no EnableAutosave: notify-only
            Library = JsonSettings.Load<LibrarySettings>("ui.library.json").EnableAutosave();
            Worker = JsonSettings.Load<WorkerSettings>("ui.worker.json").EnableAutosave();

            _app = JsonSettings.Load<AppSettings>("ui.app.json");
            App = _app.EnableIAutosave<AppSettings, IAppSettings>();
        }

        /// <summary>
        ///     Disposing a settings object detaches its modules — including the
        ///     <c>NotificationBinder</c>'s nested-collection subscriptions — so nothing keeps saving
        ///     through, or keeps alive, a closed window's settings.
        /// </summary>
        public void Dispose() {
            Window.Dispose();
            Guards.Dispose();
            Profile.Dispose();
            Quick.Dispose();
            Proxy.Dispose();
            Library.Dispose();
            Worker.Dispose();
            _app.Dispose();
        }
    }
}
