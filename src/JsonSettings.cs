using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters;
using System.Text;
using nucs.JsonSettings.Fluent;
using nucs.JsonSettings.Inline;
using nucs.JsonSettings.Modulation;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Module = nucs.JsonSettings.Modulation.Module;

namespace nucs.JsonSettings {
    #region Delegates

    public delegate void BeforeLoadHandler(ref string destinition);

    public delegate void DecryptHandler(ref byte[] data);

    public delegate void AfterDecryptHandler(ref byte[] data);

    public delegate void BeforeDeserializeHandler(ref string data);

    public delegate void AfterDeserializeHandler();

    public delegate void AfterLoadHandler();

    public delegate void BeforeSaveHandler(ref string destinition);

    public delegate void BeforeSerializeHandler();

    public delegate void AfterSerializeHandler(ref string data);

    public delegate void EncryptHandler(ref byte[] data);

    public delegate void AfterEncryptHandler(ref byte[] data);

    public delegate void AfterSaveHandler(string destinition);

    public delegate void ConfigurateHandler();

    #endregion

    public abstract class JsonSettings : ISavable, ISocket, IDisposable {
        #region Static

        /// <summary>
        ///     The encoding inwhich the text will be written, by default Encoding.UTF8.
        /// </summary>
        public static Encoding Encoding { get; set; } = Encoding.UTF8;

        protected static readonly JsonSerializerSettings _settings = new JsonSerializerSettings {Formatting = Formatting.Indented, ReferenceLoopHandling = ReferenceLoopHandling.Ignore, NullValueHandling = NullValueHandling.Include, ContractResolver = new FileNameIgnoreResolver(), TypeNameHandling = TypeNameHandling.Auto};
        protected static readonly JsonSerializerSettings _loadsettings = new JsonSerializerSettings {Formatting = Formatting.Indented, ReferenceLoopHandling = ReferenceLoopHandling.Ignore, NullValueHandling = NullValueHandling.Include, TypeNameHandling = TypeNameHandling.Auto};

        #endregion

        private readonly Type _childtype;

        protected JsonSettings() {
            _childtype = GetType();
            if (!_childtype.HasDefaultConstructor())
                throw new JsonSettingsException($"Can't initiate a settings object with class that doesn't have empty public constructor.");
        }

        protected JsonSettings(string fileName) : this() {
            FileName = fileName;
        }

        /// <summary>
        ///     Serves as a reminder where to save or from where to load (if it is loaded on construction and doesnt change between constructions).<br></br>
        ///     Can be relative to executing file's directory.
        /// </summary>
        [JsonIgnore]
        public abstract string FileName { get; set; }

        #region Modularity

#if NET40
        public ReadOnlyCollection<Module> Modules {
#else
        public IReadOnlyList<Module> Modules {
#endif
            get {
                lock (_modules)
                    return _modules.ToList().AsReadOnly();
            }
        }

        public bool IsAttached(Func<Module, bool> checker) {
            return Modules.Any(checker);
        }

        public bool IsAttachedOfType<T>() where T : Module {
            return IsAttachedOfType(typeof(T));
        }

        public bool IsAttachedOfType(Type t) {
            return IsAttached(m => m.GetType() == t);
        }

        protected readonly List<Module> _modules = new List<Module>();

        public void Attach(Module t) {
            if (_isdisposed)
                throw new ObjectDisposedException("Can't attach, this object is already disposed.");
            t.Attach(this);
            lock (_modules)
                _modules.Add(t);
        }

        public void Deattach(Module t) {
            t.Deattach(this);
            lock (_modules)
                _modules.Remove(t);
        }

        /// <summary>
        ///     Will invoke attach to a freshly new object of type <see cref="T"/>.
        /// </summary>
        /// <typeparam name="T">A module class</typeparam>
        /// <param name="args">The arguments that'll be passed to the constructor</param>
        public T Attach<T>(params object[] args) where T : Module {
            var t = (Module) Activator.CreateInstance(typeof(T), args);
            Attach(t);
            return (T) t;
        }

        #endregion

        #region Loading & Saving

        /// <summary>
        ///     The filename that was originally loaded from. saving to other file does not change this field!
        /// </summary>
        /// <param name="filename">the name of the file, <DEFAULT> is the default.</param>
        public virtual void Save(string filename) {
            Save(_childtype, this, filename);
            //File.WriteAllText(filename, JsonConvert.SerializeObject(this));
        }

        /// <summary>
        ///     Save the settings file to a predefined location <see cref="ISavable.FileName" />
        /// </summary>
        public void Save() {
            Save("<DEFAULT>");
        }

        public void Load() {
            Load(this, FileName);
        }

        public void Load(string filename) {
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            Load(this, filename);
        }


        /// <summary>
        ///     Saves settings to a given path using custom password.
        /// </summary>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name. <br></br>Without path the file will be located at the executing directory</param>
        /// <param name="intype"></param>
        /// <param name="pSettings">The settings file to save</param>
        public static void Save(Type intype, object pSettings, string filename = "<DEFAULT>") {
            if (pSettings is JsonSettings == false)
                throw new ArgumentException("Given param is not JsonSettings!", nameof(pSettings));
            if (string.IsNullOrEmpty(filename))
                throw new ArgumentException("message", nameof(filename));

            var o = (JsonSettings) pSettings;
            filename = ResolvePath(o, filename);
            o.EnsureConfigured();

            lock (o) {
                //todo catch and handle filewrite properly.
                o.OnBeforeSave(ref filename);
                o.FileName = filename;
                o.OnBeforeSerialize();
                var json = JsonConvert.SerializeObject(o, intype, _settings);
                o.OnAfterSerialize(ref json);
                var bytes = Encoding.GetBytes(json);
                o.OnEncrypt(ref bytes);
                o.OnAfterEncrypt(ref bytes);
                File.WriteAllBytes(filename, bytes);
                o.OnAfterSave(filename);
            }
        }

        /// <summary>
        ///     Saves settings to a given path using custom password.
        /// </summary>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name. <br></br>Without path the file will be located at the executing directory</param>
        /// <param name="pSettings">The settings file to save</param>
        public static void Save<T>(T pSettings, string filename = "<DEFAULT>") where T : ISavable {
            Save(typeof(T), pSettings, filename);
        }

        /// <summary>
        ///     Loads a settings file or creates a new settings file.
        /// </summary>
        /// <param name="intype">The type of this object</param>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.<br></br>Without path the file will be located at the executing directory</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static object Load(Type intype, string filename = "<DEFAULT>") {
            return Load((ISavable) intype.CreateInstance(), filename);
        }

