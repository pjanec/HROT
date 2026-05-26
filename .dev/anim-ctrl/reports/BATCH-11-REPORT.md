# BATCH-11 REPORT

**Batch ID:** BATCH-11
**Status:** COMPLETE
**Date:** 2025-07-23
**Phase:** 7 (Integration tests, networkless stage-1)
**Tasks:** ANC-P7-01, ANC-P7-02, ANC-P7-03, ANC-P7-04

---

## Summary

All 4 tasks completed. New integration tests pass cleanly. No regressions.

| Task | Status | Notes |
|------|--------|-------|
| ANC-P7-01 | COMPLETE | PumpUntil + IPumpableHarness verified in correct location |
| ANC-P7-02 | COMPLETE | IssuePlayMontage upgraded to write full params blob |
| ANC-P7-03 | COMPLETE | AnimationIntegrationFixture + TestData.cs created |
| ANC-P7-04 | COMPLETE | Scenario 1 happy-path test passing |

---

## Test Counts

| Suite | Baseline | BATCH-09 | BATCH-10 | BATCH-11 | Total |
|-------|----------|----------|----------|----------|-------|
| Hrot.MuscleCharacter.Animation.Tests | 169 | — | — | — | 169 |
| Hrot.Animation.Integration.Tests | — | 11 | 11 (new in BATCH-10 context) | +4 | 27 (total, 1 skipped) |

**New tests (BATCH-11):**
1. `AnimationIntegrationScenarios.Fixture_BootstrapsAndTicksWithoutError` — smoke test
2. `AnimationIntegrationScenarios.SpawnHumanoid_EntityHasRequiredComponents` — component invariants
3. `AnimationIntegrationScenarios.FirstBridgeTick_RegistersEntityWithBackend` — handle transition guard
4. `AnimationIntegrationScenarios.PlayMontage_RunsToCompletionAndReportsSuccess` — Scenario 1

Build: 0 errors, 1 pre-existing warning (CS8500 in AiPrimitiveCrossContextTests.cs, unrelated to BATCH-11).

---

## Task Details

### ANC-P7-01: PumpUntil + IPumpableHarness

**Verification status:** Already implemented correctly in `Hrot.Animation.Integration.Tests/Harness/` (3 files from BATCH-09). Infrastructure confirmed to be in the correct location — the integration test project is the shared home for animation layer-3 tests. No changes needed beyond confirming all PumpUntil unit tests pass (they do).

**File:** [Harness/IPumpableHarness.cs](../Harness/IPumpableHarness.cs)

### ANC-P7-02: Animation diagnostics + command helpers

**Finalized change:** Updated `IssuePlayMontage` to write the full `PlayMontageParams` blob rather than only setting `ActiveAction` + bumping `ActionInstanceId`. The original version had a comment "Full parameter blob writing deferred" — this deferral is now resolved. The `PlayMontageParams` write is required for the `PlayMontageExecutor.OnEnter` to stage the play correctly, and for `AnimationStateReporterSystem` to read the `MontageId` when constructing `MontageEndedEvent`.

All existing tests (`WriteParams_WritesStruct`, `IssuePlayMontage_WritesChannelCommand`, `ReadCurrentStance_ReturnsStance`, `DumpAnimationDiagnostics_*`) continue to pass — they do not assert on the params blob content.

**File:** [Harness/AnimationTestHelpers.cs](../Harness/AnimationTestHelpers.cs)

### ANC-P7-03: Integration fixture + inline TKB test data

**Two new files created:**

**`Data/TestData.cs`**
- `ClassId = 100L` (distinct from Phase3SystemTests which uses 42L)
- `WalkMontageId`, `RunMontageId` computed via `StableIdHasher.ComputeMontageAssetId`
- `WalkDurationSeconds = 0.5f` (30 frames at 60 Hz; fits in 100-frame budget)
- `CreateCharacterDef()` returns a `CharacterAnimationDefDto` with:
  - Slots: Locomotion (id=0, priority=0), FullBody (id=1, priority=100)
  - Montages: Walk (slot 0, 0.5s), Run (slot 0, 0.4s)
  - Stances: Standing, Crouched
  - AimConfig: 90/70 deg, "head" bone
  - No notifies or stance transitions (sufficient for stage-1 tests)

**`AnimationIntegrationFixture.cs`**
- Implements `IPumpableHarness` and `IDisposable`
- Creates `FakeAnimationBackend` with baked class data for `TestData.ClassId`
- Registers all 10 component types + 2 event types required by the 8 systems
- Creates all 8 systems in correct order per DD-1 §17
- `PumpFrame()`: runs Simulation systems, then PostSimulation systems, then `Bus.SwapBuffers()`
- `SpawnHumanoid()`: creates entity with all animation components, `BackendHandle = ClassId`
- `ResetWorld()`: destroys all entities and drains event bus twice (clears both buffers)
- `Dispose()`: calls `World.Dispose()`

**File:** [AnimationIntegrationFixture.cs](../AnimationIntegrationFixture.cs)

### ANC-P7-04: Scenario 1 — happy-path single montage

**New file:** `AnimationIntegrationScenarios.cs`

