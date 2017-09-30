using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using System.Windows.Forms;
using nucs.JsonSettings.Fluent;

namespace nucs.JsonSettings.Examples {
    static class EncryptedProgram {

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        private static EncryptedSettings Settings { get; } = JsonSettings.Construct<EncryptedSettings>("mysupercomplex_password","memory.jsn").LoadNow();

        [STAThread]
        static void Main2(string[] args) {
            //simple app to open a file by command and browse to a new file on demand.
            while (true) {
                Console.WriteLine("Commands: \nopen - open a file\nbrowse - browse to a new file\nquit - closes");
                Console.Write(">");
                while (Console.ReadLine().ToLowerInvariant() is string r && (r == "open" || r == "browse" || r == "quit")) {
                    switch (r) {
                        case "open":
                            if (string.IsNullOrEmpty(Settings.LastPath))
                                goto _browse;
                            Process.Start(Settings.LastPath);
                            break;
                        case "browse":
                            _browse:
                            var dia = new OpenFileDialog() {FileName = Settings.LastPath, Multiselect = false};
                            if (dia.ShowDialog() == DialogResult.OK) {
                                Settings.LastPath = dia.FileName;
                                Settings.Save();
                            }

                            break;
                    }
                    Console.Write(">");
                }
            }
        }
    }

    public class EncryptedSettings : JsonSettings {
        public SecureString Password { get; }
        public override string FileName { get; set; } = "some.default.just.in.case.jsn";
        public string LastPath { get; set; }

        protected override void OnConfigure() {
            base.OnConfigure();
            this.WithEncryption(() => Password);
        }

        public EncryptedSettings() { }
        public EncryptedSettings(string password) : this(password, "<DEFAULT>") { }
        public EncryptedSettings(string password, string fileName = "<DEFAULT>") : this(password?.ToSecureString(), fileName) { }
        public EncryptedSettings(SecureString password) : this(password, "<DEFAULT>") { }

        public EncryptedSettings(SecureString password, string fileName = "<DEFAULT>") : base(fileName) {
            Password = password;
        }
    }
}