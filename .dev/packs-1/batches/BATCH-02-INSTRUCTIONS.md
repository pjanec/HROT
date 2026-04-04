# BATCH-02: Enforce the Intent Bus + Extract Spawning Systems

**Batch Number:** BATCH-02
**Tasks:** PACK-I001, PACK-I002, PACK-I003, PACK-P002, PACK-P004
**Phase:** Phase 3 (Enforce Intent Bus) + Phase 4 partial (PACK-P002, PACK-P004)
**Estimated Effort:** 13–16 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 complete ✅

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch works on two fronts:

**Phase 3 — Enforce the Intent Bus:** Three legacy "Cmd*" movement command paths bypass the
CQRS Intent Bus and directly mutate `NavState` (Muscle tier) from Brain-tier code. This batch
deletes all three backdoors:
1. `PersonalRouteAuthoringSystem` emits `CmdFollowTrajectory` → replace with `NavigationIntent` write.
2. `SimHostVisualization` right-click Brain-dead path directly mutates `NavState` → replace with `NavigationIntent` write.
3. `VehicleCommandSystem` processes all legacy `Cmd*` movement events → delete those handlers
   and the Cmd struct definitions (i.e. `CmdNavigateToPoint`, `CmdFollowTrajectory`,
   `CmdNavigateViaRoad`, `CmdStop`, `CmdSetSpeed`).

**IMPORTANT:** PACK-I003 must be done **last** — after I001 and I002 remove all callers of the
Cmd events. A compile error should not exist after I003 if done in order.

**Phase 4 partial — Extract Spawning Systems out of SimHostModule:** `SimHostModule` directly
holds DDS adapter inner classes and registers network-coupled `Create/DeleteEntityRequestSystem`.
These must be extracted to a network-boundary module so `SimHostModule` can be instantiated
without a `DdsParticipant`. Additionally, `UpdateEntityDescriptorRequestSystem` must move to
`Hrot.Map.Common/Replication/Ingress/` namespace and removed from its unconditional
`SimHostApp.cs` registration.

### Required Reading (IN ORDER)

1. **Developer Workflow Guide:** `.github/skills/developer/SKILL.md`
2. **Architecture & Design:** `.dev/packs-1/DESIGN.md` — read §Phase 3 (§3.A, §3.B, §3.C) and §Phase 4 §4.B carefully
3. **Task Specifications:** `.dev/packs-1/TASK-DETAIL.md` — sections PACK-I001, PACK-I002, PACK-I003, PACK-P002, PACK-P004
4. **Previous Review:** `.dev/packs-1/reviews/BATCH-01-REVIEW.md`

### Source Code Locations

**Phase 3:**
- `Hrot.SimHost/Systems/Routing/PersonalRouteAuthoringSystem.cs`
- `Hrot.SimHost/` — search for `SimHostVisualization` (grep for the class name)
- `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/VehicleCommandSystem.cs`
- Cmd event definitions — search for `CmdNavigateToPoint`, `CmdFollowTrajectory` definitions (likely `CommandEvents.cs` or similar)

**Phase 4:**
- `Hrot.SimHost/Modules/SimHostModule.cs`
- `Hrot.SimHost/` — grep for `DdsCreateEntityRequestSource`, `DdsCreateUpdateDeleteEntityAckSink`
- `Hrot.SimHost/SimHostApp.cs` — search for `UpdateEntityDescriptorRequestSystem`
- `Hrot.Map.Common/Systems/UpdateEntityDescriptorRequestSystem.cs` → move to `Hrot.Map.Common/Replication/Ingress/`

### Test Projects

- `Hrot.SimHost.Tests/` — unit tests for SimHost systems
- `Hrot.SimHost.Integration.Tests/` — integration tests
- `FDP/Toolkits/FDP.Toolkit.CarKinem.Tests/` — unit tests for VehicleCommandSystem and NavigationIntentBridgeSystem

### Report Submission

**When done, submit your report to:**
`.dev/packs-1/reports/BATCH-02-REPORT.md`

**If you have questions, create:**
`.dev/packs-1/questions/BATCH-02-QUESTIONS.md`

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
each task before starting it.

### Order of Execution

```
PACK-I001 → PACK-I002 → PACK-I003   (strict order: I003 depends on I001+I002 removing callers)
PACK-P002 → PACK-P004               (P004 depends on P002's network-boundary module existing first)
```

---

### PACK-I001 — Refactor PersonalRouteAuthoringSystem to Use NavigationIntent

See: `TASK-DETAIL.md#pack-i001`

**Summary:** Replace the `CmdFollowTrajectory` bus publish in `PersonalRouteAuthoringSystem`
with writing `NavigationIntent { Mode=FollowRoute, TrajectoryId=..., IntentId++ }` as an ECS
component. Preserve the deferred-frame mechanism (`_pendingFollowCommands`).

**Key constraints:**
- `IntentId` must be **incremented** (not just set) — otherwise `NavigationIntentBridgeSystem`
  will ignore it as a duplicate.
- Use `NavigationMode.FollowRoute` (not a raw integer).
- After the change: zero references to `CmdFollowTrajectory` in this file.

**Tests to write:**
1. Intent written with correct Mode=FollowRoute, TrajectoryId, IntentId+1.
2. No `CmdFollowTrajectory` events on the bus after trigger.
3. (Optional) Integration test if an existing route-authoring integration test exists.

---

### PACK-I002 — Refactor SimHostVisualization Right-Click to Use NavigationIntent

See: `TASK-DETAIL.md#pack-i002`

