using System;
using System.IO;
using System.Security;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Modulation.Recovery;

namespace Nucs.JsonSettings.Fluent {
    public static class FluentJsonSettings {
        internal static T _withFileName<T>(this T _instance, string filename, bool throwless = false) where T : JsonSettings {
            _instance.FileName = JsonSettings.ResolvePath(_instance, filename, throwless);
            return _instance;
        }

        public static T WithFileName<T>(this T _instance, string filename) where T : JsonSettings {
            return _withFileName(_instance, filename);
        }

        public static T WithFileName<T>(this T _instance, FileInfo filename) where T : JsonSettings {
            return _instance.WithFileName(filename?.FullName);
        }

        /// <summary>
        ///     Load custom made module.
        /// </summary>
        /// <param name="_instance">Self</param>
        /// <param name="module">The module instance</param>
        /// <returns>Self</returns>
        public static T WithModule<T>(this T _instance, Module module) where T : JsonSettings {
            _instance.Modulation.Attach(module);
            return _instance;
        }

        /// <summary>
        ///     Loads a freshly constructed module with the following arguments.
        /// </summary>
        /// <param name="_instance">Self</param>
        /// <param name="args">The arguments to fit into a constructor.</param>
        /// <returns>Self</returns>
        public static T WithModule<T, TMod>(this T _instance, params object[] args) where TMod : Module where T : JsonSettings {
            var t = (Module) typeof(TMod).CreateInstance(args);
            return _instance.WithModule(t);
        }

        /// <summary>
        ///     Fluently do something with self.
        /// </summary>
        /// <returns>Self</returns>
        public static T WithDefaultValues<T>(this T _instance, Action<T> @do) where T : JsonSettings {
            if (@do == null) throw new ArgumentNullException(nameof(@do));
            @do(_instance);
            return _instance;
        }

        /// <summary>
        ///     Attaches <see cref="RijndaelModule"/> that uses custom Rijndael256 library.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="_instance"></param>
        /// <param name="password">A fetcher/getter/generator/action-pointer to the password source</param>
        /// <returns>Self</returns>
        public static T WithEncryption<T>(this T _instance, string password) where T : JsonSettings {
            return _instance.WithEncryption(password?.ToSecureString());
        }

        /// <summary>
        ///     Attaches <see cref="RijndaelModule"/> that uses custom Rijndael256 library.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="_instance"></param>
        /// <param name="password">A fetcher/getter/generator/action-pointer to the password source</param>
        /// <returns>Self</returns>
        public static T WithEncryption<T>(this T _instance, SecureString password) where T : JsonSettings {
            return _instance.WithModule<T>(new RijndaelModule(password));
        }

        /// <summary>
        ///     Attaches <see cref="RijndaelModule"/> that uses custom Rijndael256 library.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="_instance"></param>
        /// <param name="password">A fetcher/getter/generator/action-pointer to the password source</param>
        /// <returns>Self</returns>
        public static T WithEncryption<T>(this T _instance, Func<string> password) where T : JsonSettings {
            return _instance.WithEncryption(() => password?.Invoke()?.ToSecureString());
        }

        /// <summary>
        ///     Attaches <see cref="RijndaelModule"/> that uses custom Rijndael256 library.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="_instance"></param>
        /// <param name="password">A fetcher/getter/generator/action-pointer to the password source</param>
        /// <returns>Self</returns>
        public static T WithEncryption<T>(this T _instance, Func<SecureString> password) where T : JsonSettings {
            return _instance.WithModule<T>(new RijndaelModule(password));
        }

        /// <summary>
        ///     Attaches <see cref="RijndaelModule"/> that uses custom Rijndael256 library.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="_instance"></param>
        /// <param name="password">A fetcher/getter/generator/action-pointer to the password source</param>
        /// <returns>Self</returns>
        public static T WithEncryption<T>(this T _instance, Func<T, string> password) where T : JsonSettings {
            return _instance.WithEncryption(() => password?.Invoke(_instance));
        }

        /// <summary>
        ///     Attaches <see cref="RijndaelModule"/> that uses custom Rijndael256 library.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="_instance"></param>
        /// <param name="password">A fetcher/getter/generator/action-pointer to the password source</param>
        /// <returns>Self</returns>
        public static T WithEncryption<T>(this T _instance, Func<T, SecureString> password) where T : JsonSettings {
            return _instance.WithModule<T>(new RijndaelModule((Func<SecureString>) (() => password?.Invoke(_instance)!)));
        }

