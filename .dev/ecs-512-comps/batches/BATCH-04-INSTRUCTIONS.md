# BATCH-04: Phase 6 — Flight Recorder Dual-Stream Verification + Test Hardening

**Batch Number:** BATCH-04
**Tasks:** TASK-E008 (RecorderSystem dual-stream), TASK-E009 (PlaybackSystem routing), plus corrective tasks D004, D006
**Phase:** Phase 6 — Flight Recorder
**Estimated Effort:** 12-16 hours
**Priority:** HIGH
**Dependencies:** BATCH-03 (completed, committed)

---

## Onboarding & Workflow

### Developer Instructions

BATCH-02 implemented the RecorderSystem and PlaybackSystem changes as part of making `EntityIndex`
compile. This batch verifies and completes those changes:
- Ensures `RecorderSystem` correctly writes dual hot/cold streams per DESIGN.md Phase 6 spec.
- Ensures `PlaybackSystem` correctly routes `-1` (hot) and `-2` (cold) type IDs.
- Verifies FORMAT_VERSION 4 recordings are rejected by playback.
- Fixes two debt items: D004 (delta recorder cold-only changes) and D006 (recordable mask test).
- Adds the flight recorder tests specified in TASK-E008 and TASK-E009 success conditions.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.github/skills/developer/SKILL.md`
2. **Onboarding:** `.dev/ecs-512-comps/ONBOARDING.md`
3. **Design:** `.dev/ecs-512-comps/DESIGN.md` — "Phase 6: Flight Recorder" section thoroughly.
4. **Task Details:** `.dev/ecs-512-comps/TASK-DETAIL.md` — TASK-E008 and TASK-E009 sections.
5. **BATCH-03 Review:** `.dev/ecs-512-comps/reviews/BATCH-03-REVIEW.md` — understand D004, D006.
6. **Debt Tracker:** `.dev/ecs-512-comps/DEBT-TRACKER.md`
7. **Code Standards:** `.github/skills/CODE-STANDARDS.md`

### Source Code Location

- **Flight Recorder:** `FDP/Engine/Fdp.Core/FlightRecorder/`
  - `RecorderSystem.cs`
  - `PlaybackSystem.cs`
- **Tests:** `FDP/Engine/Fdp.Core.Tests/`
  - `RecorderSystemTests.cs`
  - `RecorderDeltaLogicTests.cs`
  - `PlaybackSystemTests.cs`
  - `FlightRecorderIntegrationTests.cs`
  - `ManagedComponentPlaybackTests.cs`
- **EntityRepository.Sync.cs** (for D006 fix)
- **EntityRepositoryTests.cs** (for D006 test fix)

### Build and Test Commands

```
cd FDP
dotnet build FDP.sln -c Debug

