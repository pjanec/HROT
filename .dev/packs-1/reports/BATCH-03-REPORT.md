# BATCH-03 Report

## Status
COMPLETE

## Tasks Completed

- **PACK-P003:** Replaced all `long` shooter/target/hit network-entity IDs in `DetonationNotification`,
  `WeaponFireEvents` (`WeaponFireIntent`, `WeaponFireNotification`), and related structs with ECS
  `Entity` handles. Removed `NetworkEntityMap` from `HitResolutionSystem`, `AimAndFireExecutor`, and
  `FireProcessingSystem` constructors. Moved network-ID resolution entirely to egress translators
  (`MunitionDetonationEgressTranslator`, `WeaponFireNotificationEgressTranslator`). Updated
  `DamageCalculationSystem` to use `event.Target` (Entity). All callers, egress translators, ingress
  translators, and test files updated accordingly.

- **PACK-P001:** Split `MissionControlRequestSystem` (DDS + domain + JSON monolith) into three focused
  classes: `MissionControlIngressTranslator` (polls DDS, publishes `MissionControlIntent` via bus),
  `MissionControlAckEgressTranslator` (consumes `MissionControlAckEvent` structs, writes `MissionControlAck`
  DDS), and `MissionControlExecutionSystem` (pure domain logic — zero DDS/JSON references). New domain events
  (`MissionControlIntent` sealed class, `MissionControlAckEvent` unmanaged struct) placed in
  `Hrot.SimHost/Events/`. `MissionControlRequestSystem` retained but no longer wired. Existing DDS-based
  tests rewritten as fast ECS-only tests; 4 new `MissionControlExecutionSystemTests` added.

## Test Results

```
FDP.Toolkit.Combat.Tests:     Failed: 0, Passed: 52, Skipped: 0
FDP.Toolkit.Physics.Tests:    Failed: 0, Passed: 25, Skipped: 0
Hrot.SimHost.Tests:           Failed: 1, Passed: 421, Skipped: 0, Total: 422
  (1 pre-existing failure: GeoSpatialEgressTranslatorTests.Dispose_AlsoCallsBaseDispose — DDS timing flakiness, confirmed in HEAD before BATCH-03)

dotnet build IOS-IG-SimHost.sln: Build succeeded. 0 Error(s)
```

## Developer Insights

### Issues Encountered

1. **`replace_string_in_file` duplicate-append bug.** Multiple uses of the tool appended the old
   file content after the closing `}` brace of the new content, producing duplicate class
   declarations. Affected: `HitResolutionSystem.cs`, `AimAndFireExecutor.cs`,
   `FireProcessingSystem.cs`, `WeaponFireIntentEgressTranslator.cs`,
   `WeaponFireNotificationEgressTranslator.cs`, `HitResolutionSystemDetonationTests.cs`,
   `MunitionDetonationEgressTranslatorTests.cs`. Fixed by PowerShell brace-counting truncation.
   `AimAndFireExecutor.cs` was truncated to 7 lines by a bad truncation — recovered from
   `git show HEAD:...` and rewritten.

2. **Wrong `Entity` size assumption.** `Entity` struct has `sizeof = 8` (not 6, even with
   `[StructLayout(Sequential)]`), because the struct itself lacks `Pack=1`. Event struct size
   formulas that tried to use `6` failed the existing size-assertion tests; reverted to
   `2 * sizeof(long) + ...` which happens to equal the Entity-based formula.

3. **`NetworkEntityMap` removal missed callers.** After removing the map from
   `AimAndFireExecutor`, five additional call sites still passed `entityMap` as an argument
   (`NodeBootstrapper.cs`, `SimulationLogicModule.cs`, `HeadlessDemoApp.cs`,
   `UrbanCombatNewScenario.cs`, `ActionDispatchModuleTests.cs`). All required no-arg update.

