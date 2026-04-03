# BATCH-01: NavigationStatus CQRS & Module Realignment

**Batch Number:** BATCH-01
**Tasks:** PACK-N001, PACK-N002, PACK-N003, PACK-N004, PACK-M001, PACK-M002
**Phase:** Phase 1 (NavigationStatus CQRS) + Phase 2 (Module Realignment)
**Estimated Effort:** 15–18 hours
**Priority:** HIGH
**Dependencies:** None — this is the first batch.

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch implements the first two phases of the Logic Packs / Translator Packs refactoring.

**Phase 1** fixes the `RouteContextSystem` which is currently broken on Brain-only distributed
nodes because it queries `NavState` (a Muscle-tier component). The fix is to pipe the routing
progress information through the existing `NavigationStatus` CQRS feedback channel.

**Phase 2** corrects two module-registration misplacements: `HsmDamageBridgeSystem` lives in
the wrong module for distributed execution, and two cross-domain systems
(`ApcMobilityTriggerSystem` / `ApcMobilitySystem`) must be deleted and their logic absorbed
into `HealthApplicationSystem`.

### Required Reading (IN ORDER)

1. **Developer Workflow Guide:** `.github/skills/developer/SKILL.md`
2. **Architecture & Design:** `.dev/packs-1/DESIGN.md` — read §Phase 1 and §Phase 2 carefully
3. **Task Specifications:** `.dev/packs-1/TASK-DETAIL.md` — sections PACK-N001 through PACK-M002
4. **No previous review** — this is the first batch.

### Source Code Location

- **Navigation contracts:** `FDP/Toolkits/FDP.Toolkit.Navigation.Contracts/NavigationComponents.cs`
- **NED wire structs:** `Hrot.NED/SimDescriptors.cs`
- **NavigationExecutionSystem:** `FDP/Toolkits/FDP.Toolkit.Navigation/Systems/NavigationExecutionSystem.cs`
- **RouteContextSystem:** `Hrot.SimHost/Systems/Routing/RouteContextSystem.cs`
- **Translators (search):** grep for `NavigationStatusEgressTranslator` and `NavigationStatusIngressTranslator` in `Hrot.SimHost/` and `Hrot.Common/`
- **CombatModule:** `Hrot.SimHost/Modules/CombatModule.cs`
- **CognitiveRuntimeModule:** `FDP/Toolkits/FDP.Toolkit.Behavior/Modules/CognitiveRuntimeModule.cs`
- **HealthApplicationSystem:** `FDP/Toolkits/FDP.Toolkit.Combat/Systems/HealthApplicationSystem.cs`
- **UrbanCombatNewScenario:** `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs`
- **ApcMobilitySystem:** `FDP/Examples/Fdp.Examples.UrbanCombat/Systems/ApcMobilitySystem.cs`
- **HeadlessDemoApp:** `FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs`

### Test Projects

- `Hrot.SimHost.Tests/` — unit tests for SimHost systems
- `Hrot.SimHost.Integration.Tests/` — integration tests
- `Hrot.ClusterRunner.Integration.Tests/` — cluster runner integration tests
- FDP test projects inside `FDP/` — unit tests for FDP toolkits

### Report Submission

**When done, submit your report to:**
`.dev/packs-1/reports/BATCH-01-REPORT.md`

**If you have questions, create:**
`.dev/packs-1/questions/BATCH-01-QUESTIONS.md`

---

## 🔄 Mandatory Workflow: Test-Driven Task Progression

**You MUST follow this exact sequence for each task:**

```
1. READ the task detail in TASK-DETAIL.md (understand WHY, not just WHAT)
2. READ the relevant source files before touching anything
3. WRITE the test(s) first — watch them FAIL
4. IMPLEMENT the minimum code to make tests PASS
5. VERIFY: dotnet test [relevant project] — ALL tests must pass
6. Only then move to the next task
```

**Never skip tests. Never fake assertions. Never catch exceptions in tests just to make
them pass. Tests must check real logic/values/behavior.**

---

## 📌 Tasks

All task specifications live in `.dev/packs-1/TASK-DETAIL.md`. Read the full spec for
each task before starting it. Below is the execution order with dependency notes.

### Order of Execution

```
PACK-N001 → PACK-N002 → PACK-N003 → PACK-N004   (N004 depends on N001+N002)
PACK-M001                                          (independent)
PACK-M002                                          (independent)
```

### PACK-N001 — Extend NavigationStatus with ProgressS

See: `TASK-DETAIL.md#pack-n001`

**Summary:** Add `float ProgressS` to the ECS `NavigationStatus` struct and to the NED
wire struct. No logic changes. Do NOT change any existing field or ordering.

**Key files:**
- `FDP/Toolkits/FDP.Toolkit.Navigation.Contracts/NavigationComponents.cs` — append field
- `Hrot.NED/SimDescriptors.cs` — append field

**Tests to write:** 2 unit tests (field round-trip on ECS struct, reflection check on wire struct).

---

### PACK-N002 — Populate ProgressS in NavigationExecutionSystem

See: `TASK-DETAIL.md#pack-n002`

