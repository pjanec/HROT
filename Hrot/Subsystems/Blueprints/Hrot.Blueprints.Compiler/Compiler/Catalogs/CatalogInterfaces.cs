namespace Hrot.Blueprints.Core.Compiler.Catalogs;

/// <summary>
/// Reliability level for event delivery across the DDS network layer.
/// Used in EngineEventCatalogEntry to drive BP2016 guard warnings.
/// (DD-3 §6, §6.1)
/// </summary>
public enum EventQoS
{
    /// <summary>
    /// Every event delivery is guaranteed (DDS Reliable). Suitable for
    /// gameplay-critical events (montage lifecycle, hit windows, stance changes).
    /// </summary>
    Reliable = 0,

    /// <summary>
    /// Best-effort UDP delivery; events may be silently dropped. When a WhenNode
    /// subscribes to a BestEffort event the compiler emits a BP2016 warning.
    /// </summary>
    BestEffort = 1,
}

/// <summary>
/// Hint indicating the logical execution node a Blueprint is compiled for.
/// Used by V_WhenNodeRules to enforce BP2017 (Brain-targeted Blueprint
/// subscribing to a Muscle-local event). Default is Any (no node check).
/// (DD-3 §5.2, §6.1)
/// </summary>
public enum ExecutionNodeHint
{
    /// <summary>No execution-node context provided; BP2017 is suppressed.</summary>
    Any = 0,

    /// <summary>Blueprint runs on the Brain node. BP2017 fires on local-only events.</summary>
    Brain = 1,

    /// <summary>Blueprint runs on the Muscle node. Local-only events are available.</summary>
    Muscle = 2,
}

public sealed record EngineEventCatalogEntry(
    string Name,
    string EventTypeFqn,
    string DisplayName = "",
    string Category = "",
    string TargetFieldName = "",
    IReadOnlyList<string>? FilterableFields = null,
    EventQoS QoS = EventQoS.Reliable,
    bool PropagatesAcrossNodes = true);

public sealed record ChannelCommandCatalogEntry(
    string Name, string ChannelTypeFqn, ushort ActionId, string ParamsTypeFqn);

public enum WaitKind { Channel, Event, RingBufferResult }

public sealed record WaitPrimitiveCatalogEntry(
    string Name, WaitKind Kind, string TargetTypeFqn);

public interface IEngineEventCatalog
{
    IReadOnlyList<EngineEventCatalogEntry> GetEntries();
}

public interface IChannelCommandCatalog
{
    IReadOnlyList<ChannelCommandCatalogEntry> GetEntries();
}

public interface IWaitPrimitiveCatalog
{
    IReadOnlyList<WaitPrimitiveCatalogEntry> GetEntries();
}
