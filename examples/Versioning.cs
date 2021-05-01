using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Modulation;

namespace Nucs.JsonSettings.Examples {
    static class Versioning {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        private static VersioningSettings Settings { get; set; }

        public static void Run(string[] args) {
            Settings = JsonSettings.Configure<VersioningSettings>("versioning.jsn")
                                   .WithVersioning("1.0.0.6", VersioningResultAction.RenameAndReload,
                                                   (version, expectedVersion) => version?.Equals(expectedVersion) == true)
                                   .LoadNow()
                                   .EnableAutosave();
            
            Settings.AutoProperty = "Hello"; //Boom! saves.
        }
    }

    public class VersioningSettings : JsonSettings, IVersionable {
        public override string FileName { get; set; } = "somename.jsn";

        public virtual string AutoProperty { get; set; }

        public virtual Version Version { get; set; } = new Version(1, 0, 0, 0); 


        public VersioningSettings() { }
        public VersioningSettings(string fileName) : base(fileName) { }
    }
}