# Two-ACK Entity Lifecycle Pattern — Task Details

**Design Reference:** [TWOACK-DESIGN.md](./TWOACK-DESIGN.md)  
**Tracker:** [TWOACK-TASK-TRACKER.md](./TWOACK-TASK-TRACKER.md)

All tasks are scoped to the `Bagira.DDS.DataModel`, `Bagira.SimHost`, `Bagira.IOS`, and `Bagira.Runner` projects.  
FDP projects are **read-only** — they are never modified.

---

## Phase 1: Data Model Unification

**Goal:** Establish the shared DDS contract used by all other phases. No application logic changes yet.

---

### TWOACK-DM001 — Add `DeleteEntityRequest` to DataModel

**Design ref:** [§3.1](./TWOACK-DESIGN.md#31-new-deleteentityrequest-struct)

**Scope:** `Bagira.DDS.DataModel/GenericMessages.cs`

**Description:**  
Add the `DeleteEntityRequest` partial struct to `GenericMessages.cs`, following the same structural pattern as `UpdateEntityDescriptorRequest`. Place it immediately after the `CreateEntityRequest` struct definition.

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

**Success conditions:**
- The struct exists in `GenericMessages.cs` with the exact `DdsTopic("DeleteEntityRequest")` attribute and `DdsQos` matching the volatile/reliable/keep-all pattern used on other request messages.
- `Bagira.DDS.DataModel.Tests` project compiles without error.
- A unit test `DeleteEntityRequest_HasRequiredFields` exists in `Bagira.DDS.DataModel.Tests` asserting that `typeof(DeleteEntityRequest)` has public fields `RequestId` (type `Guid`) and `EntityId` (type `int`), and carries `[DdsTopic("DeleteEntityRequest")]`.

---

### TWOACK-DM002 — Rename `SstErrorCode` to `SstStatusCode`

**Design ref:** [§3.2](./TWOACK-DESIGN.md#32-new-sststatuscode-enum-replaces-ssterrorcode)

**Scope:** `Bagira.DDS.DataModel/GenericMessages.cs`, all call sites in `Bagira.SimHost`, `Bagira.IOS`, `Bagira.Runner`, `Bagira.DDS.DataModel.Tests`

**Description:**  
1. In `GenericMessages.cs`, rename `SstErrorCode` to `SstStatusCode`.
2. Insert `InProgress = 1` between `Success = 0` and the first error entry.
3. Shift all existing error values up by 1: `UnknownDescriptorType` becomes `2`, `EntityNotFound` becomes `3`, etc., through `VersionConflict = 8`.
4. Find and update every reference to `SstErrorCode` across the solution (enum name at declaration sites and all usages). There must be no remaining references to `SstErrorCode`.
5. Update any switch statements or integer comparisons that relied on the old numeric values.

**Success conditions:**
- Solution compiles with zero errors.
- `SstErrorCode` does not appear anywhere in the solution.
- `SstStatusCode.Success == 0`, `SstStatusCode.InProgress == 1`, `SstStatusCode.VersionConflict == 8` (verified by a unit test `SstStatusCode_ValuesAreCorrect` in `Bagira.DDS.DataModel.Tests`).
- All pre-existing tests that referenced `SstErrorCode` pass after updating their enum references.

---

### TWOACK-DM003 — Expand `CreateUpdateDeleteEntityAck` and Retire `CreateEntityAck`

**Design ref:** [§3.3](./TWOACK-DESIGN.md#33-expanded-createupdatedeleteentityack-replaces-createentityack)

**Scope:** `Bagira.DDS.DataModel/GenericMessages.cs`, plus all consumers listed below

**Description:**  
1. In `GenericMessages.cs`:
   - Add `int EntityId` field to `CreateUpdateDeleteEntityAck`.
   - Rename field `ErrorCode` to `StatusCode` in `CreateUpdateDeleteEntityAck`.
   - Remove (or mark `[Obsolete]` and then remove in the same PR) the `CreateEntityAck` struct.
2. Update all consumers that constructed or read `CreateEntityAck` to use `CreateUpdateDeleteEntityAck` instead:
   - `Bagira.SimHost` — `CreateEntityRequestSystem`: replace `ICreateEntityAckSink` usage.
   - `Bagira.Runner` — `IosSubsystem.cs`: replace `CreateEntityAckIngressHandler` and `ConcurrentEventQueue<CreateEntityAck>`.
   - `Bagira.IOS` — `DdsEventIngressHandlers.cs`: update handler class if it explicitly references `CreateEntityAck`.
   - Any test that constructs a `CreateEntityAck` must be updated.
3. Ensure `RespondingNode` and `OpaqueData` fields remain present in `CreateUpdateDeleteEntityAck`.

**Success conditions:**
- `CreateEntityAck` struct no longer exists in the codebase.
- `CreateUpdateDeleteEntityAck` has fields: `RequestId (Guid)`, `EntityId (int)`, `StatusCode (int)`, `RespondingNode (NodeId)`, `OpaqueData (byte[])`.
- A test `CreateUpdateDeleteEntityAck_HasAllRequiredFields` asserts the above.
- Solution compiles with zero errors and all existing tests pass.

---

## Phase 2: SimHost Two-ACK Pipeline

**Goal:** Implement the full two-phase ACK state machine on the SimHost side without touching FDP.

---

### TWOACK-SH001 — Create `SstRequestFinalizationSystem`

**Design ref:** [§4.1](./TWOACK-DESIGN.md#41-sstrequestfinalizationsystem-new)

**Scope:** New file `Bagira.SimHost/Systems/SstRequestFinalizationSystem.cs`

**Description:**  
Create a new `IEcsModuleSystem` that:
1. Holds an internal `Dictionary<long, PendingRequest> _tracked` where `PendingRequest` carries `(Guid RequestId, RequestKind Kind)` and `RequestKind` is a private enum `{ Create, Delete }`.
2. Exposes `void Track(long networkId, Guid requestId, RequestKind kind)` — called by `CreateEntityRequestSystem` and `DeleteEntityRequestSystem`.
3. In `Execute(ISimulationView view, float deltaTime)`:
   - Iterates a copy/snapshot of `_tracked`.
   - For each tracked entry, resolves the ECS `Entity` by network ID using the view.
   - For `Create`: checks `IsAlive` and `LifecycleState`. Sends `StatusCode=0` on `Active`, sends error code on dead entity.
   - For `Delete`: checks `IsAlive`. Sends `StatusCode=0` when entity is dead.
   - Removes resolved entries from `_tracked`.
4. Receives an `ICreateUpdateDeleteEntityAckSink` or equivalent DDS writer via constructor injection.

**Success conditions:**
- `SstRequestFinalizationSystem` compiles and its `Track(...)` method is callable.
- A unit test `SstRequestFinalizationSystem_SendsSuccessAck_WhenEntityBecomesActive` exists in `Bagira.SimHost.Tests`:
  - Creates a fake/mock ECS view where an entity starts in `Constructing` and transitions to `Active` on the second call.
  - Verifies that no ACK is sent on the first `Execute()`, and the success ACK (`StatusCode=0`) is sent on the second `Execute()`.
- A unit test `SstRequestFinalizationSystem_SendsFailureAck_WhenEntityDies` verifies the failure path (entity dies before becoming `Active`).
- A unit test `SstRequestFinalizationSystem_SendsSuccessAck_WhenDeletedEntityDies` verifies the deletion success path.

---

### TWOACK-SH002 — Update `CreateEntityRequestSystem` for Two-ACK

**Design ref:** [§4.2](./TWOACK-DESIGN.md#42-updated-createentityrequestsystem)

**Scope:** `Bagira.SimHost/Systems/CreateEntityRequestSystem.cs`

**Description:**  
1. Replace the injected `ICreateEntityAckSink` with `ICreateUpdateDeleteEntityAckSink` (or the appropriate DDS writer type).
2. In `ProcessIncomingRequest(CreateEntityRequest request)`:
   - After allocating the network ID, send `CreateUpdateDeleteEntityAck { RequestId = request.RequestId, EntityId = (int)networkId, StatusCode = (int)SstStatusCode.InProgress }`.
   - Call `_finalizationSystem.Track(networkId, request.RequestId, RequestKind.Create)`.
3. Remove any remaining `CreateEntityAck` sends from this class.
4. Inject `SstRequestFinalizationSystem` via constructor parameter.

**Success conditions:**
- Unit test `CreateEntityRequestSystem_SendsInProgressAck_OnValidRequest` in `Bagira.SimHost.Tests`:
  - Submits a valid `CreateEntityRequest`.
  - Asserts the mock ACK sink received exactly one `CreateUpdateDeleteEntityAck` with `StatusCode == 1` and a non-zero `EntityId`.
  - Asserts the finalization system received a `Track(...)` call with `RequestKind.Create`.
- Unit test `CreateEntityRequestSystem_DoesNotSendFinalAck_Immediately` asserts no second ACK is sent by this system alone.
- All pre-existing `CreateEntityRequestSystem` tests continue to pass after updating mock types.

---

### TWOACK-SH003 — Create `DeleteEntityRequestSystem`

**Design ref:** [§4.3](./TWOACK-DESIGN.md#43-new-deleteentityrequestsystem)

**Scope:** New file `Bagira.SimHost/Systems/DeleteEntityRequestSystem.cs`

**Description:**  
Create a new `IEcsModuleSystem` that:
1. Consumes `DeleteEntityRequest` messages from a `IDeleteEntityRequestSource` (DDS ingress).
2. For each request:
   a. Validates that the entity with `request.EntityId` exists in the world. If not, sends `CreateUpdateDeleteEntityAck { RequestId, EntityId, StatusCode = SstStatusCode.EntityNotFound }` and skips.
   b. Sends **Phase 1 ACK**: `CreateUpdateDeleteEntityAck { RequestId, EntityId, StatusCode = SstStatusCode.InProgress }`.
   c. Calls `_finalizationSystem.Track(networkId, request.RequestId, RequestKind.Delete)`.
   d. Publishes `DestroyEntityCommand` to the ECS event bus for FDP's ELM to handle.
3. Register this system in `SimHostModule` alongside `CreateEntityRequestSystem`.

**Success conditions:**
- Unit test `DeleteEntityRequestSystem_SendsInProgressAck_OnValidRequest`:
  - Entity exists in mock world.
  - Verify Phase 1 ACK with `StatusCode=1` is sent and `Track(...)` is called.
  - Verify `DestroyEntityCommand` is published.
- Unit test `DeleteEntityRequestSystem_SendsEntityNotFoundAck_WhenEntityMissing`:
  - Entity does not exist.
  - Verify `StatusCode = SstStatusCode.EntityNotFound` ACK is sent and no tracking or destroy command is issued.

---

## Phase 3: IOS Client Adaptation

**Goal:** Make the IOS correctly interpret two-phase ACKs, lock the UI during construction, and surface errors explicitly.

---

### TWOACK-IOS001 — Update IOS Ingress Pipeline

**Design ref:** [§5.1](./TWOACK-DESIGN.md#51-updated-ingress-pipeline-bagirarunnservicessiossubsystemcs)

**Scope:** `Bagira.Runner/Services/IosSubsystem.cs`, `Bagira.IOS/Services/DdsEventIngressHandlers.cs`

**Description:**  
1. In `IosSubsystem.Initialize()`:
   - Remove the `ConcurrentEventQueue<CreateEntityAck>` and its associated `CreateEntityAckIngressHandler`.
   - Add a `ConcurrentEventQueue<CreateUpdateDeleteEntityAck>` and create a corresponding ingress handler that reads from the `CreateUpdateDeleteEntityAck` DDS topic.
   - Pass the new queue to `IosLogic`'s constructor (replacing the old `createEntityAckQueue` parameter).
2. In `DdsEventIngressHandlers.cs` (or a new file): implement (or update) the handler class for `CreateUpdateDeleteEntityAck`. Follow the same pattern as the existing `CreateEntityAckIngressHandler`.

**Success conditions:**
- `IosSubsystem` compiles with no reference to `CreateEntityAck`.
- Integration test `IosSubsystem_ReceivesCreateUpdateDeleteEntityAck_AndForwardsToLogic` (or equivalent in `Bagira.IG.Tests`/`Bagira.Runner.Integration.Tests`) verifies that a published `CreateUpdateDeleteEntityAck` DDS sample reaches `IosLogic`.
- All pre-existing `IosSubsystem` tests pass after mock type updates.

---

### TWOACK-IOS002 — Rewrite `ProcessEntityCreationAcks` for Two-ACK State Machine

**Design ref:** [§5.2](./TWOACK-DESIGN.md#52-two-ack-state-machine-in-ioslogic)

**Scope:** `Bagira.IOS/IosLogic.cs`, `Bagira.IOS/Abstractions/IIosLogic.cs`

**Description:**  
1. Add `private readonly HashSet<int> _pendingEntities = new()` to `IosLogic`.
2. Replace the `IEventQueue<CreateEntityAck>?` constructor parameter with `IEventQueue<CreateUpdateDeleteEntityAck>?`.
3. Rewrite `ProcessEntityCreationAcks()` to handle three status codes:
   - `StatusCode == 1` (InProgress): `_pendingEntities.Add(ack.EntityId)`. `SelectEntity(ack.EntityId)`. Do **not** call `TransactionManager.CompleteRequest`.
   - `StatusCode == 0` (Success): `_pendingEntities.Remove(ack.EntityId)`. `TransactionManager.CompleteRequest(ack.RequestId, true)`.
   - `StatusCode >= 2` (Failure): `_pendingEntities.Remove(ack.EntityId)`. `TransactionManager.CompleteRequest(ack.RequestId, false, $"Error {ack.StatusCode}")`. Set global alert (see TWOACK-IOS004).
4. Extend `IIosLogic` with `bool IsEntityPending(int entityId)`. Implement it as `_pendingEntities.Contains(entityId)`.

**Success conditions:**
- Unit test `IosLogic_AddsEntityToPending_OnInProgressAck`:
  - Feed in an InProgress ACK.
  - Assert `logic.IsEntityPending(entityId) == true`.
  - Assert `TransactionManager.CompleteRequest` was NOT called.
- Unit test `IosLogic_RemovesEntityFromPending_OnSuccessAck`:
  - Feed InProgress, then Success with same `RequestId`.
  - Assert `IsEntityPending` returns `false`.
  - Assert `TransactionManager.CompleteRequest(requestId, true)` was called exactly once.
- Unit test `IosLogic_RemovesEntityFromPending_OnFailureAck`:
  - Feed InProgress, then failure (StatusCode=3).
  - Assert `IsEntityPending` returns `false`.
  - Assert `TransactionManager.CompleteRequest(requestId, false, ...)` was called.
- All existing `IosLogic` tests continue to pass.

---

### TWOACK-IOS003 — Lock UI for Pending Entities

**Design ref:** [§5.3](./TWOACK-DESIGN.md#53-ui-locking-for-pending-entities)

**Scope:** `Bagira.IOS/Panels/MissionPanel.cs`, `Bagira.IOS/Logic/ContextMenuLogic.cs`

**Description:**  
**MissionPanel:**  
In `MissionPanel.Draw(IIosLogic logic, ...)` (or wherever the task assignment and commit buttons are rendered), add a guard before rendering interactive controls:
```csharp
bool isPending = logic.IsEntityPending(logic.SelectedEntityId);
if (isPending)
{
    ImGui.TextColored(new Vector4(1f, 0.9f, 0f, 1f), "[Constructing across network...]");
    ImGui.BeginDisabled();
}
// ... existing task assignment and commit button code ...
if (isPending) ImGui.EndDisabled();
```

**ContextMenuLogic:**  
In the method that builds or returns the context menu items for the selected entity: if `logic.IsEntityPending(selectedEntityId)` is `true`, return an empty context menu (empty `List<ContextMenuItem>`) or set all items' `Enabled` flag to `false`.

**Success conditions:**
- Unit test `MissionPanel_DisablesControls_WhenEntityIsPending`:
  - Construct a `MissionPanel` with a mock `IIosLogic` that returns `IsEntityPending(id) = true`.
  - Call `Draw(...)`.
  - Verify that `ImGui.BeginDisabled()` was called (via ImGui test renderer or mock).
  - Verify the yellow label text is emitted.
- Unit test `ContextMenuLogic_ReturnsEmptyMenu_WhenEntityIsPending`:
  - Fire a `SelectionChangedEvent` for an entity that `IsEntityPending` returns `true`.
  - Assert no `ContextMenuItem` entries are present in the generated menu.
- All existing `MissionPanel` and `ContextMenuLogic` tests pass.

---

### TWOACK-IOS004 — Surface Explicit Creation Errors to Operator

**Design ref:** [§5.4](./TWOACK-DESIGN.md#54-explicit-error-surface)

**Scope:** `Bagira.IOS/IosLogic.cs`, `Bagira.IOS/UI/IosMock.cs` (or equivalent drawing class)

**Description:**  
1. Add `string? _globalAlert` field to `IosLogic` and a corresponding `string? GlobalAlert { get; }` property on `IIosLogic`.
2. When a failure ACK is processed in `ProcessEntityCreationAcks()`, set `_globalAlert = $"Failed to spawn entity: {(SstStatusCode)ack.StatusCode}"`.
3. In `IosMock.DrawUI()` (or the top-level ImGui draw method), if `Logic.GlobalAlert != null`:
   - Open an ImGui modal popup: `ImGui.OpenPopup("Entity Error")`.
   - Inside the popup, display `Logic.GlobalAlert`.
   - Provide an `[OK]` button that calls `logic.DismissAlert()` (a new method on the interface).
4. `DismissAlert()` sets `_globalAlert = null`.

**Success conditions:**
- Unit test `IosLogic_SetsGlobalAlert_OnFailureAck`:
  - Feed a failure ACK (StatusCode=3).
  - Assert `logic.GlobalAlert != null`.
  - Assert `GlobalAlert` contains the error description.
- Unit test `IosLogic_ClearsGlobalAlert_OnDismiss`:
  - Set up alert, call `DismissAlert()`.
  - Assert `GlobalAlert == null`.
- Unit test `IosLogic_NoAlertOnSuccessAck` — feeding a success ACK does not set `GlobalAlert`.
- All existing `IosLogic` tests pass.
