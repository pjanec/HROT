using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Fbt;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.BTree.Editor.Inspector;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;
using Hrot.Hsm.Editor.Inspector;
using Hrot.Hsm.Editor.Model;
using StructEdit.Core;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Inspector;

// ── Test-only DTO ─────────────────────────────────────────────────────────────
// Declared at file/namespace scope so StructEdit reflection can access all members.

internal enum DavTestDirection { None, Left, Right, Forward, Back }

internal struct DavTestActionParams
{
    public float Speed;
    public DavTestDirection Direction;
    public int Count;
}

/// <summary>
/// CT0 (BB1C) headless tests for the B-3 DefaultValueAuthoring helper:
/// <list type="bullet">
///   <item>Real StructEdit edit-service over a DTO with an enum field: hydrate → edit → commit →
///         serialize → assert JSON carries new values → rehydrate round-trips.</item>
///   <item>Accessor factory: the wired <c>expressionTargetFieldAccessor</c> returns the bound
///         variable name for a BTree Action facet and an HSM Transition facet.</item>
///   <item>B-5 tooltip constant is present and meaningful (non-empty, contains key words).</item>
/// </list>
/// </summary>
public sealed class DefaultValueAuthoringTests
{

    // ── Shared StructEdit service ─────────────────────────────────────────────

