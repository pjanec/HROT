# Blueprint Subsystem — Debug Protocol Detailed Design — Inline Patches

> **Status:** Patches to `Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md` from architect's review.
> **Effect:** Two corrections (replace thread-blocking pause with soft-pause mechanism; constrain `PinValueChanged<T>` to unmanaged + use byte buffer to avoid boxing) plus six Q-13.x open-question resolutions.
> **Reads alongside:** the main Debug Protocol DD; sections marked here supersede their counterparts.

---

## Patch 1 — Soft pause via time-controller request (supersedes §1.6, §6.4, §6.5, §7.x)

### The problem

The main DD §6.4 specified `_resumeSignal.WaitOne()` to block the simulation thread when a breakpoint hits, claiming the editor UI would remain responsive. This is a fatal misunderstanding of the engine's frame loop.

FDP's `SubsystemOrchestrator` runs `Update()` (ECS simulation) and `DrawUI()` (ImGui rendering) **sequentially on the same main thread**. Blocking inside `BlueprintTickSystem.Execute` via `WaitOne()` means `DrawUI()` never runs — the OS window freezes, the editor never paints "Continue," the user can only force-kill the process.

### The fix — soft pause (Slice 1 design)

Breakpoint hits do **not** block the thread. The probe call captures state, requests a time-controller pause, and returns. The current tick finishes naturally. The engine halts time advancement on the *next* frame, so the user inspects state in the editor UI. Step operations advance the engine by exactly one tick.

This drops the "halt mid-tick before the node executes" property. In practice, a breakpoint pauses "after the breakpoint node executed once" rather than "right before." Stepping semantics adapt naturally — advance one tick = advance through one node's worth of execution.

### Updated §1.6 threading model

The Slice 1 protocol is single-threaded; main-thread-only is unchanged. The new rule: **probes never block the calling thread.** Probes are observation points that capture state and optionally request a frame-boundary pause. Simulation always returns from the probe call within nanoseconds, regardless of breakpoint state.

### Updated `IBlueprintDebugSession` — new dependency

The session needs a handle to a time-controller capable of `RequestPause()` / `RequestResume()` semantics:

```csharp
namespace Hrot.Blueprints.Core.Debug;

public interface IBlueprintTimeController
{
    /// <summary>Request that the engine halt time advancement at the next frame boundary.</summary>
    void RequestPause();

    /// <summary>Request that the engine resume normal time advancement.</summary>
    void RequestResume();

    /// <summary>Request that the engine advance exactly one tick, then re-pause.</summary>
    void RequestStepOneTick();

    bool IsPausedByDebugger { get; }
}
```

The production implementation wraps the engine's existing time-control mechanism (whatever name the engine uses — `EngineTimeController`, `SimulationDriver`, etc. — Editor DD will identify it). For tests, the `BlueprintTestFixture` already controls ticking explicitly, so it provides a `MockTimeController` that the session calls but doesn't actually need to gate on.

### Updated `BlueprintDebugSession` constructor

```csharp
public sealed class BlueprintDebugSession : IBlueprintDebugSession
{
    private readonly BlueprintRegistry _registry;
    private readonly ISimulationView _view;
    private readonly IBlueprintTimeController _timeController;
    // ... existing fields ...

    public BlueprintDebugSession(
        BlueprintRegistry registry,
        ISimulationView view,
        IBlueprintTimeController timeController)
    {
        _registry = registry;
        _view = view;
        _timeController = timeController;
    }
}
```

### Updated §6.4 — `HandleBreakpointHit` (replaces blocking version)

```csharp
private void HandleBreakpointHit(BreakpointHit hit)
{
    _pausedAt = hit.Breakpoint;
    _pausedOnEntity = hit.Self;
    _isPaused = true;

    // Capture state snapshot before returning — the slot bytes are stable
    // until the next tick, so we can read them after the probe returns,
    // but capturing now ensures the snapshot reflects the exact moment of hit.
    _pauseSnapshot = CaptureStateSnapshot(hit.Self, hit.Breakpoint.AssetId);

    // Request the engine pause at the next frame boundary.
    // The current tick continues to completion — probes for other entities
    // hitting the same breakpoint accumulate hit counts but don't request
    // additional pauses (already paused).
    _timeController.RequestPause();

    // Fire event for editor UI. The handler runs after the tick completes,
    // during the same frame's DrawUI phase. UI shows breakpoint hit.
    OnBreakpointHit?.Invoke(hit);
    OnSessionStateChanged?.Invoke();

    // Return immediately — no thread block.
}
```

