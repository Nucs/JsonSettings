using System;
using System.IO;
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

                    //parse current name
                    var versionMatch = VersioningModule.VersionMatcher.Match(loadedPath);
                    //Groups[2] is the "-<n>" archive counter and only participates when the name already
                    //carries one; a bare "name.1.2.3.4.json" matches through the regex's lookahead branch
                    //with Groups[2] empty, so guard on its Success rather than the match's. int.Parse("")
                    //would otherwise throw a FormatException that escapes Load as a non-JsonSettingsException.
                    int fileVersion = versionMatch.Success && versionMatch.Groups[2].Success ? int.Parse(versionMatch.Groups[2].Value) + 1 : 0;
                    var cleanName = loadedPath;
                    if (!string.IsNullOrEmpty(versionMatch.Groups[0].Value))
                        cleanName = cleanName.Replace(versionMatch.Groups[0].Value, "");
                    var lastIdx = cleanName.LastIndexOf('.');
                    if (lastIdx == -1)
                        //cleanName, not loadedPath: cleanName is the shorter, version-stripped string that
                        //Insert below indexes into, so loadedPath.Length could point past its end.
                        lastIdx = cleanName.Length;

                    //figure naming of existing and rename
                    string newFileName = cleanName;
                    if (File.Exists(newFileName)) {
                        do {
                            newFileName = cleanName.Insert(lastIdx, $".{(sender is IVersionable versionable ? $"{versionable.Version}-{fileVersion++}" : fileVersion++)}");
                        } while (File.Exists(newFileName));

                        try {
                            File.Move(cleanName, newFileName);
                        } catch (Exception) {
                            // swallow
                            try {
                                File.Delete(loadedPath);
                            } catch (Exception) {
                                // swallow
                            }
                        }
                    }

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