    private static IComponentEditService BuildSvc()
        => new ComponentEditServiceBuilder().Build();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IEnumerable<EditNode> AllNodes(EditNode root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var n in AllNodes(child))
            yield return n;
    }

    private static BehaviorTreeAsset MakeBTreeAsset(string methodFqn = "Ns.Act")
    {
        var blob = new BehaviorTreeBlob
        {
            TreeName        = "T",
            Nodes           = new[]
            {
                new NodeDefinition { Type = NodeType.Root,   ChildCount = 1, SubtreeOffset = 2 },
                new NodeDefinition { Type = NodeType.Action, ChildCount = 0, SubtreeOffset = 1, RawPayloadIndex = 0 },
            },
            MethodNames     = new[] { methodFqn },
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };
        return BehaviorTreeAssetProjector.Project(
            blob, null, null, Guid.NewGuid(), "T", "/t.cs", false, "", "");
    }

    private static HsmAsset MakeHsmAsset()
    {
        var b = new HsmBuilder("T");
        b.State("Idle").Initial().Final();
        var graph = b.Build();
        HsmNormalizer.Normalize(graph);
        var flat = HsmFlattener.Flatten(graph);
        var blob = HsmEmitter.Emit(flat);
        var meta = HsmEmitter.BuildMachineMetadata(graph);
        return HsmAssetProjector.Project(blob, meta, null, Guid.NewGuid(), "T", "", false, "");
    }

    // ── B-3: real StructEdit edit-service over a DTO with an enum field ───────

    [Fact]
    public void DefaultValueAuthoring_Hydrate_FromNull_ReturnsDefault()
    {
        var svc = BuildSvc();
        var entry = new BlackboardVariableEntry("p", typeof(DavTestActionParams), null);
        var instance = DefaultValueAuthoring.Hydrate(typeof(DavTestActionParams), null);
        instance.Should().BeOfType<DavTestActionParams>();
        var p = (DavTestActionParams)instance;
        p.Speed.Should().Be(0f, "default-constructed float is 0");
        p.Direction.Should().Be(DavTestDirection.None, "default-constructed enum is 0");
    }

    [Fact]
    public void DefaultValueAuthoring_Hydrate_FromJson_RestoresValues()
    {
        var dto = new DavTestActionParams { Speed = 5.5f, Direction = DavTestDirection.Right, Count = 3 };
        var json = JsonSerializer.Serialize(dto, DefaultValueAuthoring.JsonOptions);

        var restored = (DavTestActionParams)DefaultValueAuthoring.Hydrate(typeof(DavTestActionParams), json);

        restored.Speed.Should().Be(5.5f);
        restored.Direction.Should().Be(DavTestDirection.Right);
        restored.Count.Should().Be(3);
    }

    [Fact]
    public void DefaultValueAuthoring_OpenSession_EnumField_ProducesEnumNode()
    {
        var svc = BuildSvc();
        var entry = new BlackboardVariableEntry("p", typeof(DavTestActionParams), null);

        using var session = DefaultValueAuthoring.OpenSession(svc, entry);

        session.Should().NotBeNull();
        var nodes = AllNodes(session.Document.Root).ToList();
        var dirNode = nodes.FirstOrDefault(n => n.Name == nameof(DavTestActionParams.Direction));
        dirNode.Should().NotBeNull("Direction field must appear in the document");
        dirNode!.Kind.Should().Be(EditNodeKind.Enum,
            "enum fields must produce EditNodeKind.Enum nodes");
    }

    [Fact]
    public void DefaultValueAuthoring_EditEnumField_CommitAndSerialize_CarriesNewValue()
    {
        var svc = BuildSvc();
        // Hydrate from a JSON where Direction=Left, Speed=1.0.
        var initial = new DavTestActionParams { Speed = 1.0f, Direction = DavTestDirection.Left, Count = 0 };
        var initialJson = JsonSerializer.Serialize(initial, DefaultValueAuthoring.JsonOptions);
        var entry = new BlackboardVariableEntry("p", typeof(DavTestActionParams), null,
            DefaultValueJson: initialJson);

        using var session = DefaultValueAuthoring.OpenSession(svc, entry);
        var nodes = AllNodes(session.Document.Root).ToList();

        // Set Direction to Forward.
        var dirNode = nodes.First(n => n.Name == nameof(DavTestActionParams.Direction));
        dirNode.Binding!.SetBoxed(DavTestDirection.Forward);

        // CommitAndSerialize.
        var json = DefaultValueAuthoring.CommitAndSerialize(session, typeof(DavTestActionParams));

        // The JSON must carry the updated Direction value.
        json.Should().NotBeNullOrEmpty();
        var roundTripped = JsonSerializer.Deserialize<DavTestActionParams>(json, DefaultValueAuthoring.JsonOptions);
        roundTripped.Direction.Should().Be(DavTestDirection.Forward,
            "committed enum value Forward must appear in the serialized JSON");
        roundTripped.Speed.Should().Be(1.0f,
            "unchanged Speed value must be preserved in the serialized JSON");
    }

    [Fact]
    public void DefaultValueAuthoring_CommitSerializeRehydrate_RoundTrips()
    {
        var svc = BuildSvc();
        var initial = new DavTestActionParams { Speed = 3.14f, Direction = DavTestDirection.Back, Count = 7 };
        var initialJson = JsonSerializer.Serialize(initial, DefaultValueAuthoring.JsonOptions);
        var entry = new BlackboardVariableEntry("p", typeof(DavTestActionParams), null,
            DefaultValueJson: initialJson);

        using var session = DefaultValueAuthoring.OpenSession(svc, entry);
        var nodes = AllNodes(session.Document.Root).ToList();
        // Change Speed to 9.99.
        var speedNode = nodes.First(n => n.Name == nameof(DavTestActionParams.Speed));
        speedNode.Binding!.SetBoxed(9.99f);

        var (json, rehydrated) = DefaultValueAuthoring.CommitSerializeAndRehydrate(session, typeof(DavTestActionParams));
        var result = (DavTestActionParams)rehydrated;

        result.Speed.Should().BeApproximately(9.99f, 0.001f,
            "Speed must round-trip through CommitSerializeAndRehydrate");
        result.Direction.Should().Be(DavTestDirection.Back, "unchanged Direction must survive round-trip");
        result.Count.Should().Be(7, "unchanged Count must survive round-trip");

        // JSON must be parseable.
        var fromJson = JsonSerializer.Deserialize<DavTestActionParams>(json, DefaultValueAuthoring.JsonOptions);
        fromJson.Direction.Should().Be(DavTestDirection.Back);
    }

    // ── CT0: accessor returns bound variable name for BTree Action facet ──────

    /// <summary>
    /// Builds the standard expressionTargetFieldAccessor that mirrors what the composition
    /// root would wire.  Handles BTreeActionFacet, BTreeConditionFacet, TransitionFacet,
    /// GlobalTransitionFacet; returns null for other types.
    /// </summary>
    private static Func<object?, string?> BuildAccessor()
    {
        return facet => facet switch
        {
            BTreeActionFacet af    => af.ExpressionTargetField,
            BTreeConditionFacet cf => cf.ExpressionTargetField,
            TransitionFacet tf     => tf.ExpressionTargetField,
            GlobalTransitionFacet gtf => gtf.ExpressionTargetField,
            _                      => null,
        };
    }

    [Fact]
    public void ExpressionTargetFieldAccessor_BTreeAction_ReturnsBoundVarName()
    {
        const string fqn = "Ns.TestAction";
        var asset = MakeBTreeAsset(fqn);
        var actionNode = asset.Nodes.First(n => n.KernelType == NodeType.Action);

        // Set ExpressionTargetField on the action node.
        actionNode.Action!.ExpressionTargetField = "myAutoVar";

        var ctx    = new BTreeFacetFqnContext { CurrentActionFqn = fqn };
        var mapper = new BTreeFacetMapper(asset, ctx);
        var sel    = new BTreeNodeSelection(actionNode.VisualId);
        var facet  = mapper.GetFacet(sel)!;

        var accessor = BuildAccessor();
        var result   = accessor(facet);

        result.Should().Be("myAutoVar",
            "the accessor must return ExpressionTargetField from a BTreeActionFacet");
    }

    [Fact]
    public void ExpressionTargetFieldAccessor_BTreeAction_NullWhenNotBound()
    {
        const string fqn = "Ns.UnboundAction";
        var asset = MakeBTreeAsset(fqn);
        var actionNode = asset.Nodes.First(n => n.KernelType == NodeType.Action);
        // ExpressionTargetField is null by default.

        var mapper = new BTreeFacetMapper(asset);
        var sel    = new BTreeNodeSelection(actionNode.VisualId);
        var facet  = mapper.GetFacet(sel)!;

        var accessor = BuildAccessor();
        accessor(facet).Should().BeNull("unbound action must return null");
    }

    [Fact]
    public void ExpressionTargetFieldAccessor_HsmTransition_ReturnsBoundVarName()
    {
        var asset = MakeHsmAsset();
        var accessor = BuildAccessor();

        // Build a TransitionFacet directly with a bound ExpressionTargetField.
        var facet = new TransitionFacet
        {
            SourceStateName       = "Idle",
            TargetStateName       = "Active",
            ExpressionTargetField = "hsmAutoVar",
            EventId               = 0,
        };

        var result = accessor(facet);
        result.Should().Be("hsmAutoVar",
            "the accessor must return ExpressionTargetField from a TransitionFacet");
    }

    [Fact]
    public void ExpressionTargetFieldAccessor_NonActionFacet_ReturnsNull()
    {
        // A non-action facet (e.g. BTreeWaitFacet) must not cause errors and returns null.
        var accessor = BuildAccessor();
        var waitFacet = new BTreeWaitFacet { Duration = 1.0f };
        accessor(waitFacet).Should().BeNull("non-action facets return null from the accessor");
        accessor(null).Should().BeNull("null input returns null");
    }

    // ── CT0: PerspectiveWorkspaceRegistrar forwards the accessor ─────────────

    [Fact]
    public void PerspectiveRegistrar_ForwardsExpressionTargetFieldAccessor_ToInspector()
    {
        Func<object?, string?> accessor = BuildAccessor();

        var reg = new PerspectiveWorkspaceRegistrar(
            perspectiveName:               "BTree",
            selectionStore:                new EditorSelectionStore(),
            catalog:                       new Hrot.Editor.AiShared.Catalog.AssetCatalog(),
            refactorService:               new _StubRefactor(),
            debugRegistry:                 new Hrot.Editor.AiShared.Debug.DebugSessionRegistry(),
            expressionTargetFieldAccessor: accessor);

        reg.Inspector.HasExpressionTargetFieldAccessor.Should().BeTrue(
            "the expressionTargetFieldAccessor passed to the registrar must reach the Inspector");
    }

    [Fact]
    public void PerspectiveRegistrar_WithoutAccessor_InspectorHasNone()
    {
        var reg = new PerspectiveWorkspaceRegistrar(
            perspectiveName: "BTree",
            selectionStore:  new EditorSelectionStore(),
            catalog:         new Hrot.Editor.AiShared.Catalog.AssetCatalog(),
            refactorService: new _StubRefactor(),
            debugRegistry:   new Hrot.Editor.AiShared.Debug.DebugSessionRegistry());

        reg.Inspector.HasExpressionTargetFieldAccessor.Should().BeFalse(
            "without an accessor, the Inspector's flag must be false");
    }

    // ── B-5: static-vs-dynamic tooltip constant ───────────────────────────────

    [Fact]
    public void B5_StaticVsDynamicTooltip_IsNotEmpty()
    {
        DefaultValueAuthoring.StaticVsDynamicTooltip.Should().NotBeNullOrWhiteSpace(
            "tooltip must be a non-empty string");
    }

    [Fact]
    public void B5_StaticVsDynamicTooltip_MentionsBehaviorAssignment()
    {
        DefaultValueAuthoring.StaticVsDynamicTooltip
            .Should().Contain("behavior assignment",
                "tooltip must mention when static values are applied");
    }

    [Fact]
    public void B5_StaticVsDynamicTooltip_MentionsVariable()
    {
        DefaultValueAuthoring.StaticVsDynamicTooltip
            .Should().Contain("variable",
                "tooltip must mention binding a variable for live values");
    }
}

file sealed class _StubRefactor : IRefactorService
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
