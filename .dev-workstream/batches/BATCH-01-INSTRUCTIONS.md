# BATCH-01: Universal Spatial Primitives

**Batch Number:** BATCH-01  
**Tasks:** BCS-P0-T1, BCS-P0-T2, BCS-P0-T3, BCS-P0-T4, BCS-P0-T5, BCS-P0-T6  
**Phase:** Phase 0 — Universal Spatial Primitives  
**Estimated Effort:** 8–10 hours  
**Priority:** HIGH — gates all subsequent phases  
**Dependencies:** None (first batch)

---

## 📋 Onboarding & Workflow

### Developer Instructions

Phase 0 standardises how every entity in FDP represents its world position, rotation, and velocity. Two new kernel components (`SimTransform`, `SimVelocity`) replace all ad-hoc position/velocity structs scattered across existing toolkits and example apps. Nothing from Phase 1 onwards can start until this phase compiles clean and all green tests are confirmed.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev-workstream/guides/README.md` (if present) — how to work with batches  
2. **Onboarding:** `FDP/Docs/projects/behavior-control/ONBOARDING.md` — project overview, where everything lives, zero-alloc and 256-component rules  
3. **Design §2 (full section):** `FDP/Docs/projects/behavior-control/DESIGN.md` — §2 "Universal Spatial Primitives" (lines 22–96); covers new components, bridge math, SpatialHash impact, example app migrations, cache efficiency rationale  
4. **Task Details Phase 0:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — section "Phase 0" (lines 8–243); contains file paths, exact struct definitions, bridge code patterns, and all success-condition tests  

### Source Code Locations

| Area | Path |
|---|---|
| New component file | `FDP/Kernel/Fdp.Kernel/CoreComponents/SimComponents.cs` ← **create** |
| VehicleState | `FDP/Toolkits/FDP.Toolkit.CarKinem/Core/VehicleState.cs` |
| CarKinematicsSystem | `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/CarKinematicsSystem.cs` |
| SpatialHashSystem | `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/SpatialHashSystem.cs` |
| CarKinem example | `FDP/Examples/Fdp.Examples.CarKinem/` |
| BattleRoyale example | `FDP/Examples/Fdp.Examples.BattleRoyale/` |
| NetworkDemo example | `FDP/Examples/Fdp.Examples.NetworkDemo/` |
| Solution file | `FDP/FDP.sln` |
| Kernel test project | `FDP/Kernel/Fdp.Kernel.Tests/` (create if absent) |
| CarKinem test project | `FDP/Toolkits/FDP.Toolkit.CarKinem.Tests/` |

### Build & Test Commands

```powershell
# Build from FDP root
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln

# Run all tests
dotnet test FDP.sln

