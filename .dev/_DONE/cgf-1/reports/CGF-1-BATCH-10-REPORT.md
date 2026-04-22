# CGF-1-BATCH-10 Report

**Batch:** CGF-1-BATCH-10  
**Author:** Developer  
**Date:** 2026-03-29  
**Status:** Complete — awaiting review

---

## Summary

Part A (three tech-debt items from BATCH-09 review) and Part B (CGF1-S0301 Storage
Gateway) are fully delivered.  Solution builds clean; 20 / 20 orchestrator tests pass
(+2 new).

---

## Part A — Tech debt closures

### A.1 — IG `ClusterSlave` `SetFilter` (Issue 1, P2)

**File:** `Hrot.IG/Modules/Orchestration/ClusterSlave.cs`

Added `_commandReader.SetFilter(cmd => cmd.TargetNodeId == _nodeId);` immediately after
constructing `DdsReader<NodeOpCommand>`, matching the established pattern in
`Hrot.CGF`, `Hrot.ExCon`, and `Hrot.SimHost`.

```csharp
_commandReader = new DdsReader<NodeOpCommand>(participant);
// Only process commands addressed to this node's roster ID (parity with SimHost/IOS/CGF).
_commandReader.SetFilter(cmd => cmd.TargetNodeId == _nodeId);
```

**Test coverage:** No dedicated IG+DDS integration test exists; manual verification —
confirmed `SetFilter` is the only code path that prevents the IG slave accepting
foreign-key `NodeOpCommand` samples.  Parity with the three other `ClusterSlave`
implementations is now code-identical.

**DEBT-TRACKER:** Row closed `✅`.

---

### A.2 — `CgfApplication` time bus documentation (Issue 2, P2 — Option B)

**File:** `Hrot.CGF/CgfApplication.cs`

Selected **Option B**: keep the minimal shell and document the wire path vs. listener
gap explicitly in the class-level XML `<summary>`.  The updated doc states:

- `TimeNetworkModule.CreateDescriptorTranslator` is wired, so `SwitchTimeModeWireDto`
  samples are bridged on/off DDS each frame in `Tick()` — the wire path is proven.
- `ClusterSlave` is constructed **without** the `_eventBus`; no `SlaveTimeModeListener`
  is registered.
- Ingressed `SwitchTimeModeEvent` messages are therefore **not acted on** by the
  minimal CGF shell.
- Full CGF1-S0205 end-to-end (slave switches to `SteppedSlaveController` via Future
  Barrier) requires wiring a `ModuleHostKernel` and `SlaveTimeModeListener`, which
  land in Phase 3+ when CGF acquires simulation entity management.

No production code changed.

**DEBT-TRACKER:** Row closed `✅`.

---

### A.3 — `TimeNetworkModule` class XML hygiene (Issue 4, P3)

**File:** `FDP/Toolkits/FDP.Toolkit.Time/TimeNetworkModule.cs`

Rewrote the class-level `<summary>` to:

- Name `CreateDescriptorTranslator` / `SwitchTimeModeWireDto` as the **supported
  path**.  Describes the integer `TargetModeInt` workaround for Cyclone IDL `enum`
  limits and the `CycloneNetworkModule` composition root pattern.
- Name `RegisterTranslators` as the **deprecated path** (already `[Obsolete]`),
  explaining why it cannot carry `SwitchTimeModeWireDto`.

The body of both methods is unchanged.

---

### A.4 — Subprocess CI (P3 — Opportunistic)

No change.  The subprocess `dotnet run --project Hrot.ClusterRunner -- --mode ci ...` path
remains in-process via `MinimalCIScenario.FinalEntitySnapshot` + `DeterministicRun_IsReproducible`
(BATCH-09 delivery).  DEBT-TRACKER row `Opportunistic` — not targeted for BATCH-10.

### A.5 — DEBT-TRACKER

Rows for A.1 and A.2 closed `✅` (see above).

---

## Part B — CGF1-S0301: Storage Gateway

### B.1 — `FileManifestEntry`, `NodeDistributionTarget`, `GatewayResult`

**File:** `Hrot.Orchestrator/StorageGatewayModule.cs` (new)

Three supporting types:

| Type | Fields |
|------|--------|
| `FileManifestEntry` (record) | `SourceUnc`, `RelativeDest` |
| `NodeDistributionTarget` (record) | `NodeId`, `DestinationPath` |
| `GatewayResult` (class) | `SuccessCount`, `FailureCount`, `IsFullSuccess` |

### B.2 — `StorageGatewayModule`

**File:** `Hrot.Orchestrator/StorageGatewayModule.cs` (same file)

```
public sealed class StorageGatewayModule
  const int MaxParallelCopies = 8
  Task<GatewayResult> PullToNasAsync(manifests, nasBasePath)
  Task<GatewayResult> PushToNodesAsync(nasSourcePath, targets)
```

Both methods run `Parallel.ForEach` with `MaxDegreeOfParallelism = MaxParallelCopies`
on a `Task.Run` thread-pool context.  Per-file exceptions are caught and counted;
the method always completes (no throw on partial failure).

