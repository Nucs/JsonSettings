﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using nucs.JsonSettings.Inline;
using Newtonsoft.Json;

namespace nucs.JsonSettings {
    public abstract class JsonSettings : ISaveable {
        #region Static

        /// <summary>
        ///     The encoding inwhich the text will be written, by default Encoding.UTF8.
        /// </summary>
        public static Encoding Encoding { get; set; } = Encoding.UTF8;

        private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings {Formatting = Formatting.Indented, ReferenceLoopHandling = ReferenceLoopHandling.Ignore, NullValueHandling = NullValueHandling.Include};

        private static bool hasDefaultConstructor(Type t) =>
#if NET40 || NET462
            t.IsValueType || t.GetConstructors().Any(c => c.GetParameters().Length == 0 || c.GetParameters().All(p => p.IsOptional));
#else
            t.GetTypeInfo().IsValueType || t.GetTypeInfo().GetConstructors().Any(c => c.GetParameters().Length == 0 || c.GetParameters().All(p => p.IsOptional));
#endif

        #region Singletons

        /*private static Dictionary<Type, ISaveable> singletons { get; } = new Dictionary<Type, ISaveable>();
        static JsonSettings() {
            IEnumerable<T> GetEnumerableOfInterface<T>(params object[] constructorArgs) where T : class {
                var type = typeof(T);
                var types = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(s => s.GetTypes())
#if NET40 || NET462
                    .Where(p => type.IsAssignableFrom(p) && !p.IsInterface && !p.IsAbstract).Select(Activator.CreateInstance).Cast<T>();
#else
                    .Where(p => type.GetTypeInfo().IsAssignableFrom(p) && !p.GetTypeInfo().IsInterface && !p.GetTypeInfo().IsAbstract).Select(Activator.CreateInstance).Cast<T>();
#endif
                return types;
            }

            foreach (var savable in GetEnumerableOfInterface<ISaveable>()) {
                singletons.Add(savable.GetType(), savable);
            }
        }*/

        #endregion

        #endregion

        private readonly Type _childtype;

        protected JsonSettings() {
            _childtype = GetType();
            if (!hasDefaultConstructor(_childtype))
                throw new JsonSettingsException($"Can't initiate a settings object with class that doesn't have empty public constructor.");
        }

        [JsonIgnore]
        public abstract string FileName { get; set; }

        /// <summary>
        ///     The filename that was originally loaded from. saving to other file does not change this field!
        /// </summary>
        /// <param name="filename">the name of the file, ##DEFAULT## is the default.</param>
        public virtual void Save(string filename) {
            Save(_childtype, this, filename);
            //File.WriteAllText(filename, JsonConvert.SerializeObject(this));
        }

        /// <summary>
        ///     Save the settings file to a predefined location <see cref="ISaveable.FileName" />
        /// </summary>
        public void Save() {
            Save("##DEFAULT##");
        }

        public void Load() {
            Load(this, FileName);
        }

        public void Load(string filename) {
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            Load(this, filename);
        }

        #region Inheritable Events

        public virtual void BeforeLoad(ref string destinition) { }

        public virtual void Decrypt(ref byte[] data) { }

        public virtual void AfterDecrypt(ref byte[] data) { }

        public virtual void BeforeDeserialize(ref string data) { }

        public virtual void AfterDeserialize() { }

        public virtual void AfterLoad() { }

        public virtual void BeforeSave(ref string destinition) { }

        public virtual void BeforeSerialize() { }

        public virtual void AfterSerialize(ref string data) { }

        public virtual void Encrypt(ref byte[] data) { }

        public virtual void AfterEncrypt(ref byte[] data) { }

        public virtual void AfterSave(string destinition) { }

        #endregion