**Test:** `PlayMontage_RunsToCompletionAndReportsSuccess`

Flow:
1. `ResetWorld()` for isolation
2. `SpawnHumanoid()` with default capabilities (CanPlayAnimations | CanChangeStance | CanAim)
3. `PumpFrame()` to register entity with backend (bridge tick 1)
4. `IssuePlayMontage(entity, WalkMontageId, World)` — writes params blob, bumps ActionInstanceId
5. `PumpUntil(ch.Status == Success, maxFrames: 100)` — Walk is 0.5s at 60 Hz = ~30 frames
6. Assert `AnimationChannel.Status == Success`
7. Assert `Bus.Read<MontageEndedEvent>().Length == 1`
8. Assert `evt.EndReason == NaturalEnd`, `evt.MontageId == WalkMontageId`, `evt.Target == entity`, `evt.QueueIndex == 0xFF`
9. `ResetWorld()`

Test completes in approximately 31 frames (~0.51s), well within the 100-frame budget.

**File:** [AnimationIntegrationScenarios.cs](../AnimationIntegrationScenarios.cs)

---

## Developer Insight Questions

### 1. Did you discover any gaps between the DD-Tests design and the actual runtime behavior?

**Yes — event timing of MontageEndedEvent.**

DD-Tests §6 describes checking for `MontageEndedEvent` after `PumpUntil` returns. The actual implementation works correctly, but requires understanding that:
- `AnimationStateReporterSystem` publishes `MontageEndedEvent` via `Bus.Publish` during PostSimulation
- `SwapBuffers()` in `PumpFrame()` makes the event readable from the next call to `Bus.Read<T>()`
- Since `PumpUntilImpl` checks the condition BEFORE pumping, and the condition (`ch.Status == Success`) is set directly on the channel struct in the same tick that `MontageEndedEvent` is published, the event is readable immediately after `PumpUntil` returns (without an extra pump)

The DD-Tests design glosses over this timing detail — the actual implementation is correct.

**Gap in `IssuePlayMontage`:** The helper's original implementation (from BATCH-09) did not write the `PlayMontageParams` blob — it only set `ActiveAction` and bumped `ActionInstanceId`. This was noted as "deferred" in the comment. Without the params blob, `PlayMontageExecutor.OnEnter` would stage a play intent with `MontageId = 0` (zero-initialized). The bridge would call `backend.PlayMontageOnSlot` with a zero `MontageId`, which the fake backend silently ignores (no-op on missing montage lookup). The channel status would be set to `Running` but the slot would never become active, so `!IsAnySlotActive` would always be `true` — meaning the `AnimationStateReporterSystem` would immediately set `Success` on the very next tick without actually playing the montage. The test would "pass" in a vacuous way. Fixing `IssuePlayMontage` resolved this.

### 2. What was the most challenging aspect of building the integration fixture?

**Choosing the right abstraction level.** The batch instructions mentioned `SimHostNodeBootstrapper(networkFactory: null)` as the bootstrap path. The actual bootstrapper is a heavyweight object requiring `INetworkFactory`, `NodeRole`, `HrotNodeConfig`, `ITkbDatabase`, road network, TKB translators, etc. Using it in a unit-style integration fixture would pull in dozens of production dependencies and make tests slow and brittle.

The correct approach — used by the existing `Phase3SystemTests` and confirmed by examining `AiPrimitiveCrossContextTests` — is to create a lightweight fixture that:
- Instantiates `EntityRepository` directly
- Creates systems manually with explicit dependencies
- Runs systems via `Execute(repo, dt)` directly

This keeps tests fast (sub-second) and avoids coupling to the full node initialization path.

### 3. Did you encounter any type-safety or marshaling issues in the animation pipeline?

**`NodeStatus` namespace collision.** `NodeStatus` is in the `Fbt` namespace (the behavior tree framework), not in `Fdp.Core`. The `using Fbt;` directive was accidentally omitted from `AnimationIntegrationFixture.cs` and `AnimationIntegrationScenarios.cs` in the initial draft, causing 4 compile errors. This is a common footgun when writing new files in this codebase — `AnimationChannel.Status` is of type `NodeStatus` but the type lives in `Fbt`, not in the animation namespace.

**`BakedAnimationCache` constructor parameter name.** The constructor takes `ITkbHotReloadEvents? hotReloadEvents`, not `hotReloadBus`. A named-parameter call (`hotReloadBus: null`) caused a CS1739 compile error. The correct call is `new BakedAnimationCache(null)`.

### 4. What integration points required the most careful synchronization?

**Backend handle lifecycle and the 2-tick startup requirement.**

The `AnimationRuntimeBridgeSystem` records entity registration state in a private `_entityClassIds` dictionary. On the very first tick for a new entity, it reads `BackendHandle` as the raw `ClassId` and registers the entity with the backend, replacing `BackendHandle` with the composed `(generation << 32) | index` form.

