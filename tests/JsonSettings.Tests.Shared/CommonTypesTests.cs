using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings;


namespace Nucs.JsonSettings.Tests {
    [TestClass]
    public class CommonTypesTests {
        [TestMethod]
        public void SettingsBag_List() {
            using (var f = new TempfileLife()) {
                var o = JsonSettings.Load<SettingsBag>(f.FileName);

                o["prop"] = new List<string>() {"swag", "lol"};
                o.Save();
                Console.WriteLine(File.ReadAllText(o.FileName));
                var o2 = JsonSettings.Load<SettingsBag>(f.FileName);
                var ret = o2["prop"];
                ret.Should().BeOfType<List<string>>();
                ((List<string>)ret).Count.ShouldBeEquivalentTo(2);
            }
        }

        class FilterFileNameSettings : JsonSettings {
            public override string FileName { get; set; }
            public FilterFileNameSettings() { }
            public FilterFileNameSettings(string fileName) : base(fileName) { }
        }

        class ModuleLoadingSttings : JsonSettings {
            public override string FileName { get; set; }
            public string someprop { get; set; }
            public ModuleLoadingSttings() { }
            public ModuleLoadingSttings(string fileName) : base(fileName) { }
        }

        class FilenamelessSettings : JsonSettings {
            public override string FileName { get; set; } = null;
            public string someprop { get; set; }

            public FilenamelessSettings() { }
            public FilenamelessSettings(string fileName) : base(fileName) { }
        }
    }
}