using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Host;

// ---------------------------------------------------------------------------
// Fake IActionSchemaExporter for headless tests
// ---------------------------------------------------------------------------

public sealed class FakeActionSchemaExporter : IActionSchemaExporter
{
    private readonly Dictionary<string, ActionSchemaEntry> _entries = new();

    public IReadOnlyDictionary<string, ActionSchemaEntry> All => _entries;

    public ActionSchemaEntry? Lookup(string fqn) =>
        _entries.TryGetValue(fqn, out var entry) ? entry : null;

    public void Rebuild()
    {
        Changed?.Invoke();
    }

    public event Action? Changed;

    /// <summary>Add or overwrite an entry and fire Changed.</summary>
    public void Add(string fqn, ActionSchemaEntry entry)
    {
        _entries[fqn] = entry;
        Changed?.Invoke();
    }

    /// <summary>Add without firing Changed (for initial seed before catalog construction).</summary>
    public void Seed(string fqn, ActionSchemaEntry entry)
    {
        _entries[fqn] = entry;
    }
}

// ---------------------------------------------------------------------------
// Tests T2–T8
// ---------------------------------------------------------------------------

// Test DTO types for blackboard-type filtering tests (BATCH-13).
public sealed class BrainBlackboardStub { }
public sealed class SomeOtherDto { }

/// <summary>
/// Stand-in for a Blueprint-compiler-generated AiPrimitive class (see AiPrimitiveEmitter.cs):
/// nests a Params struct (the schema entry's DtoType) and a sibling WorkingState struct, exactly
/// like the real generated "{Blueprint}_{Id:X8}_Bp" class.
/// </summary>
public static class FakeGeneratedAiPrimitive_Bp
{
    public struct Params { public int RunsNeeded; }
    public struct WorkingState { public int Ticks; }
}

public sealed class BTreeDynamicCatalogTests
{
    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName        = "test",
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset() =>
        new BehaviorTreeAsset(
            Guid.NewGuid(), "TestTree", "/TestTree.cs", true,
            "BB", "Ctx", EmptyBlob());

    // ---- T2 — action entry ----

    [Fact]
    public void Catalog_ActionEntry_QueryReturnsEncodedKind()
    {
        var fake = new FakeActionSchemaExporter();
        fake.Seed("Ns.Combat.DoThing", new ActionSchemaEntry(
            "Ns.Combat.DoThing", typeof(object), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: false));

        var catalog = new BTreeNodeCatalog(fake);

        var results = catalog.Query(new NodeSearchQuery("DoThing"));
        results.Should().ContainSingle()
            .Which.Kind.Id.Should().Be("bt.leaf.action::Ns.Combat.DoThing");
    }

    [Fact]
    public void Catalog_ActionEntry_HasCorrectDisplayName()
    {
        var fake = new FakeActionSchemaExporter();
        fake.Seed("Ns.Combat.DoThing", new ActionSchemaEntry(
            "Ns.Combat.DoThing", typeof(object), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: false));

        var catalog = new BTreeNodeCatalog(fake);

        var results = catalog.Query(new NodeSearchQuery("DoThing"));
        results.Should().ContainSingle()
            .Which.DisplayName.Should().Be("DoThing");
    }

    // ---- T3 — condition entry ----

    [Fact]
    public void Catalog_ConditionEntry_QueryReturnsEncodedKind()
    {
        var fake = new FakeActionSchemaExporter();
        fake.Seed("Ns.Combat.IsThing", new ActionSchemaEntry(
            "Ns.Combat.IsThing", typeof(object), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: true));

        var catalog = new BTreeNodeCatalog(fake);

        var results = catalog.Query(new NodeSearchQuery("IsThing"));
        results.Should().ContainSingle()
            .Which.Kind.Id.Should().Be("bt.leaf.condition::Ns.Combat.IsThing");
    }

