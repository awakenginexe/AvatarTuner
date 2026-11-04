using System;
using System.Collections.Generic;
using System.IO;

namespace AvatarTuner.Editor.Optimization
{
    // Keeps previously applied generated GUIDs usable if regeneration fails or is cancelled.
    public sealed class GeneratedFileTransaction : IDisposable
    {
        private readonly string _root, _backup;
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
            }
            _files.Add(path, backup);
        }

        public void Commit() { _committed = true; }

        public void Dispose()
        {
            if (!_committed)
                foreach (var file in _files)
                {
                    if (file.Value == null) { if (File.Exists(file.Key)) File.Delete(file.Key); }
                    else File.Copy(file.Value, file.Key, true);
                }
            if (Directory.Exists(_backup)) Directory.Delete(_backup, true);
        }
    }
}
