# BATCH-07: DEM1-D008 + BS-1 mission trigger debt + DEM1 doc alignment

**Batch Number:** BATCH-07  
**Tasks:** BS-1 (`MissionTrigger` obsolete usage) · DEM1 doc fix (`IgAltitudeBaselineEstablished`) · **DEM1-D008** (ParallelStories)  
**Phase:** DEM1 Phase 4 wrap / Phase 5 prep  
**Estimated Effort:** 8–12 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-06 approved

---

## 📋 Onboarding & Workflow

### Developer Instructions

Complete **corrective / documentation work first**, then **DEM1-D008**. All tasks must leave `dotnet test` green for `Fdp.Examples.Scenarios.Tests` and any test project you modify.

### Required Reading (IN ORDER)

1. `.dev-workstream/guides/CODE-STANDARDS.md`
2. `.dev-workstream/reviews/BATCH-06-REVIEW.md`
3. `docs/demos-1/DEM1-TASK-DETAIL.md` — § DEM1-D008 (`ParallelStories`)
4. `docs/demos-1/DEM1-DESIGN.md` — §6.3 ParallelStories
5. `.dev-workstream/DEBT-TRACKER.md` — rows with **Target Fix = BATCH-07** (and BS-1 `ReachedDestination` row updated to BATCH-07)

### Source Code Location

- **Mission / replication debt:** `Hrot.Map.Common/Replication/Egress/EntityMissionEgressTranslator.cs`, `SimHostInstance` (search repo for `MissionTrigger.ReachedDestination`)
- **Replay scenario (new):** `FDP/Examples/Fdp.Examples.Scenarios/Replay/ParallelStoriesScenario.cs`
- **Tests:** `FDP/Examples/Fdp.Examples.Scenarios.Tests/ScenarioTests.cs`
- **Runner registration:** `FDP/Examples/Fdp.Examples.Runner/ScenarioRegistry.cs` — add `ScenarioNames.ParallelStories` when scenario exists
- **Documentation:** `docs/demos-1/DEM1-TASK-DETAIL.md` — Terrain / jump-rejection bootstrap (align with `GroundClampingState.IgAltitudeBaselineEstablished` in `FDP/Toolkits/Fdp.Toolkit.Geographic`)

### Report / Questions

- Report: `.dev-workstream/reports/BATCH-07-REPORT.md`
- Questions: `.dev-workstream/questions/BATCH-07-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Task 1** complete → all relevant tests pass  
2. **Task 2** complete → docs consistent, no broken links  
3. **Task 3** complete → new scenario tests + registry + full `Fdp.Examples.Scenarios.Tests` pass  

---

## ✅ Tasks

### Task 1: [CORRECTIVE] Replace `MissionTrigger.ReachedDestination` with `BehaviorFinished`

**Debt:** `.dev-workstream/DEBT-TRACKER.md` — BS-1-BATCH-06 row (Target BATCH-07)

**Files:** At minimum `EntityMissionEgressTranslator.cs` and `SimHostInstance` usages reported in the tracker; **`grep` the repo** for `ReachedDestination` and fix every **mission-plan / egress** site that still maps obsolete triggers, per `MissionDirectorSystem` / BS1-T022 comments.

**Tests:** Run affected solution tests (e.g. `Hrot.Map` / SimHost test projects if present). Ensure **zero new `CS0618`** from these call sites in the touched projects.

---

### Task 2: [DOCS] Align DEM1 terrain jump-rejection text with engine

**File:** `docs/demos-1/DEM1-TASK-DETAIL.md` (DEM1-D007 section and any duplicate bootstrap wording)

**Description:** Document that jump-rejection bootstrap uses `GroundClampingState.IgAltitudeBaselineEstablished` (not `LastValidIgAltitude == 0`). Keep task IDs and test names unchanged unless the lead approves a rename.

**Success:** Markdown accurately describes current `TerrainQueryResolutionSystem` behavior; reference `FDP/Toolkits/Fdp.Toolkit.Geographic/Components/GroundClampingState.cs`.

---

### Task 3: `ParallelStoriesScenario` (`DEM1-D008`)

**Scope:** `docs/demos-1/DEM1-TASK-DETAIL.md` § DEM1-D008

**Implement:**

1. `Configure`: Phase A — separate `liveWorld` / `liveKernel` with `GroundKinematicsModule` + `RecordingModule`, drive vehicle **50 ticks**, record `Dictionary<uint, Vector3>` positions, dispose live kernel and flush recording.  
2. Configure **main** scenario kernel with `ReplayModule(recFilePath, world)` — **no** CarKinem in replay path.  
3. `EvaluateTick`: compare replay `SimTransform` to stored live trajectory at ticks **25** and **50**; delete temp `.fdprec` on success.

**Tests (names must match task detail):**

- `ParallelStories_RunToCompletion_ExitsZero`
- `ParallelStories_ReplayMatchesLiveAtTick25`
- `ParallelStories_NoCarKinimSystemsInReplayKernel`

**Registry:** Register `ScenarioNames.ParallelStories` in `ScenarioRegistry.cs`.

---

## 🧪 Testing

```powershell
dotnet test "FDP\Examples\Fdp.Examples.Scenarios.Tests\Fdp.Examples.Scenarios.Tests.csproj"
```

Add/update other test projects if Task 1 touches them.

---

## 🎯 Success Criteria

- [ ] Task 1: obsolete `ReachedDestination` usage resolved in scoped files; DEBT row marked ✅ in tracker when merged.  
- [ ] Task 2: DEM1-TASK-DETAIL reflects `IgAltitudeBaselineEstablished`.  
- [ ] Task 3: DEM1-D008 + three tests + runner registration; tracker checkbox for DEM1-D008 updated in lead review.  
- [ ] `.dev-workstream/reports/BATCH-07-REPORT.md` with developer insights.

---

## 📚 References

- `docs/demos-1/DEM1-TASK-TRACKER.md`
- `docs/demos-1/DEM1-DESIGN.md`
