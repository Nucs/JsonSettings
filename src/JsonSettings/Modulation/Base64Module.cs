using System;

namespace Nucs.JsonSettings.Modulation {

    /// <summary>
    ///     Will convert text to base64, not pure json.
    /// </summary>
    public class Base64Module : Module {

        public Base64Module() { }

        public override void Attach(JsonSettings socket) {
            base.Attach(socket);
            socket.Encrypt += _Encrypt;
            socket.Decrypt += _Decrypt;

        }

        public override void Deattach(JsonSettings socket) {
            base.Deattach(socket);
            socket.Encrypt -= _Encrypt;
            socket.Decrypt -= _Decrypt;
        }

        protected virtual void _Encrypt(JsonSettings sender, ref byte[] data) {
            data = JsonSettings.Encoding.GetBytes(Convert.ToBase64String(data));
        }

        protected virtual void _Decrypt(JsonSettings sender, ref byte[] data) {
            try {
                data = Convert.FromBase64String(JsonSettings.Encoding.GetString(data));
            } catch (FormatException) {
                //A file that is not valid base64 carries no decodable payload -- the text-safe analogue
                //of EncryptionModule's short-ciphertext case. Emit an empty payload so JsonSettings.Load
                //routes it through the empty-content branch to RecoveryModule (or reports "the settings
                //file is empty!" as a catchable JsonSettingsException), instead of letting FormatException
                //escape the decrypt stage as a non-JsonSettingsException that also bypasses recovery.
                data = new byte[0];
            }
        }
    }
}