# Run targeted test projects (during development)
dotnet test Kernel/Fdp.Kernel.Tests/
dotnet test Toolkits/FDP.Toolkit.CarKinem.Tests/
dotnet test Examples/Fdp.Examples.BattleRoyale/
dotnet test Examples/Fdp.Examples.NetworkDemo.Tests/
```

### Report Submission

When done, submit your report to:  
`.dev-workstream/reports/BATCH-01-REPORT.md`

If you have questions, create:  
`.dev-workstream/questions/BATCH-01-QUESTIONS.md`

---

## Context

Phase 0 is a **prerequisite refactor**, not a feature. It does not add visible behaviour — it creates the shared vocabulary (`SimTransform`, `SimVelocity`) that every behavior toolkit in Phases 1–7 will depend on.

**Related tasks:**
- [BCS-P0-T1](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p0-t1--simtransform--simvelocity-in-fdpkernel) — New kernel components  
- [BCS-P0-T2](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p0-t2--refactor-vehiclestate-and-carkinematicssystem) — VehicleState shrink + CarKinematicsSystem bridge  
- [BCS-P0-T3](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p0-t3--refactor-spatialhashsystem-to-use-simtransform) — Universal SpatialHashSystem  
- [BCS-P0-T4](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p0-t4--migrate-fdpexamplescarkinem) — Migrate CarKinem example  
- [BCS-P0-T5](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p0-t5--migrate-fdpexamplesbattleroyale) — Migrate BattleRoyale example  
- [BCS-P0-T6](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p0-t6--migrate-fdpexamplesnetworkdemo) — Migrate NetworkDemo example  

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **BCS-P0-T1:** Create `SimComponents.cs` → Write size/type tests → **ALL tests pass** ✅
2. **BCS-P0-T2:** Refactor `VehicleState` + `CarKinematicsSystem` → Write bridge tests → **ALL tests pass** ✅
3. **BCS-P0-T3:** Refactor `SpatialHashSystem` → Write universal-query tests → **ALL tests pass** ✅
4. **BCS-P0-T4:** Migrate `Fdp.Examples.CarKinem` → `dotnet build` zero errors → **ALL tests pass** ✅
5. **BCS-P0-T5:** Migrate `Fdp.Examples.BattleRoyale` → Delete local structs → **ALL tests pass** ✅
6. **BCS-P0-T6:** Migrate `Fdp.Examples.NetworkDemo` → Delete target structs → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete  
- ✅ Current task tests written  
- ✅ **ALL tests passing** (i.e. full solution test run is green)

**Why:** Tasks 2–6 depend on T1. Tasks 4–6 depend on T2+T3. A cascade failure here blocks the whole project.

---

## 🎯 Batch Objectives

- `SimTransform` and `SimVelocity` exist in `Fdp.Kernel` namespace, zero external dependencies.
- `VehicleState` no longer contains `Position`, `Forward`, `Pitch`, or `Roll`.
- `CarKinematicsSystem` reads `SimTransform`/`SimVelocity` as I/O, using a 2D↔3D math bridge.
- `SpatialHashSystem` queries `SimTransform` exclusively (all entity types participate in the grid).
- All three example apps compile and their existing tests remain green.
- No new compiler warnings introduced anywhere in the solution.

---

## ✅ Tasks

### Task 1: SimTransform / SimVelocity (BCS-P0-T1)

**File to create:** `FDP/Kernel/Fdp.Kernel/CoreComponents/SimComponents.cs`  
**Task Definition:** [TASK-DETAIL.md §BCS-P0-T1](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p0-t1--simtransform--simvelocity-in-fdpkernel)

The exact struct definitions, XML doc comments, and namespace placement are specified in TASK-DETAIL.md lines 22–53. Follow them precisely. Key constraints:
- No external toolkit dependencies — `Fdp.Kernel` only, `System.Numerics` only.
- Register both component types through whatever `ComponentRegistrar` / kernel bootstrap already exists in `Fdp.Kernel` (look at how existing built-in components like `IsActiveTag` are registered).

**Tests required** (new file `FDP/Kernel/Fdp.Kernel.Tests/SimComponentTests.cs`):  
See exact test code in TASK-DETAIL.md lines 57–67.

---

### Task 2: VehicleState Refactor + CarKinematicsSystem Bridge (BCS-P0-T2)

**Files to modify:**
- `FDP/Toolkits/FDP.Toolkit.CarKinem/Core/VehicleState.cs`  
- `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/CarKinematicsSystem.cs`

**Task Definition:** [TASK-DETAIL.md §BCS-P0-T2](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p0-t2--refactor-vehiclestate-and-carkinematicssystem)

The before/after struct layout, bridge math snippet (3D→2D input; 2D→3D output), and test scaffolding are fully specified in TASK-DETAIL.md lines 79–130. Do not duplicate the math here — read the source.

Key points not repeated in the task doc:
- `CarKinematicsSystem` must also include `SimTransform` and `SimVelocity` in its ECS query (both With\<\>).
- The existing test project is `FDP/Toolkits/FDP.Toolkit.CarKinem.Tests/`. Add the new test file there: `VehicleStateRefactorTests.cs`.

**Tests required:** See TASK-DETAIL.md lines 112–130.

---

### Task 3: SpatialHashSystem — Universal Query (BCS-P0-T3)

**File to modify:** `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/SpatialHashSystem.cs`

**Task Definition:** [TASK-DETAIL.md §BCS-P0-T3](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p0-t3--refactor-spatialhashsystem-to-use-simtransform)

The current system queries `VehicleState` and reads `state.Position` (a `Vector2`). Replace with `SimTransform` query and extract `new Vector2(pos.X, pos.Y)`. New query pattern is shown in TASK-DETAIL.md lines 142–149.

**Tests required** (add to CarKinem test project): See TASK-DETAIL.md lines 155–165. Two tests: one for a pure non-vehicle entity, one for a vehicle entity — both must appear in grid queries after the system runs.

---

### Task 4: Migrate Fdp.Examples.CarKinem (BCS-P0-T4)

**Directory:** `FDP/Examples/Fdp.Examples.CarKinem/`  
**Task Definition:** [TASK-DETAIL.md §BCS-P0-T4](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p0-t4--migrate-fdpexamplescarkinem)

The changes required are described in TASK-DETAIL.md lines 174–190. Summary: entity spawn sites must add `SimTransform`/`SimVelocity` components separately; any visualiser or adapter that reads `VehicleState.Position` or `VehicleState.Forward` must be updated to read from `SimTransform` instead.

**Success condition:** `dotnet build Examples/Fdp.Examples.CarKinem/` → zero errors. Add/update `VehicleVisualizerTests.cs` per TASK-DETAIL.md lines 185–189.

---

### Task 5: Migrate Fdp.Examples.BattleRoyale (BCS-P0-T5)

**Directory:** `FDP/Examples/Fdp.Examples.BattleRoyale/`  
**Files to delete:**
- `FDP/Examples/Fdp.Examples.BattleRoyale/Components/Position.cs`  
- `FDP/Examples/Fdp.Examples.BattleRoyale/Components/Velocity.cs`

**Task Definition:** [TASK-DETAIL.md §BCS-P0-T5](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p0-t5--migrate-fdpexamplesbattleroyale)

Migration pattern is in TASK-DETAIL.md lines 203–212. After deleting local structs, update all usages throughout the project to use `SimTransform` / `SimVelocity` from `Fdp.Kernel`. Note `PositionGeodetic` (if it appears in neighbouring projects) is NOT replaced.

**Success condition:** `dotnet build` + `dotnet test` on the project both pass. Existing BattleRoyale tests must stay green.

---

### Task 6: Migrate Fdp.Examples.NetworkDemo (BCS-P0-T6)

**Directory:** `FDP/Examples/Fdp.Examples.NetworkDemo/`  
**Files to modify or delete:**
- `FDP/Examples/Fdp.Examples.NetworkDemo/Components/DemoPosition.cs` — **delete**  
- `FDP/Examples/Fdp.Examples.NetworkDemo/Components/DemoComponents.cs` — remove `Position` and `Velocity` structs only (keep the rest of the file)

**Task Definition:** [TASK-DETAIL.md §BCS-P0-T6](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p0-t6--migrate-fdpexamplesnetworkdemo)

`PositionGeodetic` (WGS84) must be left untouched — it is a domain concept, not a spatial primitive.

**Success condition:**
```powershell
dotnet build Examples/Fdp.Examples.NetworkDemo/     # zero errors
dotnet test  Examples/Fdp.Examples.NetworkDemo.Tests/ # all pass
```

---

## 🧪 Testing Requirements

- **Minimum:** 8 unit tests total across the new test files (see per-task specs above).
- **Quality bar:** Tests must exercise actual values — size assertions, component presence in grid results, movement after system update. Do not write tests that only check compilation.
- All tests must be in xUnit.
- Run the **full solution test suite** (`dotnet test FDP.sln`) before submission — no pre-existing test may regress.

---

## ⚠️ Quality Standards

**❗ ZERO WARNINGS RULE**  
After migration, the solution must build with zero compiler warnings. Removed fields leave callers broken — fix all of them.

**❗ DO NOT MIX COORDINATE SPACES**  
The 2D↔3D bridge lives exclusively in `CarKinematicsSystem`. Callers that previously read `VehicleState.Position` (Vector2) now read `SimTransform.Position.X/Y`. Do not add implicit conversions or extension methods to paper over this — fix each call site explicitly.

**❗ KEEP `PositionGeodetic` UNTOUCHED**  
It is a WGS84 geographic coordinate, not a flat-Earth spatial primitive. Leave it in place.

**❗ DO NOT REGISTER COMPONENTS TWICE**  
If `Fdp.Kernel` already has a registrar, add `SimTransform` and `SimVelocity` there. Do not create a parallel registration path.

---

## 📊 Report Requirements

Submit `.dev-workstream/reports/BATCH-01-REPORT.md` with:

- **Test results:** total test count, pass/fail (paste `dotnet test FDP.sln` summary).
- **Q1:** What call sites were hardest to migrate? Did you find any `VehicleState.Position`/`.Forward` references in unexpected places?
- **Q2:** What design decisions did you make that the spec left open (e.g. how you handled component registration, how you structured the bridge math)?
- **Q3:** Did you spot any weak points in the existing CarKinem or example app code that would be worth addressing in a later batch?
- **Q4:** Were there any edge cases in the 2D↔3D conversion (e.g. entities with non-zero Z elevation, gimbal lock in `Atan2`) that the spec didn't cover? How did you handle them?

---

## 🎯 Success Criteria

This batch is DONE when:

- [ ] **BCS-P0-T1** — `SimComponents.cs` exists; size and type tests pass
- [ ] **BCS-P0-T2** — `VehicleState` has no `Position`/`Forward`/`Pitch`/`Roll`; `CarKinematicsSystem` bridge tests pass
- [ ] **BCS-P0-T3** — `SpatialHashSystem` queries `SimTransform`; non-vehicle entity found in grid
- [ ] **BCS-P0-T4** — `Fdp.Examples.CarKinem` builds zero errors; `VehicleVisualizerTests` pass
- [ ] **BCS-P0-T5** — `Position.cs` / `Velocity.cs` deleted from BattleRoyale; existing tests still pass
- [ ] **BCS-P0-T6** — `DemoPosition.cs` deleted, `DemoComponents.cs` trimmed; NetworkDemo tests pass
- [ ] **Full solution** — `dotnet build FDP.sln` zero errors, zero warnings; `dotnet test FDP.sln` all green
- [ ] **Report submitted** to `.dev-workstream/reports/BATCH-01-REPORT.md`

---

## 📚 Reference Materials

- **Task Details (Phase 0):** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — lines 8–243
- **Design §2:** `FDP/Docs/projects/behavior-control/DESIGN.md` — lines 22–96
- **Design talk original:** `FDP/Docs/Behavior Control Subsystem Design.json.md` — lines 4804–5258
- **Onboarding:** `FDP/Docs/projects/behavior-control/ONBOARDING.md` — zero-alloc rule, 256-component limit, testing style
- **Existing registration example:** `FDP/Kernel/Fdp.Kernel/IsActiveTag.cs` — how built-in components are declared
- **Existing demo pattern:** `FDP/Examples/Fdp.Examples.BattleRoyale/Program.cs` — kernel setup reference
