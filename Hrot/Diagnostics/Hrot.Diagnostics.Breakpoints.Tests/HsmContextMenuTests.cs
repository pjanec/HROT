using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fhsm.Kernel.Data;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Hsm.Editor.Debug;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Renderers;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Diagnostics.Breakpoints.Tests;

// ---- Flat recording context-menu builder (reused across HSM tests) ----------

file sealed class HsmRecordingContextMenuBuilder : IContextMenuBuilder
{
    private readonly List<(string Label, Action Callback)> _items = new();

    public void AddItem(string label, Action callback, bool enabled = true)
        => _items.Add((label, callback));

    public IContextMenuBuilder BeginSubmenu(string label) => this;

    public void EndSubmenu() { }

    public void AddSeparator() { }

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
// HSM context-menu populator tests
// =============================================================================

[Collection("ComponentRegistry")]
public sealed class HsmContextMenuTests
{
    private readonly DataBreakpointManager _manager;

    public HsmContextMenuTests()
    {
        ComponentTypeRegistry.Clear();

        var (mgr, live, _, _) = ManagerFactory.Create();
        _manager = mgr;
        live.RegisterComponent<HsmTraceWorkingMemory1024>();
    }

    private static (HsmAsset asset, StateNode state) MakeAsset()
    {
        var state = new StateNode("TestState") { FlatIndex = 4 };
        var rootState = new StateNode("__root__") { FlatIndex = 0 };

        var asset = new HsmAsset(
            Guid.NewGuid(),
            "TestHsm",
            "",
            false,
            "",
            new HsmDefinitionBlob(),
            new MachineMetadata(),
            rootState,
            new List<StateNode> { state },
            new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            new List<RegionNode>(),
            new List<EventDefinition>());

        return (asset, state);
    }

    // -------------------------------------------------------------------------
    // 1. "Break on Enter" callback registers the correct predicate
    // -------------------------------------------------------------------------

    [Fact]
    public void HsmContextMenu_AddBreakOnEnter_RegistersWithManager()
    {
        var (_, state) = MakeAsset();
        var builder    = new HsmRecordingContextMenuBuilder();

        HsmBreakpointMenuPopulator.PopulateStateMenu(state, builder, _manager);
        builder.GetCallback("Break on Enter");

        Assert.Equal(1, _manager.AllBreakpoints.Count);

        var bp = _manager.AllBreakpoints[0];
        Assert.IsType<TraceBufferScanPredicateDto>(bp.Condition);
        var scan = (TraceBufferScanPredicateDto)bp.Condition!;
        Assert.Equal(typeof(HsmTraceWorkingMemory1024), scan.ComponentType);
        Assert.Equal((byte)TraceOpCode.StateEnter, scan.OpCode);
        Assert.Equal((ushort)state.FlatIndex, scan.IndexField);
        Assert.True(scan.MatchIndexField);
        Assert.Equal(state.StableId, bp.SourceElementId);
    }

    // -------------------------------------------------------------------------
    // 2. "Add Conditional Data Breakpoint..." creates compound with ReadOnlyChildIndices=[0]
    // -------------------------------------------------------------------------

    [Fact]
    public void HsmContextMenu_AddConditional_OpensDetailsInspectorWithReadOnlyA()
    {
        var (_, state) = MakeAsset();
        var builder    = new HsmRecordingContextMenuBuilder();

        BreakpointId? inspectorId    = null;
        SearchPredicateDto? inspectorDto = null;
        Action<BreakpointId, SearchPredicateDto> onOpen = (id, dto) =>
        {
            inspectorId  = id;
            inspectorDto = dto;
        };

        HsmBreakpointMenuPopulator.PopulateStateMenu(state, builder, _manager, onOpen);
        builder.GetCallback("Add Conditional Data Breakpoint...");

        Assert.NotNull(inspectorId);
        var compound = Assert.IsType<CompoundPredicateDto>(inspectorDto);
        Assert.Equal(LogicalOperator.And, compound.Operator);
        Assert.Equal(2, compound.Conditions.Count);
        Assert.IsType<TraceBufferScanPredicateDto>(compound.Conditions[0]);
        Assert.IsType<BehaviorParamPredicateDto>(compound.Conditions[1]);
        Assert.Single(compound.ReadOnlyChildIndices);
        Assert.Equal(0, compound.ReadOnlyChildIndices[0]);
    }

    // -------------------------------------------------------------------------
    // 3. HsmBreakpointGutterRenderer.CountBreakpoints reflects added manager BP
    // -------------------------------------------------------------------------

    [Fact]
    public void HsmGutterRenderer_ReadsManagerForBreakpoints()
    {
        var (asset, state) = MakeAsset();

        var renderer = new HsmBreakpointGutterRenderer(asset);
        renderer.SetManager(_manager);

        var builder = new HsmRecordingContextMenuBuilder();
        HsmBreakpointMenuPopulator.PopulateStateMenu(state, builder, _manager);
        builder.GetCallback("Break on Enter");

        var (stateDots, _) = renderer.CountBreakpoints();
        Assert.Equal(1, stateDots);
    }
}