### Updated §6.5 — `Continue` / `Step*` (no signal-based resume)

```csharp
public void Continue()
{
    _stepMode = StepMode.None;
    ClearPauseState();
    _timeController.RequestResume();
}

public void StepOver()
{
    // Capture step-from context BEFORE clearing pause
    _stepMode = StepMode.Over;
    _stepFromNodeIdString = _pausedAt!.NodeIdString;
    _stepFromAssetId = _pausedAt.AssetId;
    _stepFromEntity = _pausedOnEntity!.Value;
    _stepFromCallDepth = _currentCallDepth;
    _stepFromTick = _view.Tick;

    ClearPauseState();
    _timeController.RequestStepOneTick();
}

public void StepInto()
{
    _stepMode = StepMode.Into;
    _stepFromCallDepth = _currentCallDepth;
    _stepFromEntity = _pausedOnEntity!.Value;
    _stepFromTick = _view.Tick;

    ClearPauseState();
    _timeController.RequestStepOneTick();
}

public void StepOut()
{
    _stepMode = StepMode.Out;
    _stepFromCallDepth = _currentCallDepth;
    _stepFromEntity = _pausedOnEntity!.Value;
    _stepFromTick = _view.Tick;

    ClearPauseState();
    _timeController.RequestStepOneTick();
}

private void ClearPauseState()
{
    _isPaused = false;
    _pausedAt = null;
    _pausedOnEntity = null;
    _pauseSnapshot = null;
    OnSessionStateChanged?.Invoke();
}
```

### How step semantics adapt

The main DD §7 described stepping in terms of "probe call X fires; check conditions; pause again if matched." That still works under soft pause, but the timing shifts by one tick:

- **StepOver:** user clicks → `RequestStepOneTick` → engine ticks once → during that tick, every probe checks `_stepMode == Over` and decides whether to `RequestPause` again. If the step-condition matched a node in this tick, `_timeController.RequestPause()` halts the engine at the end of this tick.
- **StepInto:** same. The `PeerCallEnter` probe (per §7.4) checks depth-increase; if yes, sets `_pendingPauseAtNextProbe = true`; the *next* `NodeEnter` triggers the pause.
- **StepOut:** the engine ticks; if a probe sees we exited the call frame, request pause for the next frame.

Functionally identical to the blocking design; the timing is "after the matching tick completes" rather than "right at the matching node enter."

### Updated `OnNodeEnter` for step handling

```csharp
public void OnNodeEnter(Entity self, string nodeId)
{
    // Always update execution history (subject to history-buffer cap)
    RecordNodeHistory(self, nodeId);

    // Check breakpoints — increment hit count, possibly fire event + request pause
    HandleBreakpointMatchingForNode(self, nodeId);

    // Check step mode
    if (_stepMode != StepMode.None)
        HandleStepMatchingForNode(self, nodeId);
}

private void HandleStepMatchingForNode(Entity self, string nodeId)
{
    bool shouldPause = _stepMode switch
    {
        StepMode.Over => MatchesStepOver(self, nodeId),
        StepMode.Into => MatchesStepInto(self, nodeId),
        StepMode.Out  => MatchesStepOut(self, nodeId),
        _ => false,
    };

    if (shouldPause)
    {
        // Capture as pseudo-hit, same as a breakpoint
        var node = FindNodeAcrossAllMaps(nodeId);
        var pseudoBp = MakePseudoBreakpoint(self, nodeId, node);
        var hit = new BreakpointHit(pseudoBp, self, _view.Time, _view.Tick);

        _stepMode = StepMode.None;
        HandleBreakpointHit(hit);   // captures state, fires event, RequestPause
    }
}
```

### What about Option B (re-entrant render pump)?

The main DD section mentioned a hypothetical re-entrant approach. **Slice 1 does not implement Option B.** The complexity (re-entering `DrawUI` from inside a held call stack, handling editor commands that mutate state from inside the held tick, etc.) is high and the benefit (mid-tick precision) is marginal. Soft pause is sufficient.

Slice 2 may revisit if mid-tick precision becomes important.

### Updated §12.2 — Hit-on-node-entry test

The `SetTestModeNoBlock(true)` escape hatch is no longer needed; probes already don't block. Tests just assert the pause was *requested*:

```csharp
[Fact]
public void Breakpoint_FiresOnNodeEntry_RequestsPauseOncePerFrame()
{
    using var fixture = new BlueprintTestFixture();
    var timeController = fixture.TimeController;
    var session = new BlueprintDebugSession(fixture.Registry, fixture.View, timeController);
    DebugProbe.Sink = session;

    var asset = TestData.LoadAsset("HealthRegen");
    fixture.CompileAndLoad(asset, CompilerMode.Debug);
    session.RegisterDebugMap(asset.AssetId, LoadDebugMapFor(asset));

    var entity = fixture.World.CreateEntity();
    fixture.World.AddComponent(entity, new BlueprintBlackboard1024());
    fixture.AttachBlueprint(asset, entity);

    var beginPlayNode = LoadDebugMapFor(asset).Nodes.First(n => n.NodeKind == "EventEntry");
    var bpId = session.SetBreakpoint(asset.AssetId, beginPlayNode.GraphId, beginPlayNode.NodeId);

    BreakpointHit? lastHit = null;
    session.OnBreakpointHit += hit => lastHit = hit;

    fixture.TickFrame(0.016f);

    Assert.NotNull(lastHit);
    Assert.True(timeController.PauseWasRequested);   // session asked engine to pause
    Assert.Equal(1, timeController.PauseRequestCount);   // only one request even if multiple entities
}
```

The `MockTimeController` exposes `PauseWasRequested` and `PauseRequestCount` for verification.

### Why this is a strict improvement

| Before | After |
|---|---|
| `WaitOne()` deadlocks editor UI | Probes return in nanoseconds; UI runs normally |
| Pause mechanism brittle on single-threaded engine | Pause is just "stop advancing time" — robust on any threading model |
| Tests need `SetTestModeNoBlock(true)` escape hatch | Tests don't need any escape hatch |
| Step semantics: probe-call precise | Step semantics: tick-boundary precise (one tick off) |

The "one tick off" is the only behavioral change. In practice, users notice this only when watching a specific frame's state evolution. For everyday "let me see what this Blueprint is doing" debugging, it's identical.

---

## Patch 2 — Constrain `PinValueChanged<T>` to unmanaged, write into byte buffer (supersedes §8.3)

### The problem

§8.3 used a generic `<T>` signature to keep boxing off the call site, then immediately boxed inside the sink: `watch.LastValue = (object)value;`. Net effect: one heap allocation per pin-value-changed probe.

For Trace mode, this fires per data-pin-read per entity per frame. A scenario with 10 traced Blueprints × 5 pin reads each × 100 entities = 5,000 allocations per frame. At 60 Hz that's 300,000 allocations/sec — guaranteed GC spike, ruining the timing profile the user is trying to trace.

### The fix

`PinValueChanged<T>` is constrained to `unmanaged`. The watch's stored "last value" is a fixed byte buffer; write into it with `Unsafe.WriteUnaligned`; the UI decodes on demand via the same `MarshalFromBytes` already specified in §8.5 for state inspection.

### Updated `IBlueprintProbeSink`

```csharp
public interface IBlueprintProbeSink
{
    void OnNodeEnter(Entity self, string nodeId);
    void OnPinValueChanged<T>(Entity self, string pinId, T value) where T : unmanaged;
    void OnPeerCallEnter(Entity self, string peerAssetIdString, string methodName);
    void OnPeerCallExit(Entity self, string peerAssetIdString, string methodName);
}

public static class DebugProbe
{
    public static void PinValueChanged<T>(Entity self, string pinId, T value)
        where T : unmanaged
        => Sink.OnPinValueChanged(self, pinId, value);
}
```

### Updated `Watch` record

```csharp
public sealed class Watch
{
    public WatchId Id { get; init; }
    public Guid AssetId { get; init; }
    public Guid GraphId { get; init; }
    public Guid PinId { get; init; }
    public string PinIdString { get; private set; }
    public string DisplayName { get; init; } = "";
    public Type ExpectedType { get; init; } = typeof(int);
    public int ExpectedSizeBytes { get; init; }

    // 64 bytes of inline storage. Sufficient for any unmanaged scalar or small struct
    // the compiler emits (Vector3 = 12 bytes; Entity = 8 bytes; Matrix4x4 = 64 bytes).
    // Allocated once at watch construction, reused for all updates — zero per-update alloc.
    private readonly byte[] _valueBuffer = new byte[64];
    public ReadOnlySpan<byte> LastValueBytes => _valueBuffer.AsSpan(0, ExpectedSizeBytes);

    public Entity LastUpdateEntity { get; private set; }
    public uint LastUpdateTick { get; private set; }
    public int UpdateCount { get; private set; }
    public bool HasEverBeenWritten { get; private set; }
    public bool IsStale { get; private set; }

    internal void WriteValue<T>(T value, Entity self, uint tick) where T : unmanaged
    {
        if (Unsafe.SizeOf<T>() > _valueBuffer.Length)
            throw new InvalidOperationException(
                $"Watch buffer too small for type {typeof(T).Name} " +
                $"({Unsafe.SizeOf<T>()} bytes > {_valueBuffer.Length}).");
        Unsafe.WriteUnaligned(ref _valueBuffer[0], value);
        LastUpdateEntity = self;
        LastUpdateTick = tick;
        UpdateCount++;
        HasEverBeenWritten = true;
    }

    internal void UpdatePinIdString(string s) => PinIdString = s;
    internal void MarkStale(bool b) => IsStale = b;
}
```

