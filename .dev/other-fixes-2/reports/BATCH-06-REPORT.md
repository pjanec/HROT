# BATCH-06 Report: LookAt ActionParams Blob Compare & FakeAnimationBackend State Mirroring

**Batch:** BATCH-06  
**Tasks:** FIX2-008, FIX2-014  
**Date:** 2026-05-31

---

## Task Status

| Task | Status | Summary |
|------|--------|---------|
| FIX2-008 | DONE | `LookAtChannelIntentEgressTranslator` now compares ActionParams blob alongside ActionInstanceId |
| FIX2-014 | DONE | `Tick()` mirrors per-tick state to `FakeAnimBackendState`; `UnregisterEntity` removes from `_entityIndexToEntity` |

---

## FIX2-008: Apply ActionParams Blob Compare to LookAtChannelIntentEgressTranslator

### Success Condition (defined before coding)
A test calls `LookAtChannelIntentEgressTranslator.ScanAndPublish()` twice with the same
`ActionInstanceId` but different `Params` bytes, and asserts that a second message is published.
Without the fix, the second call is skipped because ActionInstanceId matches.

### What Was Done
- Added `_lastPublishedActionParams` dictionary (`Dictionary<Entity, (ulong p0, ulong p1, ulong p2, ulong p3)>`)
  alongside the existing `_lastPublishedActionInstanceId` in `LookAtChannelIntentEgressTranslator.cs`.
- Moved `var ch = channel;` to before the guard check (was after) so the local copy is available for
  unsafe pointer arithmetic at params-extraction time.
- Extracted the 4-ulong tuple from `ch.Params` using an inner `unsafe {}` block (matching the exact
  pattern from `AnimationChannelIntentEgressTranslator`).
- Extended the dirty-filter condition: skip only if both `ActionInstanceId` AND the params tuple are
  unchanged.
- Added `_lastPublishedActionParams[entity] = currentParams;` after each publish.

### Test Added
**File:** `Hrot/Subsystems/Hrot.Animation.Replication.Tests/AnimationChannelTranslatorTests.cs`  
**Test:** `LookAtChannelIntentEgress_PublishesOnActionParamsChange_WhenSameInstanceId` (SC-11)

Drives `ScanAndPublish()` through the production path:
1. First call publishes (SentSampleCount == 1).
2. Mutate only `Params[0]` (ActionInstanceId stays at 99).
3. Second call publishes again (SentSampleCount == 2).
4. Third call with no change does NOT publish (DirtyFalsePositiveCount == 1).

---

## FIX2-014: Mirror Per-Tick State to FakeAnimBackendState; Fix Entity Map Leak

### Success Conditions (defined before coding)
1. After `Tick()`, `FakeAnimBackendState.TotalTicks` equals 1 for an entity that was registered and
   whose repository was injected via `SetEntityRepository`.
2. After `UnregisterEntity()`, `EntityIndexMapCount` is 0.

### What Was Done

**Entity map leak fix:**
- `UnregisterEntity()` now calls `_entityIndexToEntity.Remove(slot.EntityId)` in addition to
  removing from `_handleSlots` and `_entityStates`.

**Tick mirroring:**
- `Tick()` now iterates `_entityStates` as key-value pairs (`foreach (var (entityId, state) in _entityStates)`)
  instead of `_entityStates.Values`, giving access to the entity index.
- After `Advance*` calls, if `_repo != null` and the entity index is in `_entityIndexToEntity`, calls
  the new `MirrorToEcs(entity, state)` helper.
- `MirrorToEcs` reads the existing `FakeAnimBackendState` (for `Generation` and `TotalTicks`),
  increments `TotalTicks`, copies `Aim`, `Stance`, locomotion inputs (`HorizontalSpeed`,
  `LocalHorizontalVelocity`, `VerticalVelocity`, `IsGrounded`, `DistanceSinceLastFootstep`,
  `NextFootIndex`), pending notify count and ring, and all 8 slot states. Writes back with
  `_repo!.SetComponent(entity, newState)`.

**Test-visible property:**
- Added `public int EntityIndexMapCount => _entityIndexToEntity.Count;` to `FakeAnimationBackend`
  for test verification. Marked `public` (not `internal`) because the test project does not have
  `InternalsVisibleTo` wiring.

