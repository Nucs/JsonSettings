using System;
using System.Diagnostics;
using System.IO;

namespace Nucs.JsonSettings.Tests.Utils;

/// <summary>
///     Owns the lifetime of a settings file used by one test, and deletes it on disposal.
/// </summary>
/// <remarks>
///     Note that the default constructor does NOT create the file: it only reserves a name.
///     Most tests want that, because the thing under test is usually how JsonSettings behaves
///     when the file does not exist yet. Pass create: true for the cases that need it present.
/// </remarks>
public class TempFile : IDisposable {
    public string FileName { get; set; }

    public TempFile(bool create = false, string extension = "json") {
        FileName = Path.GetFullPath(create ? Path.ChangeExtension(Path.GetTempFileName(), extension) : Path.ChangeExtension(Path.GetRandomFileName(), extension));
    }

    public TempFile(string fileName) {
        if (string.IsNullOrEmpty(fileName))
            throw new ArgumentException("A temp file needs a name.", nameof(fileName));

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

    public static implicit operator string(TempFile value) {
        return value.FileName;
    }

    public static implicit operator FileInfo(TempFile value) {
        return new FileInfo(value.FileName);
    }
}