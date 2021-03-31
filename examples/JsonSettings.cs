using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Nucs.JsonSettings.Examples {
    static class Program {

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        private static Settings Settings { get; } = JsonSettings.Load<Settings>("memory.jsn");

        [STAThread]
        static void Main(string[] args) {
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

    public class Settings : JsonSettings {
        public override string FileName { get; set; } = "some.default.just.in.case.jsn";
        public string LastPath { get; set; }
        public Settings() { }
        public Settings(string fileName) : base(fileName) { }
    }
}