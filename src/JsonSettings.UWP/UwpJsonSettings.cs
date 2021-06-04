using Windows.Storage;

namespace Nucs.JsonSettings.UWP {
    public abstract class UwpJsonSettings : Nucs.JsonSettings.JsonSettings {
        #region Overrides of JsonSettings

        public override void Save(string filename) {
            //TODO: ask for permission here
            base.Save(filename);
        }

        #endregion
    }
}