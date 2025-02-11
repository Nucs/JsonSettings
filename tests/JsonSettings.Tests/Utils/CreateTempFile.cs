using System;
using System.Diagnostics;
using System.IO;

namespace Nucs.JsonSettings.Tests.Utils;

public class CreateTempFile : IDisposable {
    public string FileName { get; set; }

    public CreateTempFile(bool create = false, string extension = "json") {
        FileName = Path.GetFullPath(create ? Path.ChangeExtension(Path.GetTempFileName(), extension) : Path.ChangeExtension(Path.GetRandomFileName(), extension));
    }

    public CreateTempFile(string fileName) {
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

    public static implicit operator string(CreateTempFile value) {
        return value.FileName;
    }

    public static implicit operator FileInfo(CreateTempFile value) {
        return new FileInfo(value.FileName);
    }
}