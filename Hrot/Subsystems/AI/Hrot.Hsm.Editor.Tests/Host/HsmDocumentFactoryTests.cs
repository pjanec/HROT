using System;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Fdp.Presentation.Icons;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Adapters;
using Hrot.Editor.AiShared.Documents;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Interfaces;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Host;

/// <summary>
/// Tests for <see cref="HsmDocumentFactory"/> (AIE-022).
/// All tests are headless — no GPU / ImGui context needed.
/// </summary>
public sealed class HsmDocumentFactoryTests : IDisposable
{
    // ── Shared atlas (fake GPU handle = 1) ────────────────────────────────────

    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);

    public void Dispose() => _atlas.Dispose();

    // ── Helpers ────────────────────────────────────────────────────────────────

    private AiEditorAdapterBundle MakeBundle() => new(_atlas);

    private static (HsmDefinitionBlob blob, MachineMetadata meta) Compile(HsmBuilder builder)
    {
        var graph    = builder.Build();
        HsmNormalizer.Normalize(graph);
        var flatData = HsmFlattener.Flatten(graph);
        var blob     = HsmEmitter.Emit(flatData);
        var meta     = HsmEmitter.BuildMachineMetadata(graph);
        return (blob, meta);
    }

    private static HsmAsset Project(HsmDefinitionBlob blob, MachineMetadata meta,
        string name = "Test") =>
        HsmAssetProjector.Project(blob, meta, null, Guid.NewGuid(), name, "", false, "");

    private static HsmAsset MakeSimpleAsset()
    {
        var b = new HsmBuilder("Simple");
        b.State("Idle").Initial();
        var (blob, meta) = Compile(b);
        return Project(blob, meta, "Simple");
    }

    private static HsmAsset MakeAssetWithTransition()
    {
        var b = new HsmBuilder("WithTrans");
        b.Event("Trigger", 1);
        b.State("Active").Final();
        b.State("Idle").Initial().On("Trigger").GoTo("Active");
        var (blob, meta) = Compile(b);
        return Project(blob, meta, "WithTrans");
    }

    private static HsmAsset MakeCompositeAsset()
    {
        var b = new HsmBuilder("Composite");
        b.State("Parent").Initial()
            .Child("Child1", c => c.Initial())
            .Child("Child2", c => { });
        var (blob, meta) = Compile(b);
        return Project(blob, meta, "Composite");
    }

    /// <summary>
    /// Builds a parallel asset directly from model objects (the HsmBuilder parallel DSL
    /// uses a different API; this mirrors the pattern from HsmValidatorBlackboardConflictTests).
    /// </summary>
    private static HsmAsset MakeParallelAsset()
    {
        var root     = new StateNode("__root__");
        var parallel = new StateNode("Par") { IsParallel = true, Parent = root, IsInitial = true };
        root.Children.Add(parallel);

        var rn0 = new RegionNode("R0") { RegionIndex = 0 };
        var rn1 = new RegionNode("R1") { RegionIndex = 1 };
        parallel.RegionNodes.Add(rn0);
        parallel.RegionNodes.Add(rn1);

        var child0 = new StateNode("A") { IsInitial = true, RegionIndex = 0, Parent = parallel };
        var child1 = new StateNode("B") { IsInitial = true, RegionIndex = 1, Parent = parallel };
        parallel.Children.Add(child0);
        parallel.Children.Add(child1);

        var allStates  = new List<StateNode> { parallel, child0, child1 };
        var regionList = new List<RegionNode> { rn0, rn1 };

        var blob = new HsmDefinitionBlob();
        return new HsmAsset(
            Guid.NewGuid(), "Parallel", "", false, "",
            blob, new MachineMetadata(),
            root, allStates,
            new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            regionList,
            new List<EventDefinition>());
    }

    // ── AIE-022 Tests ──────────────────────────────────────────────────────────

    [Fact]
    public void HsmDocumentFactory_Build_ProducesHostServices()
    {
        var asset  = MakeSimpleAsset();
        var bundle = MakeBundle();

        var ctx = HsmDocumentFactory.Build(asset, bundle);

        ctx.Should().NotBeNull();
        ctx.View.Should().NotBeNull();
        ctx.Kind.Should().Be("HSM");

        var host = ctx.View.Host;
        host.NodeCatalog  .Should().NotBeNull();
        host.TypeSystem   .Should().NotBeNull();
        host.LinkValidator.Should().NotBeNull();
        host.CommandSink  .Should().NotBeNull();
        host.Pickers      .Should().NotBeNull();
        host.Clipboard    .Should().NotBeNull();
        host.Icons        .Should().NotBeNull();
        host.Input        .Should().NotBeNull();
        host.Theme        .Should().NotBeNull();
        host.Diagnostics  .Should().NotBeNull();
        host.Debug        .Should().BeNull();
    }

    [Fact]
    public void HsmDocumentFactory_GraphView_ExposesStatesAndTransitions()
    {
        // Build an asset with a transition and verify states + links are exposed.
        var asset  = MakeAssetWithTransition();
        var bundle = MakeBundle();

        var ctx = HsmDocumentFactory.Build(asset, bundle);

        // Model exposes all states (excluding the synthetic __root__).
        ctx.View.Model.Nodes.Count.Should().Be(asset.AllStates.Count);

        // Model exposes all transitions.
        ctx.View.Model.Links.Count.Should().Be(asset.AllTransitions.Count);

        // FindNode returns the correct state.
        var firstState = asset.AllStates.First();
        var found = ctx.View.Model.FindNode(new NodeEditor.Primitives.NodeId(firstState.StableId));
        found.Should().NotBeNull();
        found.Should().BeSameAs(firstState);
    }

    [Fact]
    public void HsmDocumentFactory_GraphView_CompositeState_IsContainer()
    {
        // Composite states (children present) must report IsContainer == true (IContainerNodeModel).
        var asset  = MakeCompositeAsset();
        var bundle = MakeBundle();

        var ctx = HsmDocumentFactory.Build(asset, bundle);

        // Find the composite "Parent" state.
        var parentState = asset.AllStates.First(s => s.Name == "Parent");
        var nodeModel   = ctx.View.Model.FindNode(
            new NodeEditor.Primitives.NodeId(parentState.StableId));

        nodeModel.Should().NotBeNull();
        // StateNode implements IContainerNodeModel — check via IsContainer property.
        (nodeModel as StateNode)!.IsContainer.Should().BeTrue();

        // Children are accessible.
        var container = nodeModel as StateNode;
        container!.Children.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void HsmDocumentFactory_GraphView_ParallelState_IsContainer()
    {
        var asset  = MakeParallelAsset();
        var bundle = MakeBundle();
        var ctx    = HsmDocumentFactory.Build(asset, bundle);

        var parState = asset.AllStates.First(s => s.Name == "Par");
        parState.IsParallel.Should().BeTrue();
        parState.IsContainer.Should().BeTrue();

        // The parallel state must appear in the model.
        var found = ctx.View.Model.FindNode(new NodeEditor.Primitives.NodeId(parState.StableId));
        found.Should().NotBeNull();
    }

    [Fact]
    public void HsmDocumentFactory_GraphView_RegionNodes_PresentForParallel()
    {
        var asset  = MakeParallelAsset();
        var bundle = MakeBundle();
        var ctx    = HsmDocumentFactory.Build(asset, bundle);

        var parState = asset.AllStates.First(s => s.Name == "Par");
        // ParallelState should have RegionNodes.
        parState.RegionNodes.Should().NotBeEmpty();
    }

    [Fact]
    public void HsmDocumentFactory_Build_CustomRenderers_ArePresent()
    {
        var asset  = MakeSimpleAsset();
        var bundle = MakeBundle();
        var ctx    = HsmDocumentFactory.Build(asset, bundle);

        // At least the 4 standard HSM renderers (TransitionLabel, InitialArrow, HistoryGlyphs, RegionConflicts).
        ctx.View.Host.CustomCanvasRenderers.Should().HaveCountGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void HsmDocumentFactory_Build_Throws_WhenAssetIsWrongType()
    {
        var wrongAsset = new FakeBTreeAsset();
        var bundle     = MakeBundle();

        var act = () => HsmDocumentFactory.Build(wrongAsset, bundle);
        act.Should().Throw<ArgumentException>().WithMessage("*HsmAsset*");
    }

    [Fact]
    public void HsmDocumentFactory_Build_MinimalMachine_BuildsWithoutThrowing()
    {
        // A minimal HSM (single state) builds without throwing and produces nodes.
        var asset  = MakeSimpleAsset();
        var bundle = MakeBundle();

        var act = () => HsmDocumentFactory.Build(asset, bundle);
        act.Should().NotThrow();

        var ctx = HsmDocumentFactory.Build(asset, bundle);
        ctx.Should().NotBeNull();
        ctx.View.Should().NotBeNull();
        ctx.View.Model.Nodes.Count.Should().BeGreaterThan(0);
    }

    // ── Fake wrong-type asset ──────────────────────────────────────────────────

    private sealed class FakeBTreeAsset : IEditableAsset
    {
        public Guid      AssetId        => Guid.NewGuid();
        public string    Name           => "fake";
        public AssetKind Kind           => AssetKind.BTree;
        public string    SourceFilePath => "";
        public bool      IsDirty        => false;
        public bool      IsEditorOwned  => false;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }
}
