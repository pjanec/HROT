using Fdp.Core;
using Hrot.Blueprints.Core.Compiler.Emit;

namespace Hrot.Blueprints.Core.Debug;

// ---- Identifier value types -----------------------------------------------

public readonly record struct BreakpointId(int Value);
public readonly record struct WatchId(int Value);

public enum StepMode { None, Over, Into, Out }

// ---- Event record types ----------------------------------------------------

public sealed record BreakpointHit(
    Entity Self,
    string NodeId,
    Guid AssetId,
    float SimulationTime,
    uint Tick);

public sealed record NodeExecuted(
    Entity Self,
    Guid AssetId,
    Guid NodeId,
    string NodeIdString,
    float SimulationTime,
    uint Tick);

// PinValueChanged uses byte[] ValueBytes + Type ValueType per Patch 2 (no boxing on probe path).
public sealed record PinValueChanged(
    Entity Self,
    string PinId,
    byte[] ValueBytes,
    Type ValueType,
    uint Tick);

public readonly record struct NodeHistoryEntry(string NodeId, uint Tick, float SimTime);

// ---- Stub support types (filled in by DBG-002 through DBG-004) ------------

public sealed record Breakpoint(
    BreakpointId Id,
    Guid AssetId,
    Guid GraphId,
    string NodeId,
    int HitCount,
    bool Enabled);

public sealed class Watch
{
    public WatchId Id { get; init; }
}

public sealed record BlueprintStateSnapshot(Entity Self, Guid AssetId);

// ---- Main interface --------------------------------------------------------

/// <summary>
/// Full debug session interface. IBlueprintDebugSession implementations
/// (BlueprintDebugSession in production, CapturingDebugSession in tests) route
/// DebugProbe calls to editor UI subscribers.
/// </summary>
public interface IBlueprintDebugSession : IBlueprintProbeSink
{
    // -- Lifecycle --
    bool IsAttached { get; }
    void Detach();

    // -- Breakpoint management --
    BreakpointId SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId);
    void ClearBreakpoint(BreakpointId id);
    void ClearAllBreakpoints();
    IReadOnlyList<Breakpoint> GetBreakpoints();
    bool IsAnyBreakpointActive { get; }

    // -- Watches --
    WatchId AddWatch(Guid assetId, Guid graphId, Guid pinId);
    void RemoveWatch(WatchId id);
    void ClearAllWatches();
    IReadOnlyList<Watch> GetWatches();
    bool IsAnyWatchActive { get; }

    // -- Pause state --
    bool IsPaused { get; }
    Breakpoint? PausedAt { get; }
    Entity? PausedOnEntity { get; }

    // -- Pause control (soft-pause per Patch 1: all methods return immediately) --
    void Continue();
    void StepOver();
    void StepInto();
    void StepOut();
    void Pause();

    // -- Inspection --
    BlueprintStateSnapshot? GetCurrentStateSnapshot();
    IReadOnlyList<NodeExecuted> GetRecentNodeHistory(int maxCount = 100);

    // -- Map registration --
    void RegisterDebugMap(DebugMap map);
    void UnregisterDebugMap(Guid assetId);

    // -- Events --
    event Action<BreakpointHit>? OnBreakpointHit;
    event Action<NodeExecuted>? OnNodeExecuted;
    // Named OnPinValueChangedEvent to avoid C# conflict with generic method OnPinValueChanged<T>.
    event Action<PinValueChanged>? OnPinValueChangedEvent;
    event Action? OnSessionStateChanged;
    // Fired when RegisterDebugMap detects a structure-hash mismatch; Guid is the affected asset.
    event Action<Guid>? OnBreakpointListChanged;
}

