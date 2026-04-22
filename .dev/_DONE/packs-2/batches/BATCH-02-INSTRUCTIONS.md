# BATCH-02: Phase 1 — Decouple Map Tools from the Network Edge

**Batch Number:** BATCH-02  
**Tasks:** PACK2-D001, PACK2-D002, PACK2-D003, PACK2-D004, PACK2-D005  
**Phase:** Phase 1 — Decouple Map Tools from the Network Edge  
**Estimated Effort:** 12–14 hours  
**Priority:** HIGH — unblocks Phase 2 (ScenarioEditor extraction)  
**Dependencies:** None (PACK2-P001 complete; BATCH-01 committed)  

> ⚠️ **Tasks D001–D005 form a coupled set.** After D001–D004 remove DDS writes from the tools,
> the distributed IG will lose entity-creation network traffic until D005 installs the ACL egress
> translators. Complete ALL five tasks before running integration tests; do not stop at D004.

---

## 📋 Onboarding & Workflow

### Developer Instructions

Phase 1 strips CycloneDDS / NED DTO coupling from the IG map tools so they emit pure FDP domain
events (`SpawnEntityCommand`, `UpdateEntityCommand`, `DestroyEntityCommand`) and publishes on the
`FdpEventBus`. The distributed IG deployment is kept whole by creating three ACL egress
translators (task D005) that convert those bus events back to DDS messages.

### Required Reading (IN ORDER)

1. **Design document:** `.dev/packs-2/DESIGN.md` — read §Phase 1 in full (all five sub-sections 1.A–1.E).
2. **Task definitions:** `.dev/packs-2/TASK-DETAIL.md` — read sections PACK2-D001 through PACK2-D005 in full.
3. **Previous batch review:** `.dev/packs-2/reviews/BATCH-01-REVIEW.md`
4. **Key source files (read BEFORE writing any code):**
   - `Hrot.IG/Tools/CreationTool.cs` — understand current DDS constructor parameter.
   - `Hrot.IG/IgApplication.cs` — main wiring file; lines ~800–850 (DDS writer construction),
     ~910 (MapCommandController construction), ~1500–1540 (delete branching),
     ~2511–2560 (`SendGeoSpatialUpdate`), ~3520–3580 (EditTool/RouteEditTool commit callbacks).
   - `Hrot.IG/Systems/MapCommandController.cs` — understand the `IDdsWriter<CreateEntityRequest>` field.
   - `Hrot.IG/Systems/ContextMenuSystem.cs` — verify current state (no `_networkEnabled` flag inside the class itself; branching is in IgApplication).
   - `FDP/Toolkits/FDP.Toolkit.NetworkSpawning/Events/SpawnEntityCommand.cs` — know the command contract.
   - `Hrot.Map.Common/Replication/Egress/` — existing egress translator pattern to follow for D005.
   - `Hrot.Map.Common/Replication/Ingress/` — ingress translator pattern for context.

### Source Code Locations

| Area | Path |
|------|------|
| CreationTool | `Hrot.IG/Tools/CreationTool.cs` |
| EditTool | `Hrot.IG/Tools/EditTool.cs` |
| RouteEditTool | `Hrot.IG/Tools/RouteEditTool.cs` |
| ContextMenuSystem | `Hrot.IG/Systems/ContextMenuSystem.cs` |
| MapCommandController | `Hrot.IG/Systems/MapCommandController.cs` |
| IgApplication (wiring hub) | `Hrot.IG/IgApplication.cs` |
| New egress translators | `Hrot.Map.Common/Replication/Egress/` (NEW files) |
| FDP events | `FDP/Toolkits/FDP.Toolkit.NetworkSpawning/Events/` |
| IG tests | `Hrot.IG.Tests/` |
| Map.Common tests | `Hrot.Map.Common.Tests/` |
| Integration tests | `Hrot.ClusterRunner.Integration.Tests/` |

### Report Submission