        /// <summary>
        ///     Attaches <see cref="RijndaelModule"/> using a binary password. The bytes are stretched
        ///     into a key with the same PBKDF2 construction as a text password, so this is salted and
        ///     iterated - but it is a DIFFERENT credential from the text password whose UTF-8 encoding
        ///     equals these bytes. To use bytes verbatim as the key instead, see
        ///     <see cref="WithEncryptionRawKey{T}(T,byte[])"/>.
        /// </summary>
        /// <param name="_instance"></param>
        /// <param name="password">The binary password.</param>
        /// <returns>Self</returns>
        public static T WithEncryption<T>(this T _instance, byte[] password) where T : JsonSettings {
            return _instance.WithModule<T>(new RijndaelModule(password));
        }

        /// <summary>
        ///     Attaches <see cref="RijndaelModule"/> using a binary password resolved on demand.
        ///     See <see cref="WithEncryption{T}(T,byte[])"/>.
        /// </summary>
        /// <param name="_instance"></param>
        /// <param name="password">A fetcher for the binary password.</param>
        /// <returns>Self</returns>
        public static T WithEncryption<T>(this T _instance, Func<byte[]> password) where T : JsonSettings {
            return _instance.WithModule<T>(new RijndaelModule(password));
        }

        /// <summary>
        ///     Attaches <see cref="RijndaelModule"/> using a binary password resolved from the instance
        ///     on demand. See <see cref="WithEncryption{T}(T,byte[])"/>.
        /// </summary>
        /// <param name="_instance"></param>
        /// <param name="password">A fetcher for the binary password, given the instance.</param>
        /// <returns>Self</returns>
        public static T WithEncryption<T>(this T _instance, Func<T, byte[]> password) where T : JsonSettings {
            return _instance.WithModule<T>(new RijndaelModule((Func<byte[]>) (() => password?.Invoke(_instance)!)));
        }

        /// <summary>
        ///     Attaches <see cref="RijndaelModule"/> using <paramref name="key"/> verbatim as the AES
        ///     key, with no key derivation. The key must be 16, 24 or 32 bytes (AES-128/192/256). For
        ///     callers that already hold key material; the caller owns the key's quality, since PBKDF2
        ///     stretching is skipped.
        /// </summary>
        /// <param name="_instance"></param>
        /// <param name="key">The raw AES key, 16/24/32 bytes.</param>
        /// <returns>Self</returns>
        public static T WithEncryptionRawKey<T>(this T _instance, byte[] key) where T : JsonSettings {
            return _instance.WithModule<T>(RijndaelModule.FromRawKey(key));
        }

        /// <summary>
        ///     Attaches <see cref="RijndaelModule"/> using a raw AES key resolved on demand.
        ///     See <see cref="WithEncryptionRawKey{T}(T,byte[])"/>.
        /// </summary>
        /// <param name="_instance"></param>
        /// <param name="key">A fetcher for the raw AES key, 16/24/32 bytes.</param>
        /// <returns>Self</returns>
        public static T WithEncryptionRawKey<T>(this T _instance, Func<byte[]> key) where T : JsonSettings {
            return _instance.WithModule<T>(RijndaelModule.FromRawKey(key));
        }

        /// <summary>
        ///     Attaches <see cref="RijndaelModule"/> using a raw AES key resolved from the instance on
        ///     demand. See <see cref="WithEncryptionRawKey{T}(T,byte[])"/>.
        /// </summary>
        /// <param name="_instance"></param>
        /// <param name="key">A fetcher for the raw AES key, given the instance.</param>
        /// <returns>Self</returns>
        public static T WithEncryptionRawKey<T>(this T _instance, Func<T, byte[]> key) where T : JsonSettings {
            return _instance.WithModule<T>(RijndaelModule.FromRawKey((Func<byte[]>) (() => key?.Invoke(_instance)!)));
        }

        public static T WithBase64<T>(this T _instance) where T : JsonSettings {
            return _instance.WithModule<T>(new Base64Module());
        }

