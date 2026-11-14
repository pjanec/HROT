using System;
using System.Collections.Generic;
using System.IO;

namespace Fdp.Toolkit.Tkb.Vfs
{
    /// <summary>
    /// ⭐ <b>B5 — one read seam for an asset at rest, whether it is a TREE or an ARCHIVE</b>
    /// (docs/DESIGN_Asset_Management.md §3, <c>Q72-D</c>). <see cref="ITkbStorageStrategy"/> NARROWS it — the TKB's
    /// directory and zip providers are the two implementations, so there is no second directory/zip reader.
    /// <para>⭐ Paths are relative to the asset's root and use <c>/</c>.</para>
    /// </summary>
    public interface IAssetStorageStrategy : IDisposable
    {
        /// <summary>Every file of the asset, at any depth.</summary>
        IEnumerable<string> EnumerateFiles();

        /// <summary>Opens one file for reading. The caller disposes the stream.</summary>
        Stream OpenRead(string relativePath);
    }

    /// <summary>Opens an asset at rest through the seam: a directory as a tree, a <c>.zip</c> as an archive.</summary>
    public static class AssetStorage
    {
        public static IAssetStorageStrategy Open(string path)
            => Directory.Exists(path) ? new RawDirectoryTkbProvider(path)
             : File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? new ZipTkbProvider(path)
             : throw new FileNotFoundException($"[AssetStorage] '{path}' is neither a directory nor a .zip archive.", path);
    }
}
