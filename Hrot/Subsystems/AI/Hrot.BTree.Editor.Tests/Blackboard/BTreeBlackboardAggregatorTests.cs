using System;
using System.Collections.Generic;
using System.Linq;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Blackboard;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Blackboard;

public sealed class BTreeBlackboardAggregatorTests
{
    // ---- dto stubs ----

    private struct SomeDto { }
    private struct OtherDto { }

    // ---- schema stub ----

    private sealed class StubSchemaExporter : IActionSchemaExporter
    {
        private readonly Dictionary<string, ActionSchemaEntry> _entries = new();

        public void Add(string fqn, Type dtoType) =>
            _entries[fqn] = new ActionSchemaEntry(fqn, dtoType, ActionHosting.BTree, BlackboardAccess.ReadWrite, null);

        public IReadOnlyDictionary<string, ActionSchemaEntry> All => _entries;
        public ActionSchemaEntry? Lookup(string fqn) => _entries.TryGetValue(fqn, out var e) ? e : null;
        public void Rebuild() { }
        public event Action? Changed { add { } remove { } }
    }

    // ---- catalog stub ----

    private sealed class StubCatalog : IAssetCatalog
    {
        private readonly Dictionary<Guid, IEditableAsset> _assets = new();

        public void Register(IEditableAsset asset) => _assets[asset.AssetId] = asset;

