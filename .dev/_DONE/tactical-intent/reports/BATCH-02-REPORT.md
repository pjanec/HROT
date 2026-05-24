# BATCH-02 Report

**Batch:** BATCH-02  
**Developer:** GitHub Copilot  
**Date:** 2026-05-02  
**Status:** Complete

---

## Task Completion

| Task ID    | Status   | Notes                                                                                  |
|------------|----------|----------------------------------------------------------------------------------------|
| TASK-TI005 | Complete | `Commander = 1 << 4` added to `BehaviorCategory`; 2/2 tests passing                  |
| TASK-TI006 | Complete | `DefendArea_Intent = 1000` reserved; `DefendAreaIntentDto` created; 2/2 tests passing |
| TASK-TI004 | Complete | `MissionAdapterSystem` now publishes `AssignTacticalIntentEvent`; 2/2 tests passing   |

---

## Testing Results

**Unit Tests — Hrot.Core.Tests:** 105 / 105 passed (4 new from this batch)  
**Unit Tests — Hrot.SimHost.Tests:** 459 / 461 passed (2 new from this batch; 2 pre-existing failures)

**Pre-existing failures (unrelated to this batch):**
- `Hrot.SimHost.Tests`: 2 failures in `MissionPlanTranslatorTests` — present before any changes in this batch.

**Key Test Scenarios Verified:**

TASK-TI005:
- [x] `Commander` value equals 16
- [x] `AllMilitary` does not include `Commander`

TASK-TI006:
- [x] `BehaviorCatalog.GetValidBehaviors(MilitaryApc)` contains "DefendArea"
- [x] `BehaviorCatalog.GetValidBehaviors(CivilianCar)` does NOT contain "DefendArea"

TASK-TI004:
- [x] SC-1: Valid `BehaviorId` → `AssignTacticalIntentEvent` published with correct `IntentId`; no `AssignBehaviorEvent` emitted
- [x] SC-3: Empty `BehaviorId` → no event published

---

## Files Changed

### New Files

| File | Purpose |
|------|---------|
| `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/Intents/DefendAreaIntentDto.cs` | Intent DTO with `[BehaviorContract]` for auto-discovery (TASK-TI006) |
| `Hrot/Engine/Hrot.Core.Tests/BehaviorCategoryTests.cs` | Tests for TASK-TI005 (2 tests) |
| `Hrot/Engine/Hrot.Core.Tests/DefendAreaIntentDtoTests.cs` | Tests for TASK-TI006 (2 tests) |

### Modified Files

| File | Change Summary |
|------|---------------|
| `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/BehaviorCategory.cs` | Added `Commander = 1 << 4` after `AllMilitary` |
| `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/BehaviorIds.cs` | Added `DefendArea_Intent = 1000` in new Tactical Intent DTOs section |
| `Hrot/Subsystems/Hrot.CGF/Systems/MissionAdapterSystem.cs` | Removed `_behaviorRegistry` and `_entityMap` fields and constructor params; added parameterless constructor; replaced `AssignBehaviorEvent` publication with `AssignTacticalIntentEvent`; merged jsonParams extraction and event publication into single `if` block; removed unused `Fdp.Toolkit.Behavior` and `Fdp.Toolkit.Replication.Services` usings; updated XML doc |
| `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs` | Changed `new MissionAdapterSystem(behaviorRegistry, entityMap)` to `new MissionAdapterSystem()` |
| `Hrot/Subsystems/Hrot.SimHost.Integration.Tests/Infrastructure/SimHostInstance.cs` | Changed `new MissionAdapterSystem(_behaviorRegistry, _entityMap)` to `new MissionAdapterSystem()` |
| `Hrot/Subsystems/Hrot.SimHost.Tests/MissionAdapterSystemTests.cs` | Filled previously-empty file with 2 tests for TI004 |

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

One issue arose during the `MissionAdapterSystem.cs` edit: the file used a bare `&` in the XML doc (`<b>Re-commits & State Caching:</b>`) rather than the XML entity `&amp;`. The `replace_string_in_file` tool requires exact text matching, so the first replacement attempt failed because the `oldString` used `&amp;`. The fix was to use `multi_replace_string_in_file` with the exact literal `&` character in the match string.

