# BATCH-03: Shared Infrastructure Services (Part 2 — Debug & Hot-Reload)

**Batch Number:** BATCH-03  
**Tasks:** TASK-S1-08, TASK-S1-11, TASK-S1-12, TASK-S1-13  
**Phase:** Phase 1 — Shared infrastructure foundation (second half, services only)  
**Estimated Effort:** 10-14 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-02 (DONE — `Hrot.Editor.AiShared` exists)

---

## Mandatory Workflow

**Complete tasks in sequence, tests passing before moving on:**

1. **TASK-S1-08:** `IGSelectionBridge` interface + callback-based impl → tests pass
2. **TASK-S1-11:** `IAiTraceObserver` + `AiTracerCoordinator` → tests pass
3. **TASK-S1-12:** `IAiDebugSession` + `AiDebugSessionBase` + `IDebugSessionRegistry` → tests pass
4. **TASK-S1-13:** `HotReloadClassifier` + `HotReloadStatusIndicator` → tests pass
5. **Final:** ALL 65 existing tests still pass; new tests all pass; main solution builds

Do NOT stop and ask for permission. Complete the entire batch and submit the report.

---

## Onboarding & Workflow

### What you're building

Pure service-layer additions to `Hrot.Editor.AiShared`. No UI (no ImGui), no DDS, no Raylib. All four tasks extend the existing library in `Hrot/Editor/Hrot.Editor.AiShared/` and tests in `Hrot/Editor/Hrot.Editor.AiShared.Tests/`.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Code Standards:** `.dev/.guides/CODE-STANDARDS.md`
3. **Task Definitions:** `.dev/blueprints-2/TASK-DETAIL.md` — Phase 1, TASK-S1-08, S1-11, S1-12, S1-13
4. **Design Spec:** `.dev/blueprints-2/AI_Editor_Shared_Infrastructure.md`
   - §5.3: DDS selection bridge
   - §11: Trace observers vs. debug sessions
   - §12: `IAiDebugSession` hierarchy (FULL — read the complete section)
   - §17: Hot-reload classification

### Existing Code to Review First

Before writing any code, read these BATCH-02 files to understand what exists:

```
Hrot/Editor/Hrot.Editor.AiShared/Selection/EditorSelectionStore.cs
Hrot/Editor/Hrot.Editor.AiShared/Catalog/IAssetCatalog.cs
Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetIdHash.cs
FDP/Engine/Fdp.Core/Entity.cs   (for Entity type)
```

### Build Commands

```powershell
# Build and test the shared project
dotnet test "Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj"

# Build main solution (run at end to verify no regressions)
dotnet build IOS-IG-SimHost.sln
```

### Report Submission

**When done, submit report to:**  
`.dev/blueprints-2/reports/BATCH-03-REPORT.md`

---

## Project Context

The `Hrot.Editor.AiShared.csproj` already exists and has one dependency: `Fdp.Core`. Do NOT add any new dependencies to the project file (no DDS, no ImGui, no Presentation). Everything in this batch must be pure net8.0 C#.

---

## Tasks

### Task 1: TASK-S1-08 — `IGSelectionBridge` (DDS adapter interface)

**Spec:** Shared infra §5.3.

The DDS bridge lets external tools (IG map view) drive `EditorSelectionStore.SelectedEntity` by publishing `SelectionChangedEvent` on the DDS bus. In `Hrot.Editor.AiShared`, define only the interface and a callback-based implementation. The actual DDS subscription wiring happens in a higher layer (the `Hrot.Editor` host); this library stays free of DDS dependencies.

**Files to create:**
- `Hrot/Editor/Hrot.Editor.AiShared/Selection/IGSelectionBridge.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Selection/CallbackSelectionBridge.cs`

**Required public surface:**

```csharp
// IGSelectionBridge.cs
namespace Hrot.Editor.AiShared.Selection;

/// <summary>
/// Adapts an external selection-changed notification source (e.g. DDS SelectionChangedEvent)
/// to the EditorSelectionStore. The bridge updates SelectedEntity on the store when
/// an external selection arrives.
/// </summary>
public interface IGSelectionBridge : IDisposable
{
    bool IsConnected { get; }
    void Connect(EditorSelectionStore store);
    void Disconnect();
}
```