### Tests Added
**File:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Tests/Phase1BackendBehaviorTests.cs`

**Test 1:** `FakeAnimBackend_Tick_MirrorsStateToEcsComponent`
- Creates a minimal `FakeAnimationBackend`, injects an `EntityRepository`, registers one entity.
- Asserts `TotalTicks == 0` before any tick.
- Calls `Tick(1.0f)` and asserts `TotalTicks == 1` via `repo.GetComponentRO<FakeAnimBackendState>`.
- Calls `Tick(1.0f)` again and asserts `TotalTicks == 2`.
- Drives the full production path through `Tick()` -> `MirrorToEcs()` -> `SetComponent`.

**Test 2:** `FakeAnimBackend_UnregisterEntity_RemovesFromEntityIndexMap`
- Registers one entity, asserts `EntityIndexMapCount == 1`.
- Calls `UnregisterEntity`, asserts `EntityIndexMapCount == 0`.

---

## Final Test Run Output

```
Hrot.MuscleCharacter.Animation.Tests:
  Passed!  - Failed: 0, Passed: 195, Skipped: 0, Total: 195

Hrot.Animation.Replication.Tests:
  Passed!  - Failed: 0, Passed: 44, Skipped: 0, Total: 44

Hrot.Blueprints.Tests (regression, --filter FullyQualifiedName!~AllocationFree):
  Passed!  - Failed: 0, Passed: 886, Skipped: 8, Total: 894
```

---

## Issues Encountered and Resolutions

1. **`EntityIndexMapCount` visibility:** Initially marked `internal`; the test project has no
   `InternalsVisibleTo` attribute, so CS1061 was reported. Changed to `public` since
   `FakeAnimationBackend` is itself a `public` class designed for test use.

2. **Test location for FIX2-008:** The batch instruction references `Hrot.MuscleCharacter.Animation.Tests`
   as the primary test suite, but `LookAtChannelIntentEgressTranslator` lives in
   `Hrot.Animation.Replication` which has its own test project. The new test was added to
   `AnimationChannelTranslatorTests.cs` in `Hrot.Animation.Replication.Tests` where the rest of the
   translator tests live and where the necessary project references already exist.

---

## Developer Insights (Report Questions)

**1. Did `LookAtChannelIntentEgressTranslator` use the same `Params` struct as the animation translator?**

Yes. Both `AnimationChannel.Params` and `LookAtChannel.Params` are `fixed byte Params[32]`
(`BehaviorConstants.ActionParamsByteSize == 32`). The exact same 4-ulong tuple extraction pattern
(`(ulong*)ch.Params` where `ch` is a stack-local copy) applied without adaptation.

**2. How was `_entityIndexToEntity` count exposed for the leak test?**

Added `public int EntityIndexMapCount => _entityIndexToEntity.Count;` directly to
`FakeAnimationBackend`. No `InternalsVisibleTo` or reflection required. Since the class is already
`public` and intended for test use, a `public` diagnostic property is appropriate.

**3. Were there additional `FakeAnimBackendState` fields not yet mirrored?**

All fields defined in `FakeAnimBackendState` are now mirrored by `MirrorToEcs`:
- `Generation` (preserved, not incremented)
- `TotalTicks` (incremented per tick)
- `Slots[8]` (copied from `EntityBehavioralState.Slots[]`)
- `Aim` / `Stance`
- `HorizontalSpeed`, `LocalHorizontalVelocity`, `VerticalVelocity`, `IsGrounded`
- `DistanceSinceLastFootstep`, `NextFootIndex`
- `PendingNotifyCount`, `PendingNotifies[16]`

No fields were found to be missing. Full coverage achieved.

**4. Suggested commit message:**

```
fix: LookAt params-blob compare and FakeAnimBackend ECS mirroring (FIX2-008, FIX2-014)

FIX2-008: LookAtChannelIntentEgressTranslator now tracks _lastPublishedActionParams
(4-ulong tuple) alongside _lastPublishedActionInstanceId. ScanAndPublish re-publishes
when either ActionInstanceId or the Params blob changes, matching the behaviour already
fixed in AnimationChannelIntentEgressTranslator (OFX-012).

FIX2-014: FakeAnimationBackend.Tick() now iterates _entityStates as key-value pairs
and calls MirrorToEcs() after each entity's Advance* calls, writing all per-tick fields
(TotalTicks, Slots, Aim, Stance, locomotion, notifies) into FakeAnimBackendState via
SetComponent. UnregisterEntity() now also removes the entity from _entityIndexToEntity,
closing the dead-entity map leak. EntityIndexMapCount property added for test visibility.

Tests: +1 (AnimationChannelTranslatorTests SC-11), +2 (Phase1BackendBehaviorTests FIX2-014)
Animation suite: 195 passed, 0 failed
Replication suite: 44 passed, 0 failed
Blueprints regression: 886 passed, 0 failed
```
