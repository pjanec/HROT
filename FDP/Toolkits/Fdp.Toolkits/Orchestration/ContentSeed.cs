using System;
using System.Collections.Generic;
using System.IO;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐⭐⭐ <b>THE CONTENT SEED — the repository is the DEVELOPMENT SEED of the NAS</b> (<c>CE-4089</c>, R-4097, docs/DESIGN_Content_Layout.md §2, L4;
    /// moved here by <c>CE-3340</c> ③ so the ONE host start can call it). A dev start copies the repository's <c>scenarios/</c>, <c>assets/</c> and
    /// <c>recipes/</c> (<see cref="ContentLayout.SeededDirectories"/>) into the NAS root, each file by the CE-1365 atomic replace-if-different
    /// write; every node then reads ONLY the NAS. A production install has no source tree (<see cref="ContentLayout.TryFindSourceTree"/> is
    /// <c>null</c>), so the seed does nothing. ⛔ Nothing is ever deleted.
    /// <para>⭐ <b>Called from exactly one place:</b> <see cref="Runner.SubsystemOrchestrator.Initialize"/> — every host's start (the cluster runner,
    /// the editor inside it, the in-process test harnesses) passes through it, before any subsystem initialises. The reverse direction (<i>Save to
    /// Git</i>, repository ← NAS) stays in <c>CuratedScenarios</c> (Hrot.Presentation), which reuses <see cref="CopyTree"/> and
    /// <see cref="CuratedRelPaths"/>.</para>
    /// </summary>
    public static class ContentSeed
    {
        /// <summary>The marker file that makes a folder a scenario (matches the runtime enumerator).</summary>
        public const string MarkerFileName = "scenario.json";

        /// <summary>
        /// <b>Copy the repository's <c>scenarios/</c>, <c>assets/</c> and <c>recipes/</c> into the NAS root</b> — each file only when its bytes differ.
        /// Returns the NAS-relative paths (<c>/</c>-separated) that were WRITTEN: empty when nothing differed, and empty (never an error) when there is
        /// no source tree — a deployed build has nothing to seed.
        /// </summary>
        /// <param name="nasRoot">The NAS root (<see cref="ContentLayout.NasRoot"/>), NOT its <c>scenarios/</c> folder.</param>
        /// <param name="sourceSearchStartDirs">Where the repository walk-up starts; none ⇒ the working directory, then the app's base directory.</param>
        public static IReadOnlyList<string> SeedIntoNas(string nasRoot, params string[] sourceSearchStartDirs)
        {
            if (string.IsNullOrWhiteSpace(nasRoot)) throw new ArgumentException("A NAS root is required.", nameof(nasRoot));

            var tree = ContentLayout.TryFindSourceTree(sourceSearchStartDirs);
            return tree is null ? Array.Empty<string>() : SeedTreeFrom(tree, nasRoot);
        }

        /// <summary>
        /// The pure copy behind <see cref="SeedIntoNas"/> — exposed so the behaviour is testable without depending on the walk-up's result.
        /// <c>scenarios/</c>: the curated names (every folder holding a <see cref="MarkerFileName"/>); <c>assets/</c> and <c>recipes/</c>: every file.
        /// </summary>
        public static IReadOnlyList<string> SeedTreeFrom(string sourceTree, string nasRoot)
        {
            var written = new List<string>();
            foreach (var top in ContentLayout.SeededDirectories)
            {
                var source = Path.Combine(sourceTree, top);
                var target = Path.Combine(nasRoot, top);
                if (!Directory.Exists(source)) continue;

                if (top == ContentLayout.ScenariosDirectoryName)
                    foreach (var rel in CuratedRelPaths(source))
                        CopyTree(Path.Combine(source, rel), Path.Combine(target, rel), top + "/" + rel, written);
                else
                    CopyTree(source, target, top, written);
            }
            return written;
        }

        /// <summary>
        /// The scenario-only overlay — every curated name of <paramref name="sourceRoot"/> into <paramref name="workingRoot"/>, force-overwriting only
        /// those names. Returns the curated relative paths copied.
        /// </summary>
        public static IReadOnlyList<string> SeedFrom(string sourceRoot, string workingRoot)
        {
            var copied = new List<string>();
            foreach (var rel in CuratedRelPaths(sourceRoot))
            {
                CopyTree(Path.Combine(sourceRoot, rel), Path.Combine(workingRoot, rel), rel, null);
                copied.Add(rel);
            }
            return copied;
        }

        /// <summary>
        /// The curated relative paths under <paramref name="root"/> — every folder that contains a <see cref="MarkerFileName"/>, forward-slash
        /// normalized and ordinally sorted. Supports nesting (e.g. <c>Combat/Ambush</c>), matching the runtime enumerator.
        /// </summary>
        public static IReadOnlyList<string> CuratedRelPaths(string root)
        {
            if (!Directory.Exists(root)) return Array.Empty<string>();

            var rels = new List<string>();
            foreach (var marker in Directory.EnumerateFiles(root, MarkerFileName, SearchOption.AllDirectories))
            {
                var dir = Path.GetDirectoryName(marker);
                if (dir is null) continue;
                var rel = Path.GetRelativePath(root, dir).Replace('\\', '/');
                if (rel != ".") rels.Add(rel);
            }
            rels.Sort(StringComparer.Ordinal);
            return rels;
        }

        /// <summary>
        /// Copy a folder (all its files, recursively) from <paramref name="src"/> to <paramref name="dst"/>, each file by
        /// <see cref="ReplaceIfDifferent"/>. Creates <paramref name="dst"/> and any subdirectories; never deletes. ⛔ Skips the temp files the
        /// atomic write itself leaves beside a target (<c>*.tmp</c>). Appends the label-relative path of every file WRITTEN to
        /// <paramref name="written"/> when given.
        /// </summary>
        public static void CopyTree(string src, string dst, string label, List<string>? written)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".tmp", StringComparison.Ordinal)) continue;
                var rel = Path.GetRelativePath(src, file);
                var target = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (ReplaceIfDifferent(file, target))
                    written?.Add(label + "/" + rel.Replace('\\', '/'));
            }
        }

        /// <summary>
        /// ⭐ CE-1365 — the NAS root is SHARED (every editor and test host on the machine seeds the same one), so a copy must never be
        /// seen half-written: identical bytes are left alone (returns <c>false</c>), anything else is written to a unique temp file beside the
        /// target and RENAMED over it (atomic on one volume; returns <c>true</c>). ⛔ Was <c>File.Copy(overwrite)</c> in place — two parallel seeds
        /// raced on <c>golden.json</c> and a reader between them read a truncated file.
        /// </summary>
        private static bool ReplaceIfDifferent(string source, string target)
        {
            var bytes = File.ReadAllBytes(source);
            try
            {
                if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) return false;
            }
            catch (IOException) { /* being replaced right now — write our own copy below */ }
            var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, target, overwrite: true);
                return true;
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }
}
