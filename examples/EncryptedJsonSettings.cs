using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace nucs.JsonSettings.Examples {
    static class EncryptedProgram {

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        private static EncryptedSettings Settings { get; } = EncryptedJsonSettings.Load<EncryptedSettings>("mysupercomplex_password","memory.jsn");

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

    public class EncryptedSettings : EncryptedJsonSettings {
        public override string FileName { get; set; } = "some.default.just.in.case.jsn";
        public string LastPath { get; set; }

        public EncryptedSettings() { }
        public EncryptedSettings(string password) : base(password) { }
        public EncryptedSettings(string password, string fileName = "##DEFAULT##") : base(password, fileName) { }
        public EncryptedSettings(SecureString password) : base(password) { }
        public EncryptedSettings(SecureString password, string fileName = "##DEFAULT##") : base(password, fileName) { }
    }
}