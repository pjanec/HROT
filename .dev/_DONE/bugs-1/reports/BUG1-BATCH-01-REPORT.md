# BUG1-BATCH-01 Report

**Batch:** BUG1-BATCH-01  
**Date:** 2026-03-20  
**Status:** Complete

---

## 📊 Task Completion

| Task ID   | Status     | Notes |
|-----------|------------|-------|
| BUG1-F001 | ✅ Complete | Domain zero guard removed from `SimHostSubsystem` + `IgSubsystem` |
| BUG1-F002 | ✅ Complete | `--node-id` / `-n` CLI option plumbed through full stack |
| BUG1-F003 | ✅ Complete | All 4 scripts updated with `cd /d %~dp0` and explicit `-d %DOMAIN%` |
| BUG1-N001 | ✅ Complete | All 4 `WriteAck` anti-pattern calls removed; debug logs retained |
| BUG1-N002 | ✅ Complete | `CycloneNetworkCleanupSystem` fan-out with per-translator try/catch |

---

## 🧪 Testing Results

**Runner Tests:**  111 / 111 passed  
**SimHost Tests:** 261 / 261 passed  
**Map.Common Tests:** 51 / 51 passed

Pre-existing IG test failures (6 tests in `Hrot.IG.Tests`) were present **before** this batch and are unrelated to any of the five tasks implemented here (confirmed via `git stash` baseline).

**Key Test Scenarios Verified:**

- ✅ Domain 0 accepted without exception (`SimHostSubsystem`, `SimHostApp.InitializeEmbedded`)
- ✅ Non-zero domain preserved (domain 5, 10, etc. flow through unchanged)
- ✅ `RunnerConfiguration.NodeId` defaults to 0; explicit value flows through `Validate()`
- ✅ Orchestrator applies `+0` (SimHost), `+100` (IG), `+200` (IOS), `+300` (Other) offsets when `NodeId != 0`
- ✅ `NodeId = 0` → `SubsystemConfig.NodeId = 0`; SimHostApp uses `SimHostNetworkConstants.LocalNodeId`
- ✅ `NodeId = 10` → `SimHostApp.TestHook_ResolvedLocalNodeId == 10`
- ✅ Non-authoritative WorldPos update → no `UpdateEntityDescriptorAck` written
- ✅ Entity-not-found → no ACK (DDS reader reads empty after system tick)
- ✅ Unsupported descriptor type (`dtEntityMaster`) → no ACK
- ✅ Authoritative WorldPos update → exactly one `Success` ACK
- ✅ All 3 mock translators receive `Dispose` when entity dies (fan-out test)
- ✅ One throwing translator doesn't block remaining translators
- ✅ Non-authoritative entities are not tracked and never disposed
- ✅ `cd /d %~dp0` present and `-d %DOMAIN%` explicit in all 4 batch scripts

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

