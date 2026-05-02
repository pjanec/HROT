# BATCH-05: Generic Mission Editor UI + Composition Root Wiring

**Batch Number:** BATCH-05
**Tasks:** TASK-C008, TASK-C009, TASK-C010, TASK-C011, DEBT-D005
**Phase:** Phase 5 — Generic Mission Editor UI
**Estimated Effort:** 10-12 hours
**Priority:** HIGH
**Dependencies:** BATCH-02 (C005 behavior DTOs and remapper), BATCH-04 (load handlers)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This is the final batch.  It delivers the generic DTO-driven mission editor UI and
wires everything together in the composition root (`CgfBehaviorSetup`).

Four tasks plus one debt fix:
1. **TASK-C008** — Presentation attributes (`MapPickableWorldLocationAttribute`, `MapPickableEntityAttribute`)
2. **TASK-C009** — `BehaviorUiCompiler` + `BehaviorUiRegistry` + `IPickInteractionContext`
3. **TASK-C010** — `MissionPanel` integration (generic generic draw path, implement `IPickInteractionContext`)
4. **TASK-C011** — `CgfBehaviorSetup` composition root wiring
5. **DEBT-D005** — Wire `ScenarioBehaviorRemapper` into `CgfApplication` load handler constructors

### Required Reading (IN ORDER)

1. **Design:** `.dev/cgf-scn/DESIGN.md` — Decision 7 (DTO-based JSON remapping, same DTOs for UI)
2. **Task Details:** `.dev/cgf-scn/TASK-DETAIL.md` — sections TASK-C008, TASK-C009, TASK-C010, TASK-C011
3. **Previous Reviews:** `.dev/cgf-scn/reviews/BATCH-04-REVIEW.md`

### Existing Files You Must Read Before Coding

| File | What to understand |
|------|-------------------|
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Attributes/RemapNetworkIdAttribute.cs` | The existing attribute pattern to follow for the two new presentation attributes |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Params/FireAtTargetParamsJsonDto.cs` | DTO that gets the new presentation attributes applied |
| `Hrot/Engine/Hrot.Presentation/Panels/MissionPanel.cs` (or `Hrot.UI.Common`) | Current hardcoded mission param UI being replaced — read first to understand the existing structure |
| `Hrot/Subsystems/Hrot.CGF/Configuration/CgfBehaviorSetup.cs` | Where doctines are registered — where you add remapper and UI registry wiring |
| `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs` | Where to pass `ScenarioBehaviorRemapper` through to load handler constructors (DEBT-D005) |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorParamRemapperCompiler.cs` | Existing compiler pattern for TASK-C009 UI compiler |

### Source Code Location

**New files in FDP submodule:**
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Attributes/MapPickableWorldLocationAttribute.cs`
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Attributes/MapPickableEntityAttribute.cs`

**New/modified files in Hrot:**
- `Hrot/Engine/Hrot.Presentation/Behavior/IPickInteractionContext.cs` (NEW) — or in `Hrot.UI.Common`
- `Hrot/Engine/Hrot.Presentation/Behavior/BehaviorUiCompiler.cs` (NEW) — or in `Hrot.UI.Common`
- `Hrot/Engine/Hrot.Presentation/Behavior/BehaviorUiRegistry.cs` (NEW) — or in `Hrot.UI.Common`
- `Hrot/Engine/Hrot.Presentation/Panels/MissionPanel.cs` (MODIFY) — integrate generic UI path
- `Hrot/Subsystems/Hrot.CGF/Configuration/CgfBehaviorSetup.cs` (MODIFY) — register DTOs and UI
- `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs` (MODIFY) — DEBT-D005: wire remapper to handlers

**Test files:**
- `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/PresentationAttributeTests.cs` — C008 tests
- `Hrot/Engine/Hrot.Presentation.Tests/` or `Hrot.SimHost.Tests/` — C009, C010 tests
- `Hrot/Subsystems/Hrot.SimHost.Tests/CgfBehaviorSetupTests.cs` — C011 tests

### Build Commands

```powershell
# From repo root d:\Work\IOS-IG-SimHost-FDP-2