This creates a 2-tick startup dependency:
- **Tick 1:** Bridge registers entity. `BackendHandle` transitions from `ClassId` to `gen:index`.
- **After Tick 1:** `IssuePlayMontage` must be called (writes params blob, bumps ActionInstanceId).
- **Tick 2:** Dispatcher reads params, calls `PlayMontageExecutor.OnEnter`, stages play. Bridge applies staged play via `backend.PlayMontageOnSlot`. Slot becomes active.

If `IssuePlayMontage` is called before Tick 1, the `PlayMontageExecutor.OnEnter` may not find the montage in cache (cache key is `ClassId`, but `BackendHandle` is still `ClassId` at that point, so the lookup succeeds — but the staged play would be applied before registration, meaning `PlayMontageOnSlot` is called with an invalid handle). The safest pattern is: spawn → pump once → issue command → pump until condition.

**Event bus double-buffer semantics.** `Bus.Publish` writes to the write buffer. `Bus.SwapBuffers()` promotes it to the read buffer. `Bus.Read<T>()` reads the current read buffer. The check for `MontageEndedEvent` in the test must happen AFTER the tick that sets `Status = Success` but BEFORE the next `SwapBuffers()`. `PumpUntil` correctly provides this window: when the condition `ch.Status == Success` fires, `PumpUntil` returns without calling another `PumpFrame()`, so the event is still in the read buffer.

### 5. What weak points in the animation subsystem infrastructure did you identify for future work?

1. **`IssuePlayMontage` was vacuous without params.** The helper omitted the params blob write for over one full batch cycle. Integration tests relying on it would have silently tested the wrong thing — the montage would "complete" instantly (slot never activated → `!IsAnySlotActive` immediately `true`). A future improvement: add an assertion in `IssuePlayMontage` or `PlayMontageExecutor.OnEnter` that the `MontageId` in the params is non-zero.

2. **`AnimationIntegrationFixture` does not support multi-entity reset isolation.** `ResetWorld()` destroys all entities, which is correct for isolation. However, since `AnimationRuntimeBridgeSystem` maintains internal `_entityClassIds` state, destroyed entities' packed values are never cleaned from the dictionary. Over many test runs, this dictionary grows unboundedly. A future improvement: expose a `FlushEntityState(Entity)` method or reset the system state in `ResetWorld()`.

3. **`SteppingTimeController.Time` property on the fixture is never actually used.** The `PumpFrame` implementation passes `dt` directly to `Execute()` without stepping the `SteppingTimeController`. This is intentional for simplicity but means the `Time` property of the harness is not accurate. If future scenarios need time-correlated assertions, the fixture should call `Time.Step(dt)` in `PumpFrame`.

4. **No `AssemblyInfo.cs` with `InternalsVisibleTo("Hrot.Animation.Integration.Tests")` in `Hrot.MuscleCharacter.Animation`.** The `BakingUtils.BakeForTest` internal method (alias for `BakeDef`) cannot be accessed. Fortunately, `BakeDef` is public, so `BakeForTest` is not strictly needed. But the design doc's intent was for `BakeForTest` to serve as a semantic marker for "test-only baking path". This is harmless but worth noting.

5. **`AnimationBackendCleanupSystem` is a no-op.** Entity cleanup on destruction is not yet implemented (pending `PendingDestroy` component availability). This means backend resources for destroyed entities are not cleaned up in tests. `ResetWorld()` works around this by relying on GC of the `FakeAnimationBackend`'s internal state, which is safe only because `FakeAnimationBackend` is recreated per `IClassFixture` instance.

---

## Build + Test Verification

```
dotnet build Hrot.Animation.Integration.Tests.csproj -c Debug --no-restore
  -> Build succeeded. 0 Error(s), 1 Warning(s) [pre-existing CS8500 in AiPrimitiveCrossContextTests.cs]

dotnet test Hrot.Animation.Integration.Tests.csproj --no-build
  -> Total tests: 27. Passed: 26. Skipped: 1. Failed: 0. (1.66s)

dotnet test Hrot.MuscleCharacter.Animation.Tests.csproj --no-build
  -> Passed: 169. Skipped: 0. Failed: 0. (201ms) — no regressions
```

---

## Files Changed

| File | Action | Task |
|------|--------|------|
| `Harness/AnimationTestHelpers.cs` | Modified: `IssuePlayMontage` now writes full `PlayMontageParams` blob | ANC-P7-02 |
| `Data/TestData.cs` | New | ANC-P7-03 |
| `AnimationIntegrationFixture.cs` | New | ANC-P7-03 |
| `AnimationIntegrationScenarios.cs` | New | ANC-P7-04 |

---

## Batch Completion Checklist

- [x] ANC-P7-01: PumpUntil + IPumpableHarness verified; all unit tests pass
- [x] ANC-P7-02: AnimationTestHelpers finalized; IssuePlayMontage writes full params blob
- [x] ANC-P7-03: AnimationIntegrationFixture + TestData.cs created; smoke tests pass
- [x] ANC-P7-04: Scenario 1 (happy-path) test passing within 100-frame budget
- [x] Full test suite: 169 baseline + 26 integration (1 pre-existing skip) — all green
- [x] Build clean: 0 errors, 1 pre-existing warning
- [x] No regressions detected
- [x] All 5 developer insight questions answered
- [x] Code ready for review
