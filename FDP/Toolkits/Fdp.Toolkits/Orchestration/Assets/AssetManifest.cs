using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Fdp.Toolkit.Orchestration.Assets
{
    /// <summary>
    /// ⭐ One file of an asset tree at rest — the per-file identity the whole asset scheme keys on
    /// (docs/DESIGN_Asset_Management.md §2: <i>"the per-file (relative path, length, mtime) manifest — on the
    /// AT-REST pair, never on the transport"</i>).
    /// <para>⭐ <see cref="RelativePath"/> always uses <c>/</c>, so a manifest taken on Windows and one taken on Linux
    /// compare equal.</para>
    /// </summary>
    public readonly record struct AssetManifestEntry(string RelativePath, long Length, DateTime LastWriteUtc)
    {
        /// <summary>
        /// ⭐⭐ <b>The ONE freshness predicate</b>: same length AND the exact same last-write time — byte for byte the rule
        /// <c>StorageGatewayModule.IsAlreadyCurrent</c> already applies (design §2 "one predicate", <c>A3</c>).
        /// ⛔ A second, looser rule here would be a review finding.
        /// </summary>
        public bool IsSameContentAs(in AssetManifestEntry other)
            => Length == other.Length && LastWriteUtc == other.LastWriteUtc;
    }

    /// <summary>
    /// ⭐⭐ <b>An asset tree's manifest</b> — every file under a root, RECURSIVELY, with its relative path, length and
    /// last-write time (docs/DESIGN_Asset_Management.md §3, §8 increment A). The SAME walker scans the NAS side and
    /// the node side (<c>Q72-K</c>: the node's tree mirrors NAS in shape AND structure).
    /// </summary>
    public sealed class AssetManifest
    {
        private readonly Dictionary<string, AssetManifestEntry> _byPath;

        public AssetManifest(IEnumerable<AssetManifestEntry> entries)
        {
            _byPath = new Dictionary<string, AssetManifestEntry>(StringComparer.Ordinal);
            foreach (var e in entries) _byPath[Normalize(e.RelativePath)] = e with { RelativePath = Normalize(e.RelativePath) };
        }

        /// <summary>The entries, ordered by relative path.</summary>
        public IReadOnlyList<AssetManifestEntry> Entries
            => _byPath.Values.OrderBy(e => e.RelativePath, StringComparer.Ordinal).ToList();

        public int Count => _byPath.Count;

        public bool TryGet(string relativePath, out AssetManifestEntry entry)
            => _byPath.TryGetValue(Normalize(relativePath), out entry);

        /// <summary>
        /// ⭐⭐ <b>The ONE recursive walker</b> (task <c>A2</c>) — every file under <paramref name="root"/>, at any depth,
        /// keyed by its path relative to the root. A missing root is an EMPTY manifest (a node that never received
        /// the asset), never an error.
        /// </summary>
        public static AssetManifest Scan(string root)
        {
            if (!Directory.Exists(root)) return new AssetManifest(Array.Empty<AssetManifestEntry>());
            var entries = new List<AssetManifestEntry>();
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(path);
                entries.Add(new AssetManifestEntry(Path.GetRelativePath(root, path), info.Length, info.LastWriteTimeUtc));
            }
            return new AssetManifest(entries);
        }

        /// <summary>
        /// ⭐⭐ <b>The three-set diff</b> (task <c>A1</c>), with THIS as the SOURCE (NAS) and <paramref name="target"/> as
        /// the node: <b>Added</b> = only in the source · <b>Changed</b> = in both but not the same content · <b>Removed</b>
        /// = only on the target. ⛔ A one-sided diff cannot say "present on the node, absent on NAS", so a deleted or
        /// renamed NAS file would orphan on every node — <b>Removed</b> is what makes "mirror" testable. Whether a
        /// removed entry is deleted is the SYNC's decision (design §9-W4), not the diff's.
        /// </summary>
        public AssetManifestDiff Diff(AssetManifest target)
        {
            var added = new List<AssetManifestEntry>();
            var changed = new List<AssetManifestEntry>();
            var removed = new List<AssetManifestEntry>();

            foreach (var src in Entries)
            {
                if (!target._byPath.TryGetValue(src.RelativePath, out var dst)) added.Add(src);
                else if (!src.IsSameContentAs(dst)) changed.Add(src);
            }
            foreach (var dst in target.Entries)
                if (!_byPath.ContainsKey(dst.RelativePath)) removed.Add(dst);

            return new AssetManifestDiff(added, changed, removed);
        }

        private static string Normalize(string relativePath) => relativePath.Replace('\\', '/');
    }

    /// <summary>The result of <see cref="AssetManifest.Diff"/> — source entries to copy (added + changed) and target
    /// entries the source no longer has (removed).</summary>
    public sealed record AssetManifestDiff(
        IReadOnlyList<AssetManifestEntry> Added,
        IReadOnlyList<AssetManifestEntry> Changed,
        IReadOnlyList<AssetManifestEntry> Removed)
    {
        /// <summary>True when the target already mirrors the source.</summary>
        public bool IsEmpty => Added.Count == 0 && Changed.Count == 0 && Removed.Count == 0;

        /// <summary>The entries to transfer, source-side: added then changed.</summary>
        public IEnumerable<AssetManifestEntry> ToCopy => Added.Concat(Changed);
    }
}
