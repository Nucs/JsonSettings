using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Nucs.JsonSettings.Inline {
    /// <summary>
    ///     Class that determines paths.
    /// </summary>
    internal static class Paths {
        #region GetModuleFileNameLongPath

        private const int MAX_PATH = 260;
        private const int MAX_UNICODESTRING_LEN = short.MaxValue;
        private const int INSUFFICIENT_BUFFER_ERROR = 0x007A;

        private static string? _moduleFileNameLongPath = null;

        /// <summary>
        /// Retrieves the fully qualified path for the file that contains the specified module.
        /// The module must have been loaded by the current process.
        /// </summary>
        /// <param name="hModule">A handle to the loaded module whose path is being requested.</param>
        /// <param name="buffer">A pointer to a buffer that receives the fully qualified path of the module.</param>
        /// <param name="length">The size of the buffer.</param>
        /// <returns>
        /// If the function succeeds, returns the length of the string that is copied to the buffer, else returns 0 (zero).
        /// </returns>
        /// <remarks>https://docs.microsoft.com/windows/win32/api/libloaderapi/nf-libloaderapi-getmodulefilenamea</remarks>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetModuleFileName(IntPtr hModule, StringBuilder buffer, int length);

/// <summary>
/// Retrieves the fully qualified path for the file that contains the specified module.
/// The module must have been loaded by the current process.
/// </summary>
/// <returns></returns>
private static string GetModuleFileNameLongPath() {
    if (_moduleFileNameLongPath is null) {
        StringBuilder buffer = new StringBuilder(MAX_PATH);
        int noOfTimes = 1;
        int length = 0;
        // Iterating by allocating chunk of memory each time we find the length is not sufficient.
        // Performance should not be an issue for current MAX_PATH length due to this change.
        while (((length = GetModuleFileName(IntPtr.Zero, buffer, buffer.Capacity)) == buffer.Capacity)
               && Marshal.GetLastWin32Error() == INSUFFICIENT_BUFFER_ERROR
               && buffer.Capacity < MAX_UNICODESTRING_LEN) {
            noOfTimes += 2; // Increasing buffer size by 520 in each iteration.
            int capacity = noOfTimes * MAX_PATH < MAX_UNICODESTRING_LEN ? noOfTimes * MAX_PATH : MAX_UNICODESTRING_LEN;
            buffer.EnsureCapacity(capacity);
        }

        buffer.Length = length;
        _moduleFileNameLongPath = Path.GetFullPath(buffer.ToString());
    }

    return _moduleFileNameLongPath;
}

        #endregion

        /// <summary>
        /// Gets the path for the executable file that started the application.
        /// </summary>
        private static string GetExecutablePath() {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                return GetModuleFileNameLongPath();
            } else {
                try {
                    return Path.GetFullPath(Process.GetCurrentProcess().MainModule!.FileName!);
                } catch (Exception) {
#if NET6_0_OR_GREATER
                    //Environment.ProcessPath replaces Assembly.CodeBase, which is obsolete
                    //(SYSLIB0012) and throws outright for assemblies with no on-disk location -
                    //single-file publish being the common case, and exactly the situation in
                    //which the MainModule lookup above is most likely to have failed too.
                    var processPath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(processPath))
                        return Path.GetFullPath(processPath!);

                    throw;
#else
                    return Uri.UnescapeDataString(new UriBuilder(Assembly.GetEntryAssembly()!.CodeBase!).Path);
#endif
                }
            }
        }

        /// <summary>
        ///     The path to the entry exe, or null when it could not be determined.
        /// </summary>
        /// <remarks>
        ///     Nullable because there are real situations in which no answer exists: a
        ///     single-file or trimmed publish, a host that loaded us without a process main
        ///     module, or a platform where neither probe above succeeds. This used to be declared
        ///     non-nullable and then left unassigned on the fallback path below, so every one of
        ///     those cases handed callers a null through a reference the compiler had promised
        ///     was not null.
        /// </remarks>
        public static readonly FileInfo? ExecutingExe;

        /// <summary>
        ///     The path to the entry exe's directory.
        /// </summary>
        /// <remarks>
        ///     Always non-null: falls back to the current directory, which is what
        ///     <see cref="CombineToExecutingBase"/> resolves relative settings file names
        ///     against.
        /// </remarks>
        public static readonly DirectoryInfo ExecutingDirectory;

        static Paths() {
            try {
                ExecutingExe = new FileInfo(GetExecutablePath());
                ExecutingDirectory = ExecutingExe.Directory!;
            } catch (Exception) {
                ExecutingExe = null;
                ExecutingDirectory = new DirectoryInfo(Environment.CurrentDirectory);
            }
        }

        /// <summary>
        ///     Checks the ability to create and write to a file in the supplied directory.
        /// </summary>
        /// <param name="directory">String representing the directory path to check.</param>
        /// <returns>True if successful; otherwise false.</returns>
        public static bool IsDirectoryWritable(this DirectoryInfo directory) {
            var success = false;

            //Path.Combine, not string concatenation. DirectoryInfo.ToString() returns the path
            //exactly as it was constructed, WITHOUT a trailing separator, so `directory + "toster.txt"`
            //turned C:\foo into C:\footoster.txt - a probe against a sibling of the directory
            //being tested, in a directory that may not even exist. The answer was about the
            //wrong location every time the caller passed a path without a trailing slash.
            //
            //The name is randomised because the probe file used to be a fixed "toster.txt":
            //two processes checking the same directory at once would collide on FileMode.CreateNew
            //and one of them would conclude the directory was read-only.
            var fullPath = Path.Combine(directory.FullName, Path.GetRandomFileName());

            if (directory.Exists)
                try {
                    using (var fs = new FileStream(fullPath, FileMode.CreateNew,
                                                   FileAccess.Write)) {
                        fs.WriteByte(0xff);
                    }

                    if (File.Exists(fullPath)) {
                        File.Delete(fullPath);
                        success = true;
                    }
                } catch (Exception) {
                    success = false;
                }

            return success;
        }

        /// <summary>
        ///     Combines the file name with the dir of <see cref="Paths.ExecutingExe" />, resulting in path of a file inside the
        ///     directory of the executing exe file.
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static FileInfo CombineToExecutingBase(string filename) =>
            new FileInfo(Path.Combine(ExecutingDirectory.FullName, filename));

        /// <summary>
        ///     Compares two FileSystemInfos the right way.
        /// </summary>
        /// <returns></returns>
        public static bool CompareTo(this FileSystemInfo fi, FileSystemInfo fi2) {
            return NormalizePath(fi.FullName, true).Equals(NormalizePath(fi2.FullName, true), StringComparison.Ordinal);
        }

        /// <summary>
        ///     Compares two FileSystemInfos the right way.
        /// </summary>
        /// <returns></returns>
        public static bool CompareTo(string fi, string fi2) {
            return NormalizePath(fi, true).Equals(NormalizePath(fi2, true), StringComparison.Ordinal);
        }

        /// <summary>
        ///     Normalizes path to prepare for comparison or storage
        /// </summary>
        public static string NormalizePath(string path, bool forComparsion = false) {
            string validBackslash = "\\";
            string invalidBackslash = "/";

            //override default backslash that is used in windows.
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                validBackslash = "/";
                invalidBackslash = "\\";
            }

            path = path
                  .Replace(invalidBackslash, validBackslash)
                  .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (forComparsion) {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    path = path.ToUpperInvariant();
            }

            if (path.Contains(validBackslash))
                if (Uri.IsWellFormedUriString(path, UriKind.RelativeOrAbsolute))
                    try {
                        path = Path.GetFullPath(new Uri(path).LocalPath);
                    } catch {
                        // ignored
                    }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                //is root, fix.
                if ((path.Length == 2) && (path[1] == ':') && char.IsLetter(path[0]))
                    path = path + validBackslash;
            }

            return path;
        }

        ///
        /// Consts defined in WINBASE.H
        ///
        private enum MoveFileFlags {
            MOVEFILE_REPLACE_EXISTING = 1,
            MOVEFILE_COPY_ALLOWED = 2,
            MOVEFILE_DELAY_UNTIL_REBOOT = 4,
            MOVEFILE_WRITE_THROUGH = 8
        }


        /// <summary>
        /// Marks the file for deletion during next system reboot
        /// </summary>
        /// <param name="lpExistingFileName">The current name of the file or directory on the local computer.</param>
        /// <param name="lpNewFileName">The new name of the file or directory on the local computer.</param>
        /// <param name="dwFlags">MoveFileFlags</param>
        /// <returns>bool</returns>
        /// <remarks>http://msdn.microsoft.com/en-us/library/aa365240(VS.85).aspx</remarks>
        [DllImport("kernel32.dll", EntryPoint = "MoveFileEx")]
        private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, MoveFileFlags dwFlags);

        public static FileInfo MarkForDeletion(FileInfo file) {
            MarkForDeletion(file.FullName);
            return file;
        }

        public static string MarkForDeletion(string filename) {
            if (File.Exists(filename) == false)
                return filename;

            //MoveFileEx lives in kernel32. Unlike GetExecutablePath above, this had no platform
            //guard, so on Linux or macOS the P/Invoke raised DllNotFoundException instead of
            //quietly doing nothing - turning a best-effort cleanup into a crash on a library that
            //advertises itself as cross-platform. There is no portable equivalent of
            //delete-on-reboot, so the honest behaviour off Windows is to decline.
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return filename;

            //Usage for marking the file to delete on reboot
            MoveFileEx(filename, null, MoveFileFlags.MOVEFILE_DELAY_UNTIL_REBOOT);
            return filename;
        }

        /// <summary>
        ///     Removes or replaces all illegal characters for path in a string.
        /// </summary>
        public static string RemoveIllegalPathCharacters(string filename, string replacewith = "") =>
            string.Join(replacewith, filename.Split(Path.GetInvalidFileNameChars()));

        public class FilePathEqualityComparer : IEqualityComparer<string> {
            public bool Equals(string x, string y) {
                return Paths.CompareTo(x, y);
            }

            public int GetHashCode(string obj) {
                return Paths.NormalizePath(obj, true).GetHashCode();
            }
        }

        public class FileInfoPathEqualityComparer : IEqualityComparer<FileSystemInfo> {
            public bool Equals(FileSystemInfo x, FileSystemInfo y) {
                return Paths.CompareTo(x, y);
            }

            public int GetHashCode(FileSystemInfo obj) {
                return Paths.NormalizePath(obj.FullName, true).GetHashCode();
            }
        }
    }
}