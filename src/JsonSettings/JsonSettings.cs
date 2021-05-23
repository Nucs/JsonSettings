using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Inline;
using Nucs.JsonSettings.Modulation;
using Module = Nucs.JsonSettings.Modulation.Module;

namespace Nucs.JsonSettings {
    #region Delegates

    public delegate void BeforeLoadHandler(JsonSettings sender, ref string destinition);

    public delegate void DecryptHandler(JsonSettings sender, ref byte[] data);

    public delegate void AfterDecryptHandler(JsonSettings sender, ref byte[] data);

    public delegate void BeforeDeserializeHandler(JsonSettings sender, ref string data);

    public delegate void AfterDeserializeHandler(JsonSettings sender);

    public delegate void AfterLoadHandler(JsonSettings settings, bool successfulLoad);

    public delegate void BeforeSaveHandler(JsonSettings sender, ref string destinition);

    public delegate void BeforeSerializeHandler(JsonSettings sender);

    public delegate void AfterSerializeHandler(JsonSettings sender, ref string data);

    public delegate void EncryptHandler(JsonSettings sender, ref byte[] data);

    public delegate void AfterEncryptHandler(JsonSettings sender, ref byte[] data);

    public delegate void AfterSaveHandler(JsonSettings sender, string destinition);

    public delegate void TryingRecoverHandler(JsonSettings sender, string fileName, JsonException? exception, ref bool recovered, ref bool handled);

    public delegate void RecoveredHandler(JsonSettings sender);

    public delegate void ConfigurateHandler(JsonSettings sender);

    #endregion

    public abstract class JsonSettings : ISavable, IDisposable {
        #region Static

        /// <summary>
        ///     The encoding inwhich the text will be written, by default Encoding.UTF8.
        /// </summary>
        public static Encoding Encoding { get; set; } = Encoding.UTF8;

        public static JsonSerializerSettings SerializationSettings { get; set; } = new JsonSerializerSettings {
            Formatting = Formatting.Indented,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            NullValueHandling = NullValueHandling.Include,
            ContractResolver = new FileNameIgnoreResolver(),
            TypeNameHandling = TypeNameHandling.Auto
        };

        #endregion

        internal readonly Type _childtype;

        /// <summary>
        ///     Serves as a reminder where to save or from where to load (if it is loaded on construction and doesnt change between constructions).<br></br>
        ///     Can be relative to executing file's directory.
        /// </summary>
        [JsonIgnore]
        public abstract string FileName { get; set; }

        #region Modularity

        /// <summary>
        ///     Modulation Manager, handles everything related to modules in this instance.
        /// </summary>
        [JsonIgnore]
        public virtual ModuleSocket Modulation { get; }

        #endregion

        /// <summary>
        ///     If this property is set, this will be used instead of the static <see cref="SerializationSettings"/>.<br></br>
        ///     Note: this property must be set during construction or as property's default value.
        /// </summary>
        [JsonIgnore]
        protected virtual JsonSerializerSettings? OverrideSerializerSettings { get; set; }

        #pragma warning disable 8618
        protected JsonSettings() : this(null!) { }

        protected JsonSettings(string fileName) {
            #pragma warning restore 8618
            _childtype = GetType();
            if (_childtype.GetCustomAttribute<ProxyGeneratedAttribute>() == null) {
                Modulation = new ModuleSocket(this);
                // ReSharper disable once VirtualMemberCallInConstructor
                // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                if (fileName != null)
                    FileName = fileName;

                if (!_childtype.HasDefaultConstructor())
                    throw new JsonSettingsException($"Can't initiate a settings object with class that doesn't have empty public constructor.");
            }
        }

