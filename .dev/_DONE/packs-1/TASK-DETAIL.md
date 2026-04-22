# TASK-DETAIL.md — Logic Packs & Translator Packs Refactoring

**Design Reference:** See [DESIGN.md](./DESIGN.md) for architecture overview, phase goals, and
rationale.

---

## Phase 1: NavigationStatus CQRS — Fix RouteContextSystem

**Design Reference:** [DESIGN.md §Phase 1](./DESIGN.md#phase-1-navigationstatus-cqrs--fix-routecontextsystem)

---

### PACK-N001 — Extend NavigationStatus with ProgressS

**Design Reference:** DESIGN.md §1.A

**Scope:**

- Add `float ProgressS` field to the ECS `NavigationStatus` struct.
- Add `float ProgressS` field to the HROT NED DDS wire struct for `NavigationStatus`.
- No logic changes in any system.

**Out of Scope:**

- Mapping the field in any translator (covered in PACK-N003).
- Populating the field from physics (covered in PACK-N002).

**Files:**

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Navigation.Contracts/NavigationComponents.cs` | Add `public float ProgressS;` to `NavigationStatus` struct |
| `Hrot.NED/SimDescriptors.cs` | Add `public float ProgressS;` to the wire `NavigationStatus` struct |

**Constraints:**

- `ProgressS` is in normalized route space (0.0 = start, end = cumulative arc-length value,
  consistent with `NavState.ProgressS` semantics).
- The field must be `float` (not `double`) to match `NavState.ProgressS`.
- Do not change any existing field names or ordering in the structs (DDS layout sensitivity).
  Append the new field at the end of each struct.

**Success Conditions:**

1. **Struct field exists (unit test):** Instantiate `NavigationStatus` and assign
   `ProgressS = 0.5f`; assert the value round-trips via direct field access.
2. **DDS wire struct field exists (compile-time):** The `Hrot.NED` project builds without error
   after the addition; a reflecting test enumerates the public fields of the DDS wire struct and
   asserts `ProgressS` is present.
3. **No regression:** All existing tests that use `NavigationStatus` continue to pass.

---

### PACK-N002 — Populate ProgressS in NavigationExecutionSystem

**Design Reference:** DESIGN.md §1.B

**Scope:**

- In `NavigationExecutionSystem`, read `NavState.ProgressS` and write it to the output
  `NavigationStatus.ProgressS` component.

**Out of Scope:**

- Translator mapping (PACK-N003).
- RouteContextSystem refactoring (PACK-N004).

**Files:**

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Navigation/Systems/NavigationExecutionSystem.cs` | Map ProgressS from NavState to NavigationStatus |

**Constraints:**

- `NavigationExecutionSystem` already queries `NavState` (Muscle-tier system). Adding the field
  mapping is additive only.
- If `NavState.ProgressS` is `0` (entity not yet moving), `NavigationStatus.ProgressS` must also
  be `0.0f`, not left at its default.

**Success Conditions:**

1. **Unit test — mapping:** Set up an entity with `NavState { ProgressS = 0.73f }` and an
   initial `NavigationStatus { ProgressS = 0f }`. Tick `NavigationExecutionSystem` once. Assert
   `NavigationStatus.ProgressS == 0.73f`.
2. **Unit test — zero passthrough:** `NavState.ProgressS = 0f` → `NavigationStatus.ProgressS == 0f`.
3. **Unit test — preserves existing fields:** After the tick, `NavigationStatus.IntentId` and
   `NavigationStatus.Result` retain their pre-tick values (no accidental zeroing).

---

### PACK-N003 — Update NavigationStatus Network Translators for ProgressS

**Design Reference:** DESIGN.md §1.C

**Scope:**

- `NavigationStatusEgressTranslator.ScanAndPublish`: map `NavigationStatus.ProgressS` (ECS) →
  DDS `ProgressS` field.
- `NavigationStatusIngressTranslator.PollIngress`: map DDS `ProgressS` → `NavigationStatus.ProgressS`
  (ECS).

**Out of Scope:**

- Changes to any other translator.

**Files:**

| File | Change |
|---|---|
| `Hrot.SimHost/` (or `Hrot.Common/`) egress translator for NavigationStatus | Add ProgressS field mapping on write |
| Ingress translator for NavigationStatus (Brain-side) | Add ProgressS field mapping on read |

> **Note:** Locate the exact files by searching for `NavigationStatusEgressTranslator` and
> `NavigationStatusIngressTranslator` in the `Hrot.SimHost` / `Hrot.Common` projects.

**Constraints:**

- A missing or `0` DDS `ProgressS` value must map to `0f` in ECS, not throw.
- Do not change the field mapping for any existing fields (`IntentId`, `Result`).

**Success Conditions:**

1. **Egress unit test:** Create an entity with `NavigationStatus { IntentId=3, Result=InProgress, ProgressS=0.4f }`.
   Run `ScanAndPublish`. Assert the produced DDS struct has `ProgressS == 0.4f` and that
   `IntentId` and `Result` are also correctly mapped (regression guard).
2. **Ingress unit test:** Feed a DDS struct `{ IntentId=3, Result=Arrived, ProgressS=0.9f }` to
   `PollIngress`. Assert the ECS component written to the entity has `ProgressS == 0.9f`.
3. **Roundtrip integration test (optional):** Run egress then ingress on the same data; assert all
   three fields survive the round-trip unchanged.

---

### PACK-N004 — Refactor RouteContextSystem (Brain-only query)

**Design Reference:** DESIGN.md §1.D

**Scope:**

- Remove `NavState` from the `_vehicleQuery` in `RouteContextSystem`.
- Add `NavigationIntent` and `NavigationStatus` to the query.
- Replace `nav.Mode` / `nav.TrajectoryId` reads with reads from `NavigationIntent`.
- Replace `nav.ProgressS` read with `status.ProgressS` from `NavigationStatus`.
- System is now a pure Brain-tier system with no Muscle-tier component dependencies.

**Out of Scope:**

- Any changes to `ResolveSegmentIndex` logic or `BrainBlackboard` write logic (preserved as-is).

**Files:**

| File | Change |
|---|---|
| `Hrot.SimHost/Systems/Routing/RouteContextSystem.cs` | Refactor query and field reads |

**Dependencies:** PACK-N001 (ProgressS field must exist), PACK-N002 (field must be populated).

**Constraints:**

- The system must *not* reference `NavState`, `VehicleState`, or any Muscle-tier struct after
  this change. Verify with a compile-time check (no using references to Muscle namespaces).
- The `_routeQuery` (`.With<RouteTrajectoryCache>().WithManaged<RoutePlan>()`) is unchanged.
- `SegmentIndex` resolution and `BrainBlackboard.Memory[BlackboardOffsets.ExpectedThreatLevel]`
  write behavior must be logically identical to the pre-refactor behavior.

**Success Conditions:**

1. **Unit test — positive path:** Set up an entity with:
   - `NavigationIntent { Mode = FollowRoute, TrajectoryId = 42, IntentId = 1 }`
   - `NavigationStatus { ProgressS = 0.5f }`
   - `BrainBlackboard` (zeroed)
   - `RouteTrajectoryCache` referencing a trajectory
   - `RoutePlan` with one segment whose arc span includes `ProgressS = 0.5f` and an
     `ExtensionJson` encoding a known threat level

   Tick `RouteContextSystem` once. Assert `BrainBlackboard.Memory[BlackboardOffsets.ExpectedThreatLevel]`
   equals the expected value derived from the segment's `ExtensionJson`.

2. **Unit test — no NavState required:** Same setup but *omit* the `NavState` component entirely.
   Assert the system still ticks and writes the blackboard correctly (i.e. no NullRef or
   missing-component exception).

3. **Unit test — inactive route:** Entity has `NavigationIntent.Mode != FollowRoute`. Assert the
   blackboard is *not* mutated.

4. **No Muscle component references (code review gate):** `RouteContextSystem.cs` must not
   reference `NavState`, `VehicleState`, `SimTransform`, or any type from
   `FDP.Toolkit.CarKinem` / physics namespaces.

---

## Phase 2: Module Realignment

**Design Reference:** [DESIGN.md §Phase 2](./DESIGN.md#phase-2-module-realignment)

---

### PACK-M001 — Relocate HsmDamageBridgeSystem to CognitiveRuntimeModule

**Design Reference:** DESIGN.md §2.A

**Scope:**

- Remove `HsmDamageBridgeSystem` registration from `CombatModule.RegisterSystems()`.
- Remove the stray `using FDP.Toolkit.Behavior.Systems;` directive from `CombatModule.cs` if it
  becomes unused.
- Add `HsmDamageBridgeSystem` registration to `CognitiveRuntimeModule.RegisterSystems()` *before*
  the `BTreeTickSystem` and `HsmTickSystem<T>` calls.

**Out of Scope:**

- Any changes to the internal logic of `HsmDamageBridgeSystem`.

**Files:**

| File | Change |
|---|---|
| `Hrot.SimHost/Modules/CombatModule.cs` | Remove `simGroup.AddSystem(new HsmDamageBridgeSystem())` |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Modules/CognitiveRuntimeModule.cs` | Add `group.AddSystem(new HsmDamageBridgeSystem())` before BTree/Hsm ticks |

**Constraints:**

- Registration order in `CognitiveRuntimeModule` must be: `ChannelArbitrationSystem`,
  `HsmDamageBridgeSystem` (NEW), `BTreeTickSystem`, `HsmTickSystem<BrainHsm128>`,
  `HsmTickSystem<BrainHsm64>`.
- No changes to system logic — purely a registration reloction.

**Success Conditions:**

1. **Integration test — damage disables HSM in distributed mode:** Simulate a Brain-only subsystem
   (no Muscle ECS components). Deliver a `DamageAssessedEvent` via the event bus. Assert that
   `HealthApplicationSystem` strips `CanMove`, and in the same frame `HsmDamageBridgeSystem`
   enqueues `MobilityLost` into `BrainHsm128` (inspect the HSM event queue). (Use the existing
   `HrotRunnerHarness` AllInOne test mode as reference for test setup.)

2. **Regression — AllInOne node still transitions on damage:** Run an existing integration test
   that validates HSM `MobilityLost` transition after damage. Confirm it still passes after the
   relocation.

3. **Negative test — Muscle node does not execute HsmDamageBridgeSystem:** In a Muscle-only
   subsystem context (no `BrainHsm128` components), tick `CombatModule` systems. Assert no
   exception is thrown (the system simply finds no matching entities).

---

### PACK-M002 — Delete ApcMobilityTriggerSystem; Absorb Logic into HealthApplicationSystem

**Design Reference:** DESIGN.md §2.B

**Scope:**

- Update `HealthApplicationSystem` to strip `ActorCapabilities.CanMove` from the entity whenever
  `DamageAssessedEvent` reduces `Health.Current` below `Health.Max` (non-lethal hit = mobility
  kill). This replaces the cross-domain role of `ApcMobilityTriggerSystem`.
- Delete `ApcMobilityTriggerSystem` (private inner class in `UrbanCombatNewScenario`) and remove
  its registration from `BuildSystems()`.
- Delete `ApcMobilitySystem` (`FDP/Examples/Fdp.Examples.UrbanCombat/Systems/ApcMobilitySystem.cs`)
  and remove its registration from `HeadlessDemoApp.cs`.

**Out of Scope:**

- Changes to `HsmDamageBridgeSystem` (it remains unchanged — it already reacts correctly to
  `CanMove` being cleared by `HealthApplicationSystem`).
- Changes to UrbanCombat scenario entity setup, HSM definitions, or test assertions.

**Files:**

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Combat/Systems/HealthApplicationSystem.cs` | Add CanMove strip logic on non-lethal hit |
| `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs` | Delete `ApcMobilityTriggerSystem` inner class and its `BuildSystems()` registration |
| `FDP/Examples/Fdp.Examples.UrbanCombat/Systems/ApcMobilitySystem.cs` | Delete file |
| `FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs` | Remove `ApcMobilitySystem` registration |

**Constraints:**

- `HealthApplicationSystem` must only strip `CanMove` when `Health.Current < Health.Max` after
  applying damage (i.e. any non-lethal hit). It must NOT strip `CanMove` on a hit that brings
  HP to exactly zero (lethal hit) or restores HP — those paths are handled elsewhere.
- `HealthApplicationSystem` must only operate on entities that *have* `ActorCapabilityState`. If
  the component is absent, skip silently.
- No new ECS queries may be added that span Brain and Muscle components.
- The full event chain is tested via the UrbanCombat scenario integration test (see success
  condition #3 below); do not break that test.

**Success Conditions:**

1. **Unit test — non-lethal hit strips CanMove:** Set up an entity with
   `Health { Current = 500f, Max = 500f }` and
   `ActorCapabilityState { Capabilities = CanMove | CanInteract }`.
   Publish a `DamageAssessedEvent { Amount = 100f }`. Tick `HealthApplicationSystem`.
   Assert:
   - `Health.Current == 400f`
   - `ActorCapabilityState.Capabilities` does **not** include `CanMove`
   - `ActorCapabilityState.Capabilities` still includes `CanInteract` (only CanMove stripped)

2. **Unit test — lethal hit does not double-strip (regression guard):** Entity at max health
   receives a lethal `DamageAssessedEvent { Amount = 500f }`. Assert the entity is destroyed (or
   HP == 0) and that `CanMove` stripping does not throw, regardless of entity liveness after the
   tick.

3. **Integration test — UrbanCombat scenario `LatchApcHalted` still passes:** Run the
   `UrbanCombatNewScenario` integration test end-to-end with `ApcMobilityTriggerSystem` deleted.
   The Insurgent fires an RPG; the APC takes a non-lethal hit; `HealthApplicationSystem` strips
   `CanMove`; `HsmDamageBridgeSystem` enqueues `MobilityLost`; APC HSM transitions to Disabled.
   Assert `LatchApcHalted == true` within the tick budget.

4. **No `ApcMobility*` references remain:** Workspace-wide grep for `ApcMobilityTriggerSystem`
   and `ApcMobilitySystem` returns zero results (excluding deleted files and test history).

---

## Phase 3: Enforce the Intent Bus

**Design Reference:** [DESIGN.md §Phase 3](./DESIGN.md#phase-3-enforce-the-intent-bus)

---

### PACK-I001 — Refactor PersonalRouteAuthoringSystem to Use NavigationIntent

**Design Reference:** DESIGN.md §3.A

**Scope:**

- In `PersonalRouteAuthoringSystem`, replace the `CmdFollowTrajectory` bus publish with writing
  `NavigationIntent { Mode=FollowRoute, TrajectoryId=..., IntentId++ }` as an ECS component.
- Preserve the existing deferred-frame mechanism (`_pendingFollowCommands` list); only the
  terminal action changes.

**Out of Scope:**

- Changes to `VehicleCommandSystem` (PACK-I003).
- Looping support beyond what `NavigationIntent` currently models.

**Files:**

| File | Change |
|---|---|
| `Hrot.SimHost/Systems/Routing/PersonalRouteAuthoringSystem.cs` | Replace CmdFollowTrajectory publish with NavigationIntent write |

**Constraints:**

- `IntentId` must be incremented to signal a new intent to `NavigationIntentBridgeSystem`
  (otherwise the bridge system's per-entity tracking will ignore the update as a duplicate).
- `NavigationMode.FollowRoute` must be used; do not use a raw integer.
- The system must compile without any reference to `CmdFollowTrajectory`.

**Success Conditions:**

1. **Unit test — intent is written:** Set up an entity with a `PersonalRouteCache` referencing
   `TrajectoryId = 7` and a `NavigationIntent { IntentId = 2, Mode = DirectPoint }`. Trigger the
   system's deferred commit. Assert the entity's `NavigationIntent` becomes
   `{ Mode = FollowRoute, TrajectoryId = 7, IntentId = 3 }`.
2. **Unit test — no CmdFollowTrajectory on bus:** After the same tick, assert the `FdpEventBus`
   has *zero* `CmdFollowTrajectory` events queued.
3. **Integration test — vehicle follows personal route:** Use the existing route-authoring
   integration test (if any); ensure the vehicle correctly begins following the personal route
   after editing it.

---

### PACK-I002 — Refactor SimHostVisualization Right-Click to Use NavigationIntent

**Design Reference:** DESIGN.md §3.B

**Scope:**

- In `SimHostVisualization.HandleRightClickForEntity`, replace the "Brain-dead path" (any direct
  `NavState` mutation or `CmdFollowTrajectory` / `CmdNavigateToPoint` publish) with writing
  `NavigationIntent { Mode=DirectPoint, FinalDestination=pos, TargetSpeed=15f, ArrivalRadius=3.0f, IntentId++ }`.

**Out of Scope:**

- Changes to the "Brain-active path" (mission machinery is preserved).

**Files:**

| File | Change |
|---|---|
| `Hrot.SimHost/` (search for `SimHostVisualization`) | Replace Brain-dead movement hack with NavigationIntent write |

**Constraints:**

- Must not mutate `NavState` directly anywhere in the right-click handler.
- `IntentId` must be incremented.
- Default `TargetSpeed = 15f` and `ArrivalRadius = 3.0f` are acceptable as temporary constants
  (matching the design talk guidance); add a TODO comment if these should become configurable.

**Success Conditions:**

1. **Unit test — intent written, no direct mutation:** Simulate a right-click at world position P
   on an entity that has no active doctrine (`BrainHsm*` absent or no active doctrine state).
   Assert the entity's `NavigationIntent` is set to `{ Mode=DirectPoint, FinalDestination=P,
   TargetSpeed=15f, ArrivalRadius=3.0f }` with `IntentId` incremented.
2. **Unit test — NavState not touched:** Assert `NavState` on the entity is unchanged after the
   right-click handler executes (ensuring no direct Muscle mutation).
3. **Existing CmdFollowTrajectory / CmdNavigateToPoint references removed:** After the change,
   `SimHostVisualization` must not reference either command event type.

---

### PACK-I003 — Remove Legacy Commands from VehicleCommandSystem

**Design Reference:** DESIGN.md §3.C

**Scope:**

- Delete processing of `CmdNavigateToPoint`, `CmdFollowTrajectory`, `CmdNavigateViaRoad`,
  `CmdStop`, `CmdSetSpeed` from `VehicleCommandSystem`.
- Delete the corresponding `Cmd*` struct definitions from `CommandEvents.cs` (or wherever they
  are defined).

**Out of Scope:**

- `CmdSpawnVehicle`, `CmdCreateFormation`, `CmdJoinFormation`, `CmdLeaveFormation` — these are
  not movement intents and are explicitly *kept*.
- `NavigationIntentBridgeSystem` — kept as is; it continues to handle `NavigationIntent → NavState`.

**Dependencies:** PACK-I001 and PACK-I002 must be complete first (no callers of the removed
commands should remain in the codebase).

**Files:**

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/VehicleCommandSystem.cs` | Remove 5 command-processing methods |
| Cmd event definitions file (e.g. `CommandEvents.cs`) | Delete 5 Cmd structs |

**Constraints:**

- A compiler error-free workspace after deletion is the gate. If any remaining code still
  references a deleted `Cmd*` event, that reference must be updated as part of this task.
- `VehicleCommandSystem` may retain `CmdSpawnVehicle`, formation commands, etc.

**Success Conditions:**

1. **Compile gate:** The entire solution builds without errors after the deletions.
2. **No remaining references:** A workspace-wide search for `CmdNavigateToPoint`,
   `CmdFollowTrajectory`, `CmdNavigateViaRoad` (for movement-intent usage), `CmdStop`,
   `CmdSetSpeed` returns zero results (excluding comments/history).
3. **Unit test — NavigationIntentBridgeSystem still translates intents:** Set up an entity with
   `NavigationIntent { Mode=DirectPoint, FinalDestination=P, TargetSpeed=10f }`. Tick
   `NavigationIntentBridgeSystem`. Assert `NavState` reflects the new destination (KinematicsMode
   and destination set). This confirms that the removal of `VehicleCommandSystem` shortcuts does
   not break the intent→physics pipeline.

---

## Phase 4: Anti-Corruption Layer — Pluggability Violations

**Design Reference:** [DESIGN.md §Phase 4](./DESIGN.md#phase-4-anti-corruption-layer--pluggability-violations)

---

### PACK-P001 — Split MissionControlRequestSystem into Translator + Logic

**Design Reference:** DESIGN.md §4.A

**Scope:**

- Define two new domain events: `MissionControlIntent` (class) and `MissionControlAckEvent` (struct).
- Create `MissionControlIngressTranslator`: polls DDS, publishes `MissionControlIntent` to bus.
- Create `MissionControlAckEgressTranslator`: consumes `MissionControlAckEvent` from bus, writes
  DDS ACK.
- Refactor `MissionControlRequestSystem` → `MissionControlExecutionSystem`: remove all DDS
  fields; consume `MissionControlIntent`; publish `MissionControlAckEvent`; delete the
  `DdsWriter<EntityMission>` (auto-replicated by `EntityMissionEgressTranslator`).
- Register the two new translators in the network boundary module; register the execution system
  in the core logic module.

**Out of Scope:**

- Changes to `EntityMissionEgressTranslator` (it continues to handle ECS → DDS mission
  replication automatically).
- Changes to `DoctrineRegistry` or `NetworkEntityMap` usage within the execution logic itself.

**New files / locations:**

| File | Notes |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Behavior/Events/MissionControlCqrsEvents.cs` | New domain events |
| `Hrot.SimHost/Network/Ingress/MissionControlIngressTranslator.cs` | New ingress translator |
| `Hrot.SimHost/Network/Egress/MissionControlAckEgressTranslator.cs` | New egress translator |
| `Hrot.SimHost/Systems/MissionControlExecutionSystem.cs` | Renamed/refactored from existing |

**Constraints:**

- `MissionControlExecutionSystem` must have *zero* references to `DdsReader`, `DdsWriter`,
  `DdsParticipant`, or `System.Text.Json` after the refactor.
- `MissionControlIntent.Payload` must use a strongly-typed `MissionCommandUnion` (not raw JSON
  string). JSON deserialization lives exclusively in `MissionControlIngressTranslator`.
- The internal constructor (unit-test constructor) in the original
  `MissionControlRequestSystem` should be adapted into `MissionControlExecutionSystem`'s
  test constructor (accepting `FdpEventBus` directly instead of a `DdsParticipant`).

**Success Conditions:**

1. **Unit test — execution system is DDS-free:** Instantiate `MissionControlExecutionSystem` with
   only an `FdpEventBus` and `DoctrineRegistry`. Publish a `MissionControlIntent` to the bus.
   Tick the system. Assert that:
   - The target entity's `MissionPlanQueue` is updated per the payload.
   - A `MissionControlAckEvent` is published on the bus with `ErrorCode == 0`.
2. **Unit test — error path:** Publish a `MissionControlIntent` with an invalid `BaseVersion`.
   Assert a `MissionControlAckEvent` with non-zero `ErrorCode` is published and `MissionPlanQueue`
   is *not* mutated.
3. **Integration test — end-to-end via translators:** Run the full stack (ingress translator
   → DDS wire → execution system → egress translator). Send a real DDS `MissionControlRequest`.
   Assert the DDS `MissionControlAck` is received with correct fields.
4. **No `DdsWriter<EntityMission>` reference in execution system:** Grep the refactored file;
   assert zero occurrences of `EntityMission` DDS writer usage.

---

### PACK-P002 — Extract Spawning Request Systems out of SimHostModule

**Design Reference:** DESIGN.md §4.B

**Scope:**

- Move `DdsCreateEntityRequestSource` and `DdsCreateUpdateDeleteEntityAckSink` inner classes
  out of `SimHostModule.cs` into a dedicated network adapter file.
- Remove `_requestSystem` (CreateEntityRequestSystem) and `_deleteSystem`
  (DeleteEntityRequestSystem) fields and their registration from `SimHostModule.RegisterSystems()`.
- Register both systems in the **network-boundary module** (wherever the DDS participant is
  managed), passing the extracted DDS adapters as constructor arguments.
- `SimHostModule` constructor must no longer require a `DdsParticipant` as a mandatory
  parameter.

**Out of Scope:**

- Internal logic of `CreateEntityRequestSystem` or `DeleteEntityRequestSystem` — unchanged.

**Files:**

| File | Change |
|---|---|
| `Hrot.SimHost/Modules/SimHostModule.cs` | Remove DDS adapter inner classes, remove Create/Delete system fields and registrations |
| `Hrot.SimHost/Network/SimHostNetworkAdapters.cs` (new) | Contains `DdsCreateEntityRequestSource` and `DdsCreateUpdateDeleteEntityAckSink` |
| Network-boundary module / `SimHostApp.cs` | Register `CreateEntityRequestSystem` and `DeleteEntityRequestSystem` here |

**Constraints:**

- `SimHostModule.cs` must have zero references to `DdsParticipant`, `DdsReader`, or `DdsWriter`
  after this change.
- The `ICreateEntityRequestSource` and `ICreateUpdateDeleteEntityAckSink` interfaces must be
  unchanged — the DDS implementations simply move.
- Offline bootstrap (no `DdsParticipant`) must not instantiate or register the two systems.

**Success Conditions:**

1. **Compile gate:** Solution builds without errors after the move.
2. **Offline instantiation:** Construct `SimHostModule` without passing a `DdsParticipant`.
   Assert no exception is thrown and the module does not reference any CycloneDDS type.
3. **Online system registration:** In the network-boundary module startup, assert both
   `CreateEntityRequestSystem` and `DeleteEntityRequestSystem` appear in the registered system
   list when a `DdsParticipant` is available.
4. **No `DdsParticipant` in SimHostModule (code review gate):** Grep `SimHostModule.cs` for
   `DdsParticipant`, `DdsReader`, `DdsWriter` — zero results.

---

### PACK-P003 — Strip NetworkEntityMap from HitResolutionSystem and AimAndFireExecutor

**Design Reference:** DESIGN.md §4.C

**Scope:**

- Modify `DetonationNotification` to use local ECS `Entity` handles instead of `long` network IDs.
- Modify `WeaponFireIntent` to use local ECS `Entity` handles instead of `long` network IDs.
- Refactor `HitResolutionSystem`: always emit `DetonationNotification`; remove `NetworkEntityMap`
  overload.
- Refactor `AimAndFireExecutor`: remove `NetworkEntityMap` from constructor.
- Update `MunitionDetonationEgressTranslator` and `WeaponFireIntentEgressTranslator`: inject
  `NetworkEntityMap`; resolve local handles → net IDs before publishing DDS messages.

**Out of Scope:**

- Changes to the ballistic trajectory, damage calculation, or projectile spawn logic.

**Files:**

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Combat.Contracts/DetonationNotification.cs` | Replace long IDs with Entity handles |
| `FDP/Toolkits/FDP.Toolkit.Combat.Events/WeaponFireIntent.cs` | Replace long IDs with Entity handles |
| `FDP/Toolkits/FDP.Toolkit.Physics/Systems/HitResolutionSystem.cs` | Remove NetworkEntityMap; always emit |
| `FDP/Toolkits/FDP.Toolkit.Combat/Systems/.../AimAndFireExecutor.cs` | Remove NetworkEntityMap |
| `Hrot.SimHost/Network/Egress/MunitionDetonationEgressTranslator.cs` | Add NetworkEntityMap injection + ID resolution |
| `Hrot.SimHost/Network/Egress/WeaponFireIntentEgressTranslator.cs` | Add NetworkEntityMap injection + ID resolution |

**Constraints:**

- `FDP.Toolkit.Physics` and `FDP.Toolkit.Combat` projects must have zero references to
  `NetworkEntityMap` after this change.
- `HitResolutionSystem` now *always* emits `DetonationNotification` (previously gated on
  `_entityMap != null`). In an offline / AllInOne context this is fine — the event simply has
  no DDS translator listening.
- The `NetworkEntityMap` lookup in translators should gracefully handle the case where no
  net ID mapping exists for a given local entity (log and skip, do not throw).

**Success Conditions:**

1. **Unit test — HitResolutionSystem offline:** Instantiate `HitResolutionSystem()` (no-arg).
   Simulate a hit event. Assert `DetonationNotification` is published on the bus with local
   `Entity` handle fields (not `0L` / placeholder long IDs).
2. **Unit test — translator resolves IDs:** Provide a `NetworkEntityMap` with entry
   `{localEntity → netId = 42L}`. Feed a `DetonationNotification` with that `localEntity` to
   `MunitionDetonationEgressTranslator.ScanAndPublish`. Assert the produced DDS struct has
   `ShooterNetId == 42L`.
3. **Unit test — translator skips unknown entity:** Feed a `DetonationNotification` with an
   entity NOT registered in `NetworkEntityMap`. Assert no DDS packet is produced (or a logged
   warning), but no exception is thrown.
4. **Compile gate:** `FDP.Toolkit.Physics.csproj` and `FDP.Toolkit.Combat.csproj` have zero
   references to `NetworkEntityMap` (verify with project reference graph).

---

## Phase 5: Orchestration Domain CQRS Cleanup

**Design Reference:** [DESIGN.md §Phase 5](./DESIGN.md#phase-5-orchestration-domain-cqrs-cleanup)

---

### PACK-C001 — Purify ClusterMaster (Remove DDS Constructors and Fallback Paths)

**Design Reference:** DESIGN.md §5.A

**Scope:**

- Delete `ClusterMaster(DdsParticipant)` and `ClusterMaster(DdsParticipant, ClusterConfiguration)`.
- Delete all DDS reader/writer fields and the `DdsIdAllocatorServer` thread management.
- Remove DDS fallback branches from `Tick()`, `IngestHeartbeats()`,
  `ConsumeNodeOpStatuses()`, `PublishOpStatus()`, `PublishClusterState()`,
  `FanOutNodeOp()`, `EjectNode()`, and `Dispose()`.
- Migrate full ACK logic (Live-from-Replay + Episode 2PC) into the bus-based
  `ConsumeNodeOpStatuses()` loop, removing the DDS reader block entirely.
- Define `AssetInventoryUpdateEvent` in `ClusterCqrsEvents.cs`
  (with `[EventId(9017)]` and `[DataPolicy(DataPolicy.NoRecord)]`).
- `ClusterMaster.PublishAssetInventory()` publishes `AssetInventoryUpdateEvent` to bus.
- Update `ClusterOpMasterTranslator` to consume `AssetInventoryUpdateEvent` and call
  `_inventoryWriter.Write(...)`.

**Out of Scope:**

- Changes to `ClusterOpMasterTranslator`'s other responsibilities.
- Changes to `DdsIdAllocatorServer` class itself — it is just no longer instantiated here.

**Files:**

| File | Change |
|---|---|
| `Hrot.Orchestrator/ClusterMaster.cs` | Major purge — see scope above |
| `Hrot.Orchestrator/Events/ClusterCqrsEvents.cs` | Add `AssetInventoryUpdateEvent` |
| `Hrot.Orchestrator/Translators/ClusterOpMasterTranslator.cs` | Add asset inventory consumption |

**Constraints:**

- No backward-compatible overloads. The two DDS constructors are **fully deleted**.
- `_eventBus` field must be `private readonly FdpEventBus _eventBus;` (non-nullable, no `!`).
- After deletion, `ClusterMaster` must have zero references to `CycloneDDS.Runtime` or any
  `Hrot.NED` namespace.
- All callers of the deleted constructors in the non-test codebase must be updated to use the
  bus constructor (or an `OrchestratorSubsystem`-level wiring change).

**Success Conditions:**

1. **Compile gate:** Zero references to `DdsParticipant` in `ClusterMaster.cs` after changes.
2. **Unit test — bus constructor only:** `new ClusterMaster(eventBus)` constructs without
   exception. `new ClusterMaster(ddsParticipant)` produces a compile error (constructor deleted).
3. **Unit test — PublishAssetInventory pushes event:** Call `PublishAssetInventory(...)`. Assert
   the bus contains one `AssetInventoryUpdateEvent` with the correct field values.
4. **Unit test — ConsumeNodeOpStatuses processes both ACK types via bus:** Publish a
   `NodeOpCompletedEvent` matching a pending branch task and one matching a pending episode
   task. Tick `ClusterMaster`. Assert both `_pendingBranchTasks` and
   `_pendingManageEpisodeTasks` entries are resolved correctly.
5. **Integration test regression:** Existing `OrchestratorIntegrationTests` pass without
   modification (they use the bus-based wiring).

---

### PACK-C002 — Purify ClusterUiCache + Create OrchestrationObserverTranslator

**Design Reference:** DESIGN.md §5.B

**Scope:**

- Remove all seven `DdsReader<T>` fields from `ClusterUiCache`.
- Update the constructor to `ClusterUiCache(FdpEventBus bus, ITimeController? localTimeController = null)`.
- Update `Update()` to consume FdpEvents: `SystemStateUpdateEvent`, `AssetInventoryUpdateEvent`,
  `NodeHeartbeatEvent`, `SwitchTimeModeEvent`, `ClusterOpCompletedEvent`, `ExecuteNodeOpIntent`,
  `NodeOpCompletedEvent`.
- Replace `JsonDocument.Parse` usage in `Process2PcNetworkTraffic` with typed `DomainPayload`
  inspection.
- Define `SystemStateUpdateEvent` in `ClusterCqrsEvents.cs` (with `[EventId(9016)]` and
  `[DataPolicy(DataPolicy.NoRecord)]`).
- Create `OrchestrationObserverTranslator` in `Hrot.Common/Orchestration/` with all seven
  `DdsReader<T>` fields; its `Tick()` polls DDS and publishes events to the provided
  `FdpEventBus`.
- Update `ExConSubsystem.cs` (and any other `ClusterUiCache` construction site) to the new
  three-component wiring pattern.

**Out of Scope:**

- Changes to `ClusterUiCache` business logic (UI state derivation, reachable-targets planner,
  etc.).
- Changes to `NodeOpSlaveTranslator` (it handles its own bus integration separately).

**Files:**

| File | Change |
|---|---|
| `Hrot.ClusterRunner/Services/ClusterUiCache.cs` | Remove DdsReaders; switch to FdpEventBus |
| `Hrot.Orchestrator/Events/ClusterCqrsEvents.cs` | Add `SystemStateUpdateEvent` |
| `Hrot.Common/Orchestration/OrchestrationObserverTranslator.cs` | New file |
| `Hrot.ExCon/ExConSubsystem.cs` (or equivalent) | Update construction site |

**Constraints:**

- `ClusterUiCache` must have zero references to `CycloneDDS.Runtime` after the change.
- `FdpEventBus` used in ExCon is standalone — no `ModuleHostKernel` required.
- `OrchestrationObserverTranslator` must forward *all* `NodeOpCommand` DDS messages
  promiscuously (not just those addressed to the local node) so `ClusterUiCache` can build a
  complete 2PC history.
- The `PayloadJson` deserialization reuses the existing logic from `NodeOpSlaveTranslator`
  (do not duplicate; extract a shared helper or call a shared factory).

**Success Conditions:**

1. **Unit test — ClusterUiCache with mock bus:** Create an `FdpEventBus`. Publish
   `SystemStateUpdateEvent { CurrentState = ClusterState.Running }`. Tick
   `ClusterUiCache.Update()`. Assert `ClusterUiCache.CurrentState == ClusterState.Running`.
2. **Unit test — inventory update:** Publish `AssetInventoryUpdateEvent { LocalScenarios = ["sc1"] }`.
   Tick. Assert `ClusterUiCache.AvailableScenarios[0] == "sc1"`.
3. **Unit test — 2PC history without JSON parsing:** Publish `ExecuteNodeOpIntent` with a typed
   `NodeTransitionPayloadDto { TargetState = "Running" }`. Tick. Assert the in-flight transaction
   history entry has `TargetDsmState == ClusterState.Running` and that `JsonDocument.Parse` is
   *never called* (mock or verify via code review).
4. **Integration test — ExCon UI updates on cluster state change:** Wire a full ExCon subsystem
   with `OrchestrationObserverTranslator`. Send a `SystemStateTopic` DDS message. Tick. Assert
   `ClusterUiCache.CurrentState` reflects the DDS message value.
5. **Compile gate — no DDS in ClusterUiCache:** `Hrot.ClusterRunner/Services/ClusterUiCache.cs`
   has zero references to `DdsReader`, `DdsWriter`, or `DdsParticipant` after the change.

---

## Phase 4 Addendum: UpdateEntityDescriptorRequestSystem

### PACK-P004 — Relocate UpdateEntityDescriptorRequestSystem to Replication.Ingress Namespace

**Design Reference:** DESIGN.md §4.B (Step 4.B.3)

**Scope:**

- Move `UpdateEntityDescriptorRequestSystem.cs` from `Hrot.Map.Common/Systems/` to
  `Hrot.Map.Common/Replication/Ingress/`.
- Update the namespace from `Hrot.Map.Common.Systems` to `Hrot.Map.Common.Replication.Ingress`.
- Remove the unconditional `_kernelGroup.AddSystem(new UpdateEntityDescriptorRequestSystem(...))`
  call from `SimHostApp.cs`.
- Register it conditionally in the same network-boundary module as the other spawning systems
  (see PACK-P002).

**Out of Scope:**

- Any changes to the internal logic of `UpdateEntityDescriptorRequestSystem`.

**Dependencies:** PACK-P002 (the network-boundary module registration site must exist first).

**Files:**

| File | Change |
|---|---|
| `Hrot.Map.Common/Systems/UpdateEntityDescriptorRequestSystem.cs` | Move to `Replication/Ingress/` + namespace update |
| `Hrot.SimHost/SimHostApp.cs` | Remove unconditional registration |
| Network-boundary module | Add conditional registration alongside Create/Delete systems |

**Constraints:**

- No logic changes inside the system itself — file path and namespace only.
- After the move, `Hrot.Map.Common/Systems/` must contain zero references to
  `UpdateEntityDescriptorRequestSystem`.

**Success Conditions:**

1. **Compile gate:** Solution builds with zero errors.
2. **Namespace check:** Grep for `Hrot.Map.Common.Systems.UpdateEntityDescriptorRequestSystem` —
   returns zero results.
3. **Offline exclusion:** Bootstrapping without a `DdsParticipant` does not register the system.
4. **Online inclusion:** Bootstrapping with a `DdsParticipant` registers the system via the
   network-boundary module.

---

## Phase 6: ExCon Egress Anti-Corruption Layer

**Design Reference:** [DESIGN.md §Phase 6](./DESIGN.md#phase-6-excon-egress-anti-corruption-layer)

---

### PACK-E001 — Eradicate DdsWriter from ClusterScenarioPanel

**Design Reference:** DESIGN.md §6.A

**Scope:**

- Define `ClusterOpIntent` event in `ClusterCqrsEvents.cs` (`[EventId(9018)]`).
- Remove the `DdsWriter<ClusterOpRequest>` constructor overload and field from
  `ClusterScenarioPanel`.
- Replace `SendRequest(ClusterOpRequest)` with `_bus.PublishManaged(new ClusterOpIntent { ... })`.
  Delete all inline `PayloadJson = $"..."` string interpolations from the class.
- Create `ClusterOpEgressTranslator` in `Hrot.Common/Orchestration/`: consumes `ClusterOpIntent`
  from `FdpEventBus`, serializes `DomainPayload` to JSON, writes `ClusterOpRequest` to DDS.
- Update `ExConSubsystem.cs`: replace `_sysOpWriter` field with `_clusterOpEgressTranslator`;
  inject `FdpEventBus` into `ClusterScenarioPanel` for the remote path.
- Update `OrchestratorSubsystem.cs`: if it also created `_sysOpWriter`, remove the DDS writer
  and use the same bus-based pattern.

**Out of Scope:**

- The `ClusterScenarioPanel(ClusterMaster, ClusterUiCache)` constructor (Orchestrator internal
  path) — it may stay as-is; `ClusterMaster` is already CQRS-clean after Phase 5.
- Changes to `ClusterUiCache` (Phase 5 handles ingress; this handles egress).

**New files / modified files:**

| File | Change |
|---|---|
| `Hrot.Orchestrator/Events/ClusterCqrsEvents.cs` | Add `ClusterOpIntent` struct |
| `Hrot.Common/Orchestration/ClusterOpEgressTranslator.cs` | New file |
| `Hrot.ClusterRunner/Services/ClusterScenarioPanel.cs` | Remove DDS ctor + field; add FdpEventBus remote path |
| `Hrot.ClusterRunner/Services/ExConSubsystem.cs` | Remove `_sysOpWriter`; wire `ClusterOpEgressTranslator` |
| `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs` | Remove `_sysOpWriter` if present |

**Constraints:**

- After the change, `ClusterScenarioPanel.cs` must have zero references to `CycloneDDS.Runtime`,
  `DdsWriter`, or `System.Text.Json`.
- `ClusterOpEgressTranslator` is the **only** class in the egress stack that may call
  `System.Text.Json.JsonSerializer`.
- The `ClusterScenarioPanelTests` test class currently constructs the panel with a
  `DdsWriter<ClusterOpRequest>`. Update the tests to use the new bus-based constructor and assert
  that `ClusterOpIntent` events are published to the bus instead of DDS writes.

**Success Conditions:**

1. **Unit test — panel publishes ClusterOpIntent:** Construct `ClusterScenarioPanel` with an
   `FdpEventBus`. Trigger the `ReplaySeek` action (set `_seekSliderValue`, call `Update(dt)` past
   debounce). Assert the bus contains a `ClusterOpIntent { OperationType = ReplaySeek }` with
   the correct `TargetWallTicks` payload. Assert zero DDS packets emitted.
2. **Unit test — egress translator serializes to DDS:** Publish a
   `ClusterOpIntent { OperationType = StartEpisode, DomainPayload = new NodeEpisodePayloadDto { EpisodeId = guid } }`
   to the bus. Tick `ClusterOpEgressTranslator`. Assert the emitted `ClusterOpRequest.PayloadJson`
   deserializes back to the same `EpisodeId`.
3. **Compile gate — no DDS in ClusterScenarioPanel:** Grep `ClusterScenarioPanel.cs` for
   `DdsWriter`, `DdsParticipant`, `CycloneDDS`, `System.Text.Json` — zero results.
4. **Regression — existing ClusterScenarioPanelTests pass** (after test updates for bus ctor).

---

### PACK-E002 — Eradicate IDdsWriter from MissionEditorService

**Design Reference:** DESIGN.md §6.B

**Scope:**

- Replace `IDdsWriter<MissionControlRequest>` constructor parameter in `MissionEditorService`
  with `FdpEventBus`.
- In `CommitMissionAsync`, publish `MissionControlIntent` (defined in PACK-P001) to the bus
  instead of calling `_requestWriter.Write(...)`.
- Create `MissionControlEgressTranslator` in `Hrot.ExCon/Network/` (or `Hrot.Common/Network/`):
  consumes `MissionControlIntent` from `FdpEventBus`, serializes parameters to JSON, writes
  `MissionControlRequest` DDS message.
- ACK ingress: `MissionEditorService` currently implements `IIngressHandler` for
  `MissionControlAck`. Replace this: have a `MissionControlAckIngressTranslator` publish
  `MissionControlAckEvent` onto the bus; `MissionEditorService.OnAckReceived()` refactors to
  `_bus.ConsumeManaged<MissionControlAckEvent>()` (polled on the service tick) OR the service
  subscribes as a bus consumer.
- Update all construction sites (`ExConLogic`, `WorkflowTests`, `MultiIosIntegrationTests`) to
  pass `FdpEventBus` instead of `IDdsWriter<MissionControlRequest>`.

**Out of Scope:**

- Changes to `MissionControlExecutionSystem` on the SimHost side (covered in PACK-P001).
- Changes to `MissionControlIngressTranslator` (SimHost side, PACK-P001).

**Dependencies:** PACK-P001 (the `MissionControlIntent` and `MissionControlAckEvent` types must
be defined before this task).

**New files / modified files:**

| File | Change |
|---|---|
| `Hrot.ExCon/Services/MissionEditorService.cs` | Replace DDS writer with FdpEventBus |
| `Hrot.ExCon/Network/MissionControlEgressTranslator.cs` (new) | Serializes intent → DDS |
| `Hrot.ExCon/Network/MissionControlAckIngressTranslator.cs` (new or extend existing) | DDS ACK → bus event |
| All `MissionEditorService` construction sites | Pass `FdpEventBus` |

**Constraints:**

- `MissionEditorService` must have zero references to `IDdsWriter`, `DdsWriter`, `DdsReader`,
  or any type from `Hrot.NED.Messages` after the refactor.
- `MissionControlEgressTranslator` is the **only** class in the ExCon mission stack that calls
  `System.Text.Json.JsonSerializer`.
- Pending-commit timeout logic (`_commitTimeoutMs`, `_pendingCommits` dict, `CancellationToken`)
  is preserved unchanged — only the transport mechanism changes.
- `WorkflowTests` in `Hrot.ExCon.Tests` currently passes `IDdsWriter<MissionControlRequest>`
  as a stub. Update the tests to pass a bus and verify `MissionControlIntent` events are
  published.

**Success Conditions:**

1. **Unit test — service publishes MissionControlIntent:** Call `CommitMissionAsync(...)` on
   `MissionEditorService` (with `FdpEventBus`). Assert the bus contains a `MissionControlIntent`
   with the correct `TargetEntityId`, `RequestId`, and `Payload`. Assert
   `IDdsWriter<MissionControlRequest>` is never referenced.
2. **Unit test — ACK resolves commit:** Publish a `MissionControlAckEvent { RequestId=..., ErrorCode=0,
   NewVersion=2 }` to the bus (simulating what `MissionControlAckIngressTranslator` would do).
   Tick/await `MissionEditorService`. Assert the `CommitMissionAsync` result resolves with
   `Success=true, NewVersion=2`.
3. **Unit test — timeout still works:** Do not publish any ACK. Let the commit timeout expire.
   Assert `CommitMissionAsync` resolves with `Success=false`.
4. **Compile gate — no DDS in MissionEditorService:** Grep `MissionEditorService.cs` for
   `IDdsWriter`, `DdsWriter`, `DdsParticipant`, `CycloneDDS` — zero results.
5. **Existing WorkflowTests pass** (after updating construction to use bus).

---

## Phase 7: Remaining Combat and Perception ACL Leaks

---

### PACK-D001 — Purify DamageAssessedEvent

**Design Reference:** DESIGN.md §7.A

**Scope:**

- Change `DamageAssessedEvent.HitEntityId: long` to `HitEntity: Entity` in
  `FDP/Toolkits/FDP.Toolkit.Combat/Events/DetonationEvents.cs`.
- `DamageCalculationSystem`: set `HitEntity = detonationTarget` (ECS handle from
  `DetonationNotification.Target`). Remove `NetworkEntityMap` constructor parameter.
- `HealthApplicationSystem`: read `evt.HitEntity` directly. Remove `NetworkEntityMap`
  constructor parameter.
- `DamageAssessedEgressTranslator` (`Hrot.SimHost/Network/Egress/DamageAssessedEgressTranslator.cs`):
  inject `NetworkEntityMap`. Resolve `evt.HitEntity` → `long` net ID before writing the DDS
  packet.
- `EntityHitDamageIngressTranslator` (`Hrot.SimHost/Network/Ingress/EntityHitDamageIngressTranslator.cs`):
  already injects `NetworkEntityMap`. Change the publish call from
  `HitEntityId = msg.HitEntityId` to `HitEntity = _entityMap.GetEntity(msg.HitEntityId)`.
- Update all affected tests (`EntityHitDamageIngressTranslatorTests`,
  `DamageAssessedEgressTranslatorTests`, and any integration tests) that assert on
  `long HitEntityId` to assert on `Entity HitEntity`.

**Out of Scope:**

- Changes to `WeaponFireIntent` or `DetonationNotification` (covered in PACK-P003).
- Changes to `HitResolutionSystem` (covered in PACK-P003).

**Dependencies:** PACK-P003 may be done in parallel; the two tasks touch adjacent files
(`DamageCalculationSystem` is downstream of `DetonationNotification`) but are editorially
independent.

**New files / modified files:**

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Combat/Events/DetonationEvents.cs` | `long HitEntityId` → `Entity HitEntity` |
| `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageCalculationSystem.cs` | Use `Entity` handle; remove `NetworkEntityMap` |
| `FDP/Toolkits/FDP.Toolkit.Combat/Systems/HealthApplicationSystem.cs` | Use `evt.HitEntity`; remove `NetworkEntityMap` |
| `Hrot.SimHost/Network/Egress/DamageAssessedEgressTranslator.cs` | Inject `NetworkEntityMap`; resolve `Entity→long` |
| `Hrot.SimHost/Network/Ingress/EntityHitDamageIngressTranslator.cs` | Resolve `long→Entity` on event publish |
| Tests in `Hrot.SimHost.Tests/` or `Hrot.SimHost.Integration.Tests/` | Update `long HitEntityId` assertions |

**Constraints:**

- After this task, `DamageCalculationSystem` and `HealthApplicationSystem` must have zero
  references to `NetworkEntityMap`, `Hrot.NED`, or any type that requires a network runtime.
- `DamageAssessedEgressTranslator` must be the **only** class in the combat event chain that
  holds a reference to `NetworkEntityMap`.

**Success Conditions:**

1. **Unit test — DamageCalculationSystem produces Entity handle:** Fire `DetonationNotification`
   with a valid target `Entity`. Assert `DamageAssessedEvent.HitEntity` equals the target
   entity. Assert no `NetworkEntityMap` parameter in the constructor signature.
2. **Unit test — HealthApplicationSystem uses Entity handle directly:** Publish
   `DamageAssessedEvent { HitEntity = someEntity }`. Assert HP is reduced on `someEntity`.
   Assert no `NetworkEntityMap` parameter in the constructor signature.
3. **Unit test — DamageAssessedEgressTranslator resolves Entity→long:** Publish
   `DamageAssessedEvent { HitEntity = entity }`. Assert the emitted DDS `EntityHitDamage`
   message contains the correct `long` net ID from `NetworkEntityMap`.
4. **Unit test — EntityHitDamageIngressTranslator resolves long→Entity:** Feed a DDS
   `EntityHitDamage` sample with a `long HitEntityId`. Assert the published bus event carries
   `HitEntity = resolvedEntity`.
5. **Compile gate:** Grep `DamageCalculationSystem.cs` and `HealthApplicationSystem.cs` for
   `NetworkEntityMap` — zero results.

---

### PACK-A001 — Fix AudioPerceptionSystem Split-Brain

**Design Reference:** DESIGN.md §7.B

**Scope:**

- Add `TargetHeardEventId = 4004` to
  `FDP/Toolkits/FDP.Toolkit.Perception/PerceptionConstants.cs`.
- Define `TargetHeardEvent` struct with `[EventId(PerceptionConstants.TargetHeardEventId)]`
  and fields `Entity Listener`, `int SourceEntityIndex`, `Vector3 Origin` in the
  `FDP.Toolkit.Perception.Events` namespace (new file or extend existing events file in
  that project).
- Purify `AudioPerceptionSystem`
  (`FDP/Toolkits/FDP.Toolkit.Perception/Systems/AudioPerceptionSystem.cs`):
  - Remove the `if (!World.HasComponent<TargetMemory>(listener)) continue;` guard (line 65).
  - Remove `World.GetComponentRW<TargetMemory>(listener)` and
    `TargetMemory.AddOrUpdateTarget(...)` calls (lines 76–82).
  - Replace with `_eventBus.Publish(new TargetHeardEvent { Listener = listener, SourceEntityIndex = evt.SourceEntityIndex, Origin = evt.Origin })`.
  - Accept `FdpEventBus` in the constructor if it does not already.
- Extend `ThreatEvaluationSystem`
  (`FDP/Toolkits/FDP.Toolkit.Perception/Systems/ThreatEvaluationSystem.cs`):
  - Add a consumption loop for `TargetHeardEvent` alongside the existing `TargetVisibleEvent`
    loop.
  - Call `TargetMemory.AddOrUpdateTarget(ref mem, entityId: heardEvt.SourceEntityIndex,
    posX: heardEvt.Origin.X, posY: heardEvt.Origin.Y, scoreBoost: 20f,
    modality: SensorModality.Acoustic)`.
- **Add network translators** for cross-node deployment:
  - Create `AudioTargetDetectedEgressTranslator` (Perception Node): catches `TargetHeardEvent`
    from the bus, writes a DDS `AudioTargetDetected` message.
  - Create `AudioTargetDetectedIngressTranslator` (Brain Node): receives the DDS message,
    publishes `TargetHeardEvent` onto the Brain node’s `FdpEventBus`.

**Out of Scope:**

- Changes to how `ThreatEvaluationSystem` handles `TargetVisibleEvent` (left as-is).

**Dependencies:** None — standalone change.

**New files / modified files:**

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Perception/PerceptionConstants.cs` | Add `TargetHeardEventId = 4004` |
| `FDP/Toolkits/FDP.Toolkit.Perception/Events/TargetHeardEvent.cs` (new) | New event struct with `int SourceEntityIndex` and `Vector3 Origin` |
| `FDP/Toolkits/FDP.Toolkit.Perception/Systems/AudioPerceptionSystem.cs` | Strip `TargetMemory` mutation; publish `TargetHeardEvent` |
| `FDP/Toolkits/FDP.Toolkit.Perception/Systems/ThreatEvaluationSystem.cs` | Add `TargetHeardEvent` consumption loop |
| `Hrot.SimHost/Translators/AudioTargetDetectedEgressTranslator.cs` (new) | Bus event → DDS |
| `Hrot.IG/Translators/AudioTargetDetectedIngressTranslator.cs` (new) | DDS → bus event |
| Related unit tests | Add tests verifying `TargetHeardEvent` publication |

**Constraints:**

- After this task, `AudioPerceptionSystem` must have zero references to `TargetMemory`,
  `GetComponentRW<TargetMemory>`, or `HasComponent<TargetMemory>`.
- `TargetMemory` must be mutated **only** by `ThreatEvaluationSystem` across the entire
  codebase.

**Success Conditions:**

1. **Unit test — audio fires event, not direct mutation:** Configure `AudioPerceptionSystem`
   with one listener and one audible source in range. Tick. Assert a `TargetHeardEvent` is on
   the bus with the correct `Listener`, `SourceEntityIndex`, and `Origin`. Assert `TargetMemory`
   was NOT mutated during this tick.
2. **Unit test — ThreatEvaluationSystem updates TargetMemory from TargetHeardEvent:** Publish
   `TargetHeardEvent { Listener = A, SourceEntityIndex = 42, Origin = ... }`. Tick
   `ThreatEvaluationSystem`. Assert `TargetMemory` of entity A contains an entry for index 42
   with non-zero score (acoustic modality).
3. **Compile gate:** Grep `AudioPerceptionSystem.cs` for `TargetMemory` — zero results.
4. **Regression:** Existing perception tests that exercise `ThreatEvaluationSystem` via
   `TargetVisibleEvent` still pass (that code path is unchanged).

---

### PACK-M003 — Remove DDS Structs from ECS Components (Mission Holders)

**Design Reference:** DESIGN.md §7.C

**Scope:**

- Define `DomainMissionTask` and `DomainMissionPlan` POCO classes in
  `FDP.Toolkit.Behavior.Components` (no `Hrot.NED` dependency). Fields for `DomainMissionTask`:
  `Guid TaskId`, `string ExecutingEngine`, `string BehaviorId`, `string BehaviorParams`.
  `DomainMissionPlan` holds `Guid ActiveTaskId` and `List<DomainMissionTask> Tasks`.
  Create a unified managed ECS component `ActiveMissionPlan` (with a `DomainMissionPlan Plan`
  property) in the same assembly.
- **Delete** `EntityMissionHolder` (`Hrot.SimHost/Components/EntityMissionHolder.cs`) and
  **delete** `IgMissionHolder` (`Hrot.IG/Components/IgMissionHolder.cs`). Register the unified
  `ActiveMissionPlan` managed component in `SimHostComponentRegistry` and `IgApplication`.
- Update `EntityMissionIngressTranslator` (`Hrot.SimHost/Translators/`): map `EntityMission`
  DDS struct → `DomainMissionPlan` POCO at the ingress boundary; write to `ActiveMissionPlan`.
- Update `IgMissionIngressTranslator` (`Hrot.IG/Translators/IgMissionIngressTranslator.cs`):
  same POCO mapping; write to `ActiveMissionPlan`.
- Update `EntityMissionEgressTranslator` (`Hrot.SimHost/Translators/`): read
  `ActiveMissionPlan.Plan`, map `DomainMissionPlan` → `EntityMission` DDS struct for
  publication.
- Update `MissionAdapterSystem` (`Hrot.SimHost/Systems/MissionAdapterSystem.cs`): query
  `ActiveMissionPlan` instead of the deleted `EntityMissionHolder`; access `plan.Plan.Tasks`
  and `plan.Plan.ActiveTaskId`. Evaluate removing `NetworkEntityMap` if only needed for DDS
  struct ID resolution.
- Update `MissionRenderLayer` (`Hrot.IG/Systems/MissionRenderLayer.cs`): query `ActiveMissionPlan`
  instead of the deleted `IgMissionHolder`; iterate `plan.Plan.Tasks` for waypoint rendering.
- Update all construction sites and tests that reference `EntityMissionHolder` or `IgMissionHolder`.

**Out of Scope:**

- Redesign of `MissionAdapterSystem` doctrine logic (only the component access pattern changes).
- Mission command network translators (PACK-P001 scope).

**Dependencies:** Coordinate with PACK-P001 if both tasks are active simultaneously —
both touch the mission component type used in `MissionAdapterSystem`.

**New files / modified files:**

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Behavior.Components/DomainMissionPlan.cs` (new) | `DomainMissionTask`, `DomainMissionPlan`, `ActiveMissionPlan` component |
| `Hrot.SimHost/Components/EntityMissionHolder.cs` | Deleted |
| `Hrot.IG/Components/IgMissionHolder.cs` | Deleted |
| `Hrot.SimHost/SimHostComponentRegistry.cs` | Register `ActiveMissionPlan` instead |
| `Hrot.IG/IgApplication.cs` | Register `ActiveMissionPlan` instead |
| `Hrot.SimHost/Translators/EntityMissionIngressTranslator.cs` | Map DDS struct → `DomainMissionPlan` POCO |
| `Hrot.IG/Translators/IgMissionIngressTranslator.cs` | Map DDS struct → `DomainMissionPlan` POCO |
| `Hrot.SimHost/Translators/EntityMissionEgressTranslator.cs` | Map `DomainMissionPlan` → DDS struct |
| `Hrot.SimHost/Systems/MissionAdapterSystem.cs` | Query `ActiveMissionPlan` |
| `Hrot.IG/Systems/MissionRenderLayer.cs` | Query `ActiveMissionPlan` |
| Related tests | Update all references to deleted holders |

**Constraints:**

- After this task, `EntityMissionHolder.cs` and `IgMissionHolder.cs` no longer exist in the
  codebase.
- `ActiveMissionPlan` and `DomainMissionPlan` must have no dependency on `Hrot.NED`,
  `CycloneDDS`, or any network-layer assembly.
- Field names in `DomainMissionTask` (`TaskId`, `ExecutingEngine`, `BehaviorId`,
  `BehaviorParams`) are the authoritative domain vocabulary; translators map DDS IDL names to
  these at the boundary.

**Success Conditions:**

1. **Compile gate — deleted holders, no NED leaks:** Verify `EntityMissionHolder.cs` and
   `IgMissionHolder.cs` no longer exist in the repo. Grep entire solution for
   `EntityMissionHolder|IgMissionHolder` — zero non-comment results.
2. **Unit test — ingress translator produces correct POCO:** Feed an `EntityMission` DDS sample
   with 2 tasks. Assert `ActiveMissionPlan.Plan.Tasks` has 2 entries with correct `BehaviorId`
   and `BehaviorParams`.
3. **Unit test — egress translator produces correct DDS struct:** Populate
   `ActiveMissionPlan.Plan.Tasks` with 2 `DomainMissionTask` instances. Assert the emitted
   `EntityMission` DDS struct contains matching task data.
4. **Integration test — MissionAdapterSystem resolves doctrine correctly:** With
   `ActiveMissionPlan.Plan` populated, tick `MissionAdapterSystem`. Assert doctrine state is
   updated identically to the pre-refactor behaviour.
5. **IG render test — MissionRenderLayer reads plan:** Set `ActiveMissionPlan.Plan` with
   waypoints. Tick `MissionRenderLayer`. Assert rendered waypoints match plan task data.
