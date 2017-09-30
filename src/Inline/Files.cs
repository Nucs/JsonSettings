using System;
using System.IO;

namespace nucs.JsonSettings {
    public static class Files {
        public static FileStream AttemptOpenFile(this FileInfo file, FileMode filemode = FileMode.Open, FileAccess fileaccess = FileAccess.Read, FileShare fileshare = FileShare.None) {
            return AttemptOpenFile(file?.FullName, filemode, fileaccess, fileshare);
        }

        public static FileStream AttemptOpenFile(string file, FileMode filemode = FileMode.Open, FileAccess fileaccess = FileAccess.Read, FileShare fileshare = FileShare.None) {
            if (string.IsNullOrEmpty(file)) throw new ArgumentException("message", nameof(file));

            FileStream stream = null;
            if (File.Exists(file) == false && filemode != FileMode.Create && filemode != FileMode.CreateNew && filemode != FileMode.OpenOrCreate)
                return null;
            try {
                return File.Open(file, filemode, fileaccess, fileshare);
            } catch (IOException) {
                //the file is unavailable because it is:
                //still being written to
                //or being processed by another thread
                //or does not exist (has already been processed)
                return null;
            } catch (UnauthorizedAccessException) {
                return null;
            }
        }

#if NETSTANDARD1_6

        public static void Close(this Stream str) {
            str.Dispose();
        }

#endif
    }
}