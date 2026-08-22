using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Xunit;

// Alias to disambiguate between the two Breakpoint types in scope — same convention as
// AiWatchBreakpointsWindowTests.
using DpBreakpoint = Hrot.Diagnostics.Breakpoints.Breakpoint;
using DpBpId       = Hrot.Diagnostics.Breakpoints.BreakpointId;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-2 — <c>AiWatchWindow</c> converted to the <c>PanelSnapshot</c> contract.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example, §Adoption <c>U-obs-2</c> · AS-BUILT
/// deviations ①/②/④ · <c>docs/blueprints/batches/HANDOFF_Panel_Observability.md</c>.
///
/// <para>⭐⭐ <b>Mirrors <c>ThePilotPanelDumpsWhatItDrawsTests</c></b> — same BUILD/CAPTURE/RENDER shape,
/// same headless rationale: capture happens before the ImGui-context guard, so these rails never open a
/// frame. ⚠ <c>PanelSnapshot</c> is process-global static state, so every case resets it.</para>
///
/// <para>⭐⭐⭐ <b>Rail 3 is the whole reason the contract splits <c>PanelId</c> (the address) from
/// <c>PanelKind</c> (the logical name)</b> — three perspectives each host their own Watch, and a shared
/// id would let one silently overwrite another in the snapshot (AS-BUILT deviation ②).</para>
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheWatchPanelDumpsItsPinnedRowsTests : IDisposable
{
    private static readonly Guid AssetId = new("aaaaaaaa-0000-0000-0000-00000000000a");

    public TheWatchPanelDumpsItsPinnedRowsTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    // ── fixtures — mirrors AiWatchBreakpointsWindowTests' StubBreakpointManager and
    //    TheWatchGoesLiveTests' IntSchema, the established construction pattern for this window ──────

    /// <summary>Minimal stub for IDataBreakpointManager providing only what the window draws from.</summary>
    private sealed class StubBreakpointManager : IDataBreakpointManager
    {
        private readonly List<DpBreakpoint> _bps = new();
        public IReadOnlyList<DpBreakpoint> AllBreakpoints => _bps;

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

    /// <summary>A one-variable schema source, trimmed from <c>TheWatchGoesLiveTests.IntSchema</c> to the
    /// members <see cref="IVariablesSchemaSource"/> requires (no default bodies — U-5/BP-230).</summary>
    private sealed class OneIntSchema : IVariablesSchemaSource
    {
        public OneIntSchema(string name)
            => Variables = new[]
            {
                new VariableViewModel(
                    Name: name, TypeName: "int", ByteSize: 4, FieldType: typeof(int),
                    Comment: null, AliasedBy: Array.Empty<(string, Guid, Guid)>(), IsUnused: false),
            };

        public IReadOnlyList<VariableViewModel> Variables { get; }
        public bool IsReadOnly => false;
        public bool SupportsRoleScopeEditing => false;
        public string? GetRefactorKey(string variableName) => null;
        public void AddVariable(Hrot.Editor.AiShared.Blackboard.BlackboardVariableEntry entry) { }
        public void RemoveVariable(string name) { }
        public void RemoveVariables(IReadOnlyList<string> names) { }
        public void RenameVariable(string oldName, string newName) { }
        public List<(string Name, string? Json)> DefaultWrites { get; } = new();
        public void UpdateVariableDefaultValueJson(string name, string? defaultValueJson)
            => DefaultWrites.Add((name, defaultValueJson));
        public List<(string Name, VariablePropertyValues Values)> PropertyWrites { get; } = new();
        public void UpdateVariableProperties(string name, VariablePropertyValues values)
            => PropertyWrites.Add((name, values));
        public DeclarationPropertySnapshot? ReadVariableProperties(string name) => null;
        public void MoveVariable(int sourceIndex, int destIndex) { }
        public int CountNodesReferencingVariable(string name) => 0;
        public IReadOnlyList<Hrot.Editor.AiShared.Windows.UnboundRequirementViewModel> UnboundRequirements
            => Array.Empty<Hrot.Editor.AiShared.Windows.UnboundRequirementViewModel>();
        public void AddAlias(string name, Hrot.Editor.AiShared.Blackboard.BlackboardAliasBinding binding) { }
        public void RemoveAlias(string name, Guid requirementAssetId, Guid requirementElementId) { }
        public IReadOnlyDictionary<Guid, int>? GetParallelRegionMap() => null;
    }

    private static AiWatchWindow MakeWindow(string id, string perspective = "BTree")
        => new(id, perspective, new StubBreakpointManager(),
               new VariableValueFormatter(RawValueDecoder.Instance));

    private static VariableRow OnePinnedRow(string name, int value)
    {
        var live   = new Dictionary<string, object> { [name] = value };
        var source = new SectionVariableRowSource(
            assetId: AssetId, assetName: "Alpha", entity: default, section: "s",
            schema:  new OneIntSchema(name),
            liveObjects: () => live);
        return source.GetRows()[0];
    }

    // ── Rail 1 — instrumented at construction, on the PRODUCTION object ─────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The window is instrumented the moment it is CONSTRUCTED — before it has ever drawn.</b>
    /// ⛔ This is the rail that would go red if <c>DeclareInstrumented</c> drifted into the draw: a Watch
    /// nobody opened would then look exactly like a Watch nobody converted.
    /// </summary>
    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        const string id = "ai_watch_rail1";
        Assert.DoesNotContain(id, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = MakeWindow(id);

        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain(id, PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet(id));
        Assert.NotNull(window);   // keeps the construction live for the assertions above
    }

    // ── Rail 2 — the dump carries the pinned row ─────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>Draw a frame with a pinned row, read the model over the snapshot, assert its shape.</b>
    /// ⛔ Calls <see cref="AiWatchWindow.DrawContent"/> directly (no ImGui context) — exactly what makes
    /// capture-before-the-guard load-bearing rather than cosmetic.
    /// </summary>
    [Fact]
    public void AfterADrawWithAPinnedRow_TheDumpCarriesTheRow()
    {
        const string id = "ai_watch_rail2";
        PanelSnapshot.CaptureEnabled = true;

        var window = MakeWindow(id);
        window.Pinned.Pin(OnePinnedRow("Health", 10));

        window.DrawContent();

        var vm = PanelSnapshot.TryGet(id);
        Assert.NotNull(vm);
        Assert.Equal(id, vm!.PanelId);
        Assert.Equal(PanelIds.Watch, vm.PanelKind);

        var dump = vm.Dump();
        Assert.Equal(id, dump["panelId"]!.GetValue<string>());
        Assert.Equal(PanelIds.Watch, dump["panelKind"]!.GetValue<string>());

        var rows = dump["rows"]!.AsArray();
        Assert.Single(rows);
        Assert.Equal("Health", rows[0]!["shortName"]!.GetValue<string>());
    }

    // ── Rail 3 — THE IMPORTANT ONE: two live watches, two addresses, one kind ───────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>Two watch windows with DIFFERENT ids stay individually addressable</b> — the whole reason
    /// <c>PanelId</c> (the address) and <c>PanelKind</c> (the logical name) are separate fields
    /// (AS-BUILT deviation ②). ⛔ A shared id would let the second draw overwrite the first in the
    /// snapshot; this rail is what would catch that regression.
    /// </summary>
    [Fact]
    public void TwoWatchWindowsWithDifferentIds_StayIndividuallyAddressable()
    {
        const string btreeId = "ai_watch_btree";
        const string hsmId   = "ai_watch_hsm";
        PanelSnapshot.CaptureEnabled = true;

        var btree = MakeWindow(btreeId, "BTree");
        var hsm   = MakeWindow(hsmId, "HSM");

        btree.Pinned.Pin(OnePinnedRow("Health", 10));
        hsm.Pinned.Pin(OnePinnedRow("Mana", 20));

        btree.DrawContent();
        hsm.DrawContent();

        Assert.Contains(btreeId, PanelSnapshot.CapturedPanels);
        Assert.Contains(hsmId,   PanelSnapshot.CapturedPanels);

        var btreeVm = PanelSnapshot.TryGet(btreeId);
        var hsmVm   = PanelSnapshot.TryGet(hsmId);
        Assert.NotNull(btreeVm);
        Assert.NotNull(hsmVm);

        // ⛔ Neither overwrote the other — each dump still carries ITS OWN pinned row.
        Assert.Equal("Health", btreeVm!.Dump()["rows"]!.AsArray()[0]!["shortName"]!.GetValue<string>());
        Assert.Equal("Mana",   hsmVm!.Dump()["rows"]!.AsArray()[0]!["shortName"]!.GetValue<string>());

        // ⭐⭐ …and PanelsOfKind groups them by the SHARED kind, not the unique address.
        var watchIds = PanelSnapshot.PanelsOfKind(PanelIds.Watch);
        Assert.Contains(btreeId, watchIds);
        Assert.Contains(hsmId,   watchIds);
        Assert.Equal(2, watchIds.Count);
    }
}