        /// <summary>
        ///     Saves settings to a given path.
        /// </summary>
        public static void Save(Type intype, object pSettings, string filename = "##DEFAULT##") {
            if (pSettings is ISaveable == false)
                throw new ArgumentException("Given param is not ISavable!", nameof(pSettings));
            var o = (ISaveable) pSettings;
            if (filename == "##DEFAULT##")
#if NETSTANDARD1_6 || NETSTANDARD2_0
                filename = (string) intype.GetTypeInfo().GetProperty("FileName", typeof(string))?.GetMethod.Invoke(o, null);
#elif NET40 || NET462
                filename = (string) intype.GetProperty("FileName", typeof(string))?.GetGetMethod().Invoke(o, null);
#endif
            if (filename.Contains("/") || filename.Contains("\\")) {
                filename = Path.Combine(Paths.NormalizePath(Path.GetDirectoryName(filename)), Path.GetFileName(filename));
                if (Directory.Exists(Path.GetDirectoryName(filename)) == false)
                    Directory.CreateDirectory(Path.GetDirectoryName(filename));
            } else {
                filename = Paths.CombineToExecutingBase(filename).FullName;
            }

            lock (o) {
                //todo catch and so on..
                o.BeforeSave(ref filename);
                o.BeforeSerialize();
                var json = JsonConvert.SerializeObject(o, intype, _settings);
                o.AfterSerialize(ref json);
                var bytes = Encoding.GetBytes(json);
                o.Encrypt(ref bytes);
                o.AfterEncrypt(ref bytes);
                File.WriteAllBytes(filename, bytes);
                o.AfterSave(filename);
            }
        }

        public static object Load(Type intype, string filename = "##DEFAULT##") {
            return Load((ISaveable) Activator.CreateInstance(intype), filename);
        }

        /// <summary>
        ///     Loads a settings file or creates a new settings file.
        /// </summary>
        /// <param name="intype">The type of this object</param>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.<br></br>Without path the file will be located at the executing directory</param>
        /// <param name="preventoverride">If the file did not exist or corrupt, dont resave it</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static T Load<T>(T instance, string filename = "##DEFAULT##") where T : ISaveable {
            byte[] ReadAllBytes(Stream instream) {
                if (instream is MemoryStream)
                    return ((MemoryStream) instream).ToArray();

                using (var memoryStream = new MemoryStream()) {
                    instream.CopyTo(memoryStream);
                    return memoryStream.ToArray();
                }
            }

            ISaveable o = (ISaveable) instance ?? (T) Activator.CreateInstance(typeof(T));
            if (filename == "##DEFAULT##")
                filename = o.FileName;

            if (filename.Contains("/") || filename.Contains("\\"))
                filename = Path.Combine(Paths.NormalizePath(Path.GetDirectoryName(filename)), Path.GetFileName(filename));
            else
                filename = Paths.CombineToExecutingBase(filename).FullName;

            o.BeforeLoad(ref filename);

            if (File.Exists(filename))
                try {
                    byte[] bytes;
                    using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
                        bytes = ReadAllBytes(fs);

                    o.Decrypt(ref bytes);
                    o.AfterDecrypt(ref bytes);

                    var fc = Encoding.GetString(bytes);
                    if (string.IsNullOrEmpty((fc ?? "").Replace("\r", "").Replace("\n", "").Trim()))
                        throw new JsonSettingsException("The settings file is empty!");
                    o.BeforeDeserialize(ref fc);
                    JsonConvert.PopulateObject(fc, o, _settings);
                    o.AfterDeserialize();
                    o.FileName = filename;
                    o.AfterLoad();
                    return (T) o;
                } catch (InvalidOperationException e) when (e.Message.Contains("Cannot convert")) {
                    throw new JsonSettingsException("Unable to deserialize settings file, value<->type mismatch. see inner exception", e);
                } catch (ArgumentException e) when (e.Message.StartsWith("Invalid")) {
                    throw new JsonSettingsException("Settings file is corrupt.");
                }

            //doesnt exist.
            o.AfterLoad();
            o.FileName = filename;
            o.Save(filename);

            return (T) o;
        }

        /// <summary>
        ///     Saves settings to a given path.
        /// </summary>
        public static void Save<T>(T pSettings, string filename = "##DEFAULT##") where T : ISaveable {
            Save(typeof(T), pSettings, filename);
        }

        /// <summary>
        ///     Loads or creates a settings file.
        /// </summary>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.</param>
        /// <param name="preventoverride">If the file did not exist or corrupt, dont resave it</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static T Load<T>(string filename = "##DEFAULT##") where T : ISaveable, new() {
            return (T) Load(typeof(T), filename);
        }
    }

    public class JsonSettingsException : Exception {
        public JsonSettingsException() { }
        public JsonSettingsException(string message) : base(message) { }
        public JsonSettingsException(string message, Exception inner) : base(message, inner) { }
    }
}