**Summary:** In `SimHostVisualization.HandleRightClickForEntity`, replace the "Brain-dead path"
(any direct `NavState` mutation or `CmdFollowTrajectory`/`CmdNavigateToPoint` publish) with:
```csharp
var intent = repo.GetComponent<NavigationIntent>(entity);
intent.IntentId++;
intent.Mode = NavigationMode.DirectPoint;
intent.FinalDestination = pos;
intent.TargetSpeed = 15f;
intent.ArrivalRadius = 3.0f;
repo.SetComponent(entity, intent);
return;
```

Add a TODO comment that TargetSpeed/ArrivalRadius should eventually be configurable.

**Key constraints:**
- Must NOT mutate `NavState` directly.
- `IntentId` must be incremented.

**Tests to write:**
1. Intent written with correct Mode=DirectPoint, FinalDestination, TargetSpeed=15f, ArrivalRadius=3.0f, IntentId++.
2. NavState NOT mutated.
3. After change: `SimHostVisualization` has zero references to `CmdFollowTrajectory` or `CmdNavigateToPoint`.

---

### PACK-I003 — Remove Legacy Commands from VehicleCommandSystem

See: `TASK-DETAIL.md#pack-i003`

**Dependencies:** PACK-I001 and PACK-I002 must be complete (so no callers remain).

**Summary:**
- Delete processing of `CmdNavigateToPoint`, `CmdFollowTrajectory`, `CmdNavigateViaRoad`,
  `CmdStop`, `CmdSetSpeed` from `VehicleCommandSystem`.
- Delete the 5 corresponding `Cmd*` struct definitions from wherever they are defined
  (`CommandEvents.cs` or similar).
- **Keep:** `CmdSpawnVehicle`, `CmdCreateFormation`, `CmdJoinFormation`, `CmdLeaveFormation`.

**Gate:** Solution must compile after deletions. If any reference remains, update it as part of
this task.

**Tests to write:**
1. Compile gate — solution builds after deletion.
2. Zero remaining workspace references to the 5 deleted Cmd types.
3. Unit test: `NavigationIntentBridgeSystem` still correctly translates `NavigationIntent` with
   Mode=DirectPoint → `NavState` destination (confirms the intent→physics pipeline works after
   the command backdoor removal).

---

### PACK-P002 — Extract Spawning Request Systems out of SimHostModule

See: `TASK-DETAIL.md#pack-p002`

**Summary:**
1. Move `DdsCreateEntityRequestSource` and `DdsCreateUpdateDeleteEntityAckSink` inner classes
   from `SimHostModule.cs` to a new file `Hrot.SimHost/Network/SimHostNetworkAdapters.cs`.
2. Remove `_requestSystem` (CreateEntityRequestSystem) and `_deleteSystem`
   (DeleteEntityRequestSystem) fields and their registration from `SimHostModule.RegisterSystems()`.
3. Register both systems in the network-boundary module (wherever other DDS translators are
   registered in `SimHostApp.cs` or equivalent network startup code).
4. `SimHostModule` constructor must no longer require `DdsParticipant` as a mandatory parameter.

**Key constraint:** After this change, `SimHostModule.cs` must have **zero** references to
`DdsParticipant`, `DdsReader`, or `DdsWriter`.

**Tests to write:**
1. Offline instantiation: Construct `SimHostModule` without `DdsParticipant` — no exception.
2. No `DdsParticipant` in SimHostModule (code review: grep result = 0).
3. (Integration) Both Create/Delete systems appear in the registered system list when DDS is available.

---

### PACK-P004 — Relocate UpdateEntityDescriptorRequestSystem

See: `TASK-DETAIL.md#pack-p004`

**Dependencies:** PACK-P002 (the network-boundary module registration site must exist).

**Summary:**
- Move `UpdateEntityDescriptorRequestSystem.cs` from `Hrot.Map.Common/Systems/` to
  `Hrot.Map.Common/Replication/Ingress/`.
- Update namespace from `Hrot.Map.Common.Systems` to `Hrot.Map.Common.Replication.Ingress`.
- Remove the unconditional `_kernelGroup.AddSystem(new UpdateEntityDescriptorRequestSystem(...))`
  from `SimHostApp.cs`.
- Register it **conditionally** in the same network-boundary module as the other spawning systems.

**No logic changes** — file path and namespace only.

**Tests to write:**
1. Compile gate — solution builds.
2. Namespace check — grep `Hrot.Map.Common.Systems.UpdateEntityDescriptorRequestSystem` → zero results.
3. Offline: bootstrapping without DDS does NOT register the system.
4. Online: bootstrapping with DDS DOES register the system.

---

## ✅ Batch Success Criteria

1. All 5 tasks implemented per TASK-DETAIL.md specifications.
2. All tests with real behavioral assertions pass.
3. `dotnet build` succeeds for the full solution.
4. `dotnet test` succeeds for:
   - `FDP/Toolkits/FDP.Toolkit.CarKinem.Tests/`
   - `Hrot.SimHost.Tests/`
   - `Hrot.SimHost.Integration.Tests/`
   - `Hrot.ClusterRunner.Integration.Tests/` (smoke)
5. `VehicleCommandSystem.cs` has no references to the 5 deleted Cmd types.
6. `SimHostModule.cs` has zero references to `DdsParticipant`/`DdsReader`/`DdsWriter`.
7. `UpdateEntityDescriptorRequestSystem` lives in `Replication/Ingress/` namespace.

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

Submit to `.dev/packs-1/reports/BATCH-02-REPORT.md` using this structure:

```markdown
# BATCH-02 Report

## Status
[COMPLETE / PARTIAL — list any incomplete tasks]

## Tasks Completed
- PACK-I001: [brief summary]
- PACK-I002: [brief summary]
- PACK-I003: [brief summary]
- PACK-P002: [brief summary]
- PACK-P004: [brief summary]

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
