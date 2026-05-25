using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Blueprints.Core.Debug;
using Hrot.Diagnostics.Breakpoints;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Diagnostics.Breakpoints.Tests;

// ---- Flat recording context-menu builder for Blueprint tests ----------------

file sealed class BlueprintRecordingContextMenuBuilder : IContextMenuBuilder
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

    public bool HasItem(string label)
    {
        foreach (var (l, _) in _items)
            if (l == label) return true;
        return false;
    }
}

// ---- Minimal ISimulationView stub for Blueprint session tests ---------------

file sealed class FakeSimulationView : ISimulationView
{
    private readonly EntityRepository _repo;
    public FakeSimulationView(EntityRepository repo) => _repo = repo;

    public uint  Tick => 0;
    public float Time => 0f;

    public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged
        => throw new NotImplementedException();
    public T GetManagedComponentRO<T>(Entity e) where T : class
        => throw new NotImplementedException();
    public bool IsAlive(Entity e) => throw new NotImplementedException();
    public bool HasComponent<T>(Entity e) where T : unmanaged => throw new NotImplementedException();
    public bool HasManagedComponent<T>(Entity e) where T : class => throw new NotImplementedException();
    public IReadOnlyList<T> ReadManagedEvents<T>() => throw new NotImplementedException();
    public IEntityCommandBuffer GetCommandBuffer() => throw new NotImplementedException();
    public ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged => throw new NotImplementedException();
    public QueryBuilder Query() => throw new NotImplementedException();
}

// =============================================================================
// Blueprint breakpoint menu populator tests
// =============================================================================

[Collection("ComponentRegistry")]
public sealed class BlueprintContextMenuTests
{
    private readonly DataBreakpointManager _manager;
    private readonly EntityRepository _liveRepo;
    private readonly MockDebugTimeController _tc;
    private readonly BlueprintDebugSession _session;

    public BlueprintContextMenuTests()
    {
        ComponentTypeRegistry.Clear();

        var (mgr, live, _, tc) = ManagerFactory.Create();
        _manager  = mgr;
        _liveRepo = live;
        _tc       = tc;

        var registry = new BlueprintRegistry();
        var view     = new FakeSimulationView(_liveRepo);
        _session = new BlueprintDebugSession(registry, view, _tc);
        _session.SetDataBreakpointManager(_manager);
    }

    // -------------------------------------------------------------------------
    // 1. Blueprint session breakpoint hit routes to manager -> IsPaused == true
    // -------------------------------------------------------------------------

    [Fact]
    public void Blueprint_NodeBP_RoutesToManager_TripleBufferRewindApplied()
    {
        var assetId  = Guid.NewGuid();
        var nodeGuid = Guid.NewGuid();

        // Register a breakpoint on the node in the Blueprint session
        _session.SetBreakpoint(assetId, assetId, nodeGuid);

        var entity = _liveRepo.CreateEntity();

        // Simulate the probe call from the Blueprint runtime
        _session.OnNodeEnter(entity, nodeGuid.ToString("D"));

        // The manager should now be paused (triple-buffer rewind path)
        Assert.True(_manager.IsPaused);
    }

    // -------------------------------------------------------------------------
    // 2. BlueprintBreakpointMenuPopulator synthesises compound with correct structure
    // -------------------------------------------------------------------------

    [Fact]
    public void Blueprint_AddConditional_SynthesizesCompoundWithReadOnlyA()
    {
        var assetId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid().ToString("D");
        var builder = new BlueprintRecordingContextMenuBuilder();

        BreakpointId? inspectorId    = null;
        SearchPredicateDto? inspectorDto = null;
        Action<BreakpointId, SearchPredicateDto> onOpen = (id, dto) =>
        {
            inspectorId  = id;
            inspectorDto = dto;
        };

        BlueprintBreakpointMenuPopulator.PopulateNodeMenu(nodeId, assetId, builder, _manager, onOpen);
        builder.GetCallback("Add Conditional Data Breakpoint...");

        Assert.NotNull(inspectorId);
        var compound = Assert.IsType<CompoundPredicateDto>(inspectorDto);
        Assert.Equal(LogicalOperator.And, compound.Operator);
        Assert.Equal(2, compound.Conditions.Count);

        var tagPred = Assert.IsType<ExternalHitTagPredicateDto>(compound.Conditions[0]);
        Assert.Equal(nodeId, tagPred.Tag);

        var varPred = Assert.IsType<BlueprintVariablePredicateDto>(compound.Conditions[1]);
        Assert.Equal(assetId, varPred.TargetBlueprintAssetId);

        Assert.Single(compound.ReadOnlyChildIndices);
        Assert.Equal(0, compound.ReadOnlyChildIndices[0]);
    }

    // -------------------------------------------------------------------------
    // 3. PopulateNodeMenu includes the conditional breakpoint item (UBP-P10T9)
    // -------------------------------------------------------------------------

    [Fact]
    public void Blueprint_ContextMenu_ShowsConditionalBreakpointItem()
    {
        var assetId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid().ToString("D");
        var builder = new BlueprintRecordingContextMenuBuilder();

        BlueprintBreakpointMenuPopulator.PopulateNodeMenu(nodeId, assetId, builder, _manager, null);

        // The menu must contain the conditional breakpoint entry.
        Assert.True(builder.HasItem("Add Conditional Data Breakpoint..."),
            "Expected 'Add Conditional Data Breakpoint...' in the context menu.");
    }
}
