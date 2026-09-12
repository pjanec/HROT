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
using Hrot.Hsm.Editor.Inspector;
using Hrot.Hsm.Editor.Model;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Inspector;

/// <summary>
/// AIE-023 behavioral tests for <see cref="HsmFacetDispatcher"/> and
/// <see cref="InspectorWindow"/> facet dispatch for the HSM perspective.
/// All tests are headless — no ImGui context.
/// </summary>
public sealed class HsmFacetDispatcherTests
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
        // Declare Active first so GoTo can resolve it.
        var b = new HsmBuilder("Simple");
        b.Event("Fire", 1);
        b.State("Active").Final();
        b.State("Idle").Initial().On("Fire").GoTo("Active");
        var (blob, meta) = Compile(b);
        return HsmAssetProjector.Project(blob, meta, null, Guid.NewGuid(), "Simple", "", false, "");
    }

    /// <summary>
    /// ⭐⭐ <b><c>S2</c> (<c>BP-399</c>) — the facet now resolves through
    /// <c>NodePropertiesSource</c>, not through <c>InspectorWindow</c>.</b>
    /// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §7.6 ②: the node arms were EXTRACTED to
    /// <c>details.nodeproperties</c>. ⚠ The claim each test below makes is UNCHANGED — it was always
    /// about the MAPPER; the window was only a convenient driver.
    /// <para>⭐ And the port is closer to production: the context is built by
    /// <c>DetailsContextBuilder</c>, the same call the shell makes every frame.</para>
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

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Inspector_HsmStateSelection_YieldsStateFacet()
    {
        var asset  = MakeSimpleAsset();
        var disp   = new HsmFacetDispatcher(asset);
        var store  = new EditorSelectionStore();
        store.ActiveAsset = asset;

        // Pick the "Idle" state (non-root).
        var idleState = asset.AllStates.First(s => s.Name == "Idle");
        var sel = new HsmStateSelection(idleState.StableId);
        store.ActiveSubSelection = sel;

        var facet = disp.GetFacet(sel);

        facet.Should().NotBeNull();
        facet.Should().BeOfType<StateFacet>();
        var sf = (StateFacet)facet!;
        sf.Name.Should().Be("Idle");
    }

    [Fact]
    public void Inspector_HsmTransitionSelection_YieldsTransitionFacet()
    {
        var asset  = MakeSimpleAsset();
        var disp   = new HsmFacetDispatcher(asset);

        var transition = asset.AllTransitions.First();
        var sel = new HsmTransitionSelection(transition.VisualId);

        var facet = disp.GetFacet(sel);

        facet.Should().BeOfType<TransitionFacet>();
        var tf = (TransitionFacet)facet!;
        tf.SourceStateName.Should().Be("Idle");
        tf.TargetStateName.Should().Be("Active");
    }

    [Fact]
    public void Inspector_HsmEventSelection_YieldsEventFacet()
    {
        var asset  = MakeSimpleAsset();
        var disp   = new HsmFacetDispatcher(asset);

        var ev  = asset.AllEvents.First();
        var sel = new HsmEventSelection(ev.EventId);

        var facet = disp.GetFacet(sel);

        facet.Should().BeOfType<EventFacet>();
        var ef = (EventFacet)facet!;
        ef.Name.Should().Be("Fire");
    }

    [Fact]
    public void Inspector_Commit_AppliesToAsset_AndMarksDirty()
    {
        var asset  = MakeSimpleAsset();
        var disp   = new HsmFacetDispatcher(asset);
        var store  = new EditorSelectionStore();
        store.ActiveAsset = asset;

        var idleState = asset.AllStates.First(s => s.Name == "Idle");
        var sel = new HsmStateSelection(idleState.StableId);
        store.ActiveSubSelection = sel;

        // Get facet and mutate it.
        var facet = (StateFacet)disp.GetFacet(sel)!;
        facet.Comment = "new comment";

        // Apply.
        disp.ApplyFacet(sel, facet);

        // Asset must be dirty and the node must have the new comment.
        asset.IsDirty.Should().BeTrue("apply must mark asset dirty");
        idleState.Comment.Should().Be("new comment");
    }

    [Fact]
    public void Inspector_NoSubSelection_FallsBackToAssetProperties()
    {
        var asset  = MakeSimpleAsset();
        var disp   = new HsmFacetDispatcher(asset);
        var store  = new EditorSelectionStore();
        store.ActiveAsset = asset;
        // No sub-selection.

        var source = MakeFacetSource(disp);

        source.FacetFor(ContextOf(store)).Should().BeNull(
            "no sub-selection means no facet");
    }

    [Fact]
    public void Inspector_WrongSubSelectionType_ReturnsNull()
    {
        var asset  = MakeSimpleAsset();
        var disp   = new HsmFacetDispatcher(asset);

        // BTree selection handed to HSM dispatcher.
        var btSel = new BTreeNodeSelection(Guid.NewGuid());
        disp.GetFacet(btSel).Should().BeNull("HSM dispatcher ignores BTree selections");
    }

    // ── stub ──────────────────────────────────────────────────────────────────

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