### Updated `OnPinValueChanged` in the session

```csharp
public void OnPinValueChanged<T>(Entity self, string pinId, T value) where T : unmanaged
{
    if (!_watchesByPinIdString.TryGetValue(pinId, out var watch)) return;

    watch.WriteValue(value, self, _view.Tick);

    // No allocation — the event payload's "value" field is a ReadOnlySpan<byte>,
    // not a boxed object. Subscribers decode on demand.
    var evt = new PinValueChanged(
        Self: self,
        AssetId: watch.AssetId,
        PinId: watch.PinId,
        ValueBytes: watch.LastValueBytes.ToArray(),  // single allocation only when listeners present
        ValueType: watch.ExpectedType,
        SimulationTime: _view.Time);
    OnPinValueChanged?.Invoke(evt);
}
```

The `ToArray()` on the byte span allocates when an event listener exists. If no listener is attached (which is the common case in headless tests or production debug-without-watch-UI), the event isn't fired. The truly hot path (probe fires, no listener) does zero allocation.

For Slice 2 we can switch to a `ref struct PinValueChangedRef` to eliminate the allocation even when listeners exist, but that requires the event to be a delegate-of-ref-struct, which complicates editor wiring. Slice 1 accepts the allocation only when the editor is actively listening.

### Updated `PinValueChanged` event record

```csharp
public sealed record PinValueChanged(
    Entity Self,
    Guid AssetId,
    Guid PinId,
    byte[] ValueBytes,           // copied from watch's buffer at firing time
    Type ValueType,
    float SimulationTime);
```

UI consumers decode via the same `MarshalFromBytes` from §8.5:

```csharp
// In editor UI handler:
session.OnPinValueChanged += evt =>
{
    object decoded = MarshalFromBytes(evt.ValueBytes, evt.ValueType);
    UpdateWatchPanelDisplay(evt.PinId, decoded);   // formatting/display only
};
```

The `MarshalFromBytes` allocates when boxing primitives (e.g., `MemoryMarshal.Read<int>` returns an `int`, but storing it as `object` boxes). This is acceptable — it's behind the UI boundary, not in the hot path. Slice 1 ships with this cost; Slice 2 could use type-specific display paths that keep values unboxed all the way to the ImGui label.

### What types does this cover?

The unmanaged constraint covers all variable/pin types the compiler currently emits:
- All numeric primitives (`int`, `long`, `float`, `double`, `byte`, etc.)
- `bool` (1 byte unmanaged)
- `Vector3`, `Vector2`, `Quaternion`, `Matrix4x4`
- `Entity` (8 bytes — Index + Generation)
- `BlueprintLatentCursor` (16 bytes)
- Any user-defined unmanaged struct used as a Blueprint variable

What it doesn't cover: strings, managed arrays, reference types. Per Compiler DD §3.1 and Runtime DD §4.8, Blueprint variables cannot be managed types anyway (the ECS chunk layout requires unmanaged). So this constraint is consistent with what the compiler can emit.

### Buffer-size fit check

The 64-byte buffer fits everything in the catalog:

| Type | Size |
|---|---|
| `int`, `float`, `bool` | 4 or fewer bytes |
| `long`, `double` | 8 bytes |
| `Vector2`, `Vector3`, `Vector4` | 8/12/16 bytes |
| `Quaternion` | 16 bytes |
| `Entity` | 8 bytes |
| `Matrix4x4` | 64 bytes (exact fit) |
| `BlueprintLatentCursor` | 16 bytes |

If a Slice 2 type exceeds 64 bytes (uncommon for game state — anything bigger usually lives in component fields), the constructor's size check throws. The buffer can be bumped to a larger constant or allocated lazily per-watch based on the expected type's size.

### Per-frame allocation count after Patch 2

Per Compiler DD's allocation-test framework:

- **Release mode:** 0 allocs (no probes).
- **Debug mode, no listeners:** 0 allocs (probes call into session, session updates internal state in-place).
- **Debug mode with listeners:** 1 alloc per fired event (the byte-array copy in `PinValueChanged` record construction).
- **Trace mode, no watches set:** 0 allocs (probe-lookup miss returns immediately).
- **Trace mode with watches set, no listeners:** 0 allocs (buffer write only).
- **Trace mode with watches set + listeners:** 1 alloc per watched pin per tick.

The "no listeners" case is what tests assert. The CI gate from Q-13.5 catches the listener case if it ever fires unexpectedly in a baseline benchmark.

---

## Resolutions to §13 open questions (all six confirmed)

The architect confirmed all six Slice 1 decisions as proposed. Locked:

- **Q-13.1 — Single-debugger limitation:** Locked. One `DebugProbe.Sink` at a time. `MultiplexingProbeSink` deferred to Slice 2.
- **Q-13.2 — Time semantics:** Locked. `SimulationTime` only on `BreakpointHit` records. Wall-clock added by editor handlers when needed.
- **Q-13.3 — Pause-on-error:** Locked. Protocol doesn't intercept exceptions. They propagate to engine crash handler or attached IDE.
- **Q-13.4 — Watch persistence:** Locked. In-memory only for Slice 1.
- **Q-13.5 — Performance budget CI gate:** Locked. Probe-overhead benchmarks added to M12 acceptance gate; budget breach fails CI.
- **Q-13.6 — Debug map storage in production:** Locked. `.dbgmap.json` ships alongside production DLLs even though Release-mode generated code emits no probes — the structural metadata is needed for Live ExCon diagnostics and editor inspection.

---

## Patches summary

| Patch | Affects | Change |
|---|---|---|
| 1: Soft pause via time controller | §1.6, §6.4, §6.5, §7.x, §12.2 | Remove `WaitOne()`. Probes return immediately. `IBlueprintTimeController` injected into session. `RequestPause()` halts at next frame boundary. Step semantics shift one tick later but remain functionally equivalent. |
| 2: Unmanaged-only `PinValueChanged`, byte buffer | §8.3, §8.4, watch storage shape | `where T : unmanaged` constraint. Watch holds a 64-byte fixed buffer. `Unsafe.WriteUnaligned` for storage. UI decodes via existing `MarshalFromBytes`. Zero allocation on probe path when no listeners attached. |
| Q-13.1 through Q-13.6 | §13 open questions | All Slice 1 decisions locked as proposed. |

### Effect on implementation

Slice 1 implementation simplifies meaningfully:

- **No thread-blocking primitives.** No `ManualResetEventSlim`, no signal-based resume logic.
- **No `SetTestModeNoBlock` escape hatch.** Tests use the production path; just inject a `MockTimeController`.
- **Cleaner editor wiring.** `BlueprintDebugSession` constructor takes `(Registry, View, TimeController)`; no special "pause via signal" plumbing in editor windows.
- **Per-frame allocation budget tightens.** Trace-mode probe path is provably zero-allocation when no listeners are attached. Listener-attached case allocates one byte-array copy per fired event — still bounded, no GC spikes.
- **Step semantics are tick-boundary, not probe-call.** Cleaner mental model for users (each step = one tick = one node's execution).

### Effect on Editor DD

Editor DD needs to:
1. Identify and inject the engine's actual time-controller class as `IBlueprintTimeController`.
2. Subscribe to `session.OnBreakpointHit` and `session.OnSessionStateChanged` in its UI thread (which is the same main thread, but in the `DrawUI()` phase of the next frame after the pause request).
3. Render the breakpoint-hit overlay, watch panel, and step controls during `DrawUI()`.
4. Enforce the §11.4 rule from Hot Reload DD: hot reload UI greyed out while `session.IsPaused == true`.

These are all natural UI concerns owned by the Editor DD; no surprises.

### Effect on Test Harness DD

The fixture gains a `MockTimeController` field exposing pause-request counts for assertions. The fixture's `TickFrame(dt)` ignores pause requests by default (tests want explicit control), but a method `fixture.TickFrameRespectingPause(dt)` is added for tests that want to verify the pause-then-resume cycle end-to-end.

These additions are minor. No structural patch to the Test Harness DD needed — the fixture's `BlueprintTimeController` interface is just one more mock alongside `MockSimulationView` and `MockEntityCommandBuffer`.

---

*End of Debug Protocol DD inline patches. The Debug Protocol DD plus this patches doc is the implementable specification for M12. Next major document: Editor Detailed Design.*
