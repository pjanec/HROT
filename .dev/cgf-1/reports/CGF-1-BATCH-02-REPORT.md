# CGF-1-BATCH-02 Report

**Batch number:** CGF-1-BATCH-02  
**Author:** Developer  
**Date:** 2026-03-28  
**Related batch:** CGF-1-BATCH-01 (APPROVED)

---

## Commands run

```powershell
# Build
dotnet build IOS-IG-SimHost.sln --nologo
# → Build succeeded. 0 Warning(s). 0 Error(s).

# Test — targeted projects
dotnet test Hrot.SimHost.Integration.Tests --nologo --no-build
dotnet test Hrot.Orchestrator.Tests --nologo --no-build
dotnet test Hrot.NED.Tests --nologo --no-build
dotnet test Hrot.SimHost.Tests --nologo --no-build
dotnet test Hrot.IG.Tests --nologo --no-build
```

---

## Test summary

| Project | Passed | Failed | Notes |
|---------|--------|--------|-------|
| `Hrot.SimHost.Integration.Tests` | 30 | 0 | Includes new `ClusterSlaveHeartbeatTests` + existing migration/lifecycle |
| `Hrot.Orchestrator.Tests` | 1 | 0 | `OrchestratorPublishesStandbyOnStartup` — domain 15, exactly-one assertion |
| `Hrot.NED.Tests` | 43 | 0 | `AllTopicStructsHaveDdsTopicAttribute` now reflection-based |
| `Hrot.SimHost.Tests` | 360 | 0 | No regressions |
| `Hrot.IG.Tests` | 429 | 0 | No regressions |

**Full-solution parallel run note:** `dotnet test IOS-IG-SimHost.sln` may still produce intermittent failures across other assemblies (FDP.Toolkit tests, NetworkDemo) that use domain 0 concurrently. Mitigations applied in this batch (domain isolation + `[Collection]` for CGF tests) eliminate CGF-originated flakes. Residual risk documented in `CGF-1-ONBOARDING.md`. Recommended CI workaround until a broader solution lands: `dotnet test IOS-IG-SimHost.sln --maxcpucount:1` or run integration-test assemblies individually.

---

## DEBT-TRACKER rows closed

All 7 rows with **Target Fix = CGF-1-BATCH-02** marked ✅:

| Row | What was done |
|-----|---------------|
| P2 Performance — `ProcessRequests` dual call | Removed redundant `_idAllocatorServer?.ProcessRequests()` from `ClusterMaster.Tick()`. Background thread loop is the sole caller. |
| P2 Testing/Infra — DDS parallel test isolation | Orchestrator tests moved to domain 15 + `[Collection("OrchestratorTests", DisableParallelization=true)]`; heartbeat test uses domain 16; onboarding note added. |
| P3 Testing — `OrchestrationSchemaTests` reflection | `AllTopicStructsHaveDdsTopicAttribute` now reflects over all public, non-codegen structs in `Hrot.NED.Descriptors.Orchestration` (filters `*View`, `*_Native`, `*KeyHolder`). |
| P3 Testing — exactly-one sample | `OrchestratorPublishesStandbyOnStartup` collects all samples in window, asserts `Count == 1`. |
| P3 Observability — 5 s warning | `SimHostApp.EnsureIdAllocatorRouting` logs `FdpLog.Warn` at ≈5 s when `IdAllocatorLocalFallbackEnabled == false` and no publication match found yet. |
| P3 Performance — `NodeRoster.PruneStale` | Replaced per-tick `new List<int>()` with reusable `_staleBuffer` field (cleared each call). |
| P3 Hygiene — `ClusterMaster._profiles` | Removed dead `Dictionary<int, NodeHealthProfile> _profiles` and its write site in `IngestHeartbeats`. `NodeRoster` is the sole source of truth. |

---

## CGF1-S0104 success conditions

### `OrchestratorReceivesHeartbeatsFromBothNodes`

- `ClusterMaster` (domain 16) + `SimHostApp` (nodeId=1, domain 16) + `CgfApplication` (nodeId=400, domain 16) run in-process.
- Both tick for up to 2 s wall-clock (16 ms sleep per iteration ≈ 60 Hz).  
- After 2 s, `ClusterMaster.NodeRoster.ActiveNodes` contains both nodeId=1 and nodeId=400.
- Both have `LocalClusterState == Standby`.
- **Result: PASSED** (part of `Hrot.SimHost.Integration.Tests` run above).

### `IDsmHandler` FDP boundary audit

- `IDsmHandler` is declared in `Hrot.Common.Orchestration.IDsmHandler` (assembly `Hrot.Common`).
- `Hrot.Common.csproj` references only `Hrot.NED` and `Fdp.Kernel` — no reverse Hrot→FDP.
- `grep -r "IDsmHandler" FDP/` returns 0 matches (no FDP project references the interface).

---

## Design decisions & notes

