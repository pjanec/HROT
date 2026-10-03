using System;
using System.Collections.Generic;

namespace Fdp.Toolkit.Tkb.Vfs
{
    /// <summary>
    /// Abstraction over a TKB storage medium (raw directory or ZIP archive).
    /// Implementations decide how entity files are located, read, written, and deleted.
    /// </summary>
    /// <remarks>⭐ Narrows <see cref="IAssetStorageStrategy"/> (docs/DESIGN_Asset_Management.md §3, B5).</remarks>
    public interface ITkbStorageStrategy : IAssetStorageStrategy
    {
        /// <summary>
        /// Lazily enumerates every JSON entity file in the storage.
        /// The <see cref="TkbEntityFile.JsonStream"/> must be consumed or disposed before
        /// advancing to the next result to keep memory bounded to one file at a time.
        /// </summary>
        IEnumerable<TkbEntityFile> EnumerateEntityFiles();

        /// <summary>
        /// Writes <paramref name="jsonContent"/> to the relative path within the storage root.
        /// Creates missing intermediate directories if the storage is mutable.
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// Thrown by read-only implementations such as <see cref="ZipTkbProvider"/>.
        /// </exception>
        void WriteEntityFile(string relativeFilePath, string jsonContent);

        /// <summary>
        /// Deletes the entity file at the relative path within the storage root.
        /// Does nothing if the file does not exist.
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// Thrown by read-only implementations such as <see cref="ZipTkbProvider"/>.
        /// </exception>
        void DeleteEntityFile(string relativeFilePath);
    }
}
