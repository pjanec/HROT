# CGF-1-BATCH-01: Orchestration schema, ClusterMaster bootstrap, centralized ID allocator

**Batch number:** CGF-1-BATCH-01  
**Tasks:** CGF1-S0101, CGF1-S0102, CGF1-S0103  
**Phase:** Phase 1 — Skeleton (Stages 1.1–1.3)  
**Estimated effort:** 18–22 hours (greenfield control-plane; size assumes first-time DDS/orchestrator wiring in this repo)  
**Priority:** HIGH  
**Dependencies:** None  

---

## Onboarding and workflow

### Developer instructions

You are implementing the first vertical slice of the CGF-1 control plane: **wire-level DDS contracts**, a **minimal `ClusterMaster`** that owns `SystemStateTopic`, and **moving `DdsIdAllocatorServer` out of SimHost** into the orchestrator with a **transition fallback** for standalone SimHost. Complete all three tasks in order; do not stop mid-batch for approval to run tests or fix failures—run the full solution test suite and fix root causes before submitting the report.

### Required reading (in order)

1. **Workflow:** [.dev/.guides/DEV-GUIDE.md](../../.guides/DEV-GUIDE.md) — batch workflow, report expectations  
2. **Workstream onboarding:** [.dev/cgf-1/CGF-1-ONBOARDING.md](../CGF-1-ONBOARDING.md) — scope, folder map, FDP/Hrot boundary  
3. **Design (normative fields and QoS):** [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) — §3 Phase 1, especially §3.1–§3.3  
4. **Task specs and success conditions (do not re-derive from this batch):** [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) — CGF1-S0101, CGF1-S0102, CGF1-S0103  
5. **Tracker (context):** [.dev/cgf-1/CGF-1-TASK-TRACKER.md](../CGF-1-TASK-TRACKER.md)  

### Source code locations (repo root)

| Area | Path |
|------|------|
| DDS schemas (existing pattern) | `Hrot.NED/` (see e.g. `SimDescriptors.cs` for `[DdsTopic]`, `[DdsQos]`, `[DdsKey]` usage) |
| New orchestration messages | `Hrot.NED/Orchestration/OrchestrationMessages.cs` (per task detail) |
| ID allocator (server/client) | `FDP/ModuleHost/ModuleHost.Network.Cyclone/Services/DdsIdAllocatorServer.cs`, `DdsIdAllocator.cs` |
| SimHost app wiring (migration) | `Hrot.SimHost/SimHostApp.cs` |
| Runner / subsystem entry | `Hrot.ClusterRunner/` (`Hrot.ClusterRunner.csproj` is the executable host today) |
| Native DDS prerequisite | `README.md` (First-Time Setup) — `FDP/ExtDeps/FastCycloneDds/build/native-win.ps1` |

### Build and test commands (repo root)

```powershell
dotnet build IOS-IG-SimHost.sln
dotnet test IOS-IG-SimHost.sln
```

Use narrower projects while iterating if helpful (e.g. `Hrot.NED.Tests/Hrot.NED.Tests.csproj`), but **before the report** run **`dotnet test IOS-IG-SimHost.sln`** and ensure green.

### Report and questions

**Report (when done):** `.dev/cgf-1/reports/CGF-1-BATCH-01-REPORT.md`  

**Questions (only if blocked after reading linked docs):** `.dev/cgf-1/questions/CGF-1-BATCH-01-QUESTIONS.md`  

**Lead review (filled by dev lead after your report):** `.dev/cgf-1/reviews/CGF-1-BATCH-01-REVIEW.md`  

### Debt and follow-ups

P2/P3 items discovered during implementation or review are recorded in **[.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md)** (source batch: `CGF-1-BATCH-01`). P1 issues belong in corrective work at the start of the next batch, not in the debt table.

---

## Mandatory workflow: test-driven task progression

**Complete tasks in sequence with passing tests:**

1. **CGF1-S0101:** Implement → tests → **all relevant tests pass**  
2. **CGF1-S0102:** Implement → tests → **all relevant tests pass**  
3. **CGF1-S0103:** Implement → tests → **full solution tests pass**  

