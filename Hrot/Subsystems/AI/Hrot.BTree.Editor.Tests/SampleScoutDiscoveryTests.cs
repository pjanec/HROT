using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Emit;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Identity;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

/// <summary>
/// Tests for AIE-ENABLE-2: verifies that BTreeAssetContributor discovers SampleScout
/// from the Hrot.AI.Behaviors assembly, and that the BTree emitter LayoutNamespace
/// resolves correctly against the runtime assembly (AIE-ENABLE-1 round-trip).
///
/// PU-402: SampleScout.cs is decommitted; SampleScout.Build() is now generated from
/// Trees/SampleScout.btree.json. The [BTreeLayout] method no longer exists in the
/// assembly. Layout coverage migrated to BTreeJsonAssetContributor reading the live JSON.
/// </summary>
public sealed class SampleScoutDiscoveryTests
{
    // The assembly containing SampleScout — obtained via a type anchor so it works
    // whether the test host is the local process or a separate test runner.
    private static readonly Assembly BehaviorsAssembly =
        typeof(Hrot.AI.Behaviors.Trees.SampleScout).Assembly;

    private const string ExpectedAssetId = "54ef3847-0000-0000-0000-000000000000";

    // ── contributor discovery ─────────────────────────────────────────────────

    [Fact]
    public void BTreeAssetContributor_LoadFrom_DiscoversSampleScout()
    {
        var contributor = new BTreeAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);

        var assets = contributor.Enumerate();
        assets.Should().Contain(a => a.Name == "SampleScout",
            "SampleScout should be found by name");
    }

    [Fact]
    public void BTreeAssetContributor_LoadFrom_SampleScout_HasCorrectAssetId()
    {
        var contributor = new BTreeAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);

        var scout = contributor.Enumerate().FirstOrDefault(a => a.Name == "SampleScout");
        scout.Should().NotBeNull();

        // AssetId is derived from FNV-1a-32("SampleScout") by the contributor.
        var expected = AssetIdHasher.FromName("SampleScout");
        scout!.AssetId.Should().Be(expected,
            "asset ID must be the FNV-1a-32 hash of the tree name");
    }

    // PU-402 MIGRATED: Layout is now in Trees/SampleScout.btree.json (not in the assembly).
    // Assert layout via BTreeJsonAssetContributor reading the live committed JSON file.
    [Fact]
    public void BTreeAssetContributor_LoadFrom_SampleScout_LayoutIsApplied()
    {
        // Locate the live JSON — it lives at Trees/SampleScout.btree.json under
        // Hrot/Subsystems/Hrot.AI.Behaviors/ relative to the repo root.
        // Walk up from the test assembly output dir (net8.0 → Debug → bin →
        // Hrot.BTree.Editor.Tests → AI → Subsystems → Hrot → repo root).
        var asmDir = Path.GetDirectoryName(typeof(SampleScoutDiscoveryTests).Assembly.Location)!;
        var repoRoot = asmDir;
        for (int i = 0; i < 7; i++)
            repoRoot = Path.GetDirectoryName(repoRoot)!;
        var jsonPath = Path.Combine(repoRoot, "Hrot", "Subsystems", "Hrot.AI.Behaviors",
            "Assets", "BTrees", "SampleScout.btree.json");

        jsonPath.Should().NotBeNullOrEmpty();
        File.Exists(jsonPath).Should().BeTrue(
            $"live SampleScout.btree.json must exist at {jsonPath} (PU-402 decommit)");

        var contrib = new BTreeJsonAssetContributor();
        contrib.Discover(jsonPaths: new[] { jsonPath });
        // LoadAll is private — trigger it via Refresh with explicit paths
        contrib.Refresh(jsonPaths: new[] { jsonPath });

        var assets = contrib.Enumerate();
        assets.Should().Contain(a => a.Name == "SampleScout",
            "JSON contributor must discover SampleScout from the live .btree.json");

        var scout = assets.FirstOrDefault(a => a.Name == "SampleScout")
                    as BehaviorTreeAsset;
        scout.Should().NotBeNull("JSON contributor must return a BehaviorTreeAsset");

        // At least one node must have non-zero X or Y — proves layout from JSON was applied.
        // (SampleScout layout: Sequence at (200,50), Wait1 at (100,200), Wait2 at (300,200))
        scout!.Nodes.Should().Contain(n => n.Position.X != 0f || n.Position.Y != 0f,
            "layout positions must be applied from Trees/SampleScout.btree.json (PU-402)");
    }

    // PU-402 DELETED: SampleScout_Layout_ReturnsNonNullWithExpectedNodes
    // The [BTreeLayout] method no longer exists in the generated assembly. Layout lives
    // in Trees/SampleScout.btree.json; use BTreeJsonAssetContributor to read it (above).

    // ── emitter output for assembly-loaded asset ─────────────────────────────

    [Fact]
    public void BTreeEmitter_Emit_ProducesValidCSharp_ForAssemblyLoadedAsset()
    {
        // PU-402: the assembly now carries a GENERATED SampleScout (from SampleScout.btree.json).
        // The assembly no longer has a [BTreeLayout] method; the assembly contributor
        // loads the asset without layout (layout is JSON-owned, not assembly-owned).
        // Verify the emitter still produces compilable C# for this reflection-loaded asset.

        var contributor = new BTreeAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);

        var scout = contributor.Enumerate().FirstOrDefault(a => a.Name == "SampleScout")
                    as BehaviorTreeAsset;
        scout.Should().NotBeNull();

        var emitter = new BTreeFluentEmitter();
        string code = emitter.Emit(scout!);

        // Must produce non-empty C# with the expected structure markers.
        code.Should().NotBeNullOrWhiteSpace("emitter must produce non-empty output");
        code.Should().Contain("CreateBuilder()", "emitted code must include CreateBuilder");
        code.Should().Contain("[BTreeDefinition(", "emitted code must include [BTreeDefinition] thunk");

        // Must NOT reference the old, non-existent namespace.
        code.Should().NotContain("Hrot.AI.Behaviors.Trees.Layout",
            "old incorrect namespace must not appear in emitted code");
    }

    [Fact]
    public void SampleScout_Build_ReturnsValidBlob()
    {
        var blob = Hrot.AI.Behaviors.Trees.SampleScout.Build();

        blob.Should().NotBeNull();
        blob.TreeName.Should().Be("SampleScout");
        blob.Nodes.Should().NotBeEmpty("compiled tree must have nodes");
    }
}