        /// <summary>
        ///     Loads or creates a settings file.
        /// </summary>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.</param>
        /// <param name="preventoverride">If the file did not exist or corrupt, dont resave it</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static T Load<T>(string filename = "<DEFAULT>") where T : ISavable {
            return (T) Load(typeof(T), filename);
        }

        /// <summary>
        ///     Loads a settings file or creates a new settings file.
        /// </summary>
        /// <param name="instance">The instance inwhich to load into</param>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.<br></br>Without path the file will be located at the executing directory</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static T Load<T>(T instance, string filename = "<DEFAULT>") where T : ISavable {
            byte[] ReadAllBytes(Stream instream) {
                if (instream is MemoryStream stream)
                    return stream.ToArray();

                using (var memoryStream = new MemoryStream()) {
                    instream.CopyTo(memoryStream);
                    return memoryStream.ToArray();
                }
            }

            JsonSettings o = (JsonSettings) ((ISavable) instance ?? (T) typeof(T).CreateInstance());
            filename = ResolvePath(o, filename);
            o.EnsureConfigured();

            o.OnBeforeLoad(ref filename);

            if (File.Exists(filename))
                try {
                    byte[] bytes;
                    using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
                        bytes = ReadAllBytes(fs);

                    o.OnDecrypt(ref bytes);
                    o.OnAfterDecrypt(ref bytes);

                    var fc = Encoding.GetString(bytes);
                    if (string.IsNullOrEmpty((fc ?? "").Replace("\r", "").Replace("\n", "").Trim()))
                        throw new JsonSettingsException("The settings file is empty!");
                    o.OnBeforeDeserialize(ref fc);
                    JsonConvert.PopulateObject(fc, o, _loadsettings);
                    o.OnAfterDeserialize();
                    o.FileName = filename;
                    o.OnAfterLoad();
                    return (T) (object) o;
                } catch (InvalidOperationException e) when (e.Message.Contains("Cannot convert")) {
                    throw new JsonSettingsException("Unable to deserialize settings file, value<->type mismatch. see inner exception", e);
                } catch (ArgumentException e) when (e.Message.StartsWith("Invalid")) {
                    throw new JsonSettingsException("Settings file is corrupt.");
                }

            //doesnt exist.
            o.OnAfterLoad();
            o.FileName = filename;
            o.Save(filename);