```csharp
// CallbackSelectionBridge.cs
namespace Hrot.Editor.AiShared.Selection;

/// <summary>
/// IGSelectionBridge implementation that accepts a subscription factory callback.
/// The factory receives an Action<Entity?> and returns an IDisposable token.
/// This keeps the shared library free of DDS or network dependencies.
/// </summary>
public sealed class CallbackSelectionBridge : IGSelectionBridge
{
    private readonly Func<Action<Entity?>, IDisposable> _subscribeFactory;
    private IDisposable? _subscription;
    private EditorSelectionStore? _store;

    public CallbackSelectionBridge(Func<Action<Entity?>, IDisposable> subscribeFactory);

    public bool IsConnected => _subscription is not null;

    public void Connect(EditorSelectionStore store);
    // stores _store, calls _subscribeFactory with the handler Action<Entity?> that writes
    // store.SelectedEntity = entity; sets _subscription to the returned token

    public void Disconnect();
    // disposes _subscription, nulls _store and _subscription

    public void Dispose() => Disconnect();
}
```

**Tests (put in `Selection/CallbackSelectionBridgeTests.cs`) — minimum 6:**

- `IsConnected_IsFalse_BeforeConnect`
- `IsConnected_IsTrue_AfterConnect`
- `IsConnected_IsFalse_AfterDisconnect`
- `Connect_WhenExternalFiresEntity_StoreUpdated`: create a `CallbackSelectionBridge` with a factory that captures the callback, call `Connect`, invoke the captured callback with a test entity, assert `store.SelectedEntity == entity`
- `Connect_WhenExternalFiresNull_StoreEntitySetToNull`
- `Disconnect_DisposesSubscription`

---

### Task 2: TASK-S1-11 — `IAiTraceObserver` + `AiTracerCoordinator`

**Spec:** Shared infra §11.1, §11.4.

**Files to create:**
- `Hrot/Editor/Hrot.Editor.AiShared/Debug/TraceLevel.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Debug/IAiTraceObserver.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Debug/AiTracerCoordinator.cs`

**Required public surface:**

```csharp
// TraceLevel.cs
namespace Hrot.Editor.AiShared.Debug;

[Flags]
public enum TraceLevel
{
    None       = 0,
    Lifecycle  = 1 << 0,
    Decisions  = 1 << 1,
    Values     = 1 << 2,
    Async      = 1 << 3,
    Errors     = 1 << 4,
    All        = Lifecycle | Decisions | Values | Async | Errors,
}
```

```csharp
// IAiTraceObserver.cs
namespace Hrot.Editor.AiShared.Debug;

/// <summary>
/// Passive subscriber to tracer output. Multiple observers may be attached
/// per subsystem simultaneously. Does not control execution.
/// </summary>
public interface IAiTraceObserver
{
    /// <summary>
    /// Begins emitting trace records for all entities running this asset.
    /// Reference-counted internally so multiple observers can request the same asset.
    /// </summary>
    void BeginObservingAsset(Guid assetId, TraceLevel level);
    void EndObservingAsset(Guid assetId);

    /// <summary>Returns all entities currently running this asset.</summary>
    IReadOnlyList<Entity> GetActiveEntities(Guid assetId);
}
```