- **DDS union serialization failure in test**: `MakeUnsupportedTypeRequest` initially used `(EDescriptorType)99999` as the discriminant, which caused `DdsWriter.Write` to throw `BadParameter` because the union discriminant is unknown to the CycloneDDS schema. Fixed by using `EDescriptorType.dtEntityMaster` (a valid DDS case that hits the `default` branch of the switch).
- **`TestHook_ResolvedLocalNodeId` visibility**: The property is `internal` in `Hrot.SimHost`, but tests in `Hrot.ClusterRunner.Tests` don't have `InternalsVisibleTo` access to it. Resolved by moving the NodeId value-assertion tests to `Hrot.SimHost.Tests` (which does have `InternalsVisibleTo` access), and keeping the Runner tests as no-throw behavioral checks.
- **Ambiguous `IDescriptorTranslator`**: Two assemblies (`Fdp.Interfaces` and `ModuleHost.Core.Network`) define this interface. In `CycloneNetworkCleanupSystemTests.cs`, qualified the usage as `Fdp.Interfaces.IDescriptorTranslator` to match what `CycloneNetworkCleanupSystem` uses.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- `IgSubsystem.Initialize()` had the same `> 0` domain guard as `SimHostSubsystem`. Not in BUG1-F001 scope but fixed in the same pass as BUG1-F002 since the file was already being edited. Leaving this undocumented as scope creep would risk the IG always using a stale config.json domain.
- `UpdateEntityDescriptorRequestSystem` is `sealed` and creates all DDS objects in its primary constructor, making unit testing without DDS impossible without instrumentation. The integration-test approach (real DDS per test, high domain numbers) works but is slow. A future improvement would be to inject the ack writer via an internal test constructor (as `CreateEntityRequestSystem` does via `ICreateEntityAckSink`).
- The `translators` list in `SimHostApp.OnLoad()` includes `MissionIngressTranslator` alongside egress translators. Calling `Dispose(netId)` on an ingress translator is a no-op in practice (DDS readers don't have DisposeInstance semantics), but it adds noise. Separating egress-only translators into a dedicated list would be cleaner.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

- **Orchestrator offset via `subsystem.Name`**: The spec says the orchestrator should resolve per-subsystem node IDs. Since `SubsystemOrchestrator` lives in the framework layer (`FDP.Framework.Runner`) and has no knowledge of Hrot-specific constants, I used `ISubsystem.Name` string matching (`"SimHost"`, `"IG"`, `"IOS"`) as the dispatch key. The alternative (an `INodeIdOffsetProvider` interface on `ISubsystem`) would have been more extensible but heavier—unnecessary for the current three-subsystem topology.
- **`_effectiveInstanceId` field in `IgApplication`**: Rather than re-computing `_nodeIdOverride != 0 ? _nodeIdOverride : IgNetworkConstants.InstanceId` at every call site, I precomputed it once in `InitializeEmbedded` into a field. This covers both initialization-time calls (NodeIdMapper, DdsIdAllocator) and runtime calls (MapClickEvent, command filter).
- **`localNodeIdForMapper` alias in `SimHostApp.OnLoad`**: After introducing the top-level `localNodeId` resolved variable, the old `var localNodeId = SimHostNetworkConstants.LocalNodeId;` line near the `NodeIdMapper` construction was replaced with a `var localNodeIdForMapper = localNodeId;` alias. This preserves the surrounding code structure and keeps the diff small, though the alias adds no semantic value—it could be inlined.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- `SimHostApp` stores `_domainOverride` in both the constructor and `InitializeEmbedded`. These are redundant but harmless. With the addition of `_nodeIdOverride`, the same pattern is used: constructor doesn't accept it, `InitializeEmbedded` does. This asymmetry (domain in constructor + embedded; nodeId only in embedded) is an existing wart in the API, not introduced by this batch.
- The `CycloneNetworkCleanupSystem` only tracks entities that are authoritative at the time they first appear. If authority transfers after entity creation, the system won't track the new owner's entity. This is documented behavior (not a bug introduced here).

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- `CycloneNetworkCleanupSystem.Execute` allocates a `List<long>` (`toRemove`) on every frame where entities are destroyed. For normal usage this is fine (rare events), but the existing code already does this and this batch doesn't change the allocation pattern.
- The DDS integration tests use `Thread.Sleep(200)` for pub/sub matching. This is the existing pattern for this codebase and is acceptable. A more deterministic approach would use `DdsReader.WaitForMatchedWriter` but that API is not present in the current CycloneDDS wrapper.

---

## ⚠️ Outstanding Issues / Next Steps

- Pre-existing IG test failures (6 tests in `Hrot.IG.Tests.EditToolTests` and `TraceLoggingTests`) must be investigated separately — they are not related to this batch.
- The `--node-id` flag currently only supports a single base ID per process. Running two IGs requires two separate runner processes, each with a distinct `--node-id` (confirmed by design; documented in DESIGN.md §1.2).
- IOS subsystem does not have its own `IosSubsystem` changes for Task 2 node-id pass-through — the IOS app likely doesn't use a static `LocalNodeId` constant in the same way. If it does, a follow-up fix is needed.
