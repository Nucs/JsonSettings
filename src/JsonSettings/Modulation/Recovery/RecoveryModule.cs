using System;
using System.Threading;
using Newtonsoft.Json;

namespace Nucs.JsonSettings.Modulation.Recovery {
    /// <summary>
    ///     Takes care of recovering from errors when trying to parse the json file
    /// </summary>
    public class RecoveryModule : Module {
        public RecoveryAction RecoveryAction { get; set; }
        protected string loadedPath; //the attempted load path
        //Reentrancy guard for event handling. ++/-- is a read-modify-write, so it goes through
        //Interlocked to stay atomic under concurrent load of one instance; the field is therefore NOT
        //`volatile`, because passing a volatile field by ref to Interlocked warns CS0420 (the ref would
        //not be treated as volatile anyway).
        protected int internalCalls;

        /// <summary>
        ///     The parameters that'll be passed to the constructor of JsonSettings that were passed.
        /// </summary>
        public object[]? ConstructingParameters { get; set; } = Array.Empty<object>();

        public RecoveryModule(RecoveryAction recoveryAction) {
            RecoveryAction = recoveryAction;
        }

        public override void Attach(JsonSettings socket) {
            base.Attach(socket);
            socket.BeforeLoad += SocketOnBeforeLoad;
            socket.TryingRecover += SocketOnTryingRecover;
        }

        public override void Deattach(JsonSettings socket) {
            base.Deattach(socket);
            socket.BeforeLoad -= SocketOnBeforeLoad;
            socket.TryingRecover -= SocketOnTryingRecover;
        }

        protected virtual void SocketOnBeforeLoad(JsonSettings sender, ref string destinition) {
            loadedPath = destinition;
        }

        protected virtual void SocketOnTryingRecover(JsonSettings sender, string filename, JsonException? jsonException, ref bool recovered, ref bool handled) {
            HandleRecovery(sender, RecoveryAction, ref recovered, ref handled);
        }

        protected virtual void HandleRecovery(JsonSettings sender, RecoveryAction action, ref bool recovered, ref bool handled) {
            //versions mismatch, handle
            if (recovered || handled)
                return;

            switch (action) {
                case RecoveryAction.Throw: throw new JsonSettingsRecoveryException($"Loading {sender._childtype.Name} settings{(sender is IVersionable v ? $" version '{v.Version}'" : "")}");
                case RecoveryAction.RenameAndLoadDefault: {
                    if (loadedPath is null)
                        throw new ArgumentNullException(nameof(loadedPath));

                    //Sender may or may not be versionable: pass the version label when it is (".{version}-{n}"),
                    //or null when it isn't so only the archive counter is stamped (".{n}").
                    var cleanName = VersioningModule.RenameToArchive(loadedPath, sender is IVersionable versionable ? (versionable.Version?.ToString() ?? string.Empty) : null);

                    //save
                    Interlocked.Increment(ref internalCalls);
                    try {
                        sender.FileName = loadedPath = cleanName;
                        sender.LoadDefault(ConstructingParameters);
                        sender.Save();
                        recovered = true;
                        handled = true;
                    } finally {
                        Interlocked.Decrement(ref internalCalls);
                    }

                    return;
                }
                case RecoveryAction.LoadDefault:
                    sender.LoadDefault(ConstructingParameters);
                    recovered = true;
                    handled = true;
                    return;
                case RecoveryAction.LoadDefaultAndSave:
                    sender.LoadDefault(ConstructingParameters);
                    sender.Save();
                    recovered = true;
                    handled = true;
                    return;

                default: throw new ArgumentOutOfRangeException();
            }
        }
    }
}