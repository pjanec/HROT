using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ O-4 (CE-4415, DESIGN_Terrain_Authoring §18a) — a terrain's / an overlay's CONTENT identity (SHA-256, lowercase hex), so a NAME no longer stands for bytes:
    /// an overlay names the hash of the base it was compiled against and a node refuses a mismatch.
    /// <para><see cref="OfTerrain"/>: every file of the terrain folder except <c>navtiles/</c>, <c>source/</c> and <c>terrain.hash</c>, in ordinal relative-path order (path, length, bytes);
    /// cached in <c>&lt;folder&gt;/terrain.hash</c> keyed by each file's (length, mtime ticks) — a height store of 127 MB is hashed once, then re-read only if a file changed.
    /// <see cref="OfOverlay"/>: the world file and <c>buildings/*</c> (not <c>overlay.json</c>, which carries the hash; not <c>navtiles/</c>, a derived cache).</para>
    /// </summary>
    public static class TerrainContentHash
    {
        public const string CacheFileName = "terrain.hash";

        public static string OfTerrain(string definitionPath)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(definitionPath))!;
            var files = new List<(string Rel, FileInfo Info)>();
            foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(folder, path).Replace('\\', '/');
                if (IsExcluded(rel)) continue;
                files.Add((rel, new FileInfo(path)));
            }
            files.Sort((a, b) => string.CompareOrdinal(a.Rel, b.Rel));

            string cachePath = Path.Combine(folder, CacheFileName);
            if (TryReadCache(cachePath, files, out var cached)) return cached!;

            string hash = Hash(files);
            TryWriteCache(cachePath, hash, files);
            return hash;
        }

        public static string OfOverlay(string overlayFolder, string? baseHash = null)   // ⭐ baseHash: the base the parts were compiled over (null = the header's)
        {
            string folder = Path.GetFullPath(overlayFolder);
            string worldName = TerrainOverlayHeader.DefaultWorldFileName;
            string headerPath = Path.Combine(folder, TerrainOverlayHeader.FileName);
            string? headerBase = null;
            if (File.Exists(headerPath))
                try { var h = TerrainOverlayHeader.Parse(File.ReadAllText(headerPath), headerPath); worldName = h.World; headerBase = h.BaseHash; } catch (ArgumentException) { }
            baseHash ??= headerBase ?? "";
            var files = new List<(string Rel, FileInfo Info)>();
            var world = new FileInfo(Path.Combine(folder, worldName));
            if (world.Exists) files.Add((worldName, world));
            string buildings = Path.Combine(folder, TerrainAssets.BuildingsFolderName);
            if (Directory.Exists(buildings))
                foreach (var path in Directory.EnumerateFiles(buildings, "*", SearchOption.AllDirectories))
                    files.Add((Path.GetRelativePath(folder, path).Replace('\\', '/'), new FileInfo(path)));
            files.Sort((a, b) => string.CompareOrdinal(a.Rel, b.Rel));
            // ⭐ the BASE terrain's hash is part of the overlay's identity: the same parts over another base are another overlay (its navtiles differ),
            //   and the staged folder is named by this hash — two scenarios must never share one folder across bases
            return Hash(files, baseHash);
        }

        private static bool IsExcluded(string rel)
            => rel == CacheFileName || rel.StartsWith("navtiles/", StringComparison.Ordinal) || rel.StartsWith("source/", StringComparison.Ordinal);

        private static string Hash(List<(string Rel, FileInfo Info)> files, string? prefix = null)
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            if (!string.IsNullOrEmpty(prefix)) { sha.AppendData(Encoding.UTF8.GetBytes("base:" + prefix)); sha.AppendData(new byte[] { 0 }); }
            var buffer = new byte[1 << 20];
            Span<byte> len = stackalloc byte[8];
            foreach (var (rel, info) in files)
            {
                sha.AppendData(Encoding.UTF8.GetBytes(rel));
                sha.AppendData(new byte[] { 0 });
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(len, info.Length);
                sha.AppendData(len);
                using var s = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
                int n;
                while ((n = s.Read(buffer, 0, buffer.Length)) > 0) sha.AppendData(buffer, 0, n);
            }
            return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        }

        private sealed record Entry(string Path, long Length, long MtimeTicks);
        private sealed record Cache(string Hash, List<Entry> Files);

        private static bool TryReadCache(string cachePath, List<(string Rel, FileInfo Info)> files, out string? hash)
        {
            hash = null;
            try
            {
                if (!File.Exists(cachePath)) return false;
                var c = JsonSerializer.Deserialize<Cache>(File.ReadAllText(cachePath));
                if (c == null || string.IsNullOrEmpty(c.Hash) || c.Files == null || c.Files.Count != files.Count) return false;
                for (int i = 0; i < files.Count; i++)
                {
                    var e = c.Files[i]; var (rel, info) = files[i];
                    if (e.Path != rel || e.Length != info.Length || e.MtimeTicks != info.LastWriteTimeUtc.Ticks) return false;
                }
                hash = c.Hash;
                return true;
            }
            catch (Exception) { return false; }
        }

        private static void TryWriteCache(string cachePath, string hash, List<(string Rel, FileInfo Info)> files)
        {
            try
            {
                var entries = new List<Entry>(files.Count);
                foreach (var (rel, info) in files) entries.Add(new Entry(rel, info.Length, info.LastWriteTimeUtc.Ticks));
                File.WriteAllText(cachePath, JsonSerializer.Serialize(new Cache(hash, entries)));
            }
            catch (Exception) { }   // a read-only folder (the NAS): just no cache
        }
    }
}
