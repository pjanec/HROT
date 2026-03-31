# Two-ACK Entity Lifecycle Pattern — Design Document

**Reference:** See [TWOACK-TASK-DETAIL.md](./TWOACK-TASK-DETAIL.md) for per-task specifications  
**Tracker:** [TWOACK-TASK-TRACKER.md](./TWOACK-TASK-TRACKER.md)

---

## 1. Problem Statement

The current entity creation/deletion pipeline suffers from a "half-baked entity" UX problem. When the IOS issues a `CreateEntityRequest`, it receives a single `CreateEntityAck` the moment the SimHost allocates a network ID. However, the real work — a distributed multi-node handshake managed by FDP's `EntityLifecycleModule` (ELM) — has not yet completed. From that point on, the IOS has no reliable way to know whether the creation succeeded, failed (e.g., due to an ELM peer timeout), or is still in progress.

**Consequences of the current design:**
- If the distributed handshake fails, the entity silently disappears from the simulation. The operator sees a "ghost" unit in the ORBAT tree that suddenly vanishes with no explanation.
- The IOS cannot prevent an operator from assigning missions or commands to an entity that is still being initialised across the network — leading to commands sent to an invalid simulation state.
- There is no `DeleteEntityRequest` at all: non-owning nodes cannot request entity deletion via a well-defined API contract.

---

## 2. Solution: Two-Phase Acknowledgment Pattern

The solution is modelled after the existing `MapCommandAck` pattern that is already proven in the map authoring tools. It splits every entity lifecycle request into two explicit, ordered acknowledgments:

| Phase | StatusCode | Meaning |
|-------|-----------|---------|
| Phase 1 — Immediate | `1` (`InProgress`) | SimHost has validated the request and started the distributed handshake. The affected EntityId is known. |
| Phase 2 — Terminal | `0` (`Success`) | All peer nodes confirmed. Entity is fully live. |
| Phase 2 — Terminal | `>= 2` (Error) | The distributed handshake failed (timeout, validation error, etc.). |

This directly maps to the `SstStatusCode` enum (see §3.2) and is carried by the unified `CreateUpdateDeleteEntityAck` message (see §3.3).

### 2.1 Creation Flow (End-to-End)

```
IOS                          SimHost                        FDP ELM
 │                              │                              │
 │── CreateEntityRequest ──────>│                              │
 │                              │ allocate NetworkId           │
 │<── CUDEntityAck(InProgress) ─│ StatusCode=1, EntityId       │
 │   [entity shown as pending]  │                              │
 │                              │── SpawnEntityCommand ──────> │
 │                              │                              │ Constructing
 │                              │                              │ ... peer ACKs ...
 │                              │    lifecycle → Active        │
 │                              │<── (observed by SstRequestFinalizationSystem)
 │<── CUDEntityAck(Success) ────│ StatusCode=0
 │   [entity fully interactive] │
```

If ELM times out waiting for peers, `world.IsAlive(entity)` will return `false`. The `SstRequestFinalizationSystem` detects this and sends:
```
 │<── CUDEntityAck(Failure) ────│ StatusCode=2 (EntityNotFound or timeout error)
 │   [entity removed, alert shown]
```

### 2.2 Deletion Flow (End-to-End)

```
IOS                          SimHost                        FDP ELM
 │                              │                              │
 │── DeleteEntityRequest ──────>│                              │
 │                              │ validate + initiate teardown │
 │<── CUDEntityAck(InProgress)──│ StatusCode=1                │
 │                              │── DestroyEntityCommand ────> │
 │                              │                              │ TearDown
 │                              │                              │ ... peer ACKs ...
 │                              │   entity destroyed           │
 │                              │<── (IsAlive → false, observed by SstRequestFinalizationSystem)
 │<── CUDEntityAck(Success) ────│ StatusCode=0
```

---

## 3. Phase 1: Data Model Unification (`Hrot.NED`)

This phase establishes the shared network contract that all other phases depend on.

### 3.1 New `DeleteEntityRequest` Struct

Add to `Hrot.NED/GenericMessages.cs`. Mirrors the pattern of `UpdateEntityDescriptorRequest`.

```csharp
[DdsTopic("DeleteEntityRequest")]
[DdsIdlFile("bdc-sst-generic-msgs")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
public partial struct DeleteEntityRequest
{
    public Guid RequestId;
    public int EntityId;
}
```

### 3.2 New `SstStatusCode` Enum (replaces `SstErrorCode`)

Rename `SstErrorCode` to `SstStatusCode`. Insert `InProgress = 1` as a healthy intermediate state. Shift all existing error codes up by 1 to accommodate it (errors now start at `2`). This mirrors `MapCommandAck.StatusCode` semantics exactly.

