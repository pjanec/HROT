using System;
using System.IO;
using Hrot.Editor.AiShared;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Identity;

/// <summary>
/// ⭐⭐⭐ <b>Ruling 67 — the one true authoring blocker: on a DEPLOYED node there is no source tree, so
/// the <c>.csproj</c> walk-up answers <see langword="null"/> and the asset roots were unusable.</b>
///
/// <para>🔒 <b>User, <c>2026-08-14</c>:</b> *"we need a <b>config file provided asset path</b> for the CGF
/// as well as the Editor (<b>same shared code</b>), with <b>fallback to the repo source</b> as of now."*
/// ⇒ resolution order <b>config → source walk-up → output directory</b>.</para>
///
/// <para>⚠⚠ <b>These rails MUTATE PROCESS-GLOBAL STATE and restore it in a <c>finally</c>.</b>
/// <c>AssetRoots</c> is a <c>static</c> class and ruling 67 chose an explicit <c>Configure(...)</c> over a
/// provider *("a provider is cleaner but ripples" — 30 call sites)*. ⛔ The cost is real and is stated
/// here rather than hidden: a test that forgot the restore would poison every later test in the
/// assembly. ⭐ <see cref="AssetRoots.ConfiguredRoot"/> exists so the restore is possible at all.</para>
/// </summary>
// ⚠⚠ CE-099 — this class mutates the process-global AssetRoots.ConfiguredRoot, so it must be SERIAL with
//    every other class that does. 📐 It had been racing since ruling 67 landed; it simply had nothing to
//    collide with until TheRootReportingPolicyIsOneImplementationTests arrived and lost the coin toss.
[Collection(AssetRootsTestCollection.Name)]
public sealed class TheDeployedNodeFindsItsAssetsTests : IDisposable
{
    private readonly string? _saved = AssetRoots.ConfiguredRoot;

    /// <summary>⭐ Always restores, even on a failing assertion — see the class remarks.</summary>
    public void Dispose() => AssetRoots.Configure(_saved);