dotnet build IOS-IG-SimHost.sln

dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj
dotnet test Hrot\Engine\Hrot.Presentation.Tests\Hrot.Presentation.Tests.csproj  # if exists
dotnet test Hrot\Subsystems\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj
```

### Report Submission

**When done, submit your report to:**
`.dev/cgf-scn/reports/BATCH-05-REPORT.md`

---

## Context

The generic mission editor UI uses the same `FireAtTargetParamsJsonDto`,
`FollowRouteParamsJsonDto`, `MoveToLocationParamsJsonDto` DTOs that were built for
JSON remapping.  Presentation attributes (`MapPickableEntity`, `MapPickableWorldLocation`)
annotate the DTO properties that need ImGui map-picker controls.  `BehaviorUiCompiler`
uses expression trees (same pattern as `BehaviorParamRemapperCompiler`) to emit compiled
ImGui draw delegates for each DTO type.  `MissionPanel` replaces its hardcoded `DrawXxx`
methods with a `BehaviorUiRegistry` lookup.

**Related Tasks:**
- [TASK-C008](../TASK-DETAIL.md#task-c008--presentation-attributes) — Presentation attributes
- [TASK-C009](../TASK-DETAIL.md#task-c009--behavioruicompiler) — BehaviorUiCompiler
- [TASK-C010](../TASK-DETAIL.md#task-c010--missionpanel-integration) — MissionPanel integration
- [TASK-C011](../TASK-DETAIL.md#task-c011--composition-root-registration) — Composition root

---

## 🎯 Batch Objectives

- Add `MapPickableWorldLocationAttribute` and `MapPickableEntityAttribute` to FDP toolkit
- Apply both to the DTOs from TASK-C005b where appropriate
- Implement `IPickInteractionContext`, `BehaviorUiCompiler`, `BehaviorUiRegistry`
- Integrate generic UI path into `MissionPanel`; implement `IPickInteractionContext`
- Wire remapper and UI registry in `CgfBehaviorSetup`
- DEBT-D005: pass `ScenarioBehaviorRemapper` to load handler constructors in `CgfApplication`
- All tests passing; solution builds cleanly

---

## ✅ Tasks

### Task 1: Presentation Attributes (TASK-C008)

**Files (NEW in `FDP/Toolkits/Fdp.Toolkits/Behavior/Attributes/`):**
- `MapPickableWorldLocationAttribute.cs` — marker attribute, no properties
- `MapPickableEntityAttribute.cs` — accepts optional `string[]` filter presets via vararg ctor

**Also modify existing DTOs in `FDP/Toolkits/Fdp.Toolkits/Behavior/Params/`:**
- `FireAtTargetParamsJsonDto.cs` — apply `[MapPickableEntity]` to `TargetNetworkId`
- `FollowRouteParamsJsonDto.cs` — no `MapPickable*` annotation (target is a route entity ID, not map-pick)
- `MoveToLocationParamsJsonDto.cs` — apply `[MapPickableWorldLocation]` to `TargetLat` and `TargetLon`

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-c008--presentation-attributes)

**Tests Required** (in `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/PresentationAttributeTests.cs`):
1. `typeof(FireAtTargetParamsJsonDto).GetProperty("TargetNetworkId")` has both `MapPickableEntityAttribute` and `RemapNetworkIdAttribute`
2. `MoveToLocationParamsJsonDto` — `TargetLat` and `TargetLon` have `MapPickableWorldLocationAttribute`; no property has `RemapNetworkIdAttribute`
3. `new MapPickableEntityAttribute("roads", "graphs")` — `FilterPresets` contains `["roads", "graphs"]`

### Task 2: BehaviorUiCompiler (TASK-C009)

**Files (NEW in `Hrot/Engine/Hrot.Presentation/Behavior/` or `Hrot/Engine/Hrot.UI.Common/Behavior/`):**
- `IPickInteractionContext.cs`
- `BehaviorUiCompiler.cs` (+ `BehaviorUiDrawDelegate` typedef in same file)
- `BehaviorUiRegistry.cs`

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-c009--behavioruicompiler)

Key design requirements:
- `BehaviorUiDrawDelegate` = `Func<string, int, IPickInteractionContext, string>` (public delegate)
- `IPickInteractionContext` interface has:
  - `bool IsPickPendingFor(int taskIndex, string propertyName)`
  - `void RequestEntityPick(int taskIndex, string propertyName, string[]? filterPresets)`
  - `void RequestLocationPick(int taskIndex, string propertyName)`
- `BehaviorUiCompiler.Compile<TDto>()` — uses expression trees; all reflection at compile time
- Delegate signature: `(string currentJson, int taskIndex, IPickInteractionContext context) → string`
- For `[MapPickableEntity]` properties: ImGui label + button; on click call `context.RequestEntityPick`
- For `[MapPickableWorldLocation]` properties: ImGui label + button; on click call `context.RequestLocationPick`
- For `float`: `ImGui.InputFloat`; `double`: `ImGui.InputDouble`; `int`/`long`: `ImGui.InputText`;
  `bool`: `ImGui.Checkbox`
- No allocation in stable render path (no user input)
- If no registry entry for a behavior, fall through to `DrawRawJsonEditor`
- `BehaviorUiRegistry`: `Dictionary<string, BehaviorUiDrawDelegate>`

**Tests Required** (in nearest available Presentation test project):
1. `Compile<FireAtTargetParamsJsonDto>()` returns a non-null delegate
2. Compiled delegate does not use reflection at render time (verify via PropertyInfo.GetValue counter = 0)
3. JSON round-trip when value changes
4. No change returns original JSON reference

### Task 3: MissionPanel Integration (TASK-C010)

**File:** `Hrot/Engine/Hrot.Presentation/Panels/MissionPanel.cs` (MODIFY)
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-c010--missionpanel-integration)

Key changes:
1. Find `MissionPanel.cs` — confirm it exists in exactly one location (if in two, consolidate)
2. Remove `DrawFireAtTargetParams`, `DrawMoveToLocationParams`, `DrawFollowRouteParams` methods
3. Remove any `TryParseXxx` / `BuildXxx` static helpers that have ZERO external callers
   (grep for them first; if external callers exist, keep as pass-through shims)
4. Add `BehaviorUiRegistry` constructor injection
5. Implement `IPickInteractionContext` on `MissionPanel`:
   - Store `(taskIndex, propertyName)` for pending pick
   - `IsPickPendingFor` checks the stored value
   - `RequestEntityPick` / `RequestLocationPick` set the stored value and route to existing
     `HandlePickEntity` / `HandlePickLocation` callbacks
6. In the behavior task draw loop: look up `BehaviorUiRegistry` by `BehaviorId`; if found,
   call the delegate; if not found, call `DrawRawJsonEditor` as fallback

**Tests Required:**
1. Registry path renders without error (`FireAtTarget` registered; task with valid JSON; no exception)
2. Fallback `DrawRawJsonEditor` used for unknown behaviors
3. File search confirms `MissionPanel.cs` in exactly one project

### Task 4: Composition Root Registration (TASK-C011)

**File:** `Hrot/Subsystems/Hrot.CGF/Configuration/CgfBehaviorSetup.cs` (MODIFY)
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-c011--composition-root-registration)

Changes:
1. Construct `ScenarioBehaviorRemapper` and register `FireAtTargetParamsJsonDto` for `"FireAtTarget"`
   and `FollowRouteParamsJsonDto` for `"FollowRoute"`
2. Construct `BehaviorUiRegistry` and register delegates for `"FireAtTarget"`, `"FollowRoute"`,
   `"MoveToLocation"`
3. Expose both (e.g., as properties on `CgfBehaviorSetup`) so `CgfApplication` can retrieve them

**Tests Required:**
1. Integration test: minimal scenario JSON with `FireAtTarget` task → remapper updates `targetNetworkId`
2. `ScenarioBehaviorRemapper` has delegates for `"FireAtTarget"` and `"FollowRoute"` after setup
3. `BehaviorUiRegistry` has entries for `"FireAtTarget"`, `"FollowRoute"`, `"MoveToLocation"`

### Task 5: DEBT-D005 — Wire Remapper into CgfApplication

**File:** `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs` (MODIFY)

After running `CgfBehaviorSetup`, retrieve the `ScenarioBehaviorRemapper` and pass it
to `CgfScenarioLoadHandler` and `CgfEpisodeLoadHandler` constructors.  These constructors
already accept an optional `remapper` parameter — just pass the non-null instance.

---

## 🧪 Testing Requirements

- 3 tests for TASK-C008
- 4 tests for TASK-C009
- 3 tests for TASK-C010
- 3 tests for TASK-C011
- All tests assert values/behavior

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Task 1 (C008):** Add attributes + apply to DTOs → Write 3 tests → ALL pass ✅
2. **Task 2 (C009):** `IPickInteractionContext` + `BehaviorUiCompiler` + Registry → Write 4 tests → ALL pass ✅
3. **Task 3 (C010):** Modify `MissionPanel` → Write 3 tests → ALL pass ✅
4. **Task 4 (C011):** Modify `CgfBehaviorSetup` → Write 3 tests → ALL pass ✅
5. **Task 5 (D005):** Wire remapper in `CgfApplication` → `dotnet build` passes ✅
6. **Final:** `dotnet build IOS-IG-SimHost.sln` → 0 errors; all test projects green ✅

**Do NOT stop to ask for permission. Fix all failures before writing the report.**

---

## 📊 Report Requirements

Submit `.dev/cgf-scn/reports/BATCH-05-REPORT.md` with:

### 1. Completion Summary

### 2. Test Results (all test projects)

### 3. Developer Insights

**Q1:** What issues did you encounter? How did you resolve them?

**Q2:** Was the ImGui renderer testable without a real ImGui context? What approach did you take?

**Q3:** Were there any external callers of the removed `BuildXxx`/`TryParseXxx` helpers in `MissionPanel`?

**Q4:** What design decisions did you make beyond the spec?

**Q5:** Any performance concerns in the compiled UI delegates?

**Q6:** Suggested git commit message.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `MapPickableWorldLocationAttribute` and `MapPickableEntityAttribute` created and applied to DTOs
- [ ] `IPickInteractionContext` defined
- [ ] `BehaviorUiCompiler.Compile<TDto>()` implemented with expression trees; 4 tests pass
- [ ] `BehaviorUiRegistry` implemented
- [ ] `MissionPanel` integrated with registry lookup + `IPickInteractionContext`; 3 tests pass
  and `MissionPanel.cs` exists in exactly one project
- [ ] `CgfBehaviorSetup` registers remapper and UI registry; 3 tests pass
- [ ] `CgfApplication` passes `ScenarioBehaviorRemapper` to both load handlers (DEBT-D005)
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors
- [ ] All test projects green (or pre-existing failures only)
- [ ] TASK-TRACKER.md updated with all Phase 5 tasks done
- [ ] Report submitted

---

## ⚠️ Common Pitfalls to Avoid

- `BehaviorUiCompiler`: all reflection must happen inside `Compile<TDto>()`, not in the returned delegate
- `MissionPanel` must implement `IPickInteractionContext` — not a separate adapter class
- Do NOT break existing `DrawRawJsonEditor` fallback path
- `BehaviorId` strings in `CgfBehaviorSetup` must match what `BehaviorRegistry` uses exactly

---

## 📚 Reference Materials

- **Task Details:** `.dev/cgf-scn/TASK-DETAIL.md` — TASK-C008, C009, C010, C011
- **Design:** `.dev/cgf-scn/DESIGN.md` — Decision 7
- **Behavior attributes dir:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Attributes/`
- **BehaviorParamRemapperCompiler:** `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorParamRemapperCompiler.cs` (expression tree pattern)
- **MissionPanel:** `Hrot/Engine/Hrot.Presentation/Panels/MissionPanel.cs`
- **CgfBehaviorSetup:** `Hrot/Subsystems/Hrot.CGF/Configuration/CgfBehaviorSetup.cs`
- **CgfApplication:** `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs`
- **Previous reviews:** `.dev/cgf-scn/reviews/BATCH-01-REVIEW.md` through `BATCH-04-REVIEW.md`
