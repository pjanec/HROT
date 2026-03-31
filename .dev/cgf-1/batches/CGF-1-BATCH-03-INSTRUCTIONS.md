# CGF-1-BATCH-03: P1 fail-fast & Runner-only + CGF1-S0105

**Batch number:** CGF-1-BATCH-03  
**Tasks:** **Part A — mandatory product/policy corrective (P1/P2)** → **CGF1-S0105**  
**Phase:** Phase 1 — Skeleton (Stage 1.5)  
**Estimated effort:** 22–28 hours (~6–10 h Part A + ~16–18 h S0105; adjust if ImGui scope is split)  
**Priority:** HIGH  
**Dependencies:** [CGF-1-BATCH-02](../reviews/CGF-1-BATCH-02-REVIEW.md) — APPROVED; **read Issues section before coding**  

---

## Onboarding and workflow

### Developer instructions

**Part A is non-negotiable and comes first:** remove silent DDS degradation, remove legacy allocator fallback, remove standalone exe projects, align tests. **Then** implement **CGF1-S0105** per task detail and design §3.5. Do not ship S0105 while SimHost can still start “without DDS” or with a local ID server fallback.

### Required reading (in order)

1. [.dev/.guides/DEV-GUIDE.md](../../.guides/DEV-GUIDE.md)  
2. [.dev/cgf-1/CGF-1-ONBOARDING.md](../CGF-1-ONBOARDING.md) — update in Part A for Runner-only launch  
3. [.dev/cgf-1/reviews/CGF-1-BATCH-02-REVIEW.md](../reviews/CGF-1-BATCH-02-REVIEW.md) — **Issues 1–4** drive Part A  
4. [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §3.5  
5. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0105  
6. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — rows targeting **CGF-1-BATCH-03**  

### Report / review

- Report: `.dev/cgf-1/reports/CGF-1-BATCH-03-REPORT.md`  
- Review: `.dev/cgf-1/reviews/CGF-1-BATCH-03-REVIEW.md`  

### Build / test

```powershell
dotnet build IOS-IG-SimHost.sln
dotnet test IOS-IG-SimHost.sln
```

---

## Mandatory workflow

Complete **Part A** in order; all tests green; then **Part B** (S0105) with tests after each major milestone. Full solution test run before report.

---

## Part A — Product policy & technical debt (do first)

### A.1 — Remove standalone projects (DEBT: Product / P2)

- Remove **`Hrot.Orchestrator.Standalone`** and **`Hrot.CGF.Standalone`** from **`IOS-IG-SimHost.sln`** and delete the project directories (or leave a one-line README pointing to Runner if deletion is blocked — **prefer deletion**).  
- No other Hrot subsystem ships a separate `.Standalone` exe; **orchestrator and CGF run only through `Hrot.ClusterRunner`** (`--mode orchestrator`, `--mode cgf`, combined flags as today).  
- Keep **`CgfApplication`** for integration tests and **`CgfSubsystem`** for Runner.  
- Update [.dev/cgf-1/CGF-1-ONBOARDING.md](../CGF-1-ONBOARDING.md) and any README references that mention `dotnet run --project …Standalone`.

### A.2 — DDS fail-fast: IG (DEBT: P1 Safety)

**File:** `Hrot.IG/IgApplication.cs` — `InitializeNetwork`.

- When **`enableNetwork == true`**, **do not** use a broad `catch` that sets `_networkEnabled = false` and “runs offline.”  
- On DDS / translator / participant failure: **propagate** the exception (after disposing partially constructed DDS objects if needed) so the process fails visibly, **or** call a single documented fatal path that terminates the app with a clear message.  
- **ClusterSlave** must be created only on the success path; there is no supported IG mode with “network enabled but DDS dead.”

**Audit:** `Hrot.SimHost`, `Hrot.ExCon`, `Hrot.ClusterRunner` subsystems — **no silent swallow** of DDS initialization when DDS is required for that mode. If a code path is intentionally headless-without-DDS, it must be **explicit** (`enableNetwork: false` or dedicated test entry) and documented, not a catch-all fallback.

### A.3 — Remove centralized ID allocator fallback (DEBT: P1 Architecture)

- Delete **`LocalIdAllocatorFallbackHost`** and all call sites.  
- Remove **`NodeConfiguration.IdAllocatorLocalFallbackEnabled`** and **`IdAllocatorLocalFallbackDelaySeconds`** (and JSON/config samples).  
- **`EnsureIdAllocatorRouting`** (or its replacement): after a **short, bounded** wait for `DdsIdAllocator` publication match, if still unmatched → **`throw`** (type and message documented) — SimHost **must not** continue as if healthy.  
- Update **`DdsIdAllocatorMigrationTests`**, **`ClusterSlaveHeartbeatTests`**, **`EntityLifecycleIntegrationTests`**, and any test that relied on fallback — each must start **`ClusterMaster`** (or host `DdsIdAllocatorServer` in-test **only** if you introduce a dedicated test double that is **not** a production fallback path).

### A.4 — SimHost `ClusterSlave` “DDS-less” constructor (DEBT: policy)

- Remove **`public ClusterSlave()`** **or** make it **`internal`** with `InternalsVisibleTo` limited to specific test assemblies — production **`BuildOrchestration`** must **`throw`** if `participant` is null when the role requires orchestration.  
- Fix **`RecordReplayIntegrationTests`** (and any other) to use **`ClusterSlave(DdsParticipant, …)`** with a test participant or shared test fixture.

### A.5 — `OrchestrationSchemaTests` filter (DEBT: P3)

- Replace **`Type.Name.Contains('_')`** with a **narrow** codegen rule (suffixes only: `_Native`, `View`, `KeyHolder`, etc.) so future hand-written types are not excluded.

### A.6 — DEBT-TRACKER hygiene

Mark rows **✅** when fixed:

- P1 Safety — IG DDS catch / offline  
- P1 Architecture — allocator fallback removal  
- P2 Product — Standalone removal  
- P3 Testing — `OrchestrationSchemaTests` filter (if done in this batch)

---

## Part B — CGF1-S0105: Orchestrator health, bootstrap recovery, ImGui

**Task definition:** [CGF-1-TASK-DETAIL.md §CGF1-S0105](../CGF-1-TASK-DETAIL.md#cgf1-s0105--orchestrator-health-monitoring--bootstrap-recovery)  
**Design:** [CGF-1-DESIGN.md §3.5](../CGF-1-DESIGN.md#35-stage-15--orchestrator-health-monitoring--bootstrap-recovery)

**Normative correction:** DDS node identity is **`int`** (`NodeHeartbeat.NodeId`). Implement **`EjectNode(int nodeId)`** (not `Guid`). Update **CGF-1-TASK-DETAIL.md** §step 4 in the same PR to match wire types.

**Scope summary:**

1. **`ClusterConfiguration`** + `orchestrator-config.json` load.  
2. **`ClusterMaster`:** `_bootstrapLatch`, mandatory roster gating, **`ClusterOpRequest` / `ClusterOpStatus`** handling (read/write as per design), reject until mandatory nodes in Standby.  
3. Heartbeat timeout → **`EjectNode(int nodeId)`**, degraded state, **`NodeOpCommand`** broadcasts per task detail.  
4. **`DistributedTransaction` history** ring buffer.  
5. **ImGui** orchestrator panel: remove **`WaitingRoomCoordinator`** gate, banner while waiting for mandatory nodes, disable controls until latched, health table + 2PC history table.

**Tests (extend `Hrot.Orchestrator.Tests`):** all four scenarios in task detail — assert **observable DDS outcomes** and roster/command delivery, not log substrings.

**Doc debt (same PR if small):**

- Align CGF1-S0103 wording (heartbeat vs allocator match) in task detail / design §3.3.  
- **`NodeOpType` `NodeReplaySeek`** vs design `ReplaySeek` — footnote in design.

---

## Testing requirements

- Part A: regression-free SimHost/IG/IOS/Runner integration; **no** test depends on local allocator fallback.  
- Part B: new `ClusterMasterBootstrapTests` cases per task detail; domain isolation pattern consistent with BATCH-02.  
- **Test quality:** assertions on state machines, topic samples, and command receipt — not shallow presence checks.

---

## Report requirements

Document Part A checklist (with files removed/changed), S0105 milestones, full `dotnet test` outcome, closed DEBT rows, suggested commit message, and any remaining parallel-DDS CI notes.

---

## Success criteria

- [ ] Standalone orchestrator/CGF projects removed; Runner-only documented.  
- [ ] No silent IG “offline” on DDS failure when network enabled.  
- [ ] No `LocalIdAllocatorFallbackHost`; SimHost fails if orchestrator allocator unavailable.  
- [ ] `ClusterSlave()` / null participant policy resolved per A.4.  
- [ ] CGF1-S0105 success conditions + design §3.5 met.  
- [ ] DEBT-TRACKER updated (✅ for resolved CGF-1-BATCH-03 targets).  
- [ ] Report filed.  

---

## Reference

- [CGF-1-BATCH-02 review Issues](../reviews/CGF-1-BATCH-02-REVIEW.md#issue-1-ig-swallows-dds-failures-p1--fail-fast-violation)  
- [DEV-LEAD-GUIDE.md](../../.guides/DEV-LEAD-GUIDE.md) — test quality bar  

**Next (preview):** CGF-1-BATCH-04 — Phase 2 start (**CGF1-S0201** …) after S0105 complete and CI green.