    [Fact]
    public void Catalog_ConditionEntry_IsPureTrue()
    {
        var fake = new FakeActionSchemaExporter();
        fake.Seed("Ns.Combat.IsThing", new ActionSchemaEntry(
            "Ns.Combat.IsThing", typeof(object), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: true));

        var catalog = new BTreeNodeCatalog(fake);

        var results = catalog.Query(new NodeSearchQuery("IsThing"));
        results.Should().ContainSingle()
            .Which.IsPure.Should().BeTrue();
    }

    // ---- T4 — host filter ----

    [Fact]
    public void Catalog_HsmOnlyEntry_NotPresent()
    {
        var fake = new FakeActionSchemaExporter();
        fake.Seed("Ns.Hsm.DoHsm", new ActionSchemaEntry(
            "Ns.Hsm.DoHsm", typeof(object), ActionHosting.Hsm,
            BlackboardAccess.Unknown, null, IsCondition: false));

        var catalog = new BTreeNodeCatalog(fake);

        // The HSM-only entry must not appear in catalog.All.
        catalog.All.Should().NotContain(e => e.Kind.Id.Contains("Ns.Hsm.DoHsm"));
    }

    // ---- T5 — re-query on Changed ----

    [Fact]
    public void Catalog_OnChanged_NewEntryAppears()
    {
        var fake = new FakeActionSchemaExporter();
        var catalog = new BTreeNodeCatalog(fake);

        // Before adding: not present.
        catalog.All.Should().NotContain(e => e.Kind.Id.Contains("Ns.Late.DoLate"));

        // Add and raise Changed.
        fake.Add("Ns.Late.DoLate", new ActionSchemaEntry(
            "Ns.Late.DoLate", typeof(object), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: false));

        // After Changed: must be present.
        catalog.All.Should().Contain(e => e.Kind.Id == "bt.leaf.action::Ns.Late.DoLate");
    }

    // ---- T6 — kinds parse ----

    [Fact]
    public void TryParseLeafActionKind_ActionPrefix_ReturnsTrue()
    {
        var ok = BTreeKinds.TryParseLeafActionKind(
            "bt.leaf.action::Ns.Combat.DoThing", out var fqn, out var isCond);

        ok.Should().BeTrue();
        fqn.Should().Be("Ns.Combat.DoThing");
        isCond.Should().BeFalse();
    }

    [Fact]
    public void TryParseLeafActionKind_ConditionPrefix_ReturnsTrue()
    {
        var ok = BTreeKinds.TryParseLeafActionKind(
            "bt.leaf.condition::Ns.Combat.IsThing", out var fqn, out var isCond);

        ok.Should().BeTrue();
        fqn.Should().Be("Ns.Combat.IsThing");
        isCond.Should().BeTrue();
    }