A second decision point was the hash-change-detection logic refactor. The original code computed `currentDefHash` from both `jsonParams` and `phase.BehaviorId` in a single flat block after the task extraction. After removing the registry lookup and merging the publication into the task-extraction `if` block, the `else` branch (no `ActiveMissionPlan`) needed a separate (simpler) hash computed from `phase.BehaviorId` alone. This correctly preserves the "skip if nothing changed" behaviour for both code paths.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- The `_entityMap` field was in `MissionAdapterSystem` but was never used in `Execute`. This is the kind of drift that occurs when a field is added speculatively (or carried over from a refactor) without being used. A compiler warning (or a code-review checklist item for unused private fields) would have surfaced this earlier.

- `BehaviorCatalog` only maps three categories (`MilitaryApc`, `Infantry`, `Insurgent`) in its `BuildMap` static method. The new `Commander` flag (TI005) is intentionally excluded — but the exclusion is implicit (the categories array just doesn't include it). A short comment in `BuildMap` explaining why `Commander` is absent would prevent future confusion.

**Q3: What design decisions did you make beyond the instructions?**

- **Merged jsonParams extraction and event publication into a single `if` block:** The original code had a two-step structure — extract `jsonParams` in one `if`, compute the hash outside that `if`, then publish. After the change, both extraction and publication require the task object, so merging them into one block is cleaner and avoids accessing `activePlan.Plan.Tasks[queue.CurrentPhase]` twice. The `else` branch handles the case where there is no `ActiveMissionPlan`, preserving the existing change-detection semantics.

- **Used `RegisterManagedComponent<ActiveMissionPlan>()` in the test helper instead of `RegisterComponent<>`.** The batch instructions showed `RegisterComponent`, but every other test in `Hrot.SimHost.Tests` that registers `ActiveMissionPlan` uses `RegisterManagedComponent`, which is correct for reference-type components. Following the pattern in `MissionControlExecutionSystemTests.cs` and `MissionControlRequestSystemFollowRouteTests.cs` is more robust. The `MissionAdapterSystem.Execute` uses `repo.GetComponent<ActiveMissionPlan>(entity)` which works for managed components in FDP's unified `EntityRepository` API.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- **Whitespace-only BehaviorId:** The guard is `string.IsNullOrWhiteSpace(task.BehaviorId)`, not just `string.IsNullOrEmpty`. This means a BehaviorId of `"   "` will also be silently skipped. This matches the batch instructions but is worth noting: an accidentally whitespace-padded BehaviorId in a scenario file would produce no error — the mission phase would simply not trigger an intent.

- **No `ActiveMissionPlan` at all:** If an entity is in the query (has `MissionPlanQueue` + `BehaviorState`) but has never had an `ActiveMissionPlan` set, `repo.GetComponent<ActiveMissionPlan>(entity)` returns `null`. The null check `activePlan?.Plan?.Tasks != null` handles this safely, falling through to the `else` branch that still updates `adapterState` without publishing an event.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

None specific to this batch. The system now makes fewer calls per entity (no registry `TryGetDefinition` lookup) so it is strictly faster than the previous implementation on the hot path.

---

## Outstanding Issues / Next Steps

- [ ] `BehaviorCatalog.BuildMap()` should have a comment explaining that `Commander` is intentionally excluded from the iterated categories.
- [ ] The test `SC-2` (whitespace BehaviorId) is not explicitly covered in the test file — only `string.Empty` is tested. A third test for `"   "` could be added to document the whitespace-trimming behavior.
- [ ] `MissionPlanTranslatorTests` (2 pre-existing failures) are out of scope for this batch but should be tracked.
- [ ] A future batch (TASK-TI011) will add a `DefendAreaMapper` that registers against the `TacticalIntentMapperRegistry` and maps `"DefendArea"` intents to a concrete `AssignBehaviorEvent`.

---

## Suggested Commit Message

```
feat(tactical-intent): Commander category, DefendAreaIntentDto, MissionAdapterSystem wiring (BATCH-02)

TASK-TI005: Add Commander = 1 << 4 to BehaviorCategory; excluded from AllMilitary
TASK-TI006: Reserve BehaviorIds.DefendArea_Intent = 1000; add DefendAreaIntentDto
            with [BehaviorContract(AllMilitary)] for auto-discovery by BehaviorCatalog
TASK-TI004: MissionAdapterSystem now publishes AssignTacticalIntentEvent instead of
            AssignBehaviorEvent; remove _behaviorRegistry and _entityMap (unused);
            parameterless constructor; BehaviorId guard via IsNullOrWhiteSpace

- CgfLogicPack: new MissionAdapterSystem() (no args)
- SimHostInstance: new MissionAdapterSystem() (no args)
- 6 new tests (2 + 2 + 2), all passing

Pre-existing failures in MissionPlanTranslatorTests are unrelated and unchanged.
```
