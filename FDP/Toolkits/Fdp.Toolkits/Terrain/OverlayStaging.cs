using System;
using System.IO;
using System.Linq;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ T-6 (CE-3432, docs/DESIGN_Terrain_Authoring.md §18a-4) — the NODE's staged overlay folders (<c>{node}/Terrain/overlays/&lt;hash&gt;/</c>, content-addressed: every re-save of a scenario with blocks stages a new one) are a CACHE, kept like the recording cache
    /// (<c>ExerciseRetention.PruneArchivedCopies</c>, CE-4330 ④): the overlay the current load needs, plus the <see cref="ResolveKeep"/> most recently USED others — by the folder's last-access time, which <see cref="Touch"/> sets on every use (a prefetch, a load). Run at
    /// prefetch time, after the current overlay is staged. ⛔ The NAS by-hash copies (O-8) are NOT pruned here: recordings name them.
    /// </summary>
    public static class OverlayStaging
    {
        /// <summary>How many other staged overlays a node keeps by default.</summary>
        public const int DefaultKeep = 8;

        /// <summary>Environment setting for the count (a positive number).</summary>
        public const string KeepVariable = "HROT_KEEP_OVERLAYS";

        public static int ResolveKeep(Func<string, string?>? environment = null)
        {
            environment ??= Environment.GetEnvironmentVariable;
            return int.TryParse(environment(KeepVariable), out var n) && n > 0 ? n : DefaultKeep;
        }

        /// <summary>Marks an overlay folder as used now (its last-access time); a failure is ignored (a read-only share keeps the older stamp).</summary>
        public static void Touch(string? overlayFolder, DateTime? utcNow = null)
        {
            if (string.IsNullOrEmpty(overlayFolder) || !Directory.Exists(overlayFolder)) return;
            try { Directory.SetLastAccessTimeUtc(overlayFolder, utcNow ?? DateTime.UtcNow); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        private static DateTime UsedAt(DirectoryInfo d) => d.LastAccessTimeUtc > d.LastWriteTimeUtc ? d.LastAccessTimeUtc : d.LastWriteTimeUtc;

        /// <summary>
        /// In <paramref name="overlaysDir"/>: keeps <paramref name="currentHash"/> (never deleted) and the <paramref name="keepRecent"/> most recently used of the others; deletes the rest (a folder in use that cannot be deleted is skipped). Returns how many were deleted.
        /// </summary>
        public static int Prune(string overlaysDir, string? currentHash, int keepRecent, Action<string>? log = null)
        {
            if (string.IsNullOrEmpty(overlaysDir) || !Directory.Exists(overlaysDir) || keepRecent < 0) return 0;
            var others = new DirectoryInfo(overlaysDir).GetDirectories()
                .Where(d => !string.Equals(d.Name, currentHash, StringComparison.Ordinal))
                .OrderByDescending(UsedAt).ThenBy(d => d.Name, StringComparer.Ordinal)
                .ToList();
            int removed = 0;
            for (int i = keepRecent; i < others.Count; i++)
            {
                try { others[i].Delete(recursive: true); removed++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            if (removed > 0) log?.Invoke($"[Terrain] staged overlays: removed {removed} least recently used folder(s) under {overlaysDir}, keeping the current one and the {keepRecent} newest");
            return removed;
        }
    }
}