Do **not** start the next task until the current one meets its **success conditions** in [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md).

---

## Context

This batch establishes the **DDS vocabulary** and **orchestrator process** that later phases (ClusterSlave, 2PC, persistence) depend on. **Do not duplicate** field lists or QoS tables here—use the design doc and task detail as the single source of truth.

**Task definition links:**

- [CGF1-S0101 — Orchestration DDS schema](../CGF-1-TASK-DETAIL.md#cgf1-s0101--orchestration-dds-schema-definition)  
- [CGF1-S0102 — Hrot.Orchestrator bootstrapping](../CGF-1-TASK-DETAIL.md#cgf1-s0102--hrotorchestrator-bootstrapping)  
- [CGF1-S0103 — Centralized identity migration](../CGF-1-TASK-DETAIL.md#cgf1-s0103--centralized-identity-migration)  

**Design anchors:**

- [§3.1 Stage 1.1 — Orchestration DDS schema](../CGF-1-DESIGN.md#31-stage-11--orchestration-dds-schema)  
- [§3.2 Stage 1.2 — Hrot.Orchestrator bootstrapping](../CGF-1-DESIGN.md#32-stage-12--hrotorchestrator-bootstrapping)  
- [§3.3 Stage 1.3 — Centralized identity migration](../CGF-1-DESIGN.md#33-stage-13--centralized-identity-migration)  

---

## Batch objectives

- All orchestration topics and enums exist under `Hrot.NED.Descriptors.Orchestration` with correct attributes (per design §3.1).  
- `ClusterMaster` runs in a new `Hrot.Orchestrator` library, is hostable from `Hrot.ClusterRunner` (`--mode orchestrator` or equivalent config), and publishes initial `SystemStateTopic` as specified in task detail.  
- `DdsIdAllocatorServer` runs in the orchestrator; SimHost uses client-only path with documented fallback when no orchestrator is present (per task detail).  

---

## Tasks

### Task 1 — CGF1-S0101 (~4–5 h)

**Task definition:** [CGF-1-TASK-DETAIL.md § CGF1-S0101](../CGF-1-TASK-DETAIL.md#cgf1-s0101--orchestration-dds-schema-definition)  
**Design:** [CGF-1-DESIGN.md §3.1](../CGF-1-DESIGN.md#31-stage-11--orchestration-dds-schema)

**Scope:** Implement `OrchestrationMessages.cs` and **OrchestrationSchemaTests** in `Hrot.NED.Tests` exactly as specified in the task detail success conditions (reflection over `Hrot.NED.Descriptors.Orchestration`, enum values, `NodeHeartbeat` key, `SystemStateTopic` QoS).

**Non-goals:** No `ClusterMaster` or runtime DDS wiring in this task.

---

### Task 2 — CGF1-S0102 (~8–10 h)

**Task definition:** [CGF-1-TASK-DETAIL.md § CGF1-S0102](../CGF-1-TASK-DETAIL.md#cgf1-s0102--hrotorchestrator-bootstrapping)  
**Design:** [CGF-1-DESIGN.md §3.2](../CGF-1-DESIGN.md#32-stage-12--hrotorchestrator-bootstrapping)

**Scope:**

- New projects: `Hrot.Orchestrator` (library, `net8.0`) and `Hrot.Orchestrator.Standalone` (executable host), registered in `IOS-IG-SimHost.sln`. Project references must align with the task detail (DataModel, Fdp.Kernel, Cyclone/module host as needed—**no** `Hrot.*` inside `FDP/`).  
- `ClusterMaster`: subscribe `NodeHeartbeat`, publish `SystemStateTopic` on startup, `Tick()` for heartbeat-driven maintenance (roster pruning threshold per task detail / design).  
- Skeleton types: `DistributedTransaction`, `NodeRoster` as described in task detail (no full 2PC yet).  
- `Hrot.ClusterRunner`: activate orchestrator subsystem via **`--mode orchestrator`** and/or configuration—match the pattern used for other modes in that project.

**Tests:** Meet **all** success conditions in the task detail, including `ClusterMasterBootstrapTests.OrchestratorPublishesStandbyOnStartup` (wall-clock bound, DDS reader asserts `CurrentState` and `TransactionEpoch`). Prefer a dedicated `Hrot.Orchestrator.Tests` project if integration tests need a clean host boundary; add it to the solution if you create it.

**Note:** If the repo has no prior `*.Standalone` exe besides `Hrot.ClusterRunner`, implement `Hrot.Orchestrator.Standalone` as a thin `Program.cs` that boots the same orchestrator path the Runner uses, with clean shutdown on Ctrl+C (task detail).

---

### Task 3 — CGF1-S0103 (~5–7 h)

**Task definition:** [CGF-1-TASK-DETAIL.md § CGF1-S0103](../CGF-1-TASK-DETAIL.md#cgf1-s0103--centralized-identity-migration)  
**Design:** [CGF-1-DESIGN.md §3.3](../CGF-1-DESIGN.md#33-stage-13--centralized-identity-migration)

**Scope:**

- Remove `DdsIdAllocatorServer` startup from `SimHostApp` (and any type references—task detail requires **no** `DdsIdAllocatorServer` on SimHost).  
- Host `DdsIdAllocatorServer` inside `ClusterMaster` (orchestrator), with `ProcessRequests()` (or equivalent) called from the orchestrator loop so allocations work under load.  
- **Transition:** config flag for “orchestrator optional”: if no orchestrator / allocator server discovered within **5 s** (or the semantics specified in task detail), fall back to **local** server for legacy standalone SimHost workflows. Document the flag in `config.json` or the config type you extend.

**Tests:** `DdsIdAllocatorMigrationTests.SimHostReceivesIdFromOrchestratorServer` per task detail; `Hrot.SimHost.Integration.Tests` unchanged pass.

---

## Testing requirements

- **Quality bar:** Assertions must check **behavior and invariants** (enum values, attribute parameters, received DDS samples)—not merely string fragments of source or “non-null” objects.  
- **Regression:** Full solution test run green before report.  
- **Warnings:** New code: zero new compiler warnings where the repo already enforces that standard.

---

## Report requirements

Submit `.dev/cgf-1/reports/CGF-1-BATCH-01-REPORT.md`. Include:

- Commands run and test summary (assemblies / counts).  
- **Developer insights** (see DEV-LEAD-GUIDE): issues encountered and fixes, weak spots in existing code, extra design decisions, edge cases, performance notes, **suggested commit message** line.  

Do **not** treat the report as a comprehension quiz; focus on professional observations.

---

## Success criteria (batch done when)

- [ ] CGF1-S0101 success conditions satisfied (including `OrchestrationSchemaTests`).  
- [ ] CGF1-S0102 success conditions satisfied (integration test + Standalone clean exit).  
- [ ] CGF1-S0103 success conditions satisfied (migration test + SimHost integration suite).  
- [ ] `dotnet test IOS-IG-SimHost.sln` passes.  
- [ ] Report filed at `.dev/cgf-1/reports/CGF-1-BATCH-01-REPORT.md`.  

---

## Common pitfalls

- **Boundary:** No `Hrot.*` references under `FDP/`; DSM/orchestration wire types stay in `Hrot.NED`.  
- **DdsIdAllocator:** Preserve the existing “server discovered before first alloc” behavior; moving the server to another process changes timing—ensure SimHost still does not race the allocator.  
- **Standalone:** If Cyclone native libs are missing, local runs fail with `DllNotFoundException`—document in the report if CI differs from dev machine.  

---

## Reference materials

| Document | Use |
|----------|-----|
| [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) | Field names, topic names, QoS |
| [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) | Exact tests and deliverables |
| [.dev/.guides/DEV-LEAD-GUIDE.md](../../.guides/DEV-LEAD-GUIDE.md) | Review expectations, debt rules |
| [README.md](../../../README.md) | Cyclone native build, restore, build |

---

## Next batch (preview — do not start)

**CGF-1-BATCH-02** is expected to cover **CGF1-S0104** (ClusterSlave foundation) and **CGF1-S0105** (health / bootstrap recovery), pending lead review of BATCH-01.
