using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Nucs.JsonSettings.Modulation;

namespace Nucs.JsonSettings.Tests.Upgrade {
    /// <summary>
    ///     Custom <see cref="Module"/>s used by <see cref="ModuleChainingTests"/> to stand in for the
    ///     kind a consumer writes: something that transforms the byte stream on the way to and from
    ///     disk, hooked onto the same Encrypt/Decrypt pair <see cref="RijndaelModule"/> and
    ///     <see cref="Base64Module"/> use.
    /// </summary>
    /// <remarks>
    ///     The three differ in exactly one property - whether their output is valid UTF-8 - because
    ///     that is the property 2.1.0 began testing. <see cref="GzipModule"/> and
    ///     <see cref="XorModule"/> emit bytes that are not valid UTF-8; <see cref="HexModule"/> emits
    ///     bytes that are valid UTF-8 but are not JSON. Having all three separates "the payload must
    ///     be UTF-8" from "the payload must be JSON", which the failure message alone cannot.
    /// </remarks>
    internal static class BaselineModules { }

    /// <summary>
    ///     Compresses on the way out, decompresses on the way in. Gzip's 0x1f 0x8b header is already
    ///     an invalid UTF-8 sequence, so its output trips a UTF-8 validity check on the first two
    ///     bytes.
    /// </summary>
    internal sealed class GzipModule : Module {
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

        private void _Encrypt(JsonSettings sender, ref byte[] data) {
            using (var ms = new MemoryStream()) {
                using (var gz = new GZipStream(ms, CompressionMode.Compress, true))
                    gz.Write(data, 0, data.Length);
                data = ms.ToArray();
            }
        }

        private void _Decrypt(JsonSettings sender, ref byte[] data) {
            using (var ms = new MemoryStream(data))
            using (var gz = new GZipStream(ms, CompressionMode.Decompress))
            using (var o = new MemoryStream()) {
                gz.CopyTo(o);
                data = o.ToArray();
            }
        }
    }

    /// <summary>
    ///     XORs every byte with 0xFF. Its own inverse, and it reliably turns ASCII JSON into bytes
    ///     that are not valid UTF-8 (every ASCII byte becomes 0x80-0xFF, i.e. a continuation byte
    ///     with no lead byte in front of it).
    /// </summary>
    internal sealed class XorModule : Module {
        public override void Attach(JsonSettings socket) {
            base.Attach(socket);
            socket.Encrypt += _Transform;
            socket.Decrypt += _Transform;
        }

        public override void Deattach(JsonSettings socket) {
            base.Deattach(socket);
            socket.Encrypt -= _Transform;
            socket.Decrypt -= _Transform;
        }

        private void _Transform(JsonSettings sender, ref byte[] data) {
            var o = new byte[data.Length];
            for (var i = 0; i < data.Length; i++)
                o[i] = (byte) (data[i] ^ 0xFF);
            data = o;
        }
    }

    /// <summary>
    ///     Hex-encodes. The control case: the payload it hands back is not JSON, but every byte of it
    ///     is a printable ASCII character and therefore valid UTF-8.
    /// </summary>
    internal sealed class HexModule : Module {
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

        private void _Encrypt(JsonSettings sender, ref byte[] data) {
            var sb = new StringBuilder(data.Length * 2);
            foreach (var b in data)
                sb.Append(b.ToString("x2"));
            data = Encoding.ASCII.GetBytes(sb.ToString());
        }

        private void _Decrypt(JsonSettings sender, ref byte[] data) {
            var hex = Encoding.ASCII.GetString(data);
            var o = new byte[hex.Length / 2];
            for (var i = 0; i < o.Length; i++)
                o[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            data = o;
        }
    }

    /// <summary>
    ///     The settings shape every test in this folder round-trips.
    /// </summary>
    public class UpgradeSettings : JsonSettings {
        public override string FileName { get; set; }
        public string Value { get; set; }
        public int Number { get; set; }
    }
}
