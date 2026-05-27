using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// A strategy that can aggregate blackboard DTO requirements from one specific
/// asset kind. Registered by subsystem editors and dispatched by
/// <see cref="BlackboardAggregatorService"/>.
/// </summary>
public interface IBlackboardAggregatorStrategy
{
    bool CanHandle(IEditableAsset asset);

    /// <summary>
    /// Aggregate DTO requirements from <paramref name="asset"/> and
    /// statically-resolvable descendants. <paramref name="visited"/> is the
    /// caller-maintained cycle-guard set; the strategy must add
    /// <paramref name="asset"/>.AssetId before recursing.
    /// </summary>
    AggregationResult Aggregate(
        IEditableAsset            asset,
        IActionSchemaExporter     schema,
        IAssetCatalog             catalog,
        HashSet<Guid>             visited);
}

/// <summary>
/// Dispatches aggregation to registered <see cref="IBlackboardAggregatorStrategy"/>
/// implementations. Register strategies in ascending priority order; the first
/// one whose CanHandle returns true wins.
/// </summary>
public sealed class BlackboardAggregatorService
{
    private readonly List<IBlackboardAggregatorStrategy> _strategies;
    private readonly IActionSchemaExporter _schema;
    private readonly IAssetCatalog _catalog;

    public BlackboardAggregatorService(
        IEnumerable<IBlackboardAggregatorStrategy> strategies,
        IActionSchemaExporter schema,
        IAssetCatalog catalog)
    {
        _strategies = strategies.ToList();
        _schema = schema;
        _catalog = catalog;
    }

    // Registers a strategy after construction; used for test bootstrapping to
    // break the circular dependency between service and strategy.
    internal void Register(IBlackboardAggregatorStrategy strategy) => _strategies.Add(strategy);

    public AggregationResult Aggregate(IEditableAsset asset)
    {
        var visited = new HashSet<Guid>();
        return AggregateInternal(asset, visited);
    }

    // Entry point used by strategies that recurse into child assets.
    public AggregationResult AggregateInternal(IEditableAsset asset, HashSet<Guid> visited)
    {
        foreach (var s in _strategies)
            if (s.CanHandle(asset))
                return s.Aggregate(asset, _schema, _catalog, visited);

        // No registered strategy for this asset kind -- return empty.
        return new AggregationResult(
            Array.Empty<DtoRequirement>(),
            Array.Empty<AggregationWarning>());
    }
}

public sealed record AggregationResult(
    IReadOnlyList<DtoRequirement>     Requirements,
    IReadOnlyList<AggregationWarning> Warnings)
{
    public static AggregationResult Empty { get; } =
        new(Array.Empty<DtoRequirement>(), Array.Empty<AggregationWarning>());

    public AggregationResult Merge(AggregationResult other) =>
        new(Requirements.Concat(other.Requirements).ToList(),
            Warnings.Concat(other.Warnings).ToList());
}

/// <summary>
/// One parameter DTO requirement discovered during aggregation.
/// </summary>
/// <param name="DtoType">The action's parameter DTO type.</param>
/// <param name="RequiredByPath">
/// Human-readable provenance string, e.g. "Shoot_BT > Action#7 (FireAtTarget)".
/// </param>
/// <param name="RequiringAssetId">Asset in which the requirement was found.</param>
/// <param name="RequiringElementId">Node/element visual-id within that asset.</param>
public sealed record DtoRequirement(
    Type   DtoType,
    string RequiredByPath,
    Guid   RequiringAssetId,
    Guid   RequiringElementId);

public enum AggregationWarningKind
{
    UnresolvedSubtree,
    Cycle,
    SchemaEntryNotFound,
}

public sealed record AggregationWarning(
    AggregationWarningKind Kind,
    string Message,
    Guid? AssetId = null);