            return (T) (object) o;
        }

        /// <summary>
        ///     Create a settings object for further configuration.
        /// </summary>
        /// <param name="intype">The type of the configuration file.</param>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.<br></br>Without path the file will be located at the executing directory</param>
        /// <returns>A freshly new object or <paramref name="intype"/>.</returns>
        public static object Configure(Type intype, string filename = "<DEFAULT>") {
            return Configure((ISavable) intype.CreateInstance(), filename);
        }

        /// <summary>
        ///     Create a settings object for further configuration.
        /// </summary>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.<br></br>Without path the file will be located at the executing directory</param>
        public static T Configure<T>(string filename = "<DEFAULT>") where T : ISavable {
            return (T) Configure(typeof(T), filename);
        }

        /// <summary>
        ///     Create a settings object for further configuration.
        /// </summary>
        /// <param name="instance">An instance if available.</param>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.<br></br>Without path the file will be located at the executing directory</param>
        /// <returns>A freshly new object or <paramref name="instance"/>.</returns>
        public static T Configure<T>(T instance, string filename = "<DEFAULT>") where T : ISavable {
            JsonSettings o = (JsonSettings) ((ISavable) instance ?? (T) typeof(T).CreateInstance());
            FluentlyExt._withFileName(o, filename, true);
            o.EnsureConfigured();
            return (T) (object) o;
        }

        internal static string ResolvePath<T>(T o, string filename, bool throwless = false) where T : JsonSettings {
            if (!throwless && (string.IsNullOrEmpty(filename) || (filename == "<DEFAULT>" && string.IsNullOrEmpty(o.FileName))))
                throw new JsonSettingsException("Could not resolve path because FileName is null or empty.");

            if (filename == "<DEFAULT>")
                filename = o.FileName; //load default - TODO: load from a cached of type default value.

            if (filename.Contains("/") || filename.Contains("\\"))
                filename = Path.Combine(Paths.NormalizePath(Path.GetDirectoryName(filename)), Path.GetFileName(filename));
            else
                filename = Paths.CombineToExecutingBase(filename).FullName;

            return filename;
        }

        #endregion

        #region Events

        #region Inheritable Events

        public event BeforeLoadHandler BeforeLoad;

        private event DecryptHandler _decrypt;

        //reverse insert
        public event DecryptHandler Decrypt {
            add => this._decrypt = value + _decrypt;
            remove => this._decrypt -= value;
        }

        public event AfterDecryptHandler AfterDecrypt;

        public event BeforeDeserializeHandler BeforeDeserialize;

        public event AfterDeserializeHandler AfterDeserialize;

        public event AfterLoadHandler AfterLoad;

        public event BeforeSaveHandler BeforeSave;

        public event BeforeSerializeHandler BeforeSerialize;

        public event AfterSerializeHandler AfterSerialize;

        public event EncryptHandler Encrypt;

        public event AfterEncryptHandler AfterEncrypt;

        public event AfterSaveHandler AfterSave;

        public event ConfigurateHandler Configurate;

        #endregion

        private bool _hasconfigured = false;

        /// <summary>
        ///     Configurate properties of this JsonSettings, for example - call <see cref="FluentlyExt.WithBase64{T}"/> on this.<br></br>
        /// </summary>
        protected virtual void OnConfigure() {
            if (_hasconfigured) throw new InvalidOperationException("Can't run configure twice!");
            _hasconfigured = true;
            Configurate?.Invoke();
        }

        protected internal void EnsureConfigured() {
            if (_hasconfigured)
                return;
            OnConfigure();
        }

        internal virtual void OnBeforeLoad(ref string destinition) {
            BeforeLoad?.Invoke(ref destinition);
        }

        public virtual void OnDecrypt(ref byte[] data) {
            _decrypt?.Invoke(ref data);
        }

        internal virtual void OnAfterDecrypt(ref byte[] data) {
            AfterDecrypt?.Invoke(ref data);
        }

        internal virtual void OnBeforeDeserialize(ref string data) {
            BeforeDeserialize?.Invoke(ref data);
        }

        internal virtual void OnAfterDeserialize() {
            AfterDeserialize?.Invoke();
        }

        internal virtual void OnAfterLoad() {
            AfterLoad?.Invoke();
        }

        internal virtual void OnBeforeSave(ref string destinition) {
            BeforeSave?.Invoke(ref destinition);
        }

        internal virtual void OnBeforeSerialize() {
            BeforeSerialize?.Invoke();
        }

        internal virtual void OnAfterSerialize(ref string data) {
            AfterSerialize?.Invoke(ref data);
        }

        public virtual void OnEncrypt(ref byte[] data) {
            Encrypt?.Invoke(ref data);
        }

        internal virtual void OnAfterEncrypt(ref byte[] data) {
            AfterEncrypt?.Invoke(ref data);
        }

        internal virtual void OnAfterSave(string destinition) {
            AfterSave?.Invoke(destinition);
        }

        #endregion

        private class FileNameIgnoreResolver : DefaultContractResolver {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization) {
                var prop = base.CreateProperty(member, memberSerialization);
                if (prop.PropertyName.Equals("FileName", StringComparison.OrdinalIgnoreCase))
                    prop.Ignored = true;
                return prop;
            }
        }

        private bool _isdisposed = false;

        public void Dispose() {
            if (_isdisposed)
                return;
            _isdisposed = true;
            foreach (var module in _modules.ToArray()) {
                module.Dispose();
            }
        }
    }
}