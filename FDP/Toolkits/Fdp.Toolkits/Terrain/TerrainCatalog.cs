using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐⭐ THE ONE RESOLVER for a terrain name. A terrain is a FOLDER <c>&lt;root&gt;/&lt;name&gt;/</c> holding
    /// <see cref="DefinitionFileName"/> plus the files that definition names (world, road networks), so two
    /// terrains' files can never collide. The roots are searched in order — the first folder that holds a
    /// definition wins:
    /// <list type="number">
    ///   <item><b>node staging</b> <c>{node}/Terrain/</c> — what the cluster's prefetch copied here</item>
    ///   <item><b>the NAS stand-in</b> <c>{shared}/terrain/</c> — what operators publish</item>
    ///   <item><b>the shipped terrains</b> <c>{app}/Recipes/Terrain/</c> — the test terrains built with the product</item>
    /// </list>
    /// <para>⭐ The gateway (to stage), the load step (to load) and the editor's picker (to list) all use this,
    /// so "which terrains exist" has one answer. 📄 docs/DESIGN_Terrain_World.md §7.3 W12.</para>
    /// </summary>
    public sealed class TerrainCatalog
    {
        /// <summary>The definition file inside a terrain folder.</summary>
        public const string DefinitionFileName = "terrain.json";

        /// <summary>The NAS / shared directory terrains are published under.</summary>
        public const string SharedDirectoryName = "terrain";

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
        /// The standard search list for a node: its staging root, the shared root, then the shipped terrains.
        /// Any argument may be null (a host without staging, a test with no shipped content).
        /// </summary>
        public static TerrainCatalog ForNode(string? localStagingRoot, string? sharedRoot, string? shippedRoot = null)
            => new(new[]
            {
                localStagingRoot == null ? null : Path.Combine(localStagingRoot, StagingDirectoryName),
                sharedRoot == null ? null : Path.Combine(sharedRoot, SharedDirectoryName),
                shippedRoot ?? DefaultShippedRoot,
            });

        /// <summary>Where the build ships its terrains: <c>{AppContext.BaseDirectory}/Recipes/Terrain</c>.</summary>
        public static string DefaultShippedRoot => Path.Combine(AppContext.BaseDirectory, "Recipes", "Terrain");

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
