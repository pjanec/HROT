using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.AiShared.Windows;
using Xunit;

using DpBreakpoint = Hrot.Diagnostics.Breakpoints.Breakpoint;
using DpBpId       = Hrot.Diagnostics.Breakpoints.BreakpointId;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>AiBreakpointsWindow</c> converted to the <c>PanelSnapshot</c> contract.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class AiBreakpointsWindowDumpsItsCountsTests : IDisposable
{
    public AiBreakpointsWindowDumpsItsCountsTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    /// <summary>Minimal stub for IDataBreakpointManager providing only what the window counts from.</summary>
    private sealed class StubBreakpointManager : IDataBreakpointManager
    {
        public readonly List<DpBreakpoint> Bps = new();
        public IReadOnlyList<DpBreakpoint> AllBreakpoints => Bps;

        public bool IsPaused => false;
        public long PausedTick => 0;
        public int PendingMutationsCount => 0;
        public bool HasMountedDelegates => false;
        public bool HasStatefulTrackers => false;
        public ISimulationView ActiveView => throw new NotImplementedException();

        public DpBpId Add(DpBreakpoint bp) => bp.Id;
        public DpBpId AddBreakpoint(SearchPredicateDto cond, Entity? filter = null,
            int thresh = 1, string name = "", Guid? srcId = null) => DpBpId.Invalid;
        public void Remove(DpBpId id) { }
        public void SetEnabled(DpBpId id, bool e) { }
        public void UpdateCondition(DpBpId id, SearchPredicateDto? c) { }
        public void MarkAsWatch(DpBpId id, bool w) { }
        public void SaveWatches(string p) { }
        public void LoadWatches(string p) { }
        public void OnHotReloadCompleted() { }
        public void OnHotReloadBegin() { }
        public void StageMutation(Entity entity, Type componentType, object componentValue) { }
        public void OnHit(DpBreakpoint bp, Entity e) { }
        public void OnExternalHit(string tag, Entity e) { }
        public void RequestStep() { }
        public void RequestContinue() { }
        public void EvaluateStatefulBreakpoints(EntityRepository repo) { }

        public IReadOnlyList<(DpBreakpoint Breakpoint, CompiledComponentPredicate Compiled)>
            MountedComponentPredicates => Array.Empty<(DpBreakpoint, CompiledComponentPredicate)>();
        public IReadOnlyList<(DpBreakpoint Breakpoint, CompiledEventScanner Scanner)>
            MountedEventScanners => Array.Empty<(DpBreakpoint, CompiledEventScanner)>();

#pragma warning disable CS0067
        public event Action<DpBreakpoint, Entity>? OnBreakpointHit;
        public event Action<bool>? OnPauseStateChanged;
#pragma warning restore CS0067
    }

    private static AiBreakpointsWindow MakeWindow(string id, StubBreakpointManager manager)
        => new(id, "BTree", manager);

    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        const string id = "ai_breakpoints_rail1";
        Assert.DoesNotContain(id, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = MakeWindow(id, new StubBreakpointManager());

        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain(id, PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet(id));
        Assert.NotNull(window);
    }

    [Fact]
    public void AfterABuild_TheDumpCarriesTheActiveCount()
    {
        const string id = "ai_breakpoints_rail2";
        PanelSnapshot.CaptureEnabled = true;
        var manager = new StubBreakpointManager();
        manager.Bps.Add(new DpBreakpoint { Id = DpBpId.Invalid, Enabled = true });
        manager.Bps.Add(new DpBreakpoint { Id = DpBpId.Invalid, Enabled = false });
        var window = MakeWindow(id, manager);

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet(id);
        Assert.NotNull(vm);
        Assert.Equal(id, vm!.PanelId);
        Assert.Equal(AiBreakpointsWindow.Kind, vm.PanelKind);

        var dump = vm.Dump();
        Assert.Equal(1, dump["activeCount"]!.GetValue<int>());
        Assert.Equal(2, dump["totalCount"]!.GetValue<int>());
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        const string id = "ai_breakpoints_rail3";
        var manager = new StubBreakpointManager();
        manager.Bps.Add(new DpBreakpoint { Id = DpBpId.Invalid, Enabled = true });
        var window = MakeWindow(id, manager);   // CaptureEnabled stays false

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);
        Assert.Equal(1, vm.ActiveCount);
    }
}
