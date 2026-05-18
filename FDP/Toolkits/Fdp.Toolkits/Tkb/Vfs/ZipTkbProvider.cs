using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Fdp.Toolkit.Tkb.Vfs
{
    /// <summary>
    /// <see cref="ITkbStorageStrategy"/> backed by a ZIP archive opened with
    /// <see cref="ZipArchiveMode.Read"/>. Strictly read-only at runtime.
    /// ZIP archives are created by a CI/CD build step; they are never written
    /// through the VFS interface.
    /// </summary>
    public sealed class ZipTkbProvider : ITkbStorageStrategy
    {
        private readonly ZipArchive _archive;

        /// <summary>
        /// Opens the ZIP archive at <paramref name="archivePath"/> in read-only mode.
        /// </summary>
        public ZipTkbProvider(string archivePath)
        {
            _archive = ZipFile.Open(archivePath, ZipArchiveMode.Read);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Skips directory markers (entries whose <c>FullName</c> ends with '/') and
        /// non-<c>.json</c> entries. Backslashes in entry paths are normalized to
        /// forward slashes.
        /// </remarks>
        public IEnumerable<TkbEntityFile> EnumerateEntityFiles()
        {
            foreach (var entry in _archive.Entries)
            {
                // Skip directory markers
                if (entry.FullName.EndsWith('/'))
                    continue;

                // Skip non-json entries
                if (!entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Normalize path separators
                var fullNameFwd = entry.FullName.Replace('\\', '/');

                // Derive category path from directory portion
                var lastSlash = fullNameFwd.LastIndexOf('/');
                var categoryPath = lastSlash >= 0 ? fullNameFwd[..lastSlash] : string.Empty;

                var fileName = Path.GetFileNameWithoutExtension(entry.Name);

                using var stream = entry.Open();
                yield return new TkbEntityFile(categoryPath, fileName, stream);
            }
        }

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">Always — ZIP provider is read-only.</exception>
        public void WriteEntityFile(string relativeFilePath, string jsonContent)
            => throw new NotSupportedException(
                "ZipTkbProvider is read-only. Use RawDirectoryTkbProvider for authoring.");

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">Always — ZIP provider is read-only.</exception>
        public void DeleteEntityFile(string relativeFilePath)
            => throw new NotSupportedException(
                "ZipTkbProvider is read-only. Use RawDirectoryTkbProvider for authoring.");

        /// <inheritdoc/>
        public void Dispose() => _archive.Dispose();
    }
}