4. **`FDP.Toolkit.Behavior` cannot reference `Hrot.NED`.** The spec said to put
   `MissionControlCqrsEvents.cs` in `FDP/Toolkits/FDP.Toolkit.Behavior/Events/`. But
   `MissionCommandUnion` is from `Hrot.NED.Messages` and `FDP.Toolkit.Behavior` does not (and
   should not) depend on `Hrot.NED`. Placed the events in `Hrot.SimHost/Events/` instead.

5. **`IDescriptorTranslator` interface requires full implementation.** The interface
   (`Fdp.Interfaces.IDescriptorTranslator`) demands `DescriptorOrdinal`, `TopicName`,
   `ScanAndPublish`, `PollIngress`, `ApplyToEntity`, and `Dispose(long)`. Only two were initially
   missing (`DescriptorOrdinal`, `TopicName`); added no-ops and ordinals 90/91.

6. **`IEntityCommandBuffer` has no `PublishManagedEvent`.** The interface only exposes
   `PublishEvent<T>() where T : unmanaged`. To publish the managed `MissionControlIntent` from
   `MissionControlIngressTranslator.PollIngress`, cast `view` to `EntityRepository` (same pattern
   used by `EntityMissionIngressTranslator`) and call `repo.Bus.PublishManaged(...)`.

7. **EventId 6001 conflict.** `TogglePerspectiveEvent` was already registered with ID 6001 in
   `SimHostEventIds`. The new `MissionControlAckEvent` initially used the same ID, causing a
   static type initializer exception at test time. Changed to 6002 and added
   `SimHostEventIds.MissionControlAck = 6002`.

### Weak Points Spotted

1. **`[EventId]` collisions are silent at design time.** The ID conflict only surfaces at runtime
   during static type initialization. There is no compile-time check or centralized registry that
   prevents two structs from claiming the same ID. A test that calls `EventType<T>.Id` for every
   registered event type on startup would catch this immediately.

2. **`MissionControlRequestSystem` is vestigial.** It is still compiled and present but no longer
   wired in `SimHostApp.cs`. It should be deleted in a follow-up batch to avoid confusion and
   maintain a clean codebase.

3. **Translator `Dispose(long)` contract is unclear.** The `IDescriptorTranslator.Dispose(long
   networkEntityId)` method is used for entity-death cleanup (e.g. destroying a DDS writer on
   entity disappearance). The new translators implement it as a no-op, which is correct for
   event-bus bridges (they are not per-entity), but there is no documentation on when to use it
   vs. not.

4. **`view as EntityRepository` cast is fragile.** The code in `EntityMissionIngressTranslator`
   (and now in `MissionControlIngressTranslator`) casts `ISimulationView` to `EntityRepository`
   to access `Bus.PublishManaged`. If the view is ever wrapped (e.g. in testing or replays),
   this cast returns `null` and the method silently becomes a no-op. `ISimulationView` should
   expose a `PublishManagedEvent<T>(T evt)` method or a `Bus` accessor.

5. **Test DDS domain-ID collisions.** The original `MissionControlRequestSystemTests.cs` used
   explicit domain IDs (152–162) for each DDS-bound test to avoid port conflicts. This pattern
   is fragile and requires manual ID tracking. Moving to bus-based tests (as done here) eliminates
   the problem entirely.

### Design Decisions Beyond Spec

1. **Events placed in `Hrot.SimHost/Events/` not `FDP.Toolkit.Behavior/`.** The spec said to
   define `MissionControlCqrsEvents` in `FDP.Toolkit.Behavior`. `MissionCommandUnion` is from
   `Hrot.NED.Messages`, which `FDP.Toolkit.Behavior` cannot reference (cross-layer dependency
   violation). Decision: place both events in `Hrot.SimHost/Events/` to preserve layer integrity.

2. **JSON parsing extracted to `MissionControlBehaviorParamsHelper`.** `System.Text.Json`
   references in `MissionControlExecutionSystem` would violate the spec's grep constraint.
   Rather than inline the JSON parsing into the ingress translator (which would merge two
   responsibilities), a static helper class was created to isolate the JSON concern.

