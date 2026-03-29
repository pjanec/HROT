# CGF-1-BATCH-09: S0205 closure + test infra + keyed NodeOpCommand

**Batch number:** CGF-1-BATCH-09  
**Tasks:** **Part A — Tech debt & S0205 normative closure** → **Part B — keyed `NodeOpCommand` (ADR implementation)**  
**Phase:** Phase 2 closure → Phase 3 entry  
**Estimated effort:** 24–32 hours (~8–12 h Part A + ~16–20 h Part B)  
**Priority:** HIGH  
**Dependencies:** [CGF-1-BATCH-08](../reviews/CGF-1-BATCH-08-REVIEW.md) — APPROVED (S0205 partial)  

---

## Onboarding

1. [.dev/.guides/DEV-GUIDE.md](../../.guides/DEV-GUIDE.md)  
2. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0205, §CGF1-S0105 ADR (keyed topics)  
3. [.dev/cgf-1/reviews/CGF-1-BATCH-08-REVIEW.md](../reviews/CGF-1-BATCH-08-REVIEW.md) — S0205 gaps  
4. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — rows **Target Fix = CGF-1-BATCH-09**  

**Report:** `.dev/cgf-1/reports/CGF-1-BATCH-09-REPORT.md`  

---

## Mandatory workflow

Tackle **Part A** items **before** large **DrillMaster** fan-out refactors so CI and coordinator behavior stay green. Prefer **isolated domain IDs** or **xUnit collections** when touching DDS tests (see BATCH-08 report on parallel contention).

---

## Part A — Tech debt & S0205 closure (first)

### A.1 — **Consume `DrillMaster.PendingTimeMode` and drive `DistributedTimeCoordinator`**

- Identify the host that owns both **`DrillMaster`** (or DDS visibility of orchestrator state) and **`ModuleHostKernel`** + time coordinator (**`OrchestratorSubsystem`** or future combined process).  
- When **`PendingTimeMode`** indicates deterministic **`LoadingLive`**, call **`SwitchToDeterministic`** (or equivalent) **before** the cluster is expected to enter **`RunningLive`**, per **CGF-1-TASK-DETAIL §S0205**.  
- Add **unit or integration** coverage that **`PendingTimeMode`** transitions trigger coordinator code (mock kernel acceptable).

### A.2 — **`SwitchTimeModeDescriptorTranslator` on CGF node**

- Wire **`TimeNetworkModule.CreateDescriptorTranslator`** wherever **`CgfSubsystem` / `Bagira.CGF`** builds **`CycloneNetworkModule`** (same pattern as SimHost/IG).  
- Document **NetworkDemo** exclusion (BATCH-08) in **CGF-1-DESIGN** or task detail if not already obvious.

### A.3 — **Stricter CI tests (task-detail alignment)**

- **`DeterministicRun_IsReproducible`:** capture **entity ids or component payload** at tick 600 in two runs; assert **bit-identical** or **structural equality** per **CGF-1-TASK-DETAIL** (not only exit codes).  
- Add **`MinimalCIScenarioTests`** (or integration test) that spawns **`dotnet run --project Bagira.Runner -- --mode ci --scenario minimalci_01`** (or the exact key) with **timeout**, asserting **exit code 0** — or document **CI pipeline** substitution if subprocess tests are forbidden in-repo.

### A.4 — **Hygiene & infra**

- **`TimeNetworkModule.RegisterTranslators`:** mark **`[Obsolete]`** with message pointing to **`CreateDescriptorTranslator`**, or remove if unused.  
- **Parallel test domains:** reconcile **`TestDomainAllocator`** with fixed orchestrator domain **15** (or document single-thread CI); reduce flakes in full **`IOS-IG-SimHost.sln`** test.

### A.5 — **DEBT-TRACKER**

Close rows satisfied by A.1–A.4; roll **SurvivingNodes** only if **Part B** slips.

---

## Part B — Keyed **`NodeOpCommand`** (CGF-1-TASK-DETAIL ADR)

Implement the **§CGF1-S0105** ADR: **`[DdsKey]`** (or approved wire shape), **`DrillMaster`** fan-out / writer cache, ejection disposal, and **updated** **`SurvivingNodes`** test with **two participants** asserting **isolation**.

---

## Success criteria

- [x] Part A: S0205 **normative** gaps from BATCH-08 review **closed** (coordinator consumption, CGF translator, stronger tests, obsolete API / domain flake as scoped).  
- [x] Part B: Keyed **`NodeOpCommand`** + tests; **SurvivingNodes** debt **✅** or explicitly re-rolled with justification.  
- [x] Solution build clean; tests green (document parallel policy if needed).  
- [x] DEBT-TRACKER updated.  
- [x] Report filed.  

**Post-review:** [CGF-1-BATCH-09 review](../reviews/CGF-1-BATCH-09-REVIEW.md) — IG **`SetFilter`**, CGF bus, docs → **CGF-1-BATCH-10**.

---

## Reference

- [CGF-1-BATCH-08 review — S0205 gaps](../reviews/CGF-1-BATCH-08-REVIEW.md#summary)  

**Next preview:** **CGF-1-BATCH-10** — **CGF1-S0301** (Storage Gateway) or Phase 3 planning after Phase 2 sign-off.
