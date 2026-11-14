using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Fdp.Toolkit.Orchestration.Assets
{
    /// <summary>
    /// ⭐ <b>B3 — which differing files travel ALONE and which travel in ONE archive</b> (docs/DESIGN_Asset_Management.md
    /// §7.2, <c>Q72-H1</c>). 🔒 Not a CPU argument: a 100 GB member would make the archive 100 GB+ and need the space twice
    /// to unpack, so big and already-compressed files go standalone and the rest — the many small files the partition
    /// exists for — go in one archive. <i>One 100 GB heightmap + 3 000 small files ⇒ 2 transfers.</i>
    /// <para>⚠ The threshold and the extension set are tuning values (design §9-W1), configurable here.</para>
    /// </summary>
    public sealed class TransportPartitioner
    {
        /// <summary>Default: files of 64 MiB or more travel standalone.</summary>
        public const long DefaultStandaloneBytes = 64L * 1024 * 1024;

        /// <summary>Default pre-compressed extensions — packing them again buys nothing.</summary>
        public static readonly IReadOnlyCollection<string> DefaultStandaloneExtensions = new[]
        {
            ".zip", ".7z", ".gz", ".rar", ".png", ".jpg", ".jpeg", ".dds", ".ktx", ".ktx2", ".mp4", ".mkv", ".webm",
        };

        public long StandaloneBytes { get; init; } = DefaultStandaloneBytes;
        public IReadOnlyCollection<string> StandaloneExtensions { get; init; } = DefaultStandaloneExtensions;

        /// <summary>True when <paramref name="entry"/> travels as its own copy.</summary>
        public bool IsStandalone(in AssetManifestEntry entry)
            => entry.Length >= StandaloneBytes
            || StandaloneExtensions.Contains(Path.GetExtension(entry.RelativePath), StringComparer.OrdinalIgnoreCase);

        /// <summary>Splits <paramref name="entries"/> into the standalone copies and the archived set.</summary>
        public (IReadOnlyList<AssetManifestEntry> Standalone, IReadOnlyList<AssetManifestEntry> Archived) Partition(
            IEnumerable<AssetManifestEntry> entries)
        {
            var standalone = new List<AssetManifestEntry>();
            var archived = new List<AssetManifestEntry>();
            foreach (var e in entries) (IsStandalone(e) ? standalone : archived).Add(e);
            return (standalone, archived);
        }
    }

    /// <summary>How a tree sync may touch the destination (docs/DESIGN_Asset_Management.md §7.3b).</summary>
    public enum AssetSyncMode
    {
        /// <summary>⭐ The destination becomes a MIRROR of the source: added and changed files are written and files the
        /// source no longer has are DELETED (design §9-W4, answered: <i>delete</i> — §2's word is "mirror").</summary>
        Mirror = 0,

        /// <summary>⭐⭐ §7.3b clause ③ — for a kind the destination AUTHORS: only files it does not have are added.
        /// ⛔ Never overwrite, never delete — adding a file the node lacks cannot destroy work.</summary>
        AddOnly = 1,

        /// <summary>⭐ The explicit publish (node→NAS) and refresh (NAS→author) — design §10 D6: add what the destination lacks
        /// and replace a file ONLY where the source copy is NEWER. ⛔ Never delete, ⛔ never put an older file over a newer
        /// one — with several authors, a mirror-publish would wipe or roll back other people's work on NAS.</summary>
        UpdateNewer = 2,
    }

    /// <summary>What an <see cref="AssetSyncMode.UpdateNewer"/> sync WOULD do — the refresh's "name the files it will replace"
    /// (design §7.3c), and the publish's transfer set.</summary>
    public sealed record AssetUpdatePlan(
        IReadOnlyList<AssetManifestEntry> ToAdd,
        IReadOnlyList<AssetManifestEntry> ToReplace,
        IReadOnlyList<AssetManifestEntry> DestinationNewer)
    {
        public IEnumerable<AssetManifestEntry> ToWrite => ToAdd.Concat(ToReplace);
    }

    /// <summary>⭐ C2 — a kind's tree in summary: file count and newest last-write time. Stat only: no file is opened.</summary>
    public readonly record struct AssetTreeSummary(int Count, DateTime NewestUtc)
    {
        public static AssetTreeSummary Of(AssetManifest m)
            => new(m.Count, m.Count == 0 ? DateTime.MinValue : m.Entries.Max(e => e.LastWriteUtc));
    }

    /// <summary>What one tree sync did.</summary>
    public sealed record AssetSyncResult(
        int CopiedStandalone, int Unpacked, int Deleted, int HeldBackChanged, int HeldBackRemoved)
    {
        /// <summary>Every write the sync made to the destination (copies + unpacked files + deletions).</summary>
        public int Writes => CopiedStandalone + Unpacked + Deleted;
    }

    /// <summary>
    /// ⭐⭐ <b>B4 — make one destination tree match one source tree</b> (docs/DESIGN_Asset_Management.md §4): manifest both
    /// sides, diff, partition the differing set, copy the standalone files, carry the rest in ONE archive unpacked in
    /// place — ⭐⭐⭐ <b>then RESTORE each unpacked file's last-write time from the source manifest</b>. ZIP stores DOS
    /// timestamps on a 2-second grid; without the restore every archived file would compare unequal forever and
    /// re-transfer on every sync (design §2). With it, a second sync with no source change writes NOTHING.
    /// <para>⭐ Pure filesystem — the orchestrator's prefetch saga drives it per node and per asset (B4a), and the
    /// explicit publish / refresh (increment C) reuse it pointed the other way.</para>
    /// </summary>
    public sealed class AssetTreeSync
    {
        private readonly TransportPartitioner _partitioner;

        public AssetTreeSync(TransportPartitioner? partitioner = null) => _partitioner = partitioner ?? new TransportPartitioner();

        /// <summary>Synchronises <paramref name="destRoot"/> from <paramref name="sourceRoot"/>. A missing source is an
        /// error (the caller named an asset that is not there); a missing destination is simply empty.</summary>
        public AssetSyncResult Sync(string sourceRoot, string destRoot, AssetSyncMode mode = AssetSyncMode.Mirror)
        {
            if (!Directory.Exists(sourceRoot))
                throw new DirectoryNotFoundException($"[AssetSync] source tree '{sourceRoot}' does not exist.");

            var source = AssetManifest.Scan(sourceRoot);
            var diff = source.Diff(AssetManifest.Scan(destRoot));
            if (diff.IsEmpty) return new AssetSyncResult(0, 0, 0, 0, 0);

            var toWrite = mode switch
            {
                AssetSyncMode.Mirror      => diff.ToCopy.ToList(),
                AssetSyncMode.UpdateNewer => PlanUpdateNewer(source, AssetManifest.Scan(destRoot)).ToWrite.ToList(),
                _                         => diff.Added.ToList(),
            };
            var (standalone, archived) = _partitioner.Partition(toWrite);

            foreach (var e in standalone)
            {
                var dest = PathFor(destRoot, e);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(PathFor(sourceRoot, e), dest, overwrite: true);   // File.Copy keeps the source mtime
            }

            int unpacked = archived.Count == 0 ? 0 : CarryInOneArchive(sourceRoot, destRoot, archived);

            int deleted = 0;
            if (mode == AssetSyncMode.Mirror)
            {
                foreach (var e in diff.Removed)
                {
                    File.Delete(PathFor(destRoot, e));
                    deleted++;
                }
            }

            return new AssetSyncResult(
                standalone.Count, unpacked, deleted,
                HeldBackChanged: mode == AssetSyncMode.Mirror ? 0 : diff.Changed.Count - (toWrite.Count - diff.Added.Count),
                HeldBackRemoved: mode == AssetSyncMode.Mirror ? 0 : diff.Removed.Count);
        }

        /// <summary>The <see cref="AssetSyncMode.UpdateNewer"/> plan for <paramref name="source"/> → <paramref name="dest"/>.</summary>
        public static AssetUpdatePlan PlanUpdateNewer(AssetManifest source, AssetManifest dest)
        {
            var diff = source.Diff(dest);
            var replace = new List<AssetManifestEntry>();
            var destNewer = new List<AssetManifestEntry>();
            foreach (var e in diff.Changed)
            {
                dest.TryGet(e.RelativePath, out var d);
                (e.LastWriteUtc > d.LastWriteUtc ? replace : destNewer).Add(e);
            }
            return new AssetUpdatePlan(diff.Added, replace, destNewer);
        }

        /// <summary>The plan for two trees on disk.</summary>
        public static AssetUpdatePlan PlanUpdateNewer(string sourceRoot, string destRoot)
            => PlanUpdateNewer(AssetManifest.Scan(sourceRoot), AssetManifest.Scan(destRoot));

        private static int CarryInOneArchive(string sourceRoot, string destRoot, IReadOnlyList<AssetManifestEntry> entries)
        {
            string archive = Path.Combine(Path.GetTempPath(), $"hrot-asset-{Guid.NewGuid():N}.zip");
            try
            {
                using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                    foreach (var e in entries)
                        zip.CreateEntryFromFile(PathFor(sourceRoot, e), e.RelativePath, CompressionLevel.Fastest);

                Directory.CreateDirectory(destRoot);
                ZipFile.ExtractToDirectory(archive, destRoot, overwriteFiles: true);

                // ⭐⭐⭐ The named success condition (design §2): the member's mtime comes from the MANIFEST, not the zip.
                foreach (var e in entries)
                    File.SetLastWriteTimeUtc(PathFor(destRoot, e), e.LastWriteUtc);
                return entries.Count;
            }
            finally
            {
                try { File.Delete(archive); } catch (IOException) { }
            }
        }

        private static string PathFor(string root, in AssetManifestEntry e)
            => Path.Combine(root, e.RelativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
