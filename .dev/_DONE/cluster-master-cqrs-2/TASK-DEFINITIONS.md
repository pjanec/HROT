# Task Definitions: ClusterMaster CQRS Post-Refactoring Cleanups (cqrs-2)

**Topic:** `cluster-master-cqrs-2`  
**Source:** `.dev/cluster-master-cqrs-2/task-list.txt`  
**Context:** Follow-up improvements and bug fixes after the cqrs-1 refactoring. All tasks eliminate primitive obsession, strengthen type safety, fix correctness bugs, and clarify architectural boundaries.

---

## TASK-D01 — Explicit Payload Structs (Replace Boxed Primitives)

**Phase:** 2  
**Touches:** `FDP/Toolkits/FDP.Toolkit.Orchestration/`, `Hrot.Common/Orchestration/NodeOpSlaveTranslator.cs`, `Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs`, `FDP/Toolkits/FDP.Toolkit.Orchestration/ClusterSlave.cs`, `Hrot.Orchestrator/ClusterMaster.cs`

**Problem:**  
Several `ExecuteNodeOpIntent.DomainPayload` uses box primitive types (`int`, `long`, `Guid`) rather than explicit named structs. This causes:  
- `ClusterSlave.DispatchIntent()` uses `intent.DomainPayload is int stateId` — brittle, breaks if payload is ever wrapped.  
- `ClusterMaster` fans out `CommitState` with a raw `(int)tStep.TargetState`.  
- `NodeOpSlaveTranslator.DeserializeNodePayload()` returns boxed primitives for `CommitState`, `NodeReplaySeek`, `AbortTransaction`.  
- `NodeOpMasterTranslator.SerializeNodePayload()` has an `if (domainPayload is int stateId)` special-case.

**Deliverables:**

1. **New file:** `FDP/Toolkits/FDP.Toolkit.Orchestration/NodeOpPayloads.cs`  
   Add three new `readonly record struct` types (alongside the already-existing payloads):
   ```csharp
   public readonly record struct CommitStatePayload(int TargetStateId);
   public readonly record struct ReplaySeekPayload(long TargetWallTicks);
   public readonly record struct AbortTransactionPayload(Guid TargetTransactionId);
   ```

2. **Update `ClusterSlave.cs`:**  
   - `DispatchIntent()` dedup discriminant: change `intent.DomainPayload is int sd` → `intent.DomainPayload is CommitStatePayload csp ? csp.TargetStateId : -1`  
   - `DispatchIntent()` CommitState handling: change `intent.DomainPayload is int stateId ? stateId : _localStateId` → `intent.DomainPayload is CommitStatePayload p ? p.TargetStateId : _localStateId`  
   - `Tick()` buffered intent dedup: update same `intent.DomainPayload is int v` → `is CommitStatePayload csp ? csp.TargetStateId : -1`

3. **Update `ClusterMaster.cs`:**  
   Find the call sites that fan out `NodeOpType.CommitState` with a raw `(int)` payload and change them to use `new CommitStatePayload(...)`.  
   Similarly, any fan-out of `NodeReplaySeek` with a `long` should use `new ReplaySeekPayload(...)` and `AbortTransaction` with a `Guid` should use `new AbortTransactionPayload(...)`.

4. **Update `NodeOpSlaveTranslator.cs` (`Hrot.Common/Orchestration/NodeOpSlaveTranslator.cs`):**  
   In `DeserializeNodePayload()`, return `new CommitStatePayload(stateId)` instead of `(object)stateId`, and similarly for `ReplaySeekPayload`/`AbortTransactionPayload`.

5. **Update `NodeOpMasterTranslator.cs` (`Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs`):**  
   In `SerializeNodePayload()`, replace the `if (domainPayload is int stateId) return stateId.ToString()` branch with handling for `CommitStatePayload`, `ReplaySeekPayload`, `AbortTransactionPayload`.

**Tests Required:**
- Unit test in `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/` verifying ClusterSlave CommitState dispatch using `CommitStatePayload` (not raw int).
- Update any existing test that passes raw `int` as CommitState DomainPayload to use `CommitStatePayload`.
- Translator round-trip test: serialize `CommitStatePayload` to JSON → `DeserializeNodePayload()` returns `CommitStatePayload`.

---

