using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Presentation.Icons;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Presentation.WindowManager;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Xunit;

// Aliases: two Breakpoint / BreakpointId types are in scope in this test assembly.
using DpBreakpoint = Hrot.Diagnostics.Breakpoints.Breakpoint;
using DpBpId       = Hrot.Diagnostics.Breakpoints.BreakpointId;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>Track C is REACHABLE.</b>
///
/// <para>📐 The finding this batch exists for: five of seven Track C deliverables were built, tested,
/// and <b>hosted by nothing</b> — <c>VariableTableControl</c> referenced only inside its own folder,
/// <c>BlackboardMyBlueprintModel</c> constructed by nothing at all. ⛔ Every one of them had passing
/// tests. ⇒ <b>a component with passing tests is not a component someone can use</b>, and that gap is
/// what these rails close.</para>
///
/// <para>⭐⭐ <b>Every assertion here is on the CONSTRUCTED OBJECT</b>, never on the registrar's source
/// or on a type existing. That is the 2026-08-16 forwarding-rail rule: the defect was a caller that
/// held a dependency and did not pass it, so only the built object can show the difference.</para>
/// </summary>
public sealed class TrackCWiringTests : IDisposable
{
    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);
    public void Dispose() => _atlas.Dispose();

    private static PerspectiveWorkspaceRegistrar MakeRegistrar(
        string perspective = "BTree",
        BlackboardHostKind? host = BlackboardHostKind.BTree,
        IDataBreakpointManager? breakpoints = null)
        => new(
            perspectiveName:   perspective,
            selectionStore:    new EditorSelectionStore(),
            catalog:           new AssetCatalog(),
            refactorService:   new StubRefactor(),
            debugRegistry:     new DebugSessionRegistry(),
            breakpointManager: breakpoints,
            hostKind:          host);

    // ══ item 1 — the outline ═════════════════════════════════════════════════

    /// <summary>
    /// 🔴 RED before this batch: the AI perspectives registered eight windows and none of them was a
    /// My Blueprint outline, so <c>BlackboardMyBlueprintModel</c> could not be reached from the editor
    /// at all.
    /// </summary>
    [Fact]
    public void TheRegistrar_ConstructsAMyBlueprintOutline_ForBTreeAndHsm()
    {
        var btree = MakeRegistrar("BTree", BlackboardHostKind.BTree);
        var hsm   = MakeRegistrar("HSM",   BlackboardHostKind.Hsm);

        Assert.NotNull(btree.MyBlueprint);
        Assert.NotNull(hsm.MyBlueprint);
        Assert.Equal(BlackboardHostKind.BTree, btree.MyBlueprint!.Host);
        Assert.Equal(BlackboardHostKind.Hsm,   hsm.MyBlueprint!.Host);
    }

    /// <summary>
    /// ⛔ The other half: the Blueprint perspective must NOT get one — it already has
    /// <c>BlueprintMyBlueprintWindow</c>, and two outlines for one concept is the duplication
    /// ruling 9 forbids. Without this arm the test above would pass for a registrar that hands every
    /// perspective an outline.
    /// </summary>
    [Fact]
    public void TheBlueprintPerspective_GetsNoSecondOutline()
        => Assert.Null(MakeRegistrar("Blueprint", host: null).MyBlueprint);

    /// <summary>
    /// ⭐ Each host's section list, in <c>SortOrder</c>, off the constructed model. ⚠ The sections are
    /// asserted as a SEQUENCE — an outline whose order drifts reads as a different panel.
    /// </summary>
    [Theory]
    [InlineData(BlackboardHostKind.BTree)]
    [InlineData(BlackboardHostKind.Hsm)]
    public void TheOutline_YieldsItsHostsSections_InSortOrder(BlackboardHostKind host)
    {
        var window = MakeRegistrar("X", host).MyBlueprint!;
        window.Retarget(() => Array.Empty<BlackboardVariableEntry>(), null, null);

        var ids = window.Model!.Sections.OrderBy(s => s.SortOrder).Select(s => s.Id).ToArray();

        Assert.Equal(
            new[]
            {
                BlackboardMyBlueprintModel.SectionInputs,
                BlackboardMyBlueprintModel.SectionWorkingState,
                BlackboardMyBlueprintModel.SectionAssetGlobals,
            },
            ids);
    }

    /// <summary>
    /// ⚠ EMPTY rather than ABSENT — every supported section is always listed, only its item list is
    /// empty. ⛔ A section that appears and disappears reads as a broken feature.
    /// </summary>
    [Fact]
    public void AnEmptyBlackboard_StillListsEverySection()
    {
        var window = MakeRegistrar().MyBlueprint!;
        window.Retarget(() => Array.Empty<BlackboardVariableEntry>(), null, null);

        Assert.Equal(3, window.Model!.Sections.Count);
        Assert.All(window.Model.Sections, s => Assert.Empty(window.Model.GetItems(s.Id)));
    }

    // ══ item 2 — the table ═══════════════════════════════════════════════════

    /// <summary>
    /// 🔴 RED before this batch: <c>VariableTableControl</c> was referenced only inside its own folder.
    /// ⭐ And the window is REGISTERED, not merely constructed — a surface the host must remember to
    /// attach is how these five became unreachable.
    /// </summary>
    [Fact]
    public void TheRegistrar_ConstructsAndRegistersTheVariablesTable()
    {
        var reg = MakeRegistrar();
        var wm = new WindowManager(_atlas);
        reg.RegisterWindows(wm);

        Assert.NotNull(reg.Variables);
        Assert.Contains(reg.Variables, reg.RegisteredWindows);
        Assert.Contains(reg.MyBlueprint!, reg.RegisteredWindows);
    }

    /// <summary>
    /// ⭐⭐ <b>The additive guarantee, in executable form.</b> The user's ruling is that the OLD
    /// surface stays for now — so the batch is wrong if hosting the new one displaced it.
    /// </summary>
    [Fact]
    public void TheOldBlackboardSurface_IsStillRegistered()
    {
        var reg = MakeRegistrar();
        var wm = new WindowManager(_atlas);
        reg.RegisterWindows(wm);

        Assert.Contains(reg.BlackboardAuthoring, reg.RegisteredWindows);
    }

    /// <summary>
    /// ⭐⭐⭐ Selection routes the table. 📄 design §1c — <i>"selection yields a SECTION, not a
    /// variable… the routing key is <c>(asset, section)</c>"</i>. ⛔ Asserted end to end through the
    /// registrar's own wiring, not by calling <c>ShowSection</c> directly, because the wiring is the
    /// thing that was missing.
    /// </summary>
    [Fact]
    public void SelectingASectionInTheOutline_ReFiltersTheTable()
    {
        var reg = MakeRegistrar();
        var rows = new Dictionary<string, IVariableRowSource>
        {
            [BlackboardMyBlueprintModel.SectionInputs]       = Source("Health"),
            [BlackboardMyBlueprintModel.SectionWorkingState] = Source("Cursor", "Phase"),
        };
        reg.SetSectionSourceResolver(id => rows.TryGetValue(id, out var s) ? s : null);

        reg.MyBlueprint!.SelectSection(BlackboardMyBlueprintModel.SectionInputs);
        Assert.Equal(BlackboardMyBlueprintModel.SectionInputs, reg.Variables.Section);
        Assert.Equal(new[] { "Health" }, Names(reg.Variables));

        reg.MyBlueprint.SelectSection(BlackboardMyBlueprintModel.SectionWorkingState);
        Assert.Equal(BlackboardMyBlueprintModel.SectionWorkingState, reg.Variables.Section);
        Assert.Equal(new[] { "Cursor", "Phase" }, Names(reg.Variables));
    }

    /// <summary>
    /// ⭐ The table is source-agnostic: rows from two assets and two entities render together. ⚠ This
    /// is the case <c>PinnedSource</c> needs, proved on the hosted window rather than on the control
    /// in isolation.
    /// </summary>
    [Fact]
    public void TheTable_RendersHeterogeneousRows()
    {
        var reg = MakeRegistrar();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        reg.Variables.ShowSection("mixed", new FixedVariableRowSource(new[]
        {
            Row(a, new Entity(1, 0), "Scout",  "Health"),
            Row(b, new Entity(2, 0), "Guard",  "Health"),
        }));

        var view = reg.Variables.Model.Build();
        Assert.Equal(2, view.AllRows.Count);
        // ⭐ Same short name, different origins ⇒ the control must qualify them rather than collapse.
        Assert.Equal(2, view.AllRows.Select(r => r.Origin.Key).Distinct().Count());
    }

    // ══ item 3 — the Watch ═══════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ The variable watch is wired as its OWN feed, and the breakpoint list is untouched — the
    /// two are different entities and merging them silently was explicitly ruled out.
    /// </summary>
    [Fact]
    public void TheWatch_GetsAVariableFeed_AndKeepsItsBreakpointList()
    {
        var manager = new StubBreakpointManager();
        var reg     = MakeRegistrar(breakpoints: manager);

        Assert.NotNull(reg.Watch);
        Assert.True(reg.Watch!.HasVariableWatch,
            "the registrar HOLDS the formatter and must pass it — a silent default would render every cell <unreadable>");
        Assert.Same(manager, reg.Watch.Manager);
    }

    /// <summary>
    /// ⭐ A pinned set spanning two assets and two entities keeps independent identities, and
    /// ⭐⭐ <c>Type</c> is hidden in Watch while shown in Details — the one column toggle, per surface.
    /// </summary>
    [Fact]
    public void TheWatch_PinsAcrossAssetsAndHidesTheTypeColumn()
    {
        var reg = MakeRegistrar(breakpoints: new StubBreakpointManager());
        var watch = reg.Watch!;

        watch.Pinned.Pin(Row(Guid.NewGuid(), new Entity(1, 0), "Scout", "Health"));
        watch.Pinned.Pin(Row(Guid.NewGuid(), new Entity(2, 0), "Guard", "Health"));

        Assert.Equal(2, watch.Variables!.Build().AllRows.Count);
        Assert.False(watch.Variables.Columns.ShowType);
        Assert.True(reg.Variables.Model.Columns.ShowType);
    }

    /// <summary>
    /// ⭐ A stale row is KEPT and refuses its dialog. ⛔ Dropping it would make the list silently
    /// shrink under the designer.
    /// </summary>
    [Fact]
    public void AStaleWatchRow_SurvivesAndRefusesEditing()
    {
        var reg = MakeRegistrar(breakpoints: new StubBreakpointManager());
        var row = Row(Guid.NewGuid(), new Entity(1, 0), "Scout", "Health");

        reg.Watch!.Pinned.Pin(row);
        Assert.True(reg.Watch.Pinned.MarkStale(row.Origin));

        var only = Assert.Single(reg.Watch.Variables!.Build().AllRows);
        Assert.True(only.IsStale);
        Assert.False(only.CanEverBeWritten);
    }

    // ══ the formatter that makes the Value column real ═══════════════════════

    /// <summary>
    /// ⭐⭐ 📐 Measured at Batch 79: every <c>VariableValueFormatter</c> in the repo lived in a TEST.
    /// ⛔ Hosting the table with a null decoder would have rendered every cell
    /// <c>&lt;unreadable&gt;</c> — a panel that looks wired and shows nothing. This asserts the
    /// registrar's formatter decodes real bytes.
    /// </summary>
    [Fact]
    public void TheRegistrarsFormatter_DecodesRealBytes()
    {
        var reg = MakeRegistrar();
        var row = Row(Guid.NewGuid(), new Entity(1, 0), "Scout", "Health",
                      clr: typeof(float), bytes: BitConverter.GetBytes(2.5f));

        Assert.Contains("2.5", reg.ValueFormatter.Cell(row));
    }

    /// <summary>⛔ And it fails VISIBLY rather than guessing when the bytes cannot be the type.</summary>
    [Fact]
    public void TheDecoder_RefusesUndersizedBytes()
        => Assert.Null(RawValueDecoder.Decode(new byte[] { 1, 2 }, typeof(long)));

    // ── helpers ─────────────────────────────────────────────────────────────

    private static string[] Names(AiVariablesWindow w)
        => w.Model.Build().AllRows.Select(r => r.ShortName).ToArray();

    private static IVariableRowSource Source(params string[] names)
        => new FixedVariableRowSource(
            names.Select(n => Row(Guid.NewGuid(), new Entity(1, 0), "Asset", n)).ToList());

    private static VariableRow Row(
        Guid assetId, Entity entity, string assetName, string name,
        Type? clr = null, byte[]? bytes = null)
        => new(
            Origin:    new VariableRowOrigin(assetId, entity, "s", name, assetName),
            ShortName: name,
            TypeText:  (clr ?? typeof(int)).Name,
            ClrType:   clr ?? typeof(int),
            ReadValue: () => bytes ?? new byte[4],
            AssetTick: null,
            RowKind:   VariableRowKind.Normal,
            IsStale:   false,
            HasEverBeenWritten: true);

    /// <summary>
    /// Minimal stub for IDataBreakpointManager providing only what the windows
    /// use (AllBreakpoints for drawing, identity for equality checks).
    /// </summary>
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

    private sealed class StubRefactor : IRefactorService
    {
        public IReadOnlyList<AssetReferenceInfo> FindReferences(string k) => Array.Empty<AssetReferenceInfo>();
        public IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid id) => Array.Empty<AssetReferenceInfo>();
        public RefactorPreview PreviewRename(string f, string t, RefactorOptions o) =>
            new(f, t, Array.Empty<RefactorFileEdit>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyRename(RefactorPreview p) => new(true, Array.Empty<string>(), null);
        public DeletePreview PreviewDelete(Guid id, DeleteOptions o) =>
            new(id, Array.Empty<AssetReferenceInfo>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyDelete(DeletePreview p) => new(true, Array.Empty<string>(), null);
        public System.Threading.Tasks.Task<RefactorPreview> PreviewRenameAsync(
            string f, string t, RefactorOptions o, System.Threading.CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(PreviewRename(f, t, o));
        public System.Threading.Tasks.Task<RefactorResult> ApplyRenameAsync(
            RefactorPreview p, System.Threading.CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(ApplyRename(p));
    }
}
