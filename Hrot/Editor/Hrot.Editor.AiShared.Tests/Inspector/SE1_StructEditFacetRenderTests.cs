using System;
using System.Collections.Generic;
using System.Linq;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Inspector;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;
using StructEdit.Core;
using StructEdit.Reflection;

namespace Hrot.Editor.AiShared.Tests.Inspector;

/// <summary>
/// SE1 headless tests: verifies that a StructEdit <see cref="IComponentEditService"/> correctly
/// builds an <see cref="EditDocument"/> for BTree/HSM facet structs, that enum fields produce
/// <see cref="EditNodeKind.Enum"/> nodes, that picker attributes flow into
/// <see cref="EditNodeMetadata.CustomAttributes"/>, and that a round-trip commit via
/// <see cref="InspectorWindow.CommitCurrentFacet"/> reaches the asset model.
/// All tests are headless — no ImGui context required.
/// </summary>
public sealed class SE1_StructEditFacetRenderTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Builds a minimal IComponentEditService (no custom drawers needed for headless).</summary>
    private static IComponentEditService BuildEditService()
        => new ComponentEditServiceBuilder().Build();

    /// <summary>Opens a managed (boxed) edit session for a value-type facet.</summary>
    private static IEditSession OpenSession(IComponentEditService svc, object facet)
        => svc.Open(facet, facet.GetType());

    /// <summary>Finds all leaf nodes (DFS) in a document root.</summary>
    private static IEnumerable<EditNode> AllNodes(EditNode root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var n in AllNodes(child))
            yield return n;
    }

    // ── Simple BTreeActionFacet tests ────────────────────────────────────────

    [Fact]
    public void EditService_OpensFacet_DocumentHasFieldNodes()
    {
        var svc   = BuildEditService();
        var facet = new BTreeActionFacet
        {
            MethodFqn            = "Ns.C.DoThing",
            ExpressionTargetField = null,
            Comment              = "test comment",
            IsBreakpoint         = false,
            VisualId             = Guid.NewGuid().ToString(),
            LastResult           = "Success",
            TickCount            = 3,
        };

        using var session = OpenSession(svc, facet);

        session.Should().NotBeNull();
        session.Document.Should().NotBeNull();

        var nodes = AllNodes(session.Document.Root).ToList();

        // There must be a node for every public field (root = the struct itself).
        nodes.Should().Contain(n => n.Name == nameof(BTreeActionFacet.MethodFqn),
            "MethodFqn is a public field");
        nodes.Should().Contain(n => n.Name == nameof(BTreeActionFacet.IsBreakpoint),
            "IsBreakpoint is a public bool field");
        nodes.Should().Contain(n => n.Name == nameof(BTreeActionFacet.Comment),
            "Comment is a public string field");
    }

    [Fact]
    public void EditService_BoolField_ProducesBooleanNode()
    {
        var svc   = BuildEditService();
        var facet = new BTreeActionFacet { IsBreakpoint = true };

        using var session = OpenSession(svc, facet);
        var nodes = AllNodes(session.Document.Root).ToList();

        var bpNode = nodes.FirstOrDefault(n => n.Name == nameof(BTreeActionFacet.IsBreakpoint));
        bpNode.Should().NotBeNull("IsBreakpoint field must exist in the document");
        bpNode!.Kind.Should().Be(EditNodeKind.Boolean,
            "bool fields must render as EditNodeKind.Boolean");
    }

    [Fact]
    public void EditService_StringFieldWithBehaviorHashPicker_CarriesPickerAttribute()
    {
        var svc   = BuildEditService();
        var facet = new BTreeActionFacet { MethodFqn = "Ns.C.DoThing" };

        using var session = OpenSession(svc, facet);
        var nodes = AllNodes(session.Document.Root).ToList();

        var methodNode = nodes.FirstOrDefault(n => n.Name == nameof(BTreeActionFacet.MethodFqn));
        methodNode.Should().NotBeNull("MethodFqn field must exist in the document");

        // The [BehaviorHashPicker] attribute on the field must be collected in CustomAttributes.
        methodNode!.Metadata.CustomAttributes
            .Should().Contain(a => a is BehaviorHashPickerAttribute,
                "MethodFqn carries [BehaviorHashPicker] — must flow into EditNodeMetadata.CustomAttributes");
    }

    [Fact]
    public void EditService_StringFieldWithBlackboardFieldPicker_CarriesPickerAttribute()
    {
        var svc   = BuildEditService();
        var facet = new BTreeActionFacet { ExpressionTargetField = "speed" };

        using var session = OpenSession(svc, facet);
        var nodes = AllNodes(session.Document.Root).ToList();

        var exprNode = nodes.FirstOrDefault(n => n.Name == nameof(BTreeActionFacet.ExpressionTargetField));
        exprNode.Should().NotBeNull("ExpressionTargetField must exist in the document");

        exprNode!.Metadata.CustomAttributes
            .Should().Contain(a => a is BlackboardFieldPickerAttribute,
                "ExpressionTargetField carries [BlackboardFieldPicker]");
    }

    // ── Enum node test ────────────────────────────────────────────────────────

    /// <summary>A minimal struct that has an enum field — used to test EditNodeKind.Enum.</summary>
    private struct StructWithEnum
    {
        public string Name;
        public SampleKind Kind;  // enum field
        public bool Flag;
    }

    private enum SampleKind { None, Alpha, Beta }

    [Fact]
    public void EditService_EnumField_ProducesEnumNode()
    {
        var svc   = BuildEditService();
        var facet = new StructWithEnum { Name = "test", Kind = SampleKind.Alpha, Flag = true };

        using var session = OpenSession(svc, facet);
        var nodes = AllNodes(session.Document.Root).ToList();

        var kindNode = nodes.FirstOrDefault(n => n.Name == nameof(StructWithEnum.Kind));
        kindNode.Should().NotBeNull("Kind field must exist in the document");
        kindNode!.Kind.Should().Be(EditNodeKind.Enum,
            "enum fields must produce EditNodeKind.Enum — combos are rendered automatically by ComponentEditDrawer");
    }

    // ── Round-trip: set value + Commit ────────────────────────────────────────

    [Fact]
    public void EditService_SetBoolValue_CommitReturnsMutatedFacet()
    {
        var svc   = BuildEditService();
        var facet = new BTreeActionFacet
        {
            MethodFqn    = "Ns.C.Action",
            IsBreakpoint = false,
            VisualId     = Guid.NewGuid().ToString(),
            LastResult   = "Running",
            TickCount    = 0,
        };

        using var session = OpenSession(svc, facet);
        var nodes = AllNodes(session.Document.Root).ToList();

        // Find the IsBreakpoint node and set it to true.
        var bpNode = nodes.First(n => n.Name == nameof(BTreeActionFacet.IsBreakpoint));
        bpNode.Binding.Should().NotBeNull("IsBreakpoint node must have a binding");
        bpNode.Binding!.SetBoxed(true);

        // Commit returns the modified facet.
        var committed = session.Commit();
        committed.Should().BeOfType<BTreeActionFacet>();
        var committedFacet = (BTreeActionFacet)committed;
        committedFacet.IsBreakpoint.Should().BeTrue(
            "setting IsBreakpoint=true and committing must produce a facet with IsBreakpoint=true");
    }

    // ── InspectorWindow CommitCurrentFacet round-trip ─────────────────────────

    private static BehaviorTreeBlob MakeMinimalBlob() => new()
    {
        TreeName = "T",
        Nodes = new[]
        {
            new NodeDefinition { Type = NodeType.Root,   ChildCount = 1, SubtreeOffset = 2 },
            new NodeDefinition { Type = NodeType.Action, ChildCount = 0, SubtreeOffset = 1, RawPayloadIndex = 0 },
        },
        MethodNames     = new[] { "Ns.C.Original" },
        FloatParams     = Array.Empty<float>(),
        IntParams       = Array.Empty<int>(),
        SubtreeAssetIds = Array.Empty<string>(),
    };

    private static BehaviorTreeAsset MakeAsset(BehaviorTreeBlob blob) =>
        BehaviorTreeAssetProjector.Project(
            blob, null, null,
            Guid.NewGuid(), blob.TreeName, "/test.cs", false,
            string.Empty, string.Empty);

    /// <summary>
    /// ⭐⭐ <b><c>S2</c> (<c>BP-399</c>) — the facet arm is a Details VIEW now, so these rails drive the
    /// SOURCE and the VIEW instead of <c>InspectorWindow</c>.</b> 📄 §7.6 ② · §7.4.
    /// ⚠ Every claim below is unchanged; ⭐ and the context is built by <c>DetailsContextBuilder</c>,
    /// which is what the shell calls every frame.
    /// </summary>
    private static Hrot.Editor.AiShared.Shell.NodePropertiesSource MakeSource(
        IFacetDispatcher? dispatcher = null,
        IComponentEditService? editSvc = null)
    {
        var source = new Hrot.Editor.AiShared.Shell.NodePropertiesSource();
        source.SetFacetDispatcher(dispatcher);
        source.SetFacetEditService(editSvc);
        return source;
    }

    private static Hrot.Editor.AiShared.Shell.DetailsContext ContextOf(EditorSelectionStore store)
        => Hrot.Editor.AiShared.Shell.DetailsContextBuilder.Build(
               store, "BTree", Hrot.Editor.AiShared.Variables.VariableRunState.Planning);

    /// <remarks>⭐ <c>S2</c>: was <c>CommitCurrentFacet_AppliesEditedFacetToAsset</c> — the commit path
    /// moved from <c>InspectorWindow</c> to <c>NodePropertiesSource</c>, unchanged in substance.</remarks>
    [Fact]
    public void CommitFacet_AppliesEditedFacetToAsset()
    {
        var blob   = MakeMinimalBlob();
        var asset  = MakeAsset(blob);
        var mapper = new BTreeFacetMapper(asset);
        var store  = new EditorSelectionStore();
        store.ActiveAsset = asset;

        var actionNode = asset.Nodes.First(n => n.KernelType == NodeType.Action);
        var sel = new BTreeNodeSelection(actionNode.VisualId);
        store.ActiveSubSelection = sel;

        var svc     = BuildEditService();
        var source  = MakeSource(mapper, svc);
        var context = ContextOf(store);

        // Get the initial facet.
        var original = (BTreeActionFacet)source.FacetFor(context)!;
        original.MethodFqn.Should().Be("Ns.C.Original");

        // Open a session, mutate, commit through the window.
        using var session = svc.Open(original, typeof(BTreeActionFacet));
        var nodes   = AllNodes(session.Document.Root).ToList();
        var mfqNode = nodes.First(n => n.Name == nameof(BTreeActionFacet.MethodFqn));
        mfqNode.Binding!.SetBoxed("Ns.C.Updated");
        var committed = session.Commit();

        source.CommitFacet(context, committed);

        // The asset must reflect the updated method FQN.
        asset.IsDirty.Should().BeTrue("CommitFacet must mark the asset dirty");
        actionNode.Action!.MethodFqn.Should().Be("Ns.C.Updated",
            "the committed facet's MethodFqn must be written back to the asset");
    }

    /// <remarks>⭐ <c>S2</c>: was <c>InspectorWindow_GetFacetSession_IsNullWhenNoEditService</c>.
    /// ⚠ The session belongs to the VIEW INSTANCE now *(§1: an uncommitted edit buffer is legitimately
    /// per-instance)*, while the facet itself comes from the shared source.</remarks>
    [Fact]
    public void NodePropertiesView_FacetSession_IsNullWhenNoEditService()
    {
        // When no edit service is wired, the view holds no session (and nothing crashes).
        var blob   = MakeMinimalBlob();
        var asset  = MakeAsset(blob);
        var mapper = new BTreeFacetMapper(asset);
        var store  = new EditorSelectionStore();
        store.ActiveAsset = asset;

        var actionNode = asset.Nodes.First(n => n.KernelType == NodeType.Action);
        store.ActiveSubSelection = new BTreeNodeSelection(actionNode.VisualId);

        var source = MakeSource(mapper, editSvc: null);
        using var view = new Hrot.Editor.AiShared.Shell.NodePropertiesDetailsView(source);

        // The facet still resolves headlessly.
        source.FacetFor(ContextOf(store)).Should().NotBeNull("dispatcher is wired");
        // No edit service → no session. ⛔ And no ImGui was needed to say so.
        view.FacetSession.Should().BeNull("no edit service wired");
    }
}

