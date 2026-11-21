using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler.Emit;

// DEBT-018 deferred: These debug files are placed in the Hrot.Blueprints.Core root (not a Debug/
// subfolder) to avoid the .gitignore [Dd]ebug/ wildcard match. Namespace is correct.
// New debug files should follow the same placement convention for consistency.

namespace Hrot.Blueprints.Core.Debug;

// ---- Identifier value types -----------------------------------------------

// DEBT-003: BreakpointKey(string NodeId) record from TASK-TH-008 is not implemented;
// BreakpointId (int value) serves the same purpose for all current callers.
// BreakpointKey is a design-only alias not required by any caller.
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

/// <summary>
/// Represents a single peer-call frame on the active call stack (Editor DD §8.7).
/// </summary>
public readonly record struct CallFrame(string PeerAssetIdString, string MethodName, int Depth);

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
    /// <summary>
    /// The runtime probe node id that OnNodeEnter fires.  Each exec node now maps
    /// one-to-one to its own probe: BreakpointTargets[nodeId] == nodeId's probe id.
    /// Defaults to NodeId for pre-compile tentative breakpoints.
    /// </summary>
    public string ProbeNodeId { get; init; } = string.Empty;
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

/// <summary>
/// ⭐⭐ <b>Batch 97 (<c>97c</c>) — where one working-state field lives, as the WRITER wants it.</b>
/// </summary>
/// <param name="ComponentType">
/// ⭐ The ECS <b>COMPONENT</b> the working state is stored in — ⛔ <b>not the field's own type.</b>
/// ⚠ Confusing the two is a size mismatch at best and a write into the wrong component at worst.
/// </param>
/// <param name="ComponentOffsetBytes">
/// ⭐⭐⭐ <b>Batch 102 (<c>102a</c>) — the offset WITHIN THE COMPONENT, fully resolved. ⛔ It was
/// <c>RawOffsetBytes</c>, and the rename is the whole point: it forces every reader to be revisited.</b>
///
/// <para>🔴 <b>Why the contract had to change.</b> The old shape said <i>"raw, within the working-state
/// block; the 8-byte header is applied by <c>TryWriteWorkingStateField</c>"</i> — ⭐ true for
/// <c>AiPrimitive</c>, whose block starts at component byte 0 behind an 8-byte <c>StructureHash</c>.
/// ⛔⛔ <b>It cannot express an <c>Instance</c> address at all.</b> An <c>Instance</c> field lives at
/// <c>payloadOffset + field.OffsetBytes</c>, where the partition allocator chose <c>payloadOffset</c> at
/// runtime and the block opens with a <b>16-byte <c>BlueprintLatentCursor</c></b>, ⛔ not the 8-byte
/// header. ⇒ a writer applying <c>ComponentOffsetOf</c> unconditionally would land <b>8 bytes past every
/// Instance field</b> — 📌 <c>Q32</c> §2.1, <i>"memory corruption, not a wrong value."</i></para>
///
/// <para>⇒ ⭐⭐ <b>Each dispatch kind now applies its OWN transform in the resolver, where the layout is
/// known</b>, and the writer stores what it is given. ⛔ The alternative — returning
/// <c>payloadOffset + offset − 8</c> so the writer's <c>+8</c> cancels — is a lie encoded as
/// arithmetic.</para>
/// </param>
/// <param name="SizeBytes">
/// ⭐ The layout's own width. ⚠ <b>A caller must refuse a payload of a different length</b> rather than
/// truncate or overrun — 📌 <c>Q32</c> §2.1.
/// </param>
public sealed record WorkingStateFieldRef(Type ComponentType, int ComponentOffsetBytes, int SizeBytes);

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
    void Attach();
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

    // -- Live write (Batch 84, row 59c) --

    /// <summary>
    /// ⭐⭐⭐ <b>Writes one working-state field of a LIVE entity, while frozen.</b>
    ///
    /// <para>📌 <b>Ruling 15</b> <i>(user)</i>: <i>"the change of runtime var makes sense <b>ONLY if
    /// sim is paused on breakpoint or deterministic time step</b>. at that time nothing else changes
    /// the blackboard."</i> ⇒ ⛔ <b>a free-running session MUST return <c>false</c>.</b></para>
    ///
    /// <para>📌 <b><c>R-63</c>:</b> the write is <b>STAGED through the command buffer</b>, ⛔ never
    /// applied to <c>ActiveView</c> — while paused that view IS the pre-tick snapshot, and resume
    /// restores the live repo from the POST-tick one, so a direct write is <b>silently lost</b>.
    /// ⭐ The staged write drains AFTER that restore, which is exactly why it survives.</para>
    ///
    /// <para>⚠ <b><paramref name="fieldOffsetBytes"/> is the offset WITHIN THE WORKING-STATE BLOCK</b>,
    /// as the layout reports it. ⛔ Do not add the 8-byte header — the implementation owns that
    /// (<c>WorkingStateLayout</c>), so the read path and the write path cannot disagree by 8 bytes.</para>
    ///
    /// <para>⭐ <b>Returns <c>false</c> rather than throwing</b> when it cannot write: the UI asks this
    /// to decide whether to GREY a control, and a refusal is an expected answer, not a fault.
    /// ⛔ A bad OFFSET is a different thing and still throws — 📌 <c>Q32</c> §2.1: <i>"an out-of-range
    /// offset/size is MEMORY CORRUPTION, not a wrong value."</i></para>
    /// </summary>
    bool TryWriteWorkingStateField(
        Entity entity, Type componentType, int fieldOffsetBytes, ReadOnlySpan<byte> bytes) => false;

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 97 (<c>97c</c>) — where a working-state field LIVES, by NAME.</b>
    ///
    /// <para>🔴🔴 <b>The missing half.</b> <see cref="TryWriteWorkingStateField"/> has existed since
    /// Batch 84 and had <b>zero production callers</b> — 📐 measured by Batch 96 — because the editor
    /// knows a variable by its <b>NAME</b> and that method wants a <c>(componentType, offset)</c>. The
    /// walk that maps one to the other was <b>private</b> to the implementation, used only by the READ
    /// path. ⇒ ⭐ this exposes the same two tables the read consults, as a lookup.</para>
    ///
    /// <para>⛔⛔ <b>The offset returned is RAW — the offset WITHIN THE WORKING-STATE BLOCK</b>, exactly
    /// as the layout reports it, so it can be handed straight to
    /// <see cref="TryWriteWorkingStateField"/>, <b>which applies the 8-byte header itself</b>.
    /// ⚠ The READ path converts before slicing *(<c>WorkingStateLayout.ComponentOffsetOf</c>)*, so
    /// ⛔ <b>copying the read's <c>start</c> here would DOUBLE-APPLY the header and scribble on the
    /// neighbouring field</b> — 📌 <c>Q32</c> §2.1: <i>"an out-of-range offset is MEMORY CORRUPTION,
    /// not a wrong value."</i></para>
    ///
    /// <para>⭐ <b>Returns <c>null</c> when it cannot say</b> — no debug map, no definition, an unknown
    /// name, or ⚠ <b>a dispatch kind whose state is not laid out this way</b>. ⛔ It must never GUESS:
    /// an <c>Instance</c> blueprint's fields are offset within a per-instance payload, a different
    /// space entirely, and answering for one would corrupt memory rather than mis-report a value.</para>
    /// </summary>
    WorkingStateFieldRef? ResolveWorkingStateField(Entity entity, Guid assetId, string fieldName)
        => null;

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

    // -- Node-granular virtual pointer (NGS-2.1) --
    // Valid while IsPaused and recordings exist for the paused entity.
    // Both properties return -1 / null when no recordings are active.

    /// <summary>
    /// Current virtual-pointer index into the sub-tick recording ring.
    /// -1 when no node-granular recordings are active for the paused entity.
    /// </summary>
    int CurrentNodePointer { get; }

    /// <summary>
    /// Node-id string at the current virtual-pointer position.
    /// Null when <see cref="CurrentNodePointer"/> is -1.
    /// </summary>
    string? CurrentNodeId { get; }

    /// <summary>Number of per-node recordings in the ring for the paused tick.</summary>
    int RecordedNodeCount { get; }

    /// <summary>
    /// Move the virtual pointer one step backward (towards node 0).
    /// Clamped at 0 — calling at index 0 is a no-op.
    /// Only valid while <see cref="IsPaused"/> and recordings exist.
    /// </summary>
    void StepBack();

    // -- Inspection --
    BlueprintStateSnapshot? GetCurrentStateSnapshot();

    /// <summary>
    /// Returns a live (non-pause-gated) snapshot of the working-state for the given entity
    /// and blueprint asset. Unlike <see cref="GetCurrentStateSnapshot"/>, this method does
    /// NOT require the session to be paused — it reads state directly from the entity's
    /// blackboard partition. Returns null when the entity has no slot for the asset or when
    /// no DebugMap has been registered for the asset.
    /// </summary>
    BlueprintStateSnapshot? CaptureLiveState(Entity self, Guid assetId);

    IReadOnlyList<NodeExecuted> GetRecentNodeHistory(int maxCount = 100);
    /// <summary>
    /// Returns the peer-call frame stack for the currently paused entity, ordered shallowest-first
    /// (index 0 = outermost call, last = innermost). Returns an empty list when not paused or when
    /// no call stack has been recorded for the paused entity. (Editor DD §8.7)
    /// </summary>
    IReadOnlyList<CallFrame> GetCurrentCallStack();
    // DEBT-022: per-entity node execution history; added to interface for editor access.
    IReadOnlyList<NodeHistoryEntry> GetNodeHistory(Entity entity, int maxCount = 100);

    // -- Map registration --
    void RegisterDebugMap(DebugMap map);
    void UnregisterDebugMap(Guid assetId);

    // -- PDB locator --
    void RegisterPdbLocator(Guid assetId, Func<string> pdbPathResolver);

    // -- Breakpoint eligibility --
    /// <summary>
    /// Returns true when <paramref name="nodeId"/> is present in the DebugMap's
    /// BreakpointTargets (exec nodes only). Pure data nodes (GetVariable, LiteralNode,
    /// CastNode, pure FunctionCall) and unknown ids return false.
    /// </summary>
    bool IsNodeBreakpointable(Guid assetId, Guid graphId, Guid nodeId);

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