```csharp
public enum SstStatusCode : int
{
    Success                  = 0,
    InProgress               = 1,   // NEW — healthy intermediate result
    UnknownDescriptorType    = 2,   // was 1
    EntityNotFound           = 3,   // was 2
    DescriptorInstanceNotFound = 4, // was 3
    NotOwner                 = 5,   // was 4
    ValidationFailed         = 6,   // was 5
    NotSupported             = 7,   // was 6
    VersionConflict          = 8,   // was 7
}
```

> **Migration note:** All existing usage of `SstErrorCode` must be updated to `SstStatusCode`, and any integer comparisons against the old values (1–7) must be updated to the new values (2–8).

### 3.3 Expanded `CreateUpdateDeleteEntityAck` (replaces `CreateEntityAck`)

The existing `CreateUpdateDeleteEntityAck` is missing:
- `EntityId` (needed to carry the newly allocated ID from creation requests, or echo the ID for deletions)
- `StatusCode` with the two-phase semantics (currently named `ErrorCode` with the old `SstErrorCode` semantics)

The standalone `CreateEntityAck` is removed and its information consolidated here.

```csharp
[DdsTopic("CreateUpdateDeleteEntityAck")]
[DdsIdlFile("bdc-sst-generic-msgs")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
public partial struct CreateUpdateDeleteEntityAck
{
    public Guid RequestId;

    /// <summary>The affected entity. For creation requests this holds the newly allocated ID.</summary>
    public int EntityId;

    /// <summary>Maps to SstStatusCode: 0=Success, 1=InProgress, >=2=Error.</summary>
    public int StatusCode;

    /// <summary>Identifies which SimHost node is replying.</summary>
    public NodeId RespondingNode;

    /// <summary>Optional 32-byte engine-specific execution receipt.</summary>
    public byte[] OpaqueData;
}
```

The old topic `CreateEntityAck` is retired. The old `ErrorCode` field is removed.

---

## 4. Phase 2: SimHost Two-ACK Pipeline (`Hrot.SimHost`)

The SimHost implementation avoids any changes to FDP. All two-ACK state machine logic lives in the `Hrot.SimHost` application layer, which simply observes FDP's `EntityLifecycle` state transitions.

### 4.1 `SstRequestFinalizationSystem` (new)

A new `IEcsModuleSystem` running in the `PostSimulation` phase. It maintains a dictionary that maps `long networkId → (Guid requestId, RequestKind kind)` for all in-flight requests. Every frame it queries the ECS world:

**For tracked creations:**
- If `!world.IsAlive(entity)` → ELM aborted. Send `CreateUpdateDeleteEntityAck` with `StatusCode = SstStatusCode.EntityNotFound` (or a dedicated timeout code). Remove from tracking.
- If `world.GetHeader(entity.Index).LifecycleState == EntityLifecycle.Active` → ELM succeeded. Send `CreateUpdateDeleteEntityAck` with `StatusCode = SstStatusCode.Success`. Remove from tracking.

**For tracked deletions:**
- While the entity is alive in `TearDown`, do nothing.
- The frame `!world.IsAlive(entity)` → deletion complete. Send `CreateUpdateDeleteEntityAck` with `StatusCode = SstStatusCode.Success`. Remove from tracking.

The system receives its ACK sink injected via `ICreateUpdateDeleteEntityAckSink` (or the equivalent DDS writer type used in SimHost).

### 4.2 Updated `CreateEntityRequestSystem`

Modify `ProcessIncomingRequest`:
1. Allocate the network ID (unchanged).
2. Send **Phase 1 ACK**: `CreateUpdateDeleteEntityAck { RequestId, EntityId = networkId, StatusCode = 1 }`.
3. Register `(networkId, requestId, RequestKind.Create)` into `SstRequestFinalizationSystem`.
4. Enqueue `PendingRequest` for the `SpawnEntityCommand` dispatch (unchanged).

Remove the existing `CreateEntityAck` send. The old `ICreateEntityAckSink` is replaced by `ICreateUpdateDeleteEntityAckSink` (or the DDS writer directly).

### 4.3 New `DeleteEntityRequestSystem`

A new `IEcsModuleSystem` that consumes `DeleteEntityRequest` messages from DDS. For each request:
1. Validate the entity exists and the requester has permission.
2. Send **Phase 1 ACK**: `CreateUpdateDeleteEntityAck { RequestId, EntityId, StatusCode = 1 }`.
3. Register `(networkId, requestId, RequestKind.Delete)` into `SstRequestFinalizationSystem`.
4. Publish `DestroyEntityCommand` to the ECS event bus. FDP's ELM handles the rest.

