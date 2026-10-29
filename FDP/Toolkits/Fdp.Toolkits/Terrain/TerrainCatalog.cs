using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fdp.Toolkit.Orchestration;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐⭐ THE ONE RESOLVER for a terrain name. A terrain is a FOLDER <c>&lt;root&gt;/&lt;name&gt;/</c> holding
    /// <see cref="DefinitionFileName"/> plus the files that definition names (world, road networks), so two
    /// terrains' files can never collide. The roots are searched in order — the first folder that holds a
    /// definition wins:
    /// <list type="number">
    ///   <item><b>node staging</b> <c>{node}/Terrain/</c> — what the cluster's prefetch copied here</item>
    ///   <item><b>the NAS</b> <c>{nas}/assets/terrain/</c> (<see cref="ContentLayout.TerrainAssets"/>) — what operators publish, and what a
    ///     dev start seeds from the repository's <c>assets/terrain/</c></item>
    /// </list>
    /// <para>⭐ <c>CE-4089</c>: there is NO shipped arm any more — no terrain is read from the exe folder (docs/DESIGN_Content_Layout.md L4).</para>
    /// <para>⭐ The gateway (to stage), the load step (to load) and the editor's picker (to list) all use this,
    /// so "which terrains exist" has one answer. 📄 docs/DESIGN_Terrain_World.md §7.3 W12.</para>
    /// </summary>
    public sealed class TerrainCatalog
    {
        /// <summary>The definition file inside a terrain folder.</summary>
        public const string DefinitionFileName = "terrain.json";

        /// <summary>The node staging directory a prefetched terrain lands in.</summary>
        public const string StagingDirectoryName = "Terrain";

        private readonly IReadOnlyList<string> _roots;

        public TerrainCatalog(IEnumerable<string?> roots)
        {
            _roots = roots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r!).ToList();
        }

        /// <summary>The roots, in search order.</summary>
        public IReadOnlyList<string> Roots => _roots;

        /// <summary>
        /// The standard search list for a node: its staging root, then the NAS's <c>assets/terrain</c>. Either argument may be null
        /// (a host without staging, a test with no NAS). ⭐ <paramref name="nasRoot"/> is the NAS ROOT, not a terrain folder.
        /// </summary>
        public static TerrainCatalog ForNode(string? localStagingRoot, string? nasRoot)
            => new(new[]
            {
                localStagingRoot == null ? null : Path.Combine(localStagingRoot, StagingDirectoryName),
                nasRoot == null ? null : ContentLayout.TerrainAssets(nasRoot),
            });

        /// <summary>
        /// The definition file for <paramref name="terrainName"/> (which may carry a subfolder, e.g.
        /// <c>europe/test-town</c>), or <c>null</c> when no root holds it.
        /// </summary>
        public string? ResolveDefinition(string terrainName)
        {
            if (string.IsNullOrWhiteSpace(terrainName)) return null;
            var relative = terrainName.Replace('\\', '/').Trim('/');
            if (relative.Split('/').Any(part => part == ".."))
                throw new ArgumentException($"Terrain name '{terrainName}' may not climb out of its root.", nameof(terrainName));

            foreach (var root in _roots)
            {
                var path = Path.Combine(root, relative, DefinitionFileName);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        /// <summary>
        /// ⭐ <c>AQ107 C</c> — the parsed definition of <paramref name="terrainName"/> (locate through <see cref="ResolveDefinition"/>, parse
        /// through <see cref="TerrainDefinitionParser"/>), or <c>null</c> when no root holds it. ⭐ The ONE locate-and-parse for callers that
        /// need only the definition (the cluster master reads the origin from it; <c>TerrainResidency.ApplyOriginAhead</c> too).
        /// <see cref="TerrainDefinitionParser"/> still throws on a malformed file.
        /// </summary>
        public TerrainDefinition? ReadDefinition(string terrainName)
        {
            var path = ResolveDefinition(terrainName);
            return path == null ? null : TerrainDefinitionParser.Parse(File.ReadAllText(path));
        }

        /// <summary>The folder holding <paramref name="terrainName"/>, or <c>null</c>.</summary>
        public string? ResolveFolder(string terrainName)
        {
            var def = ResolveDefinition(terrainName);
            return def == null ? null : Path.GetDirectoryName(def);
        }

        /// <summary>
        /// Every terrain name any root offers (a folder at any depth holding a definition), de-duplicated,
        /// sorted. ⭐ What the editor's terrain picker lists.
        /// </summary>
        public IReadOnlyList<string> List()
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var root in _roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var def in Directory.EnumerateFiles(root, DefinitionFileName, SearchOption.AllDirectories))
                {
                    var folder = Path.GetDirectoryName(def)!;
                    var rel = Path.GetRelativePath(root, folder).Replace('\\', '/');
                    if (rel != ".") names.Add(rel);
                }
            }
            return names.ToList();
        }
    }
}