**When complete, write your report to:**  
`.dev/packs-2/reports/BATCH-02-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**Complete tasks in sequence. Do NOT stop between tasks to ask questions. Fix everything until green, then move on.**

1. **D001:** Refactor `CreationTool` → Write/update unit tests → ALL green ✅  
2. **D002:** Update `IgApplication.cs` edit-commit subscribers → Run tests → ALL green ✅  
3. **D003:** Remove delete branching → Run tests → ALL green ✅  
4. **D004:** Remove `IDdsWriter<CreateEntityRequest>` from `MapCommandController` → Tests ✅  
5. **D005:** Create 3 egress translators + install in `IgApplication.cs` → Tests ✅  
6. **Full solution build + all test suites green**  
7. Write report.

---

## Context

`IgApplication.cs` is the composition root for the IG subsystem. Currently it wires DDS writers
directly into tool callbacks. After this batch:
- Tools emit **pure FDP bus events** (SpawnEntityCommand, UpdateEntityCommand, DestroyEntityCommand).
- `IgApplication.cs` installs **ACL egress translators** that intercept those events and forward
  to DDS (transparent to the tools).
- `MapCommandController` publishes `SpawnEntityCommand` on the bus instead of writing to DDS.

---

## ✅ Tasks

---

### Task 1 — `CreationTool`: Replace `Action<CreateEntityRequest>` with FdpEventBus (PACK2-D001)

**Task Definition:** [TASK-DETAIL.md §PACK2-D001](../TASK-DETAIL.md#pack2-d001--refactor-creationtool-to-emit-spawnentitycommand)  
**Design Reference:** [DESIGN.md §1.A](../DESIGN.md#1a--purge-createentityrequest-from-creationtool)

**Files to modify:**
- `Hrot.IG/Tools/CreationTool.cs` — replace the `Action<CreateEntityRequest>` constructor parameter.
- `Hrot.IG/IgApplication.cs` — update all call sites that create `CreationTool` instances.
- `Hrot.IG.Tests/CreationToolTests.cs` (likely exists) — update to use the new bus-based constructor.

**What to do:**

1. In `CreationTool.cs`:
   - Remove `using Hrot.NED.Descriptors;`, `using Hrot.NED.Messages;`, `using Hrot.NED.Common;`.
   - Replace the `Action<CreateEntityRequest>` constructor parameter with `Action<SpawnEntityCommand>` (for testability) **or** `FdpEventBus` (for production). The task definition allows either; match whatever pattern existing callers expect most cleanly.
   - On left-click, publish a `SpawnEntityCommand` (from `FDP.Toolkit.NetworkSpawning.Events`) containing `TkbType`, an initial `SimTransform` (geographic position from click), and optionally `OwnerNodeId`.
   - Preserve `OnCommandPublished` event — redirect it to fire when `SpawnEntityCommand` is published.
   - Preserve `autoPopOnPlace`, `_nameResolver`, multi-placement behaviour.
   - If `_initialPropertiesJson` was previously placed in `CreateEntityRequest.InitialAttributesJson`, place it in the appropriate `SpawnEntityCommand` field (inspect `SpawnEntityCommand` contract first).

2. In `IgApplication.cs`, find all `new CreationTool(...)` call sites and update them to pass an `Action<SpawnEntityCommand>` (or a bus) instead of `Action<CreateEntityRequest>`.

3. Remove `_createEntityDdsWriter` field and DDS writer creation at line ~814 *(unless D005 still needs it — check; after D005 installs the egress translator, the DDS write happens there instead)*.

**Tests required:**
- Unit test: instantiate `CreationTool` with a capturing `Action<SpawnEntityCommand>`. Simulate a left-click. Assert exactly one `SpawnEntityCommand` was captured with correct `TkbType` and a non-zero `SimTransform`.
- Compile-time: zero `using Hrot.NED` directives in `CreationTool.cs`.

---

### Task 2 — `EditTool` / `RouteEditTool`: Update `IgApplication.cs` Subscribers (PACK2-D002)

**Task Definition:** [TASK-DETAIL.md §PACK2-D002](../TASK-DETAIL.md#pack2-d002--refactor-edittool-and-routeedittool-to-emit-updateentitycommand)  
**Design Reference:** [DESIGN.md §1.B](../DESIGN.md#1b--cleanse-edittool-and-routeedittool-of-updateentitydescriptorrequest)

> Note: `EditTool.cs` and `RouteEditTool.cs` themselves are already clean (they use `OnPolylineCommitted` event / `onCommit` callback with no DDS types). The DDS coupling is in **`IgApplication.cs`** in the subscriber lambdas.

**Files to modify:**
- `Hrot.IG/IgApplication.cs` — update the subscriber lambdas for `OnPolylineCommitted` (around line 3523) and the `onCommit` callback passed to `RouteEditTool` (around line 3407).

**What to do:**

1. Find the lambda at `editTool.OnPolylineCommitted += (committedEntity, absCartPoints) => ...` (line ~3523). It currently builds `UpdateEntityDescriptorRequest`. Replace with:
   ```csharp
   editTool.OnPolylineCommitted += (committedEntity, absCartPoints) =>
   {
       if (!_world.IsAlive(committedEntity)) return;
       if (!_entityMap.TryGetNetworkId(committedEntity, out var networkId)) return;
   
       // Convert cartesian ghost-points back to a relative EditablePolyline representation.
       var updatedPolyline = ConvertGhostPointsToEditablePolyline(committedEntity, absCartPoints);
   
       _world.Bus.PublishManaged(new UpdateEntityCommand
       {
           NetworkId        = networkId,
           ComponentsToUpdate = new List<object> { updatedPolyline }
       });
   };
   ```
   *(Adapt `ConvertGhostPointsToEditablePolyline` to whatever helper already handles coordinate mapping — do not duplicate logic, reuse the existing coordinate conversion utilities.)*

2. Find the `RouteEditTool onCommit` callback (around line 3407). It currently builds `UpdateEntityDescriptorRequest` with `dtMapRoute` payload. Replace with:
   ```csharp
   Action<Entity, List<RouteWaypoint>> onCommit = (routeEntity, waypoints) =>
   {
       if (!_world.IsAlive(routeEntity)) return;
       if (!_entityMap.TryGetNetworkId(routeEntity, out var networkId)) return;
   
       var updatedPlan = new RoutePlan(waypoints.ToArray());
       _world.Bus.PublishManaged(new UpdateEntityCommand
       {
           NetworkId        = networkId,
           ComponentsToUpdate = new List<object> { updatedPlan }
       });
   };
   ```

3. Remove `_commandGateway`, `_commandGatewayInterface`, and `NedCommandGateway` fields/usage from `IgApplication.cs` only if they are exclusively used in the above commit callbacks (inspect carefully — the gateway may be used elsewhere; remove only D002-specific uses).

**Tests required:**
- Update/add integration test for the EditTool commit flow. In `Hrot.IG.Tests/AdvancedFeaturesIntegrationTests.cs` (line ~237): verify the new subscriber publishes `UpdateEntityCommand` with the correct `NetworkId` and modified `EditablePolyline`.
- For RouteEditTool: add a test verifying commit → `UpdateEntityCommand` with `RoutePlan` matching the waypoints.

---

### Task 3 — `ContextMenuSystem` / Delete Hotkeys: Remove Network Branching (PACK2-D003)

**Task Definition:** [TASK-DETAIL.md §PACK2-D003](../TASK-DETAIL.md#pack2-d003--remove-network-branching-from-context-menus-and-delete-hotkeys)  
**Design Reference:** [DESIGN.md §1.C](../DESIGN.md#1c--remove-network-branching-from-context-menus-and-delete-hotkeys)

**Files to modify:**
- `Hrot.IG/IgApplication.cs` — remove the `_networkEnabled` branch for entity deletion (line ~1514).

**What to do:**

In the `builder.AddItem("Delete entity", ()` lambda (around line 1504–1535) in `IgApplication.cs`,
replace the `if (_networkEnabled) { _deleteEntityDdsWriter?.Write(...)  } else { bus.Publish... }` with always publishing `DestroyEntityCommand`:

```csharp
builder.AddItem("Delete entity", () =>
{
    if (_world.IsAlive(entity) && _world.HasComponent<NetworkIdentity>(entity))
    {
        ref readonly var netId = ref _world.GetComponentRO<NetworkIdentity>(entity);
        _world.Bus.PublishManaged(new DestroyEntityCommand
        {
            NetworkId = netId.Value,
            Reason    = "context-menu-deleted"
        });
    }
    else if (_world.IsAlive(entity))
    {
        _world.DestroyEntity(entity);
    }

    if (_fdpInspectorState.SelectedEntity == entity)
        _fdpInspectorState.SelectedEntity = null;
});
```

Also check `Hrot.IG/IgApplication.cs` around line ~2769 for a second delete path (Delete key hotkey);
apply the same removal.

After this change, **remove** the `_deleteEntityDdsWriter` field and its `DdsWriter` construction
(line ~814) and disposal (line ~1897) from `IgApplication.cs`. After D005, DDS deletion is handled
by `DestroyEntityCommandEgressTranslator`.

**Tests required:**
- Unit test: verify that the delete action publishes exactly one `DestroyEntityCommand` on the bus.
- Compile-time: zero `IDdsWriter<DeleteEntityRequest>` field in `IgApplication.cs`.
- Regression: all entity-deletion integration tests still pass after D005 installs the egress translator.

---

### Task 4 — `MapCommandController`: Remove `IDdsWriter<CreateEntityRequest>` (PACK2-D004)

**Task Definition:** [TASK-DETAIL.md §PACK2-D004](../TASK-DETAIL.md#pack2-d004--sever-iddswritercreateentityrequest-from-mapcommandcontroller)  
**Design Reference:** [DESIGN.md §1.D](../DESIGN.md#1d--sever-iddswritercreateentityrequest-from-mapcommandcontroller)

**Files to modify:**
- `Hrot.IG/Systems/MapCommandController.cs` — remove `IDdsWriter<CreateEntityRequest> _createEntityWriter` field and constructor parameter; inject `FdpEventBus` instead; replace `_createEntityWriter.Write(request)` with `_eventBus.PublishManaged(new SpawnEntityCommand { ... })`.
- `Hrot.IG/IgApplication.cs` — update the `MapCommandController` constructor call at line ~910 to omit `_createEntityDdsWriter` and pass the event bus instead.

**Retain:** `IDdsWriter<MapCommandAck> _ackWriter` — the controller still needs to ACK the ExCon.

**Tests required:**
- Unit test: instantiate `MapCommandController` with a test bus and a mock `MapCommandAck` writer. Simulate receiving a `MapCommandRequest`. Assert the `CreationTool` is pushed onto the canvas and one `SpawnEntityCommand` appears on the bus after a simulated click.
- Compile-time: `MapCommandController.cs` has no `IDdsWriter<CreateEntityRequest>` field.
- Integration: `AreaAuthoringIntegrationTests` and `MiniExConIntegrationTests` pass after D005.

---

### Task 5 — Create ACL Egress Translators + Install in IG Composition Root (PACK2-D005)

**Task Definition:** [TASK-DETAIL.md §PACK2-D005](../TASK-DETAIL.md#pack2-d005--create-acl-egress-translators-for-spawn-update-and-destroy-commands)  
**Design Reference:** [DESIGN.md §1.E](../DESIGN.md#1e--create-acl-egress-translators-for-spawn-update-and-destroy-commands)

This task restores the DDS forwarding that was removed in D001–D004 by adding three ACL egress
translators. These translators catch FDP bus events and write them back to DDS wire.

**Study existing egress translators first:** Look at `Hrot.Map.Common/Replication/Egress/` for
patterns. `GeoSpatialEgressTranslator.cs` is a good model — it subscribes to bus events and writes
to a `DdsWriter<T>`.

**Files to create:**
- `Hrot.Map.Common/Replication/Egress/SpawnEntityCommandEgressTranslator.cs` (NEW)
- `Hrot.Map.Common/Replication/Egress/UpdateEntityCommandEgressTranslator.cs` (NEW)
- `Hrot.Map.Common/Replication/Egress/DestroyEntityCommandEgressTranslator.cs` (NEW)

**Files to modify:**
- `Hrot.IG/IgApplication.cs` — install the three translators in the `customTranslators` list (or as distinct `IEcsModule` registrations) so the distributed IG deployment forwards entity commands over DDS.

**What to create:**

#### `SpawnEntityCommandEgressTranslator`
```csharp
// Catches SpawnEntityCommand on the FdpEventBus; serialises to CreateEntityRequest; writes DDS.
// Constructor: (DdsWriter<CreateEntityRequest> writer, IGeographicTransform geoTransform, ...)
// Adapt fields from SpawnEntityCommand → CreateEntityRequest matching the existing SimHost schema.
// CRITICAL: preserve InitialAttributesJson round-trip fidelity so AreaAuthoring tests pass.
```

#### `UpdateEntityCommandEgressTranslator`
```csharp
// Catches UpdateEntityCommand; inspects ComponentsToUpdate list;
// If contains EditablePolyline → serialise to UpdateEntityDescriptorRequest with dtMapVisualOverlay.
// If contains RoutePlan → serialise to UpdateEntityDescriptorRequest with dtMapRoute.
// Uses the same serialisation helpers as the existing NED gateway code.
```

#### `DestroyEntityCommandEgressTranslator`
```csharp
// Catches DestroyEntityCommand; writes DeleteEntityRequest to DDS.
// Constructor: (DdsWriter<DeleteEntityRequest> writer)
```

**Install in `IgApplication.cs`:**

In `InitializeNetwork`, after creating the DDS participant, add the three translators to
`customTranslators`. The `DdsWriter<CreateEntityRequest>` used by `SpawnEntityCommandEgressTranslator`
replaces the old `_createEntityDdsWriter` field that was removed in D001.

**Tests required:**
- Unit test — Spawn: publish `SpawnEntityCommand` to a test bus; assert `SpawnEntityCommandEgressTranslator` calls the mock DDS writer with matching `TkbType` and geodetic position.
- Unit test — Destroy: publish `DestroyEntityCommand`; assert one `DeleteEntityRequest` with matching `NetworkId`.
- Integration: ALL `Hrot.ClusterRunner.Integration.Tests` that exercise `CreateEntityRequest` or `DeleteEntityRequest` on DDS pass after this task.

---

## 🧪 Final Testing Checklist

Run before writing your report:

```powershell
# Full solution build
dotnet build d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln

# IG unit tests + Map.Common tests
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot.IG.Tests
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot.Map.Common.Tests

# ClusterRunner unit + integration tests (critical — must pass)
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot.ClusterRunner.Tests
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot.ClusterRunner.Integration.Tests
```

Zero new failures. The 4 pre-existing failures (3 in ClusterRunner.Tests + 1 in SimHost.Tests)
may remain. Any NEW failure is a regression and must be fixed before reporting.

---

## 🎯 Success Criteria

- [ ] `CreationTool` has zero `using Hrot.NED` directives; emits `SpawnEntityCommand`.
- [ ] `IgApplication.cs` edit-commit subscribers publish `UpdateEntityCommand`.
- [ ] Delete path always publishes `DestroyEntityCommand`; `_deleteEntityDdsWriter` removed.
- [ ] `MapCommandController` has no `IDdsWriter<CreateEntityRequest>` field.
- [ ] `SpawnEntityCommandEgressTranslator`, `UpdateEntityCommandEgressTranslator`, `DestroyEntityCommandEgressTranslator` created in `Hrot.Map.Common/Replication/Egress/`.
- [ ] IG composition root installs the three translators.
- [ ] All integration tests that previously passed still pass.
- [ ] Report submitted to `.dev/packs-2/reports/BATCH-02-REPORT.md`.

---

## ⚠️ Pitfalls to Avoid

- **Do not remove `_createEntityAckReader` / `_mapCommandController`** from `IgApplication.cs` — the ack and remote-tool-activation paths are unchanged.
- **Do not remove `IDdsWriter<MapCommandAck>`** from `MapCommandController` — the controller still ACKs the ExCon for tool lifecycle.
- **Do not touch `ContextMenuSystem.cs`** behaviour (the class itself is already clean) — only the subscriber in `IgApplication.cs` changes.
- **Preserve `InitialAttributesJson` round-trip** in `SpawnEntityCommandEgressTranslator` — `AreaAuthoringIntegrationTests` depend on this.
- **Do not duplicate coordinate conversion logic** — reuse existing helpers.

---

## 📊 Report Requirements

Submit `.dev/packs-2/reports/BATCH-02-REPORT.md` answering:

**Q1:** What existing coordinate-conversion or NED serialisation utilities did you reuse in
the egress translators? Did you need to add any new ones?

**Q2:** What issues did you encounter (build errors, test failures, surprise dependencies)?
How did you resolve them?

**Q3:** Did you find any additional DDS/NED coupling in `IgApplication.cs` beyond what
the task definitions described? If so, what did you do about it?

**Q4:** Did you spot any weak points in the existing codebase that could cause problems
in later phases (E001–E004 tool migration)?

**Q5:** Suggested git commit message for this batch.

---

## 📚 Reference Materials

- **Task Defs:** `.dev/packs-2/TASK-DETAIL.md` — §PACK2-D001 through §PACK2-D005
- **Design:** `.dev/packs-2/DESIGN.md` — §Phase 1 (§1.A–1.E)
- **Previous review:** `.dev/packs-2/reviews/BATCH-01-REVIEW.md`
- **FdpEventBus:** `FDP/Kernel/Fdp.Kernel/FdpEventBus.cs`
- **SpawnEntityCommand:** `FDP/Toolkits/FDP.Toolkit.NetworkSpawning/Events/SpawnEntityCommand.cs`
- **UpdateEntityCommand:** `FDP/Toolkits/FDP.Toolkit.NetworkSpawning/Events/UpdateEntityCommand.cs`
- **DestroyEntityCommand:** `FDP/Toolkits/FDP.Toolkit.NetworkSpawning/Events/DestroyEntityCommand.cs`
- **Existing egress translator example:** `Hrot.Map.Common/Replication/Egress/GeoSpatialEgressTranslator.cs`
- **IgApplication.cs:** `Hrot.IG/IgApplication.cs`
- **CreationTool:** `Hrot.IG/Tools/CreationTool.cs`
- **MapCommandController:** `Hrot.IG/Systems/MapCommandController.cs`
