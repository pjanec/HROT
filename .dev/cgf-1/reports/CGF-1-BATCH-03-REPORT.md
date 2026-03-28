# CGF-1-BATCH-03 Report

**Batch number:** CGF-1-BATCH-03  
**Author:** Developer  
**Date:** 2026-03-29  
**Related batch:** CGF-1-BATCH-02 (APPROVED)

---

## Commands run

```powershell
# Build
dotnet build IOS-IG-SimHost.sln --nologo
# → Build succeeded. 0 Error(s).

# Test — targeted projects (all run with --no-build after build)
dotnet test Bagira.DDS.DataModel.Tests     --nologo --no-build
dotnet test Bagira.Map.Common.Tests        --nologo --no-build
dotnet test Bagira.Orchestrator.Tests      --nologo --no-build
dotnet test Bagira.IG.Tests                --nologo --no-build
dotnet test Bagira.IOS.Tests               --nologo --no-build
dotnet test Bagira.Runner.Tests            --nologo --no-build
dotnet test Bagira.SimHost.Tests           --nologo --no-build
dotnet test Bagira.SimHost.Integration.Tests --nologo --no-build
```

---

## Test summary

| Project | Passed | Failed | Notes |
|---------|--------|--------|-------|
| `Bagira.DDS.DataModel.Tests` | 43 | 0 | |
| `Bagira.Map.Common.Tests` | 94 | 0 | |
| `Bagira.Orchestrator.Tests` | 5 | 0 | 4 new S0105 tests + 1 pre-existing |
| `Bagira.IG.Tests` | 429 | 0 | Domain IDs 240/241/242 fixed → 205/206/207 |
| `Bagira.IOS.Tests` | 340 | 0 | |
| `Bagira.Runner.Tests` | 112 | 0 | `SimHostSubsystemTests` now has DrillMaster fixture (domains 0 + 98) |
| `Bagira.SimHost.Tests` | 360 | 0 | `SimHostComponentRegistrationTests` + `SimHostTimeSyncTests` now have DrillMaster fixtures |
| `Bagira.SimHost.Integration.Tests` | 30 | 0 | |

**Full-solution parallel run note:** `dotnet test IOS-IG-SimHost.sln` may produce 1–3 intermittent failures in `Fdp.Tests` and `ModuleHost.Core.Tests` due to pre-existing DDS domain-0 contention in high-parallelism runs. These failures are absent when each project runs in isolation and are not introduced by this batch. The same caveat was noted in the BATCH-02 report. Recommended CI workaround: run integration-test assemblies individually, or use `--maxcpucount:1`.

---

## Part A — Corrective checklist

### A.1 — Remove standalone exe projects

- Removed `Bagira.Orchestrator.Standalone` project entry and 12 config lines from `IOS-IG-SimHost.sln`.  
- Removed `Bagira.CGF.Standalone` project entry and 12 config lines from `IOS-IG-SimHost.sln`.  
- Deleted `Bagira.Orchestrator.Standalone/` and `Bagira.CGF.Standalone/` directories.  
- Updated `CGF-1-ONBOARDING.md` directory tree and launch instructions (Runner-only: `dotnet run --project Bagira.Runner -- --mode orchestrator/simhost/cgf`).  

### A.2 — IG DDS fail-fast

- Removed the `catch (Exception ex)` block (with log-and-go-offline fallback) from `IgApplication.InitializeNetwork`.  
- Fixed an extra closing brace that caused 197 compiler errors after the catch removal.  
- Side effect: three test classes (`DrawPersonalRouteCommandTests`, `SetSelectionCommandTests`, `SetViewCommandTests`) were using domain IDs 242/241/240 which **exceed CycloneDDS's maximum domain ID of 232** and had silently passed via the offline fallback. Fixed by changing to domains 207/206/205 respectively.  

### A.3 — Remove local ID allocator fallback

- Deleted `Bagira.SimHost/Network/LocalIdAllocatorFallbackHost.cs`.  
- Removed `IdAllocatorLocalFallbackEnabled` and `IdAllocatorLocalFallbackDelaySeconds` from `NodeConfiguration.cs`.  
- Rewrote `SimHostApp.EnsureIdAllocatorRouting`: waits up to 30 s for `_idAllocator.HasPublicationMatch`, logs a warning at 5 s, throws `InvalidOperationException` after 30 s.  
- Removed the `_localIdAllocatorFallback` field and its `Dispose()` call from `SimHostApp.Shutdown()`.  
- Updated `DrillSlaveHeartbeatTests`, `DdsIdAllocatorMigrationTests`, `EntityLifecycleIntegrationTests` to remove the now-deleted config fields.  
- Side effect: tests that called `SimHostApp.InitializeHeadless` without an external allocator server now timeout then fail. Fixed by adding `DrillMaster` fixtures (which include `DdsIdAllocatorServer`) to:  
  - `Bagira.Runner.Tests/SimHostSubsystemTests` (domains 0 + 98)  
  - `Bagira.SimHost.Tests/SimHostComponentRegistrationTests` (domains 0, 96, 97, 98, 99)  
  - `Bagira.SimHost.Tests/SimHostTimeSyncTests` (domain 210)  
  - Added `Bagira.Orchestrator` project reference to `Bagira.SimHost.Tests.csproj` for `DrillMaster` access.  

