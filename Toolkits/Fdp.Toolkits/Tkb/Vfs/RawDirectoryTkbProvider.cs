using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Fdp.Toolkit.Tkb.Vfs
{
    /// <summary>
    /// <see cref="ITkbStorageStrategy"/> backed by a raw directory on disk.
    /// Used for authoring (TKB Editor writes here) and for debug/dev runs with a folder layout.
    /// </summary>
    public sealed class RawDirectoryTkbProvider : ITkbStorageStrategy
    {
        private readonly string _rootPath;

        /// <summary>
        /// Initializes a new <see cref="RawDirectoryTkbProvider"/> rooted at
        /// <paramref name="rootPath"/>.
        /// </summary>
        public RawDirectoryTkbProvider(string rootPath)
        {
            _rootPath = rootPath;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Recursively enumerates all <c>*.json</c> files under the root. Only one
        /// <see cref="FileStream"/> is open at a time; it is disposed when the enumerator
        /// advances to the next entry.
        /// </remarks>
        public IEnumerable<TkbEntityFile> EnumerateEntityFiles()
        {
            foreach (var filePath in Directory.EnumerateFiles(_rootPath, "*.json", SearchOption.AllDirectories))
            {
                var relPath = Path.GetRelativePath(_rootPath, filePath);
                var dirPart = Path.GetDirectoryName(relPath) ?? string.Empty;
                // Normalize to forward slashes and strip any trailing slash
                var categoryPath = dirPart.Replace('\\', '/').TrimEnd('/');
                var fileName = Path.GetFileNameWithoutExtension(filePath);

                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                yield return new TkbEntityFile(categoryPath, fileName, stream);
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Creates missing intermediate directories and writes UTF-8 (no BOM) content.
        /// </remarks>
        public void WriteEntityFile(string relativeFilePath, string jsonContent)
        {
            var fullPath = Path.Combine(_rootPath, relativeFilePath);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(fullPath, jsonContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        /// <inheritdoc/>
        /// <remarks>Does nothing if the file does not exist.</remarks>
        public void DeleteEntityFile(string relativeFilePath)
        {
            var fullPath = Path.Combine(_rootPath, relativeFilePath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }

        /// <inheritdoc/>
        /// <remarks>No-op — file streams are closed per iteration by the enumerator.</remarks>
        public void Dispose() { }
    }
}
