# CGF-1-BATCH-10: Orchestration parity + CGF time bus + CGF1-S0301

**Batch number:** CGF-1-BATCH-10  
**Tasks:** **Part A — BATCH-09 review follow-ups (tech debt)** → **CGF1-S0301** (Storage Gateway)  
**Phase:** Phase 3 entry  
**Estimated effort:** 22–28 hours (~3–5 h Part A + ~18–23 h S0301)  
**Priority:** HIGH  
**Dependencies:** [CGF-1-BATCH-09](../reviews/CGF-1-BATCH-09-REVIEW.md) — APPROVED  

---

## Onboarding

1. [.dev/.guides/DEV-GUIDE.md](../../.guides/DEV-GUIDE.md)  
2. [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §5.1  
3. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0301  
4. [.dev/cgf-1/reviews/CGF-1-BATCH-09-REVIEW.md](../reviews/CGF-1-BATCH-09-REVIEW.md) — Issues 1–4  
5. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — **Target Fix = CGF-1-BATCH-10**  

**Report:** `.dev/cgf-1/reports/CGF-1-BATCH-10-REPORT.md`  

---

## Mandatory workflow

Complete **Part A** (small, correctness-first) **before** large SMB / **`StorageGatewayModule`** work so **IG** keyed **`NodeOpCommand`** behaviour matches other nodes.

---

## Part A — Tech debt first

### A.1 — **`Hrot.IG` `ClusterSlave` `SetFilter`** (P2 — BATCH-09 Issue 1)

**File:** `Hrot.IG/Modules/Orchestration/ClusterSlave.cs`  

- After creating **`DdsReader<NodeOpCommand>`**, add **`SetFilter(cmd => cmd.TargetNodeId == _nodeId)`** to match **SimHost / IOS / CGF**.  
- Add or extend a **unit/integration** test if an IG+DDS harness exists; otherwise document **manual** verification in the report.

### A.2 — **`CgfApplication` time bus coherence** (P2 — BATCH-09 Issue 2)

Pick one and document in XML + report:

- **Option A:** Pass the same **`FdpEventBus`** into **`ClusterSlave`** (if/when ctor supports it) and register a minimal **`SlaveTimeModeListener`** + kernel when scope allows; **or**  
- **Option B:** Keep minimal shell but **document** explicitly that **`SwitchTimeModeDescriptorTranslator`** only moves bytes on/off DDS until Phase 3+ kernel lands.

### A.3 — **Docs hygiene** (P3 — BATCH-09 Issue 4)

- Update **`TimeNetworkModule`** class-level XML to describe **`CreateDescriptorTranslator`** / **`SwitchTimeModeWireDto`** as the supported path (not blit-on-`SwitchTimeModeEvent`).

### A.4 — **Optional subprocess CI** (P3)

If CI agents allow: one integration test spawning **`dotnet run --project Hrot.ClusterRunner -- --mode ci --scenario minimalci_01`** with timeout — **or** confirm pipeline runs that command and **close** the opportunistic DEBT row.

### A.5 — **DEBT-TRACKER**

Close **A.1–A.3** rows when done.

---

## Part B — CGF1-S0301: Storage Gateway

**Task definition:** [CGF-1-TASK-DETAIL.md §CGF1-S0301](../CGF-1-TASK-DETAIL.md#cgf1-s0301--storage-gateway)  
**Design:** [CGF-1-DESIGN.md §5.1](../CGF-1-DESIGN.md#51-stage-31--storage-gateway)

Implement **`StorageGatewayModule`** (or equivalent), **`FileManifestEntry`**, **`PullToNasAsync` / `PushToNodesAsync`**, **`ClusterMaster`** hook after **`SerializeLocal`** ACKs, and **all** **`StorageGatewayTests`** success conditions.

---

## Success criteria

- [x] Part A: IG **`SetFilter`**; CGF bus doc or wiring; **`TimeNetworkModule`** XML; DEBT updated. — [review §Part A](../reviews/CGF-1-BATCH-10-REVIEW.md#summary)  
- [x] Part B: CGF1-S0301 success conditions met. — [review §Part B](../reviews/CGF-1-BATCH-10-REVIEW.md#summary)  
- [x] Solution build clean; tests green.  
- [x] DEBT-TRACKER updated.  
- [x] Report filed.  

---

## Reference

- [CGF-1-BATCH-09 review Issues](../reviews/CGF-1-BATCH-09-REVIEW.md#gaps-schedule-batch-10)  
- **Review:** [CGF-1-BATCH-10-REVIEW.md](../reviews/CGF-1-BATCH-10-REVIEW.md) — APPROVED  

**Next:** [CGF-1-BATCH-11](CGF-1-BATCH-11-INSTRUCTIONS.md) — debt + **CGF1-S0306**; **CGF1-S0307** → **BATCH-12**; **S0302** after S0306+S0307.