    [Fact]
    public void TryParseLeafActionKind_GenericAction_ReturnsFalse()
    {
        var ok = BTreeKinds.TryParseLeafActionKind(
            "bt.leaf.action", out _, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void TryParseLeafActionKind_Composite_ReturnsFalse()
    {
        var ok = BTreeKinds.TryParseLeafActionKind(
            "bt.composite.sequence", out _, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void KindIdToNodeType_ActionEncoded_ReturnsAction()
    {
        BTreeKinds.KindIdToNodeType("bt.leaf.action::X")
            .Should().Be(NodeType.Action);
    }

    [Fact]
    public void KindIdToNodeType_ConditionEncoded_ReturnsCondition()
    {
        BTreeKinds.KindIdToNodeType("bt.leaf.condition::X")
            .Should().Be(NodeType.Condition);
    }

    // ---- T7 — placement bakes identity ----

    [Fact]
    public void CommandSink_AddNode_EncodedAction_BakesMethodFqn()
    {
        var asset = MakeAsset();
        var graph = new StubGraphModel();
        var sink  = new BTreeCommandSink(asset, graph);
        var nodeId = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(
            nodeId,
            new NodeKindKey("bt.leaf.action::Ns.Combat.DoThing"),
            Vector2.Zero,
            null));

        var node = asset.FindNode(nodeId.Value);
        node.Should().NotBeNull();
        node!.KernelType.Should().Be(NodeType.Action);
        node.Action.Should().NotBeNull();
        node.Action!.MethodFqn.Should().Be("Ns.Combat.DoThing");
    }

    [Fact]
    public void CommandSink_AddNode_EncodedCondition_BakesMethodFqn()
    {
        var asset = MakeAsset();
        var graph = new StubGraphModel();
        var sink  = new BTreeCommandSink(asset, graph);
        var nodeId = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(
            nodeId,
            new NodeKindKey("bt.leaf.condition::Ns.Combat.IsThing"),
            Vector2.Zero,
            null));

        var node = asset.FindNode(nodeId.Value);
        node.Should().NotBeNull();
        node!.KernelType.Should().Be(NodeType.Condition);
        node.Condition.Should().NotBeNull();
        node.Condition!.MethodFqn.Should().Be("Ns.Combat.IsThing");
    }

    // ---- T8 — generic fallback unchanged ----

    [Fact]
    public void CommandSink_AddNode_GenericAction_NoMethodFqn()
    {
        var asset = MakeAsset();
        var graph = new StubGraphModel();
        var sink  = new BTreeCommandSink(asset, graph);
        var nodeId = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(
            nodeId,
            new NodeKindKey(BTreeKinds.Action),
            Vector2.Zero,
            null));

        var node = asset.FindNode(nodeId.Value);
        node.Should().NotBeNull();
        node!.KernelType.Should().Be(NodeType.Action);
        // Generic action must NOT bake a MethodFqn.
        node.Action.Should().BeNull();
    }

    // ---- E2 — a placed composed blueprint action is labelled by blueprint name ----

    [Fact]
    public void CommandSink_AddNode_AiPrimitive_LabelsNodeWithBlueprintName_NotTickCore()
    {
        var fake = new FakeActionSchemaExporter();
        // {ns}.{Blueprint}_{id:X8}_Bp.TickCore — the generated composed-action FQN shape.
        const string fqn = "Ns.Gen.FakeBlueprint_1A2B3C4D_Bp.TickCore";
        fake.Seed(fqn, new ActionSchemaEntry(
            fqn, typeof(FakeGeneratedAiPrimitive_Bp.Params), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: false, DtoFields: null, IsAiPrimitive: true));

        var asset = MakeAsset();
        var graph = new StubGraphModel();
        var sink  = new BTreeCommandSink(asset, graph, fake);
        var nodeId = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(
            nodeId, new NodeKindKey("bt.leaf.action::" + fqn), Vector2.Zero, null));

        var node = asset.FindNode(nodeId.Value);
        node.Should().NotBeNull();
        node!.DisplayLabel.Should().Be("FakeBlueprint",
            "a placed composed blueprint node is labelled by blueprint name, not the bare TickCore method");
    }

    // ---- E2 — placing an AiPrimitive palette node composes the T31 shape ----

    [Fact]
    public void CommandSink_AddNode_AiPrimitiveAction_ComposesT31Shape()
    {
        const string fqn = "Hrot.AI.Behaviors.Generated.Demo_1A2B3C4D_Bp.TickCore";
        var fake = new FakeActionSchemaExporter();
        fake.Seed(fqn, new ActionSchemaEntry(
            fqn, typeof(FakeGeneratedAiPrimitive_Bp.Params), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: false, DtoFields: null, IsAiPrimitive: true));

        var asset  = MakeAsset();
        var graph  = new StubGraphModel();
        var sink   = new BTreeCommandSink(asset, graph, fake);
        var nodeId = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(
            nodeId,
            new NodeKindKey("bt.leaf.action::" + fqn),
            Vector2.Zero,
            null));