**Summary:** In `NavigationExecutionSystem`, map `NavState.ProgressS` → `NavigationStatus.ProgressS`.
Add within the existing write-out block — additive only.

**Tests to write:** 3 unit tests (mapping, zero passthrough, preserves existing fields).

---

### PACK-N003 — Update NavigationStatus Network Translators for ProgressS

See: `TASK-DETAIL.md#pack-n003`

**Summary:** Map the new field in egress and ingress translators for `NavigationStatus`.
Find the files by searching for `NavigationStatusEgressTranslator` and
`NavigationStatusIngressTranslator`.

**Tests to write:** 2 unit tests (egress, ingress) + optional roundtrip.

---

### PACK-N004 — Refactor RouteContextSystem (Brain-only query)

See: `TASK-DETAIL.md#pack-n004`

**Dependencies:** PACK-N001 and PACK-N002 must be done first.

**Summary:** Remove `NavState` from the query; add `NavigationIntent` and `NavigationStatus`.
Replace all `nav.*` reads with reads from the new components. The system becomes Brain-tier only.

**Tests to write:** 3 unit tests (positive path, no-NavState-required, inactive route).
**Code review gate:** `RouteContextSystem.cs` must have zero references to `NavState`,
`VehicleState`, `SimTransform`, or any type from physics/CarKinem namespaces.

---

### PACK-M001 — Relocate HsmDamageBridgeSystem to CognitiveRuntimeModule

See: `TASK-DETAIL.md#pack-m001`

**Summary:** Remove `HsmDamageBridgeSystem` from `CombatModule.RegisterSystems()`. Add it to
`CognitiveRuntimeModule.RegisterSystems()` **before** `BTreeTickSystem` and `HsmTickSystem<T>`.

**Important registration order in CognitiveRuntimeModule:**
```
ChannelArbitrationSystem
HsmDamageBridgeSystem   ← NEW, insert here
BTreeTickSystem
HsmTickSystem<BrainHsm128>
HsmTickSystem<BrainHsm64>
```

**Tests to write:** Integration test (Brain-only damage → HSM transition) + regression (AllInOne).

---

### PACK-M002 — Delete ApcMobilityTriggerSystem; Absorb Logic into HealthApplicationSystem

See: `TASK-DETAIL.md#pack-m002`

**Summary:**
1. Update `HealthApplicationSystem` to strip `ActorCapabilities.CanMove` when
   `Health.Current < Health.Max` after applying a `DamageAssessedEvent` (non-lethal hit).
   Only if entity has `ActorCapabilityState`; skip silently if not.
2. Delete `ApcMobilityTriggerSystem` (inner class in `UrbanCombatNewScenario.cs`).
3. Delete `ApcMobilitySystem.cs` (`FDP/Examples/Fdp.Examples.UrbanCombat/Systems/`).
4. Remove `ApcMobilitySystem` registration from `HeadlessDemoApp.cs`.

**Tests to write:**
- Unit: non-lethal hit strips CanMove (only CanMove, not CanInteract).
- Unit: lethal hit does not throw (regression guard).
- Verify the existing UrbanCombat integration test `LatchApcHalted` still passes.

**After:** Workspace grep for `ApcMobilityTriggerSystem` and `ApcMobilitySystem` must return zero.

---

## ✅ Batch Success Criteria

1. All tasks implemented per TASK-DETAIL.md specifications.
2. All tests written with real behavioral assertions — no trivial "compiles" checks.
3. `dotnet build` succeeds for the full solution.
4. `dotnet test` succeeds for:
   - All FDP toolkit test projects touching Navigation, Behavior, Combat
   - `Hrot.SimHost.Tests/`
   - `Hrot.SimHost.Integration.Tests/`
   - `Hrot.ClusterRunner.Integration.Tests/` (smoke — ensure no regressions)
5. `RouteContextSystem.cs` has zero references to Muscle-tier types.
6. `ApcMobilityTriggerSystem` and `ApcMobilitySystem` are fully deleted.

---

## 💡 Developer Insights Section

In your report, please explicitly answer:

1. **What issues were encountered?** (compile errors, unexpected dependencies, etc.)
2. **What weak points were spotted in the codebase?** (fragile patterns, missing abstractions,
   excessive coupling you noticed beyond the task scope)
3. **What design decisions were made beyond the spec?** (choices you made to resolve ambiguities)
4. **Did any test reveal something unexpected about the current behavior?**

---

## 📄 Report Format

Submit to `.dev/packs-1/reports/BATCH-01-REPORT.md` using this structure:

```markdown
# BATCH-01 Report

## Status
[COMPLETE / PARTIAL — list any incomplete tasks]

## Tasks Completed
- PACK-N001: [brief summary]
- PACK-N002: [brief summary]
- PACK-N003: [brief summary]
- PACK-N004: [brief summary]
- PACK-M001: [brief summary]
- PACK-M002: [brief summary]

## Test Results
[Paste dotnet test summary output]

## Developer Insights
### Issues Encountered
[...]
### Weak Points Spotted
[...]
### Design Decisions Beyond Spec
[...]
### Unexpected Findings from Tests
[...]

## Files Changed
[List of all modified/created/deleted files]
```
