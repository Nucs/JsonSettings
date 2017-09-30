using System.Security;

namespace nucs.JsonSettings.xTests {
    public class CasualExampleSettings : EncryptedJsonSettings {
        public override string FileName { get; set; } = "casual.json";
        public string SomeProperty { get; set; }
        public int SomeNumeralProperty { get; set; } = -1; //with default value.
        public SmallClass SomeClassProperty { get; set; }

        //Resharper auto generate constructors based on parent.
        public CasualExampleSettings() { }
        public CasualExampleSettings(string password) : base(password) { }
        public CasualExampleSettings(string password, string fileName = "<DEFAULT>") : base(password, fileName) { }
        public CasualExampleSettings(SecureString password) : base(password) { }
        public CasualExampleSettings(SecureString password, string fileName = "<DEFAULT>") : base(password, fileName) { }
    }
}