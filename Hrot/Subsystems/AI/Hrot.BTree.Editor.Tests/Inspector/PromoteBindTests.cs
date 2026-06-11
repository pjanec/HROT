using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Inspector;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Selection;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Inspector;

/// <summary>
/// Corrective Task 0: headless tests proving that the Promote gesture creates an
/// auto-variable AND binds ExpressionTargetField via the ApplyFacet path.
/// The ImGui button click that drives <see cref="BlackboardFieldPickerDrawer.DrawInput"/> is
/// replaced by the equivalent headless sequence:
///   1. mapper.GetFacet  (populates fqnContext.CurrentNodeVisualId)
///   2. drawer.Promote(visualId)  → returns newName
///   3. Build an edited facet with ExpressionTargetField = newName
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
        return (asset, actionNode.VisualId);
    }

    // ── Promote creates variable and sets ExpressionTargetField ──────────────

    [Fact]
    public void Promote_CreatesVar_AndFacetApply_SetsExpressionTargetField_BTree()
    {
        const string fqn = "Ns.FloatAction";
        var (asset, nodeVisualId) = MakeAssetWithAction(fqn);
        var entry    = new ActionSchemaEntry(fqn, typeof(float), ActionHosting.BTree, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);
        var ctx      = new BTreeFacetFqnContext { CurrentActionFqn = fqn };
        var mapper   = new BTreeFacetMapper(asset, ctx);
        var drawer   = new BlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn, ctx);

        // Step 1: Get the facet (populates CurrentNodeVisualId via mapper).
        var sel   = new BTreeNodeSelection(nodeVisualId);
        var facet = (BTreeActionFacet)mapper.GetFacet(sel)!;

        // Step 2: Simulate DrawInput clicking "Promote".
        var visualId = ctx.CurrentNodeVisualId;
        visualId.Should().Be(nodeVisualId.ToString(), "mapper must populate CurrentNodeVisualId");
        var newName = drawer.Promote(visualId!);
        newName.Should().NotBeNull("Promote must succeed for a known FQN");

        // Step 3: Apply the facet with the new name bound.
        facet.ExpressionTargetField = newName;
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
        var entry    = new ActionSchemaEntry(fqn, typeof(int), ActionHosting.BTree, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);
        var ctx      = new BTreeFacetFqnContext { CurrentActionFqn = fqn };
        var mapper   = new BTreeFacetMapper(asset, ctx);
        var drawer   = new BlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn, ctx);

        // Simulate promote + bind.
        var sel    = new BTreeNodeSelection(nodeVisualId);
        var facet  = (BTreeActionFacet)mapper.GetFacet(sel)!;
        var name   = drawer.Promote(ctx.CurrentNodeVisualId!)!;
        facet.ExpressionTargetField = name;
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
        var entry    = new ActionSchemaEntry(fqn, typeof(float), ActionHosting.BTree, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);
        var ctx      = new BTreeFacetFqnContext { CurrentActionFqn = fqn };
        var mapper   = new BTreeFacetMapper(asset, ctx);
        var drawer   = new BlackboardFieldPickerDrawer(asset, exporter, () => ctx.CurrentActionFqn, ctx);

        var sel   = new BTreeNodeSelection(nodeVisualId);
        mapper.GetFacet(sel);  // populate context
        var name1 = drawer.Promote(ctx.CurrentNodeVisualId!)!;
        var name2 = drawer.Promote(ctx.CurrentNodeVisualId!)!;

        name1.Should().Be(name2, "same visualId must always produce the same auto-name");
        asset.BlackboardVariables.Should().HaveCount(1, "second promote is idempotent — no duplicate");
    }

    [Fact]
    public void FqnContext_CurrentNodeVisualId_IsSetByMapper_BTree()
    {
        const string fqn = "Ns.BoolAction";
        var (asset, nodeVisualId) = MakeAssetWithAction(fqn);
        var ctx    = new BTreeFacetFqnContext { CurrentActionFqn = fqn };
        var mapper = new BTreeFacetMapper(asset, ctx);

        ctx.CurrentNodeVisualId.Should().BeNull("not set yet");

        mapper.GetFacet(new BTreeNodeSelection(nodeVisualId));

        ctx.CurrentNodeVisualId.Should().Be(nodeVisualId.ToString(),
            "mapper.GetFacet must write CurrentNodeVisualId to the shared context");
    }
}
