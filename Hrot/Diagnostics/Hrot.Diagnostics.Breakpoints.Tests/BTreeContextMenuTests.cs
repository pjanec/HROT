using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fbt;
using Hrot.BTree.Editor.Debug;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Renderers;
using Hrot.Diagnostics.Breakpoints;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Diagnostics.Breakpoints.Tests;

// ---- Recording context-menu builder (test double) ---------------------------

file sealed class RecordingContextMenuBuilder : IContextMenuBuilder
{
    private readonly List<(string Label, Action Callback)> _items = new();

    public void AddItem(string label, Action callback, bool enabled = true)
        => _items.Add((label, callback));

    // Returns this so all submenu items land in the same flat list —
    // test helpers use label-based lookup and don't need hierarchy.
    public IContextMenuBuilder BeginSubmenu(string label) => this;

    public void EndSubmenu() { }

    public void AddSeparator() { }

    /// <summary>Finds the callback for the first item with the given label and invokes it.</summary>
    public void GetCallback(string label)
    {
        foreach (var (l, cb) in _items)
        {
            if (l == label)
            {
                cb();
                return;
            }
        }
        throw new InvalidOperationException($"No menu item with label '{label}' found.");
    }
}

// =============================================================================
// BTree context-menu populator tests
// =============================================================================

[Collection("ComponentRegistry")]
public sealed class BTreeContextMenuTests
{
    private readonly DataBreakpointManager _manager;
    private readonly EntityRepository _liveRepo;

    public BTreeContextMenuTests()
    {
        ComponentTypeRegistry.Clear();

        var (mgr, live, _, _) = ManagerFactory.Create();
        _manager  = mgr;
        _liveRepo = live;
        _liveRepo.RegisterComponent<BTreeTraceWorkingMemory1024>();
    }

    private static BTreeEditorNode MakeNode(int kernelBlobIndex = 3) =>
        new BTreeEditorNode
        {
            VisualId       = Guid.NewGuid(),
            KernelBlobIndex = kernelBlobIndex,
            DisplayLabel   = "TestAction",
        };

    /// <summary>⭐ CE-1324 — the document the node lives in (the step names its behaviour).</summary>
    private static BehaviorTreeAsset MakeAsset(BTreeEditorNode node)
    {
        var asset = new BehaviorTreeAsset(Guid.NewGuid(), "TestTree", "", false, "", "", new BehaviorTreeBlob());
        asset.ReplaceAll(new List<BTreeEditorNode> { node }, new List<BTreeEditorPill>(), new BehaviorTreeBlob());
        return asset;
    }

    // -------------------------------------------------------------------------
    // 1. "Break on Activation (Enter)" callback registers the correct predicate
    // -------------------------------------------------------------------------

    [Fact]
    public void BTreeContextMenu_AddBreakOnActivation_RegistersWithManager()
    {
        var node    = MakeNode();
        var builder = new RecordingContextMenuBuilder();

        BTreeBreakpointMenuPopulator.PopulateMenu(MakeAsset(node), node, builder, _manager);
        builder.GetCallback("Break on Activation (Enter)");
        builder.GetCallback("Break on Completion (Exit)");
        builder.GetCallback("Break on Interruption (Abort)");

        // ⭐ CE-1324 — each item is the BEHAVIOUR STEP (not the whole-ring scan): the real node, the behaviour's name, the outcome
        Assert.Equal(3, _manager.AllBreakpoints.Count);
        void Check(int at, BehaviourNodeOutcome outcome)
        {
            var step = Assert.IsType<BehaviourStepPredicateDto>(_manager.AllBreakpoints[at].Condition);
            Assert.Equal(BehaviourStepKind.NodeRuns, step.Kind);
            Assert.Equal("TestTree", step.Behaviour);
            Assert.Equal(node.VisualId, step.Node);
            Assert.Equal(outcome, step.Outcome);
            Assert.Equal(node.VisualId, _manager.AllBreakpoints[at].SourceElementId);
        }
        Check(0, BehaviourNodeOutcome.Running);      // activation
        Check(1, BehaviourNodeOutcome.Completes);    // completion: Success or Failure
        Check(2, BehaviourNodeOutcome.Aborted);      // abort: a scope popped
    }

    // -------------------------------------------------------------------------
    // 2. "Add Conditional Data Breakpoint..." creates compound with ReadOnlyChildIndices=[0]
    // -------------------------------------------------------------------------

    [Fact]
    public void BTreeContextMenu_AddConditional_OpensDetailsInspectorWithEditReadOnlyA()
    {
        var node    = MakeNode();
        var builder = new RecordingContextMenuBuilder();

        BreakpointId? inspectorId    = null;
        SearchPredicateDto? inspectorDto = null;
        Action<BreakpointId, SearchPredicateDto> onOpen = (id, dto) =>
        {
            inspectorId  = id;
            inspectorDto = dto;
        };

        BTreeBreakpointMenuPopulator.PopulateMenu(MakeAsset(node), node, builder, _manager, onOpen);
        builder.GetCallback("Add Conditional Data Breakpoint...");

        Assert.NotNull(inspectorId);
        var compound = Assert.IsType<CompoundPredicateDto>(inspectorDto);
        Assert.Equal(LogicalOperator.And, compound.Operator);
        Assert.Equal(2, compound.Conditions.Count);
        Assert.Equal(BehaviourNodeOutcome.Running, Assert.IsType<BehaviourStepPredicateDto>(compound.Conditions[0]).Outcome);   // ⭐ CE-1324 — the step, not the ring scan
        Assert.IsType<BehaviorParamPredicateDto>(compound.Conditions[1]);
        Assert.Single(compound.ReadOnlyChildIndices);
        Assert.Equal(0, compound.ReadOnlyChildIndices[0]);
    }

    // -------------------------------------------------------------------------
    // 3. BTreeBreakpointGutterRenderer.CountManagerBreakpoints reflects added BP
    // -------------------------------------------------------------------------

    [Fact]
    public void BTreeGutterRenderer_ReadsManagerForBreakpoints()
    {
        var assetId = Guid.NewGuid();
        var node    = MakeNode();

        // Build a minimal BehaviorTreeAsset containing the node
        var asset = new BehaviorTreeAsset(
            assetId, "TestTree", "", false, "", "", new BehaviorTreeBlob());
        asset.ReplaceAll(
            new List<BTreeEditorNode> { node },
            new List<BTreeEditorPill>(),
            new BehaviorTreeBlob());

        var renderer = new BTreeBreakpointGutterRenderer(asset);
        renderer.SetManager(_manager);

        var builder = new RecordingContextMenuBuilder();
        BTreeBreakpointMenuPopulator.PopulateMenu(asset, node, builder, _manager);
        builder.GetCallback("Break on Activation (Enter)");

        Assert.Equal(1, renderer.CountManagerBreakpoints());
    }
}
