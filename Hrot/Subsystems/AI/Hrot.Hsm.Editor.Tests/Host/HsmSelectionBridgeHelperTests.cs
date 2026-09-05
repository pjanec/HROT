using System;
using System.Linq;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Inspector;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Host;

/// <summary>
/// FIX-A headless tests for <see cref="HsmSelectionBridgeHelper.MapSelection"/>.
/// Tests the pure static mapping: (SelectionState, HsmAsset?) → HsmStateSelection?
/// and the full chain dispatcher + sub-selection → GetCurrentFacet() != null.
/// No ImGui context required.
/// </summary>
public sealed class HsmSelectionBridgeHelperTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static (HsmDefinitionBlob blob, MachineMetadata meta) Compile(HsmBuilder b)
    {
        var graph = b.Build();
        HsmNormalizer.Normalize(graph);
        var flat = HsmFlattener.Flatten(graph);
        return (HsmEmitter.Emit(flat), HsmEmitter.BuildMachineMetadata(graph));
    }

    private static HsmAsset MakeSimpleAsset()
    {
        var b = new HsmBuilder("Simple");
        b.Event("Fire", 1);
        b.State("Active").Final();
        b.State("Idle").Initial().On("Fire").GoTo("Active");
        var (blob, meta) = Compile(b);
        return HsmAssetProjector.Project(blob, meta, null, Guid.NewGuid(), "Simple", "", false, "");
    }

    /// <summary>
    /// ⭐⭐ <b><c>S2</c> (<c>BP-399</c>) — the facet resolves through <c>NodePropertiesSource</c> now.</b>
    /// 📄 §7.6 ②: <c>InspectorWindow</c>'s node arms were EXTRACTED to <c>details.nodeproperties</c>.
    /// ⚠ The claim below is unchanged — it is about the BRIDGE, and the window was only the driver.
    /// ⭐ The context is built by <c>DetailsContextBuilder</c>, the call the shell makes every frame.
    /// </summary>
    private static Hrot.Editor.AiShared.Shell.NodePropertiesSource MakeFacetSource(
        IFacetDispatcher? dispatcher = null)
    {
        var source = new Hrot.Editor.AiShared.Shell.NodePropertiesSource();
        source.SetFacetDispatcher(dispatcher);
        return source;
    }

    private static Hrot.Editor.AiShared.Shell.DetailsContext ContextOf(EditorSelectionStore store)
        => Hrot.Editor.AiShared.Shell.DetailsContextBuilder.Build(
               store, "HSM", Hrot.Editor.AiShared.Variables.VariableRunState.Planning);

    // ── MapSelection null / empty guards ──────────────────────────────────────

    [Fact]
    public void MapSelection_NullAsset_ReturnsNull()
    {
        var sel = new SelectionState();
        sel.ReplaceWith(SelectionEntry.OfNode(new NodeId(Guid.NewGuid())));

        HsmSelectionBridgeHelper.MapSelection(sel, hsmAsset: null).Should().BeNull();
    }

    [Fact]
    public void MapSelection_EmptySelection_ReturnsNull()
    {
        var asset = MakeSimpleAsset();
        HsmSelectionBridgeHelper.MapSelection(new SelectionState(), asset).Should().BeNull();
    }

    [Fact]
    public void MapSelection_MultipleNodesSelected_ReturnsNull()
    {
        var asset = MakeSimpleAsset();
        var sel   = new SelectionState();
        sel.ReplaceWith(new[]
        {
            SelectionEntry.OfNode(new NodeId(Guid.NewGuid())),
            SelectionEntry.OfNode(new NodeId(Guid.NewGuid())),
        });

        HsmSelectionBridgeHelper.MapSelection(sel, asset).Should().BeNull();
    }

    [Fact]
    public void MapSelection_UnknownLinkId_ReturnsNull()
    {
        // A canvas LinkId not present in the asset → returns null (stale-id guard).
        var asset = MakeSimpleAsset();
        var sel   = new SelectionState();
        sel.ReplaceWith(SelectionEntry.OfLink(new LinkId(Guid.NewGuid())));

        HsmSelectionBridgeHelper.MapSelection(sel, asset).Should().BeNull();
    }

    [Fact]
    public void MapSelection_MultipleLinksSelected_ReturnsNull()
    {
        var asset = MakeSimpleAsset();
        var sel   = new SelectionState();
        sel.ReplaceWith(new[]
        {
            SelectionEntry.OfLink(new LinkId(Guid.NewGuid())),
            SelectionEntry.OfLink(new LinkId(Guid.NewGuid())),
        });

        HsmSelectionBridgeHelper.MapSelection(sel, asset).Should().BeNull();
    }

    [Fact]
    public void MapSelection_UnknownNodeId_ReturnsNull()
    {
        // A canvas NodeId not present in the asset → returns null (stale-id guard).
        var asset = MakeSimpleAsset();
        var sel   = new SelectionState();
        sel.ReplaceWith(SelectionEntry.OfNode(new NodeId(Guid.NewGuid())));

        HsmSelectionBridgeHelper.MapSelection(sel, asset).Should().BeNull();
    }

    // ── MapSelection happy path ────────────────────────────────────────────────

    /// <summary>
    /// FIX-A core: canvas NodeId.Value == StateNode.StableId (HsmGraphModel contract).
    /// Selecting a state node returns HsmStateSelection with the correct StableId.
    /// </summary>
    [Fact]
    public void MapSelection_StateNodeSelected_ReturnsHsmStateSelection_WithCorrectStableId()
    {
        var asset     = MakeSimpleAsset();
        var idleState = asset.AllStates.First(s => s.Name == "Idle");

        var sel = new SelectionState();
        // Canvas NodeId.Value == StateNode.StableId per HsmGraphModel / StateNode.Id contract.
        sel.ReplaceWith(SelectionEntry.OfNode(new NodeId(idleState.StableId)));

        var result = HsmSelectionBridgeHelper.MapSelection(sel, asset);

        result.Should().NotBeNull();
        result.Should().BeOfType<HsmStateSelection>();
        ((HsmStateSelection)result!).StableId.Should().Be(idleState.StableId,
            "canvas NodeId.Value must equal StateNode.StableId");
    }

    [Fact]
    public void MapSelection_AnotherState_ReturnsCorrectStableId()
    {
        var asset       = MakeSimpleAsset();
        var activeState = asset.AllStates.First(s => s.Name == "Active");

        var sel = new SelectionState();
        sel.ReplaceWith(SelectionEntry.OfNode(new NodeId(activeState.StableId)));

        var result = HsmSelectionBridgeHelper.MapSelection(sel, asset);

        result.Should().NotBeNull();
        result.Should().BeOfType<HsmStateSelection>();
        ((HsmStateSelection)result!).StableId.Should().Be(activeState.StableId);
    }

    // ── MapSelection transition link happy path (HSM-TRANS) ───────────────────

    /// <summary>
    /// HSM-TRANS core: canvas LinkId.Value == TransitionNode.VisualId (HsmGraphModel contract).
    /// Selecting a transition link returns HsmTransitionSelection with the correct VisualId.
    /// </summary>
    [Fact]
    public void MapSelection_TransitionLinkSelected_ReturnsHsmTransitionSelection_WithCorrectVisualId()
    {
        var asset      = MakeSimpleAsset();
        // MakeSimpleAsset has Idle -Fire-> Active; that transition is the only one.
        var transition = asset.AllTransitions.First();

        var sel = new SelectionState();
        // Canvas LinkId.Value == TransitionNode.VisualId (HsmGraphModel / HsmTransitionLink contract).
        sel.ReplaceWith(SelectionEntry.OfLink(new LinkId(transition.VisualId)));

        var result = HsmSelectionBridgeHelper.MapSelection(sel, asset);

        result.Should().NotBeNull();
        result.Should().BeOfType<HsmTransitionSelection>(
            "a single selected canvas link maps to HsmTransitionSelection");
        ((HsmTransitionSelection)result!).VisualId.Should().Be(transition.VisualId,
            "canvas LinkId.Value must equal TransitionNode.VisualId");
    }

    /// <summary>
    /// ⚠⚠ <b><c>L0.2</c> RE-EXPRESSED THIS RAIL, and the change of premise IS the finding.</b>
    ///
    /// <para>🔴 It used to assert <c>MapSelection</c> returns the <b>state</b> for a mixed node+link
    /// selection — <i>"the state node is preferred"</i>. 📌 That preference was a FILTER DECISION MADE
    /// IN THE BRIDGE, which is exactly what <c>R-118</c> deletes: <i>"the bridge REPORTS, never
    /// filters."</i></para>
    ///
    /// <para>⭐⭐ <b>The tie-break is not lost — it became ORDER.</b> <c>MapSelections</c> reports the
    /// state FIRST and the transition second, so a ranked consumer still reads <i>state wins</i>.
    /// ⛔ <b>But the derived single is now <c>null</c></b>, because two things really are selected —
    /// and <c>null</c> here means one fact *(not exactly one)*, not the three it used to mean.</para>
    ///
    /// <para>⚠ <b>The user-visible consequence, stated rather than buried:</b> until <c>L1.4</c>'s
    /// predicate and <c>R-117</c>'s grey line land in <c>L1</c>/<c>L2</c>, a mixed selection shows
    /// NOTHING where it used to show the state. ⭐ That is the design's intended end state arriving one
    /// layer early — ⛔ not a regression anyone chose to accept silently.</para>
    /// </summary>
    [Fact]
    public void MapSelections_MixedNodeAndLink_ReportsBoth_StateFirst()
    {
        var asset      = MakeSimpleAsset();
        var idleState  = asset.AllStates.First(s => s.Name == "Idle");
        var transition = asset.AllTransitions.First();

        var sel = new SelectionState();
        sel.ReplaceWith(new[]
        {
            SelectionEntry.OfNode(new NodeId(idleState.StableId)),
            SelectionEntry.OfLink(new LinkId(transition.VisualId)),
        });

        var all = HsmSelectionBridgeHelper.MapSelections(sel, asset);

        // ⭐ BOTH are reported — the bridge no longer discards the transition.
        all.Should().HaveCount(2, "R-118: the bridge reports every selected element");
        all[0].Should().BeOfType<HsmStateSelection>(
            "the state-wins tie-break survives as ORDER: nodes are reported before links");
        ((HsmStateSelection)all[0]).StableId.Should().Be(idleState.StableId);
        all[1].Should().BeOfType<HsmTransitionSelection>();
        ((HsmTransitionSelection)all[1]).VisualId.Should().Be(transition.VisualId);

        // ⛔ And the derived single is null, because two things are selected. ⚠ This is the measured
        //   behaviour change — the old rail asserted HsmStateSelection here.
        HsmSelectionBridgeHelper.MapSelection(sel, asset).Should().BeNull(
            "two selected elements are not 'exactly one'; L1.4's predicate is where that question now lives");
    }

    /// <summary>
    /// ⭐⭐ <b><c>L0.2</c> — an UNRESOLVABLE id is DROPPED, not fatal.</b> 📄 §6 <c>L0.2</c>, verbatim:
    /// <i>"an unresolvable node is dropped, not fatal."</i> ⛔ The deleted code returned <c>null</c> for
    /// the WHOLE selection, so one stale canvas id discarded the designer's other selections.
    /// </summary>
    [Fact]
    public void MapSelections_AStaleId_IsDropped_AndTheRestSurvive()
    {
        var asset     = MakeSimpleAsset();
        var idleState = asset.AllStates.First(s => s.Name == "Idle");

        var sel = new SelectionState();
        sel.ReplaceWith(new[]
        {
            SelectionEntry.OfNode(new NodeId(idleState.StableId)),
            SelectionEntry.OfNode(new NodeId(Guid.NewGuid())),   // ⛔ belongs to no state in this asset
        });

        var all = HsmSelectionBridgeHelper.MapSelections(sel, asset);

        all.Should().ContainSingle("the stale id is dropped and the real one survives");
        ((HsmStateSelection)all[0]).StableId.Should().Be(idleState.StableId);
    }

    // ── GetCurrentFacet integration ───────────────────────────────────────────

    /// <summary>
    /// FIX-A end-to-end headless: confirms the full chain
    ///   SetFacetDispatcher(BuildFacetDispatcher(asset)) +
    ///   ActiveSubSelection = new HsmStateSelection(stableId)
    ///   → source.FacetFor(ContextOf(store)) != null.
    /// This is the exact condition the lead's symptom report describes as broken.
    /// </summary>
    [Fact]
    public void GetCurrentFacet_ReturnsNonNull_WhenDispatcherAndSubSelectionAreWired()
    {
        var asset     = MakeSimpleAsset();
        var idleState = asset.AllStates.First(s => s.Name == "Idle");

        var store = new EditorSelectionStore();
        store.ActiveAsset = asset;

        var dispatcher = HsmSelectionBridgeHelper.BuildFacetDispatcher(asset);
        dispatcher.Should().NotBeNull();

        var source     = MakeFacetSource(dispatcher);

        // Simulate what AfterDraw publishes: map canvas node click → HsmStateSelection.
        var sel = new SelectionState();
        sel.ReplaceWith(SelectionEntry.OfNode(new NodeId(idleState.StableId)));
        var subSel = HsmSelectionBridgeHelper.MapSelection(sel, asset);
        store.ActiveSubSelection = subSel;

        var facet = source.FacetFor(ContextOf(store));

        facet.Should().NotBeNull(
            "dispatcher + ActiveSubSelection → GetCurrentFacet must return the state facet");
        facet.Should().BeOfType<StateFacet>(
            "clicking an HSM state must yield a StateFacet");
        var sf = (StateFacet)facet!;
        sf.Name.Should().Be("Idle");
    }

    /// <summary>
    /// HSM-TRANS end-to-end headless: confirms the full chain
    ///   SetFacetDispatcher(BuildFacetDispatcher(asset)) +
    ///   ActiveSubSelection = new HsmTransitionSelection(visualId)
    ///   → source.FacetFor(ContextOf(store)) returns TransitionFacet.
    /// </summary>
    [Fact]
    public void GetCurrentFacet_ReturnsTransitionFacet_WhenTransitionSubSelectionIsWired()
    {
        var asset      = MakeSimpleAsset();
        var transition = asset.AllTransitions.First();

        var store = new EditorSelectionStore();
        store.ActiveAsset = asset;

        var dispatcher = HsmSelectionBridgeHelper.BuildFacetDispatcher(asset);
        dispatcher.Should().NotBeNull();

        var source     = MakeFacetSource(dispatcher);

        // Simulate what AfterDraw publishes: map canvas link click → HsmTransitionSelection.
        var sel = new SelectionState();
        sel.ReplaceWith(SelectionEntry.OfLink(new LinkId(transition.VisualId)));
        var subSel = HsmSelectionBridgeHelper.MapSelection(sel, asset);
        store.ActiveSubSelection = subSel;

        var facet = source.FacetFor(ContextOf(store));

        facet.Should().NotBeNull(
            "dispatcher + ActiveSubSelection → GetCurrentFacet must return the transition facet");
        facet.Should().BeOfType<TransitionFacet>(
            "clicking an HSM transition must yield a TransitionFacet");
        var tf = (TransitionFacet)facet!;
        tf.SourceStateName.Should().Be("Idle");
        tf.TargetStateName.Should().Be("Active");
    }

    // ── BuildFacetDispatcher null guard ───────────────────────────────────────

    [Fact]
    public void BuildFacetDispatcher_NullAsset_ReturnsNull()
    {
        HsmSelectionBridgeHelper.BuildFacetDispatcher(null).Should().BeNull();
    }

    // ── stub helpers ──────────────────────────────────────────────────────────

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
        public Task<RefactorPreview> PreviewRenameAsync(string f, string t, RefactorOptions o, CancellationToken ct = default) =>
            Task.FromResult(PreviewRename(f, t, o));
        public Task<RefactorResult> ApplyRenameAsync(RefactorPreview p, CancellationToken ct = default) =>
            Task.FromResult(ApplyRename(p));
    }
}
