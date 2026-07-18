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
    bool PropagatesAcrossNodes = true,
    /// <summary>
    /// P4 (GAP-3) -- true when the event type is a managed class (must be published via
    /// <c>IEventBus.PublishManaged&lt;T&gt;</c>, not <c>Publish&lt;T&gt;</c>). Baked here (not
    /// discovered via reflection) for the same reason FunctionCallNode.TrailingContext is baked:
    /// the Roslyn incremental generator runs as a netstandard2.0 analyzer that cannot load game
    /// assemblies to inspect a real CLR type. Defaults to false (unmanaged struct, the common case).
    /// </summary>
    bool Managed = false,
    /// <summary>
    /// Baked payload data-in fields for this event (excluding the optional <see cref="TargetFieldName"/>
    /// target, which is projected as the "Target" pin). Each becomes a data-IN pin named by
    /// <see cref="EventPayloadField.Name"/> and typed by <see cref="EventPayloadField.TypeId"/>. Baked
    /// (not reflected) so Stage0/the editor can rehydrate a pin-less PublishEvent node without loading the
    /// game assembly — the same reason <see cref="TargetFieldName"/>/<see cref="Managed"/> are baked.
    /// Null = no payload pins (target-only / marker events).
    /// </summary>
    IReadOnlyList<EventPayloadField>? PayloadFields = null);

/// <summary>One baked PublishEvent payload data-in pin: field name + pin TypeId.</summary>
public sealed record EventPayloadField(string Name, string TypeId);

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