        /// <summary>
        ///     Returns configuration based on the following fallback: <br/>
        ///     settings ?? this.OverrideSerializerSettings ?? JsonSettings.SerializationSettings ?? JsonConvert.DefaultSettings?.Invoke()
        ///              ?? throw new JsonSerializationException("Unable to resolve JsonSerializerSettings to serialize this JsonSettings");
        /// </summary>
        /// <param name="settings">If passed a non-null, This is the settings intended to use, not any of the fallbacks.</param>
        /// <exception cref="JsonSerializationException">When no configuration valid was found.</exception>
        protected virtual JsonSerializerSettings ResolveConfiguration(JsonSerializerSettings? settings = null) {
            return settings
                   ?? this.OverrideSerializerSettings
                   ?? JsonSettings.SerializationSettings
                   ?? JsonConvert.DefaultSettings?.Invoke()
                   ?? throw new JsonSerializationException("Unable to resolve JsonSerializerSettings to serialize this JsonSettings");
        }

        #region Loading & Saving

        #region Save

        /// <summary>
        ///     The filename that was originally loaded from. saving to other file does not change this field!
        /// </summary>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name. <br></br>Without path the file will be located at the executing directory</param>
        public virtual void Save(string filename) {
            Save(_childtype, this, filename);
        }

        /// <summary>
        ///     Save the settings file to a predefined location <see cref="ISavable.FileName" />
        /// </summary>
        public void Save() {
            Save("<DEFAULT>");
        }


        /// <summary>
        ///     Saves settings to a given path using custom password.
        /// </summary>
        /// <param name="pSettings">The settings file to save</param>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name. <br></br>Without path the file will be located at the executing directory</param>
        public static void Save<T>(T pSettings, string filename = "<DEFAULT>") where T : ISavable {
            Save(typeof(T), pSettings, filename);
        }

        /// <summary>
        ///     Saves settings to a given path using custom password.
        /// </summary>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name. <br></br>Without path the file will be located at the executing directory</param>
        /// <param name="inType"></param>
        /// <param name="boxedJsonSettings">The settings file to save</param>
        public static void Save(Type inType, object boxedJsonSettings, string filename = "<DEFAULT>") {
            if (boxedJsonSettings is not JsonSettings o)
                throw new ArgumentException("Given param is not JsonSettings!", nameof(boxedJsonSettings));
            if (string.IsNullOrEmpty(filename))
                throw new ArgumentException("message", nameof(filename));

            filename = ResolvePath(o, filename);
            o.EnsureConfigured();
            FileStream stream = null!;

            try {
                lock (o) {
                    o.OnBeforeSave(ref filename);
                    stream = Files.AttemptOpenFile(filename, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
                    o.FileName = filename;
                    o.OnBeforeSerialize();
                    var json = o.ToJson(serializeAsType: inType);
                    o.OnAfterSerialize(ref json);
                    var bytes = Encoding.GetBytes(json);
                    o.OnEncrypt(ref bytes);
                    o.OnAfterEncrypt(ref bytes);

                    stream.SetLength(0);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Close();
                    stream = null;

                    o.OnAfterSave(filename);
                }
            } catch (IOException e) {
                throw new JsonSettingsException($"Failed writing the file to path: '{filename}'", e);
            } catch (UnauthorizedAccessException e) {
                throw new JsonSettingsException($"Failed writing the file to path: '{filename}'", e);
            } finally {
                stream?.Dispose();
            }
        }

        #endregion

        #region Load

        #region Regular Load

        public void Load() {
            Load(this, (Action) null!, FileName);
        }

        public void Load(string filename) {
            if (filename == null)
                throw new ArgumentNullException(nameof(filename));
            Load(this, (Action) null!, filename);
        }

        public void LoadDefault(params object[]? args) {
            var defaultedValue = (JsonSettings) Activator.CreateInstance(GetType(), args);
            var config = ResolveConfiguration(); //pass configuration that we use here, not default one.
            LoadJson(defaultedValue.ToJson(config), config);

            OnRecovered();
            OnAfterLoad(true);
        }

        public void LoadDefault<T>(params object[]? args) where T : ISavable {
            var defaultedValue = (JsonSettings) Activator.CreateInstance(typeof(T), args);
            var config = ResolveConfiguration(); //pass configuration that we use here, not default one.
            LoadJson(defaultedValue.ToJson(config), config);

            OnRecovered();
            OnAfterLoad(true);
        }

        internal void LoadDefault(Version version, params object[]? args) {
            var defaultedValue = (JsonSettings) Activator.CreateInstance(GetType(), args);
            var config = ResolveConfiguration(); //pass configuration that we use here, not default one.
            LoadJson(defaultedValue.ToJson(config), config);
            OnRecovered();
            if (this is IVersionable versionable)
                versionable.Version = version;
            OnAfterLoad(true);
        }

        internal void LoadDefault<T>(Version version, params object[]? args) where T : ISavable {
            var defaultedValue = (JsonSettings) Activator.CreateInstance(typeof(T), args);
            var config = ResolveConfiguration(); //pass configuration that we use here, not default one.
            LoadJson(defaultedValue.ToJson(config), config);
            if (this is IVersionable versionable)
                versionable.Version = version;

            OnAfterLoad(true);
        }

        public virtual void LoadJson(string json, JsonSerializerSettings? settings = null) {
            JsonConvert.PopulateObject(json, this, ResolveConfiguration(settings));
        }

        public virtual string ToJson(JsonSerializerSettings? settings = null, Type? serializeAsType = null, Formatting? formatting = null) {
            var config = ResolveConfiguration(settings);
            return JsonConvert.SerializeObject(this, serializeAsType ?? GetType(), formatting ?? config.Formatting, config);
        }

        /// <summary>
        ///     Loads or creates a settings file.
        /// </summary>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.</param>
        /// <param name="configure">Configurate the settings instance prior to loading - called after OnConfigure</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public void Load(string filename, Action<JsonSettings>? configure) {
            if (filename == null)
                throw new ArgumentNullException(nameof(filename));
            Load(this, configure, filename);
        }

