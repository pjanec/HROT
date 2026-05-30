using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
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
    uint Tick,
    string? SourceFilePath = null,
    int? SourceLine = null);

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
    bool Enabled)
{
    /// <summary>Structure hash captured when this breakpoint was set. 0 = wildcard (matches any hash).</summary>
    public ulong AssetStructureHashAtSetTime { get; init; } = 0;
    /// <summary>True when the asset structure changed and this breakpoint may no longer be valid.</summary>
    public bool IsStale { get; init; } = false;
}

public sealed class Watch
{
    private readonly byte[] _valueBuffer;  // 64-byte pre-allocated buffer
    private int _lastBytesWritten;

    public WatchId    Id                { get; }
    public Guid       AssetId           { get; }
    public Guid       GraphId           { get; }
    public Guid       PinId             { get; }
    public string     PinIdString       { get; }
    public string     DisplayName       { get; }
    public Type       ExpectedType      { get; }
    public int        ExpectedSizeBytes { get; }

    public ReadOnlySpan<byte> LastValueBytes => _valueBuffer.AsSpan(0, _lastBytesWritten);
    public Entity   LastUpdateEntity  { get; private set; }
    public uint     LastUpdateTick    { get; private set; }
    public int      UpdateCount       { get; private set; }
    public bool     HasEverBeenWritten { get; private set; }
    public bool     IsStale           { get; set; }

    public Watch(WatchId id, Guid assetId, Guid graphId, Guid pinId, string displayName, Type expectedType)
    {
        Id                = id;
        AssetId           = assetId;
        GraphId           = graphId;
        PinId             = pinId;
        PinIdString       = pinId.ToString("D");
        DisplayName       = displayName;
        ExpectedType      = expectedType;
        ExpectedSizeBytes = Unsafe.SizeOf<byte>(); // placeholder; actual size written in WriteValue<T>
        _valueBuffer      = new byte[64];
    }

    public void WriteValue<T>(T value, Entity self, uint tick) where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        if (size > 64)
            throw new InvalidOperationException(
                $"Watch value type {typeof(T).Name} is {size} bytes; exceeds 64-byte buffer.");
        Unsafe.WriteUnaligned(ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(_valueBuffer), value);
        _lastBytesWritten  = size;
        LastUpdateEntity   = self;
        LastUpdateTick     = tick;
        UpdateCount++;
        HasEverBeenWritten = true;
    }
}

public sealed record BlueprintStateSnapshot(
    Entity Self,
    Guid AssetId,
    string AssetName,
    BlueprintDispatchKind Dispatch,
    IReadOnlyDictionary<string, object> FieldValues,
    BlueprintLatentCursor? Cursor);

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
    WatchId AddWatch(Guid assetId, Guid graphId, Guid pinId, string displayName, Type expectedType);
    void RemoveWatch(WatchId id);
    void ClearAllWatches();
    IReadOnlyList<Watch> GetWatches();
    bool IsAnyWatchActive { get; }

    // -- Entity filter --
    void SetEntityFilter(Entity? entity);
    Entity? GetEntityFilter();

    // -- Active entity tracking --
    IReadOnlyList<Entity> GetActiveEntities(Guid assetId);

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

    // -- PDB locator --
    void RegisterPdbLocator(Guid assetId, Func<string> pdbPathResolver);

    // -- Hot reload --
    void OnHotReloadBegin();
    void OnHotReloadCompleted(Guid[] reloadedAssetIds);

    // -- Tick boundary (called by coordinator at start of each tick) --
    /// <summary>Resets per-frame breakpoint dedup set. Call once at the start of every simulation tick.</summary>
    void OnNewTick();

    // -- Events --
    event Action<BreakpointHit>? OnBreakpointHit;
    event Action<NodeExecuted>? OnNodeExecuted;
    // Named OnPinValueChangedEvent to avoid C# conflict with generic method OnPinValueChanged<T>.
    event Action<PinValueChanged>? OnPinValueChangedEvent;
    event Action? OnSessionStateChanged;
    // Fired when RegisterDebugMap detects a structure-hash mismatch; Guid is the affected asset.
    event Action<Guid>? OnBreakpointListChanged;
}

