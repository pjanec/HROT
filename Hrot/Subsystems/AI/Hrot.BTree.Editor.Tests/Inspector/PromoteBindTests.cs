using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Inspector;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector.ActionBinding;
using Hrot.Editor.AiShared.Selection;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Inspector;

/// <summary>
/// Corrective Task 0: headless tests proving that the Promote gesture creates an
/// auto-variable AND binds ExpressionTargetField via the ApplyFacet path.
/// The ImGui button click that drives <c>ActionBindingDrawer.DrawInput</c> is replaced by the equivalent headless
/// sequence (⭐ <c>CE-417</c> slice 4b — the facet's binding carries the site id the promote names the variable after):
///   1. mapper.GetFacet
///   2. sources.Promote(facet.Action)  → returns newName
///   3. Build an edited facet with Action.ExpressionTargetField = newName
///   4. mapper.ApplyFacet  → persists into asset
/// </summary>
public sealed class PromoteBindTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private sealed class StubExporter : IActionSchemaExporter
    {
        private readonly Dictionary<string, ActionSchemaEntry> _map;
        public IReadOnlyDictionary<string, ActionSchemaEntry> All => _map;
        public event Action? Changed { add { } remove { } }
        public StubExporter(params ActionSchemaEntry[] entries)
        {
            _map = new Dictionary<string, ActionSchemaEntry>(StringComparer.Ordinal);
            foreach (var e in entries) _map[e.Fqn] = e;
        }
        public ActionSchemaEntry? Lookup(string fqn) => _map.GetValueOrDefault(fqn);
        public void Rebuild() { }
    }

    /// <summary>
    /// Build a minimal BTree asset that has one Action node.
    /// Returns the asset and the VisualId of the action node.
    /// Uses no explicit debug metadata so ProjectVisualId is auto-minted;
    /// the action node is discovered from asset.Nodes after projection.
    /// </summary>
    private static (BehaviorTreeAsset asset, Guid actionVisualId) MakeAssetWithAction(
        string fqn = "Ns.FloatAction")
    {
        var blob = new BehaviorTreeBlob
        {
            TreeName        = "T",
            Nodes           = new[]
            {
                new NodeDefinition { Type = NodeType.Root,   ChildCount = 1, SubtreeOffset = 2 },
                new NodeDefinition { Type = NodeType.Action, ChildCount = 0, SubtreeOffset = 1, RawPayloadIndex = 0 },
            },
            MethodNames     = new[] { fqn },
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };
        var asset = BehaviorTreeAssetProjector.Project(
            blob, null, null, Guid.NewGuid(), "T", "/t.cs", false, "", "");

        // Discover the action node from the projected asset.
        var actionNode = asset.Nodes.First(n => n.KernelType == NodeType.Action);
        // ⚠ A blob carries no delegate shape, so the projector says FourParamFull (whole blackboard) — which has no
        //   per-binding variable to promote (DelegateShapeGuardTests). A promotable action binds ONE variable.
        actionNode.DelegateShape = BTreeActionDelegateShape.ThreeParamReusable;
        return (asset, actionNode.VisualId);
    }

    // ── Promote creates variable and sets ExpressionTargetField ──────────────

    [Fact]
    public void Promote_CreatesVar_AndFacetApply_SetsExpressionTargetField_BTree()
    {
        const string fqn = "Ns.FloatAction";
        var (asset, nodeVisualId) = MakeAssetWithAction(fqn);
        var entry    = new ActionSchemaEntry(fqn, typeof(float), ActionHosting.BTree, BlackboardAccess.ReadWrite);
        var exporter = new StubExporter(entry);
        var mapper   = new BTreeFacetMapper(asset);
        var sources  = new ActionBindingSources(asset, _ => Array.Empty<string>(), exporter);

        // Step 1: Get the facet (its binding carries the node's id).
        var sel   = new BTreeNodeSelection(nodeVisualId);
        var facet = (BTreeActionFacet)mapper.GetFacet(sel)!;

        // Step 2: Simulate DrawInput clicking "Promote".
        facet.Action.SiteId.Should().Be(nodeVisualId.ToString(), "the mapper must carry the node id on the binding");
        var newName = sources.Promote(facet.Action);
        newName.Should().NotBeNull("Promote must succeed for a known FQN");

        // Step 3: Apply the facet with the new name bound.
        facet.Action.ExpressionTargetField = newName;
        mapper.ApplyFacet(sel, facet);

        // Assert: auto-variable created in asset.
        var created = asset.BlackboardVariables.Should().ContainSingle().Subject;
        created.Name.Should().Be(newName);
        created.FieldType.Should().Be(typeof(float));
        created.IsAutoManaged.Should().BeTrue();

        // Assert: ExpressionTargetField persisted on the node.
        var node = asset.FindNode(nodeVisualId)!;
        node.Action!.ExpressionTargetField.Should().Be(newName,
            "ApplyFacet must persist ExpressionTargetField from the edited facet");
    }

    [Fact]
    public void Promote_AndApplyFacet_BindingSurvivesRoundTrip_BTree()
    {
        const string fqn = "Ns.IntAction";
        var (asset, nodeVisualId) = MakeAssetWithAction(fqn);
        var entry    = new ActionSchemaEntry(fqn, typeof(int), ActionHosting.BTree, BlackboardAccess.ReadWrite);
        var exporter = new StubExporter(entry);
        var mapper   = new BTreeFacetMapper(asset);
        var sources  = new ActionBindingSources(asset, _ => Array.Empty<string>(), exporter);

        // Simulate promote + bind.
        var sel    = new BTreeNodeSelection(nodeVisualId);
        var facet  = (BTreeActionFacet)mapper.GetFacet(sel)!;
        var name   = sources.Promote(facet.Action)!;
        facet.Action.ExpressionTargetField = name;
        mapper.ApplyFacet(sel, facet);

        // Round-trip through DTO.
        var restored = BehaviorTreeAssetMapper.FromDto(BehaviorTreeAssetMapper.ToDto(asset));

        // Asset still has the auto-variable.
        var restoredVar = restored.BlackboardVariables.Should().ContainSingle().Subject;
        restoredVar.Name.Should().Be(name, "auto-variable must survive DTO round-trip");
        restoredVar.IsAutoManaged.Should().BeTrue();

        // ExpressionTargetField preserved in round-tripped node.
        var restoredNode = restored.FindNode(nodeVisualId)!;
        restoredNode.Action!.ExpressionTargetField.Should().Be(name,
            "ExpressionTargetField must survive model→DTO→model round-trip");
    }

    [Fact]
    public void Promote_SecondCallSameId_IsIdempotent_BindingUnchanged_BTree()
    {
        const string fqn = "Ns.FloatAction";
        var (asset, nodeVisualId) = MakeAssetWithAction(fqn);
        var entry    = new ActionSchemaEntry(fqn, typeof(float), ActionHosting.BTree, BlackboardAccess.ReadWrite);
        var exporter = new StubExporter(entry);
        var mapper   = new BTreeFacetMapper(asset);
        var sources  = new ActionBindingSources(asset, _ => Array.Empty<string>(), exporter);

        var sel   = new BTreeNodeSelection(nodeVisualId);
        var facet = (BTreeActionFacet)mapper.GetFacet(sel)!;
        var name1 = sources.Promote(facet.Action)!;
        var name2 = sources.Promote(facet.Action)!;

        name1.Should().Be(name2, "same visualId must always produce the same auto-name");
        asset.BlackboardVariables.Should().HaveCount(1, "second promote is idempotent — no duplicate");
    }

    [Fact]
    public void TheMappersBinding_CarriesTheNodeId_BTree()
    {
        var (asset, nodeVisualId) = MakeAssetWithAction("Ns.BoolAction");

        var facet = (BTreeActionFacet)new BTreeFacetMapper(asset).GetFacet(new BTreeNodeSelection(nodeVisualId))!;

        facet.Action.SiteId.Should().Be(nodeVisualId.ToString(),
            "the promoted variable is named after the node (_auto_{id}), so the binding must carry it");
        facet.Action.SiteSlot.Should().BeNull("a BTree node has one binding — the primary one");
    }
}
