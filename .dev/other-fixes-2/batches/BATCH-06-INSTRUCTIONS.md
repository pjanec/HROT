# BATCH-06: LookAt ActionParams Blob Compare & FakeAnimationBackend State Mirroring

**Batch Number:** BATCH-06  
**Tasks:** FIX2-008, FIX2-014  
**Priority:** HIGH / MEDIUM  
**Dependencies:** None from previous batches (animation area is independent)

---

## Mandatory Workflow

**Read AGENTS.md at the repo root before writing a single line of code.**

Complete tasks in strict sequence. For each task:
1. Define the **success condition** BEFORE touching any code.
2. Implement the fix.
3. Write / update tests that drive the **production path**.
4. Run the relevant test project and confirm all tests pass.
5. Fix any failures before moving to the next task.

Do NOT ask for permission at any step. Do NOT stop early. Finish both tasks, make all tests green, then write the report.

---

## Onboarding & Workflow

### Required Reading (in order)
1. **Task details:** `.dev/other-fixes-2/TASK-DETAIL.md` -- sections FIX2-008, FIX2-014
2. **Source finding OFX-012:** `.dev/other-fixes-1/TASK-DETAIL.md` -- OFX-012
3. **Source finding OFX-003:** `.dev/other-fixes-1/TASK-DETAIL.md` -- OFX-003
4. **Animation DD-2 §2.4:** search for `OFX` / animation design docs under `Hrot/Subsystems/` or `docs/`

### Source Code Areas
- **LookAt translator:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Channels/LookAtChannelIntentEgressTranslator.cs`
- **Animation translator (already fixed reference):** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Channels/AnimationChannelIntentEgressTranslator.cs`
- **FakeAnimationBackend:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Tests/Fake/FakeAnimationBackend.cs`
- **FakeAnimBackendState:** search for `FakeAnimBackendState` in the same tests area
- **Animation tests:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Tests/`

### Build & Test
```
cd d:\WORK\IOS-IG-SimHost-FDP
dotnet build Hrot\Subsystems\Hrot.MuscleCharacter.Animation.Tests\Hrot.MuscleCharacter.Animation.Tests.csproj --nologo -v q
dotnet test Hrot\Subsystems\Hrot.MuscleCharacter.Animation.Tests\Hrot.MuscleCharacter.Animation.Tests.csproj --nologo
```

Also check the Hrot.Blueprints.Tests for regressions:
```
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName!~AllocationFree" --nologo
```

### Report Submission
Submit report to: `.dev/other-fixes-2/reports/BATCH-06-REPORT.md`

---

## Context

FIX2-008: The `AnimationChannelIntentEgressTranslator` was already fixed in round 1 (OFX-012) to compare the 4-ulong `Params` blob alongside `ActionInstanceId`. But the **LookAt** translator (`LookAtChannelIntentEgressTranslator`) was not updated and still gates publication solely on `ActionInstanceId`, causing silent drops of in-place param mutations.

FIX2-014: `FakeAnimationBackend.Tick()` still iterates `_entityStates.Values` (managed Dictionary). The per-tick state (Slots/Aim/Stance/TotalTicks/footstep/notifies) is never mirrored to `FakeAnimBackendState`. Also, `_entityIndexToEntity` leaks dead entities when `UnregisterEntity` is called without `ResetWorld`.

---

## Tasks

### Task 1 -- FIX2-008: Apply ActionParams blob compare to `LookAtChannelIntentEgressTranslator`

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-008`

**Success condition (define before coding):**
A test calls `LookAtChannelIntentEgressTranslator.Translate()` twice with the same `ActionInstanceId` but different `Params` blobs, and asserts that a new action is published on the second call (not silently dropped). Without the fix, the second call is a no-op.

**What to fix:**
- Read `AnimationChannelIntentEgressTranslator.cs` (the already-fixed reference) around lines 27 and 70-84 to understand the exact blob-comparison logic.
- Apply the **same** logic to `LookAtChannelIntentEgressTranslator.cs` around lines 25 and 61-66.
- The 4-ulong `Params` struct comparison must be added: store the last published params alongside `ActionInstanceId` and re-publish when either changes.

**Test required:**
- Test name: `LookAtTranslator_RepublishesWhenParamsBlobChanges_SameActionInstanceId` (or similar)
- Must: call `Translate()` once (first publish), call again with same `ActionInstanceId` but different `Params`, assert output channel received a second publication.
- Must NOT: call the internal comparison logic directly; go through `Translate()`.

---

### Task 2 -- FIX2-014: Mirror per-tick state to `FakeAnimBackendState` and fix entity map leak

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-014`

**Success condition (define before coding):**
1. After `Tick()`, calling `FakeAnimBackendState.TotalTicks` for an entity returns the incremented value (not 0). This proves the managed Dictionary Tick path is mirrored to the ECS component.
2. After `UnregisterEntity()`, the entity's entry is absent from `_entityIndexToEntity`. This proves the leak is fixed.

**What to fix:**
1. In `FakeAnimationBackend.Tick()`, for each entity in `_entityStates.Values`, after updating the managed state, mirror the relevant fields to `FakeAnimBackendState` (via `EntityRepository.SetComponent` or equivalent).
2. In `FakeAnimationBackend.UnregisterEntity()`, remove the entity from `_entityIndexToEntity` (in addition to `_entityStates`).

**Tests required:**
- Test 1: `FakeAnimBackend_Tick_MirrorsStateToEcsComponent` -- after `Tick()`, query `FakeAnimBackendState` from the EntityRepository and assert `TotalTicks == 1`, slot values match, etc.
- Test 2: `FakeAnimBackend_UnregisterEntity_RemovesFromEntityIndexMap` -- register an entity, unregister it, assert `_entityIndexToEntity` (if accessible via internal/reflection, or add a test-visible property `EntityIndexMapCount`) does not contain the entity.
- Must NOT: directly manipulate `FakeAnimBackendState` without going through `Tick()`.

---

## Quality Standards

**PRODUCTION PATH:** All tests must drive the production `Translate()` and `Tick()` paths. Direct field manipulation does NOT count.

**ALL EXISTING TESTS MUST STAY GREEN.**

---

## Developer Insights (Report Questions)

1. Did the `LookAtChannelIntentEgressTranslator` use exactly the same struct for `Params` as the animation translator, or did you need to adapt the comparison?
2. How did you expose `_entityIndexToEntity` count for the leak test (test-visible property, internal member, or reflection)?
3. Did you discover any additional fields in `FakeAnimBackendState` that should be mirrored but aren't yet (scope for future work)?
4. **Suggested commit message** for this batch.
