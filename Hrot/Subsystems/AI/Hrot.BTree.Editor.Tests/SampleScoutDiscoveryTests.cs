using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Emit;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Identity;
using Hrot.Editor.AiShared.Layout;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

/// <summary>
/// Tests for AIE-ENABLE-2: verifies that BTreeAssetContributor discovers SampleScout
/// from the Hrot.AI.Behaviors assembly, and that the BTree emitter LayoutNamespace
/// resolves correctly against the runtime assembly (AIE-ENABLE-1 round-trip).
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

    [Fact]
    public void BTreeAssetContributor_LoadFrom_SampleScout_LayoutIsApplied()
    {
        var contributor = new BTreeAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);

        var scout = contributor.Enumerate().FirstOrDefault(a => a.Name == "SampleScout")
                    as Hrot.BTree.Editor.Model.BehaviorTreeAsset;
        scout.Should().NotBeNull("contributor must return a BehaviorTreeAsset");

        // At least one node must have a non-zero position — proves layout was applied.
        scout!.Nodes.Should().Contain(n => n.Position.X != 0f || n.Position.Y != 0f,
            "layout positions must be applied from the [BTreeLayout] method");
    }

    // ── emitter layout-namespace round-trip ──────────────────────────────────

    [Fact]
    public void BTreeEmitter_LayoutUsing_ResolvesInRuntimeAssembly()
    {
        // The BTreeFluentEmitter must emit "using Hrot.Editor.AiShared.Layout;"
        // (not the old "Hrot.AI.Behaviors.Trees.Layout") so that the emitted file
        // compiles against Hrot.Editor.AiContracts (referenced by Hrot.AI.Behaviors).

        var contributor = new BTreeAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);

        var scout = contributor.Enumerate().FirstOrDefault(a => a.Name == "SampleScout")
                    as BehaviorTreeAsset;
        scout.Should().NotBeNull();

        var emitter = new BTreeFluentEmitter();
        string code = emitter.Emit(scout!);

        // The emitted using must reference the actual layout namespace.
        code.Should().Contain("using Hrot.Editor.AiShared.Layout;",
            "emitter LayoutNamespace must match the namespace types actually live in");

        // Must NOT reference the old, non-existent namespace.
        code.Should().NotContain("Hrot.AI.Behaviors.Trees.Layout",
            "old incorrect namespace must not appear in emitted code");

        // Verify the namespace is reachable: the runtime assembly must reference a
        // dependency that exports Hrot.Editor.AiShared.Layout types.
        var referencedAssemblyNames = BehaviorsAssembly
            .GetReferencedAssemblies()
            .Select(n => n.Name)
            .ToHashSet();
        referencedAssemblyNames.Should().Contain("Hrot.Editor.AiContracts",
            "Hrot.AI.Behaviors must reference Hrot.Editor.AiContracts to resolve layout types");
    }

    [Fact]
    public void SampleScout_Layout_ReturnsNonNullWithExpectedNodes()
    {
        // Directly invoke SampleScout.Layout() to verify the layout method itself works.
        var layout = Hrot.AI.Behaviors.Trees.SampleScout.Layout();

        layout.Should().NotBeNull();
        layout.Nodes.Should().HaveCount(3,
            "SampleScout layout declares positions for Sequence, Wait1, Wait2");
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
