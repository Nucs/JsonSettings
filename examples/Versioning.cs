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
    public class VersioningSettings : JsonSettings, IVersionable {
        public override string FileName { get; set; } = "somename.jsn";
        public virtual Version Version { get; set; } = new Version(1, 0, 0, 6);
        public virtual string AutoProperty { get; set; } = "Hi";


        public VersioningSettings() { }
        public VersioningSettings(string fileName) : base(fileName) { }
    }

    static class Versioning {
        private static VersioningSettings Settings { get; set; }

        public static void Run(string[] args) {
            //load version 1.0.0.6
            Settings = JsonSettings.Configure<VersioningSettings>("versioning.jsn")
                                   .WithVersioning("1.0.0.6", VersioningResultAction.RenameAndLoadDefault) 
                                   .LoadNow()
                                   .EnableAutosave();
            
            Settings.AutoProperty = "Hello"; //Boom! saves.
            
            //after some changes and development, you decide to upgrade to 1.0.0.7
            Settings = JsonSettings.Configure<VersioningSettings>("versioning.jsn")
                                   .WithVersioning("1.0.0.7", VersioningResultAction.RenameAndLoadDefault)
                                   .LoadNow()
                                   .EnableAutosave();
            
            Console.WriteLine(Settings.AutoProperty); // == "Hi";
            
            // AutoProperty is now the default value.
            // because the versions mismatch and the
            // handling VersioningResultAction method is 
            // RenameAndReload therefore there should be a file
            // named versioning.1.0.0.6.jsn holding the old 
            // values and a new and filled with default values
            // versioning.jsn is created.
            
            // other handling techniques can be
            // VersioningResultAction.Throw: throws InvalidVersionException
            // VersioningResultAction.OverrideDefault: Incase of invalid version, default settings will be loaded and saved to disk immediately.
            // VersioningResultAction.LoadDefaultSilently: Incase of invalid version, default settings will be loaded without touching the existing file until next save.
            // VersioningResultAction.RenameAndReload
        }
    }
}