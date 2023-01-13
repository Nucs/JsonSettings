using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Modulation.Recovery;

namespace Nucs.JsonSettings.Examples {
    public class RecoverableSettings : JsonSettings{
        public override string FileName { get; set; } = "somename.json";
        /*public virtual Version Version { get; set; } = new Version(1, 0, 0, 6);*/
        public virtual string AutoProperty { get; set; } = "Hi";


        public RecoverableSettings() { }
        public RecoverableSettings(string fileName) : base(fileName) { }
    }

    static class RecoverableFromFailureToLoad {
        private static RecoverableSettings Settings { get; set; }

        public static void Run(string[] args) {
            Settings = JsonSettings.Configure<RecoverableSettings>("abouttofail.jsn")
                                   .WithRecovery(RecoveryAction.RenameAndLoadDefault)
                                   .LoadNow()
                                   .EnableAutosave();
            
            Settings.AutoProperty = "Hello"; //Boom! saves.
            //someone changed the file manually and messed up the json.
            File.WriteAllText("abouttofail.jsn", File.ReadAllText("abouttofail.jsn", Encoding.UTF8).Replace("Hello", "Hi\"}\n:={}"), Encoding.UTF8); //some random text breaking the json
            
            //after some changes and development, you decide to upgrade to 1.0.0.7
            Settings = JsonSettings.Configure<RecoverableSettings>("abouttofail.jsn")
                                   .WithRecovery(RecoveryAction.RenameAndLoadDefault)
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