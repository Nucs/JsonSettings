using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Nucs.JsonSettings.Modulation {
    public class VersioningModule {
        /// <summary>
        ///     The default policy used for <see cref="VersioningModule{T}"/>.<br/>
        ///     By default: returns true if expectedVersion is null or expectedVersion.Equals(version)
        /// </summary>
        public static readonly VersioningPolicyHandler DefaultPolicy = DefaultEqualPolicy;

        public static bool DefaultEqualPolicy(Version version, Version expectedVersion) {
            return expectedVersion?.Equals(version) != false;
        }

        /// <summary>
        ///     See https://regex101.com/r/7xXzRt/1
        /// </summary>
        public static readonly Regex VersionMatcher = new Regex(@"(\.\d+\.\d+\.\d+\.\d+(?:\.\d+)?)(?:(?=\.)|-(\d+)|$)", RegexOptions.Compiled | RegexOptions.Multiline);
    }

    /// <summary>
    ///     VersioningModule used to enforce a policy and take <see cref="VersioningResultAction"/> when the policy
    ///     detects an invalid version. A policy is a comparer between two <see cref="Version"/>.
    /// </summary>
    /// <typeparam name="T">The settings type inheriting <see cref="IVersionable"/></typeparam>
    public class VersioningModule<T> : Module where T : JsonSettings, IVersionable {
        // ReSharper disable once StaticMemberInGenericType
        private static VersioningPolicyHandler? _defaultPolicy;
        protected volatile int internalCalls; //guard for event handling
        protected string? loadedPath; //the path that was passed during loading

        /// <summary>
        ///     The default policy used when specifying null
        /// </summary>
        public static VersioningPolicyHandler DefaultPolicy {
            get => _defaultPolicy ?? VersioningModule.DefaultPolicy;
            set => _defaultPolicy = value;
        }

        public virtual VersioningResultAction VersionMismatchAction { get; set; }
        public virtual Version ExpectedVersion { get; set; }
        public virtual VersioningPolicyHandler Policy { get; set; }

        /// <summary>
        ///     The parameters that'll be passed to the constructor of <typeparamref name="T"/>.
        /// </summary>
        public virtual object[]? ConstructingParameters { get; set; } = Array.Empty<object>();

        public VersioningModule(VersioningResultAction versionMismatchAction, Version expectedVersion, VersioningPolicyHandler policy)
            : this(versionMismatchAction, policy, null) {
            ExpectedVersion = expectedVersion;
        }

        public VersioningModule(VersioningResultAction versionMismatchAction, Version expectedVersion, VersioningPolicyHandler policy, object[]? constructingParameters)
            : this(versionMismatchAction, policy, constructingParameters) {
            ExpectedVersion = expectedVersion;
        }

        public VersioningModule(VersioningResultAction versionMismatchAction, VersioningPolicyHandler policy)
            : this(versionMismatchAction, policy, null) { }

        public VersioningModule(VersioningResultAction versionMismatchAction, VersioningPolicyHandler policy, object[]? constructingParameters) {
            VersionMismatchAction = versionMismatchAction;
            Policy = policy;
            if (constructingParameters != null)
                ConstructingParameters = constructingParameters;
        }

        #pragma warning disable 693
        private class DefaultVersionCache<T> where T : JsonSettings, IVersionable {
            #pragma warning restore 693
            static DefaultVersionCache() {
                var attr = typeof(T).GetProperty(nameof(IVersionable.Version), BindingFlags.Instance | BindingFlags.Public)
                                   ?.GetCustomAttributes<EnforcedVersionAttribute>()
                                    .FirstOrDefault(); //get latest attribute
                if (attr == null)
                    return;

                if (attr.Version != null)
                    EnforcedVersion = attr.Version;
            }

            // ReSharper disable once StaticMemberInGenericType
            public static readonly Version? EnforcedVersion;
        }

        public override void Attach(JsonSettings socket) {
            if (!(socket is IVersionable))
                throw new InvalidOperationException($"{socket._childtype.Name} does not implement IVersionable.");

            // ReSharper disable once ConstantNullCoalescingCondition
            ExpectedVersion ??= DefaultVersionCache<T>.EnforcedVersion ?? throw new InvalidVersionException($"Unable to resolve 'ExpectedVersion' for '{socket._childtype.Name}'");

            base.Attach(socket);
            socket.AfterLoad += SocketOnAfterLoad;
            socket.BeforeLoad += SocketOnBeforeLoad;
            socket.Recovered += SocketOnRecovered;
        }

        public override void Deattach(JsonSettings socket) {
            base.Deattach(socket);
            socket.AfterLoad -= SocketOnAfterLoad;
            socket.BeforeLoad -= SocketOnBeforeLoad;
            socket.Recovered -= SocketOnRecovered;
        }

        protected virtual void SocketOnBeforeLoad(JsonSettings sender, ref string destinition) {
            loadedPath = destinition;
        }

        protected virtual void SocketOnAfterLoad(JsonSettings sender, bool successfulLoad) {
            if (internalCalls >= 1)
                return;

            T tsender = (T) sender;
            if (successfulLoad == false) {
                tsender.Version = ExpectedVersion;
                return;
            }

            if (!Policy(tsender.Version, ExpectedVersion)) {
                HandleInvalidVersion(tsender, VersionMismatchAction);
            }
        }

        protected virtual void SocketOnRecovered(JsonSettings sender) {
            ((IVersionable) sender).Version = ExpectedVersion;
        }

        protected virtual void HandleInvalidVersion(T sender, VersioningResultAction action) {
            //versions mismatch, handle
            switch (action) {
                case VersioningResultAction.DoNothing: return;
                case VersioningResultAction.Throw:     throw new InvalidVersionException($"Loaded version '{sender.Version}' mismatches expected version '{ExpectedVersion}'");
                case VersioningResultAction.RenameAndLoadDefault: {
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
                            newFileName = cleanName.Insert(lastIdx, $".{sender.Version}-{fileVersion++}");
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
                    } finally {
                        internalCalls--;
                    }

                    return;
                }
                case VersioningResultAction.LoadDefault:
                    sender.LoadDefault(ConstructingParameters);
                    return;
                case VersioningResultAction.LoadDefaultAndSave:
                    sender.LoadDefault(ConstructingParameters);
                    sender.Save();
                    return;

                default: throw new ArgumentOutOfRangeException();
            }
        }
    }
}