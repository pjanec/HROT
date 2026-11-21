using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>One scenario folder found under a scenarios root: its NAME (the leaf folder), its CATEGORY (the sub-folder path above it, <c>""</c> at the top level) and its folder.</summary>
    public readonly record struct ScenarioFolder(string Name, string Category, string Directory);

    /// <summary>
    /// ⭐⭐⭐ <b>CE-4360 (docs/DESIGN_Content_Layout.md §6, Q-S1/Q-S2) — scenarios may sit in SUB-FOLDERS; a scenario's NAME stays its leaf folder.</b>
    /// <c>scenarios/demos/air/ua-air-assault/scenario.json</c> is the scenario <c>ua-air-assault</c>, in the category <c>demos/air</c>. This is the index
    /// behind <see cref="ContentLayout.ResolveScenarioDirectory"/> / <see cref="ContentLayout.ResolveUnderScenariosRoot"/> for a PLAIN name (no
    /// <c>/</c>): the top-level folder when it exists, else the ONE folder under any sub-folder whose leaf is the name and that holds a
    /// <c>scenario.json</c>. ⭐ Built once per scenarios root and REBUILT ON A MISS (a scenario moved or created since) or when the cached folder is gone —
    /// so a steady-state lookup costs a dictionary read and a <c>Directory.Exists</c>, never a scan. ⛔ No FileSystemWatcher.
    /// <para>⚠ A leaf is a leaf: the walk does not enter a scenario folder, and skips dot-folders (<c>.autosave</c>, <c>.slices</c>).</para>
    /// </summary>
    public static class ScenarioFolderIndex
    {
        private static readonly ConcurrentDictionary<string, Dictionary<string, string>> Cache = new(StringComparer.Ordinal);

        /// <summary>
        /// The folder of the scenario named <paramref name="name"/> (a plain leaf name) under <paramref name="scenariosRoot"/>: <c>{root}/{name}</c> when it
        /// exists, else the folder found by the index, else <c>null</c> (a scenario that does not exist yet — a save then writes <c>{root}/{name}</c>, the
        /// top level, Q-S3 "My scenarios").
        /// </summary>
        public static string? Find(string scenariosRoot, string name)
        {
            var top = Path.Combine(scenariosRoot, name);
            if (Directory.Exists(top)) return top;

            var key = Path.GetFullPath(scenariosRoot);
            if (Cache.TryGetValue(key, out var map) && map.TryGetValue(name, out var hit) && Directory.Exists(hit)) return hit;

            // a miss, or a stale hit: rebuild once (a moved/new scenario). A name that truly does not exist costs one walk of the tree per call
            // — only a load/save asks, never a per-frame reader.
            map = Build(scenariosRoot);
            Cache[key] = map;
            return map.TryGetValue(name, out hit) ? hit : null;
        }

        /// <summary>Forget the cached index of <paramref name="scenariosRoot"/> (tests that rebuild a tree under the same path).</summary>
        public static void Invalidate(string scenariosRoot) => Cache.TryRemove(Path.GetFullPath(scenariosRoot), out _);

        /// <summary>
        /// ⭐ EVERY scenario folder under <paramref name="scenariosRoot"/>, a fresh walk: the NAME (leaf), the CATEGORY (sub-folder path, forward slashes,
        /// <c>""</c> at the top level) and the folder. Ordinal by category then name. ⛔ The ONE enumerator — a lister of "the scenarios" calls this and never
        /// <c>Directory.GetDirectories(scenariosRoot)</c>, which sees only the top level.
        /// </summary>
        public static IReadOnlyList<ScenarioFolder> Enumerate(string scenariosRoot)
        {
            var found = new List<ScenarioFolder>();
            if (!string.IsNullOrWhiteSpace(scenariosRoot) && Directory.Exists(scenariosRoot)) Walk(scenariosRoot, string.Empty, found);
            found.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.Category, b.Category);
                return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
            });
            if (found.Count > 0 || Directory.Exists(scenariosRoot))
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var f in found) map.TryAdd(f.Name, f.Directory);
                Cache[Path.GetFullPath(scenariosRoot)] = map;   // every walk refreshes the index
            }
            return found;
        }

        private static Dictionary<string, string> Build(string scenariosRoot)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in Enumerate(scenariosRoot)) map.TryAdd(f.Name, f.Directory);   // a duplicate leaf: the first wins; the uniqueness rail names both
            return map;
        }

        /// <summary>
        /// The category of the scenario <paramref name="name"/> under <paramref name="scenariosRoot"/> — the sub-folder path above its folder (<c>demos/air</c>, forward
        /// slashes), or <c>null</c> at the top level or when the scenario is not there (Q-S1: the picker's sub-category is the sub-folder path).
        /// </summary>
        public static string? CategoryOf(string scenariosRoot, string name)
        {
            var dir = ContentLayout.ResolveUnderScenariosRoot(scenariosRoot, name);
            var rel = Path.GetRelativePath(scenariosRoot, dir).Replace('\\', '/');
            if (rel.StartsWith("..", StringComparison.Ordinal) || !Directory.Exists(dir)) return null;
            var slash = rel.LastIndexOf('/');
            return slash > 0 ? rel.Substring(0, slash) : null;
        }

        /// <summary>
        /// ⭐ The NAMES a lister offers (Q-S2): the LEAF of every scenario folder; ⚠ a leaf that is not unique is offered by its relative path instead (still loadable —
        /// a name with segments resolves exactly), so two scenarios never share one entry. Ordinal.
        /// </summary>
        public static IReadOnlyList<string> Names(string scenariosRoot)
        {
            var all = Enumerate(scenariosRoot);
            var count = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var f in all) count[f.Name] = count.TryGetValue(f.Name, out var n) ? n + 1 : 1;
            var names = new List<string>(all.Count);
            foreach (var f in all)
                names.Add(count[f.Name] == 1 || f.Category.Length == 0 ? f.Name : f.Category + "/" + f.Name);
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        private static void Walk(string dir, string category, List<ScenarioFolder> found)
        {
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var leaf = Path.GetFileName(sub);
                if (leaf.Length == 0 || leaf[0] == '.') continue;
                if (HoldsScenario(sub))
                    found.Add(new ScenarioFolder(leaf, category, sub));
                else
                    Walk(sub, category.Length == 0 ? leaf : category + "/" + leaf, found);
            }
        }

        /// <summary>A scenario folder holds a <c>scenario.json</c> (matched case-insensitively — a Windows-authored <c>Scenario.json</c> on Linux).</summary>
        private static bool HoldsScenario(string dir)
        {
            foreach (var file in Directory.EnumerateFiles(dir))
                if (string.Equals(Path.GetFileName(file), ContentLayout.ScenarioFileName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
