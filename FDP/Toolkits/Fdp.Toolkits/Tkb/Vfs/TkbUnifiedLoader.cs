using System;
using System.Collections.Generic;
using System.IO;

namespace Fdp.Toolkit.Tkb.Vfs
{
    /// <summary>
    /// Factory facade that selects the appropriate <see cref="ITkbStorageStrategy"/> based on
    /// the source path: <c>.zip</c> files use <see cref="ZipTkbProvider"/>, directories use
    /// <see cref="RawDirectoryTkbProvider"/>.
    /// </summary>
    public sealed class TkbUnifiedLoader : IDisposable
    {
        private readonly ITkbStorageStrategy _strategy;

        /// <summary>
        /// Initializes the loader, auto-detecting the storage medium from <paramref name="sourcePath"/>.
        /// </summary>
        /// <param name="sourcePath">
        /// Path to a <c>.zip</c> file or to an existing directory.
        /// The <c>.zip</c> extension check is case-insensitive.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="sourcePath"/> is neither a <c>.zip</c> file nor an
        /// existing directory.
        /// </exception>
        public TkbUnifiedLoader(string sourcePath)
        {
            if (File.Exists(sourcePath) &&
                sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                _strategy = new ZipTkbProvider(sourcePath);
            }
            else if (Directory.Exists(sourcePath))
            {
                _strategy = new RawDirectoryTkbProvider(sourcePath);
            }
            else
            {
                throw new ArgumentException(
                    $"TKB source path is not a .zip file or an existing directory: {sourcePath}",
                    nameof(sourcePath));
            }
        }

        /// <summary>
        /// Enumerates all entity files via the underlying storage strategy.
        /// </summary>
        public IEnumerable<TkbEntityFile> EnumerateEntityFiles()
            => _strategy.EnumerateEntityFiles();

        /// <summary>
        /// Disposes the underlying storage strategy.
        /// </summary>
        public void Dispose() => _strategy.Dispose();
    }
}