## TASK-D02 — Add NodeOpType to NodeOpCompletedEvent and NodeOpStatus

**Phase:** 3  
**Touches:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterCqrsEvents.cs`, `Hrot.NED/Orchestration/OrchestrationMessages.cs`, `Hrot.Common/Orchestration/NodeOpSlaveTranslator.cs`, `Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs`, `Hrot.Orchestrator/ClusterMaster.cs`

**Problem:**  
`NodeOpCompletedEvent` and `NodeOpStatus` lack the operation type ID, forcing `NodeOpMasterTranslator.DeserializeResultPayload()` to blindly return a raw JSON string. This causes `ClusterMaster.ConsumeNodeOpStatuses()` to directly invoke `JsonSerializer.Deserialize<List<FileManifestEntry>>(ev.ResultPayload as string)` — a violation of SRP.

**Deliverables:**

1. Add `NodeOpType Operation` field to `NodeOpCompletedEvent` struct (after `TransactionId`).  
2. Add `NodeOpType Operation` field to the DDS `NodeOpStatus` struct in `Hrot.NED/Orchestration/OrchestrationMessages.cs`.  
3. Update `NodeOpSlaveTranslator` to populate `Operation` in the `NodeOpStatus` it writes.  
4. Update `NodeOpMasterTranslator.Tick()` to copy `status.Operation` into the published `NodeOpCompletedEvent`.  
5. Refactor `NodeOpMasterTranslator.DeserializeResultPayload()`:  
   - Accept `NodeOpType operation` parameter.  
   - For `NodeOpType.SerializeLocal`: deserialize to `FileManifestResult[]` and return directly.  
   - For all others: `return null` (or a typed result if needed later).  
6. Update `ClusterMaster.ConsumeNodeOpStatuses()`:  
   - Replace `JsonSerializer.Deserialize<List<FileManifestEntry>>(ev.ResultPayload as string)` with a direct cast/access: `ev.ResultPayload as FileManifestResult[]` (or equivalent).  
   - Remove the import of `System.Text.Json` from ClusterMaster if it is no longer used after this change.

**Tests Required:**
- Unit test verifying `NodeOpMasterTranslator` sets `Operation` on the published `NodeOpCompletedEvent` from `NodeOpStatus.Operation`.
- Unit test verifying `DeserializeResultPayload()` correctly returns `FileManifestResult[]` for `SerializeLocal`.
- Update round-trip integration test if affected.

---

## TASK-D03 — ClusterStateTransitionedEvent.NewStateId → ClusterState Enum

**Phase:** 1  
**Touches:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterCqrsEvents.cs`, `Hrot.Orchestrator/ClusterMaster.cs`

**Problem:**  
`ClusterStateTransitionedEvent.NewStateId` is `int`, which forces consumers to blind-cast to `ClusterState`. This is Primitive Obsession and defeats the purpose of having a strongly-typed domain enum.

**Deliverables:**

1. Change `NewStateId` field type from `int` to `ClusterState` in `ClusterCqrsEvents.cs`.  
2. In `ClusterMaster.PublishClusterState()`, remove the `(int)state` cast.  
3. Verify no other consumers exist (currently only the publisher exists; any translator consuming this event must also be updated).

**Tests Required:**
- Verify existing CQRS struct test in `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/FdpOrchestrationCqrsStructTests.cs` compiles and passes.
- Add a test that constructs `ClusterStateTransitionedEvent { NewStateId = ClusterState.Live }` and verifies the field type is `ClusterState` (not int).

---

## TASK-D04 — Remove Handler const int OperationId Constants

**Phase:** 1  
**Touches:** All `IClusterStateHandler` implementations in `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/` and `Hrot.IG/Modules/Orchestration/`

**Problem:**  
Handlers expose redundant `public const int *OperationId` fields that manually duplicate the `NodeOpType` enum underlying values. These are fragile SSoT violations: reordering enum values would silently break routing.