// ── Stub refactor service for SE1 tests ──────────────────────────────────────

file sealed class StubRefactorSE1 : IRefactorService
{
    public IReadOnlyList<AssetReferenceInfo> FindReferences(string k) => Array.Empty<AssetReferenceInfo>();
    public IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid id) => Array.Empty<AssetReferenceInfo>();
    public RefactorPreview PreviewRename(string f, string t, RefactorOptions o) =>
        new(f, t, Array.Empty<RefactorFileEdit>(), Array.Empty<RefactorIssue>());
    public RefactorResult ApplyRename(RefactorPreview p) =>
        new(true, Array.Empty<string>(), null);
    public DeletePreview PreviewDelete(Guid id, DeleteOptions o) =>
        new(id, Array.Empty<AssetReferenceInfo>(), Array.Empty<RefactorIssue>());
    public RefactorResult ApplyDelete(DeletePreview p) =>
        new(true, Array.Empty<string>(), null);
    public Task<RefactorPreview> PreviewRenameAsync(string f, string t, RefactorOptions o, CancellationToken ct = default) =>
        Task.FromResult(PreviewRename(f, t, o));
    public Task<RefactorResult> ApplyRenameAsync(RefactorPreview p, CancellationToken ct = default) =>
        Task.FromResult(ApplyRename(p));
}