        var node = asset.FindNode(nodeId.Value);
        node.Should().NotBeNull();
        node!.KernelType.Should().Be(NodeType.Action);
        node.Action.Should().NotBeNull();
        node.Action!.MethodFqn.Should().Be(fqn);
        node.Action.DelegateShape.Should().Be(BTreeActionDelegateShape.AiPrimitiveTickCore,
            "T31's Action node uses DelegateShape=AiPrimitiveTickCore");
        node.Action.WorkingStateTypeId.Should().Be(
            typeof(FakeGeneratedAiPrimitive_Bp.WorkingState).FullName,
            "WorkingStateTypeId is derived from the Params type's declaring (generated) class");
        node.Action.ExpressionTargetField.Should().NotBeNullOrEmpty(
            "T31 binds the node to a blackboard variable holding its Params");

        // Slice 1: placing a composed AiPrimitive action now creates TWO variables — the Params
        // (Input) variable and a distinct WorkingState (State) variable bound via
        // WorkingStateTargetField.
        node.Action.WorkingStateTargetField.Should().NotBeNullOrEmpty(
            "Slice 1: the sink must auto-create and bind a WorkingState host variable");

        asset.BlackboardVariables.Should().HaveCount(2,
            "Slice 1: bpParams (Input) and bpWorkingState (State) are both auto-created");

        var varEntry = asset.BlackboardVariables.Should()
            .ContainSingle(v => v.Name == node.Action.ExpressionTargetField).Which;
        varEntry.FieldType.Should().Be(typeof(FakeGeneratedAiPrimitive_Bp.Params));
        varEntry.IsAutoManaged.Should().BeTrue(
            "the auto-created Params variable follows the existing 'Promote to new variable' lifecycle convention");

