# BATCH-01: Foundation — `Hrot.UI.Common` Project + `BehaviorCatalog` + `BehaviorRegistry` Extension

**Batch Number:** BATCH-01  
**Tasks:** EDIT1-L001, EDIT1-L002, EDIT1-L003  
**Phase:** Phase 0 — `Hrot.UI.Common` Shared Library Foundation  
**Estimated Effort:** 6–8 hours  
**Priority:** CRITICAL — all subsequent batches depend on this  
**Dependencies:** None (first batch)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch lays the **foundation** for the entire `edit-1` workstream.  
You are creating the contract layer — the shared project and all Port interfaces — that  
every subsequent panel migration and adapter implementation depends on.  
Nothing else can be built until these three tasks are complete and compiling cleanly.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.github/skills/developer/SKILL.md` — how to work with batches
2. **Design Document:** `.dev/edit-1/DESIGN.md` — architecture overview, §Phase 0 (§0.A, §0.B, §0.C)
3. **Task Definitions:** `.dev/edit-1/TASK-DETAIL.md` — EDIT1-L001, EDIT1-L002, EDIT1-L003

### Source Code Locations

- **New project (create):** `Hrot.UI.Common/` (root of repo)
- **Existing project (modify):** `Hrot.Map.Definitions/` — add `BehaviorCatalog.cs`
- **Existing file (modify):** `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorRegistry.cs` — add `GetRegisteredNames()`
- **FDP submodule root:** `FDP/` (this is a git submodule; changes to it require their own submodule commit on the dev branch)
- **Solution file:** `IOS-IG-SimHost.sln` — add `Hrot.UI.Common` project reference
- **Test project for Map.Definitions:** `Hrot.Map.Common.Tests/` — add `BehaviorCatalogTests.cs`
- **Test project for FDP.Toolkit.Behavior:** No dedicated test project exists yet; use `Hrot.ClusterRunner.Tests/` or create `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/` if needed. Check how other FDP toolkit tests are structured first.

### Run tests with

```powershell
# Build
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln

