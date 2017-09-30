using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.Serialization;

namespace nucs.JsonSettings.Modulation {
    /// <summary>
    ///     A module that can be attached to a <see cref="ISocket"/>
    /// </summary>
    public abstract class Module : IDisposable {
        internal bool _isattached = false;
        private JsonSettings _socket = null;
        public virtual void Attach(JsonSettings socket) {
            if (_isattached) throw new ModularityException("The module is already attached.");
            _isattached = true;
            _socket = socket;
        }

        public virtual void Deattach(JsonSettings socket) {
            if (_socket==null) throw new ModularityException("The module is not attached.");
            _socket = null;
        }

        public void Dispose() {
            try {
                if (_socket != null)
                    Deattach(_socket);
            } catch { }
        }
    }

    /// <summary>
    ///     A class that can be attached to and deattached from with <see cref="Module"/>s.
    /// </summary>
    public interface ISocket {
        /// <summary>
        ///     Attach a module to current socket.
        /// </summary>
        void Attach(Module t);
        /// <summary>
        ///     Deattach a module from any socket it was attached to.<br></br>This is merely a shortcut to <see cref="Module.Deattach"/>.
        /// </summary>
        void Deattach(Module t);
#if NET40
        ReadOnlyCollection<Module> Modules { get; }
#else
        IReadOnlyList<Module> Modules { get; }
#endif
        bool IsAttached(Func<Module, bool> checker);
        bool IsAttachedOfType<T>() where T : Module;
        bool IsAttachedOfType(Type t);
        
    }

    public class ModularityException : Exception {
        public ModularityException() { }
        public ModularityException(string message) : base(message) { }
        public ModularityException(string message, Exception inner) : base(message, inner) { }
    }
}