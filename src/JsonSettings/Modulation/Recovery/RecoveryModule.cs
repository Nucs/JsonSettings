using System;
using System.IO;
using Newtonsoft.Json;

namespace Nucs.JsonSettings.Modulation.Recovery {
    /// <summary>
    ///     Takes care of recovering from errors when trying to parse the json file
    /// </summary>
    public class RecoveryModule : Module {
        public RecoveryAction RecoveryAction { get; set; }
        protected string loadedPath; //the attempted load path
        protected volatile int internalCalls; //guard for event handling

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
                    if (loadedPath == null)
                        throw new ArgumentNullException(nameof(loadedPath));

                    //parse current name
                    var versionMatch = VersioningModule.VersionMatcher.Match(loadedPath);
                    int fileVersion = versionMatch.Success ? int.Parse(versionMatch.Groups[2].Value) + 1 : 0;
                    var cleanName = loadedPath;
                    if (!string.IsNullOrEmpty(versionMatch.Groups[0].Value))
                        cleanName = cleanName.Replace(versionMatch.Groups[0].Value, "");
                    var lastIdx = cleanName.LastIndexOf('.');
                    if (lastIdx == -1)
                        lastIdx = loadedPath.Length;

                    //figure naming of existing and rename
                    string newFileName = cleanName;
                    if (File.Exists(newFileName)) {
                        do {
                            newFileName = cleanName.Insert(lastIdx, $".{(sender is IVersionable versionable ? versionable.Version : "")}{(fileVersion++ == 0 ? "" : $"-{fileVersion}")}");
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
                    internalCalls++;
                    try {
                        sender.FileName = loadedPath = cleanName;
                        sender.LoadDefault(ConstructingParameters);
                        sender.Save();
                        recovered = true;
                        handled = true;
                    } finally {
                        internalCalls--;
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