    /// <summary>
    /// ⭐ A temp directory with the <c>Assets/BTrees</c> shape and — crucially — <b>no <c>.csproj</c>
    /// anywhere above it</b>. That is what makes it a DEPLOYED shape rather than a dev one.
    /// </summary>
    private static string MakeDeployedShapeRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ruling67-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "BTrees"));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Blueprints"));
        Directory.CreateDirectory(Path.Combine(root, "Recipes", "Scenarios"));
        return root;
    }

    // ══ the three arms, in order ═════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE RULING, asserted: a configured root wins, and it resolves under a directory with no
    /// source tree above it.</b> ⛔ Before this, the same node got <c>null</c> and indexed nothing.
    /// </summary>
    [Fact]
    public void AConfiguredRootResolvesWithNoSourceTreeAnywhereAbove()
    {
        var root = MakeDeployedShapeRoot();
        try
        {
            AssetRoots.Configure(root);

            Assert.Equal(root, AssetRoots.ConfiguredRoot);
            Assert.Equal(root, AssetRoots.ResolveBase("no", "such", "project.csproj"));

            var btrees = AssetRoots.ResolveAssetsRoot(AssetKind.BTree, "no", "such", "project.csproj");
            Assert.Equal(Path.Combine(root, "Assets", "BTrees"), btrees);
            Assert.True(Directory.Exists(btrees), "the deployed-shape root must actually be listable");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// ⭐⭐ <b>Config OUTRANKS the walk-up</b> — and this rail passes real, findable <c>.csproj</c> segments
    /// so the walk-up would otherwise succeed. ⛔ Without the ordering, a deployed node that also happened
    /// to sit inside a checkout would silently prefer the source tree over its own configuration.
    /// </summary>
    [Fact]
    public void ConfigOutranksTheSourceWalkUp()
    {
        var viaWalkUp = AssetRoots.ResolveProjectDir("Subsystems", "Hrot.AI.Behaviors", "Hrot.AI.Behaviors.csproj");

        var root = MakeDeployedShapeRoot();
        try
        {
            AssetRoots.Configure(root);
            var resolved = AssetRoots.ResolveBase("Subsystems", "Hrot.AI.Behaviors", "Hrot.AI.Behaviors.csproj");

            Assert.Equal(root, resolved);
            if (viaWalkUp != null) Assert.NotEqual(viaWalkUp, resolved);   // ⭐ only meaningful in a checkout
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// ⭐⭐ <b>Unset config restores the pre-ruling behaviour EXACTLY.</b> That is what keeps every one of
    /// the ~30 existing call sites, and every dev box, unchanged — ⛔ the change must be opt-in.
    /// </summary>
    [Fact]
    public void WithNoConfigTheWalkUpThenTheOutputDirectoryStillAnswer()
    {
        AssetRoots.Configure(null);
        Assert.Null(AssetRoots.ConfiguredRoot);

        // ⭐ In a checkout the walk-up answers; in a deployed test host it does not. Both are legal, so
        //   this asserts the CONTRACT (never null, and the two possible arms) rather than one machine.
        var resolved = AssetRoots.ResolveBase("Subsystems", "Hrot.AI.Behaviors", "Hrot.AI.Behaviors.csproj");
        Assert.False(string.IsNullOrEmpty(resolved));

        var walkUp = AssetRoots.ResolveProjectDir("Subsystems", "Hrot.AI.Behaviors", "Hrot.AI.Behaviors.csproj");
        Assert.Equal(walkUp ?? AppContext.BaseDirectory, resolved);
    }

    /// <summary>
    /// ⭐⭐ <b>The last arm is never null</b>, which is the difference from
    /// <see cref="AssetRoots.ResolveProjectDir"/> — whose <c>null</c> means *"there is no source tree"*
    /// and must stay honest. ⚠ Two methods, two contracts, stated so neither is read as the other.
    /// </summary>
    [Fact]
    public void ResolveBaseNeverAnswersNullEvenWithNothingToFind()
    {
        AssetRoots.Configure(null);

        Assert.Null(AssetRoots.ResolveProjectDir("definitely", "not", "here.csproj"));
        Assert.Equal(AppContext.BaseDirectory, AssetRoots.ResolveBase("definitely", "not", "here.csproj"));
    }

    // ══ the fail-fast half ═══════════════════════════════════════════════════════

    /// <summary>
    /// 🔒 <b>A configured-but-MISSING root throws at startup — the ruling's own call.</b>
    /// *"Silently falling through to the walk-up would reintroduce 'it worked on the dev box'."*
    /// ⛔ So a typo in config is a startup failure, not an empty asset list three screens later.
    /// </summary>
    [Fact]
    public void AConfiguredButMissingRootThrowsRatherThanFallingBack()
    {
        var missing = Path.Combine(Path.GetTempPath(), "ruling67-absent-" + Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(missing));

        var ex = Assert.Throws<DirectoryNotFoundException>(() => AssetRoots.Configure(missing));
        Assert.Contains("does not exist", ex.Message);

        // ⚠ And the failed call must not have half-applied.
        Assert.Null(AssetRoots.ConfiguredRoot);
    }

    /// <summary>⚠ Whitespace is "unset", not "a root named space" — the JSON default is an empty string.</summary>
    [Fact]
    public void AnEmptyOrWhitespaceRootMeansUnset()
    {
        AssetRoots.Configure("   ");
        Assert.Null(AssetRoots.ConfiguredRoot);

        AssetRoots.Configure("");
        Assert.Null(AssetRoots.ConfiguredRoot);
    }

    // ══ the split-brain guard ════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>BROWSE and CREATE must resolve to the SAME tree — the half of ruling 67 that is easy to
    /// ship without.</b>
    ///
    /// <para>🔴 <b>This rail exists because the first version of this batch shipped it wrong.</b> The new
    /// <c>ResolveBase</c> honoured the config while <see cref="AssetRoots.AssetsFor"/> — which is what
    /// <c>BlueprintAssetContributor.BaseFolder</c>, <c>BTreeJsonAssetContributor</c>,
    /// <c>HsmJsonAssetContributor</c>, <c>BTreeNewAssetService</c> and <c>HsmNewAssetService</c> all use —
    /// still pointed at <c>AppContext.BaseDirectory</c>. ⇒ a configured node would have LISTED assets
    /// from one tree and CREATED them in another: ⛔ two competing path authorities, the exact failure
    /// ruling 67 exists to prevent, reintroduced by the fix for it.</para>
    /// </summary>
    [Fact]
    public void EveryRootMemberAgreesWithTheConfiguredRoot()
    {
        var root = MakeDeployedShapeRoot();
        try
        {
            AssetRoots.Configure(root);

            Assert.Equal(Path.Combine(root, "Assets"),   AssetRoots.AssetsRoot);
            Assert.Equal(Path.Combine(root, "Recipes"),  AssetRoots.RecipesRoot);

            foreach (var kind in new[] { AssetKind.Blueprint, AssetKind.BTree, AssetKind.Hsm })
            {
                Assert.StartsWith(root, AssetRoots.AssetsFor(kind), StringComparison.Ordinal);
                Assert.StartsWith(root, AssetRoots.RecipesFor(kind), StringComparison.Ordinal);

                // ⭐⭐ THE POINT: the browse path and the create path are the same string.
                Assert.Equal(AssetRoots.ResolveAssetsRoot(kind, "no", "such", "x.csproj"),
                             AssetRoots.AssetsFor(kind));
            }

            Assert.StartsWith(root, AssetRoots.ScenariosRecipesRoot, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// ⚠ <b>And with config UNSET every one of those members is byte-identical to before</b> — the
    /// change must be opt-in for ~30 call sites and every dev box.
    /// </summary>
    [Fact]
    public void WithNoConfigEveryRootMemberIsUnchanged()
    {
        AssetRoots.Configure(null);

        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Assets"),  AssetRoots.AssetsRoot);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Recipes"), AssetRoots.RecipesRoot);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Assets", "BTrees"),
                     AssetRoots.AssetsFor(AssetKind.BTree));
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Recipes", "Scenarios"),
                     AssetRoots.ScenariosRecipesRoot);
    }

    /// <summary>⭐ The diagnostic line names WHICH arm answered — "empty" and "pointed elsewhere" are
    /// different problems and an operator has to be able to tell them apart.</summary>
    [Fact]
    public void DescribeBaseNamesTheArmThatAnswered()
    {
        var root = MakeDeployedShapeRoot();
        try
        {
            AssetRoots.Configure(root);
            Assert.Contains("config", AssetRoots.DescribeBase("no", "such", "x.csproj"));

            AssetRoots.Configure(null);
            Assert.Contains("output directory", AssetRoots.DescribeBase("definitely", "not", "here.csproj"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