cd FDP/Engine/Fdp.Core.Tests
dotnet test
```

### Report Submission

**When done, submit your report to:**
`.dev/ecs-512-comps/reports/BATCH-04-REPORT.md`

**If you have questions, create:**
`.dev/ecs-512-comps/questions/BATCH-04-QUESTIONS.md`

---

## Context

The RecorderSystem and PlaybackSystem were partially updated in BATCH-02 to compile (dual stream
writes added, routing added). This batch verifies those changes are complete and correct per the
DESIGN.md spec, and adds the proper test coverage from the task detail.

**Key DESIGN.md constraints to verify:**
- `ENTITY_INDEX_HOT_TYPE_ID = -1` (unchanged from legacy value)
- `ENTITY_INDEX_COLD_TYPE_ID = -2` (new)
- Both `RecordDeltaFrame` and `RecordKeyframe`/`RecordAllChunks` must write dual streams.
- FORMAT_VERSION 5 in the `RecordingGlobalHeader`.
- FORMAT_VERSION 4 recordings must be REJECTED by `PlaybackSystem` with a clear error.

---

## Corrective Task 0a (D006): Fix `GetRecordableMask` test

**File:** `FDP/Engine/Fdp.Core.Tests/EntityRepositoryTests.cs`
**Method to fix:** `GetRecordableMask_ReturnsBitMask512_WithRegisteredBit`

**Problem (from BATCH-03-REVIEW.md):** The test only asserts `mask.IsEmpty()` on a fresh
registry. It doesn't verify that a recordable component's bit is set.

**Fix:** Look at how `DataPolicyAttribute` (or the component registration API) marks a component
as recordable. Then:
1. Register a component type that is explicitly recordable.
2. Call `repo.GetRecordableMask()`.
3. Assert `mask.IsSet(componentId) == true` for that component.

Check `FDP/Engine/Fdp.Core/DataPolicyAttribute.cs` and `TestComponents.cs` for examples of
how to declare a recordable component, or look at how existing recorder tests set up recordable
components.

---

## Corrective Task 0b (D004): Add cold-chunk dirty tracking in delta recorder

**File:** `FDP/Engine/Fdp.Core/FlightRecorder/RecorderSystem.cs`
**Problem (from BATCH-02-REVIEW.md):** Cold chunks are only written when the hot chunk has a
dirty version. Changes to cold-only fields (LastChangeTick, DisType, LifecycleState) between
keyframes are not captured in delta frames.

**Fix:** When iterating chunks in `RecordDeltaFrame`, also write cold chunks when the cold
chunk's version has changed (add/track a cold chunk version or use the existing cold table's
chunk version mechanism).

Study how `NativeChunkTable.GetChunkVersion()` and `IncrementChunkVersion()` work in the
existing code. Then ensure `RecordDeltaFrame` checks both hot and cold chunk versions.

**IMPORTANT:** If implementing cold dirty tracking is a significant risk (touches many test
scenarios), it is acceptable to document the limitation clearly in code with a comment and
leave it as a P2 open item. Write a test that demonstrates the current behavior (cold-only
changes in a delta frame are not captured), clearly marked as a known limitation.

---

## Task 1: RecorderSystem Dual-Stream Verification (TASK-E008)

**File:** `FDP/Engine/Fdp.Core/FlightRecorder/RecorderSystem.cs`

**Task Definition:** See [TASK-E008 in TASK-DETAIL.md](./../TASK-DETAIL.md#task-e008--recordersystem-dual-stream-entity-index).

Verify and complete per DESIGN.md Phase 6 "RecorderSystem changes" section:

1. `ENTITY_INDEX_COLD_TYPE_ID = -2` constant exists.
2. The entity index flush loop writes two chunks per chunk:
   - Hot chunk with sanitized (dead-slot-zeroed) and filtered (non-recordable bits cleared) data.
   - Cold chunk with sanitized (dead-slot-zeroed) data.
3. Both `RecordDeltaFrame` and `RecordAllChunks` (keyframe path) apply this pattern.
4. `SanitizeHotMasks` (or equivalent) applies `BitwiseAnd` with the recordable mask per slot.
5. `GetRecordableMask()` returns `BitMask512` (already done in BATCH-03; verify).

**Tests Required (add to `RecorderSystemTests.cs` or `RecorderDeltaLogicTests.cs`):**

Cover all TASK-E008 success conditions:

1. **Dual stream written** — Record a keyframe with at least one active entity. Parse the raw binary
   stream; verify exactly one chunk with typeId==-1 (hot) AND exactly one with typeId==-2 (cold)
   are present for the entity index in the output.

2. **Hot chunk size** — The byte count of the hot entity index chunk equals
   `entityIndex.GetChunkCapacity() * Unsafe.SizeOf<BitMask512>()` (= capacity * 64).

3. **Sanitization (dead entity)** — Create 3 entities, destroy entity index 1, record a keyframe.
   Re-read the hot chunk bytes. Verify that the 64-byte block at slot 1 is all zeros.

4. **Recordable mask filter** — A component marked `record: false` must have its bit cleared in
   the hot chunk data. Verify by reading back the hot chunk bytes and confirming the corresponding
   bit is zero for a non-recordable component the entity has.

5. **FORMAT_VERSION** — The outer `RecordingGlobalHeader` written to the stream has
   `FORMAT_VERSION == 5`.

---

## Task 2: PlaybackSystem Dual-Stream Routing (TASK-E009)

**File:** `FDP/Engine/Fdp.Core/FlightRecorder/PlaybackSystem.cs`

**Task Definition:** See [TASK-E009 in TASK-DETAIL.md](./../TASK-DETAIL.md#task-e009--playbacksystem-route-hotcold-streams).

Verify and complete per DESIGN.md Phase 6 "PlaybackSystem changes" section:

1. `ApplyChunkData` (or equivalent dispatch logic) correctly routes:
   - `typeId == -1` → `entityIndex.RestoreHotChunkFromBuffer(chunkIndex, data)` → return
   - `typeId == -2` → `entityIndex.RestoreColdChunkFromBuffer(chunkIndex, data)` → return
   - All other typeIds: existing component table routing unchanged.
2. `RepairManagedComponentMasks` uses `GetMetadataUnsafe(i)` for liveness and `GetComponentMaskUnsafe(i)` for bit updates.
3. FORMAT_VERSION 4 recordings are rejected with a clear error (exception or error return).

**Tests Required (add to `PlaybackSystemTests.cs` or `FlightRecorderIntegrationTests.cs`):**

Cover all TASK-E009 success conditions:

1. **Round-trip** — Record a world (with active entities, component bits, generation numbers).
   Play it back. Assert the `EntityIndex` state matches the original:
   - Active entity count
   - A specific entity's hot mask (`GetComponentMask(idx).IsSet(bit)`)
   - A specific entity's cold metadata (`GetMetadata(idx).Generation`, `IsActive`)

2. **Hot chunk applied** — After playback, verify `entityIndex.GetComponentMask(n).IsSet(bit)` for a
   known bit that was set before recording.

3. **Cold chunk applied** — After playback, verify `entityIndex.GetMetadata(n).Generation` and
   `IsActive` match the originals.

4. **Version mismatch rejection** — Attempting to play back a FORMAT_VERSION 4 recording throws
   an exception or returns a clear error (check what convention `SchemaValidator`/`PlaybackSystem`
   uses for version mismatch — throw or return error code — and write the test accordingly).

5. **All existing `PlaybackSystemTests.cs`, `FlightRecorderIntegrationTests.cs`, and
   `ManagedComponentPlaybackTests.cs` tests pass.**

---

## Testing Requirements

- All 777 existing `Fdp.Core.Tests` tests must pass (minus the pre-existing
  `ComponentDirtyTracking_PerformanceScan` flaky test — that is tracked separately as P3 and
  is not a BATCH-04 failure).
- New tests must verify actual binary content (byte sizes, specific chunk type IDs, zero-block
  checks, generation numbers) — not just that recording/playback "completes without exception."
- Minimum: 5 new tests for TASK-E008, 4 new tests for TASK-E009.

---

## Quality Standards

**Test Quality:**
- NOT ACCEPTABLE: `Assert.True(playback completed)` — tests must verify actual state.
- REQUIRED: For round-trip test, check both hot AND cold state after playback (not just active count).
- REQUIRED: For sanitization test, read the raw bytes and verify a specific 64-byte block is zero.
- REQUIRED: For dual-stream test, parse the binary to confirm both typeId=-1 AND typeId=-2 chunks exist.

**Code Quality:**
- No compiler warnings introduced.
- Cold chunk write must use correct entity size (`Unsafe.SizeOf<EntityMetadataCold>()`) for sanitization buffer sizing.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Corrective 0a (D006):** Fix `GetRecordableMask` test → **ALL tests pass** ✅
2. **Corrective 0b (D004):** Add/document cold dirty tracking → **ALL tests pass** ✅
3. **Task 1 (TASK-E008):** Verify/complete RecorderSystem dual-stream + add tests → **ALL tests pass** ✅
4. **Task 2 (TASK-E009):** Verify/complete PlaybackSystem routing + add tests → **ALL tests pass** ✅

**DO NOT** stop to ask for permission. Work autonomously. Write the report only when everything is green.

---

## Success Criteria

This batch is DONE when:

- [ ] **D006 corrective**: `GetRecordableMask` test registers a truly-recordable component and asserts its specific bit is set.
- [ ] **D004**: Cold dirty tracking either implemented or clearly documented as known limitation with a test demonstrating current behavior.
- [ ] **TASK-E008**: Dual-stream test passes (typeId=-1 AND typeId=-2 chunks found); hot chunk size is correct; dead-entity sanitization is zero-block verified; FORMAT_VERSION==5 in stream.
- [ ] **TASK-E009**: Round-trip test verifies hot mask bits AND cold generation/IsActive; version-mismatch (v4) is rejected.
- [ ] All existing flight recorder tests pass: `RecorderSystemTests.cs`, `RecorderDeltaLogicTests.cs`, `PlaybackSystemTests.cs`, `FlightRecorderIntegrationTests.cs`, `ManagedComponentPlaybackTests.cs`.
- [ ] Full `dotnet build FDP/FDP.sln -c Debug` — 0 errors, 0 new warnings.
- [ ] Report submitted to `.dev/ecs-512-comps/reports/BATCH-04-REPORT.md`.

---

## Developer Insights (Required in Report)

**Q1:** What issues did you encounter? How did you resolve them?

**Q2:** Was the D004 cold dirty tracking fixable, or did it require documenting as a known limitation? What was the constraint?

**Q3:** What was the existing version-mismatch behavior in PlaybackSystem (throw or error return)? Does FORMAT_VERSION 4 currently get rejected?

**Q4:** What design decisions did you make beyond the spec?

**Q5:** Are there any correctness concerns in the dual-stream recording that the design lead should know about?

**Q6:** Suggested commit message for this batch.

---

## Reference Materials

- **Task Details:** `.dev/ecs-512-comps/TASK-DETAIL.md` — TASK-E008, TASK-E009
- **Design:** `.dev/ecs-512-comps/DESIGN.md` — Phase 6 section
- **Previous Review:** `.dev/ecs-512-comps/reviews/BATCH-03-REVIEW.md`
- **Debt Tracker:** `.dev/ecs-512-comps/DEBT-TRACKER.md`
- **RecorderSystem source:** `FDP/Engine/Fdp.Core/FlightRecorder/RecorderSystem.cs`
- **PlaybackSystem source:** `FDP/Engine/Fdp.Core/FlightRecorder/PlaybackSystem.cs`
- **Existing recorder tests:** `FDP/Engine/Fdp.Core.Tests/RecorderSystemTests.cs`, `RecorderDeltaLogicTests.cs`
- **Existing playback tests:** `FDP/Engine/Fdp.Core.Tests/PlaybackSystemTests.cs`, `FlightRecorderIntegrationTests.cs`
- **DataPolicyAttribute:** `FDP/Engine/Fdp.Core/DataPolicyAttribute.cs`
- **TestComponents:** `FDP/Engine/Fdp.Core.Tests/TestComponents.cs`