```csharp
// AiTracerCoordinator.cs
namespace Hrot.Editor.AiShared.Debug;

/// <summary>
/// Reference-counted asset observation tracker.
/// When multiple observers request the same asset, the effective TraceLevel
/// is the bitwise OR (union) of all requested levels.
/// On refcount reaching zero, EndObservingAssetImpl is called.
/// Subsystem coordinators derive and override BeginObservingAssetImpl/EndObservingAssetImpl
/// to talk to their kernel.
/// </summary>
public class AiTracerCoordinator
{
    // Key: assetId. Value: (refcount, effective TraceLevel)
    private readonly Dictionary<Guid, (int RefCount, TraceLevel Level)> _observed = new();

    /// <summary>Increments refcount for the asset. Calls BeginObservingAssetImpl on first call.</summary>
    public void AddObserver(Guid assetId, TraceLevel level);

    /// <summary>Decrements refcount. Calls EndObservingAssetImpl on reaching zero.</summary>
    public void RemoveObserver(Guid assetId);

    /// <summary>Effective TraceLevel for the asset (union of all observer levels). Zero if not observed.</summary>
    public TraceLevel GetEffectiveLevel(Guid assetId);

    /// <summary>True if at least one observer is watching this asset.</summary>
    public bool IsObserving(Guid assetId);

    /// <summary>
    /// Called on first observer for an asset.
    /// Override in subsystem-specific subclasses to set DebugState.Flags on matching entities.
    /// Default: no-op (test-friendly).
    /// </summary>
    protected virtual void BeginObservingAssetImpl(Guid assetId, TraceLevel level) { }

    /// <summary>Called when refcount reaches zero. Override to clear DebugState.Flags.</summary>
    protected virtual void EndObservingAssetImpl(Guid assetId) { }
}
```

**Rules:**
- `AddObserver`: increment refcount. If this is the first observer (count was 0), call `BeginObservingAssetImpl`. If count was already >0, update the effective level to `existing | level` and call `BeginObservingAssetImpl` again ONLY if the new union level is strictly wider than the previous (i.e., new bits added). Actually, simplify: always call `BeginObservingAssetImpl` when refcount was 0 (first add). When adding a second observer, just update the effective level — no additional impl call (to avoid redundant work on subsystem coordinators). Track: `(refcount, level)` in the dictionary.
- `RemoveObserver`: decrement. On reaching zero, call `EndObservingAssetImpl` and remove the entry. If `RemoveObserver` is called for an asset that isn't being observed, no-op (defensive).
- `GetEffectiveLevel`: returns `level` from the dictionary, or `TraceLevel.None` if not in dictionary.

**Tests (put in `Debug/AiTracerCoordinatorTests.cs`) — minimum 12:**

- `AddObserver_FirstObserver_RefCountIsOne`
- `AddObserver_SecondObserver_RefCountIsTwo`
- `RemoveObserver_OnZeroRefCount_CallsEndImpl`
- `RemoveObserver_WhenTwoObservers_RefCountIsOne_EndImplNotCalled`
- `RemoveObserver_UnobservedAsset_IsNoOp`
- `GetEffectiveLevel_ReturnsNone_WhenNotObserved`
- `GetEffectiveLevel_ReturnsSingleLevel_WhenOneObserver`
- `GetEffectiveLevel_ReturnsUnion_WhenMultipleObservers`: add Lifecycle + Decisions; effective = Lifecycle | Decisions
- `IsObserving_ReturnsFalse_WhenNotObserved`
- `IsObserving_ReturnsTrue_WhenObserved`
- `AddObserver_ThenRemoveTwice_SecondRemoveIsNoOp`
- `BeginObservingAssetImpl_CalledOnFirstAdd_NotOnSubsequentAdds`: use a subclass that counts BeginImpl calls; add 3 observers → BeginImpl called exactly once

---

### Task 3: TASK-S1-12 — `IAiDebugSession` + `AiDebugSessionBase` + `IDebugSessionRegistry`

**Spec:** Shared infra §11.2, §12.1 (FULL SECTION — read carefully).

**Files to create:**
- `Hrot/Editor/Hrot.Editor.AiShared/Debug/BreakpointId.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Debug/Breakpoint.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Debug/IAiDebugSession.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Debug/AiDebugSessionBase.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Debug/IDebugSessionRegistry.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Debug/DebugSessionRegistry.cs`

**Required public surface (verbatim from spec §12.1):**

