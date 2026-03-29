# CGF-1-BATCH-10 Review

**Batch:** CGF-1-BATCH-10  
**Reviewer:** Development Lead  
**Date:** 2026-03-28  
**Status:** **APPROVED** (minor follow-ups scheduled for **CGF-1-BATCH-11**)

**Report:** [CGF-1-BATCH-10-REPORT.md](../reports/CGF-1-BATCH-10-REPORT.md) — verified against **source** and [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0301.

---

## Summary

**Part A** matches the report and instructions:

- **`Bagira.IG.Modules.Orchestration.DrillSlave`**: `SetFilter(cmd => cmd.TargetNodeId == _nodeId)` is applied immediately after `DdsReader<NodeOpCommand>` construction — parity with SimHost / IOS / CGF ([`DrillSlave.cs`](../../../Bagira.IG/Modules/Orchestration/DrillSlave.cs) lines 48–50).
- **`CgfApplication`**: **Option B** documented in class-level XML — wire path via `CreateDescriptorTranslator` + `Tick()`; `DrillSlave` not on `_eventBus`; no `SlaveTimeModeListener` ([`CgfApplication.cs`](../../../Bagira.CGF/CgfApplication.cs) lines 12–28).
- **`TimeNetworkModule`**: class summary names **`CreateDescriptorTranslator` / `SwitchTimeModeWireDto`** as supported path and **`RegisterTranslators`** as deprecated ([`TimeNetworkModule.cs`](../../../FDP/Toolkits/FDP.Toolkit.Time/TimeNetworkModule.cs) lines 10–30).
- **A.4 subprocess CI**: unchanged; remains **Opportunistic** per instructions — acceptable.

**Part B (CGF1-S0301)** is **substantively complete** relative to the **written success conditions**:

- **`StorageGatewayModule`**: `PullToNasAsync`, `PushToNodesAsync`, `MaxParallelCopies = 8`, `Parallel.ForEach` with `ParallelOptions`, per-file try/catch and aggregated `GatewayResult` ([`StorageGatewayModule.cs`](../../../Bagira.Orchestrator/StorageGatewayModule.cs)).
- **`FileManifestEntry`**, **`NodeDistributionTarget`**, **`GatewayResult`**: present with XML aligned to design (UNC / relative dest).
- **`DrillMaster`**: `DdsReader<NodeOpStatus>`, `SetStorageGateway`, `FanOutSerializeLocal`, `ConsumeNodeOpStatuses` on each `Tick()`, JSON deserialize of `ResultJson` → `List<FileManifestEntry>`, fire-and-forget `PullToNasAsync` when ACKs complete ([`DrillMaster.cs`](../../../Bagira.Orchestrator/DrillMaster.cs)).
- **`StorageGatewayTests`**: both named success-condition tests exist and pass.

**Tests run (review):** `dotnet test Bagira.Orchestrator.Tests` — **20** passed (18 + 2 new).

---

## Alignment with design (§5.1)

Design §5.1 describes the **SMB Pull Gateway Pattern**, parallel pull with **`MaxDegreeOfParallelism = 8`**, and milestone validation via **local mock** manifests. The implementation uses **`File.Copy`** from manifest `SourceUnc` to paths under `nasBasePath`, which is appropriate for **UNC or local** roots (real SMB is path-dependent, not a separate API layer here). **Aligned** with the design’s intent and task-detail wording (“opens one outbound SMB connection” is satisfied in practice by sequential SMB sessions per `File.Copy` from the orchestrator host).

---

## Gaps and risks (non-blocking)

1. **`PushToNodesAsync`**: Implemented per task-detail item 1 but **not covered** by unit tests (success conditions only specified **Pull** tests). **Risk:** Low for current milestone; track as **P3** and add parity tests in BATCH-11.
2. **`FanOutSerializeLocal`**: **No in-repo call sites** yet (only definition). **`ConsumeNodeOpStatuses`** correctly completes the **post-ACK → Pull** half of the integration. Full **SaveScenario → SerializeLocal** wiring is explicitly deferred to Phase 3 / **S0307** in code comments and report — **acceptable** for S0301 as scoped.
3. **Parallelism assertion**: `PullToNas_CopiesAllFiles` checks **`MaxParallelCopies ≤ 8`** via the public constant rather than introspecting **`ParallelOptions`** or a mock. Task detail allows “mock or … options inspection”; this is **weaker but acceptable** proof that the cap is ≤ 8.
4. **IG `SetFilter`**: No automated test (report matches code); same posture as instructions (“if harness exists … otherwise manual”).
5. **Hygiene**: `DrillMaster` XML on `SerializeLocalTask` references **`_remainingAcks`** in a `<see cref>` — that symbol does not exist (field is **`RemainingAcks`** on the nested type). Cosmetic; fix in BATCH-11 or opportunistically.

---

## Verdict on tests

The two **Pull** tests exercise **real filesystem I/O**, **partial failure**, and **directory creation** — they validate the behaviour operators care about for the gateway. They do **not** validate **`PushToNodesAsync`**, **`DrillMaster` + DDS `NodeOpStatus`**, or **`FanOutSerializeLocal`** end-to-end; that gap is understood and partly deferred to **S0307** / later integration tests.

---

## Suggested commit message

```
feat(cgf-1): BATCH-10 storage gateway, IG DrillSlave filter, time docs

- IG DrillSlave: SetFilter on NodeOpCommand by TargetNodeId (parity)
- CgfApplication + TimeNetworkModule: document SwitchTimeMode wire vs listener gap
- Orchestrator: StorageGatewayModule (pull/push), DrillMaster SerializeLocal ACK → NAS pull
- Tests: StorageGatewayTests (pull success + partial failure)

Refs: CGF-1-BATCH-10, CGF1-S0301
```

---

## Next batch

See **[CGF-1-BATCH-11](../batches/CGF-1-BATCH-11-INSTRUCTIONS.md)**: tech-debt first, then **CGF1-S0306**. **CGF1-S0307** is scheduled for **CGF-1-BATCH-12**; **CGF1-S0302** follows after both land.