`PullToNasAsync` creates intermediate destination directories automatically via
`Directory.CreateDirectory` before `File.Copy`.

### B.3 — `ClusterMaster` hook (CGF1-S0301 task item 2)

**File:** `Hrot.Orchestrator/ClusterMaster.cs`

New infrastructure added to `ClusterMaster`:

| Addition | Purpose |
|----------|---------|
| `DdsReader<NodeOpStatus> _nodeOpStatusReader` | Reads per-node ACKs (SerializeLocal + future ops) |
| `StorageGatewayModule? _gateway` | Optional gateway reference (set via `SetStorageGateway`) |
| `string _nasBasePath` | Gateway target root path |
| `Dictionary<Guid, SerializeLocalTask> _pendingSerializeTasks` | Tracks outstanding SerializeLocal rounds |
| `void SetStorageGateway(gateway, nasBasePath)` | Public wiring method for Phase 3 hosts |
| `internal void FanOutSerializeLocal(requestId, nodeIds, payloadJson)` | Issues SerializeLocal + registers pending task |
| `void ConsumeNodeOpStatuses()` | Reads NodeOpStatus; accumulates manifests; fires `PullToNasAsync` when all ACKs in |

`ConsumeNodeOpStatuses()` is called at the end of every `Tick()`.  `ResultJson` is
deserialized as `List<FileManifestEntry>` (JSON, case-insensitive); malformed entries
are logged and skipped.  When `RemainingAcks` reaches zero the gateway pull is invoked
fire-and-forget (`_ = _gateway.PullToNasAsync(...)`); Phase 3 SaveScenario handling
will add the full completion → `ClusterOpStatus` pipeline.

### B.4 — `StorageGatewayTests`

**File:** `Hrot.Orchestrator.Tests/StorageGatewayTests.cs` (new)

| Test | What it verifies |
|------|-----------------|
| `PullToNas_CopiesAllFiles` | 5 manifests → 5 files in NAS dir; `SuccessCount=5`, `FailureCount=0`; `MaxParallelCopies ≤ 8` |
| `PullToNas_FailingFile_ReturnsPartialFailureResult` | 4 valid + 1 non-existent → `SuccessCount=4`, `FailureCount=1`; no throw |

Both tests use local temp directories (created/deleted in a `try/finally` block) to
simulate the NAS path; no real SMB connection needed.

---

## Test results

```
dotnet test Hrot.Orchestrator.Tests --nologo --no-build

Passed!  - Failed: 0, Passed: 20, Skipped: 0, Total: 20, Duration: 3 s
```

20 = 18 pre-existing + 2 new `StorageGatewayTests`.

---

## Files changed

| File | Change |
|------|--------|
| `Hrot.IG/Modules/Orchestration/ClusterSlave.cs` | A.1: `SetFilter(cmd => cmd.TargetNodeId == _nodeId)` |
| `Hrot.CGF/CgfApplication.cs` | A.2: class XML — Option B bus / listener gap documented |
| `FDP/Toolkits/FDP.Toolkit.Time/TimeNetworkModule.cs` | A.3: class XML — `CreateDescriptorTranslator` / `SwitchTimeModeWireDto` as supported path |
| `Hrot.Orchestrator/StorageGatewayModule.cs` | **NEW** — `FileManifestEntry`, `NodeDistributionTarget`, `GatewayResult`, `StorageGatewayModule` |
| `Hrot.Orchestrator/ClusterMaster.cs` | B.3: `_nodeOpStatusReader`, gateway fields, `SetStorageGateway`, `FanOutSerializeLocal`, `ConsumeNodeOpStatuses` |
| `Hrot.Orchestrator.Tests/StorageGatewayTests.cs` | **NEW** — 2 `StorageGatewayTests` |
| `.dev/DEBT-TRACKER.md` | A.1 + A.2 rows closed `✅` |
| `.dev/cgf-1/CGF-1-TASK-TRACKER.md` | CGF1-S0301 marked `[x]`; progress counter updated |

---

## Success criteria checklist

- [x] Part A: IG `SetFilter` added; CGF bus documented (Option B); `TimeNetworkModule` XML refreshed; DEBT-TRACKER updated.
- [x] Part B: CGF1-S0301 success conditions met (`PullToNas_CopiesAllFiles`, `PullToNas_FailingFile_ReturnsPartialFailureResult` — both pass).
- [x] Solution build clean (0 errors, 265 pre-existing warnings unchanged).
- [x] Tests green: 20 / 20 `Hrot.Orchestrator.Tests`.
- [x] DEBT-TRACKER updated.
- [x] Report filed.

---

## Open items / deferred

| Item | Status |
|------|--------|
| A.4 subprocess CI (`dotnet run … --mode ci`) | Opportunistic — no change |
| CGF1-S0205 full end-to-end (`SlaveTimeModeListener` in CGF) | Phase 3+ (`ModuleHostKernel` prerequisite) |
| `ClusterMaster.ConsumeNodeOpStatuses` full lifecycle (`ClusterOpStatus` publish on pull completion) | Phase 3 SaveScenario handling |