```csharp
// BreakpointId.cs
public readonly record struct BreakpointId(int Value);

// Breakpoint.cs
public sealed record Breakpoint(
    BreakpointId Id,
    Guid AssetId,
    Guid ElementId,
    int HitCount,
    bool Enabled,
    string DisplayName);

// IAiDebugSession.cs
public interface IAiDebugSession : IAiTraceObserver
{
    bool IsAttached { get; }
    void Detach();

    BreakpointId SetBreakpoint(Guid assetId, Guid elementId);
    void ClearBreakpoint(BreakpointId id);
    void ClearAllBreakpoints();
    IReadOnlyList<Breakpoint> GetBreakpoints();
    bool IsAnyBreakpointActive { get; }

    bool IsPaused { get; }
    Breakpoint? PausedAt { get; }
    Entity? PausedOnEntity { get; }
    void Continue();
    void StepOver();
    void StepInto();
    void StepOut();
    void Pause();

    event Action? OnSessionStateChanged;
}
```

**`AiDebugSessionBase` abstract class:**

The base class implements the common parts:
- Breakpoint registry: a `List<Breakpoint>` with auto-incrementing `BreakpointId` (start at 1); `SetBreakpoint` mints a new id, adds the breakpoint, raises `OnSessionStateChanged`; `ClearBreakpoint` removes by id; `ClearAllBreakpoints` clears all
- `IsAnyBreakpointActive`: true when `GetBreakpoints()` has any entry where `Enabled == true`
- `IsAttached`: protected setter, public getter; `Detach()` sets to false, clears all breakpoints, calls `OnDetachImpl()`
- `IsPaused`, `PausedAt`, `PausedOnEntity`: protected setters, public getters
- `OnSessionStateChanged`: event, raised by the base class when any state changes
- `BeginObservingAsset`, `EndObservingAsset`, `GetActiveEntities`: delegates to a `AiTracerCoordinator` instance held as a protected field (subclass provides it to constructor or base has a default no-op coordinator)
- Abstract methods for subsystems: `protected abstract void OnContinueImpl(); protected abstract void OnPauseImpl(); protected abstract void OnStepOverImpl(); protected abstract void OnStepIntoImpl(); protected abstract void OnStepOutImpl(); protected virtual void OnDetachImpl() { }`
- `Continue()`, `Pause()`, `StepOver()`, `StepInto()`, `StepOut()`: update `IsPaused`, call the impl, raise `OnSessionStateChanged`
- `Continue()` clears `IsPaused = false`, `PausedAt = null`, `PausedOnEntity = null`, calls `OnContinueImpl()`
- `Pause()` sets `IsPaused = true`, calls `OnPauseImpl()`

**`IDebugSessionRegistry` (from spec §11.2):**

```csharp
public interface IDebugSessionRegistry
{
    bool TryAcquireSession<TSession>(out TSession? session) where TSession : class, IAiDebugSession;
    void ReleaseSession(IAiDebugSession session);
    IDisposable RegisterObserver<TObserver>(TObserver observer) where TObserver : IAiTraceObserver;
    IReadOnlyList<IAiTraceObserver> ActiveObservers { get; }
    IAiDebugSession? ActiveSession { get; }
    event Action? Changed;
}
```

**`DebugSessionRegistry` implementation:**

- `TryAcquireSession<TSession>`: if `ActiveSession` is already set (and not the same session), return false with `session = null`; otherwise, try to create a `TSession` by calling a registered factory (or checking an injected `IReadOnlyDictionary<Type, Func<IAiDebugSession>>` session factories); if factory found, set `ActiveSession`, fire `Changed`, return true; if no factory, return false
- Actually simpler: accept a `Func<IAiDebugSession>` per `Type` in a dictionary, registered via `RegisterSessionFactory<TSession>(Func<TSession> factory)`. For Phase 1 testing, this lets tests register stub sessions.
- `ReleaseSession`: if `session == ActiveSession`, set `ActiveSession = null`, fire `Changed`; call `session.Detach()`
- `RegisterObserver<TObserver>`: adds to `_observers` list; returns a `Disposable` that removes it; fires `Changed`
- `ActiveObservers`: snapshot of the observer list
- Exclusivity: only one `ActiveSession` at a time

**Tests (put in `Debug/DebugSessionRegistryTests.cs` and `Debug/AiDebugSessionBaseTests.cs`) — minimum 16:**