**Handlers to clean:**
- `ReferenceEpisodeLoadHandler` (StartEpisodeOperationId=20, StopEpisodeOperationId=21)
- `ReferenceArchiveHandler` (SerializeLocalOperationId=15)
- `ReferenceCheckpointHandler` (TakeSnapshotOperationId=4)
- `ReferenceEditLoadHandler` (PrepareStateOperationId=1)
- `ReferenceLiveLoadHandler` (PrepareLiveOperationId=9, FinalizeLiveOperationId=10)
- `ReferencePrefetchHandler` (PrefetchFilesOperationId=25)
- `ReferencePreviewHandler` (PrepareStateOperationId=1)
- `ReferenceReplayLoadHandler` (PrepareReplayOperationId=11, FinalizeReplayOperationId=12, PrepareLiveOperationId=9)
- `ReferenceScenarioLoadHandler` (PrepareLiveOperationId=9)
- `IgZoneDummyHandler` in `Hrot.IG/Modules/Orchestration/` (PrepareZoneOperationId, CommitZoneOperationId)

**Deliverables:**

1. Delete all `public const int *OperationId` fields from the above handlers.  
2. Verify `CanHandle()` methods already use `NodeOpType` enum directly (no integer comparisons remain).  
3. Update any consumers (unit tests, reflection utilities) that reference these constants to use the enum directly.

**Tests Required:**
- Compile-check: all handler unit tests pass without the removed constants.
- If any existing test uses e.g. `ReferenceEpisodeLoadHandler.StartEpisodeOperationId`, update it to `(int)NodeOpType.StartEpisode`.

---

## TASK-D05 — OrchestrationStatusCode → Enum

**Phase:** 1  
**Touches:** `FDP/Toolkits/FDP.Toolkit.Orchestration/OrchestrationStatusCode.cs`, `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterCqrsEvents.cs`, `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterOpIntents.cs`, and all call sites.

**Problem:**  
`OrchestrationStatusCode` is a static class with `const int` fields. Debuggers display cryptic integer values. StatusCode fields in events are typed as `int`. This is Primitive Obsession.

**Deliverables:**

1. Convert `OrchestrationStatusCode` from `static class` to `enum OrchestrationStatusCode : int` (keep all numerical values and range comments).  
2. Convert the `IsError(int code)` static method to an extension method `IsError(this OrchestrationStatusCode code)` in a `static class OrchestrationStatusCodeExtensions`.  
3. Change `ClusterOpCompletedEvent.StatusCode` from `int` to `OrchestrationStatusCode`.  
4. Change `NodeOpCompletedEvent.StatusCode` from `int` to `OrchestrationStatusCode`.  
5. Change `StorageOpCompletedEvent.StatusCode` from `int` to `OrchestrationStatusCode`.  
6. Update `NodeOpStatus` DDS struct (`Hrot.NED/Orchestration/OrchestrationMessages.cs`): keep `int StatusCode` in the DDS struct (DDS is a wire format; enums in DDS structs should map to their `int` underlying value). The translator must cast between `int` (DDS wire) and `OrchestrationStatusCode` (domain event).  
7. Update `NodeOpMasterTranslator.Tick()` to cast `status.StatusCode` (int from DDS) to `OrchestrationStatusCode` when publishing `NodeOpCompletedEvent`.  
8. Update `NodeOpSlaveTranslator.Tick()` to cast `ev.StatusCode` (OrchestrationStatusCode from domain) to `int` when writing `NodeOpStatus`.  
9. Update all usages: `OrchestrationStatusCode.IsError(x)` → `x.IsError()`, `OrchestrationStatusCode.Failure` remains valid as enum member.  
10. Update `ClusterMaster.cs` — `PublishOpStatus(...)` method signature — if it takes `int`, change to `OrchestrationStatusCode`.  
11. Update tests: anywhere `StatusCode = 0` is used inline, replace with `StatusCode = OrchestrationStatusCode.Success`.

**Tests Required:**
- Verify `OrchestrationStatusCode.Success.IsError()` returns `false`.  
- Verify `OrchestrationStatusCode.Failure.IsError()` returns `true`.  
- Verify the DDS translator converts `int 0` from DDS to `OrchestrationStatusCode.Success` correctly.
- All existing struct layout tests pass.

---

## TASK-D06 — Bootstrap Latch Case-Insensitive Fix

**Phase:** 1  
**Touches:** `Hrot.Orchestrator/ClusterMaster.cs`

**Problem:**  
`CheckBootstrapLatch()` uses `kv.Value.SubsystemName == name` (ordinal case-sensitive). If configuration declares `"simhost"` but the subsystem announces `"SimHost"`, the latch never releases, causing the cluster to stay stuck in "not bootstrapped" permanently.