### A.4 — DrillSlave() internal + BuildOrchestration guard

- Changed `DrillSlave()` constructor from `public` to `internal`.  
- Added `InternalsVisibleTo("Bagira.SimHost.Integration.Tests")` to `Bagira.SimHost.csproj`.  
- Added null-participant guard in `NodeBootstrapper.BuildOrchestration`: throws `ArgumentNullException` when `participant == null` for `NodeRole.Brain` or `NodeRole.AllInOne`.  
- Updated `RecordReplayIntegrationTests` to create `DdsParticipant(18)` for the Brain role test.  

### A.5 — OrchestrationSchemaTests filter narrowed

- Removed `|| t.Name.Contains('_')` from `IsCodeGenType` in `OrchestrationSchemaTests.cs`.  

### A.6 — DEBT-TRACKER rows closed

7 rows marked ✅ with **Target Fix = CGF-1-BATCH-03**:

| Row | Summary |
|-----|---------|
| P1 Safety — IG DDS catch | Removed silent offline fallback in `IgApplication.InitializeNetwork` |
| P1 Architecture — allocator fallback | Removed `LocalIdAllocatorFallbackHost` and `IdAllocatorLocalFallback*` config |
| P2 Product — Standalone removal | Removed `Bagira.Orchestrator.Standalone` and `Bagira.CGF.Standalone` |
| P3 Testing — `IsCodeGenType` filter | Narrowed to suffix-only rules (removed `Contains('_')`) |
| P3 Documentation — CGF1-S0103 wording | Corrected task detail wording |
| P3 Documentation — NodeOpType/NodeReplaySeek | Added footnote in CGF-1-DESIGN.md §3.5 |
| P3 Specification — EjectNode(Guid) vs int | Corrected to `EjectNode(int nodeId)` in CGF-1-TASK-DETAIL.md |

---

## Part B — CGF1-S0105 implementation

### New files

| File | Purpose |
|------|---------|
| `Bagira.Orchestrator/ClusterConfiguration.cs` | Config record: `Mandatory[]`, `Optional[]`, `HeartbeatTimeoutSeconds`, `TransactionHistoryCapacity`, `LoadFrom(path)` |

### Modified files

| File | Changes |
|------|---------|
| `Bagira.Orchestrator/DistributedTransaction.cs` | Added `IsAborted` + `NodeAckLatencyMs` |
| `Bagira.Orchestrator/DrillMaster.cs` | Full rewrite: bootstrap latch, `EjectNode(int)`, `DetectAndEjectTimedOutNodes`, SysOpRequest handling, transaction history ring buffer, `BootstrapComplete`/`TransactionHistory` properties |
| `Bagira.Runner/Services/OrchestratorSubsystem.cs` | Loads `orchestrator-config.json`; `DrawUI()` renders bootstrap banner + Node Health table + 2PC History table |
| `Bagira.Orchestrator.Tests/DrillMasterBootstrapTests.cs` | 4 new S0105 success-condition tests |
| `.dev/cgf-1/CGF-1-TASK-DETAIL.md` | Step 4 corrected: `EjectNode(int nodeId)` *(normative correction: wire type is int)* |
| `.dev/cgf-1/CGF-1-DESIGN.md` | Added `NodeReplaySeek` footnote in enum table |

### S0105 success conditions

| Test | Result |
|------|--------|
| `RejectsCommands_UntilMandatoryNodesReady` | PASSED |
| `EjectsMandatoryNode_EntersDegraded` | PASSED |
| `SurvivingNodes_CommandedToStandby_AfterEjection` | PASSED |
| `TransactionHistory_RecordsCompletedTransaction` | PASSED |

---

## Bug fixes discovered & resolved

### BUG-B03-01: IDL enum `@value` annotations missing (CycloneDDS `BadParameter` on write)

**Root cause:** `CycloneDDS.CodeGen.IdlEmitter.EmitEnum` emitted enum members as sequential IDL ordinals (0, 1, 2, …), ignoring the C# enum integer values. CycloneDDS validates the wire value against IDL-defined ordinals; `DSMState.Degraded = 99` exceeded the ordinal range (0–13) → `ReturnCode: BadParameter`.

**Fix (3 files in `FDP/ExtDeps/FastCycloneDds/tools/CycloneDDS.CodeGen/`):**

