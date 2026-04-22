# Task Details: Network Architecture Cleanup and Module Phase Manual

**Reference design:** See [DESIGN.md](./DESIGN.md) for architecture context and rationale.

All task IDs use prefix `MPM` (Module Phase Manual).

---

## Phase 1: Dead Code Purge

### MPM-P1-T01 - Delete Legacy Perception Systems

**Design reference:** [DESIGN.md § Phase 1.1](./DESIGN.md#11-delete-legacy-perception-systems)

**Scope:**
Delete two dead wrapper systems that were replaced by `AutonomousPerceptionModule` and are explicitly excluded from registration in `CombatModule.cs`.

**Files to delete:**
- `Hrot/Subsystems/Hrot.SimHost/Systems/PerceptionBroadphaseSystem.cs`
- `Hrot/Subsystems/Hrot.SimHost/Systems/ThreatEvaluationAdapterSystem.cs`

**Files to modify:**
- `Hrot/Subsystems/Hrot.SimHost/Modules/CombatModule.cs`:
  - Remove the comment block: `// PerceptionBroadphaseSystem and ThreatEvaluationAdapterSystem are intentionally not registered here...`
  - Remove any now-unused `using` directives that only imported the deleted system namespaces.

**Success conditions:**
- The two `.cs` files no longer exist.
- The solution builds without errors (`dotnet build`).
- No code in `Hrot.SimHost` references `PerceptionBroadphaseSystem` or `ThreatEvaluationAdapterSystem`.

---

### MPM-P1-T02 - Delete INetworkReplayTarget and Strip from Translators

**Design reference:** [DESIGN.md § Phase 1.2](./DESIGN.md#12-delete-networkreplaysystem-and-inetworkreplaytarget)

**Scope:**
`INetworkReplayTarget` is a dead interface whose only consumer (`NetworkReplaySystem`) is in the NetworkDemo (being deleted in MPM-P1-T03). Strip it from all surviving base translator classes.

**File to delete:**
- `FDP/Network/Fdp.Network.Cyclone/Abstractions/INetworkReplayTarget.cs`

**Files to modify (remove `: INetworkReplayTarget` from class declaration, delete `InjectReplayData` method):**
- `FDP/Network/Fdp.Network.Cyclone/Translators/CycloneTranslator.cs`
- `FDP/Network/Fdp.Network.Cyclone/Translators/CycloneNativeEventTranslator.cs`
- `FDP/Network/Fdp.Network.Cyclone/Translators/CycloneManagedEventTranslator.cs`
- `FDP/Network/Fdp.Network.Cyclone/Translators/MultiInstanceCycloneTranslator.cs`

**Additional change in `CycloneNativeEventTranslator.cs`:**
Remove the hack line `DescriptorOrdinal = topicName.GetHashCode();` from the constructor. The `DescriptorOrdinal` property itself stays for now (it is still required by `IDescriptorTranslator` until Phase 3 changes the interface).

**Success conditions:**
- `INetworkReplayTarget.cs` file no longer exists.
- Searching the codebase for `INetworkReplayTarget` yields zero results (excluding NetworkDemo which is deleted in T03).
- `CycloneNativeEventTranslator` no longer assigns `DescriptorOrdinal = topicName.GetHashCode()`.
- Solution builds without errors.
- Existing unit tests in `Fdp.Network.Cyclone.Tests` for `CycloneTranslatorTests` and `CycloneManagedEventTranslatorTests` still pass.

---

### MPM-P1-T03 - Delete AutoCycloneTranslators, ReplicationBootstrap, and NetworkDemo

**Design reference:** [DESIGN.md § Phase 1.3](./DESIGN.md#13-delete-autocyclonetranslators-replicationbootstrap-and-networkdemo)

**Scope:**
Delete the ACL-violating auto-translators and the entire NetworkDemo project that uses them.

**Files/directories to delete:**
- `FDP/Network/Fdp.Network.Cyclone/Translators/AutoCycloneTranslator.cs`
- `FDP/Network/Fdp.Network.Cyclone/Translators/ManagedAutoCycloneTranslator.cs`
- `FDP/Network/Fdp.Network.Cyclone/ReplicationBootstrap.cs`
- `FDP/Engine/Fdp.Core/Abstractions/FdpDescriptorAttribute.cs`
- Entire `FDP/Examples/Fdp.Examples.NetworkDemo/` directory
- Entire `FDP/Examples/Fdp.Examples.NetworkDemo.Tests/` directory
- `FDP/Network/Fdp.Network.Cyclone.Tests/Translators/AutoCycloneTranslatorTests.cs`

**Solution files to update:**
- `FDP/FDP.sln`: Remove project entries for `Fdp.Examples.NetworkDemo` and `Fdp.Examples.NetworkDemo.Tests`.
- `IOS-IG-SimHost.sln`: Remove project entries for `Fdp.Examples.NetworkDemo` and `Fdp.Examples.NetworkDemo.Tests`.

**Success conditions:**
- All listed files/directories no longer exist in the filesystem.
- `FDP/FDP.sln` and `IOS-IG-SimHost.sln` no longer reference the deleted projects.
- Searching the entire codebase for `AutoCycloneTranslator`, `ManagedAutoCycloneTranslator`, `ReplicationBootstrap`, `[FdpDescriptor`, `FdpDescriptorAttribute` yields zero results.
- Full solution builds without errors.
- No test references the deleted test file.

---

## Phase 2: Descriptor Ordinal Cleanup

### MPM-P2-T01 - Extend EDescriptorType Enum

**Design reference:** [DESIGN.md § Phase 2.1](./DESIGN.md#21-extend-edescriptortype-for-the-ned-domain)

**Scope:**
Add all missing NED translator ordinals to the `EDescriptorType` enum so every NED translator can reference a named constant.

**File to modify:**
- `Hrot/Network/Hrot.Network.NED/AllDescriptors.cs`

Add the following enum values (preserve existing values unchanged):
- `dtMapEntitySymbol = 40`
- `dtSensorConfig = 60`
- `dtRaycastRequestBatch = 61`
- `dtSensorTrackState = 62`
- `dtRaycastResponseBatch = 63`
- `dtPathRequestBatch = 64`
- `dtPathResponseBatch = 65`
- `dtGroundClampingOverride = 66`
- `dtWeaponFireRequest = 80`
- `dtWeaponFire = 81`
- `dtMunitionDetonation = 82`
- `dtEntityHitDamage = 83`
- `dtAudioTargetDetected = 84`
- `dtMissionControlRequest = 90`
- `dtMissionControlAck = 91`

**Success conditions:**
- `EDescriptorType` contains all values listed above in addition to the existing ones.
- No existing numeric values are changed.
- Solution builds without errors.

---

### MPM-P2-T02 - Fix NED Translator Magic Ordinals

**Design reference:** [DESIGN.md § Phase 2.2](./DESIGN.md#22-fix-ned-translators-still-using-magic-ordinals)

**Scope:**
Replace remaining magic integer literals in NED translators with named enum references.

**Specific files to modify:**
- `Hrot/Network/Hrot.Network.NED/Replication/Map/Ingress/EntityMissionIngressTranslator.cs`: Change `DescriptorOrdinal => 50` to `DescriptorOrdinal => (long)EDescriptorType.dtEntityMission` (value 51).
- `Hrot/Network/Hrot.Network.NED/Replication/Map/Ingress/EntityMasterIngressTranslator.cs`: Change `OrdinalValue = -2` to `OrdinalValue = (long)EDescriptorType.dtEntityMaster` (value 0).
- `Hrot/Network/Hrot.Network.NED/Replication/Map/Ingress/MapEntitySymbolIngressTranslator.cs`: Change `OrdinalValue = 40` to `OrdinalValue = (long)EDescriptorType.dtMapEntitySymbol`.

**Additional sweep:**
Scan all remaining files under `Hrot/Network/Hrot.Network.NED/Replication/` for any translator that still uses a raw integer literal for `DescriptorOrdinal` or `OrdinalValue`. Update each one to reference the corresponding `EDescriptorType` member added in MPM-P2-T01.

**Ordinal collision pre-check (required before changing EntityMasterIngressTranslator):**

Before changing `-2` to `0`, verify that the shared ordinal does not cause a startup crash:
1. Confirm `CycloneIngressSystem`, `CycloneEgressSystem`, and `CycloneNetworkCleanupSystem` store translators as plain `IDescriptorTranslator[]` arrays (not `Dictionary<long, IDescriptorTranslator>`). If this holds, no `KeyAlreadyExistsException` can occur.
2. Check `NedReplicationModule.PopulateDescriptorOwnershipMap`. It calls `_descriptorOwnershipMap.RegisterFromTranslator(t.DescriptorOrdinal, t.TargetComponentIds)` for every translator. Confirm `DescriptorOwnershipMap._descriptorToComponentIds` uses indexer assignment (`[key] = value`), not `.Add()`. A second write with ordinal `0` will silently overwrite the first; this is safe only if `EntityMasterIngressTranslator.TargetComponentIds` and `EntityMasterEgressTranslator.TargetComponentIds` return the same component IDs. Verify this equality before merging.

**Success conditions:**
- Searching `Hrot.Network.NED` for `OrdinalValue = [0-9]` or `DescriptorOrdinal => [0-9]` yields zero matches.
- `EntityMissionIngressTranslator.DescriptorOrdinal` equals 51 at runtime.
- `EntityMasterIngressTranslator.DescriptorOrdinal` equals 0 at runtime.
- Collision pre-check is documented in the batch report.
- Solution builds without errors.
- Existing NED descriptor tests pass.

---

### MPM-P2-T03 - Create TimeDescriptorType Enum and Update Time Translators

**Design reference:** [DESIGN.md § Phase 2.3](./DESIGN.md#23-create-timedescriptortype-for-the-fdp-time-toolkit)

**Scope:**
Create a domain-owned enum for the FDP time toolkit's network ordinals. Update all five time translators to use named constants.

**New file:** `FDP/Toolkits/Fdp.Toolkits/Time/TimeDescriptorType.cs`

```csharp
namespace Fdp.Toolkit.Time
{
    public enum TimeDescriptorType
    {
        SwitchTimeModeEvent = 201,
        MasterFrameOrder    = 202,
        SlaveFrameOrder     = 203,
        TimeSyncRequest     = 205,
        TimeSyncResponse    = 206
    }
}
```

**Files to modify:**
- `FDP/Toolkits/Fdp.Toolkits/Time/Translators/SwitchTimeModeDescriptorTranslator.cs`: `OrdinalValue = 201` → `OrdinalValue = (long)TimeDescriptorType.SwitchTimeModeEvent`
- `FDP/Toolkits/Fdp.Toolkits/Time/Translators/MasterLockstepTranslator.cs`: `OrdinalValue = 202` → `OrdinalValue = (long)TimeDescriptorType.MasterFrameOrder`
- `FDP/Toolkits/Fdp.Toolkits/Time/Translators/SlaveLockstepTranslator.cs`: `OrdinalValue = 203` → `OrdinalValue = (long)TimeDescriptorType.SlaveFrameOrder`
- `FDP/Toolkits/Fdp.Toolkits/Time/Translators/MasterTimeSyncTranslator.cs`: `OrdinalValue = 205` → `OrdinalValue = (long)TimeDescriptorType.TimeSyncRequest`
- `FDP/Toolkits/Fdp.Toolkits/Time/Translators/SlaveTimeSyncTranslator.cs`: `OrdinalValue = 206` → `OrdinalValue = (long)TimeDescriptorType.TimeSyncResponse`

**Success conditions:**
- `TimeDescriptorType.cs` file exists in the correct location.
- Searching `FDP/Toolkits/Fdp.Toolkits/Time/` for `OrdinalValue = 20` or `OrdinalValue = [0-9]` yields zero matches in translator files.
- The numeric values produced at runtime are unchanged (201, 202, 203, 205, 206).
- Existing time toolkit tests (`Fdp.Toolkits.Tests/Time/`) pass without modification.
- Solution builds without errors. `TimeDescriptorType` must not reference `Hrot.NED.Descriptors`.

---

### MPM-P2-T04 - Create BdcDescriptorType Enum and Update BDC Translators

**Design reference:** [DESIGN.md § Phase 2.4](./DESIGN.md#24-create-bdcdescriptortype-for-the-bdc-domain)

**Scope:**
Create a domain-owned enum for BDC network ordinals. Update the two BDC translators.

**New file:** `Hrot/Network/Hrot.Network.BDC/BdcDescriptorType.cs`

```csharp
namespace Hrot.BDC
{
    public enum BdcDescriptorType
    {
        EntityMaster = 1000,
        WorldPos     = 1002
    }
}
```

**Files to modify:**
- `Hrot/Network/Hrot.Network.BDC/Replication/BdcEntityMasterTranslator.cs`: `DescriptorOrdinal => 1000` → `DescriptorOrdinal => (long)BdcDescriptorType.EntityMaster`
- `Hrot/Network/Hrot.Network.BDC/Replication/BdcWorldPosTranslator.cs`: `DescriptorOrdinal => 1002` → `DescriptorOrdinal => (long)BdcDescriptorType.WorldPos`

**Success conditions:**
- `BdcDescriptorType.cs` file exists in the correct location.
- Searching `Hrot/Network/Hrot.Network.BDC/` for `=> 1000` or `=> 1002` yields zero matches in translator files.
- Numeric values at runtime remain 1000 and 1002.
- Solution builds without errors. `BdcDescriptorType` must not reference `Hrot.NED.Descriptors`.

---

## Phase 3: Network Interface Segregation

### MPM-P3-T01 - Create INetworkTranslator Base Interface

**Design reference:** [DESIGN.md § Phase 3.1](./DESIGN.md#31-create-inetworktranslator-base-interface)

**Scope:**
Introduce the root `INetworkTranslator` interface. This is a purely additive step with no behavioral changes.

**New file:** `FDP/Engine/Fdp.Core/Abstractions/INetworkTranslator.cs`

Content: `TopicName`, `Direction`, `ReceivedSampleCount`, `SentSampleCount`, `PollIngress`, `ScanAndPublish` as specified in [DESIGN.md § 3.1](./DESIGN.md#31-create-inetworktranslator-base-interface).

**Success conditions:**
- File exists at the specified path.
- Namespace is `Fdp.Interfaces`.
- Interface compiles without errors.
- No existing code is changed yet (this is a pure addition).

---

### MPM-P3-T02 - Refactor IDescriptorTranslator to Extend INetworkTranslator

**Design reference:** [DESIGN.md § Phase 3.2](./DESIGN.md#32-refactor-idescriptortranslator-to-extend-inetworktranslator)

**Scope:**
Make `IDescriptorTranslator` extend `INetworkTranslator` and remove the duplicated members.

**File to modify:** `FDP/Engine/Fdp.Core/Abstractions/IDescriptorTranslator.cs`

- Add `: INetworkTranslator` to the interface declaration.
- Remove from `IDescriptorTranslator`: `TopicName`, `Direction`, `ReceivedSampleCount`, `SentSampleCount`, `PollIngress`, `ScanAndPublish` (they are now inherited).
- Keep: `DescriptorOrdinal`, `TargetComponentIds`, `ApplyToEntity`, `Dispose`.

**Success conditions:**
- `IDescriptorTranslator` extends `INetworkTranslator`.
- All current implementors of `IDescriptorTranslator` (`CycloneTranslator<>` and its subclasses) still compile without modification because they already implement the underlying methods.
- Existing tests pass.
- Downstream code that casts to `IDescriptorTranslator` still compiles.

---

### MPM-P3-T03 - Extract CycloneBaseTranslator and Switch Event Translator Interfaces

**Design reference:** [DESIGN.md § Phase 3.0 - 3.4](./DESIGN.md#30-extract-cyclonebasetranslator-prerequisite)

**Scope:**
This task has two sequential steps:

**Step A — Extract `CycloneBaseTranslator` (prerequisite to Step B)**

`CycloneTranslator`, `CycloneNativeEventTranslator`, and `CycloneManagedEventTranslator` are sibling classes (no inheritance between them). After the interface changes, all three must implement `INetworkTranslator`. To avoid duplicating the shared implementation (`TopicName`, `Direction`, `ReceivedSampleCount`, `SentSampleCount`, and the constructor plumbing), extract a common abstract base class:

**New file:** `FDP/Network/Fdp.Network.Cyclone/Translators/CycloneBaseTranslator.cs`

- Implements `INetworkTranslator`.
- Carries the non-generic shared members: `TopicName`, `Direction (abstract)`, `ReceivedSampleCount`, `SentSampleCount`.
- Because each translator is generic with its own `TDds` type, `Reader`/`Writer` construction must remain in the derived classes; only the non-generic `INetworkTranslator` members live in the base.
- `CycloneTranslator<TDds,TView>` and the event translators change their base from `(none)` to `CycloneBaseTranslator`.

**Step B — Create `INetworkEventTranslator` and switch event translator interfaces**

**New file:** `FDP/Engine/Fdp.Core/Abstractions/INetworkEventTranslator.cs`

Content: empty marker interface extending `INetworkTranslator` with XML doc as specified in [DESIGN.md § 3.3](./DESIGN.md#33-create-inetworkeventtranslator-marker-interface).

**Files to modify:**
- `FDP/Network/Fdp.Network.Cyclone/Translators/CycloneNativeEventTranslator.cs`:
  - Change `: IDescriptorTranslator` to `: CycloneBaseTranslator, INetworkEventTranslator`.
  - Remove `DescriptorOrdinal` property (the `topicName.GetHashCode()` assignment was removed in Phase 1).
  - Remove `TargetComponentIds`, `ApplyToEntity`, `Dispose` members (no meaning for transient events).
- `FDP/Network/Fdp.Network.Cyclone/Translators/CycloneManagedEventTranslator.cs`:
  - Apply the same changes.

**Concrete subclass:** `FireInteractionEventTranslator` inherits from `CycloneNativeEventTranslator`. After this change it automatically satisfies `INetworkEventTranslator`. No change to its file.

**Success conditions:**
- `CycloneBaseTranslator.cs` exists; `INetworkEventTranslator.cs` exists.
- `CycloneNativeEventTranslator` and `CycloneManagedEventTranslator` extend `CycloneBaseTranslator` and implement `INetworkEventTranslator`.
- `CycloneTranslator` extends `CycloneBaseTranslator` and still satisfies `IDescriptorTranslator`.
- Neither event translator class exposes `DescriptorOrdinal`, `ApplyToEntity`, or `Dispose`.
- `FireInteractionEventTranslator` compiles and satisfies `INetworkEventTranslator`.
- Existing `CycloneManagedEventTranslatorTests` pass.
- Solution builds without errors.

---

### MPM-P3-T04 - Update Ingress/Egress Systems and Diagnostic Panel

**Design reference:** [DESIGN.md § Phase 3.5 - 3.6](./DESIGN.md#35-update-cyclonenetworkingresssystem-and-cycloneegresssystem)

**Scope:**
Update the systems that iterate over translator collections to use `INetworkTranslator` where only `PollIngress`/`ScanAndPublish` are needed. Remove the `GetDirectionLabel` string-matching hack.

**Files to modify:**
- `FDP/Network/Fdp.Network.Cyclone/Systems/CycloneNetworkIngressSystem.cs`: Change constructor/field type from `IDescriptorTranslator[]` to `INetworkTranslator[]`.
- `FDP/Network/Fdp.Network.Cyclone/Systems/CycloneEgressSystem.cs` (or equivalent egress system): Same change.
- `FDP/Engine/Fdp.Presentation/ImGui/Panels/ArchitectureDiagnosticsPanel.cs`:
  - Delete the `GetDirectionLabel(string systemName)` method (lines ~274-end of method).
  - In `EnumerateTranslatorRows`, replace `var direction = GetDirectionLabel(system.GetType().Name)` with `translator.Direction.ToString()`.
  - Ensure `EnumerateTranslatorRows` iterates over `INetworkTranslator` (to include both descriptor and event translators).

**Success conditions:**
- `GetDirectionLabel` method no longer exists in `ArchitectureDiagnosticsPanel.cs`.
- `CycloneNetworkIngressSystem` and the egress system accept `INetworkTranslator[]`.
- `CycloneNetworkCleanupSystem` still accepts `IDescriptorTranslator[]`.
- The diagnostics panel correctly shows direction for all translator types without string-matching.
- Solution builds without errors. All Cyclone system tests pass.

---

## Phase 4: SystemPhase.Manual

### MPM-P4-T01 - Add SystemPhase.Manual to Enum

**Design reference:** [DESIGN.md § Phase 4.1](./DESIGN.md#41-add-manual-to-systemphase-enum)

**Scope:**
Add `Manual = 255` to the `SystemPhase` enum and update `ExecutePhase` to skip it.

**File to modify:** `FDP/Engine/Fdp.ModuleHost/Abstractions/SystemPhase.cs`

Add `Manual = 255` with the XML documentation as specified in the design.

**File to modify:** `FDP/Engine/Fdp.ModuleHost/Scheduling/SystemScheduler.cs`

In `ExecutePhase(SystemPhase phase, ...)`, add a guard at the top:
```csharp
if (phase == SystemPhase.Manual) return;
```

**Success conditions:**
- `SystemPhase.Manual` equals `255`.
- Calling `scheduler.ExecutePhase(SystemPhase.Manual, view, dt)` does nothing (no system executes).
- Existing phase execution tests pass.
- Solution builds without errors.

---

### MPM-P4-T02 - Add RegisterManualSystem to ISystemRegistry and Implement in SystemScheduler

**Design reference:** [DESIGN.md § Phase 4.2 - 4.3](./DESIGN.md#42-add-registermanualsystem-to-isystemregistry)

**Scope:**
Extend the `ISystemRegistry` interface and provide a concrete implementation in `SystemScheduler` that tracks execution time for manually-ticked systems via a profiled wrapper.

**File to modify:** `FDP/Engine/Fdp.ModuleHost/Abstractions/ISystemRegistry.cs`

Add `RegisterManualSystem<T>` method signature as specified in the design.

**File to modify:** `FDP/Engine/Fdp.ModuleHost/Scheduling/SystemScheduler.cs`

1. Implement `RegisterManualSystem<T>`: call `RegisterSystem(system)` (which puts it in phase `Manual`), then return a `new ProfiledManualSystemWrapper(system, this)`.
2. Add the private `ProfiledManualSystemWrapper` nested class that calls `GetProfileData(_inner)`, starts a `Stopwatch`, calls `_inner.Execute(view, deltaTime)`, stops the watch, and calls `profile?.RecordExecution(ms)`.

**Success conditions:**
- `ISystemRegistry` has `RegisterManualSystem<T>` in its definition.
- `SystemScheduler.RegisterManualSystem<T>(system)` registers the system under `SystemPhase.Manual` in `_systemsByPhase` and `_profileData`.
- The returned wrapper calls `RecordExecution` on the profile after each `Execute` call.
- Unit test: Register a mock system via `RegisterManualSystem`. Assert that:
  - `GetProfileData(mockSystem)` returns non-null data.
  - `ExecutePhase(SystemPhase.Manual, ...)` does NOT execute the mock system.
  - Calling `wrapper.Execute(view, dt)` DOES execute the inner system and records elapsed time.

---

### MPM-P4-T03 - Update CapturingSystemRegistry in ModuleHostKernel

**Design reference:** [DESIGN.md § Phase 4.4](./DESIGN.md#44-update-capturingsystemregistry-in-modulehostkernel)

**Scope:**
The private `CapturingSystemRegistry` inside `ModuleHostKernel` wraps `SystemScheduler`. Implement `RegisterManualSystem` on it.

**File to modify:** `FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs`

In the `CapturingSystemRegistry` nested class (~line 1815):
- Add `RegisterManualSystem<T>` that calls `Captured.Add(system)` and then returns `_scheduler.RegisterManualSystem(system)`.

**Success conditions:**
- `CapturingSystemRegistry` satisfies the updated `ISystemRegistry` interface (compiles).
- When a module calls `registry.RegisterManualSystem(system)`, the system appears in the kernel's captured list AND in the scheduler's `_profileData` dictionary.
- Integration test: Register a module with one manual system. Verify that `kernel.SystemScheduler.GetProfileData(system)` returns non-null and the system does NOT execute during normal kernel phase execution.

---

### MPM-P4-T04 - Tag Perception Systems with [UpdateInPhase(SystemPhase.Manual)]

**Design reference:** [DESIGN.md § Phase 4.5](./DESIGN.md#45-tag-the-four-perception-systems-with-updateinphasesystephasemanual)

**Scope:**
`SystemScheduler.RegisterSystem` reads `[UpdateInPhase(...)]` to determine a system's phase. Without this attribute, the scheduler throws. Tag the four perception systems.

**Files to modify** (add `[UpdateInPhase(SystemPhase.Manual)]` attribute):
- `FDP/Toolkits/Fdp.Toolkits/Perception/Systems/LocalGridBuilderSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits/Perception/Systems/VisionBroadphaseSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits/Perception/Systems/LosRequestBatchingSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits/Perception/Systems/SensorTrackDebounceSystem.cs`

**Success conditions:**
- Each of the four files has `[UpdateInPhase(SystemPhase.Manual)]` on the class declaration.
- `SystemScheduler.RegisterSystem` does NOT throw when called with these system instances.
- Solution builds without errors.

---

### MPM-P4-T05 - Refactor AutonomousPerceptionModule

**Design reference:** [DESIGN.md § Phase 4.6](./DESIGN.md#46-refactor-autonomousperceptionmodule)

**Scope:**
Replace the "hidden systems" pattern with first-class kernel registration using `RegisterManualSystem`. After this change, all four perception systems appear in `ArchitectureDiagnosticsPanel` under the `Manual` phase.

**File to modify:** `FDP/Toolkits/Fdp.Toolkits/Perception/Modules/AutonomousPerceptionModule.cs`

Changes:
1. Change the four system field types from concrete system types to `IEcsModuleSystem`.
2. Initialize them to `null!` rather than in the constructor.
3. In `RegisterSystems`, call `registry.RegisterManualSystem(new ConcreteSystem(...))` for each and store the returned wrapper.
4. In `Tick`, call the stored `IEcsModuleSystem` wrappers instead of concrete fields. The bus swaps between calls remain unchanged.
5. Remove the concrete system instantiations from the constructor.

**Wrapper casting rule:** `RegisterManualSystem` returns a `ProfiledManualSystemWrapper`. The stored `IEcsModuleSystem` field must **never** be downcast to the concrete type (`(LocalGridBuilderSystem)_localGridBuilder` etc.). If code outside `Tick` — such as setup logic or tests — needs to call a method specific to the concrete system, use the **dual-reference pattern**: hold a separate `readonly` field of the concrete type before passing to `RegisterManualSystem`:

```csharp
private LocalGridBuilderSystem _localGridBuilderRaw = null!;  // for direct access
private IEcsModuleSystem       _localGridBuilder    = null!;  // profiled wrapper for Execute()

_localGridBuilderRaw = new LocalGridBuilderSystem(_localGrid);
_localGridBuilder    = registry.RegisterManualSystem(_localGridBuilderRaw);
```

In the current module only `Execute(scopedView, dt)` is called on each system inside `Tick`, so the dual-reference pattern is **not required** for this task. Apply it only if a future change needs to access inner-system properties or methods directly.

**File to modify:** `Hrot/Subsystems/Hrot.SimHost/SimHostCoreLogicPack.cs`

`AutonomousPerceptionModule` is NOT registered directly with the kernel — it is a private field inside `SimHostCoreLogicPack`, which IS the registered module. For `AutonomousPerceptionModule.RegisterSystems` to receive the kernel's registry (so `RegisterManualSystem` calls reach the scheduler), `SimHostCoreLogicPack.RegisterSystems` must forward the call:

```csharp
public void RegisterSystems(ISystemRegistry registry)
{
    // ... existing registrations ...
    _perceptionModule.RegisterSystems(registry); // forward to inner module
}
```

Without this forwarding call, `AutonomousPerceptionModule.RegisterSystems` is never invoked and the systems are never added to the kernel's scheduler.

**Success conditions:**
- `AutonomousPerceptionModule.RegisterSystems` calls `registry.RegisterManualSystem` four times.
- `AutonomousPerceptionModule.Tick` calls `Execute` on the stored `IEcsModuleSystem` wrappers.
- `SimHostCoreLogicPack.RegisterSystems` calls `_perceptionModule.RegisterSystems(registry)`.
- After kernel registration, `kernel.SystemScheduler.GetProfileData` returns non-null data for each of the four perception systems.
- The `ArchitectureDiagnosticsPanel` shows all four systems in the `Manual` phase bucket.
- Existing perception module integration tests pass.
- Solution builds without errors.

---

## Phase 5: Doctrine Auto-Registration

### MPM-P5-T01 - Create DoctrineCategory and DoctrineContractAttribute

**Design reference:** [DESIGN.md § Phase 5.1](./DESIGN.md#51-define-doctrinecategory-and-doctrinecontractattribute)

**Scope:**
Introduce the two new types in `Hrot.Core` that anchor all doctrine metadata.

**New files:**
- `Hrot/Engine/Hrot.Core/MapDefinitions/Doctrine/DoctrineCategory.cs`
- `Hrot/Engine/Hrot.Core/MapDefinitions/Doctrine/DoctrineContractAttribute.cs`

Contents as specified in [DESIGN.md § 5.1](./DESIGN.md#51-define-doctrinecategory-and-doctrinecontractattribute).

**Success conditions:**
- Both files exist and compile without errors.
- `DoctrineCategory` is a `[Flags]` enum with values: `None=0`, `Civilian=1`, `MilitaryApc=2`, `Infantry=4`, `Insurgent=8`, `AllMilitary=14`.
- `DoctrineContractAttribute` is `AttributeUsage(AttributeTargets.Class, Inherited=false, AllowMultiple=false)`.
- Unit test: Decorate a dummy class with the attribute. Verify `GetCustomAttribute<DoctrineContractAttribute>()` returns the correct `DoctrineId`, `BehaviorId`, and `ValidCategories`.

---

### MPM-P5-T02 - Decorate DTOs and Create Empty Marker DTOs

**Design reference:** [DESIGN.md § Phase 5.2](./DESIGN.md#52-decorate-existing-parameter-dtos-and-add-empty-marker-dtos)

**Scope:**
Apply `[DoctrineContract]` to existing DTOs and create empty marker DTOs for parameterless doctrines. Add `public const string BehaviorId` to each.

**Existing DTO files to modify** (all in `Hrot/Engine/Hrot.Core/`):
- `FireAtTargetParamsJsonDto.cs` - add `[DoctrineContract(CgfDoctrineIds.FireAtTarget_BT, BehaviorId, DoctrineCategory.AllMilitary)]` and `public const string BehaviorId = "FireAtTarget";`
- `MoveToLocationParamsJsonDto.cs` - add `[DoctrineContract(CgfDoctrineIds.MoveTo_BT, BehaviorId, DoctrineCategory.AllMilitary | DoctrineCategory.Civilian)]` and `public const string BehaviorId = "MoveToLocation";`
- `FollowRouteParamsJsonDto.cs` - add attribute and `public const string BehaviorId = "FollowRoute";`
- `JoinFormationParamsJsonDto.cs` - add attribute and `public const string BehaviorId = "JoinFormation";`

**New empty marker DTO files to create** (in `Hrot/Engine/Hrot.Core/MapDefinitions/Doctrine/`):
- `IdleParamsJsonDto.cs`
- `WanderMilitaryParamsJsonDto.cs`
- `ConvoyEscortParamsJsonDto.cs`
- `InfantryCombatParamsJsonDto.cs`
- `AmbushParamsJsonDto.cs`

Each new file contains a class with the `[DoctrineContract]` attribute and a `public const string BehaviorId` constant only.

**Success conditions:**
- All five existing DTOs have both the `[DoctrineContract]` attribute and a `const string BehaviorId`.
- All five marker DTO files exist and compile.
- Reflection test: `Assembly.GetTypes().Where(t => t.GetCustomAttribute<DoctrineContractAttribute>() != null)` returns at least 9 types (4 existing + 5 new).

---

### MPM-P5-T03 - Create DoctrineSchemaDiscovery

**Design reference:** [DESIGN.md § Phase 5.3](./DESIGN.md#53-build-doctrineschemariscovery-for-auto-registration)

**Scope:**
Create the auto-registration utility that scans the assembly and invokes `Register<T>` generically on `BehaviorUiRegistry` and `ScenarioBehaviorRemapper`.

**Pre-condition:** Verify which project can reference both `Hrot.Core` (for `DoctrineContractAttribute`) and `Fdp.Toolkit.Behavior` (for `ScenarioBehaviorRemapper`) and `Hrot.Presentation` (for `BehaviorUiRegistry`) without creating a circular dependency. Check `.csproj` files of candidate projects (`Hrot.Presentation`, `Hrot.CGF`).

**New file:** Create in the appropriate project (confirmed by dependency check above).

Content: `DoctrineSchemaDiscovery.AutoRegister(BehaviorUiRegistry, ScenarioBehaviorRemapper)` as specified in [DESIGN.md § 5.3](./DESIGN.md#53-build-doctrineschemariscovery-for-auto-registration).

**Success conditions:**
- `DoctrineSchemaDiscovery.cs` compiles without creating a circular project dependency.
- Unit test: Call `AutoRegister` with mock/real registry instances. Verify that `BehaviorUiRegistry` has registered entries for all 9 doctrine DTOs. Verify `ScenarioBehaviorRemapper` has delegates for all 9 behavior IDs.
- No magic strings appear in the test or the `AutoRegister` method itself.

---

### MPM-P5-T04 - Replace BehaviorUiSetup and CgfDoctrineSetup Manual Registrations

**Design reference:** [DESIGN.md § Phase 5.4 - 5.5](./DESIGN.md#54-replace-behavioruisetup-manual-registrations)

**Scope:**
Remove the hardcoded `Register<T>("string")` calls from both setup files and replace with the auto-discovery call.

**Files to modify:**
- `Hrot/Engine/Hrot.Presentation/Behavior/BehaviorUiSetup.cs`: Replace the body of `CreateRegistry()` with `DoctrineSchemaDiscovery.AutoRegister(registry, remapper)`. Plumb `remapper` parameter in from the call site if not already present.
- `Hrot/Subsystems/Hrot.CGF/Configuration/CgfDoctrineSetup.cs`: Derive the `behaviorId` argument in each `registry.Register(id, behaviorId, ...)` call from the DTO's `[DoctrineContract].BehaviorId` instead of a raw string literal. Alternatively, if the full auto-registration approach applies here, use `DoctrineSchemaDiscovery`.

**Success conditions:**
- No magic behavior-ID string literals exist in `BehaviorUiSetup.cs` or `CgfDoctrineSetup.cs`.
- Integration test: Build and run the UI setup; assert that `BehaviorUiRegistry` returns a non-null UI descriptor for `"FireAtTarget"`, `"MoveToLocation"`, etc.
- Solution builds without errors.

---

### MPM-P5-T05 - Rebuild DoctrineCatalog Using Reflection

**Design reference:** [DESIGN.md § Phase 5.6](./DESIGN.md#56-rebuild-doctrinecatalog-using-reflection)

**Scope:**
Replace the hardcoded string arrays in `DoctrineCatalog.cs` with a reflection-based dictionary built once at type initialization.

**File to modify:** `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/DoctrineCatalog.cs`

Replace `s_militaryApcDoctrines`, `s_infantryDoctrines`, `s_insurgentDoctrines`, `s_defaultDoctrines` arrays with the `BuildMap()` implementation as specified in [DESIGN.md § 5.6](./DESIGN.md#56-rebuild-doctrinecatalog-using-reflection).

**Success conditions:**
- No string literals like `"ConvoyEscort"`, `"FireAtTarget"`, etc. appear in `DoctrineCatalog.cs`.
- Unit test: `DoctrineCatalog.GetValidDoctrines(TkbEntityTypes.MilitaryApc)` returns a list containing `"FireAtTarget"`, `"MoveToLocation"`, `"ConvoyEscort"`, `"WanderMilitary"`, `"FollowRoute"`.
- Unit test: `DoctrineCatalog.GetValidDoctrines(TkbEntityTypes.CivilianPedestrian)` does NOT contain `"FireAtTarget"`.
- Adding a new `[DoctrineContract]` DTO automatically appears in the catalog results without touching `DoctrineCatalog.cs`.

---

### MPM-P5-T06 - Update CgfNodes.cs to Use DTO BehaviorId Constants

**Design reference:** [DESIGN.md § Phase 5.7](./DESIGN.md#57-update-cgfnodescs-to-use-dto-constants)

**Scope:**
Replace inline JSON tree-name string literals with references to the `const string BehaviorId` defined on the corresponding DTO.

**File to modify:** `Hrot/Subsystems/Hrot.CGF/Brains/CgfNodes.cs`

For each AI tree JSON string containing `"TreeName": "XxxDoctrineId"`, change the literal string value to use a C# interpolated raw string:
```csharp
private static readonly string FireAtTargetJson = $$"""
{
  "TreeName": "{{FireAtTargetParamsJsonDto.BehaviorId}}",
  ...
}
""";
```
Apply to all tree definitions in the file.

**Success conditions:**
- No raw behavior-ID string literals remain in `CgfNodes.cs` (in the `TreeName` positions).
- The runtime JSON strings are identical to what they were before (same values, just generated from constants).
- Existing CGF behavior tree tests pass.
- Solution builds without errors.

---

### MPM-P5-T07 - Create DoctrineTestHelper and Update Tests

**Design reference:** [DESIGN.md § Phase 5.8](./DESIGN.md#58-create-doctrinetesthelper-and-update-unit-tests)

**Scope:**
Provide a test helper to extract behavior IDs from the attribute, and update all test files that hardcode behavior-ID strings.

**New file:** `Hrot/Engine/Hrot.Core/MapDefinitions/Doctrine/DoctrineTestHelper.cs`

Content as specified in [DESIGN.md § 5.8](./DESIGN.md#58-create-doctrinetesthelper-and-update-unit-tests).

**Files to scan and update:**
Search for any test file under `Hrot/` that contains a string literal matching a doctrine behavior ID (e.g., `"FireAtTarget"`, `"MoveToLocation"`, `"FollowRoute"`, etc. used as test data). In each such file, replace the raw string with `DoctrineTestHelper.GetBehaviorId<TDto>()`.

**Success conditions:**
- `DoctrineTestHelper.GetBehaviorId<FireAtTargetParamsJsonDto>()` returns `"FireAtTarget"`.
- `DoctrineTestHelper.GetBehaviorId<SomethingWithoutAttribute>()` throws `InvalidOperationException`.
- All updated test files pass.
- No magic behavior-ID strings remain in test files that were previously hardcoded.