3. **`MissionControlAckEvent` is an unmanaged struct (no `string? ErrorMessage`).** The spec
   draft included an `ErrorMessage` field.  `string` is not unmanaged, so the struct would need
   `[StructLayout]` workarounds or the event bus `PublishManaged` path. Decision: keep
   `MissionControlAckEvent` as a clean unmanaged struct (`Guid` + `int ErrorCode` + `long
   NewVersion`). The egress translator's `MapErrorMessage` method reconstructs the human-readable
   string from the error code when writing the DDS message.

4. **Existing DDS-based `MissionControlRequestSystemTests` fully rewritten as bus-based tests.**
   The spec said "update" the test files. All DDS infrastructure (`DdsParticipant`, `DdsWriter`,
   `DdsReader`, `Thread.Sleep`) was removed. Tests now use `TestHook_ProcessIntent` and check the
   bus (`repo.Bus.Consume<MissionControlAckEvent>()`), making them deterministic and ~10x faster.
   The `[Collection("SimHostDds")]` attribute was removed since no DDS is involved.

5. **`DescriptorOrdinal` values 90 (ingress) and 91 (egress) chosen.** These are above the
   highest existing SimHost ordinal (83 for `EntityHitDamageIngressTranslator`) and below the FDP
   toolkit range (201+). Added to a comment in the respective files.

### Unexpected Findings from Tests

1. **`sizeof(Entity) == 8`, not 6.** `Entity` has `int Index` (4 bytes) and `ushort Generation`
   (2 bytes), but without `Pack=1` on the struct itself, the runtime pads to 8 bytes. The
   event-struct size assertions (`CombatComponentTests`) caught this when initial estimates used
   size `6`. Final struct sizes are identical before and after the PACK-P003 change (Entity = same
   width as `long`).

2. **`ConsumeManaged<T>()` requires `SwapBuffers()` between publish and consume.** The
   `FdpEventBus` double-buffers both managed and unmanaged events. Tests that called
   `TestHook_ProcessIntent` and immediately iterated the bus received empty results until
   `repo.Bus.SwapBuffers()` was called between the publish and consume step. This is documented
   in `FdpEventBus` but was easy to miss.

3. **`GeoSpatialEgressTranslatorTests.Dispose_AlsoCallsBaseDispose` is genuinely pre-existing.**
   Confirmed by running the test against the HEAD commit before any BATCH-03 changes. The failure
   is intermittent DDS timing and unrelated to this batch.

## Files Changed