**Deliverables:**

1. Change `kv.Value.SubsystemName == name` to `string.Equals(kv.Value.SubsystemName, name, StringComparison.OrdinalIgnoreCase)` in `CheckBootstrapLatch()`.

**Tests Required:**
- Unit test in `Hrot.Orchestrator.Tests/` verifying that bootstrap latch releases when subsystem name differs only in casing (e.g., config says `"simhost"`, heartbeat says `"SimHost"`).  
- Unit test verifying latch does NOT release when name is completely different.

---

## TASK-D07 — ScenarioSerializer: Strip Hrot-Specific Knowledge

**Phase:** 3  
**Touches:** `FDP/Toolkits/FDP.Toolkit.Scenario/ScenarioSerializer.cs`, `FDP/Toolkits/FDP.Toolkit.Scenario/ScenarioHeader.cs`, `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferenceScenarioLoadHandler.cs`, `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferenceEditLoadHandler.cs`, `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferenceEpisodeLoadHandler.cs`, new files in `Hrot.Common/`

**Problem:**  
`ScenarioSerializer` (in `FDP.Toolkit.Scenario`) contains `PeekSubsystemType()` and `IsMatchingSubsystem()` which parse a Hrot-specific file envelope (`Header.SubsystemType`). This leaks application-layer format knowledge into the pure ECS engine toolkit. The handlers that call these methods belong at the Hrot layer, not the FDP toolkit.

**Deliverables:**

1. **Remove from `ScenarioSerializer`:** Delete `PeekSubsystemType()` and `IsMatchingSubsystem()` methods.  
2. **Remove `ScenarioHeader.cs`** from `FDP.Toolkit.Scenario` (delete the file). The `Serialize()` method signature currently takes `ScenarioHeader`; this parameter will move to the Hrot layer.  
3. **Simplify `ScenarioSerializer.Serialize()`:** Change signature from `Serialize(EntityRepository repo, ScenarioHeader header)` to `Serialize(EntityRepository repo)` — returns only the `Entities` `JsonObject` (no `Header`+`Entities` wrapper).  
4. **Simplify `ScenarioSerializer.Deserialize()`:** Change the `Deserialize(EntityRepository, JsonObject, ...)` overload to expect the raw `Entities` node (not the full `{ Header, Entities }` root). Remove the `dom["Header"]?["SubsystemType"]` check from the `JsonObject` overload. Keep the string-overload but update it to parse only the entities JSON.  
5. **Create `Hrot.Common/Scenario/HrotScenarioEnvelope.cs`:**  
   ```csharp
   namespace Hrot.Common.Scenario;
   public static class HrotScenarioEnvelope
   {
       public static string? PeekSubsystemType(string jsonText) { ... }
       public static bool IsMatchingSubsystem(string? subsystemType, string expected) { ... }
       public static JsonObject WrapEntities(JsonObject entities, string subsystemType, int schemaVersion = 1) { ... }
       public static JsonObject? UnwrapEntities(JsonObject dom) { ... }
   }
   ```
6. **Migrate handlers** to use `HrotScenarioEnvelope`: Update `ReferenceScenarioLoadHandler`, `ReferenceEditLoadHandler`, `ReferenceEpisodeLoadHandler` to call `HrotScenarioEnvelope.PeekSubsystemType()` / `IsMatchingSubsystem()` instead of `_serializer.*`. Also update their call to `_serializer.Deserialize()` to first unwrap the entities node via `HrotScenarioEnvelope.UnwrapEntities()`.  
7. **Update handler callers** (composition roots `Hrot.SimHost`, `Hrot.IG`, `AllInOne`) — the `Serialize()` call now needs to wrap the returned entities: `HrotScenarioEnvelope.WrapEntities(entities, subsysType)`.  
8. **Update all FDP-layer tests** that use `ScenarioHeader` and the old `Serialize` signature.

**Tests Required:**
- Unit tests for `HrotScenarioEnvelope`: peek, match, wrap, unwrap.
- Integration test: `WrapEntities(Serialize(repo)) → UnwrapEntities → Deserialize(repo)` round-trip produces identical entities.
- Existing scenario serializer tests updated to use the new signature.