        /// <summary>
        ///     Performs Load on the JsonSettings, used after calling <see cref="JsonSettings.Configure"/> to perform a load at the end.
        /// </summary>
        /// <param name="_instance">The instance of type JsonSettings</param>
        /// <param name="filename">Specific file to load (ignores <see cref="JsonSettings.FileName"/>)</param>
        /// <returns>Self</returns>
        public static T LoadNow<T>(this T _instance, string filename) where T : JsonSettings {
            _instance.Load(filename);
            return _instance;
        }

        /// <summary>
        ///     Performs Load on the JsonSettings, used after calling <see cref="JsonSettings.Configure"/> to perform a load at the end.<br></br>
        ///     Uses <see cref="JsonSettings.FileName"/>.
        /// </summary>
        /// <param name="_instance">The instance of type JsonSettings</param>
        /// <returns>Self</returns>
        public static T LoadNow<T>(this T _instance) where T : JsonSettings {
            _instance.Load();
            return _instance;
        }

        /// <summary>
        ///     VersioningModule used to enforce a policy and take <see cref="VersioningResultAction"/> when the policy
        ///     detects an invalid version. A policy is a <see cref="VersioningPolicyHandler"/> comparer between two <see cref="Version"/>.
        /// </summary>
        /// <typeparam name="T">The settings type inheriting <see cref="IVersionable"/></typeparam>
        /// <param name="_instance">the instance being configured</param>
        /// <param name="expectedVersion">The expected version based on given <paramref name="policy"/></param>
        /// <param name="invalidAction">The action to take when the <paramref name="policy"/> returns false</param>
        /// <param name="policy">The version comparison policy, return true when version is OK.</param>
        public static T WithVersioning<T>(this T _instance, Version expectedVersion, VersioningResultAction invalidAction, VersioningPolicyHandler? policy = null)
            where T : JsonSettings, IVersionable {
            return _instance.WithModule<T>(new VersioningModule<T>(invalidAction, expectedVersion, policy ?? VersioningModule<T>.DefaultPolicy));
        }

        /// <summary>
        ///     VersioningModule used to enforce a policy and take <see cref="VersioningResultAction"/> when the policy
        ///     detects an invalid version. A policy is a <see cref="VersioningPolicyHandler"/> comparer between two <see cref="Version"/>.
        /// </summary>
        /// <typeparam name="T">The settings type inheriting <see cref="IVersionable"/></typeparam>
        /// <param name="_instance">the instance being configured</param>
        /// <param name="expectedVersion">The expected version based on given <paramref name="policy"/></param>
        /// <param name="invalidAction">The action to take when the <paramref name="policy"/> returns false</param>
        /// <param name="policy">The version comparison policy, return true when version is OK.</param>
        public static T WithVersioning<T>(this T _instance, string expectedVersion, VersioningResultAction invalidAction, VersioningPolicyHandler? policy = null)
            where T : JsonSettings, IVersionable {
            return WithVersioning(_instance, Version.Parse(expectedVersion), invalidAction, policy);
        }

        /// <summary>
        ///     VersioningModule used to enforce a policy and take <see cref="VersioningResultAction"/> when the policy
        ///     detects an invalid version. A policy is a <see cref="VersioningPolicyHandler"/> comparer between two <see cref="Version"/>.<br/>
        ///     This overload uses <see cref="EnforcedVersionAttribute"/> to resolve the default attribute.
        /// </summary>
        /// <typeparam name="T">The settings type inheriting <see cref="IVersionable"/></typeparam>
        /// <param name="_instance">the instance being configured</param>
        /// <param name="invalidAction">The action to take when the <paramref name="policy"/> returns false</param>
        /// <param name="policy">The version comparison policy, return true when version is OK.</param>
        public static T WithVersioning<T>(this T _instance, VersioningResultAction invalidAction, VersioningPolicyHandler? policy = null)
            where T : JsonSettings, IVersionable {
            return _instance.WithModule<T>(new VersioningModule<T>(invalidAction, policy ?? VersioningModule<T>.DefaultPolicy));
        }
        
        /// <summary>
        ///     <see cref="RecoveryModule"/> used to enforce a fail-safe recovery and take <see cref="RecoveryAction"/> when the
        ///     json parsing fails.
        /// </summary>
        public static T WithRecovery<T>(this T _instance, RecoveryAction action) where T : JsonSettings {
            return _instance.WithModule<T>(new RecoveryModule(action));
        }
    }
}