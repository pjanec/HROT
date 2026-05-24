# BATCH-03 REPORT

## Summary

Batch implements **TASK-EQS-007** (four DDS translator bodies + three integration tests T8/T9/T10)
and **TASK-EQS-008** (`EqsQueryTemplate.cs` + four unit tests). One architectural gap discovered
in BATCH-02 was fixed as a prerequisite: `EqsResultUpdateSystem` was missing from `CgfLogicPack`.

---

## TASK-EQS-007 — DDS Translator Bodies + Integration Tests

### Files Modified / Created

**New file: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsResultUpdateEvent.cs`**
- Moved `EqsResultUpdateEvent` from `Hrot.SimHost.Systems` to `Fdp.Toolkit.Spatial.Eqs` to
  eliminate a circular dependency.
- Both `Hrot.Network.NED` (which needs the type in `EqsResultIngressTranslator`) and
  `Hrot.SimHost` (which uses it in `EqsResultUpdateSystem`) reference `Fdp.Toolkits`, so no
  circular dependency arises.

**Modified: `Hrot/Subsystems/Hrot.SimHost/Systems/EqsResultUpdateEvent.cs`**
- Replaced the class declaration with a redirect comment; the type now lives in
  `Fdp.Toolkit.Spatial.Eqs`.

**New implementation: `Hrot/Network/Hrot.Network.NED/SimHost/EqsSensorConfigEgressTranslator.cs`**
- Replaced the `throw new NotImplementedException()` stubs.
- Brain-side egress: scans entities with `EqsSensor` + `NetworkIdentity`; uses
  `SmartEgressUtil.ShouldPublish` / `MarkPublished` for dirty-flag gating;
  calls `_writer.DisposeInstance` in `Dispose(networkEntityId)`.

**New implementation: `Hrot/Network/Hrot.Network.NED/SimHost/EqsSensorConfigIngressTranslator.cs`**
- Muscle-side ingress: takes each `EqsSensorConfigTopic` DDS sample and applies/removes
  the `EqsSensor` component on the ghost entity.
- `NOT_ALIVE_DISPOSED` path: uses `DdsTypeSupport.FromNative<EqsSensorConfigTopic>(sample.NativePtr)`
  to extract the entity key from disposed samples (same pattern as
  `EntityMasterIngressTranslator`), then calls `cmd.RemoveComponent<EqsSensor>`.

**New implementation: `Hrot/Network/Hrot.Network.NED/SimHost/EqsResultEventEgressTranslator.cs`**
- Muscle-side egress: reads `EqsResultEvent` unmanaged events from the local bus,
  dereferences the `EqsResultPool` handle only when `EntryCount > 0` (zero-count events
  from the Phase 1 stub are published directly with an empty `Results` list; see design
  deviation #2 below).
- Publishes `EqsResultTopic` via `DdsWriter<EqsResultTopic>`.

**New implementation: `Hrot/Network/Hrot.Network.NED/CGF/EqsResultIngressTranslator.cs`**
- Brain-side ingress: receives `EqsResultTopic` DDS samples, maps `SensorNetworkId` back
  to the local CGF entity via `_entityMap`, publishes a managed `EqsResultUpdateEvent`
  onto the CGF world's event bus.

**Modified: `Hrot/Network/Hrot.Network.NED/SimHost/SimHostAuxiliaryTranslatorPack.cs`**
- Added `using Hrot.Network.NED.CGF;`.
- Registered four new translators:
  - Brain block: `EqsSensorConfigEgressTranslator`, `EqsResultIngressTranslator`.
  - Muscle block: `EqsSensorConfigIngressTranslator`, `EqsResultEventEgressTranslator`.

**Modified: `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs`**
- Added `using Hrot.SimHost.Systems;` and `simList.Add(new EqsResultUpdateSystem())`.
- **Reason:** `EqsResultUpdateSystem` was placed in `SimHostCoreLogicPack` in BATCH-02,
  which suffices for the offline (Editor) single-world path. The distributed (HrotRunnerHarness)
  Brain world requires the system to run on the CGF world's sim loop so it can consume
  `EqsResultUpdateEvent` managed events published by `EqsResultIngressTranslator`.
  This is a prerequisite for T9 to pass.

**New file: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsTranslatorTests.cs`**
- Three integration tests using `HrotRunnerHarness("simhost,cgf", domain)`.
- Domain range: 71-79 (free gap between SensorMechanism 60-69 and HrotRunnerHarness 100-145).

