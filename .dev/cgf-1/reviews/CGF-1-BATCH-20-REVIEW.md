# CGF-1-BATCH-20 Review

**Batch:** CGF-1-BATCH-20  
**Reviewer:** Development Lead  
**Date:** 2026-03-30  
**Status:** **APPROVED** — **Part A** matches the report and **source** and closes the BATCH-19 §S0308 residual and the **`RecordReplayIntegrationTests`** regression. **Part B** (**CGF1-S0310**, **CGF1-S0106**) was **explicitly deferred** to **BATCH-21** with tracker alignment; that deferral is acceptable.

**Report:** [CGF-1-BATCH-20-REPORT.md](../reports/CGF-1-BATCH-20-REPORT.md) — verified against [CGF-1-BATCH-20-INSTRUCTIONS.md](../batches/CGF-1-BATCH-20-INSTRUCTIONS.md), [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0308.

---

## Part A — Tasks vs description

| Item | Verdict |
|------|---------|
| **A.1 — CGF `StoryLoadDsmHandler`** | **Met.** New handler ([`Bagira.CGF/.../StoryLoadDsmHandler.cs`](../../../Bagira.CGF/Modules/Orchestration/Handlers/StoryLoadDsmHandler.cs)): **`StartStory`** header-peek by **`ScenarioSerializer.IsMatchingSubsystem`**, **`StopStory`** always participating; **`Commit`** publishes **`NodeOpStatus`** with **`IsParticipating`**. Registered from **`CgfApplication`** when **`ScenarioSerializer`** exists. |
| **A.2 — `NodeOpStatus.IsParticipating` + DrillMaster gating** | **Partially met, as documented.** SimHost and CGF handlers **`PublishAck`** on the happy paths; **`DrillSlave.NodeOpStatusWriter`** on CGF is wired. **`DrillMaster.ManageStory`** still **fans out and updates `ActiveStories` without waiting for `NodeOpStatus`** ([`DrillMaster.cs`](../../../Bagira.Orchestrator/DrillMaster.cs) ~605–630, ~649–666). TASK-DETAIL records this as an **intentional MVP delta** — **good honesty**. Instructions also asked for **DESIGN** in the same PR; **CGF-1-DESIGN.md** still has **no** parallel callout → **documentation gap** (P3). |
| **A.3 — `RecordReplayIntegrationTests`** | **Met.** Brain test supplies **`FdpEventBus`**, asserts **`IsHandlerRegistered<LiveLoadDsmHandler>`** ([`RecordReplayIntegrationTests.cs`](../../../Bagira.SimHost.Integration.Tests/RecordReplayIntegrationTests.cs)). Method name still says **`RegistersEcsRecordReplayController`** — **misleading** (P3 hygiene). |
| **A.4 — DEBT-TRACKER** | **Met.** Rows for BATCH-19 follow-ups are closed ✅. |

---

## Design alignment

- **Story injection flow:** Node-side **`IsParticipating`** ACKs match the **design intent** of distinguishing participating vs non-participating nodes for **`StartStory`**.
- **Orchestrator:** Normative “wait only for participating ACKs” is **not** implemented; documented only in TASK-DETAIL. Until **`DrillMaster`** consumes ACKs, **`SysOpStatus.InProgress`** and **`ActiveStories`** can diverge from on-node reality — **known MVP risk**, should not stay undocumented in DESIGN.

---

## Tests — do they check what matters?

- **Integration:** Report claims **38/38** **`Bagira.SimHost.Integration.Tests`**; **not re-verified here** — local **`dotnet test`** failed on **MSB3027** (**`Fhsm.SourceGen.dll`** locked by another process), same class of infra noise as prior batches.
- **Story ACK semantics:** No new test asserts **DDS `NodeOpStatus`** for **`ManageStory`** or **malformed payloads**. Existing **`StoryInjectionTests`** focus on handler/repo behaviour — still valuable, but **orchestrator 2PC for story ops** is **untested**.

---

## Fail early / no silent swallowing

**Good**

- SimHost **`CommitStartStory`**: **`Deserialize`** logs **Error** and **rethrows** ([`StoryLoadDsmHandler.cs`](../../../Bagira.SimHost/Modules/Orchestration/Handlers/StoryLoadDsmHandler.cs) ~252–256).
- CGF **`Commit`**: always **`PublishAck`** when **`_pendingTransactionId`** matches (including non-participating **`StartStory`**).

**Gaps (track for BATCH-21)**

1. **SimHost `PrepareAsync`** resets **`_pendingTransactionId`** then returns **without setting it** when **`StartStory`** has invalid **`StoryId`/`ScenarioId`** (lines ~157–169) or **`StopStory`** has invalid **`StoryId`** (~269–275). **`Commit`** then **no-ops** (`_pendingTransactionId != cmd.TransactionId`) → **no `NodeOpStatus`** → orchestrator may **never** complete the transaction for that node **without a NAK**.
2. **`CommitStartStory` / `CommitStopStory`** when **`repo` and `_world` are null**: **Warn** and **return without `PublishAck`** (~232–241, ~293–301) — **silent on the wire** if that path is hit.
3. **`DrillMaster` `ManageStory`**: **`JsonException`** on payload parse for **`storyMode`/`storyId`** is **swallowed** (~595) — **`ActiveStories`** may skip update **without** surfacing rejection.

---

## Suggested commit message

```
feat(cgf1): S0308 residual — CGF StoryLoadDsmHandler, NodeOpStatus ACKs, RecordReplay test

- Add CGF StoryLoadDsmHandler (header-peek, IsParticipating ACK via DrillSlave writer)
- Wire SimHost StoryLoadDsmHandler PublishAck + NodeBootstrapper
- Fix Brain integration test to assert LiveLoadDsmHandler + event bus
- Document DrillMaster story ACK gating as MVP delta in TASK-DETAIL
```

---

## Follow-up

See **DEBT-TRACKER** (new P2 rows) and **[CGF-1-BATCH-21-INSTRUCTIONS.md](../batches/CGF-1-BATCH-21-INSTRUCTIONS.md)** — **tech debt first**, then **S0310** / **S0106**.
