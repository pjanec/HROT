# BATCH-03 Report

**Batch:** BATCH-03  
**Tasks:** TASK-S1-08, TASK-S1-11, TASK-S1-12, TASK-S1-13  
**Status:** COMPLETE

---

## Summary

Four pure C# service types were added to `Hrot.Editor.AiShared` with full unit tests.

### TASK-S1-08 -- IGSelectionBridge + CallbackSelectionBridge

Two files added to `Hrot/Editor/Hrot.Editor.AiShared/Selection/`:

- `IGSelectionBridge.cs` -- interface with `IsConnected`, `Connect(EditorSelectionStore)`, `Disconnect()`, extends `IDisposable`
- `CallbackSelectionBridge.cs` -- callback-factory-based implementation; factory receives `Action<Entity?>` and returns `IDisposable`; `Connect` stores the factory result; `Disconnect` disposes it

### TASK-S1-11 -- IAiTraceObserver + AiTracerCoordinator

Three files added to `Hrot/Editor/Hrot.Editor.AiShared/Debug/`:

- `TraceLevel.cs` -- `[Flags]` enum: `None`, `Lifecycle`, `Decisions`, `Values`, `Async`, `Errors`, `All`
- `IAiTraceObserver.cs` -- passive subscriber interface: `BeginObservingAsset`, `EndObservingAsset`, `GetActiveEntities`
- `AiTracerCoordinator.cs` -- reference-counted tracker; effective level is bitwise OR of all observer levels; `BeginObservingAssetImpl` is called only on first observer; `EndObservingAssetImpl` on refcount reaching zero

### TASK-S1-12 -- IAiDebugSession + AiDebugSessionBase + IDebugSessionRegistry + DebugSessionRegistry

Six files added to `Hrot/Editor/Hrot.Editor.AiShared/Debug/`:

- `BreakpointId.cs` -- `readonly record struct BreakpointId(int Value)`
- `Breakpoint.cs` -- `sealed record Breakpoint(BreakpointId, Guid, Guid, int, bool, string)`
- `IAiDebugSession.cs` -- extends `IAiTraceObserver`; breakpoint CRUD, pause/step controls, `OnSessionStateChanged` event
- `AiDebugSessionBase.cs` -- abstract base; breakpoint list with auto-incrementing ids starting at 1; `Continue`/`Pause` guarded against double-fire; `IAiTraceObserver` delegates to a `AiTracerCoordinator`; abstract step impl hooks
- `IDebugSessionRegistry.cs` -- interface: `TryAcquireSession<T>`, `ReleaseSession`, `RegisterObserver<T>`, `ActiveObservers`, `ActiveSession`, `Changed`
- `DebugSessionRegistry.cs` -- factory-pattern implementation; `RegisterSessionFactory<TSession>(Func<TSession>)` stores per-type factory; `TryAcquireSession` enforces exclusivity (only one active session at a time); `RegisterObserver` returns a remove-token `IDisposable`

### TASK-S1-13 -- HotReloadTier + HotReloadClassifier + HotReloadStatus

Three files added to `Hrot/Editor/Hrot.Editor.AiShared/HotReload/`:

- `HotReloadTier.cs` -- enum: `Cosmetic=0`, `Soft=1`, `Hard=2`
- `HotReloadClassifier.cs` -- static: `Classify` (structure hash change -> Hard, param hash change -> Soft, else Cosmetic); `MostImpactful` via `Math.Max` on enum int values
- `HotReloadStatus.cs` -- `sealed record HotReloadStatus(HotReloadTier Tier, int LiveInstanceCount)` with computed property `RequiresConfirmation => Tier == Hard && LiveInstanceCount > 0`

---

## Test Count

| Category                      | Count |
|-------------------------------|-------|
| Existing tests (BATCH-02)     | 65    |
| TASK-S1-08 CallbackSelectionBridge | 6 |
| TASK-S1-11 AiTracerCoordinator     | 12 |
| TASK-S1-12 AiDebugSessionBase      | 7  |
| TASK-S1-12 DebugSessionRegistry    | 9  |
| TASK-S1-13 HotReloadClassifier     | 11 |
| **Total**                     | **110** |

All 110 tests pass.

---

## Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Developer Insights

**1. AiTracerCoordinator: level escalation**

`AddObserver` handles level escalation by ORing the new level into the stored effective level on every call after the first:

```csharp
if (_observed.TryGetValue(assetId, out var existing))
    _observed[assetId] = (existing.RefCount + 1, existing.Level | level);
```

So if observer A requests `Lifecycle` and observer B later requests `Decisions`, the effective level becomes `Lifecycle | Decisions`. `BeginObservingAssetImpl` is not called again for subsequent adds -- it is called only when the refcount was 0 (the entry did not exist yet).

**2. DebugSessionRegistry: TryAcquireSession without knowing the concrete type at construction**

`RegisterSessionFactory<TSession>(Func<TSession> factory)` stores a `Func<IAiDebugSession>` in a `Dictionary<Type, Func<IAiDebugSession>>` keyed by `typeof(TSession)`. At acquire time, `TryAcquireSession<TSession>` looks up `typeof(TSession)`, calls the stored factory, and casts the result to `TSession`. The registry never needs to know the concrete type; only the caller (who registered the factory) does. This lets tests inject stub sessions without the registry knowing anything about them.

**3. AiDebugSessionBase.Detach(): what gets cleared**

In order:
1. `IsAttached` set to `false`
2. The internal breakpoint list (`_breakpoints`) is cleared directly (no event from the clear, avoiding a double-fire)
3. `OnDetachImpl()` is called (virtual no-op in base, overridable in subsystems)
4. `OnSessionStateChanged` is raised once

`IsPaused`, `PausedAt`, and `PausedOnEntity` are NOT reset by `Detach` -- the spec does not require it, and the debug UI reads those fields to show the last pause location even after detach.

**4. HotReloadClassifier.MostImpactful: symmetry**

`MostImpactful(a, b) == MostImpactful(b, a)` because `Math.Max(int, int)` is commutative. The tests verify symmetry explicitly for the two non-trivial cases (`Hard`/`Soft` and `Soft`/`Cosmetic`) by calling both orderings inside the same test fact.

**5. Design decisions beyond the spec**

- `HotReloadStatus.RequiresConfirmation` was made a computed property rather than a positional constructor parameter. The spec showed it in the record parameter list with a comment "true only for Hard with LiveInstanceCount > 0", which reads as a derivation rule. A computed property avoids inconsistency (a caller could pass `RequiresConfirmation=true` with `Tier=Soft`) and produces more meaningful tests.
- `AiDebugSessionBase.Detach()` clears `_breakpoints` directly instead of calling the public `ClearAllBreakpoints()` method, to prevent the event from firing twice during a single Detach call.

---

## Deviations from Spec

- `HotReloadStatus`: the spec listed `bool RequiresConfirmation` as a positional record parameter; implemented as a computed property instead (see Developer Insight 5 above). The observable API is identical for read access and produces more meaningful test assertions.
- No other deviations.
