using System;
using System.IO;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Assets;
using Xunit;

namespace Fdp.Toolkit.Tests.Orchestration
{
    /// <summary>
    /// ⭐⭐ <c>CE-4089</c> (R-4097, docs/DESIGN_Content_Layout.md §1–§2) — <see cref="ContentLayout"/> is the ONE path authority for the content tree.
    /// It owns the segment names, reuses the NAS root and the scenarios constant (no second definition), and owns the ONE repository walk-up.
    /// </summary>
    public sealed class ContentLayoutTests
    {
        private static string NewTempDir()
            => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "content-layout-" + Guid.NewGuid().ToString("N"))).FullName;

        /// <summary>⭐ The tree of §1, spelled by ONE class — a segment renamed here moves every reader with it.</summary>
        [Fact]
        public void TheTreeOfTheDesign_IsWhatTheLayoutSpells()
        {
            var r = Path.Combine("x", "root");
            Assert.Equal(Path.Combine(r, "scenarios"), ContentLayout.Scenarios(r));
            Assert.Equal(Path.Combine(r, "assets", "behaviors", "BTrees"), ContentLayout.BehaviorAssets(r, ContentLayout.BTreesFolder));
            Assert.Equal(Path.Combine(r, "assets", "behaviors", "HSMs"), ContentLayout.BehaviorAssets(r, ContentLayout.HsmsFolder));
            Assert.Equal(Path.Combine(r, "assets", "behaviors", "Blueprints"), ContentLayout.BehaviorAssets(r, ContentLayout.BlueprintsFolder));
            Assert.Equal(Path.Combine(r, "assets", "behaviors", "Params"), ContentLayout.BehaviorAssets(r, ContentLayout.ParamsFolder));
            Assert.Equal(Path.Combine(r, "assets", "terrain"), ContentLayout.TerrainAssets(r));
            Assert.Equal(Path.Combine(r, "assets", "buildings"), ContentLayout.BuildingAssets(r));
            Assert.Equal(Path.Combine(r, "assets", "icons"), ContentLayout.IconAssets(r));
            Assert.Equal(Path.Combine(r, "assets", "audio"), ContentLayout.AudioAssets(r));
            Assert.Equal(Path.Combine(r, "assets", "textures"), ContentLayout.TextureAssets(r));
            Assert.Equal(Path.Combine(r, "recipes", "scenarios"), ContentLayout.ScenarioRecipes(r));
            Assert.Equal(Path.Combine(r, "recipes", "behaviors", "Blueprints"), ContentLayout.BehaviorRecipes(r, ContentLayout.BlueprintsFolder));
            Assert.Equal(new[] { "scenarios", "assets", "recipes" }, ContentLayout.SeededDirectories);
        }

        /// <summary>⭐ L2 — recipes are separate from assets BY CONSTRUCTION: no assets path is under recipes/ and the reverse.</summary>
        [Fact]
        public void RecipesAndAssets_AreDisjointRoots()
        {
            var r = Path.Combine("x", "root");
            Assert.False(ContentLayout.BehaviorRecipes(r).StartsWith(ContentLayout.Assets(r) + Path.DirectorySeparatorChar));
            Assert.False(ContentLayout.BehaviorAssets(r).StartsWith(ContentLayout.Recipes(r) + Path.DirectorySeparatorChar));
        }

        /// <summary>⭐ No second constant: the scenarios and assets names ARE the orchestration constants'.</summary>
        [Fact]
        public void TheNames_AreTheOrchestrationConstants_AndTheNasRootIsTheSharedRoot()
        {
            Assert.Equal(OrchestrationConstants.ScenariosDirectoryName, ContentLayout.ScenariosDirectoryName);
            Assert.Equal(OrchestrationConstants.NasAssetsDirectoryName, ContentLayout.AssetsDirectoryName);
            Assert.Equal(OrchestrationConstants.GetSharedRoot(), ContentLayout.NasRoot);
        }

        /// <summary>
        /// ⭐⭐ The asset-sync's NAS tree of a behaviour kind IS the seeded tree: <c>{nas}/assets/behaviors/&lt;Folder&gt;</c> — ⛔ not a second
        /// <c>{nas}/assets/&lt;kind&gt;</c> copy. Any other kind keeps <c>assets/&lt;kind&gt;</c>.
        /// </summary>
        [Fact]
        public void TheAssetSyncRootOfABehaviourKind_IsTheSeededTree()
        {
            var nas = Path.Combine("x", "nas");
            Assert.Equal(ContentLayout.BehaviorAssets(nas, ContentLayout.BlueprintsFolder), OrchestrationConstants.GetNasAssetRoot(nas, AssetTokens.Kinds.Blueprint));
            Assert.Equal(ContentLayout.BehaviorAssets(nas, ContentLayout.BTreesFolder), OrchestrationConstants.GetNasAssetRoot(nas, AssetTokens.Kinds.BTree));
            Assert.Equal(ContentLayout.BehaviorAssets(nas, ContentLayout.HsmsFolder), OrchestrationConstants.GetNasAssetRoot(nas, AssetTokens.Kinds.Hsm));
            Assert.Equal(Path.Combine(nas, "assets", "other"), OrchestrationConstants.GetNasAssetRoot(nas, "other"));
        }

        /// <summary>
        /// ⭐⭐ The ONE repository walk-up: <c>HROT.sln</c> beside an <c>assets/</c> folder, found from any folder below it; a marker WITHOUT
        /// <c>assets/</c> (or the reverse) is not a repository; and from outside any checkout the answer is null (production).
        /// </summary>
        [Fact]
        public void TryFindSourceTree_FindsTheMarkerBesideAssets_AndNullOutsideACheckout()
        {
            var repo = NewTempDir();
            var below = Directory.CreateDirectory(Path.Combine(repo, "Hrot", "Deep", "bin")).FullName;

            Assert.Null(ContentLayout.TryFindSourceTree(below));                       // no marker, no assets/ yet

            File.WriteAllText(Path.Combine(repo, ContentLayout.RepositoryMarkerFileName), "");
            Assert.Null(ContentLayout.TryFindSourceTree(below));                       // a marker alone is not a repository

            Directory.CreateDirectory(ContentLayout.Assets(repo));
            Assert.Equal(repo, ContentLayout.TryFindSourceTree(below));                // marker + assets/

            var outside = NewTempDir();
            Assert.Null(ContentLayout.TryFindSourceTree(outside));                     // production: no source tree
            Assert.Equal(repo, ContentLayout.TryFindSourceTree(outside, below));       // the first start that answers wins
        }

        /// <summary>⭐ From this very test host (inside the checkout) the default starts find the repository the suite runs from.</summary>
        [Fact]
        public void TryFindSourceTree_WithNoStarts_FindsTheCheckoutTheTestsRunIn()
        {
            var tree = ContentLayout.TryFindSourceTree();
            Assert.NotNull(tree);
            Assert.True(File.Exists(Path.Combine(tree!, ContentLayout.RepositoryMarkerFileName)));
            Assert.True(Directory.Exists(ContentLayout.TerrainAssets(tree!)));
        }
    }
}
