# Behavior Diagnostics — FastBTree & FastHSM Per-Entity Trace Buffers

**Reference design talk:** [design-talk.md](./design-talk.md)
**Tasks:** [TASK-DETAIL.md](./TASK-DETAIL.md)
**Tracker:** [TASK-TRACKER.md](./TASK-TRACKER.md)
**Debt:** [DEBT-TRACKER.md](./DEBT-TRACKER.md)

---

## 1. Motivation

The framework currently provides rich diagnostic facilities for FastHSM (a 64KB managed `HsmTraceBuffer` driven by a process-static pointer in [HsmKernelCore.cs:13](FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmKernelCore.cs#L13) and `HsmKernelCore.SetTraceBuffer`) but **no equivalent for FastBTree** — BTree diagnosis today relies on the live `BTreeVisualizerRenderer`, manual `BehaviorLog` calls inside condition/action nodes, and post-hoc inspection of `BrainBlackboard` values. A fleeting condition flip cannot be reconstructed during replay.

Even the HSM trace path is unsuitable for replay scrubbing: it routes through a single global managed buffer that cannot record into the `.fdp` flight recorder and forbids concurrent per-entity tracing.

**Goal:** introduce a per-entity, zero-allocation, ECS-native trace buffer that:
1. Captures BTree node evaluations (and supporting structural ops) automatically.
2. Captures the existing HSM trace opcodes per-entity, removing the global static buffer.
3. Survives flight-recorder save/replay so traces can be inspected during scrub.
4. Is dynamically toggleable per entity at runtime (UI context menu, programmatic).
5. Can optionally be auto-enabled system-wide from a global debug flag.
6. Can optionally mirror its records to NLog (`AI.Behavior` target) via `BehaviorLog`.
7. Renders in the existing entity inspector and exports into JSON dumps.

The capacity target is "few seconds of history for one or two debugged entities at a time" — sufficient for immediate causality investigation. Heavier captures are deferred to a possible future managed-singleton add-on.

---

## 2. Architectural Overview

### 2.1 Component Topology

Each behavior-aware entity may optionally carry:

- **`BTreeTraceWorkingMemory1024`** — 1024-byte unmanaged ECS component. Ring buffer of `BTreeTraceRecord`s. Lives only on entities currently executing a BTree.
- **`HsmTraceWorkingMemory1024`** — 1024-byte unmanaged ECS component. Ring buffer of `TraceRecord`s. Lives only on entities currently executing an HSM.
- **`DebugState`** — small unmanaged transient component carrying generic debug bitflags (one group per subsystem). Whether tracing is *active* for a given entity is decided by `DebugState.Behavior` bits, not by the presence of the buffer alone.

Both ring buffers are pure unmanaged structs that fit the 1024-byte `MaxComponentSize` ceiling enforced by [EntityCommandBuffer.cs:34](FDP/Engine/Fdp.Core/EntityCommandBuffer.cs#L34). They are decorated with `[DataPolicy(DataPolicy.NoSave)]` so the JSON scenario serializer ignores them while the Flight Recorder still snapshots their byte contents into `.fdp` recordings.

### 2.2 Data Flow

```
                    DebugState.Behavior bits
                              |
                              v
   BTreeTickSystem / HsmTickSystem
        |              |
        | resolves     | resolves
        v              v
   tracePtr (BTreeTraceWorkingMemory1024*)   tracePtr (HsmTraceWorkingMemory1024*)
        |                                       |
        v                                       v
   BTreeContext (impl ITreeTracer)         HsmTraceContext* (Fhsm.Kernel.Data)
        |                                       |
        v                                       v
   Interpreter<TB,TC>                      HsmKernelCore
        |                                       |
        | calls ctx.TraceNodeEvaluated(...)     | calls traceCtx->WriteX(...)
        v                                       v
        +----- writes 16-byte records into the 1020-byte payload via WritePos%1008 ----+
                                                                                       |
   Flight Recorder picks up the dirtied chunk version and serializes 1024 bytes/entity v
                                              ^
                                              |
   ImGui renderers / JSON translators read the ring back via BehaviorRegistry symbols
```

### 2.3 Layer Boundaries (Critical)

The FastBTree and FastHSM kernel projects (`Fbt.Kernel`, `Fhsm.Kernel`) **must not** depend on `Fdp.Toolkits`, `Hrot.*`, or the concrete 1024-byte component types. They expose pure unmanaged contracts:

- `Fbt.ITreeTracer` — an interface containing `TraceNodeEvaluated`, `TraceScopePushed/Popped`, `TraceWaitStarted/Completed`. The generic `TContext` of `Interpreter<TB,TC>` is constrained `where TContext : struct, IAIContext, ITreeTracer`. JIT devirtualizes the interface calls on a constrained generic struct, so the indirection is free.
- `Fhsm.Kernel.Data.HsmTraceContext` — an unmanaged struct holding pointers into a caller-owned byte buffer plus header fields (`WritePos`, `RecordCount`, `CapacityBytes`, `MaxRecords`, `FilterLevel`, `CurrentTick`). All ring-buffer math lives in this struct.

The concrete `BTreeContext` (in `Fdp.Toolkit.Behavior`) implements `ITreeTracer`, holds an `unsafe BTreeTraceWorkingMemory1024* TraceBuffer`, and forwards calls. The `HsmTickSystem` constructs an `HsmTraceContext` over the entity's `HsmTraceWorkingMemory1024` and passes its pointer down to `HsmKernel.Update`. Neither kernel sees a concrete component type.

### 2.4 Replay Strategy

The 1024-byte buffer is a normal unmanaged component, so the Flight Recorder snapshots its bytes natively each frame. During replay scrubbing, `PlaybackSystem.ApplyChunkData` restores the bytes and the ImGui renderer reads from the restored memory. Per the design talk's discussion of `RecordDeltaFrame`/`_chunkVersions`, this means a traced entity will dirty its chunk every frame — acceptable because tracing is opt-in and only used for one or two entities during active debugging.

We explicitly do **not** implement the optional global singleton snapshot scheme described in the design talk; if the 1024-byte buffer proves too short, that managed-singleton extension may be added later.

---

## 3. Unmanaged Memory Layouts

### 3.1 `BTreeTraceOpCode` (new enum in `Fbt.Kernel`)

```csharp
public enum BTreeTraceOpCode : byte
{
    None          = 0x00,
    NodeEvaluated = 0x01,
    ScopePushed   = 0x02,
    ScopePopped   = 0x03,
    WaitStarted   = 0x04,
    WaitCompleted = 0x05,
    ChannelMutated= 0x06,
    Error         = 0x0E,
}
```

The opcode lives in `Fbt.Kernel` because `ITreeTracer` references it; concrete payload layout for `ChannelMutated`/`Error` is mapped by the application layer.

### 3.2 `BTreeTraceRecord` (16-byte union, `Fdp.Toolkit.Behavior.Diagnostics`)

```csharp
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct BTreeTraceRecord
{
    // Header (8 bytes)
    [FieldOffset(0)] public BTreeTraceOpCode OpCode;     // byte
    [FieldOffset(1)] public byte Reserved;
    [FieldOffset(2)] public ushort Timestamp;            // entity-relative tick
    [FieldOffset(4)] public uint InstanceId;             // BehaviorState.InstanceId

    // Payload union (8 bytes) — fields share offsets per opcode
    [FieldOffset(8)] public ushort NodeIndex;            // NodeEvaluated, Wait*, ChannelMutated, Error
    [FieldOffset(10)] public NodeStatus Status;          // byte — NodeEvaluated

    [FieldOffset(8)] public ushort StackDepth;           // Scope*

    [FieldOffset(8)] public float Duration;              // Wait* (overlaps NodeIndex/Status)

    [FieldOffset(8)] public ChannelKind Channel;         // byte — ChannelMutated (overlaps NodeIndex low byte)
    [FieldOffset(10)] public ushort ActiveAction;        // ChannelMutated
    [FieldOffset(12)] public NodeStatus ChannelStatus;   // byte — ChannelMutated

    [FieldOffset(8)] public ushort ErrorCode;            // Error
}
```

For `ChannelMutated` and `Wait*`, the design accepts overlapping `NodeIndex` access: the application-layer writer is responsible for writing the union fields in the right order. The ImGui renderer/JSON translator switches on `OpCode` and reads the correct overlay.

We deliberately omit the variable-length `FixedString32` opcode for this iteration; if needed later it can be added when a larger buffer variant is introduced.

### 3.3 `BTreeTraceWorkingMemory1024`

```csharp
[StructLayout(LayoutKind.Sequential, Size = 1024)]
[ComponentId(BehaviorApplicationComponentIds.BTreeTraceWorkingMemory)]
[DataPolicy(DataPolicy.NoSave)]
public unsafe struct BTreeTraceWorkingMemory1024
{
    public const int RecordStride    = 16;
    public const int CapacityRecords = 63;
    public const int PayloadBytes    = CapacityRecords * RecordStride; // 1008 (== usable buffer)
    public const int BufferBytes     = 1016;                           // 1024 - 8-byte header

    // 8-byte header
    public ushort WritePos;     // byte offset in [0, PayloadBytes); ALWAYS pre-wrapped mod PayloadBytes
    public ushort RecordCount;  // saturates at CapacityRecords
    public uint   LastInstanceId;

    // 1016-byte payload. First 1008 bytes (=63 records × 16) are written; trailing 8 bytes are unused.
    public fixed byte Buffer[BufferBytes];
}
```

> **Critical:** `WritePos` is wrapped *at write time* within `[0, PayloadBytes)` (i.e., `[0, 1008)`), **not** at `ushort` overflow boundary. The naive `WritePos += 16` pattern with deferred modulo is unsafe — `ushort` rolls at 65536, and `65536 % 1008 ≠ 0`, so the slot stream would drift after the first overflow and corrupt chronological ordering. The wrap math is enforced inside `NextRecord()` (§4.5).

### 3.4 `HsmTraceWorkingMemory1024`

Identical layout to BTree. Same wrap semantics — `WritePos` is pre-wrapped in `[0, 1008)`.

```csharp
[StructLayout(LayoutKind.Sequential, Size = 1024)]
[ComponentId(BehaviorApplicationComponentIds.HsmTraceWorkingMemory)]
[DataPolicy(DataPolicy.NoSave)]
public unsafe struct HsmTraceWorkingMemory1024
{
    public const int RecordStride    = 16;
    public const int CapacityRecords = 63;
    public const int PayloadBytes    = CapacityRecords * RecordStride; // 1008
    public const int BufferBytes     = 1016;

    public ushort WritePos;
    public ushort RecordCount;
    public uint   LastInstanceId;
    public fixed byte Buffer[BufferBytes];
}
```

The active `TraceLevel` filter is carried in the stack-local `HsmTraceContext` (§5.2), not stored in the component, so toggling the filter level does not dirty the ECS chunk.

### 3.5 ID Allocation

In [BehaviorApplicationComponentIds.cs](FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorApplicationComponentIds.cs) (FDP-level, 160–199 block):

```csharp
public const byte ActiveMissionPlan         = 162; // existing
public const byte BTreeTraceWorkingMemory   = 163;
public const byte HsmTraceWorkingMemory     = 164;
public const byte DebugState                = 165;
```

All three new IDs land in `BehaviorApplicationComponentIds` (FDP-level). See §6 for why `DebugState` moves to FDP-level instead of `HrotComponentIds`.

### 3.6 Registration

In [CognitiveComponentRegistry.cs](Hrot/Subsystems/Hrot.SimHost/CognitiveComponentRegistry.cs):

```csharp
world.RegisterComponent<BTreeTraceWorkingMemory1024>();
world.RegisterComponent<HsmTraceWorkingMemory1024>();
world.RegisterComponent<DebugState>();
```

---

## 4. FastBTree Kernel Instrumentation

### 4.1 The `ITreeTracer` Contract (new in `Fbt.Kernel`)

```csharp
namespace Fbt
{
    public interface ITreeTracer
    {
        void TraceNodeEvaluated(int nodeIndex, NodeStatus status);
        void TraceScopePushed(ushort newStackDepth);
        void TraceScopePopped(ushort newStackDepth);
        void TraceWaitStarted(int nodeIndex, float duration);
        void TraceWaitCompleted(int nodeIndex, float duration);
    }
}
```

`ChannelMutated` and `Error` are domain-level and **not** part of the kernel contract — they are exposed directly on the concrete `BTreeTraceWorkingMemory1024` struct via instance methods and called by user-authored action code.

### 4.2 Interpreter Generic Constraint Update

In [Interpreter.cs:8](FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs#L8):

```csharp
public class Interpreter<TBlackboard, TContext> : ITreeRunner<TBlackboard, TContext>
    where TBlackboard : struct
    where TContext   : struct, IAIContext, ITreeTracer   // NEW: ITreeTracer
```

Calls on a struct-constrained generic interface are devirtualized and inlined by the JIT, so the indirection costs nothing at runtime.

### 4.3 Engine Hooks

- **`ExecuteAction`** (covers both action and condition nodes): after the action delegate returns, call `ctx.TraceNodeEvaluated(nodeIndex, status)`.
- **`ExecuteWait`**: on the frame the wait starts, call `ctx.TraceWaitStarted(nodeIndex, duration)`. On the frame it completes, call `ctx.TraceWaitCompleted(nodeIndex, duration)`.
- **`BehaviorTreeState.PushNode` / `PopNode`**: extend the signatures to take a `ref TContext` parameter (or have `Interpreter` call `ctx.TraceScopePushed/Popped` immediately after invoking `state.PushNode/PopNode`). We choose the latter to avoid coupling `BehaviorTreeState` (in `Fbt.Kernel`) to `ITreeTracer` generic dispatch — `Interpreter` already controls every Push/Pop site.

`ExecuteSelector` / `ExecuteSequence` themselves do **not** emit additional opcodes beyond what their child evaluations emit; per-child status is already captured via `ExecuteAction` traces.

### 4.4 Concrete `BTreeContext` Update

In [BTreeContext.cs](FDP/Toolkits/Fdp.Toolkits/Behavior/BTreeContext.cs):

```csharp
public struct BTreeContext : IAIContext, ITreeTracer
{
    public Entity Self;
    public EntityRepository World;
    internal float _deltaTime;
    internal float _time;
    internal int   _frameCount;
    internal float[]? _floatParams;
    internal int[]?   _intParams;

    public unsafe BTreeTraceWorkingMemory1024* TraceBuffer;  // NEW

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TraceNodeEvaluated(int nodeIndex, NodeStatus status)
    {
        unsafe { if (TraceBuffer != null) TraceBuffer->WriteNodeEvaluated(nodeIndex, status, (ushort)_frameCount); }
    }
    // …same shape for TraceScopePushed/Popped/WaitStarted/WaitCompleted
}
```

### 4.5 Write API on `BTreeTraceWorkingMemory1024`

Instance methods, all `[MethodImpl(MethodImplOptions.AggressiveInlining)]`:

- `WriteNodeEvaluated(int nodeIndex, NodeStatus status, ushort tick)`
- `WriteScopePushed(ushort stackDepth, ushort tick)`
- `WriteScopePopped(ushort stackDepth, ushort tick)`
- `WriteWaitStarted(int nodeIndex, float duration, ushort tick)`
- `WriteWaitCompleted(int nodeIndex, float duration, ushort tick)`
- `WriteChannelMutated(int nodeIndex, ChannelKind channel, ushort activeAction, NodeStatus status, ushort tick)`
- `WriteError(int nodeIndex, ushort errorCode, ushort tick)`

A private `NextRecord()` helper performs:

```csharp
private BTreeTraceRecord* NextRecord()
{
    int offset = WritePos;                                            // already wrapped in [0, 1008)
    WritePos   = (ushort)((WritePos + RecordStride) % PayloadBytes);  // wrap inside payload, not ushort
    if (RecordCount < CapacityRecords) RecordCount++;

    var record = (BTreeTraceRecord*)((byte*)Unsafe.AsPointer(ref Buffer[0]) + offset);
    Unsafe.InitBlockUnaligned(record, 0, (uint)RecordStride);         // zero before union write
    record->InstanceId = LastInstanceId;                               // stamped by tick system each frame
    return record;
}
```

All record bytes are zeroed before writing union-overlapping fields to keep the JSON dump predictable. Because `WritePos` is always in `[0, 1008)`, the ImGui renderer and JSON translator iterate the ring with `startOffset = (RecordCount == CapacityRecords) ? WritePos : 0` (no further modulo needed on `WritePos`).

### 4.6 InstanceId on BTree records

Set the record's `InstanceId` from `BehaviorState.InstanceId` (a `uint` already present on the component). The tick system passes this down to the context once per entity per tick (either as a context field or as a one-time prologue write). Recommended: add `internal uint _instanceId` to `BTreeContext` and stamp it during context construction; the kernel writes `record->InstanceId = ctx._instanceId` via the context's accessor (or via the buffer-write methods that already accept tick — extend their signatures to accept InstanceId too, or read it via the buffer's stored copy if we keep a small header field). **Final decision:** add a 4-byte header field `LastInstanceId` to the 1024-byte component (replacing 4 bytes of trailing padding) so the buffer remembers its current owner; the tick system overwrites it each frame and `NextRecord()` stamps it into each record.

Final header: `ushort WritePos; ushort RecordCount; uint LastInstanceId;` (8 bytes), payload `fixed byte Buffer[1016]`, `CapacityRecords = 63 (= 1008 / 16)`, with 8 trailing bytes of padding inside `Buffer`. (HSM mirrors this layout.)

---

## 5. FastHSM Kernel Refactoring

### 5.1 Eradicate the Global Static

Delete `private static HsmTraceBuffer? _traceBuffer` and `SetTraceBuffer(...)` from [HsmKernelCore.cs](FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmKernelCore.cs), and delete the managed `HsmTraceBuffer` class entirely. All internal call sites currently calling `_traceBuffer?.WriteX(...)` are rewritten to call methods on the unmanaged `HsmTraceContext*` parameter.

### 5.2 `HsmTraceContext` (new, unmanaged, in `Fhsm.Kernel.Data`)

```csharp
public unsafe struct HsmTraceContext
{
    public byte*    Buffer;
    public ushort*  WritePos;
    public ushort*  RecordCount;
    public ushort   CapacityBytes;   // UsablePayload from the caller's 1KB component
    public ushort   MaxRecords;
    public TraceLevel FilterLevel;
    public ushort   CurrentTick;
    public uint     InstanceId;

    public void WriteTransition(uint instanceId, ushort src, ushort dst, ushort eventId);
    public void WriteStateChange(uint instanceId, ushort stateIndex, bool isEntry);
    public void WriteEventHandled(uint instanceId, ushort eventId);
    public void WriteGuardEvaluated(uint instanceId, ushort guardId, bool result);
    public void WriteActionExecuted(uint instanceId, ushort actionId);
    public void WriteError(uint instanceId, ushort errorCode);
    public void WriteConflict(uint instanceId, ushort detail);
    // (TimerSet/TimerFired remain unwired — see §11 future work)
}
```

All write methods short-circuit on the `FilterLevel` bit mask before doing any work. The internal `WriteRecord<T>(T* payload)` helper enforces a **strict 16-byte stride** regardless of the payload struct's natural size:

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private void WriteRecord<T>(T* payload) where T : unmanaged
{
    int offset = *WritePos;                                  // already wrapped in [0, CapacityBytes)
    *WritePos  = (ushort)((*WritePos + 16) % CapacityBytes); // wrap inside payload, not ushort
    if (*RecordCount < MaxRecords) (*RecordCount)++;

    byte* dst = Buffer + offset;
    Unsafe.InitBlockUnaligned(dst, 0, 16);                   // zero entire slot first
    Unsafe.CopyBlockUnaligned(dst, payload, (uint)sizeof(T)); // copy actual payload (≤16 bytes)
}
```

> **Why strict 16-byte stride.** The existing FastHSM record structs are mixed 12/16 byte (`TraceStateChange` = 12, `TraceTransition` = 16, etc., from [TraceRecord.cs](FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/TraceRecord.cs)). Advancing the cursor by `sizeof(T)` would let records straddle the 1008-byte payload boundary — e.g., a 16-byte write starting at offset 1004 would overrun by 4 bytes into adjacent ECS-chunk memory, causing silent cross-component corruption. By always advancing exactly 16 bytes (and pre-zeroing the slot), every record sits in a uniform 16-byte cell and the wrap math is fully aligned. The trailing 4 bytes of a 12-byte payload remain zero — the reader uses `OpCode` to determine which fields are valid, so the wasted bytes cost nothing in decode correctness.

The 12-byte and 16-byte HSM record variants (`TraceStateChange`, `TraceEventHandled`, `TraceActionExecuted`, `TraceError`, `TraceGuardEvaluated`, `TraceTransition`) keep their existing struct layouts; only the buffer-level stride is uniform.

> **Capacity note:** `RecordCount` is an exact count of slots written, capped at `CapacityRecords = 63`. The ImGui renderer iterates by stepping 16 bytes per record and dispatching the decode by `OpCode`.

### 5.3 Pipeline Threading

Add an `HsmTraceContext* traceCtx` parameter (defaulting to `null`) to:

- `HsmKernel.Update<TInstance,TContext>` and `HsmKernel.UpdateBatch<...>`
- `HsmKernelCore.UpdateBatchCore`
- `HsmKernelCore.ProcessInstancePhase`
- `HsmKernelCore.ExecuteTransition`
- All other internal call sites that currently invoke `_traceBuffer?.WriteX`

Each call site checks `traceCtx != null && (header->Flags & InstanceFlags.DebugTrace) != 0` before writing (mirroring the existing per-instance gate at [InstanceHeader.cs](FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/InstanceHeader.cs)'s `InstanceFlags.DebugTrace` bit 6).

### 5.4 `HsmKernelBridge` Extension

In [HsmTickSystem.cs:23](FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmTickSystem.cs#L23):

```csharp
public unsafe struct HsmKernelBridge
{
    public Entity  Self;
    public IntPtr  WorldHandle;
    public HsmTraceContext* TraceContext;  // NEW — null if tracing disabled
}
```

User-authored HSM actions/guards may write domain-level errors via `bridge.TraceContext->WriteError(...)`.

### 5.5 `TraceSymbolicator` (`Fhsm.Kernel`)

Keep `TraceSymbolicator` unchanged in its current role (symbolicating a `ReadOnlySpan<TraceRecord>` using `MachineMetadata` — used by the diagnostic UI). Its bug where `EventHandled` is no-op is **not** fixed in this design (separate concern; tracked as future work).

---

## 6. Generic `DebugState` & Patch Command

> **Project-dependency note (deviation from user preference).** The user initially preferred `DebugState` to live in `Hrot.Common`. However, `Fdp.Toolkits` does **not** reference `Hrot.Common` (it's a lower layer — verified in [Fdp.Toolkits.csproj](FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj)). The BTree/HSM tick systems live in `Fdp.Toolkits` and must read `DebugState` each tick. To avoid breaking the layer direction, `DebugState`, `BehaviorDebugFlags`, `PatchDebugStateCommand`, `DebugStatePatchCompiler`, and `DebugStatePatchSystem` live in **`Fdp.Toolkit.Behavior.Diagnostics`** (FDP-level) — alongside the trace buffer types. `Hrot.Common` (which already references `Fdp.Toolkits`) can still register them. The component ID moves from `HrotComponentIds` to `BehaviorApplicationComponentIds`.

### 6.1 The Component

In `Fdp.Toolkit.Behavior.Diagnostics`:

```csharp
[Flags]
public enum BehaviorDebugFlags : uint
{
    None              = 0,
    EnableTraceBuffer = 1u << 0,
    EmitToLog         = 1u << 1,
    HsmTraceTier1     = 1u << 2,  // maps to TraceLevel.Tier1
    HsmTraceTier2     = 1u << 3,
    HsmTraceTier3     = 1u << 4,
}

[StructLayout(LayoutKind.Sequential)]
[ComponentId(BehaviorApplicationComponentIds.DebugState)]
[DataPolicy(DataPolicy.Transient)]
public struct DebugState
{
    public BehaviorDebugFlags Behavior;
    // Future groups (Physics, Network, …) can be appended as additional flag fields.
}
```

`StructEdit` natively renders `[Flags]` enums as a per-bit checkbox grid through [ComponentEditDrawer.cs:483](FDP/Engine/Fdp.Presentation/ImGui/Editing/ComponentEditDrawer.cs#L483), so the inspector exposes per-flag checkboxes with zero extra UI code.

### 6.2 `PatchDebugStateCommand` (managed event in `Fdp.Toolkit.Behavior.Diagnostics`)

```csharp
public sealed class PatchDebugStateCommand
{
    public Entity Target;
    public string PatchJson = string.Empty;
}
```

Registered via `world.RegisterManagedEvent<PatchDebugStateCommand>()` alongside the existing managed events.

### 6.3 Expression-Tree Patch Compiler

`DebugStatePatchCompiler` (in `Fdp.Toolkit.Behavior.Diagnostics`, alongside `DebugState` — see §6 header note):

- On `Build()`, scans every public instance field of `DebugState`. For each `[Flags]` enum field, compiles a setter delegate that walks a nested JSON object's properties, mapping each property name to an enum value via `Enum.Parse` at build time, emitting `field |= value` / `field &= ~value` bitwise expressions. For primitive/`FixedString32` fields, emits a direct `field = element.GetXxx()` assignment.
- Exposes `static void ApplyPatch(ref DebugState state, string json)` that parses the JSON via `System.Text.Json.JsonDocument` and dispatches each top-level property to its compiled setter.

This avoids runtime reflection on the hot path and makes the patch system fail at compile time when the consumer (UI handler) uses an invalid `nameof(...)` expression.

### 6.4 `DebugStatePatchSystem`

```csharp
[UpdateInPhase(SystemPhase.Input)]
public sealed class DebugStatePatchSystem : IEcsModuleSystem
```

Drains `repo.Bus.ReadManaged<PatchDebugStateCommand>()`, ensures `DebugState` exists (`AddComponent` if missing — never replaces an existing one), then `DebugStatePatchCompiler.ApplyPatch(ref state, cmd.PatchJson)` on the `ref` from `GetComponentRW<DebugState>`.

> **Note on system ordering:** `UpdateBefore`/`UpdateAfter` attribute classes do not exist in this codebase (only `UpdateInPhase`). Order within a phase is dictated by registration order in module/composition-root code, per the comment at [HsmTickSystem.cs:50](FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmTickSystem.cs#L50). `DebugStatePatchSystem` is registered in `SystemPhase.Input` before any system that reads `DebugState` in `Simulation`.

---

## 7. Trace-Buffer Provisioning System

`BTreeTraceWorkingMemory1024` / `HsmTraceWorkingMemory1024` components are added/removed reactively based on `DebugState.Behavior & EnableTraceBuffer`:

`TraceBufferLifecycleSystem` (in `Fdp.Toolkit.Behavior.Diagnostics`):

```csharp
[UpdateInPhase(SystemPhase.BeforeSync)]
public sealed class TraceBufferLifecycleSystem : IEcsModuleSystem
```

For every entity with `DebugState`, it inspects `BehaviorState.BrainTier` and ensures the matching trace component exists (if `EnableTraceBuffer` is set) or is removed (if cleared). Adding/removing here happens between the `Input` phase (where the patch was applied) and the `Simulation` phase (where the tick systems consume the pointer), so the buffer is present on the very first tick after enabling.

---

## 8. Tick System Integration

### 8.1 `BTreeTickSystem`

In [BTreeTickSystem.cs](FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BTreeTickSystem.cs), inside the per-entity loop:

```csharp
unsafe BTreeTraceWorkingMemory1024* tracePtr = null;
if (repo.HasComponent<DebugState>(entity))
{
    ref readonly var dbg = ref repo.GetComponentRO<DebugState>(entity);
    if ((dbg.Behavior & BehaviorDebugFlags.EnableTraceBuffer) != 0 &&
        repo.HasComponent<BTreeTraceWorkingMemory1024>(entity))
    {
        ref var traceMem = ref repo.GetComponentRW<BTreeTraceWorkingMemory1024>(entity);
        traceMem.LastInstanceId = behavior.InstanceId;
        tracePtr = (BTreeTraceWorkingMemory1024*)Unsafe.AsPointer(ref traceMem);
    }
}

var ctx = new BTreeContext
{
    Self        = entity,
    World       = repo,
    _deltaTime  = deltaTime,
    _frameCount = (int)repo.GlobalVersion,
    _floatParams= def.BTreeInterpreter.Blob.FloatParams,
    _intParams  = def.BTreeInterpreter.Blob.IntParams,
    _instanceId = behavior.InstanceId,
    TraceBuffer = tracePtr,
};
```

When `tracePtr == null` we never call `GetComponentRW<BTreeTraceWorkingMemory1024>`, so the chunk version is not bumped and the Flight Recorder delta-compression remains undisturbed.

### 8.2 `HsmTickSystem<T>`

Identical pattern, but instead of injecting a pointer into the context, the system builds a stack-local `HsmTraceContext` over the component memory and updates the `InstanceFlags.DebugTrace` bit on the `InstanceHeader` to match `EnableTraceBuffer`:

```csharp
HsmTraceContext traceCtx = default;
HsmTraceContext* traceCtxPtr = null;
if (tracingEnabled && repo.HasComponent<HsmTraceWorkingMemory1024>(entity))
{
    ref var mem = ref repo.GetComponentRW<HsmTraceWorkingMemory1024>(entity);
    traceCtx.Buffer        = (byte*)Unsafe.AsPointer(ref mem.Buffer[0]);
    traceCtx.WritePos      = (ushort*)Unsafe.AsPointer(ref mem.WritePos);
    traceCtx.RecordCount   = (ushort*)Unsafe.AsPointer(ref mem.RecordCount);
    traceCtx.CapacityBytes = HsmTraceWorkingMemory1024.UsablePayload;
    traceCtx.MaxRecords    = HsmTraceWorkingMemory1024.CapacityRecords;
    traceCtx.FilterLevel   = ResolveTraceLevel(dbg.Behavior);
    traceCtx.CurrentTick   = (ushort)repo.GlobalVersion;
    traceCtx.InstanceId    = component.Header.MachineId; // or InstanceHeader.MachineId
    traceCtxPtr = &traceCtx;
}
var bridge = new HsmKernelBridge { Self = entity, WorldHandle = repo.UnmanagedHandle, TraceContext = traceCtxPtr };
HsmKernel.Update(def.HsmDefinition, ref component, bridge, deltaTime, ref cmdPage, traceCtxPtr);
```

`ResolveTraceLevel(BehaviorDebugFlags)` maps the `HsmTraceTier1/2/3` bits to `TraceLevel.Tier1/2/3`.

The `InstanceFlags.DebugTrace` bit is set when `EnableTraceBuffer` is on so existing per-instance gates inside `HsmKernelCore` honor the toggle.

---

## 9. ImGui Renderers

### 9.1 `BTreeTraceWorkingMemoryRenderer` (`Hrot.Presentation`)

Implements `IEntityAwareImGuiRenderer`, decorated with `[ImGuiRenderer(typeof(BTreeTraceWorkingMemory1024))]`. Pattern mirrors [BrainBlackboardRenderer.cs](Hrot/Engine/Hrot.Presentation/Renderers/BrainBlackboardRenderer.cs):

- Static `BehaviorRegistry? BehaviorRegistryAccessor` set at composition root.
- Resolves the entity's `BehaviorState.ActiveBehaviorHash` via `IInspectableSession.GetComponent(entity, typeof(BehaviorState))`.
- Looks up `def.BTreeInterpreter.Blob` for `NodeDebugMetadata[]` to translate `NodeIndex → Label`.
- Iterates `RecordCount` records in chronological order: `startOffset = RecordCount == CapacityRecords ? WritePos % UsablePayload : 0`.
- Renders an ImGui table with columns: `Tick | OpCode | Node (idx + label) | Result/Detail`.

### 9.2 `HsmTraceWorkingMemoryRenderer` (`Hrot.Presentation`)

Same shape. Resolves `MachineMetadata` via `BehaviorDefinition.HsmMetadata` (the new field added in §10) and uses `GetStateName / GetEventName / GetActionName` to label indices. The renderer handles variable-length HSM records by re-reading each header's `OpCode` and dispatching the row layout per opcode (state change / transition / action / guard / error).

Both renderers display both the numeric ID and the human-readable name (e.g., `1 (Cruising)`).

> **Known limitation — `ChannelMutated.ActiveAction` is rendered numerically.** The `ActiveAction` field on a `ChannelMutated` record is a domain `ushort` (e.g., `NavigationConstants.ActionIdMoveTo = 1`). The `BehaviorTreeBlob.DebugMetadata` array maps node indices to labels but does **not** map action IDs to names. We deliberately do not inject domain registries (`NavigationConstants`, `CombatConstants`, …) into the unmanaged renderer to stringify this one field — the layer boundary is more valuable than the convenience. The `Channel` and `ChannelStatus` enum fields still render symbolically because they are framework enums. If named action-ID rendering is needed later, the cleanest path is to extend `BehaviorRegistry` (or a separate domain-action registry) with an `ActionId → name` lookup that the renderer queries optionally.

---

## 10. `BehaviorDefinition.HsmMetadata`

Per user decision: extend [BehaviorDefinition.cs](FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs) (or wherever `BehaviorDefinition` lives in the registry file) with:

```csharp
public MachineMetadata? HsmMetadata { get; init; }
```

Update [AiBehaviorFactory.cs](Hrot/Subsystems/Hrot.AI.Behaviors/AiBehaviorFactory.cs) so when constructing each HSM-backed behavior it captures the `MachineMetadata` produced by the FastHSM compiler. The factory currently calls `HsmEmitter.Emit(...)`; this becomes a call that captures both the blob and metadata (either a new overload of `Emit` returning both, or use `EmitWithDebug` and capture the metadata separately — `HsmEmitter.EmitWithDebug` already takes `MachineMetadata metadata` as an input parameter, so we need an `Emit` overload that *produces* metadata from the flattener output). Concretely, add `HsmEmitter.Emit(HsmFlatGraph flat, out MachineMetadata metadata)` (or similar) and route its output into `BehaviorDefinition.HsmMetadata`.

`BehaviorRegistry.TryGetDefinition` already returns the full `BehaviorDefinition`, so renderers/translators access `def.HsmMetadata` directly — no static accessor dictionary required.

---

## 11. JSON Dump Translators

### 11.1 `BTreeTraceWorkingMemoryTranslator` (`Hrot.SimHost.Serializers`)

Implements `IEntityScenarioTranslator`. Constructor `(BehaviorRegistry registry)`. `Inject` is intentionally empty (transient trace data is never loaded from a scenario file).

`Extract` walks the ring buffer in chronological order and emits a `JsonObject` with `RecordCount` and a `History` `JsonArray`. Each record carries:

- Always: `Timestamp`, `OpCode` (string), `InstanceId`
- Per opcode: `NodeIndex` + `NodeName` (resolved via `Blob.DebugMetadata[NodeIndex].Label`) + `Status` or `Duration` or `StackDepth` etc.
- For `ChannelMutated`: `Channel` (enum name), `ActiveAction` (numeric only — see §9 limitation note), `ChannelStatus` (enum name).

`IsExtractionSafe => true` (no GUID translation needed). `GetConsumedComponentsMask()` sets the bit for `BTreeTraceWorkingMemory1024` so `FdpAutoSerializer` does not also emit the raw bytes.

### 11.2 `HsmTraceWorkingMemoryTranslator`

Same shape, constructor `(BehaviorRegistry registry)`. Resolves `def.HsmMetadata` (the new field on `BehaviorDefinition`) and emits `StateIndex`/`StateName`, `SourceStateIndex/Name`, `TargetStateIndex/Name`, `TriggerEventId/Name`, `ActionId/Name`, `GuardId/Name`, `GuardResult` per opcode.

### 11.3 Factory Registration

In [HrotScenarioSerializerFactory.cs](Hrot/Subsystems/Hrot.SimHost/Serializers/HrotScenarioSerializerFactory.cs):

```csharp
.RegisterTranslator(new BTreeTraceWorkingMemoryTranslator(behaviorRegistry))
.RegisterTranslator(new HsmTraceWorkingMemoryTranslator(behaviorRegistry))
```

---

## 12. UI Context Menu Integration

### 12.1 Action ID

In [GlobalActionIds.cs](Hrot/Engine/Hrot.Common/Constants/GlobalActionIds.cs):

```csharp
public const int ToggleAiTrace = 251;
public const int ToggleAiTraceLog = 252;
```

### 12.2 Context Menu Item

Inside the UI bootstrapper that owns the entity inspector (likely [EditorSubsystem.cs:870](Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs), or wherever `RegisterContextMenuHandler` is currently called for entity-aware menus — verified in `CgfSubsystem`, `EditorSubsystem`, `SimHostVisualization`):

```csharp
_fdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
{
    if (!_world.IsAlive(entity)) return;
    if (!_world.HasComponent<BehaviorState>(entity)) return;

    builder.AddItem("Toggle AI Trace Buffer", () =>
        _interactionBus.Publish(new GlobalActionRequestedEvent
        {
            ActionId = GlobalActionIds.ToggleAiTrace,
            Target   = entity
        }));

    builder.AddItem("Toggle AI Trace Log", () =>
        _interactionBus.Publish(new GlobalActionRequestedEvent
        {
            ActionId = GlobalActionIds.ToggleAiTraceLog,
            Target   = entity
        }));
}));
```

### 12.3 Action Registry Wiring

```csharp
actionRegistry.Register(GlobalActionIds.ToggleAiTrace, (view, target) =>
    PublishToggleFlag(view, target, BehaviorDebugFlags.EnableTraceBuffer));

actionRegistry.Register(GlobalActionIds.ToggleAiTraceLog, (view, target) =>
    PublishToggleFlag(view, target, BehaviorDebugFlags.EmitToLog));

// helper:
static void PublishToggleFlag(ISimulationView view, Entity target, BehaviorDebugFlags flag)
{
    if (target == Entity.Null) return;
    var repo = (EntityRepository)view;
    if (!repo.HasComponent<BehaviorState>(target)) return;

    bool current = false;
    if (repo.HasComponent<DebugState>(target))
        current = (repo.GetComponentRO<DebugState>(target).Behavior & flag) != 0;

    bool next = !current;
    string nextStr = next ? "true" : "false";
    string patchJson = $$"""
    {
        "{{nameof(DebugState.Behavior)}}": {
            "{{flag}}": {{nextStr}}
        }
    }
    """;
    repo.Bus.PublishManaged(new PatchDebugStateCommand { Target = target, PatchJson = patchJson });
}
```

JSON property names are derived from `nameof(...)`. The flag enum value's `ToString()` returns the member name when the value is a single bit (e.g., `EnableTraceBuffer`).

---

## 13. System-Wide Auto-Enable

### 13.1 Move `GlobalDebugSettings` into `Hrot.Common`

Move [GlobalDebugSettings.cs](Hrot/Subsystems/Hrot.IG/Gizmos/GlobalDebugSettings.cs) from `Hrot.IG.Gizmos` to `Hrot.Common`. The SimHost / Brain node bootstrapping pipeline cannot reference `Hrot.IG`, so the relocation is a hard prerequisite for §13.2.

Add a new field:

```csharp
[MarshalAs(UnmanagedType.I1)]
public bool AutoEnableAiTracing;
```

### 13.2 `AiDiagnosticsTkbTranslator`

**Project location:** `Hrot.SimHost` (not `Fdp.Toolkits`). Reason: this translator reads `GlobalDebugSettings` which lives in `Hrot.Common`. `Fdp.Toolkits` does not reference `Hrot.Common`. `Hrot.SimHost` references both, so the translator lives there alongside `CognitiveComponentRegistry` and `HrotScenarioSerializerFactory`. The `ITkbEntityTranslator` interface itself comes from `Fdp.Core` so any project can implement it.

```csharp
public sealed class AiDiagnosticsTkbTranslator : ITkbEntityTranslator
{
    public IEnumerable<Type> GetConsumedDescriptors() => Array.Empty<Type>();  // observer pattern

    public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
    {
        if (!repo.HasSingletonUnmanaged<GlobalDebugSettings>()) return;
        if (!repo.GetSingletonUnmanaged<GlobalDebugSettings>().AutoEnableAiTracing) return;

        if (!template.TryGetDescriptor<BehaviorProfileDto>(out var profile)) return;

        byte tier = profile.BrainTier;

        if (tier == BehaviorConstants.BrainTierBTree
            && repo.IsComponentTypeRegistered<BTreeTraceWorkingMemory1024>()
            && !repo.HasComponent<BTreeTraceWorkingMemory1024>(entity))
        {
            repo.AddComponent(entity, new BTreeTraceWorkingMemory1024());
            ApplyDebugState(repo, entity);
        }
        else if (tier == BehaviorConstants.BrainTierHsm
            && repo.IsComponentTypeRegistered<HsmTraceWorkingMemory1024>()
            && !repo.HasComponent<HsmTraceWorkingMemory1024>(entity))
        {
            repo.AddComponent(entity, new HsmTraceWorkingMemory1024());
            ApplyDebugState(repo, entity);
        }
    }

    private static void ApplyDebugState(EntityRepository repo, Entity entity)
    {
        if (!repo.IsComponentTypeRegistered<DebugState>()) return;
        if (!repo.HasComponent<DebugState>(entity))
            repo.AddComponent(entity, new DebugState { Behavior = BehaviorDebugFlags.EnableTraceBuffer });
        else
        {
            ref var state = ref repo.GetComponentRW<DebugState>(entity);
            state.Behavior |= BehaviorDebugFlags.EnableTraceBuffer;
        }
    }
}
```

Register in [SimHostNodeBootstrapper.cs:133](Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs#L133) alongside `new BehaviorTkbTranslator()`. Because `GetConsumedDescriptors` returns empty, this translator runs as a pure observer alongside `BehaviorTkbTranslator` without competing for ownership of the `BehaviorProfileDto` descriptor.

---

## 14. BehaviorLog Integration

The `BehaviorLog` infrastructure is already implemented in [BehaviorLog.cs](Hrot/Subsystems/Hrot.AI.Behaviors/Logging/BehaviorLog.cs). What we add is automatic emission from the new trace records when `DebugState.Behavior & EmitToLog` is set.

### 14.1 Delta Extraction in `BTreeTickSystem`

```csharp
ushort startWritePos = tracePtr != null ? tracePtr->WritePos : (ushort)0;

// ---- execute the tree ----
var rootResult = def.BTreeInterpreter.Tick(ref blackboard, ref btState.State, ref ctx);

if (tracePtr != null
    && (dbg.Behavior & BehaviorDebugFlags.EmitToLog) != 0
    && BehaviorLog.IsTraceEnabled)
{
    int bytesWritten = tracePtr->WritePos - startWritePos;
    if (bytesWritten < 0)
        bytesWritten += BTreeTraceWorkingMemory1024.PayloadBytes; // wrap-around path
    int recordsWritten = bytesWritten / 16;
    if (recordsWritten > 0)
        EmitBTreeRecordsToLog(entity, repo, tracePtr, startWritePos, recordsWritten, def.BTreeInterpreter.Blob);
}
```

> **Why not the standard `(ushort)(end - start)` trick.** That trick only works when the buffer wraps at the integer bit-width boundary (65536). Because our ring wraps at `PayloadBytes = 1008`, a frame that crosses the wrap (e.g., `start = 1000`, `end = 8`) would compute `(ushort)(8 - 1000) = 64544` — instructing the log emitter to read ~4034 records from a 63-record buffer. The explicit `if (bytesWritten < 0) bytesWritten += PayloadBytes;` handles the single-wrap case correctly. (A single tick writing more than `PayloadBytes` records is impossible for a moderate BTree — the trees evaluate only a handful of nodes per tick — but if it ever happens, the emit path simply produces the last `≤63` records, which is the desired ring-buffer semantics anyway.)

`EmitBTreeRecordsToLog` decodes each record, builds a `string` (allocates only because we've already verified `IsTraceEnabled`), and dispatches to `BehaviorLog.Trace(entity, repo, message, "BTreeTrace")`. Inside the decoder, the byte offset for record `i` is `(startWritePos + i * 16) % PayloadBytes` to handle the wrap correctly.

### 14.2 Same for HSM

`HsmTickSystem` performs the equivalent delta extraction over `HsmTraceWorkingMemory1024.Buffer`. Identical wrap-handling: capture `startWritePos` before `HsmKernel.Update`, compute `bytesWritten = endWritePos - startWritePos; if (bytesWritten < 0) bytesWritten += PayloadBytes;` after the call, divide by 16 (strict stride from §5.2), then format each record using `def.HsmMetadata` for state/event/action names, and dispatch to `BehaviorLog.Trace`.

---

## 15. Layer & Project Impact Summary

| Concern | Project | New / Modified |
|---|---|---|
| `BTreeTraceOpCode`, `ITreeTracer` interface | `Fbt.Kernel` | New |
| `Interpreter` generic constraint + trace hook calls | `Fbt.Kernel` | Modified |
| `HsmTraceContext`, refactored kernel methods | `Fhsm.Kernel` | New + Modified |
| Delete managed `HsmTraceBuffer` + `SetTraceBuffer` | `Fhsm.Kernel` | Removed |
| `BTreeTraceWorkingMemory1024`, `HsmTraceWorkingMemory1024`, `BTreeTraceRecord`, write APIs | `Fdp.Toolkits` (Behavior/Diagnostics) | New |
| `BTreeContext` adds `ITreeTracer` impl + trace ptr | `Fdp.Toolkits` (Behavior) | Modified |
| `HsmKernelBridge` extends with `TraceContext*` | `Fdp.Toolkits` (Behavior/Systems) | Modified |
| `BTreeTickSystem`, `HsmTickSystem<T>` | `Fdp.Toolkits` | Modified |
| `BehaviorDefinition.HsmMetadata` field | `Fdp.Toolkits` (Behavior) | Modified |
| `AiBehaviorFactory` populates `HsmMetadata` | `Hrot.AI.Behaviors` | Modified |
| `TraceBufferLifecycleSystem` | `Fdp.Toolkits` (Behavior/Diagnostics) | New |
| `AiDiagnosticsTkbTranslator` | `Hrot.SimHost` (reads `Hrot.Common.GlobalDebugSettings`) | New |
| `DebugState`, `BehaviorDebugFlags`, `PatchDebugStateCommand`, `DebugStatePatchCompiler`, `DebugStatePatchSystem` | `Fdp.Toolkit.Behavior.Diagnostics` (FDP-level — see §6 note) | New |
| Move `GlobalDebugSettings` (add `AutoEnableAiTracing`) | `Hrot.IG.Gizmos` → `Hrot.Common` | Moved + Modified |
| `BTreeTraceWorkingMemoryRenderer`, `HsmTraceWorkingMemoryRenderer` | `Hrot.Presentation` | New |
| `BTreeTraceWorkingMemoryTranslator`, `HsmTraceWorkingMemoryTranslator` | `Hrot.SimHost.Serializers` | New |
| `HrotScenarioSerializerFactory` registers translators | `Hrot.SimHost.Serializers` | Modified |
| Context menu items + action registry wiring | `Hrot.Editor` / `Hrot.SimHost` (wherever `RegisterContextMenuHandler` is called) | Modified |
| `CognitiveComponentRegistry.RegisterAll` adds new components | `Hrot.SimHost` | Modified |
| `SimHostNodeBootstrapper` registers `AiDiagnosticsTkbTranslator` | `Hrot.SimHost` | Modified |
| `GlobalActionIds.ToggleAiTrace`, `ToggleAiTraceLog` | `Hrot.Common` | Modified |

### Project Dependency Considerations

- `Fdp.Toolkits` already references `Fbt.Kernel` and `Fhsm.Kernel`; no new project references needed inside the kernel projects.
- `Hrot.Common` already references `Fdp.Toolkits` — so it transitively gets `DebugState` and friends from `Fdp.Toolkit.Behavior.Diagnostics`. Nothing extra needed.
- `Hrot.SimHost` references `Hrot.Common` already.
- After moving `GlobalDebugSettings` to `Hrot.Common`, every file that referenced it via `Hrot.IG.Gizmos` must update its `using` directive.

### Out-of-Solution Test/Example Projects (Critical!)

The following projects are **not** included in `IOS-IG-SimHost.sln` but **will break** when the FastBTree/FastHSM kernel signatures change. They live in separate sub-solutions (`FDP/ExtDeps/FastBTree/FastBTree.sln`, `FDP/ExtDeps/FastHSM/FastHSM.sln`):

- `Fbt.Tests` — 33+ unit tests, several construct `BTreeContext` and run `Interpreter`.
- `Fbt.Benchmarks`, `Fbt.Examples.Console`, `Fbt.Examples.FluentBTree`, `Fbt.Examples.FluentBTree.Trees`, `Fbt.Demo.Visual.Tests`.
- `Fhsm.Tests` — 38 unit tests, including `Tooling/TraceTests.cs`, `Tooling/TraceSymbolicationTests.cs`, `Kernel/OrthogonalRegionTests.cs`, `Kernel/FailSafeTests.cs` — these directly exercise the (now deleted) `HsmTraceBuffer` and `HsmKernelCore.SetTraceBuffer`.
- `Fhsm.Benchmarks`, `Fhsm.Examples.Console`, `Fhsm.Demo.Visual`, `Fhsm.Demo.Visual.Tests`.

These projects **must be fixed** as a dedicated implementation task because:
1. CI for the FastBTree / FastHSM sub-repositories will turn red.
2. Future developers running `dotnet test` against the sub-solutions will see broken builds.
3. Trace-related tests need to be rewritten to construct an `HsmTraceContext` over a caller-owned buffer rather than calling the deleted `SetTraceBuffer` API.

This work is broken out into Phase 6 below.

---

## 16. Implementation Phases

Tasks are sequenced so each phase compiles and tests cleanly before the next begins.

### Phase 1 — Foundation: Memory & Kernel Contracts

Build the data structures and kernel-side contracts. No tick-system or UI wiring yet.

- T1.1 — `BTreeTraceOpCode` enum and `ITreeTracer` interface in `Fbt.Kernel`
- T1.2 — `HsmTraceContext` unmanaged struct in `Fhsm.Kernel.Data`
- T1.3 — `BTreeTraceRecord` + `BTreeTraceWorkingMemory1024` + write APIs in `Fdp.Toolkit.Behavior.Diagnostics`
- T1.4 — `HsmTraceWorkingMemory1024` in `Fdp.Toolkit.Behavior.Diagnostics`
- T1.5 — `BehaviorApplicationComponentIds` additions; `CognitiveComponentRegistry` registration

### Phase 2 — Kernel Refactoring

Rewire the kernels to consume the new contracts.

- T2.1 — `Interpreter<TB,TC>` adds `ITreeTracer` constraint; emit hooks in `ExecuteAction`, `ExecuteWait`, around scope changes
- T2.2 — `BTreeContext` implements `ITreeTracer`, holds `TraceBuffer*` and `_instanceId`
- T2.3 — Delete `HsmTraceBuffer` + `HsmKernelCore.SetTraceBuffer` + static `_traceBuffer`
- T2.4 — Add `HsmTraceContext* traceCtx` parameter throughout `HsmKernel` / `HsmKernelCore` execution pipeline; rewrite all `_traceBuffer.WriteX` sites
- T2.5 — `HsmKernelBridge` adds `HsmTraceContext* TraceContext` field

### Phase 3 — Generic Debug-State Plumbing

- T3.1 — Move `GlobalDebugSettings` into `Hrot.Common`, add `AutoEnableAiTracing`; fix all existing references
- T3.2 — Define `DebugState`, `BehaviorDebugFlags`, `HrotComponentIds.DebugState` constant; register
- T3.3 — Define `PatchDebugStateCommand` managed event; register
- T3.4 — Implement `DebugStatePatchCompiler` (expression-tree based)
- T3.5 — Implement `DebugStatePatchSystem` in `SystemPhase.Input`

### Phase 4 — Runtime Wiring

- T4.1 — `TraceBufferLifecycleSystem` (adds/removes 1KB buffers based on `EnableTraceBuffer`)
- T4.2 — `BTreeTickSystem` reads `DebugState`, stamps `LastInstanceId`, injects `TraceBuffer` pointer into context
- T4.3 — `HsmTickSystem<T>` builds `HsmTraceContext` over `HsmTraceWorkingMemory1024`, updates `InstanceFlags.DebugTrace`, passes pointer down
- T4.4 — Extend `BehaviorDefinition` with `HsmMetadata`; update `AiBehaviorFactory` to populate it via `HsmEmitter.Emit(..., out MachineMetadata metadata)`

### Phase 5 — UI, Translators, Auto-Enable

- T5.1 — `BTreeTraceWorkingMemoryRenderer` and `HsmTraceWorkingMemoryRenderer`
- T5.2 — `BTreeTraceWorkingMemoryTranslator` and `HsmTraceWorkingMemoryTranslator`; register in `HrotScenarioSerializerFactory`
- T5.3 — `GlobalActionIds.ToggleAiTrace` and `ToggleAiTraceLog`; register action handlers; add context menu items
- T5.4 — `AiDiagnosticsTkbTranslator` and register in `SimHostNodeBootstrapper`
- T5.5 — BehaviorLog emission: delta extraction + decoding in `BTreeTickSystem` and `HsmTickSystem`

### Phase 6 — Examples & Out-of-Solution Tests

- T6.1 — Fix `Fbt.Tests` to satisfy the new `ITreeTracer` constraint on `Interpreter<TB,TC>` (provide a NullTracer or update test context structs)
- T6.2 — Fix `Fbt.Benchmarks` and `Fbt.Examples.*` and `Fbt.Demo.Visual.Tests` similarly
- T6.3 — Fix `Fhsm.Tests` — rewrite `Tooling/TraceTests.cs` and `Tooling/TraceSymbolicationTests.cs` to construct `HsmTraceContext` over a caller-owned buffer; replace any `HsmKernelCore.SetTraceBuffer` calls; update `OrthogonalRegionTests`, `FailSafeTests` accordingly
- T6.4 — Fix `Fhsm.Benchmarks`, `Fhsm.Examples.Console`, `Fhsm.Demo.Visual`, `Fhsm.Demo.Visual.Tests`

> Important: developers must build and test the sub-solutions `FDP/ExtDeps/FastBTree/FastBTree.sln` and `FDP/ExtDeps/FastHSM/FastHSM.sln` explicitly, because they are excluded from the top-level `IOS-IG-SimHost.sln`. The main solution can compile green while these sub-projects are broken.

---

## 17. Success Conditions

The refactor is complete when all of the following are demonstrably true:

1. **Kernel purity.** `Fbt.Kernel` and `Fhsm.Kernel` contain zero references to ECS components, `Fdp.Toolkits`, or `Hrot.*`. All trace emission from inside `Fbt.Kernel` goes via the `ITreeTracer` interface; all trace emission from inside `Fhsm.Kernel` goes via the unmanaged `HsmTraceContext*`.

2. **Static state eradicated.** `HsmKernelCore._traceBuffer`, `HsmKernelCore.SetTraceBuffer`, and the managed `HsmTraceBuffer` class no longer exist anywhere in `Fhsm.Kernel`. A compile-time grep proves it.

3. **Zero-allocation hot path.** With `DebugState.Behavior & EnableTraceBuffer` set, the `BTreeTickSystem` / `HsmTickSystem` produce zero managed allocations per tick (excluding allocations already present before this change, and excluding the `EmitToLog` opt-in which is explicitly gated behind `BehaviorLog.IsTraceEnabled`).

4. **Component size compliance.** `BTreeTraceWorkingMemory1024` and `HsmTraceWorkingMemory1024` both have `sizeof(...) == 1024` and register successfully through `ComponentTypeRegistry` without exceeding `EntityCommandBuffer.MaxComponentSize`.

5. **Replay round-trip.** A scenario recorded with tracing enabled on one entity, replayed via the Flight Recorder, shows identical trace records in the ImGui inspector for the corresponding frame.

6. **Concurrent HSM tracing.** Two entities running different HSMs with `EnableTraceBuffer` set produce disjoint, non-interleaved trace buffers — provable by running the unit test suite with two parallel entities and comparing per-entity record sequences.

7. **Chunk version stability.** When `EnableTraceBuffer` is cleared, the tick systems do **not** call `GetComponentRW` on the trace buffer; the chunk's `LastChangeTick` does not advance from this component each frame.

8. **String-allocation segregation.** A profiler trace during simulation shows zero string allocations stemming from `BTreeTickSystem` or `HsmTickSystem` while tracing is enabled and `EmitToLog` is **disabled**. String formatting is only triggered when `EmitToLog` is on and `BehaviorLog.IsTraceEnabled` returns true, or inside ImGui renderers / JSON translators (UI thread only).

9. **UI round-trip.** Right-clicking an entity with a `BehaviorState` shows the "Toggle AI Trace Buffer" and "Toggle AI Trace Log" menu items. Clicking each toggles the corresponding `BehaviorDebugFlags` bit on `DebugState` via the JSON-patch pipeline; the inspector reflects the change next frame.

10. **TKB auto-enable.** Setting `GlobalDebugSettings.AutoEnableAiTracing = true` causes every newly-promoted AI-enabled entity to receive its appropriate trace buffer + `DebugState { EnableTraceBuffer }` before its first tick (verifiable with a unit test that promotes an entity and immediately inspects components without ticking).

11. **JSON dump integrity.** Copying an entity to clipboard via the inspector's dump action produces a JSON object containing a non-empty `BTreeTraceWorkingMemory1024.History` (or `HsmTraceWorkingMemory1024.History`) array with both numeric IDs and resolved human-readable names per record.

12. **Out-of-solution builds pass.** `dotnet build FDP/ExtDeps/FastBTree/FastBTree.sln` and `dotnet build FDP/ExtDeps/FastHSM/FastHSM.sln` both succeed; `dotnet test` on both succeeds.

---

## 18. Out-of-Scope (Explicit Non-Goals)

- The variable-length `FixedString32` error opcode discussed in the design talk is **not** included; the 1024-byte buffer forces all records to a fixed 16-byte stride.
- The managed-singleton snapshot scheme (`TraceBufferSnapshot` byte-array packing) discussed in the design talk is **not** included; the per-entity unmanaged component is sufficient for the few-entity debug use case.
- Fixing the `TraceSymbolicator.EventHandled` no-op bug is **not** included.
- Implementing `TimerSet` / `TimerFired` opcodes in HSM (currently unwired) is **not** included.
- A larger-buffer (`BTreeTraceWorkingMemoryLarge`) managed-component variant is **not** included.
- An automated source-generator that injects `WriteChannelMutated` calls based on `[WritesChannel]` attributes is **not** included; user code calls it manually.
- Named (string) rendering of `ChannelMutated.ActiveAction` is **not** included; the field is rendered as its raw `ushort` in both ImGui and JSON. See §9 for rationale.
