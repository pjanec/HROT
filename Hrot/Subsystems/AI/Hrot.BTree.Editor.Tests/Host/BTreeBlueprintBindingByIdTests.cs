using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Inspector;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Validation;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Inspector.ActionBinding;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Selection;
using NodeEditor.Core.Commands;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Host;

/// <summary>
/// ⭐⭐⭐ <c>CE-417</c> B-1 (slice 4c) — a BTree blueprint binding is named by the blueprint's ASSET ID, in the editor as on
/// disk. 📄 <c>docs/blueprints/DESIGN_Behavior_Action_Binding.md</c> §5.5.
/// </summary>
public sealed class BTreeBlueprintBindingByIdTests
{
    private const string ClassName = "Demo_1A2B3C4D_Bp";
    private const string Fqn       = "Hrot.AI.Behaviors.Generated." + ClassName + ".TickCore";

    private sealed class FakeBlueprint : IEditableAsset, IComposedBlueprintIdentity
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "Demo";
        public AssetKind Kind { get; init; } = AssetKind.Blueprint;
        public string SourceFilePath => "/demo.bp.json";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
        public string? GeneratedClassName { get; init; } = ClassName;
        public event Action? Changed { add { } remove { } }
    }

    private sealed class Catalog : IAssetCatalog
    {
        private readonly List<IEditableAsset> _a;
        public Catalog(params IEditableAsset[] a) => _a = a.ToList();
        public IReadOnlyList<IEditableAsset> All => _a;
        public IEditableAsset? FindByAssetId(Guid id) => _a.FirstOrDefault(x => x.AssetId == id);
        public IEditableAsset? FindByName(string n) => _a.FirstOrDefault(x => x.Name == n);
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid id) => Array.Empty<IEditableAsset>();
        public event Action<AssetKind>? Changed { add { } remove { } }
    }

    private static BehaviorTreeBlob EmptyBlob() => new()
    {
        TreeName = "T", Nodes = Array.Empty<NodeDefinition>(), MethodNames = Array.Empty<string>(),
        FloatParams = Array.Empty<float>(), IntParams = Array.Empty<int>(), SubtreeAssetIds = Array.Empty<string>(),
    };

    private static BehaviorTreeAsset AssetWith(BehaviorActionBinding binding, BTreeActionDelegateShape shape, out BTreeEditorNode node)
    {
        var asset = new BehaviorTreeAsset(Guid.NewGuid(), "T", "/T.cs", true, "BB", "Ctx", EmptyBlob());
        var root  = new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = NodeType.Root };
        node = new BTreeEditorNode
        {
            VisualId = Guid.NewGuid(), KernelType = NodeType.Action, DisplayLabel = "A",
            Action = binding, DelegateShape = shape,
        };
        root.ChildVisualIds.Add(node.VisualId);
        asset.ReplaceAll(new List<BTreeEditorNode> { root, node }, new List<BTreeEditorPill>(), EmptyBlob());
        return asset;
    }

    // ── the heal on open ──────────────────────────────────────────────────────

    [Fact]
    public void Open_HealsALegacyGeneratedFqn_ToTheBlueprintsIdAndName()
    {
        var bp    = new FakeBlueprint();
        var asset = AssetWith(new BehaviorActionBinding { MethodFqn = Fqn }, BTreeActionDelegateShape.AiPrimitiveTickCore, out var node);

        BTreeBlueprintBindingResolver.Resolve(asset, new Catalog(bp)).Should().Be(1, "a heal is an edit the caller must save");

        node.Action!.BlueprintAssetId.Should().Be(bp.AssetId);
        node.Action.BlueprintName.Should().Be("Demo");
        node.Action.MethodFqn.Should().BeNull("B-1: the TickCore is derived, never persisted");
    }

    [Fact]
    public void Open_LeavesAHandWrittenAiPrimitiveMethodAlone()
    {
        var asset = AssetWith(new BehaviorActionBinding { MethodFqn = "Hrot.AI.Behaviors.Brains.DemoAiPrimitiveNodes.TickCore" },
                              BTreeActionDelegateShape.AiPrimitiveTickCore, out var node);

        BTreeBlueprintBindingResolver.Resolve(asset, new Catalog(new FakeBlueprint())).Should().Be(0);
        node.Action!.MethodFqn.Should().Be("Hrot.AI.Behaviors.Brains.DemoAiPrimitiveNodes.TickCore");
    }

    /// <summary>
    /// ⭐⭐ Id FIRST. Two blueprints share the name "Demo"; the binding's id names the SECOND. A name-first rule (the
    /// subtree rule) would rebind it to the first. ⚠ And a renamed blueprint heals the stored name from the id.
    /// </summary>
    [Fact]
    public void Open_ResolvesById_EvenWhenTwoBlueprintsShareTheName_AndHealsARename()
    {
        var first  = new FakeBlueprint { Name = "Demo" };
        var second = new FakeBlueprint { Name = "Demo Renamed" };
        var asset  = AssetWith(new BehaviorActionBinding { BlueprintAssetId = second.AssetId, BlueprintName = "Demo" },
                               BTreeActionDelegateShape.AiPrimitiveTickCore, out var node);

        BTreeBlueprintBindingResolver.Resolve(asset, new Catalog(first, second)).Should().Be(1);

        node.Action!.BlueprintAssetId.Should().Be(second.AssetId);
        node.Action.BlueprintName.Should().Be("Demo Renamed");
    }

    [Fact]
    public void Open_NeverErasesADanglingId()
    {
        var gone  = Guid.NewGuid();
        var asset = AssetWith(new BehaviorActionBinding { BlueprintAssetId = gone, BlueprintName = "Demo" },
                              BTreeActionDelegateShape.AiPrimitiveTickCore, out var node);

        BTreeBlueprintBindingResolver.Resolve(asset, new Catalog()).Should().Be(0);
        node.Action!.BlueprintAssetId.Should().Be(gone);
        node.Action.BlueprintName.Should().Be("Demo");
    }

    // ── the readers ───────────────────────────────────────────────────────────

    [Fact]
    public void TheEffectiveMethod_IsDerived_FromTheCatalogueByIdElseTheName()
    {
        var bp      = new FakeBlueprint();
        var binding = new BehaviorActionBinding { BlueprintAssetId = bp.AssetId, BlueprintName = "Stale" };

        ComposedBlueprintResolver.EffectiveMethodFqn(binding, new Catalog(bp)).Should().Be(Fqn);
        ComposedBlueprintResolver.EffectiveMethodFqn(new BehaviorActionBinding { MethodFqn = "Ns.X" }).Should().Be("Ns.X");
        ComposedBlueprintResolver.EffectiveMethodFqn(new BehaviorActionBinding()).Should().BeNull();
    }

    [Fact]
    public void Validator_AResolvableId_IsNotDangling_AndADeletedOneIs()
    {
        var bp    = new FakeBlueprint();
        var asset = AssetWith(new BehaviorActionBinding { BlueprintAssetId = bp.AssetId, BlueprintName = "Demo" },
                              BTreeActionDelegateShape.AiPrimitiveTickCore, out var node);

        new BTreeValidator().Validate(asset, new Catalog(bp))
            .Should().NotContain(d => d.Code == BTreeDiagnosticCode.DanglingReferenceAfterReload);
        new BTreeValidator().Validate(asset, new Catalog())
            .Should().ContainSingle(d => d.Code == BTreeDiagnosticCode.DanglingReferenceAfterReload && d.VisualId == node.VisualId);
        new BTreeValidator().Validate(asset, new Catalog(bp))
            .Should().NotContain(d => d.Code == BTreeDiagnosticCode.UnboundActionMethod, "a blueprint IS a binding");
    }

    // ── the two authoring gestures ────────────────────────────────────────────

    [Fact]
    public void PaletteDrop_OfAGeneratedBlueprint_PersistsItsIdAndName()
    {
        var bp   = new FakeBlueprint();
        var fake = new FakeActionSchemaExporter();
        fake.Seed(Fqn, new ActionSchemaEntry(Fqn, typeof(FakeGeneratedAiPrimitive_Bp.Params), ActionHosting.BTree,
                                             BlackboardAccess.Unknown, IsCondition: false, DtoFields: null, IsAiPrimitive: true));
        var asset  = new BehaviorTreeAsset(Guid.NewGuid(), "T", "/T.cs", true, "BB", "Ctx", EmptyBlob());
        var sink   = new BTreeCommandSink(asset, new StubGraphModel(), fake, new Catalog(bp));
        var nodeId = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey("bt.leaf.action::" + Fqn), Vector2.Zero, null));

        var node = asset.FindNode(nodeId.Value)!;
        node.DelegateShape.Should().Be(BTreeActionDelegateShape.AiPrimitiveTickCore);
        node.Action!.BlueprintAssetId.Should().Be(bp.AssetId);
        node.Action.BlueprintName.Should().Be("Demo");
        node.Action.MethodFqn.Should().BeNull();
        node.Action.ExpressionTargetField.Should().NotBeNullOrEmpty("the compose step still binds the Params variable");
    }

    /// <summary>
    /// ⭐ §5.5 ② — picking a blueprint in the inspector composes params AND working state (the palette rule) and makes the
    /// node a TickCore call; clearing it drops both editor-owned variables and returns the node to the plain shape.
    /// </summary>
    [Fact]
    public void InspectorPick_ComposesParamsAndWorkingState_AndClearingRestoresThePlainShape()
    {
        const string tick = "Hrot.Generated.Fake_1A2B3C4D_Bp.TickCore";
        var fake = new FakeActionSchemaExporter();
        fake.Seed(tick, new ActionSchemaEntry(tick, typeof(FakeGeneratedAiPrimitive_Bp.Params), ActionHosting.BTree,
                                              BlackboardAccess.Unknown, IsCondition: false, DtoFields: null, IsAiPrimitive: true));
        var bp    = new FakeBlueprint { Name = "Fake", GeneratedClassName = "Fake_1A2B3C4D_Bp" };
        var asset = AssetWith(new BehaviorActionBinding { MethodFqn = "Ns.Plain" }, BTreeActionDelegateShape.Plain, out var node);
        var mapper = new BTreeFacetMapper(asset, new Catalog(bp), fake);
        var sel    = new BTreeNodeSelection(node.VisualId);

        var facet = (BTreeActionFacet)mapper.GetFacet(sel)!;
        ActionBindingDrawer.PickBlueprint(ref facet.Action, "Fake");
        mapper.ApplyFacet(sel, facet);

        node.DelegateShape.Should().Be(BTreeActionDelegateShape.AiPrimitiveTickCore);
        node.Action!.BlueprintAssetId.Should().Be(bp.AssetId);
        node.Action.MethodFqn.Should().BeNull();
        asset.BlackboardVariables.Single(v => v.Name == node.Action.ExpressionTargetField).FieldType
            .Should().Be(typeof(FakeGeneratedAiPrimitive_Bp.Params));
        asset.BlackboardVariables.Single(v => v.Name == node.Action.WorkingStateTargetField).FieldType
            .Should().Be(typeof(FakeGeneratedAiPrimitive_Bp.WorkingState));
        node.Action.WorkingStateTypeId.Should().Be(typeof(FakeGeneratedAiPrimitive_Bp.WorkingState).FullName);

        facet = (BTreeActionFacet)mapper.GetFacet(sel)!;
        ActionBindingDrawer.PickBlueprint(ref facet.Action, null);
        mapper.ApplyFacet(sel, facet);

        node.DelegateShape.Should().Be(BTreeActionDelegateShape.Plain);
        asset.BlackboardVariables.Should().BeEmpty("both composed variables were editor-owned");
        node.Action!.WorkingStateTargetField.Should().BeNull();
        node.Action.WorkingStateTypeId.Should().BeNull();
    }
}
