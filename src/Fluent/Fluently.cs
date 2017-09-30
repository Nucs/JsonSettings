using System;
using System.IO;
using System.Security;
using nucs.JsonSettings.Modulation;

namespace nucs.JsonSettings.Fluent {
/*    public static class FluentlyExtensions {
        public static Fluently<T> Fluent<T>(this T s) where T : JsonSettings {
            return new Fluently<T>(s);
        }
    }
    */
    public static class FluentlyExt {
        internal static T _withFileName<T>(this T _instance, string filename, bool throwless=false) where T : JsonSettings {
            _instance.FileName = JsonSettings.ResolvePath(_instance, filename, throwless);
            return _instance;
        }
        public static T WithFileName<T>(this T _instance, string filename) where T : JsonSettings {
            return _withFileName(_instance, filename);
        }

        public static T WithFileName<T>(this T _instance, FileInfo filename) where T : JsonSettings {
            return _instance.WithFileName(filename?.FullName);
        }

        public static T WithModule<T>(this T _instance, Module module) where T : JsonSettings {
            _instance.Attach(module);
            return _instance;
        }

        public static T WithDefaultValues<T>(this T _instance, Action<T> @do) where T : JsonSettings {
            if (@do == null) throw new ArgumentNullException(nameof(@do));
            @do(_instance);
            return _instance;
        }

        public static T WithEncryption<T>(this T _instance, string password) where T : JsonSettings {
            return _instance.WithEncryption(password?.ToSecureString());
        }

        public static T WithEncryption<T>(this T _instance, SecureString password) where T : JsonSettings {
            return _instance.WithModule<T,RijndaelModule>(password);
        }

        public static T WithEncryption<T>(this T _instance, Func<string> password) where T : JsonSettings {
            return _instance.WithEncryption(password);
        }

        public static T WithEncryption<T>(this T _instance, Func<SecureString> password) where T : JsonSettings {
            return _instance.WithModule<T,RijndaelModule>(password);
        }

        public static T WithBase64<T>(this T _instance) where T : JsonSettings {
            return _instance.WithModule<T, Base64Module>();
        }
        public static T WithModule<T,TMod>(this T _instance, params object[] args) where TMod : Module where T : JsonSettings {
            var t = (Module)Activator.CreateInstance(typeof(TMod), args);
            return _instance.WithModule(t);
        }

        public static T LoadNow<T>(this T _instance, string filename) where T : JsonSettings {
            _instance.Load(filename);
            return _instance;
        }
        public static T LoadNow<T>(this T _instance) where T : JsonSettings {
            _instance.Load();
            return _instance;
        }
    }

    /*public class Fluently<T> where T : JsonSettings {
        private readonly T _instance;

        public Fluently(T instance) {
            _instance = instance;
        }

        public Fluently<T> WithFileName(string filename) {
            _instance.FileName = filename;
            return this;
        }

        public Fluently<T> WithFileName(FileInfo filename) {
            return WithFileName(filename?.FullName);
        }

        public Fluently<T> WithModule(Module module) {
            _instance.Attach(module);
            return this;
        }

        public Fluently<T> WithDefaultValues(Action<T> @do) {
            if (@do == null) throw new ArgumentNullException(nameof(@do));
            @do(_instance);
            return this;
        }

        public Fluently<T> WithEncryption(string password) {
            return WithEncryption(password?.ToSecureString());
        }

        public Fluently<T> WithEncryption(SecureString password) {
            return WithModule<RijndaelModule>(password ?? SecureStringExt.EmptyString);
        }
        public Fluently<T> WithBase64() {
            return WithModule<Base64Module>();
        }
        public Fluently<T> WithModule<TMod>(params object[] args) where TMod : Module {
            var t = (Module)Activator.CreateInstance(typeof(TMod), args);
            return WithModule(t);
        }

        public T Done() {
            return _instance;
        }

        public T Load(string filename) {
            _instance.Load(filename);
            return _instance;
        }
        public T Load() {
            _instance.Load();
            return _instance;
        }
    }*/
}