        public IReadOnlyList<IEditableAsset> All => _assets.Values.ToList();
        public IEditableAsset? FindByAssetId(Guid id) => _assets.TryGetValue(id, out var a) ? a : null;
        public IEditableAsset? FindByName(string name) => _assets.Values.FirstOrDefault(a => a.Name == name);
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid id) => Array.Empty<IEditableAsset>();
        public event Action<AssetKind>? Changed { add { } remove { } }
    }

    // ---- helpers ----

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

    private static BehaviorTreeAsset MakeAsset(string name = "TestTree") =>
        new BehaviorTreeAsset(
            Guid.NewGuid(), name,
            "/trees/" + name + ".cs",
            true, "MyBlackboard", "MyContext", EmptyBlob());

    private static void AddNodes(BehaviorTreeAsset asset, List<BTreeEditorNode> nodes) =>
        asset.ReplaceAll(nodes, new List<BTreeEditorPill>(), EmptyBlob());

    // Bootstrap: create service with empty strategy list, then register the
    // strategy after construction to break the circular dependency.
    private static (BlackboardAggregatorService service, BTreeBlackboardAggregatorStrategy strategy)
        MakeServiceAndStrategy(IActionSchemaExporter schema, IAssetCatalog catalog)
    {
        var service  = new BlackboardAggregatorService(
            Enumerable.Empty<IBlackboardAggregatorStrategy>(), schema, catalog);
        var strategy = new BTreeBlackboardAggregatorStrategy(service);
        service.Register(strategy);
        return (service, strategy);
    }

    // ---- tests ----

    [Fact]
    public void Aggregate_empty_tree_returns_empty_result()
    {
        var schema  = new StubSchemaExporter();
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);
        var asset = MakeAsset();

        var result = service.Aggregate(asset);

        result.Requirements.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_action_node_emits_requirement_for_known_fqn()
    {
        const string fqn = "Combat.Actions.AimAndFire";
        var schema  = new StubSchemaExporter();
        schema.Add(fqn, typeof(SomeDto));
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        var asset = MakeAsset();
        AddNodes(asset, new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId     = Guid.NewGuid(),
                KernelType   = NodeType.Action,
                DisplayLabel = "AimAndFire",
                Action       = new BTreeActionPayload { MethodFqn = fqn },
            },
        });

        var result = service.Aggregate(asset);

        result.Requirements.Should().HaveCount(1);
        result.Requirements[0].DtoType.Should().Be(typeof(SomeDto));
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_condition_node_emits_requirement_for_known_fqn()
    {
        const string fqn = "Combat.Conditions.IsEnemyVisible";
        var schema  = new StubSchemaExporter();
        schema.Add(fqn, typeof(OtherDto));
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        var asset = MakeAsset();
        AddNodes(asset, new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId     = Guid.NewGuid(),
                KernelType   = NodeType.Condition,
                DisplayLabel = "IsEnemyVisible",
                Condition    = new BTreeConditionPayload { MethodFqn = fqn },
            },
        });

        var result = service.Aggregate(asset);

        result.Requirements.Should().HaveCount(1);
        result.Requirements[0].DtoType.Should().Be(typeof(OtherDto));
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_unknown_fqn_emits_schema_not_found_warning_not_exception()
    {
        const string fqn = "Unknown.Namespace.Method";
        var schema  = new StubSchemaExporter();   // fqn not registered
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        var asset = MakeAsset();
        AddNodes(asset, new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId   = Guid.NewGuid(),
                KernelType = NodeType.Action,
                Action     = new BTreeActionPayload { MethodFqn = fqn },
            },
        });

        var result = service.Aggregate(asset);

        result.Requirements.Should().BeEmpty();
        result.Warnings.Should().HaveCount(1);
        result.Warnings[0].Kind.Should().Be(AggregationWarningKind.SchemaEntryNotFound);
    }

    [Fact]
    public void Aggregate_subtree_node_unresolved_emits_warning_and_skips()
    {
        var schema  = new StubSchemaExporter();
        var catalog = new StubCatalog();   // returns null for any id
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        var missingId = Guid.NewGuid();
        var asset = MakeAsset();
        AddNodes(asset, new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId   = Guid.NewGuid(),
                KernelType = NodeType.Subtree,
                Subtree    = new BTreeSubtreePayload
                {
                    SubtreeAssetId = missingId,
                    SubtreeName    = "MissingTree",
                    IsResolved     = false,
                },
            },
        });

        var result = service.Aggregate(asset);

        result.Requirements.Should().BeEmpty();
        result.Warnings.Should().HaveCount(1);
        result.Warnings[0].Kind.Should().Be(AggregationWarningKind.UnresolvedSubtree);
    }

    [Fact]
    public void Aggregate_subtree_node_resolved_recurses_and_collects_child_requirements()
    {
        const string fqn = "Child.Actions.DoWork";
        var schema  = new StubSchemaExporter();
        schema.Add(fqn, typeof(SomeDto));
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        // Child tree has one action node
        var childAsset = MakeAsset("ChildTree");
        catalog.Register(childAsset);
        AddNodes(childAsset, new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId   = Guid.NewGuid(),
                KernelType = NodeType.Action,
                Action     = new BTreeActionPayload { MethodFqn = fqn },
            },
        });

        // Parent tree references child via subtree node
        var parentAsset = MakeAsset("ParentTree");
        AddNodes(parentAsset, new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId   = Guid.NewGuid(),
                KernelType = NodeType.Subtree,
                Subtree    = new BTreeSubtreePayload
                {
                    SubtreeAssetId = childAsset.AssetId,
                    SubtreeName    = "ChildTree",
                    IsResolved     = true,
                },
            },
        });

        var result = service.Aggregate(parentAsset);

        result.Requirements.Should().HaveCount(1);
        result.Requirements[0].DtoType.Should().Be(typeof(SomeDto));
        result.Warnings.Should().BeEmpty();
    }

    // ---- Fix B: locally-bound nodes must NOT emit requirements ----

    [Fact]
    public void Aggregate_locally_bound_action_node_emits_no_requirement()
    {
        // A node whose ExpressionTargetField is set is locally bound — its DTO is
        // stored in the auto-managed blackboard variable, NOT as an unbound requirement.
        const string fqn = "Combat.Actions.AimAndFire";
        var schema  = new StubSchemaExporter();
        schema.Add(fqn, typeof(SomeDto));
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        var asset = MakeAsset();
        AddNodes(asset, new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId     = Guid.NewGuid(),
                KernelType   = NodeType.Action,
                DisplayLabel = "AimAndFire",
                Action       = new BTreeActionPayload
                {
                    MethodFqn           = fqn,
                    ExpressionTargetField = "counter",   // locally bound
                },
            },
        });

        var result = service.Aggregate(asset);

        result.Requirements.Should().BeEmpty("locally-bound node must not appear as an unbound requirement");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_locally_bound_condition_node_emits_no_requirement()
    {
        const string fqn = "Combat.Conditions.IsEnemyVisible";
        var schema  = new StubSchemaExporter();
        schema.Add(fqn, typeof(OtherDto));
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        var asset = MakeAsset();
        AddNodes(asset, new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId     = Guid.NewGuid(),
                KernelType   = NodeType.Condition,
                DisplayLabel = "IsEnemyVisible",
                Condition    = new BTreeConditionPayload
                {
                    MethodFqn             = fqn,
                    ExpressionTargetField = "accum",   // locally bound
                },
            },
        });

        var result = service.Aggregate(asset);

        result.Requirements.Should().BeEmpty("locally-bound condition node must not appear as an unbound requirement");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_T10_mirror_all_locally_bound_yields_zero_requirements()
    {
        // Mirror of the T10 demo scenario:
        // - DemoCounterParams action (bound to "counter") + condition (bound to "counter")
        // - DemoAccumParams action (bound to "accum")
        // Expected: 0 requirements, all nodes are locally bound.
        const string actionFqn1    = "Demo.Actions.IncrementCounter";
        const string conditionFqn  = "Demo.Conditions.CheckCounter";
        const string actionFqn2    = "Demo.Actions.Accumulate";

        var schema = new StubSchemaExporter();
        schema.Add(actionFqn1,   typeof(SomeDto));
        schema.Add(conditionFqn, typeof(SomeDto));
        schema.Add(actionFqn2,   typeof(OtherDto));
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        var asset = MakeAsset("DemoTree");
        AddNodes(asset, new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId     = Guid.NewGuid(),
                KernelType   = NodeType.Action,
                DisplayLabel = "IncrementCounter",
                Action       = new BTreeActionPayload { MethodFqn = actionFqn1, ExpressionTargetField = "counter" },
            },
            new BTreeEditorNode
            {
                VisualId     = Guid.NewGuid(),
                KernelType   = NodeType.Condition,
                DisplayLabel = "CheckCounter",
                Condition    = new BTreeConditionPayload { MethodFqn = conditionFqn, ExpressionTargetField = "counter" },
            },
            new BTreeEditorNode
            {
                VisualId     = Guid.NewGuid(),
                KernelType   = NodeType.Action,
                DisplayLabel = "Accumulate",
                Action       = new BTreeActionPayload { MethodFqn = actionFqn2, ExpressionTargetField = "accum" },
            },
        });

        var result = service.Aggregate(asset);

        result.Requirements.Should().BeEmpty("all nodes are locally bound — zero unbound requirements expected");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_unbound_action_node_still_emits_requirement()
    {
        // Verify that an action node with an EMPTY ExpressionTargetField still surfaces
        // as an unbound requirement (regression guard for the fix).
        const string fqn = "Combat.Actions.AimAndFire";
        var schema  = new StubSchemaExporter();
        schema.Add(fqn, typeof(SomeDto));
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        var asset = MakeAsset();
        AddNodes(asset, new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId     = Guid.NewGuid(),
                KernelType   = NodeType.Action,
                DisplayLabel = "AimAndFire",
                Action       = new BTreeActionPayload
                {
                    MethodFqn             = fqn,
                    ExpressionTargetField = null,   // unbound — must still produce a requirement
                },
            },
        });

        var result = service.Aggregate(asset);

        result.Requirements.Should().HaveCount(1);
        result.Requirements[0].DtoType.Should().Be(typeof(SomeDto));
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_locally_bound_parent_subtree_with_unbound_child_still_surfaces_child_requirements()
    {
        // Locally binding the parent node must NOT suppress child subtree requirements.
        const string parentFqn = "Parent.Actions.Bound";
        const string childFqn  = "Child.Actions.Unbound";

        var schema = new StubSchemaExporter();
        schema.Add(parentFqn, typeof(SomeDto));
        schema.Add(childFqn,  typeof(OtherDto));
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        // Child tree has an unbound action node
        var childAsset = MakeAsset("ChildTree");
        catalog.Register(childAsset);
        AddNodes(childAsset, new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId   = Guid.NewGuid(),
                KernelType = NodeType.Action,
                Action     = new BTreeActionPayload { MethodFqn = childFqn },   // unbound
            },
        });

        // Parent tree: one locally-bound action + one subtree node
        var parentAsset = MakeAsset("ParentTree");
        AddNodes(parentAsset, new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId   = Guid.NewGuid(),
                KernelType = NodeType.Action,
                Action     = new BTreeActionPayload { MethodFqn = parentFqn, ExpressionTargetField = "parentVar" },
            },
            new BTreeEditorNode
            {
                VisualId   = Guid.NewGuid(),
                KernelType = NodeType.Subtree,
                Subtree    = new BTreeSubtreePayload
                {
                    SubtreeAssetId = childAsset.AssetId,
                    SubtreeName    = "ChildTree",
                    IsResolved     = true,
                },
            },
        });

        var result = service.Aggregate(parentAsset);

        result.Requirements.Should().HaveCount(1, "only the unbound child requirement surfaces");
        result.Requirements[0].DtoType.Should().Be(typeof(OtherDto));
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_cycle_stops_recursion_and_emits_cycle_warning()
    {
        var schema  = new StubSchemaExporter();
        var catalog = new StubCatalog();
        var (service, _) = MakeServiceAndStrategy(schema, catalog);

        // Asset A references asset B; asset B references asset A back.
        var assetA = MakeAsset("AssetA");
        var assetB = MakeAsset("AssetB");
        catalog.Register(assetA);
        catalog.Register(assetB);

        AddNodes(assetA, new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId   = Guid.NewGuid(),
                KernelType = NodeType.Subtree,
                Subtree    = new BTreeSubtreePayload
                {
                    SubtreeAssetId = assetB.AssetId,
                    SubtreeName    = "AssetB",
                    IsResolved     = true,
                },
            },
        });
        AddNodes(assetB, new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId   = Guid.NewGuid(),
                KernelType = NodeType.Subtree,
                Subtree    = new BTreeSubtreePayload
                {
                    SubtreeAssetId = assetA.AssetId,
                    SubtreeName    = "AssetA",
                    IsResolved     = true,
                },
            },
        });

        var result = service.Aggregate(assetA);

        result.Warnings.Should().HaveCount(1);
        result.Warnings[0].Kind.Should().Be(AggregationWarningKind.Cycle);
        result.Requirements.Should().BeEmpty();
    }
}