### Tests

| ID | Name | Result |
|----|------|--------|
| T8 | `EqsTranslators_T8_ConfigReplicatesBrainToMuscle` | PASS |
| T9 | `EqsTranslators_T9_ResultRoundTripPopulatesBrainBuffer` | PASS |
| T10 | `EqsTranslators_T10_EntityDestroyedRemovesSensorFromMuscle` | PASS |

### Design Deviations

**Deviation 1 — `EqsResultUpdateEvent` relocated to `Fdp.Toolkit.Spatial.Eqs`**
- The spec placed the type in `Hrot.SimHost.Systems`, but `Hrot.Network.NED` (which houses
  `EqsResultIngressTranslator`) must reference it. `Hrot.SimHost.csproj` already references
  `Hrot.Network.NED`, so the reverse reference would create a circular dependency.
- Resolution: moved to `Fdp.Toolkit.Spatial.Eqs` (within `Fdp.Toolkits` project), which is
  already referenced by both sides.
- Existing consumers (`EqsResultUpdateSystem`, `EqsResultUpdateSystemTests`) already had
  `using Fdp.Toolkit.Spatial.Eqs;` — no changes required in those files.

**Deviation 2 — Zero-count events published without pool access**
- The original `EqsResultEventEgressTranslator` checked `HasSingletonUnmanaged<EqsResultPool>()`
  at the top and returned early if absent. The Phase 1 stub solver emits events with
  `EntryCount = 0` and never registers the pool, causing the translator to silently swallow
  all events. Fixed by moving the pool check inside the `EntryCount > 0` branch.

**Deviation 3 — T10 uses entity destruction, not component removal**
- The spec says "remove EqsSensor from Brain entity" to trigger `NOT_ALIVE_DISPOSED` on
  Muscle. However, `translator.Dispose(networkEntityId)` (which calls `_writer.DisposeInstance`)
  is invoked by `CycloneNetworkCleanupSystem` only on full entity destruction, not on
  component removal. Removing `EqsSensor` alone only stops future egress publishes;
  it does not send a DDS dispose notification.
- T10 therefore destroys the entity via `DestroyEntityCommand` on the Brain world bus,
  which triggers the full cleanup path including `NOT_ALIVE_DISPOSED` for `EqsSensorConfig`.
  The assertion checks that the Muscle entity either leaves the entity map or loses `EqsSensor`.

**Deviation 4 — `EqsResultUpdateSystem` added to `CgfLogicPack`**
- BATCH-02 placed this system only in `SimHostCoreLogicPack`. For the distributed topology
  the system must also run on the CGF (Brain) world to consume `EqsResultUpdateEvent`
  managed events published by the ingress translator.
- Added `new EqsResultUpdateSystem()` to `CgfLogicPack.SimulationSystems`.

---

## TASK-EQS-008 — `EqsQueryTemplate.cs` + Unit Tests

### Files Created

**`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsQueryTemplate.cs`**
- Verbatim content from BATCH-03 instructions.
- Defines: `EqsTestPhase` enum, `IEqsGenerator`, `IEqsTest`, `EqsQueryTemplate` struct,
  `IEqsTemplateRegistry`, `EqsTemplateAttribute`, `EqsTemplateBase`.

**`FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/EqsQueryTemplateTests.cs`**
- Four pure unit tests, no ECS or DDS.

### Tests

| ID | Name | Result |
|----|------|--------|
| QT1 | `EqsTestPhase_ValuesAreCorrect` | PASS |
| QT2 | `EqsQueryTemplate_CanBeComposedWithTrivialGeneratorAndTest` | PASS |
| QT3 | `IEqsTemplateRegistry_TryGetTemplate_ReturnsFalseForUnknownId` | PASS |
| QT4 | `EqsTemplateAttribute_StoresAssetId` | PASS |

---

## Build Result

```
Build succeeded. 0 Error(s).
```

All pre-existing tests continue to pass. The 7 tests from BATCH-02 (Eqs subfolder) remain green.