        /// <summary>
        ///     Loads a settings file or creates a new settings file.
        /// </summary>
        /// <param name="intype">The type of this object</param>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.<br></br>Without path the file will be located at the executing directory</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static object Load(Type intype, string filename = "<DEFAULT>") {
            return Load(intype.CreateInstance(), (Action) null!, filename);
        }

        /// <summary>
        ///     Loads or creates a settings file.
        /// </summary>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.</param>
        /// <param name="configure">Configurate the settings instance prior to loading - called after OnConfigure</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static T Load<T>(string filename = "<DEFAULT>") where T : ISavable {
            return (T) Load(typeof(T), filename);
        }

        /// <summary>
        ///     Loads a settings file or creates a new settings file.
        /// </summary>
        /// <param name="intype">The type of this object</param>
        /// <param name="configure">Configurate the settings instance prior to loading - called after OnConfigure</param>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.<br></br>Without path the file will be located at the executing directory</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static object Load<T>(Type intype, Action<T>? configure, string filename = "<DEFAULT>") where T : ISavable {
            return Load((T) intype.CreateInstance(), configure, filename);
        }

        #endregion

        #region Load With Args

        /// <summary>
        ///     Loads or creates a settings file.
        /// </summary>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.</param>
        /// <param name="configure">Configurate the settings instance prior to loading - called after OnConfigure</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static T Load<T>(string filename, Action<T>? configure, object[] args) where T : ISavable {
            T o = (T) typeof(T).CreateInstance(args);
            return Load(o, configure == null ? null : () => configure?.Invoke(o), filename);
        }

        /// <summary>
        ///     Loads a settings file or creates a new settings file.
        /// </summary>
        /// <param name="intype">The type of this object</param>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.<br></br>Without path the file will be located at the executing directory</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static object Load(Type intype, object[] args) {
            return Load(intype, null, "<DEFAULT>", args);
        }

        /// <summary>
        ///     Loads a settings file or creates a new settings file.
        /// </summary>
        /// <param name="intype">The type of this object</param>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.<br></br>Without path the file will be located at the executing directory</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static object Load(Type intype, string filename, object[] args) {
            return Load(intype, null, filename, args);
        }

        /// <summary>
        ///     Loads or creates a settings file.
        /// </summary>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.</param>
        /// <param name="configure">Configurate the settings instance prior to loading - called after OnConfigure</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static T Load<T>(object[] args) where T : ISavable {
            return (T) Load(typeof(T), args);
        }


        /// <summary>
        ///     Loads or creates a settings file.
        /// </summary>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.</param>
        /// <param name="configure">Configurate the settings instance prior to loading - called after OnConfigure</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static T Load<T>(Action<T>? configure, string filename = "<DEFAULT>") where T : ISavable {
            return (T) Load(typeof(T), configure, filename);
        }

        /// <summary>
        ///     Loads or creates a settings file.
        /// </summary>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.</param>
        /// <param name="configure">Configurate the settings instance prior to loading - called after OnConfigure</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static T Load<T>(string filename, Action<T>? configure) where T : ISavable {
            return (T) Load(typeof(T), configure, filename);
        }

        /// <summary>
        ///     Loads a settings file or creates a new settings file.
        /// </summary>
        /// <param name="intype">The type of this object</param>
        /// <param name="configure">Configurate the settings instance prior to loading - called after OnConfigure</param>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.<br></br>Without path the file will be located at the executing directory</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static object Load(Type intype, Action? configure, string filename, object[] args) {
            return Load(intype.CreateInstance(args), configure, filename);
        }

        #endregion

        /// <summary>
        ///     Loads a settings file or creates a new settings file.
        /// </summary>
        /// <param name="instance">The instance inwhich to load into</param>
        /// <param name="configure">Configurate the settings instance prior to loading - called after OnConfigure</param>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.<br></br>Without path the file will be located at the executing directory</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static T Load<T>(T instance, Action<T>? configure, string filename = "<DEFAULT>") where T : ISavable {
            return Load(instance, () => configure?.Invoke(instance), filename);
        }

        /// <summary>
        ///     Loads a settings file or creates a new settings file.
        /// </summary>
        /// <param name="instance">The instance inwhich to load into</param>
        /// <param name="configure">Configurate the settings instance prior to loading - called after OnConfigure</param>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.<br></br>Without path the file will be located at the executing directory</param>
        /// <returns>The loaded or freshly new saved object</returns>
        public static T Load<T>(T instance, Action? configure, string filename = "<DEFAULT>") where T : ISavable {
            return (T) Load((object) instance, configure, filename);
        }

        /// <summary>
        ///     Loads a settings file or creates a new settings file.
        /// </summary>
        /// <param name="instance">The instance inwhich to load into</param>
        /// <param name="configure">Configurate the settings instance prior to loading - called after OnConfigure</param>
        /// <param name="filename">File name, for example "settings.jsn". no path required, just a file name.<br></br>Without path the file will be located at the executing directory</param>
        /// <returns>The loaded or freshly new saved object</returns>
        /// <exception cref="JsonSettings"></exception>
        public static object Load(object instance, Action? configure, string filename = "<DEFAULT>") {
            byte[] ReadAllBytes(FileStream instream) {
                using (var memoryStream = new MemoryStream((int) instream.Length)) {
                    instream.CopyTo(memoryStream);
                    return memoryStream.ToArray();
                }
            }

            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            JsonSettings o = (JsonSettings) (ISavable) instance;
            filename = ResolvePath(o, filename);
            o.EnsureConfigured();
            configure?.Invoke();

            o.OnBeforeLoad(ref filename);

            if (File.Exists(filename)) {
                try {
                    byte[] bytes;
                    using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
                        bytes = ReadAllBytes(fs);

                    o.OnDecrypt(ref bytes);
                    o.OnAfterDecrypt(ref bytes);

                    var fc = Encoding.GetString(bytes);
                    if (string.IsNullOrEmpty(fc) || string.IsNullOrEmpty(fc.Replace("\r", "").Replace("\n", "").Trim())) {
                        bool recovered = false; //by default we ignore
                        bool handled = false; //by default we ignore
                        o.OnTryingRecover(filename, null, ref recovered, ref handled);
                        if (!recovered)
                            throw new JsonSettingsException("The settings file is empty!");

                        o.OnRecovered();
                        o.OnAfterLoad(false);
                        o.FileName = filename;
                        return o;
                    }

                    o.OnBeforeDeserialize(ref fc);

                    try {
                        o.LoadJson(fc);
                    } catch (JsonException e) {
                        bool recovered = false; //by default we ignore
                        bool handled = false; //by default we ignore
                        o.OnTryingRecover(filename, e, ref recovered, ref handled);

                        if (!recovered)
                            throw new JsonSettingsException($"Unable to parse file '{filename}', see inner exception...", e);

                        o.OnRecovered();
                        o.OnAfterLoad(false);
                        o.FileName = filename;
                        return o;
                    }

                    o.OnAfterDeserialize();
                    o.FileName = filename;
                    o.OnAfterLoad(true);
                    return o;
                } catch (InvalidOperationException e) when (e.Message.Contains("Cannot convert")) {
                    throw new JsonSettingsException("Unable to deserialize settings file, value<->type mismatch. see inner exception", e);
                } catch (ArgumentException e) when (e.Message.StartsWith("Invalid")) {
                    throw new JsonSettingsException("Settings file is corrupt.");
                }
            } else {
                //empty file
                o.OnAfterLoad(false);
                o.FileName = filename;
                o.Save(filename);
            }

            return o;
        }

        #endregion

        #region Configure

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
            FluentJsonSettings._withFileName(o, filename, true);
            o.EnsureConfigured();
            return (T) (object) o;
        }

        #endregion

        #region Construct

        /// <summary>
        ///     Constucts a settings object for further configuration.
        /// </summary>
        /// <param name="args">The arguments that will be passed into the constructor.</param>
        /// <returns>A freshly new object.</returns>
        public static T Construct<T>(params object[] args) where T : ISavable {
            JsonSettings o = (JsonSettings) (ISavable) Activator.CreateInstance(typeof(T), args);
            o.EnsureConfigured();
            return (T) (object) o;
        }

        /// <summary>
        ///     Constucts a settings object for further configuration.
        /// </summary>
        /// <param name="args">The arguments that will be passed into the constructor.</param>
        /// <returns>A freshly new object.</returns>
        public static JsonSettings Construct(Type jsonSettingsType, params object[] args) {
            if (!typeof(ISavable).IsAssignableFrom(jsonSettingsType))
                throw new ArgumentException("Type has to inherit ISavable", nameof(jsonSettingsType));
            JsonSettings o = (JsonSettings) (ISavable) Activator.CreateInstance(jsonSettingsType, args);
            o.EnsureConfigured();
            return o;
        }

        #endregion

        /// <summary>
        ///     Resolves a path passed to a full absolute path.
        /// </summary>
        /// <remarks>This overload handles default value of passed <see cref="o"/></remarks>
        internal static string ResolvePath<T>(T o, string filename, bool throwless = false) where T : JsonSettings {
            if (!throwless && (string.IsNullOrEmpty(filename) || (filename == "<DEFAULT>" && string.IsNullOrEmpty(o.FileName))))
                throw new JsonSettingsException("Could not resolve path because 'FileName' is null or empty.");

            if (filename == "<DEFAULT>") {
                if (o.FileName == null) //param filename is default and o.FileName are null.
                    return null;
                filename = o.FileName; //load from instance.
            }

            return ResolvePath(filename, throwless);
        }

        /// <summary>
        ///     Resolves a path passed to a full absolute path.
        /// </summary>
        internal static string ResolvePath(string filename, bool throwless = false) {
            if (!throwless && string.IsNullOrEmpty(filename))
                throw new JsonSettingsException("Could not resolve path because 'FileName' is null or empty.");

            if (filename.Contains("/") || filename.Contains("\\"))
                filename = Path.Combine(Paths.NormalizePath(Path.GetDirectoryName(filename), false), Path.GetFileName(filename));
            else
                filename = Paths.CombineToExecutingBase(filename).FullName;

            return filename;
        }

        #endregion

        #region Events

        #region Inheritable Events

        private event DecryptHandler? _decrypt;

        public virtual event BeforeLoadHandler? BeforeLoad;

        //reverse insert
        public virtual event DecryptHandler? Decrypt {
            add => this._decrypt = value + _decrypt;
            remove => this._decrypt -= value;
        }

        public virtual event AfterDecryptHandler? AfterDecrypt;

        public virtual event BeforeDeserializeHandler? BeforeDeserialize;

        public virtual event AfterDeserializeHandler? AfterDeserialize;

        public virtual event AfterLoadHandler? AfterLoad;

        public virtual event BeforeSaveHandler? BeforeSave;

        public virtual event BeforeSerializeHandler? BeforeSerialize;

        public virtual event AfterSerializeHandler? AfterSerialize;

        public virtual event EncryptHandler? Encrypt;

        public virtual event AfterEncryptHandler? AfterEncrypt;

        public virtual event AfterSaveHandler? AfterSave;

        /// <summary>
        ///     When parsing JSON fails, this event is called.
        /// </summary>
        /// <remarks>handled=true will prevent other recovery mechanisms from trying to recover. recovered=false will throw the error.</remarks>
        public virtual event TryingRecoverHandler? TryingRecover;

        /// <summary>
        ///     Triggers when an object has recovered/defaulted successfully.
        /// </summary>
        /// <remarks>For example, used by <see cref="VersioningModule"/> to fill the right version on default load.</remarks>
        public virtual event RecoveredHandler? Recovered;

        internal virtual event ConfigurateHandler? Configurate;

        #endregion

        private bool _hasconfigured = false;

        /// <summary>
        ///     Configurate properties of this JsonSettings, for example - call <see cref="FluentJsonSettings.WithBase64{T}"/> on this.<br></br>
        /// </summary>
        protected virtual void OnConfigure() {
            if (_hasconfigured)
                throw new InvalidOperationException("Can't run configure twice!");
            _hasconfigured = true;
            Configurate?.Invoke(this);
        }

        protected internal void EnsureConfigured() {
            if (_hasconfigured)
                return;
            OnConfigure();
        }

        protected internal virtual void OnBeforeLoad(ref string destinition) {
            BeforeLoad?.Invoke(this, ref destinition);
        }

        protected internal virtual void OnDecrypt(ref byte[] data) {
            _decrypt?.Invoke(this, ref data);
        }

        internal virtual void OnAfterDecrypt(ref byte[] data) {
            AfterDecrypt?.Invoke(this, ref data);
        }

        protected internal virtual void OnBeforeDeserialize(ref string data) {
            BeforeDeserialize?.Invoke(this, ref data);
        }

        protected internal virtual void OnAfterDeserialize() {
            AfterDeserialize?.Invoke(this);
        }

        protected internal virtual void OnAfterLoad(bool successfulLoad) {
            AfterLoad?.Invoke(this, successfulLoad);
        }

        protected internal virtual void OnBeforeSave(ref string destinition) {
            BeforeSave?.Invoke(this, ref destinition);
        }

        protected internal virtual void OnBeforeSerialize() {
            BeforeSerialize?.Invoke(this);
        }

        protected internal virtual void OnAfterSerialize(ref string data) {
            AfterSerialize?.Invoke(this, ref data);
        }

        protected internal virtual void OnEncrypt(ref byte[] data) {
            Encrypt?.Invoke(this, ref data);
        }

        protected internal virtual void OnAfterEncrypt(ref byte[] data) {
            AfterEncrypt?.Invoke(this, ref data);
        }

        protected internal virtual void OnAfterSave(string destinition) {
            AfterSave?.Invoke(this, destinition);
        }

        protected internal virtual void OnTryingRecover(string fileName, JsonException? exception, ref bool recovered, ref bool handled) {
            TryingRecover?.Invoke(this, fileName, exception, ref recovered, ref handled);
        }

        protected internal virtual void OnRecovered() {
            Recovered?.Invoke(this);
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

        #region IDisposable

        protected virtual void Dispose(bool disposing) {
            if (disposing) {
                Modulation?.Dispose();
            }
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}