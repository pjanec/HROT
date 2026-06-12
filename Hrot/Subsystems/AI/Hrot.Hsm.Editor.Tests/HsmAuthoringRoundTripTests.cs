using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FluentAssertions;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.Hsm.Editor;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

/// <summary>
/// End-to-end round-trip tests proving the HSM authoring loop:
/// create → edit (via command sink) → save (real persistence path) → reopen → assert.
/// BATCH-HS-08.
/// </summary>
public sealed class HsmAuthoringRoundTripTests
{
    // ── helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an empty <see cref="HsmAsset"/> (root-only, no states/transitions/regions/events)
    /// and wraps it with a <see cref="HsmCommandSink"/> for mutation.
    /// Mirrors the pattern used by existing command-sink test classes.
    /// </summary>
    private static (HsmAsset asset, HsmCommandSink sink) BuildEmptyAsset(string name = "RoundTripTest")
    {
        var rootNode = new StateNode("__root__");

        var asset = new HsmAsset(
            Guid.NewGuid(), name, sourceFilePath: "", isEditorOwned: false, targetNamespace: "",
            new HsmDefinitionBlob(), new MachineMetadata(),
            rootNode,
            new List<StateNode>(),
            new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            new List<RegionNode>(),
            new List<EventDefinition>());

        var sink = new HsmCommandSink(asset);
        return (asset, sink);
    }

    /// <summary>
    /// The REAL save path: HsmAsset → HsmAssetMapper.ToDto → HsmAssetDto → HsmJsonServices.Serialize → JSON string.
    /// </summary>
    private static string SaveAsset(HsmAsset asset)
    {
        var dto = HsmAssetMapper.ToDto(asset);
        return HsmJsonServices.Serialize(dto);
    }

    /// <summary>
    /// The REAL open path: JSON string → HsmJsonServices.Deserialize → HsmAssetDto → HsmAssetMapper.ToModel → HsmAsset.
    /// </summary>
    private static HsmAsset ReopenAsset(string json, string sourceFilePath = "")
    {
        var dto = HsmJsonServices.Deserialize(json);
        dto.Should().NotBeNull("round-trip deserialization must return a non-null DTO");
        return HsmAssetMapper.ToModel(dto!, sourceFilePath: sourceFilePath, isEditorOwned: true);
    }

    // ── Full authoring round-trip ───────────────────────────────────────────────

    /// <summary>
    /// Builds a small non-trivial machine via <see cref="HsmCommandSink"/> (add states,
    /// reparent to form a composite, draw a transition, collapse the container, move nodes),
    /// saves through the real persistence path, reloads, and asserts that topology, layout,
    /// and flags are preserved with no dangling references.
    /// </summary>
    [Fact]
    public void AuthoringRoundTrip_CreateEditSaveReopen_PreservesTopologyAndLayout()
    {
        // ── 1. Build an empty machine and author via command sink ─────────────
        var (asset, sink) = BuildEmptyAsset();

        // Create 4 states: a composite-ish parent, 2 children, and a final state.
        var parentId = new NodeId(Guid.NewGuid());
        var child1Id = new NodeId(Guid.NewGuid());
        var child2Id = new NodeId(Guid.NewGuid());
        var finalId  = new NodeId(Guid.NewGuid());

        sink.Apply(new GraphCommand.AddNode(parentId, new NodeKindKey(HsmKinds.Simple),  new Vector2(100, 100), null));
        sink.Apply(new GraphCommand.AddNode(child1Id, new NodeKindKey(HsmKinds.Simple),  new Vector2(200, 200), null));
        sink.Apply(new GraphCommand.AddNode(child2Id, new NodeKindKey(HsmKinds.Simple),  new Vector2(300, 200), null));
        sink.Apply(new GraphCommand.AddNode(finalId,  new NodeKindKey(HsmKinds.Final),   new Vector2(400, 100), null));

        // Reparent child1 under parent → parent becomes a composite.
        sink.Apply(new GraphCommand.ChangeParent(
            child1Id,
            parentId,
            NewRegionIndex: 0,
            new Vector2(50, 50)));

        // Add a transition from child1 → child2.
        var child1State = asset.FindStateByStableId(child1Id.Value)!;
        var child2State = asset.FindStateByStableId(child2Id.Value)!;
        var linkId = Guid.NewGuid();
        sink.Apply(new GraphCommand.AddLink(
            new LinkId(linkId),
            new PinId(child1State.HiddenOutputPinId),
            new PinId(child2State.HiddenInputPinId)));

        // Collapse the composite container.
        sink.Apply(new GraphCommand.SetContainerCollapsed(parentId, IsCollapsed: true));

        // Move nodes to distinct positions.
        sink.Apply(new GraphCommand.MoveNodes(new[]
        {
            new NodeMove(parentId, new Vector2(150, 120)),
            new NodeMove(child1Id, new Vector2(60, 60)),
            new NodeMove(child2Id, new Vector2(60, 140)),
            new NodeMove(finalId,  new Vector2(500, 150)),
        }));

        // ── Record pre-save state for comparison ──────────────────────────────
        var preStateCount      = asset.AllStates.Count;
        var preTransitionCount = asset.AllTransitions.Count;

        // Snapshot StableIds and properties for topology assertions.
        var preStates = asset.AllStates.ToList();
        var preTransitions = asset.AllTransitions.ToList();

        var parentPre = asset.FindStateByStableId(parentId.Value)!;
        parentPre.IsCollapsed.Should().BeTrue("parent must be collapsed before save");

        // ── 2. Save: real persistence path ────────────────────────────────────
        var json = SaveAsset(asset);

        // ── 3. Reopen: real persistence path ──────────────────────────────────
        var reopened = ReopenAsset(json);

        // ── 4. Assert topology preserved ─────────────────────────────────────
        reopened.AllStates.Should().HaveCount(preStateCount);
        reopened.AllTransitions.Should().HaveCount(preTransitionCount);

        // Each pre-save state has a matching reopened state by StableId with same
        // name and kind flags.
        foreach (var preState in preStates)
        {
            var reState = reopened.FindStateByStableId(preState.StableId);
            reState.Should().NotBeNull(
                $"state '{preState.Name}' (StableId={preState.StableId}) must survive round-trip");
            reState!.Name.Should().Be(preState.Name);
            reState.IsInitial.Should().Be(preState.IsInitial);
            reState.IsFinal.Should().Be(preState.IsFinal);
            reState.IsHistory.Should().Be(preState.IsHistory);
            reState.IsDeepHistory.Should().Be(preState.IsDeepHistory);
            reState.IsParallel.Should().Be(preState.IsParallel);
        }

        // Parent/child topology.
        var parentRe = reopened.FindStateByStableId(parentId.Value)!;
        parentRe.Children.Should().HaveCount(1, "parent must still have 1 child after round-trip");
        parentRe.Children[0].StableId.Should().Be(child1Id.Value,
            "child1 must still be parented to the composite");
        parentRe.Kind.Id.Should().Be(HsmKinds.Composite,
            "parent with children must report Composite kind");

        var child1Re = reopened.FindStateByStableId(child1Id.Value)!;
        child1Re.Parent.Should().BeSameAs(parentRe,
            "child1's Parent reference must point to the composite");

        // Transition: count, VisualId, Source/Target StableIds, and no dangling refs.
        reopened.AllTransitions.Should().HaveCount(1);
        var tRe = reopened.AllTransitions[0];
        tRe.VisualId.Should().Be(linkId, "transition VisualId must survive round-trip");
        tRe.Source.Should().NotBeNull("transition Source must not be a dangling reference");
        tRe.Target.Should().NotBeNull("transition Target must not be a dangling reference");
        tRe.Source.StableId.Should().Be(child1Id.Value,
            "transition Source must point to child1");
        tRe.Target.StableId.Should().Be(child2Id.Value,
            "transition Target must point to child2");

        // ── 5. Assert layout preserved ───────────────────────────────────────
        parentRe.IsCollapsed.Should().BeTrue("IsCollapsed must survive round-trip");

        parentRe.Position.Should().Be(new Vector2(150, 120),
            "parent Position must survive round-trip");
        child1Re.Position.Should().Be(new Vector2(60, 60),
            "child1 Position must survive round-trip");
        var child2Re = reopened.FindStateByStableId(child2Id.Value)!;
        child2Re.Position.Should().Be(new Vector2(60, 140),
            "child2 Position must survive round-trip");
        var finalRe = reopened.FindStateByStableId(finalId.Value)!;
        finalRe.Position.Should().Be(new Vector2(500, 150),
            "final state Position must survive round-trip");
    }

