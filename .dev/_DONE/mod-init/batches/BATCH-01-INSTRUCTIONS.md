# BATCH-01: Stage 1 — Push Down Architecturally Coupled Systems

**Batch Number:** BATCH-01
**Tasks:** MODINIT-S100, MODINIT-S107, MODINIT-S101, MODINIT-S102, MODINIT-S103, MODINIT-S104, MODINIT-S106
**Phase:** Stage 1 — Push Down Architecturally Coupled Systems
**Estimated Effort:** 8–12 hours
**Priority:** HIGH
**Dependencies:** None — this is the first batch

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch implements all of Stage 1 of the `mod-init` workstream. The goal is to relocate several components *down* the dependency graph so that `NedReplicationModule` can later be moved out of `Hrot.ClusterRunner` and into a new shared `Hrot.Network` assembly.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.github/skills/developer/SKILL.md` — How to work with batches
2. **Design Document:** `.dev/mod-init/DESIGN.md` — Full architecture, layer rules, and rationale. Read sections: "Architectural Constraint: Clean Architecture", stage headings for 1.1, 1.2, 1.3, 1.4, and "Key Decisions"
3. **Task Definitions:** `.dev/mod-init/TASK-DETAIL.md` — See MODINIT-S100, S107, S101, S102, S103, S104, S106

### Source Code Locations

- **New assembly target:** `Hrot.Network/` (does not exist yet — you create it in MODINIT-S100)
- **Files to move from:** `Hrot.IG/Systems/DeadReckoningSyncSystem.cs`, `Hrot.SimHost/Network/*.cs`
- **Destination (most):** `Hrot.Common/Systems/`, `Hrot.Map.Common/Translators/`, `Hrot.Map.Common/Replication/Ingress/`, `Hrot.Map.Common/Replication/Egress/`, `Hrot.Network/Translators/`, `Hrot.Network/Replication/`, `Hrot.Network/Infrastructure/`
- **Callers to update:** Any `.cs` file that `using Hrot.IG.Systems;` or `using Hrot.SimHost.Network;`
- **Solution file:** `IOS-IG-SimHost.sln` (root of repo)
- **Primary test projects:** `Hrot.ClusterRunner.Integration.Tests/`, `Hrot.IG.Tests/`, `Hrot.SimHost.Tests/`, `Hrot.Map.Common.Tests/`

### Report Submission

**When done, submit your report to:**
`.dev/mod-init/reports/BATCH-01-REPORT.md`

**If you have questions, create:**
`.dev/mod-init/questions/BATCH-01-QUESTIONS.md`

---

## Context

This workstream (`mod-init`) resolves a circular dependency that is explicitly marked in the codebase as `// TODO (P2 debt)` in `Hrot.SimHost/SimHostApp.cs`. The full context is in `.dev/mod-init/DESIGN.md`.

Stage 1 is purely a *push-down* operation: no behavioral changes to any class, only relocation + namespace updates. After this stage, every component that `NedReplicationModule` depends on will live in a shared layer, enabling Stage 2 to safely move the module itself.

**Dependency execution order for this batch:**
1. MODINIT-S100 (create `Hrot.Network`) — no prerequisites
2. MODINIT-S107 (move navigation translators) — no prerequisites; **must complete before S103 and S104**
3. MODINIT-S101 (move `DeadReckoningSyncSystem`) — no prerequisites; can be done in parallel with S107
4. MODINIT-S102 (move `SharedTranslatorPack`) — no prerequisites; can be done in parallel
5. MODINIT-S103 (move `KinematicTranslatorPack`) — **requires S107 done**
6. MODINIT-S104 (move `CognitiveTranslatorPack`) — **requires S100 and S107 done**
7. MODINIT-S106 (validate boundaries) — run after all above are green

---

## 🎯 Batch Objectives

- Create the `Hrot.Network` assembly with correct dependency wiring and add it to the solution.
- Move four navigation translators from `Hrot.SimHost/Network/` into `Hrot.Map.Common/Replication/Ingress/` and `Egress/`.
- Move `DeadReckoningSyncSystem` from `Hrot.IG/Systems/` to `Hrot.Common/Systems/`.
- Move `SharedTranslatorPack` and `KinematicTranslatorPack` from `Hrot.SimHost/Network/` to `Hrot.Map.Common/Translators/`.
- Move `CognitiveTranslatorPack` from `Hrot.SimHost/Network/` to `Hrot.Network/Translators/`.
- Update all callers to use the new namespaces.
- Verify that `Hrot.Common` and `Hrot.Map.Common` have no new upward references to `Hrot.SimHost` or `Hrot.IG`.
- All existing tests remain green.

---

## ✅ Tasks

### Task 1: MODINIT-S100 — Create Hrot.Network Assembly

**Full task definition:** `.dev/mod-init/TASK-DETAIL.md#modinit-s100--create-hrotnetwork-assembly`

**What to create:**

1. Create `Hrot.Network/Hrot.Network.csproj` targeting `net8.0`, same `ImplicitUsings`/`Nullable` settings as `Hrot.Common/Hrot.Common.csproj` (see that file for reference — `<TargetFramework>net8.0</TargetFramework>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<Nullable>enable</Nullable>`).

2. The `.csproj` must include `<ProjectReference>` to:
   - `..\Hrot.Common\Hrot.Common.csproj`
   - `..\Hrot.Map.Common\Hrot.Map.Common.csproj`
   - `..\FDP\Toolkits\FDP.Toolkit.Behavior\FDP.Toolkit.Behavior.csproj`

3. Create stub directories (empty, no code files): `Hrot.Network/Replication/`, `Hrot.Network/Translators/`, `Hrot.Network/Infrastructure/`

4. Add `<ProjectReference>` to `Hrot.Network/Hrot.Network.csproj` in:
   - `Hrot.SimHost/Hrot.SimHost.csproj`
   - `Hrot.IG/Hrot.IG.csproj`
   - `Hrot.CGF/Hrot.CGF.csproj`
   - `Hrot.ClusterRunner/Hrot.ClusterRunner.csproj`

5. Add the project to the solution: `dotnet sln IOS-IG-SimHost.sln add Hrot.Network/Hrot.Network.csproj`

**Constraints (from TASK-DETAIL):**
- `Hrot.Network.csproj` must NOT reference `Hrot.SimHost` or `Hrot.IG`
- `Hrot.Common.csproj` and `Hrot.Map.Common.csproj` must NOT gain a `<ProjectReference>` to `Hrot.Network`

**Verify:**
```powershell
dotnet build IOS-IG-SimHost.sln
dotnet sln IOS-IG-SimHost.sln list | Select-String "Hrot.Network"
Select-String "<ProjectReference.*Hrot.Network" Hrot.Common/Hrot.Common.csproj, Hrot.Map.Common/Hrot.Map.Common.csproj
```
Expected: build succeeds, solution lists the project, last command returns zero matches.

---

### Task 2: MODINIT-S107 — Move Navigation Translators to Hrot.Map.Common

**Full task definition:** `.dev/mod-init/TASK-DETAIL.md#modinit-s107--move-navigation-translators-to-hrotmapcommon`

Move these four files:

| Source | Target | Target namespace |
|---|---|---|
| `Hrot.SimHost/Network/NavigationIntentIngressTranslator.cs` | `Hrot.Map.Common/Replication/Ingress/NavigationIntentIngressTranslator.cs` | `Hrot.Map.Common.Replication.Ingress` |
| `Hrot.SimHost/Network/NavigationIntentEgressTranslator.cs` | `Hrot.Map.Common/Replication/Egress/NavigationIntentEgressTranslator.cs` | `Hrot.Map.Common.Replication.Egress` |
| `Hrot.SimHost/Network/NavigationStatusIngressTranslator.cs` | `Hrot.Map.Common/Replication/Ingress/NavigationStatusIngressTranslator.cs` | `Hrot.Map.Common.Replication.Ingress` |
| `Hrot.SimHost/Network/NavigationStatusEgressTranslator.cs` | `Hrot.Map.Common/Replication/Egress/NavigationStatusEgressTranslator.cs` | `Hrot.Map.Common.Replication.Egress` |

For each file:
- Change `namespace Hrot.SimHost.Network` → new target namespace
- Update any `using` directives if needed (remove self-reference)
- Find all callers via `grep -r "NavigationIntentIngress\|NavigationIntentEgress\|NavigationStatusIngress\|NavigationStatusEgress" --include="*.cs"` and update their `using` directives from `Hrot.SimHost.Network` → the new namespace

`Hrot.Map.Common.csproj` must NOT gain a reference to `Hrot.SimHost` — these translators' only dependencies are already in `Hrot.Map.Common` (NED types and FDP toolkits it already references).

**Verify:**
```powershell
dotnet build IOS-IG-SimHost.sln
# no files remain in source:
ls Hrot.SimHost/Network/Navigation*.cs  # should be empty
# no old namespace references:
Select-String "Hrot.SimHost.Network" Hrot.Map.Common/Replication/Ingress/Navigation*.cs, Hrot.Map.Common/Replication/Egress/Navigation*.cs
```

---

### Task 3: MODINIT-S101 — Move DeadReckoningSyncSystem to Hrot.Common

**Full task definition:** `.dev/mod-init/TASK-DETAIL.md#modinit-s101--move-deadreckoningsyncsystem-to-hrotcommon`

- Source: `Hrot.IG/Systems/DeadReckoningSyncSystem.cs`
- Target: `Hrot.Common/Systems/DeadReckoningSyncSystem.cs` (create the `Systems/` subdirectory)
- Change namespace: `Hrot.IG.Systems` → `Hrot.Common.Systems`
- Find all callers: `grep -r "DeadReckoningSyncSystem\|Hrot.IG.Systems" --include="*.cs"` — update their `using` directives
- If the only reason some project referenced `Hrot.IG` was for this class, check if that reference can now be removed (but do NOT remove references that other types in the same file still require)

**Preserve exactly:** see `.dev/mod-init/TASK-DETAIL.md#modinit-s101--move-deadreckoningsyncsystem-to-hrotcommon` → "Constraints" — the `driveFromNetwork` flag behavior, `[UpdateInPhase(SystemPhase.PostSimulation)]` attribute.

**Tests required:**
See the task's "Success Conditions 3 and 4" for unit test scenarios to write. Place new tests in `Hrot.Common.Tests/` or the most appropriate existing test project.

---

### Task 4: MODINIT-S102 — Move SharedTranslatorPack to Hrot.Map.Common

**Full task definition:** `.dev/mod-init/TASK-DETAIL.md#modinit-s102--move-sharedtranslatorpack-to-hrotmapcommon`

- Source: `Hrot.SimHost/Network/SharedTranslatorPack.cs`
- Target: `Hrot.Map.Common/Translators/SharedTranslatorPack.cs`
- Namespace: `Hrot.SimHost.Network` → `Hrot.Map.Common.Translators`
- Update all callers. Find them: `grep -r "SharedTranslatorPack" --include="*.cs"`
- `Hrot.Map.Common.csproj` must NOT gain a reference to `Hrot.SimHost`

**Tests required:**
See "Success Condition 3" in the task definition for the integration test verifying that `SharedTranslatorPack.Create(...)` yields `EntityMasterEgressTranslator`, `EntityMasterIngressTranslator`, and `EntityInfoEgressTranslator`.

---

### Task 5: MODINIT-S103 — Move KinematicTranslatorPack to Hrot.Map.Common

**Prerequisite:** MODINIT-S107 must be complete (navigation translators must be in `Hrot.Map.Common`).

**Full task definition:** `.dev/mod-init/TASK-DETAIL.md#modinit-s103--move-kinematictranslatorpack-to-hrotmapcommon`

- Source: `Hrot.SimHost/Network/KinematicTranslatorPack.cs`
- Target: `Hrot.Map.Common/Translators/KinematicTranslatorPack.cs`
- Namespace: `Hrot.SimHost.Network` → `Hrot.Map.Common.Translators`
- Update all callers. Find them: `grep -r "KinematicTranslatorPack" --include="*.cs"`

**Tests required:**
See "Success Condition 2" — verify `KinematicTranslatorPack.Create(...)` returns `GeoSpatialEgressTranslator` and `NavigationStatusEgressTranslator`.

---

### Task 6: MODINIT-S104 — Move CognitiveTranslatorPack to Hrot.Network

**Prerequisites:** MODINIT-S100 and MODINIT-S107 must be complete.

**Full task definition:** `.dev/mod-init/TASK-DETAIL.md#modinit-s104--move-cognitivetranslatorpack-to-hrotnetwork`

- Source: `Hrot.SimHost/Network/CognitiveTranslatorPack.cs`
- Target: `Hrot.Network/Translators/CognitiveTranslatorPack.cs`
- Namespace: `Hrot.SimHost.Network` → `Hrot.Network.Translators`
- Update all callers. Find them: `grep -r "CognitiveTranslatorPack" --include="*.cs"`
- `Hrot.Network.csproj` must NOT gain a reference to `Hrot.SimHost` or `Hrot.IG`
- `BehaviorRegistry?` is used as a concrete type — no interface abstraction

**Tests required:**
See "Success Condition 2" — verify `CognitiveTranslatorPack.Create(...)` returns `NavigationIntentEgressTranslator`, `EntityMissionEgressTranslator`, `GeoSpatialIngressTranslator`, `NavigationStatusIngressTranslator`.

---

### Task 7: MODINIT-S106 — Validate Stage 1 Layer Boundaries

**Full task definition:** `.dev/mod-init/TASK-DETAIL.md#modinit-s106--validate-stage-1-layer-boundaries`

Run these validation queries and confirm zero results for each:

```powershell
# No upward references from shared layers to application layer
Select-String "<ProjectReference.*Hrot\.(SimHost|IG)" Hrot.Common/Hrot.Common.csproj
Select-String "<ProjectReference.*Hrot\.(SimHost|IG)" Hrot.Map.Common/Hrot.Map.Common.csproj
# Hrot.Network does not reference application layer
Select-String "<ProjectReference.*Hrot\.(SimHost|IG)" Hrot.Network/Hrot.Network.csproj
# No code files still using old namespace for moved types
Select-String "Hrot.SimHost.Network" -Recurse -Include "*.cs" | Where-Object { $_.Line -match "SharedTranslatorPack|KinematicTranslatorPack|CognitiveTranslatorPack|Navigation" }
```

Also run isolated builds:
```powershell
dotnet build Hrot.Common/Hrot.Common.csproj --no-restore
dotnet build Hrot.Map.Common/Hrot.Map.Common.csproj --no-restore
dotnet build Hrot.Network/Hrot.Network.csproj --no-restore
```

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests at each step:**

1. **Task 1 (S100):** Create project → `dotnet build IOS-IG-SimHost.sln` passes ✅
2. **Task 2 (S107):** Move 4 translators → build passes, existing tests green ✅
3. **Task 3 (S101):** Move DeadReckoningSyncSystem + write unit tests → all tests pass ✅
4. **Task 4 (S102):** Move SharedTranslatorPack + write integration test → all tests pass ✅
5. **Task 5 (S103):** Move KinematicTranslatorPack + write test → all tests pass ✅ (S107 must be done)
6. **Task 6 (S104):** Move CognitiveTranslatorPack + write test → all tests pass ✅ (S100+S107 must be done)
7. **Task 7 (S106):** Run all validation queries → zero violations → write findings in report ✅

**Do not skip test runs. Do not move to the next task until the current one passes.** If a test fails, fix the root cause before continuing. Do not stop and ask for permission — work autonomously until all tasks are done.

---

## 🧪 Testing Requirements

- All **existing** tests in `Hrot.ClusterRunner.Integration.Tests/`, `Hrot.IG.Tests/`, `Hrot.SimHost.Tests/`, `Hrot.Map.Common.Tests/` must remain green throughout.
- **New tests** are required for:
  - `DeadReckoningSyncSystem` constructor behavior (two scenarios from MODINIT-S101 success conditions 3+4)
  - `SharedTranslatorPack.Create(...)` yields expected translator types (MODINIT-S102 SC3)
  - `KinematicTranslatorPack.Create(...)` yields expected translator types (MODINIT-S103 SC2)
  - `CognitiveTranslatorPack.Create(...)` yields expected translator types (MODINIT-S104 SC2)
- Tests must assert on **values and types**, not just compilation.

**Test runner command:**
```powershell
# From repo root:
dotnet test IOS-IG-SimHost.sln
```
Or per project:
```powershell
dotnet test Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj
dotnet test Hrot.IG.Tests/Hrot.IG.Tests.csproj
dotnet test Hrot.Map.Common.Tests/Hrot.Map.Common.Tests.csproj
```

---

## 📊 Report Requirements

Submit your report to `.dev/mod-init/reports/BATCH-01-REPORT.md`.

**Required sections:**

### 1. Status Summary
For each task (S100, S107, S101, S102, S103, S104, S106): ✅ Done / ⚠️ Partial / ❌ Failed with notes.

### 2. Validation Outputs
Paste the actual terminal output of:
- `dotnet build IOS-IG-SimHost.sln` (last build)
- `dotnet test IOS-IG-SimHost.sln` (or per-project test run)
- The `Select-String` boundary validation queries from Task 7

### 3. Developer Insights

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase that could be improved? What would you change?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** Were there any files that referenced the moved types but weren't covered by a simple namespace update? How did you handle them?

**Q5:** Do you see any risks or complications for Stage 2 (moving `NedReplicationModule` itself) based on what you observed?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `Hrot.Network/Hrot.Network.csproj` exists, builds, is in the solution (MODINIT-S100)
- [ ] All 4 navigation translators in `Hrot.Map.Common/Replication/{Ingress,Egress}/` with updated namespaces (MODINIT-S107)
- [ ] `DeadReckoningSyncSystem` in `Hrot.Common/Systems/` with updated namespace; new unit tests green (MODINIT-S101)
- [ ] `SharedTranslatorPack` in `Hrot.Map.Common/Translators/` with updated namespace; integration test green (MODINIT-S102)
- [ ] `KinematicTranslatorPack` in `Hrot.Map.Common/Translators/` with updated namespace; test green (MODINIT-S103)
- [ ] `CognitiveTranslatorPack` in `Hrot.Network/Translators/` with updated namespace; test green (MODINIT-S104)
- [ ] All validation queries from S106 return zero violations (MODINIT-S106)
- [ ] `dotnet build IOS-IG-SimHost.sln` succeeds
- [ ] All existing tests pass
- [ ] Report submitted to `.dev/mod-init/reports/BATCH-01-REPORT.md`

---

## ⚠️ Common Pitfalls to Avoid

1. **Do not add `Hrot.Common` → `Hrot.Network` or `Hrot.Map.Common` → `Hrot.Network` references.** The reference direction is strictly downward: application layer → `Hrot.Network` → `Hrot.Common` / `Hrot.Map.Common`.
2. **Do not change any logic** in the moved files — namespace update only. Behavioral changes are out of scope.
3. **When searching for callers,** remember that `using Hrot.SimHost.Network;` may be a single `using` directive that covers multiple moved types. Make sure you update it to the correct new namespace for each type actually used in that file.
4. **Navigation translator files (S107):** These translators depend only on NED and FDP types that `Hrot.Map.Common` already references — no new `<ProjectReference>` to `Hrot.Map.Common.csproj` should be needed.
5. **After `CognitiveTranslatorPack` is moved (S104)**, check that `Hrot.SimHost/Network/` no longer references any of the 7 moved types — some files in that directory (like `BrainPathfindingTranslatorPack.cs`) may still live there and use the moved types via their new namespaces.
6. **Do not delete files that are `NOT` in scope** (e.g., `BrainPathfindingTranslatorPack.cs`, `PerceptionTranslators.cs`, `SimHostNetworkAdapters.cs` — these stay in `Hrot.SimHost/Network/`).

---

## 📚 Reference Materials

- **Design:** `.dev/mod-init/DESIGN.md` — Architecture, layer rules, rationale
- **Task Definitions:** `.dev/mod-init/TASK-DETAIL.md` — Full details for all 7 tasks
- **Hrot.Common project file:** `Hrot.Common/Hrot.Common.csproj` — Use as template for `Hrot.Network.csproj`
- **Existing translator in target directory:** `Hrot.Map.Common/Translators/EntityStatesIngressPack.cs` — Use as style reference
- **Existing translators in target Ingress/Egress:** `Hrot.Map.Common/Replication/Egress/GeoSpatialEgressTranslator.cs`, `Hrot.Map.Common/Replication/Ingress/GeoSpatialIngressTranslator.cs` — Use as namespace reference
