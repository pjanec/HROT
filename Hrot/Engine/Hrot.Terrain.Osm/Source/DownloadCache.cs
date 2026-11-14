using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Hrot.Terrain.Osm;

/// <summary>⭐ A folder per area hash, so the preview and the import (and a re-import with other options) download once (§5.2).</summary>
public sealed class DownloadCache
{
    public string Root { get; }
    public DownloadCache(string root) => Root = root;

    public string FolderFor(GeoBox box)
    {
        string key = string.Create(CultureInfo.InvariantCulture, $"{box.South:F6},{box.West:F6},{box.North:F6},{box.East:F6}");
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant()[..16];
        return Path.Combine(Root, hash);
    }

    /// <summary>The cached file, fetching it (into a temp name, then renamed) when it is missing.</summary>
    public async Task<string> GetOrFetchAsync(GeoBox box, string fileName, Func<string, Task> fetch)
    {
        string folder = FolderFor(box);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, fileName);
        if (File.Exists(path) && new FileInfo(path).Length > 0) return path;
        string tmp = path + ".part-" + Guid.NewGuid().ToString("N");
        try
        {
            await fetch(tmp).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
        return path;
    }
}