# Test (run these repeatedly after each task)
dotnet test Hrot.Map.Common.Tests --no-build
dotnet test Hrot.ClusterRunner.Tests --no-build
```

### Important Codebase Facts

- `TkbEntityTypes.cs` in `Hrot.Map.Definitions/` is under namespace `Hrot.Map.Common` (note: different from folder name).
  Current entries do NOT include `CivilianPedestrian`, `CivilianCar`, `MilitaryApc`, `InfantrySoldier`, `Insurgent`.
  **You must add these constants** — see DESIGN.md §0.B for the required names.
  Choose non-colliding `long` values (existing values: 100–on, 200–on, 301–on, 8801–on).
- `BehaviorRegistry.TryGetId(string, out int)` already exists in `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorRegistry.cs`.
  Only `GetRegisteredNames()` is missing.
- The `Hrot.UI.Common` project must NOT reference `Hrot.ExCon`, `CycloneDDS`, or `ModuleHost`.
- `Hrot.ExCon` already has `IMapPickService.cs` and `IMissionEditorService.cs` in its `Services/` folder.
  The new interfaces in `Hrot.UI.Common.Facades` are **different** (broader or aligned to the shared design).
  Study the existing ExCon interfaces before writing the new ones to ensure consistency.
- FDP submodule is at `FDP/` — it should be on a development branch (not detached HEAD).  
  Check with `cd FDP ; git status` before making any changes.

### Report Submission

Submit report to: `.dev/edit-1/reports/BATCH-01-REPORT.md`  
Questions (if absolutely necessary): `.dev/edit-1/questions/BATCH-01-QUESTIONS.md`

---

## Context

`edit-1` extracts shared ImGui panels from `Hrot.ExCon` into a new `Hrot.UI.Common` library
and adds new Editor authoring features (embarkation, zone editing, target seeding).  
The full architecture is documented in `.dev/edit-1/DESIGN.md`.

This batch delivers the **contracts** (interfaces + DTOs + catalog) that every later phase depends on.  
No panel or adapter implementations go in this batch.

---

## 🎯 Batch Objectives

1. **EDIT1-L001** — Create `Hrot.UI.Common` project with all nine Port interfaces and three shared DTOs.
2. **EDIT1-L002** — Add `BehaviorCatalog` static class to `Hrot.Map.Definitions` and add missing `TkbEntityTypes` constants.
3. **EDIT1-L003** — Add `GetRegisteredNames()` to `BehaviorRegistry` in FDP.Toolkit.Behavior.

---

## ✅ Tasks

### Task 1: EDIT1-L001 — Create `Hrot.UI.Common` Project & All Facade Interfaces

**Full task spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-L001  
**Design reference:** `.dev/edit-1/DESIGN.md` §0.A

**Summary of scope:**
- New project: `Hrot.UI.Common/Hrot.UI.Common.csproj` (class library, same target framework as solution — check `Directory.Build.props`)
- Add project references: `FDP/Toolkits/FDP.Toolkit.ImGui/FDP.Toolkit.ImGui.csproj`, `Hrot.NED/Hrot.NED.csproj`, `Hrot.Map.Definitions/Hrot.Map.Definitions.csproj`
- Add `Hrot.UI.Common` to `IOS-IG-SimHost.sln`
- Create **nine** interfaces in `Hrot.UI.Common/Facades/` (see TASK-DETAIL for full signatures):
  - `ISpawnController`, `IMissionEditorService`, `IOrbatDataProvider`, `IOrbatController`
  - `IMapConfigController`, `IPreviewController`, `IZoneAuthoringController`
  - `IMapPickService`, `IEntityActionController`
- Create **three** DTOs in `Hrot.UI.Common/Models/`:
  - `OrbatNodeViewModel` (sealed record), `MapLayerState` (record), `MissionCommitResult` (record)

**Key constraints:**
- Namespace: `Hrot.UI.Common.Facades` for interfaces, `Hrot.UI.Common.Models` for DTOs
- **Zero references** to `Hrot.ExCon`, `CycloneDDS`, `ModuleHost` anywhere in the project
- Interface method signatures must match exactly what DESIGN.md §0.A specifies
- Before writing `IMapPickService`, study `Hrot.ExCon/Services/IMapPickService.cs` to understand the existing pattern; the new interface adds `PickAreaEntitiesAsync` and uses the same `GeoPoint`/`CancellationToken` types

**Tests required (verify via `dotnet build`):**
- `Hrot.UI.Common` builds with zero warnings
- Any project that adds a `<ProjectReference>` to `Hrot.UI.Common` can resolve all interfaces
- Verify `IMapPickService` has exactly three methods, `IOrbatController` has `RequestEmbark` and `RequestDisembark`

---

### Task 2: EDIT1-L002 — `BehaviorCatalog` in `Hrot.Map.Definitions`

**Full task spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-L002  
**Design reference:** `.dev/edit-1/DESIGN.md` §0.B

**Summary of scope:**
- Add missing TKB type constants to `Hrot.Map.Definitions/TkbEntityTypes.cs`:
  `CivilianPedestrian`, `CivilianCar`, `MilitaryApc`, `InfantrySoldier`, `Insurgent`  
  (choose non-colliding long values, e.g. 501–505 range)
- Create `Hrot.Map.Definitions/Tkb/BehaviorCatalog.cs` — `public static class BehaviorCatalog`
- Implement `GetValidBehaviors(long tkbType) → IReadOnlyList<string>` using a C# 12 switch expression
- Back each returned list with a `private static readonly` field to avoid per-call allocation
- Cover all entity types specified in TASK-DETAIL §EDIT1-L002

**Tests required (write in `Hrot.Map.Common.Tests/`):**
1. `BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.Insurgent)` returns list containing `"Ambush"` and NOT containing `"WanderCivil"`
2. `BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.CivilianPedestrian)` returns list containing `"WanderCivil"` and NOT containing `"Ambush"`
3. `BehaviorCatalog.GetValidBehaviors(-999L)` returns fallback list containing `"MoveToLocation"`
4. Same list instance is returned on repeated calls (no per-call allocation — verify with `object.ReferenceEquals`)

---

### Task 3: EDIT1-L003 — `BehaviorRegistry.GetRegisteredNames()`

**Full task spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-L003  
**Design reference:** `.dev/edit-1/DESIGN.md` §0.C

**Summary of scope:**
- File: `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorRegistry.cs`
- Add single public method: `public IReadOnlyList<string> GetRegisteredNames()`
- Implementation: `_nameToId.Keys.ToList()` (snapshot — cannot mutate the registry)
- `TryGetId` already exists — no change needed

**⚠️ FDP submodule note:**  
`FDP/` is a git submodule. After making changes, commit inside `FDP/` submodule first  
(on its dev branch, not detached HEAD), then stage the submodule pointer update in the  
top-level repo. Both commits are needed.

**Tests required (write in `Hrot.ClusterRunner.Tests/` or a new toolkit test project):**
1. Register two behaviors with different names → `GetRegisteredNames()` returns both names
2. Empty registry → `GetRegisteredNames()` returns empty list (not null)

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1 (EDIT1-L001):** Implement → `dotnet build` clean ✅
2. **Task 2 (EDIT1-L002):** Implement → Write tests → **ALL tests pass** ✅
3. **Task 3 (EDIT1-L003):** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written and passing
- ✅ **ALL tests passing** (including previous tasks)

Do NOT stop and ask for permission to run tests, fix compile errors, or proceed to the next task.  
Work autonomously until all tasks are complete and all tests pass, then write your report.

---

## 🧪 Testing Requirements

- Minimum **7 meaningful unit tests** across Tasks 2 and 3
- Tests must verify **actual behavior** (return values, invariants), not just compilation
- No tests that only verify `new X() != null`
- All new tests must be in an appropriate existing test project (see Source Code Locations above)
- Run the full test suite before submitting to confirm no regressions

---

## ⚠️ Quality Standards

**Code quality:**
- All public APIs must have XML `<summary>` comments
- Follow the exact same style and namespace patterns as existing files in the same project
- No compiler warnings (treat as errors)

**Test quality:**
- **NOT ACCEPTABLE:** Test that only calls a constructor and checks not-null
- **REQUIRED:** Tests that verify the actual contract (correct list contents, no allocations on repeated calls, empty returns empty not null)

---

## 📊 Developer Insights (Required in Report)

Your report must answer ALL of the following:

**Q1:** What issues did you encounter during implementation? How did you resolve each one?

**Q2:** Did you spot any weak points in the existing codebase structure  
(e.g. in `BehaviorRegistry`, `TkbEntityTypes`, or the FDP submodule workflow)?  
What would you improve?

**Q3:** What design decisions did you make beyond the instructions?  
(e.g. namespace layout, DTO mutability, interface ordering)  
What alternatives did you consider?

**Q4:** Did you encounter any gaps between the TASK-DETAIL spec and the actual codebase state?  
(e.g. missing types, different class locations, naming mismatches)  
How did you handle them?

**Q5:** What would be the highest-risk items for the next batch (panel migration)?  
What should the next developer watch out for?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `Hrot.UI.Common` project exists, builds with zero warnings, and is added to `IOS-IG-SimHost.sln`
- [ ] All nine Port interfaces compile with correct signatures
- [ ] All three DTOs compile as records
- [ ] `TkbEntityTypes` has constants for `CivilianPedestrian`, `CivilianCar`, `MilitaryApc`, `InfantrySoldier`, `Insurgent`
- [ ] `BehaviorCatalog.GetValidBehaviors` returns correct behavior lists per TKB type
- [ ] `BehaviorRegistry.GetRegisteredNames()` returns a snapshot of registered behavior names
- [ ] All unit tests pass (minimum 7 tests covering Tasks 2 and 3)
- [ ] No regressions in existing test suites
- [ ] Report submitted to `.dev/edit-1/reports/BATCH-01-REPORT.md`

---

## 📚 Reference Materials

- **Task Specs:** `.dev/edit-1/TASK-DETAIL.md` — §EDIT1-L001, §EDIT1-L002, §EDIT1-L003
- **Design:** `.dev/edit-1/DESIGN.md` — §Phase 0, §0.A, §0.B, §0.C, Component Map
- **Existing TKB types:** `Hrot.Map.Definitions/TkbEntityTypes.cs`
- **Existing BehaviorRegistry:** `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorRegistry.cs`
- **Existing BehaviorConstants:** `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorConstants.cs`
- **Pattern reference for interfaces:** `Hrot.ExCon/Services/IMapPickService.cs`, `Hrot.ExCon/Services/IMissionEditorService.cs`
- **Solution file:** `IOS-IG-SimHost.sln`
- **Framework version:** Check `Directory.Build.props` at repo root
