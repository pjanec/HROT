# CGF-1-BATCH-23: Subsystem DSM parity (brain + muscle + IG + IOS) + orchestrator globals

**Batch number:** CGF-1-BATCH-23  
**Tasks:** **Part A — Cross-subsystem wiring & global scenario slice (P1/P2 tech debt first)** → **Part B — CGF1-S0310** (E2E DSM test script suite) *and/or* **CGF1-S0106** (Orchestrator ImGui) per capacity  
**Phase:** Phase 1 + Phase 3 open tasks; **product closure** for CGF / SimHost / **IG** / **IOS** roles in the DSM  
**Estimated effort:** 16–40 h Part A + 24–48 h Part B (split if needed)  
**Priority:** HIGH (Part A — brain + muscle persist; **IG** participates in load/replay; **IOS** drives orchestrator only)  
**Dependencies:** [CGF-1-BATCH-22](../reviews/CGF-1-BATCH-22-REVIEW.md) — APPROVED with corrections; Phase 4 **complete**

---

## Role snapshot (normative for this batch)

| Subsystem | DSM / persistence role |
|-----------|-------------------------|
| **CGF (brain)** | Full scenario + story + **record / replay / checkpoint** parity with product intent (see A.1). |
| **SimHost (muscle)** | Already wired with toolkit reference handlers via **`NodeBootstrapper`** — extend only if BATCH-22 gaps remain. |
| **IG (Image Generator)** | **Listening / rendering** — must still **participate in DSM** so the orchestrator gets ACKs: **recording/replay** handlers (appropriate to a network-listener node), **zone loading** (at least a **dummy** handler for the ops the planner fans out), **scenario loading** (at least **dummy header-peek / prefetch** path). *Terrain DB preload from a scenario entity is **not** implemented yet — stub/document.* |
| **IOS** | **Instructs the Orchestrator** (load scenario, inject story, transitions). **No** obligation to **save** into the scenario package or **drill recording** — no **`SerializeLocal`** / checkpoint / brain-style persistence handlers unless a future requirement says otherwise. |
| **Orchestrator** | **Not** built on FDP ECS; scenario save/load for orchestrator-owned data already uses **ECS-independent DTOs** (**`GlobalContextDto`**, DDS topics, `Orchestrator.json`). Extend that pattern for **ScenarioTime** etc. (**§A.4**) — do **not** require a kernel **`EntityRepository`** on the orchestrator node. |

---

## Sequencing note

1. **Part A** closes [DEBT-TRACKER](../../DEBT-TRACKER.md) targets **CGF-1-BATCH-23** and produces a **wiring matrix** in the report (`NodeOpType` × node kind).  
2. **Part B** — **S0310** first, then **S0106** — after Part A matrix is accepted (or explicitly scoped down with lead sign-off).

---

## Onboarding

1. [.dev/cgf-1/reviews/CGF-1-BATCH-22-REVIEW.md](../reviews/CGF-1-BATCH-22-REVIEW.md)  
2. [.dev/cgf-1/CGF-1-GENERALIZATION.md](../CGF-1-GENERALIZATION.md)  
3. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §**CGF1-S0310**, §**CGF1-S0106**  
4. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md)  
5. [`NodeBootstrapper.cs`](../../../Hrot.SimHost/NodeBootstrapper.cs), [`CgfApplication.cs`](../../../Hrot.CGF/CgfApplication.cs), [`IgApplication.cs`](../../../Hrot.IG/IgApplication.cs) — handler diff

**Report:** `.dev/cgf-1/reports/CGF-1-BATCH-23-REPORT.md` (when complete)

---

## Part A — Tech / product debt (first)

### A.1 — CGF (brain) record / replay / checkpoint parity (P1)

Unchanged intent: **`CgfApplication`** gains **`ReferenceLiveLoadHandler`**, **`ReferenceReplayLoadHandler`**, **`ReferenceCheckpointHandler`** (or documented equivalent) plus a **brain-appropriate** **`IRecordReplayController`** / seam — not stub-only for production paths.

**Tests:** Integration proof of **PrepareLive / FinalizeLive** and/or **replay** ACK path on CGF.

