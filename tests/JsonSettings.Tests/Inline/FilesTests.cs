using System;
using System.IO;
using System.Runtime.InteropServices;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Inline {
    /// <summary>
    ///     Unit tests for <see cref="Files.AttemptOpenFile(string,FileMode,FileAccess,FileShare,bool)"/>
    ///     and its <see cref="FileInfo"/> overload -- the guarded file opener the save path relies on.
    ///     The non-creating-mode short-circuit, the parent-directory creation, the empty-name guard and
    ///     the IOException swallow/rethrow behaviour were uncovered.
    /// </summary>
    [TestClass]
    public class FilesTests {
        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        [TestMethod]
        public void EmptyFileName_ThrowsArgumentException() {
            new Action(() => Files.AttemptOpenFile((string) null!))
                .Should().Throw<ArgumentException>();
            new Action(() => Files.AttemptOpenFile(""))
                .Should().Throw<ArgumentException>();
        }

        [TestMethod]
        public void NonExistentFile_WithOpenMode_ReturnsNull() {
            var path = Path.Combine(Path.GetTempPath(), "missing_" + Path.GetRandomFileName());
            //FileMode.Open will not create, and the file is absent -> the opener returns null rather than
            //throwing FileNotFoundException.
            Files.AttemptOpenFile(path, FileMode.Open).Should().BeNull();
        }

        [TestMethod]
        public void FileInfoOverload_OpenOrCreate_OpensStream() {
            using var f = new TempFile();
            using var stream = ((FileInfo) f).AttemptOpenFile(FileMode.OpenOrCreate, FileAccess.Write);
            stream.Should().NotBeNull();
            stream!.CanWrite.Should().BeTrue();
        }

        [TestMethod]
        public void OpenOrCreate_CreatesMissingParentDirectory() {
            var dir = Path.Combine(Path.GetTempPath(), "js_files_" + Path.GetRandomFileName());
            var path = Path.Combine(dir, "nested", "file.json");
            try {
                Directory.Exists(dir).Should().BeFalse();

                using (var stream = Files.AttemptOpenFile(path, FileMode.OpenOrCreate, FileAccess.Write)) {
                    stream.Should().NotBeNull();
                }

                //The opener creates the whole parent chain before opening.
                File.Exists(path).Should().BeTrue();
            } finally {
                try { Directory.Delete(dir, true); } catch { /* best effort */ }
            }
        }

        [TestMethod]
        public void LockedFile_WithoutThrow_ReturnsNull() {
            if (!IsWindows)
                Assert.Inconclusive("Deterministic FileShare.None locking across handles is a Windows guarantee.");

            using var f = new TempFile(create: true);
            File.WriteAllText(f.FileName, "x");

            using (var holder = File.Open(f.FileName, FileMode.Open, FileAccess.Write, FileShare.None)) {
                //The file exists but is exclusively locked; a silent open must swallow the IOException
                //and return null.
                Files.AttemptOpenFile(f.FileName, FileMode.Open, FileAccess.Read, FileShare.None, @throw: false)
                    .Should().BeNull();
            }
        }

        [TestMethod]
        public void LockedFile_WithThrow_RethrowsIOException() {
            if (!IsWindows)
                Assert.Inconclusive("Deterministic FileShare.None locking across handles is a Windows guarantee.");

            using var f = new TempFile(create: true);
            File.WriteAllText(f.FileName, "x");

            using (var holder = File.Open(f.FileName, FileMode.Open, FileAccess.Write, FileShare.None)) {
                new Action(() => Files.AttemptOpenFile(f.FileName, FileMode.Open, FileAccess.Read, FileShare.None, @throw: true))
                    .Should().Throw<IOException>();
            }
        }

        [TestMethod]
        public void FileInfoOverload_HonoursThrowFlag() {
            //Regression: the FileInfo overload forwarded four of its five arguments and dropped @throw,
            //so @throw:true was silently ignored and a locked file returned null instead of throwing.
            if (!IsWindows)
                Assert.Inconclusive("Deterministic FileShare.None locking across handles is a Windows guarantee.");

            using var f = new TempFile(create: true);
            File.WriteAllText(f.FileName, "x");
            var fi = (FileInfo) f;

            using (var holder = File.Open(f.FileName, FileMode.Open, FileAccess.Write, FileShare.None)) {
                //@throw:false still swallows to null (unchanged behaviour).
                fi.AttemptOpenFile(FileMode.Open, FileAccess.Read, FileShare.None, @throw: false).Should().BeNull();

                //@throw:true must now propagate the IOException, matching the string overload.
                new Action(() => fi.AttemptOpenFile(FileMode.Open, FileAccess.Read, FileShare.None, @throw: true))
                    .Should().Throw<IOException>();
            }
        }
    }
}