### `Hrot.Common` vs `Hrot.ClusterRunner`

Placed `IDsmHandler` in a new minimal `Hrot.Common` project (not in `Hrot.ClusterRunner`) because `Hrot.ClusterRunner` references `Hrot.SimHost`, `Hrot.IG`, `Hrot.ExCon` — putting the interface there would require those projects to reference `Hrot.ClusterRunner`, creating circular dependencies.

### `ClusterSlave` constructor overloads in `Hrot.SimHost`

A no-arg `ClusterSlave()` constructor is preserved so existing tests that exercise only handler registration (e.g. `RecordReplayIntegrationTests`) continue to work without a live DDS participant. The DDS-active constructor `ClusterSlave(DdsParticipant, int, string)` is used in production via `NodeBootstrapper.BuildOrchestration(…, participant, subsystemName)`.

### `EcsRecordReplayController : IDsmHandler` stub methods

Added `CanHandle`, `PrepareAsync`, `Commit`, `Abort` stubs (returns `false` / no-op / null task). Full 2PC dispatch wiring requires `ClusterStateChangedEvent` and `FdpEventBus` integration, which is scheduled for CGF1-S0202 in BATCH-03.

### IgApplication ClusterSlave exception guard

`_drillSlave` is created inside the `try { } catch { }` block in `InitializeNetwork`. If DDS initialization fails (e.g. no CycloneDDS native library), `_drillSlave` remains null and `Update()` no-ops gracefully.

### Domain numbering convention

- Domain 0: application/production default  
- Domain 10: `EntityLifecycleIntegrationTests`  
- Domain 15: `Hrot.Orchestrator.Tests`  
- Domain 16: `Hrot.SimHost.Integration.Tests` — CGF/ClusterSlave tests  

---

## Known issues / deferred work

| Issue | Deferred to |
|-------|------------|
| CGF1-S0103 task text says "wait for orchestrator heartbeat"; implementation uses publication match — align docs | CGF-1-BATCH-03 |
| `NodeOpType` uses `NodeReplaySeek` (value 13) vs design `ReplaySeek` — naming inconsistency in docs | CGF-1-BATCH-03 |
| `EjectNode(Guid)` vs `NodeHeartbeat.NodeId (int)` in S0105 spec — reconcile before implementing | CGF-1-BATCH-03 |
| Full `dotnet test IOS-IG-SimHost.sln` may still flake from non-CGF assemblies sharing domain 0 | Broader isolation initiative |

---

## Suggested commit message

```
feat(cgf-1): S0104 ClusterSlave foundation + BATCH-01 debt clearance (CGF-1-BATCH-02)

Part A — BATCH-01 debt (7 items):
- ClusterMaster.Tick: removed redundant ProcessRequests() call (P2 Perf)
- Orchestrator tests: domain 15, OrchestratorTests collection, exactly-one sample
  assertion on OrchestratorPublishesStandbyOnStartup (P2 Infra / P3 Testing)
- OrchestrationSchemaTests: reflection scan replaces fixed-list (P3 Testing)
- SimHostApp.EnsureIdAllocatorRouting: FdpLog.Warn at 5 s (P3 Observability)
- NodeRoster.PruneStale: reusable _staleBuffer (P3 Performance)
- ClusterMaster._profiles: removed dead dictionary (P3 Hygiene)
- CGF-1-ONBOARDING.md: DDS test isolation contributor note

Part B — CGF1-S0104:
- Add Hrot.Common (IDsmHandler with CanHandle/PrepareAsync/Commit/Abort)
- Add Hrot.CGF library + Hrot.CGF.Standalone (CgfApplication, CGF ClusterSlave)
- Full ClusterSlave implementation in Hrot.SimHost (heartbeat + command queue)
- ClusterSlave added to Hrot.IG, Hrot.ExCon, Hrot.CGF
- Wire SimHost ClusterSlave via NodeBootstrapper.BuildOrchestration in SimHostApp.OnLoad
- Wire IG ClusterSlave in IgApplication.InitializeNetwork / Update / Shutdown
- Wire IOS ClusterSlave in IosSubsystem.Initialize / Update / Shutdown
- Register CGF via new CgfSubsystem + RunMode.CGF in Hrot.ClusterRunner
- EcsRecordReplayController: stub IDsmHandler (CanHandle=false; S0202 wires full 2PC)
- Integration test: ClusterSlaveHeartbeatTests.OrchestratorReceivesHeartbeatsFromBothNodes
  (domain 16; SimHost nodeId=1, CGF nodeId=400; both in roster within 2 s; DSM=Standby)

Tests: 863 total passing across affected assemblies; 0 failures; 0 warnings

Related: .dev/cgf-1/CGF-1-DESIGN.md §3.4, CGF-1-TASK-DETAIL.md §CGF1-S0104,
         .dev/DEBT-TRACKER.md (7 rows closed)
```
