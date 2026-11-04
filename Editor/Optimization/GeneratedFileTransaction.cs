using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace AvatarTuner.Editor.Optimization
{
    // Keeps previously applied generated GUIDs usable if regeneration fails or is cancelled.
    public sealed class GeneratedFileTransaction : IDisposable
    {
        private readonly string _root, _backup;
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _committed;

        public GeneratedFileTransaction(string outputRoot, string backupFolder)
        {
            _root = Path.GetFullPath(outputRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            _backup = Path.Combine(Path.GetFullPath(backupFolder), Guid.NewGuid().ToString("N"));
        }

        public void Record(string path)
        {
            var full = Path.GetFullPath(path);
            if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Output outside generated root: " + path);
            Save(full);
            Save(full + ".meta");
        }

        private void Save(string path)
        {
            if (_files.ContainsKey(path)) return;
            string backup = null;
            if (File.Exists(path))
            {
                Directory.CreateDirectory(_backup);
                backup = Path.Combine(_backup, _files.Count.ToString());
                File.Copy(path, backup);
                File.SetAttributes(backup, FileAttributes.Normal);
            }
            _files.Add(path, backup);
        }

        public void CaptureDirectory(string path)
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!(full + Path.DirectorySeparatorChar).StartsWith(_root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Output outside generated root: " + path);
            _directories.Add(full);
            if (Directory.Exists(full))
                foreach (var file in Directory.GetFiles(full, "*", SearchOption.AllDirectories)) Save(Path.GetFullPath(file));
        }

        public void Commit() { _committed = true; }

        public void Dispose()
        {
            if (!_committed)
            {
                foreach (var directory in _directories)
                    if (Directory.Exists(directory))
                        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                            if (!_files.ContainsKey(Path.GetFullPath(file))) File.Delete(file);
                foreach (var file in _files)
                {
                    if (file.Value == null) { if (File.Exists(file.Key)) File.Delete(file.Key); }
                    else if (!SameContent(file.Key, file.Value))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(file.Key));
                        File.Copy(file.Value, file.Key, true);
                    }
                }
            }
            if (Directory.Exists(_backup)) Directory.Delete(_backup, true);
        }

        private static bool SameContent(string path, string backup)
        {
            if (!File.Exists(path) || new FileInfo(path).Length != new FileInfo(backup).Length) return false;
            using (var hash = SHA256.Create())
            using (var current = File.OpenRead(path))
            using (var original = File.OpenRead(backup))
            {
                var left = hash.ComputeHash(current);
                var right = hash.ComputeHash(original);
                for (var i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
                return true;
            }
        }
    }
}
