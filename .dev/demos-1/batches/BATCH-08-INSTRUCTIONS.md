# BATCH-08: Tech-debt burndown + DEM1 follow-ups + DEM1-D009 start

**Batch Number:** BATCH-08  
**Tasks:** P3 performance (ComponentReflector) · P3 naming (`DemoDoctrineIds`) · DEM1-D008 test/doc hardening · **DEM1-D009** (DistributedTank) scoping  
**Phase:** Cross-cutting debt + DEM1 Phase 5  
**Estimated Effort:** 10–14 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-07 approved

---

## 📋 Onboarding & Workflow

### Developer Instructions

**Complete debt / corrective items first** (Tasks 1–3), then begin **DEM1-D009** only if time remains; if D009 is too large, deliver Tasks 1–3 fully and document remaining D009 scope in the report.

### Required Reading

1. `.dev-workstream/guides/CODE-STANDARDS.md`
2. `.dev-workstream/reviews/BATCH-07-REVIEW.md`
3. `.dev-workstream/DEBT-TRACKER.md` — rows with **Target Fix = BATCH-08**
4. `docs/demos-1/DEM1-TASK-DETAIL.md` — § DEM1-D008 (align text with code), § DEM1-D009
5. `docs/demos-1/DEM1-DESIGN.md` — Phase 5 / DistributedTank

### Report / Questions

- `.dev-workstream/reports/BATCH-08-REPORT.md`
- `.dev-workstream/questions/BATCH-08-QUESTIONS.md`

### Paths (repo root)

- `FDP/Toolkits/FDP.Toolkit.ImGui/Utils/ComponentReflector.cs`
- `FDP/Examples/Fdp.Examples.Scenarios/DemoDoctrineIds.cs` / `FDP/Examples/Fdp.Examples.Common/Constants/DemoDoctrineIds.cs`
- `FDP/Examples/Fdp.Examples.Scenarios/Replay/ParallelStoriesScenario.cs`
- `FDP/Examples/Fdp.Examples.Scenarios.Tests/ScenarioTests.cs`
- `FDP/ModuleHost/ModuleHost.Core/ModuleHostKernel.cs`
- `docs/demos-1/DEM1-TASK-DETAIL.md`

---

## 🔄 MANDATORY WORKFLOW

Sequence: Task 1 → green tests; Task 2 → green tests; Task 3 → green tests; Task 4 as far as feasible.

---

## ✅ Tasks

### Task 1: [DEBT] `ComponentReflector` — eliminate per-frame `AllocHGlobal`

**Debt:** `.dev-workstream/DEBT-TRACKER.md` (BD1-BATCH-03 → BATCH-08)

**File:** `FDP/Toolkits/FDP.Toolkit.ImGui/Utils/ComponentReflector.cs`

**Goal:** Replace repeated `Marshal.AllocHGlobal` in the byte-cache diff path with `stackalloc` (small caps) and/or pooled `NativeArray<byte>` / rented buffers. No behaviour change for ImGui inspector output.

**Tests:** `FDP.Toolkit.ImGui.Tests` — extend or add a test that exercises reflector diffing if coverage is thin; run full ImGui test project.

---

### Task 2: [DEBT] Examples doctrine IDs + `MissionTriggerHelper` CS0618

**2a — `DemoDoctrineIds` duplication**

**Debt:** BATCH-06 naming row (Target BATCH-08)

**Problem:** `Fdp.Examples.Scenarios.DemoDoctrineIds` shadows `Fdp.Examples.Common.Constants.DemoDoctrineIds` for nested test namespaces.

**Options (pick one, document in report):** rename scenarios-local type; merge into Common; avoid `global using` unless unavoidable.

**2b — `MissionTriggerHelper`**

**Debt:** New BATCH-07 review row (Target BATCH-08)

**File:** `Hrot.Map.Common/Helpers/MissionTriggerHelper.cs`

Eliminate **CS0618** on the `"ReachedDestination"` → enum path while preserving wire backward compatibility (coordinate with `EntityMissionIngressTranslator` / `MissionDirectorSystem` BS1-T022 semantics).

**Tests:** `Fdp.Examples.Scenarios.Tests` + `Hrot.Map.Common.Tests` (or full solution subset for Map.Common).

---

### Task 3: [CORRECTIVE] DEM1-D008 hardening — kernel proof + docs

**3a — Tests**

Replace the shallow assertion in `ParallelStories_NoCarKinimSystemsInReplayKernel`: **prove** the main `ModuleHostKernel` after `Configure` has **no** kinematics/car-ground modules (e.g. no module whose `Name` or `Type` matches `LiveKinematics`, `GroundKinematics`, `CarKinematics` per policy).

**Allowed approaches:**

- Add a **small, explicit** read-only query API on `ModuleHostKernel` (e.g. snapshot of registered `IEcsModule.Name` or full type names) intended for tests/diagnostics; **or**
- `InternalsVisibleTo` from `ModuleHost.Core` to a dedicated test assembly only — justify if you choose this.

Remove or repurpose `HasCarKinematicsInMainKernel` if the test queries the kernel directly.

**3b — Documentation**

- Update `docs/demos-1/DEM1-TASK-DETAIL.md` § DEM1-D008 to describe **live** `LiveKinematicsModule` + blocking `AsyncRecorder` (not `GroundKinematicsModule` + `RecordingModule` unless you switch implementation back).
- Fix `ParallelStoriesScenario` XML summary to match code.

**Tests:** `Fdp.Examples.Scenarios.Tests` 100% green.

---

### Task 4: [FEATURE] **DEM1-D009** `DistributedTankScenario` — minimal vertical slice

**Reference:** `docs/demos-1/DEM1-TASK-DETAIL.md` § DEM1-D009, `DEM1-DESIGN.md` §6.4

**Goal:** Land the **smallest defensible slice** that proves two `ModuleHostKernel` instances + Cyclone DDS loopback and at least one authority boundary — **do not** boil the ocean in one batch. If full D009 exceeds estimate, implement Phase A only (harness + handshake skeleton + one xUnit test) and list follow-ups in the report.

**Registry:** When a runnable scenario exists, add `ScenarioNames.DistributedTank` to `ScenarioRegistry.cs`.

**Success:** New tests in `Fdp.Examples.Scenarios.Tests` (or dedicated network demo tests if cleaner); document any deferred scope in `DEM1-TASK-TRACKER.md` via lead review.

---

## 🧪 Testing

```powershell
dotnet test "FDP\Examples\Fdp.Examples.Scenarios.Tests\Fdp.Examples.Scenarios.Tests.csproj"
dotnet test "FDP\Toolkits\FDP.Toolkit.ImGui.Tests\FDP.Toolkit.ImGui.Tests.csproj"
```

Add other projects touched by Task 1 / 4.

---

## 🎯 Success Criteria

- [ ] Task 1: ComponentReflector no longer allocates native heap per frame on the hot path (verify with test or profiler note in report).  
- [ ] Task 2: Single clear doctrine-ID story for Examples scenarios; no accidental shadowing.  
- [ ] Task 3: ParallelStories “no kinematics” test reflects **actual** kernel registration; D008 docs/XML aligned.  
- [ ] Task 4: D009 progress documented; tests green for delivered slice.  
- [ ] `DEBT-TRACKER.md` rows closed or re-targeted by lead after review.  
- [ ] `BATCH-08-REPORT.md` submitted.

---

## ⚠️ Pitfalls

- `AsyncRecorder` **blocking** vs **RecordingModule** — preserve deterministic CI behaviour for ParallelStories when touching recording paths.  
- DDS / network demos: prefer loopback Domain 0 and explicit disposal of Cyclone resources.
