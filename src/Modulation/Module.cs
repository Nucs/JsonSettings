using System;
using System.Collections.ObjectModel;
using System.Runtime.Serialization;

namespace Nucs.JsonSettings.Modulation {
    /// <summary>
    ///     A module that can be attached to a <see cref="ISocket"/>
    /// </summary>
    public abstract class Module : IDisposable {
        internal bool _isattached = false;
        /// <summary>
        ///     The socket this Module is attached to. This is set when calling <see cref="Attach"/>
        /// </summary>
        protected WeakReference<JsonSettings>? Socket { get; private set; }

        public virtual void Attach(JsonSettings socket) {
            if (socket == null) throw new ArgumentNullException(nameof(socket));
            if (_isattached) throw new ModularityException("The module is already attached.");
            Socket = new WeakReference<JsonSettings>(socket);
            _isattached = true;
        }

        public virtual void Deattach(JsonSettings socket) {
            if (Socket == null) throw new ModularityException("The module is not attached.");
            Socket = null;
        }

        #region IDisposable

        protected virtual void Dispose(bool disposing) {
            if (disposing) {
                try {
                    if (Socket != null && Socket.TryGetTarget(out var target))
                        Deattach(target);
                } catch (Exception) {
                    //swallow
                }
            }
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}