    // ── Starter recipe round-trip ───────────────────────────────────────────────

    /// <summary>
    /// Starter recipe → project to live model → save (real path) → reopen (real path)
    /// → assert that the single initial state, its name, parent topology, and position
    /// are preserved.
    /// </summary>
    [Fact]
    public void StarterRecipeRoundTrip_SaveReopen_PreservesSingleInitialState()
    {
        // Start with the Starter recipe DTO (authored in code by HsmNewAssetService).
        var starterDto = HsmNewAssetService.MakeStarterDto();

        // Project to live model (the open path).
        var asset = HsmAssetMapper.ToModel(starterDto, sourceFilePath: "", isEditorOwned: true);

        // Record pre-save counts and initial state details.
        var preStateCount = asset.AllStates.Count;
        var preRegionCount = asset.AllRegions.Count;
        var preInitial = asset.AllStates.Single(s => s.IsInitial);
        var preInitialStableId = preInitial.StableId;
        var preInitialName = preInitial.Name;
        var preInitialPos = preInitial.Position;

        asset.AllStates.Should().HaveCount(2, "Starter has __Root + InitState");
        asset.AllRegions.Should().HaveCount(1, "Starter has one region");

        // Save: real persistence path.
        var json = SaveAsset(asset);

        // Reopen: real persistence path.
        var reopened = ReopenAsset(json);

        // ── Assert structure preserved ────────────────────────────────────────
        reopened.AllStates.Should().HaveCount(preStateCount,
            "state count must survive round-trip");
        reopened.AllRegions.Should().HaveCount(preRegionCount,
            "region count must survive round-trip");

        // The single initial state survives.
        var reopenedInitial = reopened.AllStates.SingleOrDefault(s => s.IsInitial);
        reopenedInitial.Should().NotBeNull("Starter must have exactly one initial state after round-trip");
        reopenedInitial!.StableId.Should().Be(preInitialStableId,
            "initial state StableId must survive round-trip");
        reopenedInitial.Name.Should().Be(preInitialName,
            "initial state Name must survive round-trip");

        // Root state exists and is the parent of the initial state.
        var rootState = reopened.AllStates.FirstOrDefault(s => s.Name == "__Root");
        rootState.Should().NotBeNull("__Root state must survive round-trip");
        rootState!.Children.Should().Contain(c => c.StableId == preInitialStableId,
            "initial state must remain a child of __Root");
        reopenedInitial.Parent.Should().BeSameAs(rootState,
            "initial state's Parent reference must point to __Root");

        // ── Assert layout preserved ───────────────────────────────────────────
        reopenedInitial.Position.Should().Be(preInitialPos,
            "initial state Position must survive round-trip");
    }
}