        var wsEntry = asset.BlackboardVariables.Should()
            .ContainSingle(v => v.Name == node.Action.WorkingStateTargetField).Which;
        wsEntry.FieldType.Should().Be(typeof(FakeGeneratedAiPrimitive_Bp.WorkingState));
        wsEntry.Role.Should().Be(Hrot.AiEditor.Persistence.BlackboardVariableRole.State);
        wsEntry.Scope.Should().Be(Hrot.AiEditor.Persistence.WorkingStateScope.Node);
    }

    [Fact]
    public void CommandSink_AddNode_AiPrimitiveAction_UniqueVariableName_WhenBpParamsTaken()
    {
        const string fqn = "Hrot.AI.Behaviors.Generated.Second_2B3C4D5E_Bp.TickCore";
        var fake = new FakeActionSchemaExporter();
        fake.Seed(fqn, new ActionSchemaEntry(
            fqn, typeof(FakeGeneratedAiPrimitive_Bp.Params), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: false, DtoFields: null, IsAiPrimitive: true));

        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("bpParams", typeof(int), null));
        var graph  = new StubGraphModel();
        var sink   = new BTreeCommandSink(asset, graph, fake);
        var nodeId = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(
            nodeId,
            new NodeKindKey("bt.leaf.action::" + fqn),
            Vector2.Zero,
            null));

        var node = asset.FindNode(nodeId.Value);
        node!.Action!.ExpressionTargetField.Should().Be("bpParams_2",
            "the default name is taken by an unrelated variable, so placement must mint a unique one");
        asset.BlackboardVariables.Should().Contain(v => v.Name == "bpParams_2");
    }

    [Fact]
    public void CommandSink_AddNode_NonAiPrimitiveAction_Unchanged_EvenWithExporterPresent()
    {
        // Regression: a hardcoded (non-Blueprint) action must NOT be composed into the
        // AiPrimitive shape just because an IActionSchemaExporter happens to be wired in.
        const string fqn = "Ns.Combat.DoThing";
        var fake = new FakeActionSchemaExporter();
        fake.Seed(fqn, new ActionSchemaEntry(
            fqn, typeof(object), ActionHosting.BTree, BlackboardAccess.Unknown, null, IsCondition: false));

        var asset  = MakeAsset();
        var graph  = new StubGraphModel();
        var sink   = new BTreeCommandSink(asset, graph, fake);
        var nodeId = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(
            nodeId,
            new NodeKindKey("bt.leaf.action::" + fqn),
            Vector2.Zero,
            null));

        var node = asset.FindNode(nodeId.Value);
        node!.Action!.MethodFqn.Should().Be(fqn);
        node.Action.DelegateShape.Should().Be(BTreeActionDelegateShape.ThreeParamReusable,
            "default enum value; non-AiPrimitive placement must stay unaffected by E2");
        node.Action.WorkingStateTypeId.Should().BeNull();
        node.Action.ExpressionTargetField.Should().BeNull();
        asset.BlackboardVariables.Should().BeEmpty();
    }

    // ---- BATCH-13 — blackboard-type filtering ----

    [Fact]
    public void Catalog_FiltersToBlackboardCompatibleActions()
    {
        var fake = new FakeActionSchemaExporter();
        var matchingFqn    = "Ns.Combat.DoThing";
        var mismatchedFqn  = "Ns.Combat.DoOther";

        fake.Seed(matchingFqn, new ActionSchemaEntry(
            matchingFqn, typeof(BrainBlackboardStub), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: false));
        fake.Seed(mismatchedFqn, new ActionSchemaEntry(
            mismatchedFqn, typeof(SomeOtherDto), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: false));

        var catalog = new BTreeNodeCatalog(fake, typeof(BrainBlackboardStub).FullName);

        // Matching DtoType → offered.
        catalog.All.Should().Contain(e => e.Kind.Id == "bt.leaf.action::" + matchingFqn);
        // Mismatched DtoType → filtered out.
        catalog.All.Should().NotContain(e => e.Kind.Id == "bt.leaf.action::" + mismatchedFqn);
    }

    [Fact]
    public void Catalog_FiltersToBlackboardCompatibleConditions()
    {
        var fake = new FakeActionSchemaExporter();
        var matchingFqn    = "Ns.Combat.IsThing";
        var mismatchedFqn  = "Ns.Combat.IsOther";

        fake.Seed(matchingFqn, new ActionSchemaEntry(
            matchingFqn, typeof(BrainBlackboardStub), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: true));
        fake.Seed(mismatchedFqn, new ActionSchemaEntry(
            mismatchedFqn, typeof(SomeOtherDto), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: true));

        var catalog = new BTreeNodeCatalog(fake, typeof(BrainBlackboardStub).FullName);

        catalog.All.Should().Contain(e => e.Kind.Id == "bt.leaf.condition::" + matchingFqn);
        catalog.All.Should().NotContain(e => e.Kind.Id == "bt.leaf.condition::" + mismatchedFqn);
    }

    [Fact]
    public void Catalog_NullBlackboard_NoDtoFilter()
    {
        var fake = new FakeActionSchemaExporter();
        fake.Seed("Ns.Combat.DoThing", new ActionSchemaEntry(
            "Ns.Combat.DoThing", typeof(BrainBlackboardStub), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: false));
        fake.Seed("Ns.Combat.DoOther", new ActionSchemaEntry(
            "Ns.Combat.DoOther", typeof(SomeOtherDto), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: false));

        // blackboardTypeName: null → no DTO filter (back-compat).
        var catalog = new BTreeNodeCatalog(fake, null);

        catalog.All.Should().Contain(e => e.Kind.Id == "bt.leaf.action::Ns.Combat.DoThing");
        catalog.All.Should().Contain(e => e.Kind.Id == "bt.leaf.action::Ns.Combat.DoOther");
    }

    // ---- I4 — blueprint AiPrimitive actions in the palette ----

    [Fact]
    public void Catalog_AiPrimitiveEntry_AppearsDespiteBlackboardFilter()
    {
        var fake = new FakeActionSchemaExporter();
        // An AiPrimitive's DtoType is its generated Params struct (SomeOtherDto here), NOT the asset
        // blackboard type. It must still be offered because AiPrimitives compose as host-BTree nodes
        // (their Params are bin-packed into the blackboard at a baked offset).
        fake.Seed("Ns.Bp.MoveToAndFire", new ActionSchemaEntry(
            "Ns.Bp.MoveToAndFire", typeof(SomeOtherDto), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: false, DtoFields: null, IsAiPrimitive: true));

        var catalog = new BTreeNodeCatalog(fake, typeof(BrainBlackboardStub).FullName);

        catalog.All.Should().Contain(e => e.Kind.Id == "bt.leaf.action::Ns.Bp.MoveToAndFire");
    }

    [Fact]
    public void Catalog_AiPrimitiveEntry_CategorizedAsBlueprint()
    {
        var fake = new FakeActionSchemaExporter();
        fake.Seed("Ns.Bp.MoveToAndFire", new ActionSchemaEntry(
            "Ns.Bp.MoveToAndFire", typeof(SomeOtherDto), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: false, DtoFields: null, IsAiPrimitive: true));

        var catalog = new BTreeNodeCatalog(fake, typeof(BrainBlackboardStub).FullName);

        var entry = catalog.All.Single(e => e.Kind.Id == "bt.leaf.action::Ns.Bp.MoveToAndFire");
        entry.CategoryPath.Should().Be("Blueprint");
    }

    [Fact]
    public void Catalog_AiPrimitiveActionEntry_HasBlueprintActionIconKey()
    {
        // Regression: Blueprint palette entries must carry a non-null icon key so the
        // palette can render an icon instead of a blank cell (see SilkIconProvider's
        // "bt/blueprint_action" mapping).
        var fake = new FakeActionSchemaExporter();
        fake.Seed("Ns.Bp.MoveToAndFire", new ActionSchemaEntry(
            "Ns.Bp.MoveToAndFire", typeof(SomeOtherDto), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: false, DtoFields: null, IsAiPrimitive: true));

        var catalog = new BTreeNodeCatalog(fake, typeof(BrainBlackboardStub).FullName);

        var entry = catalog.All.Single(e => e.Kind.Id == "bt.leaf.action::Ns.Bp.MoveToAndFire");
        entry.IconKey.Should().Be("bt/blueprint_action");
    }

    [Fact]
    public void Catalog_AiPrimitiveConditionEntry_HasBlueprintConditionIconKey()
    {
        var fake = new FakeActionSchemaExporter();
        fake.Seed("Ns.Bp.IsReady", new ActionSchemaEntry(
            "Ns.Bp.IsReady", typeof(SomeOtherDto), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: true, DtoFields: null, IsAiPrimitive: true));

        var catalog = new BTreeNodeCatalog(fake, typeof(BrainBlackboardStub).FullName);

        var entry = catalog.All.Single(e => e.Kind.Id == "bt.leaf.condition::Ns.Bp.IsReady");
        entry.IconKey.Should().Be("bt/blueprint_condition");
    }

    [Fact]
    public void Categories_BlueprintCategory_HasNonNullIconKey()
    {
        // Regression: the Blueprint category header must also carry an icon key
        // (see SilkIconProvider's "bt/blueprint" mapping).
        var catalog = new BTreeNodeCatalog();

        var blueprintCategory = catalog.Categories.Single(c => c.Path == "Blueprint");
        blueprintCategory.IconKey.Should().Be("bt/blueprint");
    }

    [Fact]
    public void Catalog_AiPrimitiveEntry_DisplayNameIsBlueprintName_NotTickCore()
    {
        // Generated TickCore FQN pattern: {ns}.{Blueprint}_{id:X8}_Bp.TickCore.
        const string fqn = "Hrot.AI.Behaviors.Generated.LocomotionMoveToDemo_1A2B3C4D_Bp.TickCore";
        var fake = new FakeActionSchemaExporter();
        fake.Seed(fqn, new ActionSchemaEntry(
            fqn, typeof(SomeOtherDto), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: false, DtoFields: null, IsAiPrimitive: true));

        var catalog = new BTreeNodeCatalog(fake, typeof(BrainBlackboardStub).FullName);

        var entry = catalog.All.Single(e => e.Kind.Id == "bt.leaf.action::" + fqn);
        entry.DisplayName.Should().Be("LocomotionMoveToDemo");
    }

    [Fact]
    public void Catalog_MismatchedHardcodedEntry_StillFilteredOut()
    {
        // Regression: a mismatched-DtoType HARD-CODED action (IsAiPrimitive == false) must remain
        // filtered by the blackboard-type gate — the exemption applies only to AiPrimitives.
        var fake = new FakeActionSchemaExporter();
        fake.Seed("Ns.Combat.DoOther", new ActionSchemaEntry(
            "Ns.Combat.DoOther", typeof(SomeOtherDto), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: false));

        var catalog = new BTreeNodeCatalog(fake, typeof(BrainBlackboardStub).FullName);

        catalog.All.Should().NotContain(e => e.Kind.Id == "bt.leaf.action::Ns.Combat.DoOther");
    }

    [Fact]
    public void Catalog_StaticEntries_AlwaysPresent()
    {
        var fake = new FakeActionSchemaExporter();
        // Seed only an incompatible action to ensure dynamic entries are filtered out.
        fake.Seed("Ns.Combat.DoOther", new ActionSchemaEntry(
            "Ns.Combat.DoOther", typeof(SomeOtherDto), ActionHosting.BTree,
            BlackboardAccess.Unknown, null, IsCondition: false));

        var catalog = new BTreeNodeCatalog(fake, typeof(BrainBlackboardStub).FullName);

        // Dynamic mismatched entry must NOT appear.
        catalog.All.Should().NotContain(e => e.Kind.Id == "bt.leaf.action::Ns.Combat.DoOther");

        // Static entries must always be present regardless of the filter.
        catalog.All.Should().Contain(e => e.Kind.Id == BTreeKinds.Sequence);
        catalog.All.Should().Contain(e => e.Kind.Id == BTreeKinds.Selector);
        catalog.All.Should().Contain(e => e.Kind.Id == BTreeKinds.Parallel);
        catalog.All.Should().Contain(e => e.Kind.Id == BTreeKinds.Root);
        catalog.All.Should().Contain(e => e.Kind.Id == BTreeKinds.Action);
        catalog.All.Should().Contain(e => e.Kind.Id == BTreeKinds.Condition);
        catalog.All.Should().Contain(e => e.Kind.Id == BTreeKinds.Wait);
        catalog.All.Should().Contain(e => e.Kind.Id == BTreeKinds.Subtree);
        catalog.All.Should().Contain(e => e.Kind.Id == BTreeKinds.Inverter);
        catalog.All.Should().Contain(e => e.Kind.Id == BTreeKinds.Repeater);
        catalog.All.Should().Contain(e => e.Kind.Id == BTreeKinds.Cooldown);
        catalog.All.Should().Contain(e => e.Kind.Id == BTreeKinds.ForceSuccess);
        catalog.All.Should().Contain(e => e.Kind.Id == BTreeKinds.ForceFailure);
        catalog.All.Should().Contain(e => e.Kind.Id == BTreeKinds.UntilSuccess);
        catalog.All.Should().Contain(e => e.Kind.Id == BTreeKinds.UntilFailure);
        catalog.All.Should().Contain(e => e.Kind.Id == BTreeKinds.ObserverSelector);
    }
}

// ---------------------------------------------------------------------------
// Minimal IGraphModel stub for command-sink tests
// ---------------------------------------------------------------------------

public sealed class StubGraphModel : IGraphModel
{
    public GraphId Id => GraphId.NewId();
    public string DisplayName => "stub";
    public GraphKindDescriptor Kind => new("stub", "stub", false, false);
    public IReadOnlyCollection<INodeModel>    Nodes    => Array.Empty<INodeModel>();
    public IReadOnlyCollection<ILinkModel>    Links    => Array.Empty<ILinkModel>();
    public IReadOnlyCollection<ICommentModel> Comments => Array.Empty<ICommentModel>();

#pragma warning disable CS0067
    public event Action<GraphChangeNotification>? Changed;
#pragma warning restore CS0067

    public INodeModel?  FindNode(NodeId id) => null;
    public IPinModel?   FindPin(PinId id)   => null;
    public ILinkModel?  FindLink(LinkId id) => null;
}