Registry tests:
- `TryAcquireSession_WhenNoSession_ReturnsTrue`
- `TryAcquireSession_WhenSessionAlreadyActive_ReturnsFalse`
- `TryAcquireSession_WhenSessionAlreadyActive_OtherTypeAlsoFails`
- `ReleaseSession_ClearsActiveSession`
- `ReleaseSession_FiresChanged`
- `ReleaseSession_CallsDetach`: verify `session.IsAttached` is false after release
- `RegisterObserver_AddsToActiveObservers`
- `RegisterObserver_DisposedToken_RemovesObserver`
- `ActiveObservers_IsEmpty_Initially`

Session base tests (use a concrete test subclass):
- `SetBreakpoint_ReturnsUniqueId`
- `SetBreakpoint_BreakpointAppearsInGetBreakpoints`
- `ClearBreakpoint_RemovesById`
- `ClearAllBreakpoints_EmptiesList`
- `IsAnyBreakpointActive_TrueWhenAnyEnabled`
- `Pause_SetsPausedStateAndFiresEvent`
- `Continue_ClearsPausedState`

---

### Task 4: TASK-S1-13 — `HotReloadClassifier` + `HotReloadStatusIndicator`

**Spec:** Shared infra §17.

**Files to create:**
- `Hrot/Editor/Hrot.Editor.AiShared/HotReload/HotReloadTier.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/HotReload/HotReloadClassifier.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/HotReload/HotReloadStatus.cs`

**Required public surface:**

```csharp
// HotReloadTier.cs
namespace Hrot.Editor.AiShared.HotReload;

public enum HotReloadTier
{
    /// <summary>Layout-only change. Runtime not affected.</summary>
    Cosmetic,
    /// <summary>Parameter change. Instances retain state but lookup tables are patched.</summary>
    Soft,
    /// <summary>Topology change. Instances reset to initial state.</summary>
    Hard,
}
```

```csharp
// HotReloadClassifier.cs
namespace Hrot.Editor.AiShared.HotReload;

/// <summary>
/// Classifies a hot-reload based on hash deltas.
/// Each subsystem provides its own StructureHash and ParamHash computations;
/// this classifier is agnostic to what the hashes represent.
/// </summary>
public static class HotReloadClassifier
{
    /// <summary>
    /// Classify a reload by comparing before/after structure and param hashes.
    /// </summary>
    public static HotReloadTier Classify(int previousStructureHash, int newStructureHash,
                                          int previousParamHash, int newParamHash);

    /// <summary>
    /// When multiple changes coalesce (e.g. layout + soft), returns the most impactful tier.
    /// Hard > Soft > Cosmetic.
    /// </summary>
    public static HotReloadTier MostImpactful(HotReloadTier a, HotReloadTier b);
}
```

**Classification logic (from spec §17.2):**
```
If structureHash changed -> Hard
else if paramHash changed -> Soft
else -> Cosmetic
```

**`HotReloadStatus`:**
```csharp
// HotReloadStatus.cs
/// <summary>Snapshot of the last hot-reload result, for status indicator display.</summary>
public sealed record HotReloadStatus(
    HotReloadTier Tier,
    int LiveInstanceCount,     // number of entities that will be affected (0 for Cosmetic)
    bool RequiresConfirmation  // true only for Hard with LiveInstanceCount > 0
);
```

`RequiresConfirmation` is `tier == Hard && liveInstanceCount > 0`. The status indicator uses this to decide whether to show the confirmation dialog.

**Tests (put in `HotReload/HotReloadClassifierTests.cs`) — minimum 10:**

- `Classify_WhenStructureHashChanged_ReturnsHard`
- `Classify_WhenOnlyParamHashChanged_ReturnsSoft`
- `Classify_WhenNeitherHashChanged_ReturnsCosmetic`
- `Classify_WhenBothHashesChanged_ReturnsHard`: structure dominates
- `Classify_SameStructure_SameParam_Cosmetic`
- `MostImpactful_HardAndSoft_ReturnsHard`
- `MostImpactful_SoftAndCosmetic_ReturnsSoft`
- `MostImpactful_TwoCosmetics_ReturnsCosmetic`
- `HotReloadStatus_Hard_WithInstances_RequiresConfirmation`
- `HotReloadStatus_Hard_NoInstances_DoesNotRequireConfirmation`
- `HotReloadStatus_Soft_DoesNotRequireConfirmation`

