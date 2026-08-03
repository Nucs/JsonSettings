#nullable enable
using System;
using System.Threading;

namespace Nucs.JsonSettings.NotifyChanges {
    /// <summary>
    ///     Opt-in marshalling of the change notifications raised by <see cref="NotifyChangesAttribute"/>
    ///     and <see cref="NotifyChangesMixinAttribute"/> onto a captured
    ///     <see cref="SynchronizationContext"/> -- so a settings object written from a background thread
    ///     still raises <c>PropertyChanged</c> on the UI thread.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The problem this solves is framework-neutral. WPF already marshals a scalar
    ///     <c>PropertyChanged</c> for you, but mutating a <em>bound</em> <c>ObservableCollection</c>
    ///     from another thread throws <see cref="NotSupportedException"/>, and other UI stacks marshal
    ///     even less. Capturing the UI thread's <see cref="SynchronizationContext"/> and posting the
    ///     raise back to it is the one mechanism every stack shares -- WPF (<c>Dispatcher</c>), WinForms,
    ///     WinUI, MAUI, Avalonia, Uno -- which is why this lives in the neutral notifications package and
    ///     takes no dependency on any of them.
    ///     </para>
    ///     <para>
    ///     Call <see cref="EnableNotificationMarshaling{T}(T)"/> <em>on the UI thread</em> (after
    ///     <c>Load</c>/<c>EnableAutosave</c>, e.g. when you set the window's <c>DataContext</c>). From
    ///     then on, a woven setter that runs on a different thread <see cref="SynchronizationContext.Post"/>s
    ///     its <c>PropertyChanged</c> (and any <see cref="NotifyChangesForAttribute"/> dependents) to the
    ///     captured context; a setter that runs on the captured thread raises inline as before. It is
    ///     stored per-instance in a weak table, so it neither changes the type nor keeps the settings
    ///     object alive, and it works the same for a notifying base, a convention class, and a mixin class.
    ///     </para>
    ///     <para>
    ///     Because the raise is <c>Post</c>ed (asynchronous), its ordering relative to the setter's own
    ///     return is not guaranteed off-thread -- do not write a handler that assumes the setter has not
    ///     yet returned. <c>PropertyChanging</c> is deliberately <em>not</em> marshalled: it must fire
    ///     before the value changes, which an async post cannot honour.
    ///     </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var settings = JsonSettings.Load&lt;AppSettings&gt;("app.json").EnableAutosave();
    /// // on the UI thread, e.g. in the view constructor:
    /// settings.EnableNotificationMarshaling();
    /// DataContext = settings;
    ///
    /// // later, on a worker thread:
    /// await Task.Run(() =&gt; settings.Items.Clear());   // PropertyChanged now arrives on the UI thread
    /// </code>
    /// </example>
    public static class NotifyMarshalingExtensions {
        /// <summary>
        ///     Captures the <see cref="SynchronizationContext"/> current on the calling thread and routes
        ///     this instance's future change notifications to it. Call on the UI thread.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
        /// <exception cref="JsonSettingsException">
        ///     No <see cref="SynchronizationContext"/> is current on the calling thread (you are not on a
        ///     UI thread). Pass one explicitly with <see cref="EnableNotificationMarshaling{T}(T, SynchronizationContext)"/>.
        /// </exception>
        public static T EnableNotificationMarshaling<T>(this T settings) where T : JsonSettings {
            if (settings is null)
                throw new ArgumentNullException(nameof(settings));

            var context = SynchronizationContext.Current;
            if (context is null)
                throw new JsonSettingsException(
                    "EnableNotificationMarshaling() must be called on a thread that has a SynchronizationContext "
                    + "(typically the UI thread), so change notifications can be marshalled back to it. None was "
                    + "current on this thread -- call it from the UI thread, or pass a context explicitly with "
                    + "EnableNotificationMarshaling(SynchronizationContext).");

            NotifyChangesRuntime.SetMarshalContext(settings, context);
            return settings;
        }

        /// <summary>
        ///     Routes this instance's future change notifications to <paramref name="context"/>. Useful
        ///     when the capturing thread is not the one holding the settings, and in tests.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> or <paramref name="context"/> is null.</exception>
        public static T EnableNotificationMarshaling<T>(this T settings, SynchronizationContext context) where T : JsonSettings {
            if (settings is null)
                throw new ArgumentNullException(nameof(settings));
            if (context is null)
                throw new ArgumentNullException(nameof(context));

            NotifyChangesRuntime.SetMarshalContext(settings, context);
            return settings;
        }

        /// <summary>
        ///     Stops marshalling this instance's change notifications; subsequent raises run inline on
        ///     whatever thread writes the property. A no-op if marshalling was never enabled.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
        public static T DisableNotificationMarshaling<T>(this T settings) where T : JsonSettings {
            if (settings is null)
                throw new ArgumentNullException(nameof(settings));

            NotifyChangesRuntime.RemoveMarshalContext(settings);
            return settings;
        }
    }
}
