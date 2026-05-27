using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.Editor.AiShared.Tests.Blackboard;

public sealed class BlackboardAggregatorServiceTests
{
    // ---- stubs ----

    private sealed class StubAsset : IEditableAsset
    {
        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name { get; } = "Stub";
        public AssetKind Kind => AssetKind.BTree;
        public string SourceFilePath => "";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
        public event Action? Changed { add { } remove { } }
    }

    private sealed class StubSchemaExporter : IActionSchemaExporter
    {
        private readonly Dictionary<string, ActionSchemaEntry> _entries = new();
        public IReadOnlyDictionary<string, ActionSchemaEntry> All => _entries;
        public ActionSchemaEntry? Lookup(string fqn) => _entries.TryGetValue(fqn, out var e) ? e : null;
        public void Rebuild() { }
        public event Action? Changed { add { } remove { } }
    }

    private sealed class StubCatalog : IAssetCatalog
    {
        public IReadOnlyList<IEditableAsset> All => Array.Empty<IEditableAsset>();
        public IEditableAsset? FindByAssetId(Guid id) => null;
        public IEditableAsset? FindByName(string name) => null;
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid id) => Array.Empty<IEditableAsset>();
        public event Action? Changed { add { } remove { } }
    }

    private sealed class CapturingStrategy : IBlackboardAggregatorStrategy
    {
        public bool AggregateCalled;
        private readonly AggregationResult _result;

        public CapturingStrategy(AggregationResult result) => _result = result;

        public bool CanHandle(IEditableAsset asset) => true;

        public AggregationResult Aggregate(
            IEditableAsset asset,
            IActionSchemaExporter schema,
            IAssetCatalog catalog,
            HashSet<Guid> visited)
        {
            AggregateCalled = true;
            return _result;
        }
    }

    private sealed class NeverHandleStrategy : IBlackboardAggregatorStrategy
    {
        public bool CanHandle(IEditableAsset asset) => false;

        public AggregationResult Aggregate(
            IEditableAsset asset,
            IActionSchemaExporter schema,
            IAssetCatalog catalog,
            HashSet<Guid> visited)
            => throw new InvalidOperationException("Should not be called");
    }

    private static BlackboardAggregatorService MakeService(
        params IBlackboardAggregatorStrategy[] strategies)
        => new BlackboardAggregatorService(strategies, new StubSchemaExporter(), new StubCatalog());

    // ---- tests ----

    [Fact]
    public void CanHandle_false_returns_empty_result()
    {
        var service = MakeService(new NeverHandleStrategy());

        var result = service.Aggregate(new StubAsset());

        Assert.Empty(result.Requirements);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Aggregate_dispatches_to_matching_strategy()
    {
        var req = new DtoRequirement(typeof(int), "path", Guid.NewGuid(), Guid.NewGuid());
        var expected = new AggregationResult(new[] { req }, Array.Empty<AggregationWarning>());
        var strategy = new CapturingStrategy(expected);
        var service = MakeService(strategy);

        var result = service.Aggregate(new StubAsset());

        Assert.True(strategy.AggregateCalled);
        Assert.Single(result.Requirements);
        Assert.Equal(req, result.Requirements[0]);
    }

    [Fact]
    public void AggregationResult_Merge_concatenates_requirements_and_warnings()
    {
        var req1 = new DtoRequirement(typeof(int), "path1", Guid.NewGuid(), Guid.NewGuid());
        var req2 = new DtoRequirement(typeof(float), "path2", Guid.NewGuid(), Guid.NewGuid());
        var warn1 = new AggregationWarning(AggregationWarningKind.Cycle, "cycle");
        var warn2 = new AggregationWarning(AggregationWarningKind.UnresolvedSubtree, "unresolved");

        var a = new AggregationResult(new[] { req1 }, new[] { warn1 });
        var b = new AggregationResult(new[] { req2 }, new[] { warn2 });

        var merged = a.Merge(b);

        Assert.Equal(2, merged.Requirements.Count);
        Assert.Equal(2, merged.Warnings.Count);
        Assert.Contains(req1, merged.Requirements);
        Assert.Contains(req2, merged.Requirements);
        Assert.Contains(warn1, merged.Warnings);
        Assert.Contains(warn2, merged.Warnings);
    }

    [Fact]
    public void AggregationResult_Empty_has_no_requirements_or_warnings()
    {
        var empty = AggregationResult.Empty;

        Assert.Empty(empty.Requirements);
        Assert.Empty(empty.Warnings);
    }
}
