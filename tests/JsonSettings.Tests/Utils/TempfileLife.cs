using System;
using System.Diagnostics;
using System.IO;

namespace Nucs.JsonSettings.xTests.Utils {
    public class TempfileLife : IDisposable {
        public string FileName { get; set; }

        public TempfileLife(bool create = false) {
            FileName = create ? Path.ChangeExtension(Path.GetTempFileName(), "json") : Path.ChangeExtension(Path.GetRandomFileName(), "json");
        }

        public TempfileLife(string fileName) {
            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentException("message", nameof(fileName));

            FileName = fileName;
        }

        public void Dispose() {
            try {
                if (File.Exists(FileName))
                    File.Delete(FileName);
            } catch (Exception e) {
                Debug.WriteLine($"Could not delete file {FileName},\n" + e);
            }
        }

        public static implicit operator string(TempfileLife value) {
            return value.FileName;
        }

        public static implicit operator FileInfo(TempfileLife value) {
            return new FileInfo(value.FileName);
        }
    }
}