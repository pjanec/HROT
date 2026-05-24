# BATCH-35 Report

**Workstream:** breakpoints-1  
**Batch:** BATCH-35  
**Status:** COMPLETE — all tasks implemented, all tests pass

---

## Task Summary

| Task      | Title                                        | Status   |
|-----------|----------------------------------------------|----------|
| UBP-P0T1  | Rename IBlueprintTimeController              | DONE     |
| UBP-P1T1  | DebugSnapshotProvider                        | DONE     |
| UBP-P1T2  | IDataBreakpointManager skeleton + gate       | DONE     |
| UBP-P1T3  | Triple-buffer pause primitives               | DONE     |

---

## UBP-P0T1 — Rename IBlueprintTimeController

**Goal:** Introduce `IEngineDebugTimeController` as the canonical name; keep `IBlueprintTimeController` alive for one batch under `[Obsolete]`.

### Files changed

**`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintTimeController.cs`**  
Renamed the interface to `IEngineDebugTimeController`. The old `IBlueprintTimeController` is
preserved as an empty extension of the new interface, annotated with `[Obsolete]`.

**`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/MasterSyncTimeControllerAdapter.cs`**  
Updated class declaration to implement both `IEngineDebugTimeController` and `IBlueprintTimeController`.
A `#pragma warning disable/restore CS0618` pair suppresses the obsolete-reference compiler error
(required because `TreatWarningsAsErrors` is on).

**`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs`**  
Changed the `_timeController` field and constructor parameter type from `IBlueprintTimeController`
to `IEngineDebugTimeController`.

### Verification

- `dotnet build Hrot.Blueprints.Tests.csproj` — Build succeeded, 0 errors.
- Relevant debug/editor tests: 36 passed, 0 failed.
- Pre-existing unrelated failures (98 MoveToAndFire demo tests) confirmed unchanged.

---

## UBP-P1T1 — DebugSnapshotProvider

**Goal:** Implement a `BeforeSync`-phase ECS system that copies the live repository into a
pre-allocated snapshot each tick while an atomic gate (`volatile int`) is open.

### New files

**`Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/Hrot.Diagnostics.Breakpoints.csproj`**  
New project targeting `net8.0`, `Nullable`, `TreatWarningsAsErrors`, `AllowUnsafeBlocks`.
References: `Fdp.Core`, `Fdp.ModuleHost`, `Fdp.Toolkits`, `Hrot.Blueprints.Core`.
Exposes internals to `Hrot.Diagnostics.Breakpoints.Tests`.

**`Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DebugSnapshotProvider.cs`**  
- `[UpdateInPhase(SystemPhase.BeforeSync)]`
- `volatile int _isEnabled` gate (0 = off, 1 = on)
- `SetEnabled(bool)` — atomic via `Interlocked.Exchange`
- `Execute(ISimulationView, float)`:
  - Returns immediately when gate is 0 (zero allocation hot path).
  - Throws `InvalidOperationException` when view is not an `EntityRepository`.
  - Calls `_preTickSnapshot.SyncFrom(repo)` when gate is 1.
- `internal int IsEnabledRaw` — exposes gate for test assertions.

### Solution registration

Both projects added with `dotnet sln add`.

### Tests (UBP-P1T1)

File: `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/DataBreakpointManagerTests.cs`  
Class: `DebugSnapshotProviderTests`

| Test                                          | Description                                          |
|-----------------------------------------------|------------------------------------------------------|
| `GateOff_DoesNoWork`                          | Gate=0 on construction; Execute returns immediately  |
| `GateOn_ExecuteRuns_WithoutException`         | Gate=1; 3 consecutive Execute calls succeed          |
| `SetEnabled_Toggle_UpdatesGate`               | SetEnabled(true) then SetEnabled(false) toggles flag |
| `Execute_NonEntityRepositoryView_Throws`      | Non-EntityRepository view throws InvalidOperationException |

---

## UBP-P1T2 — IDataBreakpointManager skeleton + reference-counted gate

**Goal:** Define the full public contract for data breakpoints and implement a reference-counted
gate that mounts/unmounts the `DebugSnapshotProvider` as breakpoints are enabled/disabled.

### New files

**`Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/BreakpointTypes.cs`**  
- `BreakpointId` — `readonly struct`, wraps int, `Invalid` sentinel = default(0), value-equality.
- `Breakpoint` — `sealed record` with: `Id`, `Condition` (SearchPredicateDto?), `FilterEntity` (Entity?),
  `HitCount`, `OccurrenceThreshold` (default 1), `Enabled`, `DisplayName`.

**`Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/IDataBreakpointManager.cs`**  
Interface methods: `Add`, `Remove`, `SetEnabled`, `UpdateCondition`, `StageMutation` (P4 stub),
`RequestStep`, `RequestContinue`, `OnExternalHit` (P7 stub).  
Events: `OnBreakpointHit`, `OnPauseStateChanged`.  
Properties: `IsPaused`, `PendingMutationsCount`, `AllBreakpoints`.