### A.2 — IG: recording / replay + zone + scenario (dummy-acceptable) (P2)

**Requirement (lead):** IG **listens to network state and renders** but **must** handle:

1. **Recording / replay** DSM participation — wire **`ReferenceLiveLoadHandler`** / **`ReferenceReplayLoadHandler`** (and related **`IRecordReplayController`** or **IG-specific no-op/listen-only adapter**) so **PrepareLive / FinalizeLive / PrepareReplay / FinalizeReplay** do not **NAK** or stall the cluster. Behaviour may be **minimal** (e.g. participate with **`IsParticipating`** as appropriate, no local `.fdp` until product needs it).  
2. **Zone loading** — at least one **dummy** handler (or **`Reference\*`** wrapper) for the **zone / area load** **`NodeOpType`**(s) the **TransitionPlanner** / **ClusterMaster** may issue to IG; **ACK** or explicit non-participation per policy.  
3. **Scenario loading** — at least **dummy header-peek / prefetch** path (**`ReferencePrefetchHandler`** and/or **`ReferenceScenarioLoadHandler`** with **`world: null`** and terrain preload **TODO**), so sync with **PrepareLive** / **PrefetchScenario** does not leave orphan transactions. Document: *full terrain DB preload from scenario entities is **future work***.

**Tests:** One integration or unit test per category (replay participation, zone dummy, scenario dummy) as feasible.

### A.3 — IOS: orchestrator instruction only (P2 / P3)

**Requirement:** IOS **drives the Orchestrator** (sys-op / scenario / story commands). It **does not** need handlers for **saving** scenario fragments or **drill recording** / **`SerializeLocal`**.

**Work:** Document in **DESIGN** + **TASK-DETAIL**; implement or confirm **sys-op client** path only. **No** requirement to register **`ReferenceCheckpointHandler`**, **`ReferenceLiveLoadHandler`** (for persistence), etc. on IOS unless IOS becomes a roster “node” that receives **`NodeOpCommand`** — if it **does** receive fan-out, register **thin stub** handlers that **ACK non-participating** or **reject** explicitly so the cluster never stalls.

### A.4 — Orchestrator globals (ECS-independent) (P2)

Extend **`GlobalContextDto`** + **`GlobalContextDsmHandler`** (and wire topics if needed) for **ScenarioTime**, and other **orchestrator-owned** globals. **Constraint:** keep types **FDP-ECS-free** (plain DTOs / JSON), consistent with today’s **`Orchestrator.json`** approach.

### A.5 — Optional test hardening (P3)

- **`ClusterMasterStoryTests`:** Multi-node **ManageEpisode** mixed ACK (optional).

### A.6 — DEBT-TRACKER

Close **Part A** rows when merged (Status ✅).

---

## Part B — S0310 / S0106

Per **CGF-1-TASK-DETAIL.md**:

- **CGF1-S0310** — E2E DSM test script suite.  
- **CGF1-S0106** — Orchestrator ImGui scenario & story controls.

Update **CGF-1-TASK-TRACKER.md** when done.

---

## Success criteria

- [ ] Part A: **wiring matrix** in report; **CGF** brain persistence path implemented or signed defer; **IG** has replay + zone + scenario **dummy/real** handlers per above; **IOS** role documented + no spurious save/recording handlers; **`GlobalContext`** extended or de-scoped in writing; DEBT updated.  
- [ ] Part B: §S0310 / §S0106 met (or defer + tracker).  
- [ ] Build clean; tests green.  
- [ ] Report filed.

---

## Reference

- [CGF-1-BATCH-22 review](../reviews/CGF-1-BATCH-22-REVIEW.md)  
- [CGF-1-BATCH-23 review](../reviews/CGF-1-BATCH-23-REVIEW.md) (lead sign-off)  
- **Deferred S0310 + Runner `nodeId` work:** [CGF-1-BATCH-24](./CGF-1-BATCH-24-INSTRUCTIONS.md)  
- [`GlobalContextDsmHandler.cs`](../../../Hrot.Orchestrator/GlobalContextDsmHandler.cs)
