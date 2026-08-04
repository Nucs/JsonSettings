using System.IO;
using System.Runtime.InteropServices;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Inline;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Inline {
    /// <summary>
    ///     Unit tests for the internal <see cref="Paths"/> helper. Path resolution is used by every
    ///     load/save, but the helper's own methods -- <see cref="Paths.NormalizePath"/>,
    ///     <see cref="Paths.CompareTo(FileSystemInfo,FileSystemInfo)"/>,
    ///     <see cref="Paths.IsDirectoryWritable"/>, <see cref="Paths.RemoveIllegalPathCharacters"/>,
    ///     <see cref="Paths.MarkForDeletion(string)"/> and the two equality comparers -- were largely
    ///     uncovered.
    /// </summary>
    [TestClass]
    public class PathsTests {
        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        [TestMethod]
        public void ExecutingDirectory_IsNeverNull() {
            //Documented invariant: always non-null, falling back to the current directory.
            Paths.ExecutingDirectory.Should().NotBeNull();
            Paths.ExecutingDirectory.FullName.Should().NotBeNullOrEmpty();
        }

        [TestMethod]
        public void NormalizePath_TrimsTrailingSeparators() {
            var input = "sub" + Path.DirectorySeparatorChar + "dir" + Path.DirectorySeparatorChar;
            var result = Paths.NormalizePath(input);
            result.Should().NotEndWith(Path.DirectorySeparatorChar.ToString());
            result.Should().Contain("dir");
        }

        [TestMethod]
        public void NormalizePath_ConvertsAlternateSeparators() {
            //The "invalid" separator for the platform is converted to the valid one, so callers can pass
            //either slash style.
            var invalid = IsWindows ? "a/b/c" : "a\\b\\c";
            var normalized = Paths.NormalizePath(invalid);
            normalized.Should().Contain(Path.DirectorySeparatorChar.ToString());
        }

        [TestMethod]
        public void NormalizePath_ForComparison_IsCaseFoldedOnWindows() {
            if (!IsWindows)
                Assert.Inconclusive("The case-folding branch is Windows-only (paths are case-sensitive elsewhere).");

            //On Windows, comparison normalization upper-cases so two differently-cased paths compare equal.
            var a = Paths.NormalizePath("c:\\Foo\\Bar", forComparsion: true);
            var b = Paths.NormalizePath("C:\\FOO\\BAR", forComparsion: true);
            a.Should().Be(b);
        }

        [TestMethod]
        public void NormalizePath_BareDriveRoot_GetsSeparatorAppended() {
            if (!IsWindows)
                Assert.Inconclusive("Drive-letter roots are a Windows concept.");

            //"C:" is a drive-relative path; the helper appends the separator so it means the drive root.
            Paths.NormalizePath("C:").Should().Be("C:\\");
        }

        [TestMethod]
        public void CompareTo_Strings_EqualAndDifferent() {
            var p = "some" + Path.DirectorySeparatorChar + "file.json";
            Paths.CompareTo(p, p).Should().BeTrue();
            Paths.CompareTo(p, "other" + Path.DirectorySeparatorChar + "file.json").Should().BeFalse();
        }

        [TestMethod]
        public void CompareTo_FileSystemInfos_EqualAndDifferent() {
            var a = new FileInfo(Path.Combine(Path.GetTempPath(), "same.json"));
            var b = new FileInfo(Path.Combine(Path.GetTempPath(), "same.json"));
            var c = new FileInfo(Path.Combine(Path.GetTempPath(), "different.json"));

            Paths.CompareTo(a, b).Should().BeTrue();
            Paths.CompareTo(a, c).Should().BeFalse();
        }

        [TestMethod]
        public void IsDirectoryWritable_ExistingWritableDirectory_IsTrue() {
            var dir = new DirectoryInfo(Path.GetTempPath());
            dir.IsDirectoryWritable().Should().BeTrue();
        }

        [TestMethod]
        public void IsDirectoryWritable_NonExistentDirectory_IsFalse() {
            var dir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "nope_" + Path.GetRandomFileName()));
            dir.Exists.Should().BeFalse();
            //The probe short-circuits on a directory that does not exist and reports not-writable.
            dir.IsDirectoryWritable().Should().BeFalse();
        }

        [TestMethod]
        public void CombineToExecutingBase_ProducesPathUnderExecutingDirectory() {
            var fi = Paths.CombineToExecutingBase("settings.json");
            fi.Name.Should().Be("settings.json");
            fi.DirectoryName.Should().Be(Paths.ExecutingDirectory.FullName);
        }

        [TestMethod]
        public void RemoveIllegalPathCharacters_StripsInvalidChars() {
            //'\0' is an invalid file-name character on every platform, so this is portable.
            Paths.RemoveIllegalPathCharacters("a\0b\0c").Should().Be("abc");
        }

        [TestMethod]
        public void RemoveIllegalPathCharacters_HonoursReplacement() {
            Paths.RemoveIllegalPathCharacters("a\0b", "_").Should().Be("a_b");
        }

        [TestMethod]
        public void MarkForDeletion_NonExistentFile_ReturnsPathUnchanged() {
            var path = Path.Combine(Path.GetTempPath(), "ghost_" + Path.GetRandomFileName());
            //Nothing to delete -> the helper returns the same path and does no work.
            Paths.MarkForDeletion(path).Should().Be(path);
        }

        [TestMethod]
        public void MarkForDeletion_ExistingFile_ReturnsPathAndDoesNotDeleteImmediately() {
            using var f = new TempFile(create: true);
            File.WriteAllText(f.FileName, "content");

            //On Windows this schedules a delete-on-reboot; off Windows it declines. Either way it returns
            //the path and must not delete the file right now.
            Paths.MarkForDeletion(f.FileName).Should().Be(f.FileName);
            File.Exists(f.FileName).Should().BeTrue();
        }

        [TestMethod]
        public void MarkForDeletion_FileInfoOverload_ReturnsSameFileInfo() {
            using var f = new TempFile(create: true);
            var fi = new FileInfo(f.FileName);
            Paths.MarkForDeletion(fi).Should().BeSameAs(fi);
        }

        [TestMethod]
        public void FilePathEqualityComparer_EqualsAndHashCode() {
            var cmp = new Paths.FilePathEqualityComparer();
            var a = Path.Combine(Path.GetTempPath(), "cfg.json");
            var b = Path.Combine(Path.GetTempPath(), "cfg.json");
            var c = Path.Combine(Path.GetTempPath(), "cfg2.json");

            cmp.Equals(a, b).Should().BeTrue();
            cmp.Equals(a, c).Should().BeFalse();
            cmp.GetHashCode(a).Should().Be(cmp.GetHashCode(b));
        }

        [TestMethod]
        public void FileInfoPathEqualityComparer_EqualsAndHashCode() {
            var cmp = new Paths.FileInfoPathEqualityComparer();
            var a = new FileInfo(Path.Combine(Path.GetTempPath(), "cfg.json"));
            var b = new FileInfo(Path.Combine(Path.GetTempPath(), "cfg.json"));
            var c = new FileInfo(Path.Combine(Path.GetTempPath(), "cfg2.json"));

            cmp.Equals(a, b).Should().BeTrue();
            cmp.Equals(a, c).Should().BeFalse();
            cmp.GetHashCode(a).Should().Be(cmp.GetHashCode(b));
        }
    }
}
