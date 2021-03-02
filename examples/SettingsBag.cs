using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JsonSettings.Examples {
    static class SettingsBagProgram {

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        private static SettingsBag Settings { get; } = JsonSettings.Load<SettingsBag>("memory.jsn").EnableAutosave();

        [STAThread]
        static void Main2(string[] args) {
            //simple app to open a file by command and browse to a new file on demand.
            while (true) {
                Console.WriteLine("Commands: \nopen - open a file\nbrowse - browse to a new file\nquit - closes");
                Console.Write(">");
                while (Console.ReadLine().ToLowerInvariant() is string r && (r == "open" || r == "browse" || r == "quit")) {
                    switch (r) {
                        case "open":
                            if (string.IsNullOrEmpty(Settings["LastPath"] as string))
                                goto _browse;
                            Process.Start((string) Settings["LastPath"]);
                            break;
                        case "browse":
                            _browse:
                            var dia = new OpenFileDialog {FileName = (string) Settings["LastPath"], Multiselect = false};
                            if (dia.ShowDialog() == DialogResult.OK) {
                                Settings["LastPath"] = dia.FileName;
                            }

                            break;
                    }
                    Console.Write(">");
                }
            }
        }
    }
}