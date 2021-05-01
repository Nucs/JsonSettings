using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Nucs.JsonSettings.Modulation {
    public class VersioningModule {
        /// <summary>
        ///     The default policy used for <see cref="VersioningModule{T}"/>.<br/>
        ///     By default: returns true if expectedVersion is null or expectedVersion.Equals(version)
        /// </summary>
        public static readonly VersioningPolicyHandler DefaultPolicy = (version, expectedVersion) => expectedVersion?.Equals(version) != false;

        private const string _versionMatchRegex = @"(\.\d+\.\d+\.\d+\.\d+(?:\.\d+)?)(?:(?=\.)|-(\d+)|$)";

        /// <summary>
        ///     See https://regex101.com/r/5ITewE/1
        /// </summary>
        public static readonly Regex VersionMatcher = new Regex(_versionMatchRegex, RegexOptions.Compiled | RegexOptions.Multiline);
    }

    /// <summary>
    ///     VersioningModule used to enforce a policy and take <see cref="VersioningResultAction"/> when the policy
    ///     detects an invalid version. A policy is a comparer between two <see cref="Version"/>.
    /// </summary>
    /// <typeparam name="T">The settings type inheriting <see cref="IVersionable"/></typeparam>
    public class VersioningModule<T> : Module where T : JsonSettings, IVersionable {
        // ReSharper disable once StaticMemberInGenericType
        private static VersioningPolicyHandler? _defaultPolicy;

        /// <summary>
        ///     The default policy used when specifying null
        /// </summary>
        public static VersioningPolicyHandler DefaultPolicy {
            get => _defaultPolicy ?? VersioningModule.DefaultPolicy;
            set => _defaultPolicy = value;
        }

        public VersioningResultAction InvalidAction { get; }
        public Version ExpectedVersion { get; set; }
        public VersioningPolicyHandler Policy { get; }


        /// <summary>
        ///     The parameters that'll be passed to the constructor of <typeparamref name="T"/>.
        /// </summary>
        public object[]? ConstructingParameters { get; set; }


        public VersioningModule(VersioningResultAction invalidAction, Version expectedVersion, VersioningPolicyHandler policy) {
            InvalidAction = invalidAction;
            ExpectedVersion = expectedVersion;
            Policy = policy;
        }

        public VersioningModule(VersioningResultAction invalidAction, Version expectedVersion, VersioningPolicyHandler policy, object[] constructingParameters) {
            InvalidAction = invalidAction;
            ExpectedVersion = expectedVersion;
            Policy = policy;
            ConstructingParameters = constructingParameters;
        }

        public override void Attach(JsonSettings socket) {
            base.Attach(socket);
            socket.AfterLoad += SocketOnAfterLoad;
            socket.BeforeLoad += SocketOnBeforeLoad;
        }

        private int _internalCalls;
        private string? _loadedPath;

        private void SocketOnAfterLoad(JsonSettings sender, bool successfulLoad) {
            if (_internalCalls >= 1)
                return;

            T tsender = (T) sender;
            if (successfulLoad == false) {
                tsender.Version = ExpectedVersion;
                return;
            }

            if (!Policy(tsender.Version, ExpectedVersion)) {
                //versions mismatch, handle
                switch (InvalidAction) {
                    case VersioningResultAction.Throw: throw new InvalidVersionException($"Loaded version '{tsender.Version}' mismatches expected version '{ExpectedVersion}'");
                    case VersioningResultAction.RenameAndReload: {
                        if (_loadedPath == null)
                            throw new ArgumentNullException(nameof(_loadedPath));

                        //parse current name
                        var versionMatch = VersioningModule.VersionMatcher.Match(_loadedPath);
                        int fileVersion = versionMatch.Success ? int.Parse(versionMatch.Groups[2].Value) + 1 : 0;
                        var cleanName = _loadedPath;
                        if (!string.IsNullOrEmpty(versionMatch.Groups[0].Value))
                            cleanName = cleanName.Replace(versionMatch.Groups[0].Value, "");
                        var lastIdx = cleanName.LastIndexOf('.');
                        if (lastIdx == -1)
                            lastIdx = _loadedPath.Length;

                        //figure naming of existing and rename
                        string newFileName = cleanName;
                        if (File.Exists(newFileName)) {
                            do {
                                newFileName = cleanName.Insert(lastIdx, $".{tsender.Version}{(fileVersion++ == 0 ? "" : $"-{fileVersion}")}");
                            } while (File.Exists(newFileName));

                            try {
                                File.Move(cleanName, newFileName);
                            } catch (Exception) {
                                // swallow
                                try {
                                    File.Delete(_loadedPath);
                                } catch (Exception) {
                                    // swallow
                                }
                            }
                        }

                        //save
                        _internalCalls++;
                        try {
                            tsender.FileName = _loadedPath = cleanName;
                            tsender.LoadDefault(ExpectedVersion, ConstructingParameters);
                            tsender.Save();
                        } finally {
                            _internalCalls--;
                        }

                        return;
                    }
                    case VersioningResultAction.LoadDefaultSilently:
                        tsender.LoadDefault(ExpectedVersion, ConstructingParameters);
                        return;
                    case VersioningResultAction.OverrideDefault:
                        tsender.LoadDefault(ExpectedVersion, ConstructingParameters);
                        sender.Save();
                        return;

                    default: throw new ArgumentOutOfRangeException();
                }
            }
        }

        private void SocketOnBeforeLoad(JsonSettings sender, ref string destinition) {
            _loadedPath = destinition;
        }

        public override void Deattach(JsonSettings socket) {
            base.Deattach(socket);
            socket.AfterLoad -= SocketOnAfterLoad;
            socket.BeforeLoad -= SocketOnBeforeLoad;
        }
    }
}