**`Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs`**  
Concrete implementation. Gate logic in `AdjustGate(int delta)`:
- Increments `_activeBreakpointCount`.
- `0 → 1` transition: calls `_snapshotProvider.SetEnabled(true)`.
- `1 → 0` transition: calls `_snapshotProvider.SetEnabled(false)`.
- Intermediate counts (2+) do not re-call SetEnabled.

### Tests (UBP-P1T2)

Class: `SnapshotGateTests`

| Test                                            | Scenario                                              |
|-------------------------------------------------|-------------------------------------------------------|
| `FirstBreakpointEnabled_MountsSnapshotProvider` | gate off → add enabled BP → gate on                  |
| `LastBreakpointRemoved_UnmountsSnapshotProvider`| gate on → remove last enabled BP → gate off           |
| `DisableThenReenable_GateTogglesCorrectly`      | disable BP lowers gate; re-enable raises it again     |
| `TwoBreakpoints_DisableOne_GateRemainsOpen`     | two BPs; disable one → gate stays on; disable second → off |
| `AddDisabledBreakpoint_GateRemainsOff`          | disabled BP at registration must not open gate        |

---

## UBP-P1T3 — Triple-buffer pause primitives

**Goal:** Implement `OnHit`, `RequestStep`, and `RequestContinue` with the triple-buffer protocol:
pre-tick snapshot (filled by provider), post-tick snapshot (captured at hit), live repo (rewound).

### Implementation in DataBreakpointManager.cs

**`OnHit(Breakpoint bp, Entity entity)`**
1. Increments `HitCount` on the stored record.
2. Returns without pausing if `HitCount < OccurrenceThreshold`.
3. `_postTickSnapshot.SyncFrom(_liveRepo)` — captures current (post-execution) state.
4. `_liveRepo.SyncFrom(_preTickSnapshot)` — rewinds world to start-of-tick.
5. `_timeController.RequestPause()` — halts the clock.
6. `_isPaused = true`.
7. Fires `OnBreakpointHit` and `OnPauseStateChanged(true)`.

**`RequestStep()`**
1. No-op when not paused.
2. `_liveRepo.SyncFrom(_postTickSnapshot)` — restore end-of-tick (clean step, no event injection).
3. `_timeController.RequestStepOneTick()`.
4. `_isPaused = false`.
5. Fires `OnPauseStateChanged(false)`.

**`RequestContinue()`**
1. No-op when not paused.
2. `_liveRepo.SyncFrom(_postTickSnapshot)` — restore end-of-tick.
3. `_timeController.RequestResume()`.
4. `_isPaused = false`.
5. Fires `OnPauseStateChanged(false)`.

### Tests (UBP-P1T3)

Class: `TripleBufferPauseTests`

| Test                                             | Scenario                                                     |
|--------------------------------------------------|--------------------------------------------------------------|
| `OnHit_PerformsTripleBufferRewind_AndFiresEvents`| pause requested, both events fire, IsPaused=true             |
| `RequestContinue_ResumesClockAndClearsPause`     | RequestResume called, IsPaused=false, event=false            |
| `RequestStep_ResumesWithOneTick_AndClearsPause`  | RequestStepOneTick called, ResumeCount=0, IsPaused=false     |
| `RequestContinue_WhenNotPaused_IsNoOp`           | no-op: zero events, zero clock calls                         |
| `RequestStep_WhenNotPaused_IsNoOp`               | no-op: zero events, zero clock calls                         |
| `OccurrenceThreshold_PausesOnNthHit`             | threshold=3: hits 1,2 no pause; hit 3 pauses                 |
| `OnHit_AlwaysIncrementsHitCount`                 | HitCount increments on every call regardless of threshold    |

---

## Test Results

```
dotnet test Hrot.Diagnostics.Breakpoints.Tests.csproj
  Passed: 16
  Failed: 0
  Error(s): 0
```

All 16 new tests pass. Blueprints test build: 0 errors.

---

## Files Created / Modified

### New files

| File | Description |
|------|-------------|
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/Hrot.Diagnostics.Breakpoints.csproj` | New project file |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DebugSnapshotProvider.cs` | UBP-P1T1 system |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/BreakpointTypes.cs` | BreakpointId + Breakpoint record |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/IDataBreakpointManager.cs` | Public interface |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` | Concrete implementation |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj` | Test project file |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/DataBreakpointManagerTests.cs` | 16 tests across 3 classes |

### Modified files

| File | Change |
|------|--------|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintTimeController.cs` | Added IEngineDebugTimeController; IBlueprintTimeController is now [Obsolete] |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/MasterSyncTimeControllerAdapter.cs` | Implements both interfaces; #pragma suppresses CS0618 |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` | Field/ctor param updated to IEngineDebugTimeController |
| `IOS-IG-SimHost.sln` | Both new projects added via dotnet sln add |