### PACK-P003 — Entity handles in combat/physics events
| File | Change |
|------|--------|
| `FDP/Toolkits/FDP.Toolkit.Combat.Contracts/DetonationNotification.cs` | `long` → `Entity Shooter/Target` |
| `FDP/Toolkits/FDP.Toolkit.Combat/Events/WeaponFireEvents.cs` | `long` → `Entity` in both structs |
| `FDP/Toolkits/FDP.Toolkit.Physics/Systems/HitResolutionSystem.cs` | Removed `NetworkEntityMap` |
| `FDP/Toolkits/FDP.Toolkit.Combat/Executors/AimAndFireExecutor.cs` | Removed `NetworkEntityMap` |
| `FDP/Toolkits/FDP.Toolkit.Combat/Systems/FireProcessingSystem.cs` | Removed `NetworkEntityMap` |
| `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageCalculationSystem.cs` | Uses `evt.Target` (Entity) |
| `FDP/Examples/Fdp.Examples.UrbanCombat/Systems/TelemetryReporterSystem.cs` | `.Shooter.Index` |
| `FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs` | No `entityMap` args |
| `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs` | No `entityMap` args |
| `FDP/Examples/Fdp.Examples.Scenarios/Physics/BallisticsAndHitScenario.cs` | Entity handles |
| `FDP/Toolkits/FDP.Toolkit.Combat.Tests/CombatComponentTests.cs` | Size formulas corrected |
| `FDP/Toolkits/FDP.Toolkit.Combat.Tests/AimAndFireExecutorTests.cs` | No `entityMap` |
| `FDP/Toolkits/FDP.Toolkit.Combat.Tests/FireProcessingSystemTests.cs` | No `entityMap` |
| `FDP/Toolkits/FDP.Toolkit.Physics.Tests/HitResolutionSystemDetonationTests.cs` | No `entityMap` |
| `FDP/Toolkits/FDP.Toolkit.Combat.Tests/DamageCalculationSystemTests.cs` | Entity-based assertions |
| `FDP/Examples/Fdp.Examples.UrbanCombat.Tests/UrbanAmbushIntegrationTests.cs` | Entity handles |
| `Hrot.SimHost/Network/Egress/MunitionDetonationEgressTranslator.cs` | `entityMap` + Entity→netId |
| `Hrot.SimHost/Network/Egress/WeaponFireIntentEgressTranslator.cs` | `evt.Shooter` authority |
| `Hrot.SimHost/Network/Egress/WeaponFireNotificationEgressTranslator.cs` | `entityMap` added |
| `Hrot.SimHost/Network/Ingress/MunitionDetonationIngressTranslator.cs` | netId→Entity |
| `Hrot.SimHost/Network/Ingress/WeaponFireRequestIngressTranslator.cs` | Entity handles in intent |
| `Hrot.SimHost/Modules/CombatModule.cs` | No `entityMap` for `FireProcessingSystem` |
| `Hrot.SimHost/NodeBootstrapper.cs` | `AimAndFireExecutor()` no arg |
| `Hrot.SimHost/Modules/SimulationLogicModule.cs` | `AimAndFireExecutor()` no arg |
| `Hrot.SimHost/SimHostApp.cs` | `entityMap` added to egress translators |
| `Hrot.SimHost.Tests/MunitionDetonationEgressTranslatorTests.cs` | Entity-based |
| `Hrot.SimHost.Tests/WeaponFireIntentEgressTranslatorTests.cs` | Entity-based |
| `Hrot.SimHost.Tests/MunitionDetonationIngressTranslatorTests.cs` | Entity assertions |
| `Hrot.SimHost.Tests/WeaponFireNotificationEgressTranslatorTests.cs` | `entityMap` added |
| `Hrot.SimHost.Tests/WeaponFireRequestIngressTranslatorTests.cs` | Entity assertions |
| `Hrot.SimHost.Tests/ActionDispatchModuleTests.cs` | `AimAndFireExecutor()` no arg |

### PACK-P001 — MissionControlRequestSystem split
| File | Change |
|------|--------|
| `Hrot.SimHost/Events/MissionControlCqrsEvents.cs` | **NEW** — `MissionControlIntent` + `MissionControlAckEvent` |
| `Hrot.SimHost/Network/Ingress/MissionControlIngressTranslator.cs` | **NEW** — DDS poll → bus publish |
| `Hrot.SimHost/Network/Egress/MissionControlAckEgressTranslator.cs` | **NEW** — bus consume → DDS write |
| `Hrot.SimHost/Systems/MissionControlBehaviorParamsHelper.cs` | **NEW** — JSON parsing isolated |
| `Hrot.SimHost/Systems/MissionControlExecutionSystem.cs` | **NEW** — pure domain logic |
| `Hrot.SimHost/SimHostComponentRegistry.cs` | `RegisterEvent<MissionControlAckEvent>()` added |
| `Hrot.SimHost/SimHostApp.cs` | New 3-part wiring; `MissionControlRequestSystem` removed |
| `Hrot.SimHost/SimHostEvents.cs` | `MissionControlAck = 6002` constant added |
| `Hrot.SimHost.Tests/MissionControlRequestSystemTests.cs` | Rewritten — DDS removed, bus-based |
| `Hrot.SimHost.Tests/Systems/MissionControlRequestSystemFollowRouteTests.cs` | `MissionControlExecutionSystem` + `TestHook_ProcessIntent` |
| `Hrot.SimHost.Tests/Systems/MissionControlExecutionSystemTests.cs` | **NEW** — 4 unit tests (SC-1…SC-4) |
| `Hrot.SimHost.Tests/SimHostAppTests.cs` | `MissionControlExecutionSystem` (no DDS participant arg) |