---

## 5. Phase 3: IOS Client Adaptation (`Hrot.ExCon` + `Hrot.ClusterRunner`)

### 5.1 Updated Ingress Pipeline (`Hrot.ClusterRunner/Services/IosSubsystem.cs`)

Replace `CreateEntityAckIngressHandler` and its `ConcurrentEventQueue<CreateEntityAck>` with a `ConcurrentEventQueue<CreateUpdateDeleteEntityAck>`. Pass the unified queue into `IosLogic`'s constructor. The `CreateEntityAckIngressHandler` class itself is updated (or replaced) to read from the new DDS topic `CreateUpdateDeleteEntityAck`.

### 5.2 Two-ACK State Machine in `IosLogic`

`IosLogic` gains a `HashSet<int> _pendingEntities` field. `ProcessEntityCreationAcks()` is rewritten to handle three states:

| StatusCode | Action |
|-----------|--------|
| `1` (InProgress) | Add `ack.EntityId` to `_pendingEntities`. Call `SelectEntity(ack.EntityId)` (show as pending in UI). Do **not** call `TransactionManager.CompleteRequest()` yet. |
| `0` (Success) | Remove entity from `_pendingEntities`. Call `TransactionManager.CompleteRequest(ack.RequestId, true)`. |
| `>= 2` (Error) | Remove entity from `_pendingEntities`. Call `TransactionManager.CompleteRequest(ack.RequestId, false, errorMsg)`. Set global UI alert. |

The `IIosLogic` interface is extended with `bool IsEntityPending(int entityId)`.

### 5.3 UI Locking for Pending Entities

**`MissionPanel.Draw()`:** Before rendering the task-assignment and commit buttons, check `logic.IsEntityPending(selectedEntityId)`. If `true`, wrap the interactive section in `ImGui.BeginDisabled()` / `ImGui.EndDisabled()` and display a yellow `"[Constructing across network...]"` label.

**`ContextMenuLogic.cs`:** In `OnSelectionChanged()` (or wherever menu items are built), if the selected entity is pending, return an empty menu or disable all `ContextMenuItem` entries so the user cannot right-click-delete or move the entity during initialisation.

### 5.4 Explicit Error Surface

When a failure ACK arrives (§5.2), the entity will have already been destroyed by SimHost. The IOS must not let this vanish silently. Adapt the existing `HasConflictAlert` / `ConflictMessage` pattern from `MissionPanel` into a global alert on `IosLogic` (or surfaced via `IosMock.DrawUI()`). Set the message to something like `"Failed to spawn entity: Network Timeout"` and require an explicit operator dismissal (ImGui modal or toast).

---

## 6. Architectural Invariants

- **FDP is not modified.** The ELM and `NetworkSpawningSystem` remain pure, generic ECS systems. All SST contract logic is in `Hrot.SimHost` and `Hrot.ExCon`.
- **The `DeleteEntityRequest` DDS topic is reliable + volatile + keep-all**, consistent with all other SST request messages.
- **`SstStatusCode` is the single source of truth** for all `CreateUpdateDeleteEntityAck.StatusCode` values. No magic integers in application code.
- **`CreateEntityAck` topic is retired.** All consumers must migrate to `CreateUpdateDeleteEntityAck`.

---

## 7. Key Files Reference

| File | Relevance |
|------|-----------|
| `Hrot.NED/GenericMessages.cs` | `DeleteEntityRequest`, `CreateUpdateDeleteEntityAck`, `SstStatusCode` |
| `Hrot.SimHost/Systems/CreateEntityRequestSystem.cs` | Phase 1 ACK + hand-off |
| `Hrot.SimHost/Systems/SstRequestFinalizationSystem.cs` | **New** — Phase 2 watcher |
| `Hrot.SimHost/Systems/DeleteEntityRequestSystem.cs` | **New** — deletion entry point |
| `Hrot.ClusterRunner/Services/IosSubsystem.cs` | Ingress handler wiring |
| `Hrot.ExCon/IosLogic.cs` | Two-ACK state machine, `_pendingEntities` |
| `Hrot.ExCon/Panels/MissionPanel.cs` | UI locking |
| `Hrot.ExCon/Logic/ContextMenuLogic.cs` | Context menu locking |
| `FDP/Toolkits/FDP.Toolkit.Lifecycle/EntityLifecycleModule.cs` | Read-only reference — not modified |
| `FDP/Kernel/Fdp.Kernel/EntityLifecycleState.cs` | `EntityLifecycle` enum — read-only reference |
