using System.Security;
using JsonSettings.Fluent;

namespace JsonSettings.xTests {
    public class CasualExampleSettings : JsonSettings {
        public override string FileName { get; set; } = "casual.json";
        public string SomeProperty { get; set; }
        public int SomeNumeralProperty { get; set; } = -1; //with default value.
        public SmallClass SomeClassProperty { get; set; }

        protected override void OnConfigure() {
            base.OnConfigure();
        }
        public CasualExampleSettings() { }
        public CasualExampleSettings(string someprop) {
            SomeProperty = someprop;
        }
    }
}