---

## Testing Requirements

- All new tests in `Hrot/Editor/Hrot.Editor.AiShared.Tests/`
- Minimum: **45 new tests** (sum of per-task minimums)
- ALL existing 65 tests must still pass
- ALL new tests must pass
- Main solution must still build after this batch

## Quality Standards

**NOT ACCEPTABLE:**
- `AiTracerCoordinator` tests that don't verify refcount behavior
- `DebugSessionRegistry` tests that only check `ActiveSession != null`
- `HotReloadClassifier` tests without known-hash vectors

**REQUIRED:**
- `AiTracerCoordinator` tests MUST verify that `BeginObservingAssetImpl` is called exactly once for the first observer, not again for subsequent observers
- `DebugSessionRegistry` tests MUST verify exclusivity: two concurrent `TryAcquireSession` calls → second one returns false
- `HotReloadClassifier.MostImpactful` MUST be tested for all three tiers

---

## Success Criteria

This batch is DONE when:

- [ ] TASK-S1-08: `IGSelectionBridge` + `CallbackSelectionBridge` with 6+ tests
- [ ] TASK-S1-11: `IAiTraceObserver` + `AiTracerCoordinator` with 12+ tests
- [ ] TASK-S1-12: `IAiDebugSession` + `AiDebugSessionBase` + `IDebugSessionRegistry` + `DebugSessionRegistry` with 16+ tests
- [ ] TASK-S1-13: `HotReloadClassifier` + `HotReloadTier` + `HotReloadStatus` with 11+ tests
- [ ] `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj` — ALL PASS (65 old + 45+ new)
- [ ] `dotnet build IOS-IG-SimHost.sln` — builds clean
- [ ] Report submitted at `.dev/blueprints-2/reports/BATCH-03-REPORT.md`

---

## Common Pitfalls

- `IAiTraceObserver.GetActiveEntities` returns `IReadOnlyList<Entity>`. `Entity` is from `Fdp.Core` namespace `Fdp`. The project already has this reference.
- `AiTracerCoordinator.AddObserver`: for a test where you verify `BeginObservingAssetImpl` call count, derive a test subclass that overrides `BeginObservingAssetImpl` and increments a counter.
- `DebugSessionRegistry.TryAcquireSession<TSession>`: the type parameter constraint is `where TSession : class, IAiDebugSession`. You need a factory to actually produce the session. The simplest design: `RegisterSessionFactory<TSession>(Func<TSession> factory)` registered before `TryAcquireSession` is called. If no factory is registered for `TSession`, return false.
- `AiDebugSessionBase.Continue()` should NOT fire `OnSessionStateChanged` if `IsPaused` is already false (no-op). Same for `Pause()` if already paused.
- Do NOT add any dependencies outside `Fdp.Core` to the `Hrot.Editor.AiShared.csproj`.

---

## Developer Insights Report Template

Use `.dev/.guides/BATCH-REPORT-TEMPLATE.md`.

**Questions to answer in your report:**

1. For `AiTracerCoordinator`: how does `AddObserver` handle the "level escalation" case — if observer A observes with `Lifecycle` and observer B later adds `Decisions`, does the effective level update? How?
2. For `DebugSessionRegistry`: how did you implement `TryAcquireSession<TSession>` without knowing the concrete type at registry-construction time? Describe the factory registration pattern.
3. For `AiDebugSessionBase.Detach()`: what exactly happens during detach? List the state that gets cleared.
4. For `HotReloadClassifier`: is `MostImpactful` symmetric (i.e., `MostImpactful(a,b) == MostImpactful(b,a)`)? Was this enforced in tests?
5. Any design decisions beyond the spec?

---

## Reference Materials

- **Task Defs:** `.dev/blueprints-2/TASK-DETAIL.md` — S1-08, S1-11, S1-12, S1-13
- **Design Spec:** `.dev/blueprints-2/AI_Editor_Shared_Infrastructure.md` — §5.3, §11, §12, §17
- **Existing code:** `Hrot/Editor/Hrot.Editor.AiShared/` (BATCH-02 deliverables)