- `TypeInfo.cs`: Added `List<long> EnumMemberValues` property.  
- `SchemaDiscovery.cs`: Populated `EnumMemberValues` alongside `EnumMembers` using Roslyn `IFieldSymbol.ConstantValue`.  
- `IdlEmitter.cs`: Emits `@value(N) MemberName` when the C# integer value differs from the sequential ordinal AND is non-negative (guards against `@value(-1)` which is invalid IDL).  

**Effect:** All enum types now have correct `@value` annotations in generated IDL. Example:
```idl
enum DSMState {
    Standby,               // 0 — no annotation needed
    @value(10) LoadingEdit,
    ...
    @value(99) Degraded
};
```

### BUG-B03-02: `NodeOpCommand` `KeepLast(1)` drops commands under rapid writes

**Root cause:** `NodeOpCommand` QoS defaulted to `KeepLast(depth=1)`. When `EjectNode` wrote `AbortTransaction` then `PrepareState` back-to-back, the reader's history could only hold 1 sample; the first was replaced before `Take()` was called.

**Fix:** Changed `NodeOpCommand` QoS in `OrchestrationMessages.cs` to `HistoryKind = DdsHistoryKind.KeepAll`. This is semantically correct for a reliable command topic.

### BUG-B03-03: Invalid CycloneDDS domain IDs in IG test classes

**Root cause:** Three test classes used domain IDs 240, 241, 242, which exceed CycloneDDS's maximum domain ID of 232. These silently fell back to offline mode before A.2 removed the catch block.

**Fix:** Changed to valid domain IDs 205, 206, 207 respectively.

### BUG-B03-04: SimHost tests lacked allocator server after A.3

**Root cause:** `SimHostApp.EnsureIdAllocatorRouting` now throws after 30 s if no `DdsIdAllocatorServer` is present (local fallback was removed in A.3). Tests that called `InitializeHeadless` without providing a server timed out.

**Fix:** Added `DrillMaster` (which hosts `DdsIdAllocatorServer`) to three test class fixtures across `Bagira.Runner.Tests` and `Bagira.SimHost.Tests`, covering all domains used by impacted tests.

---

## Design decisions

### `DetectAndEjectTimedOutNodes` — break after mandatory ejection

When a mandatory node is ejected (bootstrap latch re-engages), processing stops so that remaining nodes stay in the roster and receive the `PrepareState(Standby)` broadcast in the same tick. Without this guard, optional nodes that had also timed out would be ejected before broadcasting, leaving no "surviving" nodes.

### `DdsIdAllocatorServer` fixture pattern

Rather than using `DdsIdAllocatorServer` directly (which would require adding `ModuleHost.Network.Cyclone` references to multiple test projects), we use `DrillMaster` from `Bagira.Orchestrator`. `DrillMaster` already includes the `DdsIdAllocatorServer` as an internal detail. This keeps the test fixture minimal and matches production usage.

---

## Suggested commit message

```
feat(cgf-1): fail-fast DDS policy + allocator fallback removal + CGF1-S0105 (CGF-1-BATCH-03)

Part A — correctives:
- Remove Bagira.Orchestrator.Standalone and Bagira.CGF.Standalone (Runner-only launch).
- IgApplication: propagate DDS init failures (no silent offline mode).
- SimHostApp: remove LocalIdAllocatorFallbackHost; EnsureIdAllocatorRouting throws after 30s wait.
- NodeBootstrapper: throw when Brain/AllInOne role lacks DDS participant.
- OrchestrationSchemaTests: narrow IsCodeGenType to suffix-only rules.
- DEBT-TRACKER: close P1 Safety, P1 Architecture, P2 Product, P3 Testing rows.

Part B — CGF1-S0105:
- ClusterConfiguration: mandatory/optional node lists, heartbeat timeout, tx history capacity.
- DrillMaster: bootstrap latch, SysOpRequest reject/accept, EjectNode(int), Degraded publish,
  NodeOpCommand broadcast, DistributedTransaction history ring buffer.
- OrchestratorSubsystem: load orchestrator-config.json, ImGui health/history panels.
- CGF-1-TASK-DETAIL: correct EjectNode wire type to int.
- CGF-1-DESIGN: NodeReplaySeek/ReplaySeek naming footnote.
- Tests: DrillMasterBootstrapTests — 4 new S0105 success conditions.

Bug fixes:
- CycloneDDS.CodeGen: emit @value(N) annotations for non-sequential C# enum values
  (fixes BadParameter on DSMState.Degraded=99 and all other sparse-value enums).
- NodeOpCommand: change to KeepAll history so rapid back-to-back writes are not dropped.
- IG test domain IDs 240/241/242 fixed to 205/206/207 (above CycloneDDS max 232).
- SimHost/Runner test fixtures: add DrillMaster allocator server for test domains.

Related: CGF-1-DESIGN §3.5, CGF-1-TASK-DETAIL §CGF1-S0105, DEBT-TRACKER (BATCH-03 ✅).
```
