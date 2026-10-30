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
        public const string BehaviorsDirectoryName = ContentTreeNames.Behaviors;

        public const string TerrainDirectoryName = "terrain";
        public const string BuildingsDirectoryName = "buildings";
        public const string IconsDirectoryName = "icons";
        public const string AudioDirectoryName = "audio";
        public const string TexturesDirectoryName = "textures";

        /// <summary>
        /// <c>opened</c> — ⭐ CE-3340 ① the NAS "opened, not saved" area: a scenario that exists only as an OPENED copy (a recording's scenario
        /// opened for editing; later a never-saved autosave, AQ108 §4c A8). Neither <c>recipes/</c> (New lists those) nor <c>scenarios/</c> (Open
        /// lists those), so no listing offers it by construction. NOT seeded from the repository.
        /// </summary>
        public const string OpenedDirectoryName = "opened";

        /// <summary>The <c>opened/</c> sub-area that holds the opened copies of recordings (<c>opened/recorded/&lt;exerciseId&gt;</c>).</summary>
        public const string OpenedRecordedFolder = "recorded";

        /// <summary>The per-kind folders under <c>behaviors/</c>.</summary>
        public const string BTreesFolder = ContentTreeNames.BTrees;
        public const string HsmsFolder = ContentTreeNames.Hsms;
        public const string BlueprintsFolder = ContentTreeNames.Blueprints;
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

        /// <summary><c>{root}/opened</c> — the "opened, not saved" area (see <see cref="OpenedDirectoryName"/>).</summary>
        public static string Opened(string root) => Path.Combine(root, OpenedDirectoryName);

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

        // ── scenario NAMES → folders (L6) ───────────────────────────────────────────────────────────────

        /// <summary>⭐ L6 — the prefix of a recipe's scenario name: a recipe is named by its ROOT, <c>recipes/scenarios/&lt;seed&gt;</c>.</summary>
        public const string ScenarioRecipeNamePrefix = RecipesDirectoryName + "/" + ScenariosDirectoryName + "/";

        /// <summary>The prefix of an opened copy's scenario name: <c>opened/&lt;area&gt;/&lt;key&gt;</c>.</summary>
        public const string OpenedNamePrefix = OpenedDirectoryName + "/";

        /// <summary>The scenario name that loads the recipe (seed) <paramref name="seed"/> — <c>recipes/scenarios/&lt;seed&gt;</c>.</summary>
        public static string ScenarioRecipeName(string seed) => ScenarioRecipeNamePrefix + seed;

        /// <summary>The scenario name of an opened copy: <c>opened/&lt;area&gt;/&lt;key&gt;</c> (e.g. <c>opened/recorded/&lt;exerciseId&gt;</c>).</summary>
        public static string OpenedCopyName(string area, string key) => OpenedNamePrefix + area + "/" + key;

        /// <summary><c>true</c> when <paramref name="scenarioName"/> names a recipe (<see cref="ScenarioRecipeNamePrefix"/>), however it is slashed.</summary>
        public static bool IsScenarioRecipeName(string? scenarioName) => Normalise(scenarioName).StartsWith(ScenarioRecipeNamePrefix, StringComparison.Ordinal);

        /// <summary><c>true</c> when <paramref name="scenarioName"/> names an opened copy (<see cref="OpenedNamePrefix"/>).</summary>
        public static bool IsOpenedCopyName(string? scenarioName) => Normalise(scenarioName).StartsWith(OpenedNamePrefix, StringComparison.Ordinal);

        /// <summary>
        /// ⭐⭐⭐ <b>THE ONE name → folder resolver</b> for a scenario name against a content root (the NAS). A saved scenario is
        /// <c>{root}/scenarios/&lt;name&gt;</c>; a recipe name (<c>recipes/scenarios/&lt;seed&gt;</c>) is <c>{root}/recipes/scenarios/&lt;seed&gt;</c>;
        /// an opened-copy name (<c>opened/…</c>) is <c>{root}/opened/…</c>. ⛔ Every NAS-side reader of "the folder of scenario X" (prefetch,
        /// content names, the run's snapshot, the archive, the opened-copy stager) goes through here — nothing composes
        /// <c>Path.Combine(nas, "scenarios", name)</c> by hand. ⚠ Node STAGING is a different tree (<c>nodes/node-N/scenarios/&lt;name&gt;</c>, the name as
        /// a relative path) and is unchanged.
        /// </summary>
        public static string ResolveScenarioDirectory(string root, string scenarioName)
        {
            if (string.IsNullOrWhiteSpace(scenarioName)) throw new ArgumentException("A scenario name is required.", nameof(scenarioName));
            var name = Normalise(scenarioName);
            foreach (var segment in name.Split('/'))
                if (segment == "..") throw new ArgumentException($"A scenario name may not climb out of its root: '{scenarioName}'.", nameof(scenarioName));

            if (name.StartsWith(ScenarioRecipeNamePrefix, StringComparison.Ordinal))
                return Path.Combine(ScenarioRecipes(root), name.Substring(ScenarioRecipeNamePrefix.Length));
            if (name.StartsWith(OpenedNamePrefix, StringComparison.Ordinal))
                return Path.Combine(Opened(root), name.Substring(OpenedNamePrefix.Length));
            return Path.Combine(Scenarios(root), name);
        }

        /// <summary>
        /// ⭐ The same resolution for a reader that holds only the SCENARIOS root (the editor's scenario session, the director's take store): a saved name is
        /// <c>{scenariosRoot}/&lt;name&gt;</c> (whatever the folder is called — a test passes a temp dir); a recipe or opened-copy name is resolved against the
        /// SIBLING roots (<c>{scenariosRoot}/../recipes/scenarios</c>, <c>../opened</c>) — the scenarios root's parent IS the content root. ⛔ Never
        /// <c>Path.Combine(scenariosRoot, "recipes/scenarios/&lt;seed&gt;")</c> by hand: that folder does not exist (L6).
        /// </summary>
        public static string ResolveUnderScenariosRoot(string scenariosRoot, string scenarioName)
        {
            if (!IsScenarioRecipeName(scenarioName) && !IsOpenedCopyName(scenarioName)) return Path.Combine(scenariosRoot, scenarioName);
            var contentRoot = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(scenariosRoot)))
                              ?? throw new ArgumentException("The scenarios root has no parent content root.", nameof(scenariosRoot));
            return ResolveScenarioDirectory(contentRoot, scenarioName);
        }

        private static string Normalise(string? name) => (name ?? string.Empty).Replace('\\', '/');

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
