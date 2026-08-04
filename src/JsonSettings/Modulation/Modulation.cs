using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace Nucs.JsonSettings.Modulation {
    /// <summary>
    ///     A subclass to manage modulation of a JsonSettings
    /// </summary>
    public class ModuleSocket : ISocket, IDisposable {
        private JsonSettings _settings { get; set; }

        protected readonly List<Module> _modules = new List<Module>();

        public ModuleSocket(JsonSettings settings) {
            _settings = settings;
        }

        // ReSharper disable once InconsistentlySynchronizedField
        public IReadOnlyList<Module> Modules => _modules;

        public bool IsAttached(Func<Module, bool> checker) {
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            var len = Modules.Count;
            for (int i = 0; i < len; i++) {
                if (checker(Modules[i]))
                    return true;
            }

            return false;
        }

        public T GetModule<T>() where T : Module {
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            var len = Modules.Count;
            for (int i = 0; i < len; i++) {
                if (Modules[i] is T t)
                    return t;
            }

            throw new ModularityException($"Module of type {typeof(T).Name} was not found.");
        }

        public IEnumerable<T> GetModules<T>() where T : Module {
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            var len = Modules.Count;
            for (int i = 0; i < len; i++) {
                if (Modules[i] is T t)
                    yield return t;
            }
        }

        public bool IsAttachedOfType<T>() where T : Module {
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            var len = Modules.Count;
            for (int i = 0; i < len; i++) {
                if (Modules[i] is T)
                    return true;
            }

            return false;
        }

        public bool IsAttachedOfType(Type t) {
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            var len = Modules.Count;
            for (int i = 0; i < len; i++) {
                if (Modules[i].GetType() == t)
                    return true;
            }

            return false;
        }

        public void Attach(Module t) {
            if (Volatile.Read(ref _isdisposed) != 0)
                throw new ObjectDisposedException("Can't attach, this object is already disposed.");
            t.Attach(_settings);
            lock (_modules)
                _modules.Add(t);
        }

        public void Deattach(Module t) {
            t.Deattach(_settings);
            lock (_modules)
                _modules.Remove(t);
        }

        /// <summary>
        ///     Will invoke attach to a freshly new object of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">A module class</typeparam>
        /// <param name="args">The arguments that'll be passed to the constructor</param>
        public T Attach<T>(params object[] args) where T : Module {
            var t = (Module) Activator.CreateInstance(typeof(T), args);
            Attach(t);
            return (T) t;
        }

        //0 = live, 1 = disposed. int (not bool) so the check-and-set is a single atomic
        //Interlocked.CompareExchange: a concurrent or repeat Dispose() must not tear down -- and
        //double-Dispose every module -- a second time. Dispose is fire-and-forget, so a loser just
        //returns; no lock/wait is needed here (unlike the configure guard, which callers must wait on).
        private int _isdisposed;

        public void Dispose() {
            if (Interlocked.CompareExchange(ref _isdisposed, 1, 0) != 0)
                return;
            foreach (var module in _modules.ToArray()) {
                module.Dispose();
            }

            _settings = null;
        }
    }
}