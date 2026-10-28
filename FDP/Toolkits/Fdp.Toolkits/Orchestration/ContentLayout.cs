using System;
using System.Collections.Generic;
using System.IO;
using Fdp.Toolkit.Orchestration.Assets;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐⭐⭐ <b>THE ONE PATH AUTHORITY for the content tree</b> (<c>CE-4089</c>, R-4097 — docs/DESIGN_Content_Layout.md §1/§2).
    ///
    /// <code>
    /// &lt;content root&gt;/                the repository root in a dev build, the NAS root in production — the SAME tree
    ///   scenarios/&lt;name&gt;/
    ///   assets/  behaviors/{BTrees,HSMs,Blueprints,Params}  terrain/&lt;name&gt;  buildings  icons  audio  textures
    ///   recipes/ scenarios/&lt;seed&gt;  behaviors/{BTrees,HSMs,Blueprints}   (listed by "New …" only, never by "Open …")
    /// </code>
    ///
    /// <para>⭐ <b>One pattern (L4):</b> a dev start copies the repository's tree into the NAS; every node reads ONLY the NAS (and its
    /// own staging); a production install has no source tree, so <see cref="TryFindSourceTree"/> answers <c>null</c> and nothing is
    /// copied. ⛔ Every segment name of the tree is spelled HERE and nowhere else — <c>AssetRoots</c> (editor), <c>TerrainCatalog</c>,
    /// <c>TerrainAssets</c>, the storage gateway, the icon library and the audio layer all compose their paths through this class.</para>
    ///
    /// <para>⚠ Lives in <c>Fdp.Toolkits</c> because <c>TerrainCatalog</c>/<c>TerrainAssets</c> are here and cannot reference Hrot.</para>
    /// </summary>
    public static class ContentLayout
    {
        // ── segment names ────────────────────────────────────────────────────────────────────────────────

        /// <summary><c>scenarios</c> — ⛔ not a second constant: it IS <see cref="OrchestrationConstants.ScenariosDirectoryName"/>.</summary>
        public const string ScenariosDirectoryName = OrchestrationConstants.ScenariosDirectoryName;

        /// <summary><c>assets</c> — ⛔ one definition: <see cref="OrchestrationConstants.NasAssetsDirectoryName"/>.</summary>
        public const string AssetsDirectoryName = OrchestrationConstants.NasAssetsDirectoryName;

        /// <summary><c>recipes</c> — creation sources; separate from assets BY CONSTRUCTION (L2).</summary>
        public const string RecipesDirectoryName = "recipes";

        /// <summary><c>behaviors</c> — under <c>assets/</c> (final behaviour assets) and under <c>recipes/</c> (their creation sources).</summary>
        public const string BehaviorsDirectoryName = "behaviors";

        public const string TerrainDirectoryName = "terrain";
        public const string BuildingsDirectoryName = "buildings";
        public const string IconsDirectoryName = "icons";
        public const string AudioDirectoryName = "audio";
        public const string TexturesDirectoryName = "textures";

        /// <summary>The per-kind folders under <c>behaviors/</c>.</summary>
        public const string BTreesFolder = "BTrees";
        public const string HsmsFolder = "HSMs";
        public const string BlueprintsFolder = "Blueprints";
        public const string ParamsFolder = "Params";

        /// <summary>The marker file that identifies the repository (<c>HROT.sln</c> beside an <c>assets/</c> folder).</summary>
        public const string RepositoryMarkerFileName = "HROT.sln";

        /// <summary>⭐ The three top-level folders a dev start copies from the repository into the NAS, in order.</summary>
        public static IReadOnlyList<string> SeededDirectories { get; } =
            new[] { ScenariosDirectoryName, AssetsDirectoryName, RecipesDirectoryName };

        // ── the roots, under ANY content root (the NAS, the repository, a test's temp tree) ─────────────

        public static string Scenarios(string root) => Path.Combine(root, ScenariosDirectoryName);
        public static string Assets(string root) => Path.Combine(root, AssetsDirectoryName);
        public static string Recipes(string root) => Path.Combine(root, RecipesDirectoryName);

        /// <summary><c>{root}/assets/behaviors</c> — the final behaviour assets (the roots of Blueprints, HSMs, BTrees, Params).</summary>
        public static string BehaviorAssets(string root) => Path.Combine(Assets(root), BehaviorsDirectoryName);

        /// <summary><c>{root}/assets/behaviors/&lt;folder&gt;</c> (<paramref name="folder"/> is one of the <c>…Folder</c> constants).</summary>
        public static string BehaviorAssets(string root, string folder) => Path.Combine(BehaviorAssets(root), folder);

        /// <summary><c>{root}/recipes/behaviors</c>.</summary>
        public static string BehaviorRecipes(string root) => Path.Combine(Recipes(root), BehaviorsDirectoryName);

        /// <summary><c>{root}/recipes/behaviors/&lt;folder&gt;</c>.</summary>
        public static string BehaviorRecipes(string root, string folder) => Path.Combine(BehaviorRecipes(root), folder);

        /// <summary><c>{root}/recipes/scenarios</c> — the scenario seeds a "New scenario" is created from.</summary>
        public static string ScenarioRecipes(string root) => Path.Combine(Recipes(root), ScenariosDirectoryName);

        public static string TerrainAssets(string root) => Path.Combine(Assets(root), TerrainDirectoryName);
        public static string BuildingAssets(string root) => Path.Combine(Assets(root), BuildingsDirectoryName);
        public static string IconAssets(string root) => Path.Combine(Assets(root), IconsDirectoryName);
        public static string AudioAssets(string root) => Path.Combine(Assets(root), AudioDirectoryName);
        public static string TextureAssets(string root) => Path.Combine(Assets(root), TexturesDirectoryName);

        // ── behaviour kind ids (the asset-sync's lower-case ids) ↔ folders ──────────────────────────────

        /// <summary>The <c>behaviors/</c> folder of a behaviour kind id (<c>blueprint</c> → <c>Blueprints</c>), or <c>null</c> for any other kind.</summary>
        public static string? BehaviorFolderOfKind(string kindId) => kindId switch
        {
            AssetTokens.Kinds.Blueprint => BlueprintsFolder,
            AssetTokens.Kinds.BTree => BTreesFolder,
            AssetTokens.Kinds.Hsm => HsmsFolder,
            _ => null,
        };

        /// <summary>
        /// ⭐ The NAS tree of one asset-sync kind: a behaviour kind lives at <c>{root}/assets/behaviors/&lt;Folder&gt;</c> — ⛔ the SAME
        /// tree the dev seed fills, never a second <c>assets/&lt;kind&gt;</c> copy — any other kind at <c>{root}/assets/&lt;kind&gt;</c>.
        /// </summary>
        public static string AssetSyncRoot(string root, string kindId)
            => BehaviorFolderOfKind(kindId) is { } folder ? BehaviorAssets(root, folder) : Path.Combine(Assets(root), kindId);

        // ── where the content lives ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ <b>The NAS root every node reads its content from</b> — <see cref="OrchestrationConstants.GetSharedRoot()"/> (reused, not
        /// re-derived: <c>FDP_STAGING_ROOT</c> or the platform temp folder, + <c>shared</c>).
        /// </summary>
        public static string NasRoot => OrchestrationConstants.GetSharedRoot();

        /// <summary>
        /// ⭐ <b>The ONE repository walk-up.</b> The directory (the repository root) that holds <see cref="RepositoryMarkerFileName"/> beside an
        /// <c>assets/</c> folder, found by walking up from each of <paramref name="startDirs"/> (default: the working directory, then
        /// <see cref="AppContext.BaseDirectory"/> — a dev run, a test host and a launched binary differ). <c>null</c> when there is no source
        /// tree — a production install, where the NAS is the only source.
        /// </summary>
        public static string? TryFindSourceTree(params string[] startDirs)
        {
            IEnumerable<string> starts = startDirs is { Length: > 0 }
                ? startDirs
                : new[] { Environment.CurrentDirectory, AppContext.BaseDirectory };

            foreach (var start in starts)
            {
                var dir = string.IsNullOrWhiteSpace(start) ? null : Path.GetFullPath(start);
                while (!string.IsNullOrEmpty(dir))
                {
                    if (File.Exists(Path.Combine(dir, RepositoryMarkerFileName)) && Directory.Exists(Assets(dir)))
                        return dir;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            return null;
        }
    }
}
