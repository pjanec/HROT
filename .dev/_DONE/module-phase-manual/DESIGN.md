# Design: Network Architecture Cleanup and Module Phase Manual

## Overview

This document captures the final design arising from the design talk in `design-talk.md`.
The work spans five refactoring areas that collectively:

1. **Remove dead code** - legacy network-replay infrastructure, auto-translator ACL violations, unused NetworkDemo project, and stale perception systems.
2. **Fix descriptor ordinal magic numbers** - replace hardcoded integer ordinals in network translators with named enumerations.
3. **Segregate network translator interfaces** - separate the base `INetworkTranslator` from descriptor-specific concerns; give event translators their own lightweight contract.
4. **Introduce `SystemPhase.Manual`** - elevate the "direct execution" module pattern from an invisible hack into a first-class, diagnostics-visible feature of the ModuleHost.
5. **Eliminate doctrine magic strings** - replace all hardcoded behavior-ID strings with a `[DoctrineContract]` attribute placed on parameter DTOs, making the DTO the Single Source of Truth for every doctrine.

All phases are independent enough to be tackled in sequence without blocking each other.

---

## Codebase Context

| Component | Path |
|-----------|------|
| FDP core interfaces | `FDP/Engine/Fdp.Core/Abstractions/` |
| ModuleHost framework | `FDP/Engine/Fdp.ModuleHost/` |
| Cyclone network layer | `FDP/Network/Fdp.Network.Cyclone/` |
| NetworkDemo example | `FDP/Examples/Fdp.Examples.NetworkDemo/` |
| FDP time toolkit | `FDP/Toolkits/Fdp.Toolkits/Time/` |
| FDP behavior toolkit | `FDP/Toolkits/Fdp.Toolkits/Behavior/` |
| Perception toolkit | `FDP/Toolkits/Fdp.Toolkits/Perception/` |
| NED network layer | `Hrot/Network/Hrot.Network.NED/` |
| BDC network layer | `Hrot/Network/Hrot.Network.BDC/` |
| SimHost subsystem | `Hrot/Subsystems/Hrot.SimHost/` |
| Hrot core domain | `Hrot/Engine/Hrot.Core/` |
| Hrot presentation | `Hrot/Engine/Hrot.Presentation/` |
| CGF subsystem | `Hrot/Subsystems/Hrot.CGF/` |

### Key Facts Verified Against Codebase

- `SystemPhase` enum exists at `FDP/Engine/Fdp.ModuleHost/Abstractions/SystemPhase.cs` with phases: `Input=1`, `BeforeSync=2`, `Simulation=10`, `PostSimulation=20`, `Export=40`. `Manual` does NOT exist yet.
- `ISystemRegistry` at `FDP/Engine/Fdp.ModuleHost/Abstractions/ISystemRegistry.cs` has only `RegisterSystem<T>`. `RegisterManualSystem` does NOT exist yet.
- `SystemScheduler` implements `ISystemRegistry` and has `GetProfileData(IEcsModuleSystem)` and `RecordExecution` on `SystemProfileData`.
- `ModuleHostKernel` has a private nested `CapturingSystemRegistry : ISystemRegistry` class (line 1815) that wraps the scheduler. It needs a `RegisterManualSystem` method added.
- `AutonomousPerceptionModule` currently uses the "Direct Execution" hack: `RegisterSystems` is empty; the four inner systems are private `readonly` fields executed via `Tick()` on a scoped bus. They are invisible to the diagnostic UI.
- `IDescriptorTranslator` already includes `TranslatorDirection Direction { get; }` and `TranslatorDirection` enum already exists. The `GetDirectionLabel` string-matching hack still exists in `ArchitectureDiagnosticsPanel.cs` at line 274.
- `INetworkReplayTarget` exists at `FDP/Network/Fdp.Network.Cyclone/Abstractions/INetworkReplayTarget.cs`. It is implemented by `CycloneTranslator<>`, `CycloneNativeEventTranslator<>`, `CycloneManagedEventTranslator<>`, `MultiInstanceCycloneTranslator<>`, `AutoCycloneTranslator<>`, `ManagedAutoCycloneTranslator<>`. The `NetworkReplaySystem.cs` that used it is in `Fdp.Examples.NetworkDemo` and is dead code.
- `EDescriptorType` in `Hrot/Network/Hrot.Network.NED/AllDescriptors.cs` is partially filled. Missing entries for: `dtMapEntitySymbol`, `dtSensorConfig`, `dtRaycastRequestBatch`, `dtSensorTrackState`, `dtRaycastResponseBatch`, `dtPathRequestBatch`, `dtPathResponseBatch`, `dtGroundClampingOverride`, `dtWeaponFireRequest`, `dtWeaponFire`, `dtMunitionDetonation`, `dtEntityHitDamage`, `dtAudioTargetDetected`, `dtMissionControlRequest`, `dtMissionControlAck`.
- `EntityMissionIngressTranslator` uses magic number `50` (should be `dtEntityMission = 51`).
- `EntityMasterIngressTranslator` uses magic number `-2` (should be `dtEntityMaster = 0`).
- `EntityInfoIngressTranslator` and `EntityInfoEgressTranslator` have ALREADY been fixed to use `(long)EDescriptorType.dtEntityInfo`. No action needed.
- `TimeDescriptorType` enum does NOT exist. Five time translators use raw ordinals: `SwitchTimeModeDescriptorTranslator=201`, `MasterLockstepTranslator=202`, `SlaveLockstepTranslator=203`, `MasterTimeSyncTranslator=205`, `SlaveTimeSyncTranslator=206`.
- `BdcDescriptorType` enum does NOT exist. Two BDC translators use raw ordinals: `BdcEntityMasterTranslator=1000`, `BdcWorldPosTranslator=1002`.
- `PerceptionBroadphaseSystem.cs` and `ThreatEvaluationAdapterSystem.cs` exist in `Hrot/Subsystems/Hrot.SimHost/Systems/` but are NOT registered in `CombatModule.cs` (the comment says they are intentionally omitted).
- `AutoCycloneTranslator.cs` and `ManagedAutoCycloneTranslator.cs` exist in `FDP/Network/Fdp.Network.Cyclone/Translators/` and are used only by `Fdp.Examples.NetworkDemo`.
- `ReplicationBootstrap.cs` exists at `FDP/Network/Fdp.Network.Cyclone/ReplicationBootstrap.cs` and is used only by NetworkDemo.
- `FdpDescriptorAttribute.cs` exists at `FDP/Engine/Fdp.Core/Abstractions/FdpDescriptorAttribute.cs` and is used only by the NetworkDemo demo-specific structs/classes.
- `Fdp.Examples.NetworkDemo` and `Fdp.Examples.NetworkDemo.Tests` exist in `FDP/Examples/`.
- `DoctrineCatalog.cs` uses hardcoded string arrays (`["ConvoyEscort", "MoveToLocation", ...]`).
- `BehaviorUiSetup.cs` uses `registry.Register<FireAtTargetParamsJsonDto>("FireAtTarget")` etc.
- `CgfDoctrineSetup.cs` uses `registry.Register(CgfDoctrineIds.FireAtTarget_BT, "FireAtTarget", ...)` etc.
- `CgfNodes.cs` contains inline JSON strings with `"TreeName": "FireAtTarget"`, etc.
- `DoctrineContractAttribute` and `DoctrineCategory` do NOT exist yet.
- `ScenarioBehaviorRemapper` is in `FDP/Toolkits/Fdp.Toolkits/Behavior/ScenarioBehaviorRemapper.cs`.
- `MultiInstanceCycloneTranslator` implements `INetworkReplayTarget` but is NOT used in `Hrot` production code. It should be kept (not deleted) but `INetworkReplayTarget` stripped.
- Production `OwnershipUpdateTranslator` (in `Hrot.Network.NED`) already has `IDescriptorTranslator` only, no `INetworkReplayTarget`. The NetworkDemo-local version will be deleted as part of the demo deletion.

---

## Phase 1: Dead Code Purge

### Goal
Remove all dead code that creates confusion, violates ACL constraints, or pollutes the diagnostic UI. This is pure subtraction with no new features.

### 1.1 Delete Legacy Perception Systems

`PerceptionBroadphaseSystem` and `ThreatEvaluationAdapterSystem` in `Hrot.SimHost` are explicitly skipped by `CombatModule.cs` in favor of `AutonomousPerceptionModule`. They are unreachable dead code.

**Files to delete:**
- `Hrot/Subsystems/Hrot.SimHost/Systems/PerceptionBroadphaseSystem.cs`
- `Hrot/Subsystems/Hrot.SimHost/Systems/ThreatEvaluationAdapterSystem.cs`

**File to update:**
- `Hrot/Subsystems/Hrot.SimHost/Modules/CombatModule.cs` - remove the comment block that explains these deleted systems, and any stale `using` directives for their namespaces.

### 1.2 Delete NetworkReplaySystem and INetworkReplayTarget

`NetworkReplaySystem` is a legacy PCAP-style replay mechanism superseded by the ECS Flight Recorder. The `INetworkReplayTarget` interface was its exclusive consumer. Both are dead code.

**Files to delete:**
- `FDP/Network/Fdp.Network.Cyclone/Abstractions/INetworkReplayTarget.cs`

**Classes to strip of `INetworkReplayTarget`** (remove `: INetworkReplayTarget` and delete `InjectReplayData` method):
- `FDP/Network/Fdp.Network.Cyclone/Translators/CycloneTranslator.cs`
- `FDP/Network/Fdp.Network.Cyclone/Translators/CycloneNativeEventTranslator.cs`
- `FDP/Network/Fdp.Network.Cyclone/Translators/CycloneManagedEventTranslator.cs`
- `FDP/Network/Fdp.Network.Cyclone/Translators/MultiInstanceCycloneTranslator.cs`

Also in `CycloneNativeEventTranslator.cs`, remove the hack that set `DescriptorOrdinal = topicName.GetHashCode()` as a fake routing key for the now-deleted replay system.

Note: `AutoCycloneTranslator`, `ManagedAutoCycloneTranslator`, and the NetworkDemo-local `OwnershipUpdateTranslator` implement `INetworkReplayTarget` but are being deleted entirely in section 1.3 below.

### 1.3 Delete AutoCycloneTranslators, ReplicationBootstrap, and NetworkDemo

`AutoCycloneTranslator<T>` and `ManagedAutoCycloneTranslator<T>` tightly couple the internal ECS component memory layout directly to the DDS network wire format, bypassing the Anti-Corruption Layer. They serve only the `Fdp.Examples.NetworkDemo`. The production HROT network layers (`Hrot.Network.NED`, `Hrot.Network.BDC`) explicitly reject this pattern in favor of hand-written domain translators.

`ReplicationBootstrap.cs` uses reflection to scan for `[FdpDescriptor]`-tagged types and auto-spawn these translators. `FdpDescriptorAttribute.cs` is the marking attribute that enables this scanning.

**Files to delete:**
- `FDP/Network/Fdp.Network.Cyclone/Translators/AutoCycloneTranslator.cs`
- `FDP/Network/Fdp.Network.Cyclone/Translators/ManagedAutoCycloneTranslator.cs`
- `FDP/Network/Fdp.Network.Cyclone/ReplicationBootstrap.cs`
- `FDP/Engine/Fdp.Core/Abstractions/FdpDescriptorAttribute.cs`
- Entire `FDP/Examples/Fdp.Examples.NetworkDemo/` directory
- Entire `FDP/Examples/Fdp.Examples.NetworkDemo.Tests/` directory

**Solution files to update:**
- `FDP/FDP.sln` - remove `Fdp.Examples.NetworkDemo` and `Fdp.Examples.NetworkDemo.Tests` project references
- `IOS-IG-SimHost.sln` - remove `Fdp.Examples.NetworkDemo` and `Fdp.Examples.NetworkDemo.Tests` project references

**Tests to remove:**
- `FDP/Network/Fdp.Network.Cyclone.Tests/Translators/AutoCycloneTranslatorTests.cs` - tests the deleted class

---

## Phase 2: Descriptor Ordinal Cleanup

### Goal
Replace all magic integer literals in `DescriptorOrdinal` properties with named constants from type-safe enumerations. Each domain defines its own enum to preserve Anti-Corruption Layer boundaries.

### 2.1 Extend `EDescriptorType` for the NED Domain

`EDescriptorType` in `Hrot/Network/Hrot.Network.NED/AllDescriptors.cs` is currently incomplete. Add all missing NED translator ordinals:

```csharp
public enum EDescriptorType
{
    dtEntityMaster          = 0,
    dtEntityInfo            = 1,
    dtWorldPos              = 2,
    dtMapVisualOverlay      = 3,
    dtMapRoute              = 4,
    dtEntityDamage          = 30,
    dtMapEntitySymbol       = 40,   // MapEntitySymbolIngressTranslator
    dtEntityMission         = 51,
    dtNavigationIntent      = 52,
    dtNavigationStatus      = 53,
    dtDeferredTakeOwnership = 54,
    dtOwnershipUpdate       = 55,
    dtSensorConfig          = 60,   // SensorConfigEgressTranslator
    dtRaycastRequestBatch   = 61,   // RaycastBatchEgressTranslator
    dtSensorTrackState      = 62,   // SensorTrackStateIngressTranslator
    dtRaycastResponseBatch  = 63,   // RaycastBatchIngressTranslator
    dtPathRequestBatch      = 64,   // PathRequestBrainEgressTranslator
    dtPathResponseBatch     = 65,   // PathResponseBrainIngressTranslator
    dtGroundClampingOverride= 66,   // GroundClampingOverrideTranslator
    dtWeaponFireRequest     = 80,   // WeaponFireIntentEgressTranslator
    dtWeaponFire            = 81,   // WeaponFireNotificationEgressTranslator
    dtMunitionDetonation    = 82,   // MunitionDetonationEgressTranslator
    dtEntityHitDamage       = 83,   // DamageAssessedEgressTranslator
    dtAudioTargetDetected   = 84,   // AudioTargetDetectedEgressTranslator
    dtMissionControlRequest = 90,   // MissionControlIngressTranslator
    dtMissionControlAck     = 91    // MissionControlAckEgressTranslator
}
```

### 2.2 Fix NED Translators Still Using Magic Ordinals

The following translators were not yet updated to reference `EDescriptorType`:

- **`EntityMissionIngressTranslator`** (`Hrot.Network.NED`): uses `50` but `dtEntityMission = 51`. Change to `(long)EDescriptorType.dtEntityMission`.
- **`EntityMasterIngressTranslator`** (`Hrot.Network.NED`): uses `-2` as a collision-avoidance hack. Since `TranslatorDirection` now segregates ingress from egress, both translators can safely share ordinal `0`. Change to `(long)EDescriptorType.dtEntityMaster`.
- **`MapEntitySymbolIngressTranslator`**: uses `40`, which must now reference the newly added `dtMapEntitySymbol = 40`.

Apply the same `(long)EDescriptorType.dtXxx` pattern to all other NED translators that still use raw integer literals (scan `Hrot.Network.NED` for `OrdinalValue = [0-9]`).

**Ordinal collision verification (required before changing EntityMasterIngressTranslator):**

Changing the ingress translator from `-2` to `0` means ingress and egress translators for EntityMaster will share ordinal `0`. Before applying this, verify the two registration sites that consume `DescriptorOrdinal`:

1. **Translator storage arrays** — translators are stored as `IDescriptorTranslator[]` arrays (confirmed: `CycloneIngressSystem`, `CycloneEgressSystem`, `CycloneNetworkCleanupSystem` all use plain arrays). No `Dictionary<long, IDescriptorTranslator>` exists in the codebase, so no `KeyAlreadyExistsException` can be thrown.
2. **`DescriptorOwnershipMap.RegisterFromTranslator`** — called in `NedReplicationModule.PopulateDescriptorOwnershipMap` for every translator regardless of direction. The map's internal `_descriptorToComponentIds` dictionary uses indexer assignment (`[key] = value`), not `Add`. A second registration with ordinal `0` silently overwrites the first. This is safe as long as the ingress and egress translators for the same descriptor expose identical `TargetComponentIds` (they must — they govern the same ECS components). Confirm this before merging.

**No dictionary split is required.** The existing array-based design already separates ingress from egress at the system level.

### 2.3 Create `TimeDescriptorType` for the FDP Time Toolkit

The FDP time toolkit (`Fdp.Toolkits.Time`) must not reference `Hrot.NED.Descriptors`. Create a dedicated enum:

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

Update these five translators to use `(long)TimeDescriptorType.Xxx`:
- `SwitchTimeModeDescriptorTranslator` (201 → `SwitchTimeModeEvent`)
- `MasterLockstepTranslator` (202 → `MasterFrameOrder`)
- `SlaveLockstepTranslator` (203 → `SlaveFrameOrder`)
- `MasterTimeSyncTranslator` (205 → `TimeSyncRequest`)
- `SlaveTimeSyncTranslator` (206 → `TimeSyncResponse`)

### 2.4 Create `BdcDescriptorType` for the BDC Domain

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

Update these two translators to use `(long)BdcDescriptorType.Xxx`:
- `BdcEntityMasterTranslator` (1000 → `EntityMaster`)
- `BdcWorldPosTranslator` (1002 → `WorldPos`)

---

## Phase 3: Network Interface Segregation

### Goal
Split the monolithic `IDescriptorTranslator` into a proper interface hierarchy. Transient event translators must no longer be forced to implement descriptor-specific methods (`DescriptorOrdinal`, `ApplyToEntity`, `Dispose`). The `Direction` property in `INetworkTranslator` allows the diagnostic panel to drop its brittle string-matching hack.

### 3.0 Extract `CycloneBaseTranslator` (Prerequisite)

**Verified fact:** `CycloneTranslator<TDds,TView>`, `CycloneNativeEventTranslator<TEcs,TDds>`, and `CycloneManagedEventTranslator<TEcs,TDds>` are **sibling classes** — the event translators do NOT inherit from `CycloneTranslator`. The inheritance trap described in the feedback does not exist. However, all three classes share an identical block of `INetworkTranslator`-tier implementation: the `Reader`, `Writer`, `EntityMap` fields; the `TopicName`, `ReceivedSampleCount`, `SentSampleCount` properties; the abstract `Direction`; and the `PollIngress`/`ScanAndPublish` method bodies. Without a common base, these would be duplicated across `CycloneTranslator` and each event translator after the interface swap.

To prevent that duplication, introduce a new abstract base class before modifying any interfaces:

**New file:** `FDP/Network/Fdp.Network.Cyclone/Translators/CycloneBaseTranslator.cs`

```csharp
namespace Fdp.Network.Cyclone.Translators
{
    /// <summary>
    /// Shared abstract base for all Cyclone-based network translators.
    /// Implements the <see cref="INetworkTranslator"/> contract common to both
    /// descriptor translators and event translators.
    /// </summary>
    public abstract class CycloneBaseTranslator : INetworkTranslator
    {
        protected readonly DdsReader<TDds> Reader;   // each derived class has its own TDds
        protected readonly DdsWriter<TDds> Writer;
        protected readonly NetworkEntityMap EntityMap;

        public string TopicName { get; }
        public abstract TranslatorDirection Direction { get; }
        public long ReceivedSampleCount { get; protected set; }
        public long SentSampleCount { get; protected set; }

        protected CycloneBaseTranslator(
            DdsParticipant? participant,
            string topicName,
            NetworkEntityMap entityMap)
        {
            TopicName   = topicName   ?? throw new ArgumentNullException(nameof(topicName));
            EntityMap   = entityMap   ?? throw new ArgumentNullException(nameof(entityMap));
            // Reader/Writer initialized by generic derived classes
        }

        public abstract void PollIngress(IEntityCommandBuffer cmd, ISimulationView view);
        public abstract void ScanAndPublish(ISimulationView view);
    }
}
```

Because `CycloneTranslator`, `CycloneNativeEventTranslator`, and `CycloneManagedEventTranslator` are all generic classes with their own `TDds` type parameters, the `Reader`/`Writer` fields cannot literally live in a non-generic base. In practice the base class must either be generic itself (e.g., `CycloneBaseTranslator<TDds>`) or the reader/writer construction remains in each derived class while the base only carries the non-generic members (`TopicName`, `Direction`, `ReceivedSampleCount`, `SentSampleCount`). Choose whichever avoids duplication given the concrete type constraints. The key structural rule is: **the base provides the full `INetworkTranslator` implementation; `CycloneTranslator` adds `IDescriptorTranslator` members on top; event translators only add `INetworkEventTranslator`**.

After this base is in place, changes to `CycloneTranslator` (section 3.2) and the event translator classes (section 3.4) become straightforward — each changes only the interface it additionally declares.

### 3.1 Create `INetworkTranslator` Base Interface

**New file:** `FDP/Engine/Fdp.Core/Abstractions/INetworkTranslator.cs`

```csharp
namespace Fdp.Interfaces
{
    public interface INetworkTranslator
    {
        string TopicName { get; }
        TranslatorDirection Direction { get; }
        long ReceivedSampleCount { get; }
        long SentSampleCount { get; }

        void PollIngress(IEntityCommandBuffer cmd, ISimulationView view);
        void ScanAndPublish(ISimulationView view);
    }
}
```

`TranslatorDirection` remains in `IDescriptorTranslator.cs` (same file) or moves to its own file — it is already defined in the codebase.

### 3.2 Refactor `IDescriptorTranslator` to Extend `INetworkTranslator`

`IDescriptorTranslator` moves from a standalone interface to a specialization:

```csharp
namespace Fdp.Interfaces
{
    public interface IDescriptorTranslator : INetworkTranslator
    {
        long DescriptorOrdinal { get; }
        IReadOnlyList<int> TargetComponentIds => System.Array.Empty<int>();

        void ApplyToEntity(Entity entity, object data, EntityRepository repo);
        void Dispose(long networkEntityId);
    }
}
```

The `TopicName`, `Direction`, `ReceivedSampleCount`, `SentSampleCount`, `PollIngress`, and `ScanAndPublish` members are removed from `IDescriptorTranslator` since they are now inherited from `INetworkTranslator`. Because `CycloneTranslator<>` already provides all these implementations, no concrete translator changes are needed for this step.

### 3.3 Create `INetworkEventTranslator` Marker Interface

```csharp
namespace Fdp.Interfaces
{
    /// <summary>
    /// Marker interface for transient network event translators.
    /// Event translators do not manage persistent entity state and have no
    /// DescriptorOrdinal, TargetComponentIds, ApplyToEntity, or Dispose contract.
    /// </summary>
    public interface INetworkEventTranslator : INetworkTranslator
    {
    }
}
```

### 3.4 Update Event Translator Base Classes

Change `CycloneNativeEventTranslator<TEcs, TDds>` and `CycloneManagedEventTranslator<TEcs, TDds>` to implement `INetworkEventTranslator` instead of `IDescriptorTranslator`:

- Remove `: IDescriptorTranslator` (already done by removing `INetworkReplayTarget` in Phase 1 if combined, else do here).
- Add `: INetworkEventTranslator`.
- Remove the `DescriptorOrdinal`, `TargetComponentIds`, `ApplyToEntity`, and `Dispose` members from these classes — they have no meaning for transient events.
- The `DescriptorOrdinal = topicName.GetHashCode()` assignment was already removed in Phase 1.

### 3.5 Update `CycloneNetworkIngressSystem` and `CycloneEgressSystem`

These systems iterate over their translator collections to call `PollIngress` and `ScanAndPublish`. Change their constructor argument type from `IDescriptorTranslator[]` to `INetworkTranslator[]`. Both methods are available on the base interface.

`CycloneNetworkCleanupSystem` calls `Dispose(networkEntityId)` on all translators — keep it requiring `IDescriptorTranslator[]`.

### 3.6 Remove `GetDirectionLabel` Hack from `ArchitectureDiagnosticsPanel`

`ArchitectureDiagnosticsPanel.cs` (line 262-280) infers translator direction from the system class name (e.g., checking for "Ingress" or "Egress" in the name). Now that `INetworkTranslator.Direction` explicitly carries this information, the string-matching method must be deleted.

In `EnumerateTranslatorRows`, read `translator.Direction` directly from the interface instead of calling `GetDirectionLabel`.

---

## Phase 4: SystemPhase.Manual

### Goal
Modules that require intra-frame manual execution (like `AutonomousPerceptionModule`) are currently invisible to the diagnostic tooling because their systems are not registered with the kernel. Introduce `SystemPhase.Manual` as a first-class enum value and `RegisterManualSystem` as a first-class API, so modules can register their systems for diagnostics while retaining manual control over execution order and bus swapping.

### 4.1 Add `Manual` to `SystemPhase` Enum

**File:** `FDP/Engine/Fdp.ModuleHost/Abstractions/SystemPhase.cs`

```csharp
/// <summary>
/// Explicitly excluded from the kernel's automatic phase execution.
/// Systems in this phase are registered for diagnostics and profiling
/// but must be manually ticked by their owning module.
/// </summary>
Manual = 255
```

The kernel's automatic phase runner (`ExecutePhase`) must skip `Manual` — systems in this phase will never be invoked by the kernel automatically.

### 4.2 Add `RegisterManualSystem<T>` to `ISystemRegistry`

**File:** `FDP/Engine/Fdp.ModuleHost/Abstractions/ISystemRegistry.cs`

```csharp
/// <summary>
/// Registers a system in the Manual phase for diagnostics tracking.
/// Returns a profiled wrapper. The module must tick the wrapper manually
/// so execution time is recorded in the kernel's profiling UI.
/// </summary>
IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem;
```

### 4.3 Implement in `SystemScheduler`

**File:** `FDP/Engine/Fdp.ModuleHost/Scheduling/SystemScheduler.cs`

Add `RegisterManualSystem<T>` method that:
1. Registers the system under `SystemPhase.Manual` via `RegisterSystem<T>` (so it appears in `GetAllProfileData()` / `GetProfileData()` and the diagnostic UI).
2. Returns a `ProfiledManualSystemWrapper` that, on each `Execute()`, measures elapsed time and calls `profile.RecordExecution(ms)` before delegating to the inner system.

```csharp
public IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem
{
    RegisterSystem(system);  // registers under SystemPhase.Manual
    return new ProfiledManualSystemWrapper(system, this);
}

private sealed class ProfiledManualSystemWrapper : IEcsModuleSystem
{
    private readonly IEcsModuleSystem _inner;
    private readonly SystemScheduler _scheduler;

    public ProfiledManualSystemWrapper(IEcsModuleSystem inner, SystemScheduler scheduler)
    {
        _inner = inner;
        _scheduler = scheduler;
    }

    public void Execute(ISimulationView view, float deltaTime)
    {
        var profile = _scheduler.GetProfileData(_inner);
        var sw = Stopwatch.StartNew();
        try   { _inner.Execute(view, deltaTime); }
        finally
        {
            sw.Stop();
            profile?.RecordExecution(sw.Elapsed.TotalMilliseconds);
        }
    }
}
```

`ExecutePhase(SystemPhase.Manual, ...)` must do nothing (skip the phase).

### 4.4 Update `CapturingSystemRegistry` in `ModuleHostKernel`

**File:** `FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs`

The private nested `CapturingSystemRegistry` class (line ~1815) wraps `SystemScheduler`. Add `RegisterManualSystem` delegation:

```csharp
public IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem
{
    Captured.Add(system);
    return _scheduler.RegisterManualSystem(system);
}
```

### 4.5 Tag the Four Perception Systems with `[UpdateInPhase(SystemPhase.Manual)]`

The `SystemScheduler` reads `[UpdateInPhase(...)]` to determine a system's phase during `RegisterSystem`. If a system lacks this attribute, the scheduler throws. Add the attribute to:

- `LocalGridBuilderSystem`
- `VisionBroadphaseSystem`
- `LosRequestBatchingSystem`
- `SensorTrackDebounceSystem`

### 4.6 Refactor `AutonomousPerceptionModule`

**File:** `FDP/Toolkits/Fdp.Toolkits/Perception/Modules/AutonomousPerceptionModule.cs`

Change the four private `readonly` system fields from concrete types to `IEcsModuleSystem` (to hold the profiled wrappers). Fill them in `RegisterSystems` instead of the constructor:

```csharp
private IEcsModuleSystem _localGridBuilder   = null!;
private IEcsModuleSystem _visionBroadphase   = null!;
private IEcsModuleSystem _losRequestBatching = null!;
private IEcsModuleSystem _sensorTrackDebounce = null!;

public void RegisterSystems(ISystemRegistry registry)
{
    _localGridBuilder    = registry.RegisterManualSystem(new LocalGridBuilderSystem(_localGrid));
    _visionBroadphase    = registry.RegisterManualSystem(new VisionBroadphaseSystem(_localGrid));
    _losRequestBatching  = registry.RegisterManualSystem(new LosRequestBatchingSystem(
                               mockMode: false, colliderRadiusReader: _colliderRadiusReader));
    _sensorTrackDebounce = registry.RegisterManualSystem(new SensorTrackDebounceSystem());
}

public void Tick(ISimulationView view, float dt)
{
    var scopedView = new PerceptionScopedView(view, _scopedBus);

    _localGridBuilder.Execute(scopedView, dt);

    _visionBroadphase.Execute(scopedView, dt);
    _scopedBus.SwapBuffers();

    _losRequestBatching.Execute(scopedView, dt);
    _scopedBus.SwapBuffers();

    _sensorTrackDebounce.Execute(scopedView, dt);
}
```

After this change, all four perception systems will be visible in the `ArchitectureDiagnosticsPanel` under the `Manual` phase bucket, with live execution timing.

**Wrapper casting rule:** `RegisterManualSystem` returns a `ProfiledManualSystemWrapper` that encapsulates the original system instance. The stored `IEcsModuleSystem` field must **never** be downcast back to the concrete type (e.g., `(LocalGridBuilderSystem)_localGridBuilder`). If code outside `Tick` needs to call a method specific to the concrete system (for example, to pass configuration or read state in a test), hold a separate `readonly` field of the concrete type before passing the instance to `RegisterManualSystem`:

```csharp
// Correct dual-reference pattern:
private LocalGridBuilderSystem _localGridBuilderRaw = null!;  // concrete — for direct access if needed
private IEcsModuleSystem       _localGridBuilder    = null!;  // wrapper  — for Execute() calls

public void RegisterSystems(ISystemRegistry registry)
{
    _localGridBuilderRaw = new LocalGridBuilderSystem(_localGrid);
    _localGridBuilder    = registry.RegisterManualSystem(_localGridBuilderRaw);
    // ...
}
```

In the current `AutonomousPerceptionModule` implementation, the systems are only called via `Execute(scopedView, dt)` in `Tick`, so the dual-reference pattern is not required today. Document this rule as a convention so future modifications do not inadvertently introduce a downcast.

**Critical companion change:** `AutonomousPerceptionModule` is not registered directly with the kernel — it lives as a private field inside `SimHostCoreLogicPack`, which is the actual registered module. For `RegisterManualSystem` calls inside `AutonomousPerceptionModule.RegisterSystems` to reach the kernel's `SystemScheduler`, `SimHostCoreLogicPack.RegisterSystems` must forward the `ISystemRegistry` to the inner module:

```csharp
// In SimHostCoreLogicPack.RegisterSystems:
_perceptionModule.RegisterSystems(registry);
```

Without this forwarding call, `AutonomousPerceptionModule.RegisterSystems` is never executed and the systems remain invisible to the kernel.

---

## Phase 5: Doctrine Auto-Registration

### Goal
Eliminate doctrine behavior-ID magic strings from four distinct locations — composition roots, domain catalogs, AI tree asset definitions, and unit tests — by making the parameter DTO the **Single Source of Truth** for each doctrine's string identifier, integer ID, and tactical applicability category.

### 5.1 Define `DoctrineCategory` and `DoctrineContractAttribute`

Both belong in `Hrot.Core`, alongside the existing parameter DTOs.

**New files:**
- `Hrot/Engine/Hrot.Core/MapDefinitions/Doctrine/DoctrineCategory.cs`
- `Hrot/Engine/Hrot.Core/MapDefinitions/Doctrine/DoctrineContractAttribute.cs`

```csharp
[Flags]
public enum DoctrineCategory
{
    None         = 0,
    Civilian     = 1 << 0,
    MilitaryApc  = 1 << 1,
    Infantry     = 1 << 2,
    Insurgent    = 1 << 3,
    AllMilitary  = MilitaryApc | Infantry | Insurgent
}

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class DoctrineContractAttribute : Attribute
{
    public int DoctrineId { get; }
    public string BehaviorId { get; }
    public DoctrineCategory ValidCategories { get; }

    public DoctrineContractAttribute(int doctrineId, string behaviorId, DoctrineCategory categories)
    {
        DoctrineId = doctrineId;
        BehaviorId = behaviorId;
        ValidCategories = categories;
    }
}
```

### 5.2 Decorate Existing Parameter DTOs and Add Empty Marker DTOs

Each DTO decorated with `[DoctrineContract]` must also expose `public const string BehaviorId = "..."` so the value can be referenced in AI tree JSON templates at compile time.

Existing parameter DTOs to decorate (file paths in `Hrot/Engine/Hrot.Core/`):
- `FireAtTargetParamsJsonDto` — `[DoctrineContract(CgfDoctrineIds.FireAtTarget_BT, BehaviorId, DoctrineCategory.AllMilitary)]`
- `MoveToLocationParamsJsonDto` — `[DoctrineContract(CgfDoctrineIds.MoveTo_BT, BehaviorId, DoctrineCategory.AllMilitary | DoctrineCategory.Civilian)]`
- `FollowRouteParamsJsonDto` — `[DoctrineContract(CgfDoctrineIds.FollowRoute_BT, BehaviorId, DoctrineCategory.AllMilitary)]`
- `JoinFormationParamsJsonDto` — `[DoctrineContract(CgfDoctrineIds.JoinFormation_BT, BehaviorId, DoctrineCategory.Infantry)]`

Create new **empty marker DTOs** for parameterless doctrines (these do not carry params but still need a DTO to anchor the attribute):
- `IdleParamsJsonDto` — `[DoctrineContract(CgfDoctrineIds.Idle_HSM, BehaviorId, DoctrineCategory.AllMilitary)]`
- `WanderMilitaryParamsJsonDto` — `[DoctrineContract(CgfDoctrineIds.WanderMilitary_BT, BehaviorId, DoctrineCategory.MilitaryApc)]`
- `ConvoyEscortParamsJsonDto` — `[DoctrineContract(CgfDoctrineIds.ConvoyEscort_BT, BehaviorId, DoctrineCategory.MilitaryApc)]`
- `InfantryCombatParamsJsonDto` — `[DoctrineContract(CgfDoctrineIds.InfantryCombat_BT, BehaviorId, DoctrineCategory.Infantry)]`
- `AmbushParamsJsonDto` — `[DoctrineContract(CgfDoctrineIds.Ambush_BT, BehaviorId, DoctrineCategory.Insurgent)]`

### 5.3 Build `DoctrineSchemaDiscovery` for Auto-Registration

Create a utility class in `Hrot.Core` that scans `Hrot.Core`'s assembly for all types marked with `[DoctrineContract]` and performs the required registrations. The method signature accepts the registries:

```csharp
public static class DoctrineSchemaDiscovery
{
    public static void AutoRegister(BehaviorUiRegistry uiRegistry, ScenarioBehaviorRemapper remapper)
    {
        var uiRegMethod  = typeof(BehaviorUiRegistry).GetMethod("Register")!;
        var remapMethod  = typeof(ScenarioBehaviorRemapper).GetMethod("Register")!;

        var dtoTypes = typeof(DoctrineContractAttribute).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<DoctrineContractAttribute>() != null);

        foreach (var type in dtoTypes)
        {
            var attr = type.GetCustomAttribute<DoctrineContractAttribute>()!;
            uiRegMethod.MakeGenericMethod(type).Invoke(uiRegistry, [attr.BehaviorId]);
            remapMethod.MakeGenericMethod(type).Invoke(remapper, [attr.BehaviorId]);
        }
    }
}
```

**Dependency note:** `DoctrineSchemaDiscovery` must reference `BehaviorUiRegistry` (in `Fdp.Toolkit.Behavior`) and must be placed in a layer that can reference both. Options:
- Place it in `Hrot.Presentation` which already references `Hrot.Core` and the FDP toolkits.
- Or place it in `Hrot.CGF` if that project already has both references.
  
Verify project dependency graphs before choosing the home. Do not create circular dependencies.

### 5.4 Replace `BehaviorUiSetup` Manual Registrations

**File:** `Hrot/Engine/Hrot.Presentation/Behavior/BehaviorUiSetup.cs`

Replace the manual `Register<T>("string")` calls with a call to `DoctrineSchemaDiscovery.AutoRegister(registry, remapper)`. The `remapper` parameter should be plumbed in from the composition root.

### 5.5 Replace `CgfDoctrineSetup` Manual Registrations

**File:** `Hrot/Subsystems/Hrot.CGF/Configuration/CgfDoctrineSetup.cs`

The call to `registry.Register(id, "BehaviorId", ...)` passes the behavior-ID string manually. Replace with `DoctrineSchemaDiscovery.AutoRegister(...)` or derive the `behaviorId` parameter directly from the DTO's `[DoctrineContract].BehaviorId`.

### 5.6 Rebuild `DoctrineCatalog` Using Reflection

**File:** `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/DoctrineCatalog.cs`

Replace the hardcoded string arrays with a static dictionary built once at type-initialization from `[DoctrineContract]` attributes:

```csharp
public static class DoctrineCatalog
{
    private static readonly Dictionary<DoctrineCategory, List<string>> _map = BuildMap();

    private static Dictionary<DoctrineCategory, List<string>> BuildMap()
    {
        var map = Enum.GetValues<DoctrineCategory>()
            .ToDictionary(c => c, _ => new List<string>());

        var attrs = typeof(DoctrineContractAttribute).Assembly.GetTypes()
            .Select(t => t.GetCustomAttribute<DoctrineContractAttribute>())
            .Where(a => a != null);

        foreach (var attr in attrs)
        {
            foreach (var cat in Enum.GetValues<DoctrineCategory>())
            {
                if (cat != DoctrineCategory.None && attr!.ValidCategories.HasFlag(cat))
                    map[cat].Add(attr.BehaviorId);
            }
        }
        return map;
    }

    public static IReadOnlyList<string> GetValidDoctrines(long tkbType)
    {
        var cat = MapTkbTypeToCategory(tkbType);
        return _map.TryGetValue(cat, out var list) ? list : _map[DoctrineCategory.None];
    }

    private static DoctrineCategory MapTkbTypeToCategory(long tkbType) => tkbType switch
    {
        TkbEntityTypes.CivilianPedestrian => DoctrineCategory.Civilian,
        TkbEntityTypes.MilitaryApc        => DoctrineCategory.MilitaryApc,
        TkbEntityTypes.Insurgent          => DoctrineCategory.Insurgent,
        _                                 => DoctrineCategory.None
    };
}
```

### 5.7 Update `CgfNodes.cs` to Use DTO Constants

**File:** `Hrot/Subsystems/Hrot.CGF/Brains/CgfNodes.cs`

The inline JSON blobs contain `"TreeName": "FireAtTarget"` etc. Replace with the `const string BehaviorId` defined on the DTO:

```csharp
private static readonly string FireAtTargetJson = $$"""
{
  "TreeName": "{{FireAtTargetParamsJsonDto.BehaviorId}}",
  ...
}
""";
```

Apply the same pattern to all other AI tree JSON definitions in the file.

### 5.8 Create `DoctrineTestHelper` and Update Unit Tests

**New file:** `Hrot/Engine/Hrot.Core/MapDefinitions/Doctrine/DoctrineTestHelper.cs` (or in a test-helper project)

```csharp
public static class DoctrineTestHelper
{
    public static string GetBehaviorId<TDto>()
    {
        var attr = typeof(TDto).GetCustomAttribute<DoctrineContractAttribute>()
            ?? throw new InvalidOperationException(
                $"{typeof(TDto).Name} is missing [DoctrineContractAttribute]");
        return attr.BehaviorId;
    }
}
```

Update test files that hardcode behavior-ID strings (e.g., `BehaviorRemappingTests.cs`, `MissionPanelTests.cs`) to derive the string via `DoctrineTestHelper.GetBehaviorId<FireAtTargetParamsJsonDto>()`.

---

## Dependency Graph Summary

```
Phase 1 (Dead Code)
    |
    v
Phase 2 (Ordinals)        Phase 4 (Manual Phase)
    |                           |
    v                           v
Phase 3 (Interface Segregation) ---- consumes INetworkTranslator base
                                      (can proceed after Phase 1)
Phase 5 (Doctrine) -- fully independent
```

Phases 1, 2, 4, and 5 can be worked on simultaneously. Phase 3 must be executed after Phase 1 because Phase 3 removes members from `CycloneNativeEventTranslator` that were previously tied to `INetworkReplayTarget` / `IDescriptorTranslator`.

---

## Project Dependency Checks

- `TimeDescriptorType` in `Fdp.Toolkit.Time` must NOT reference `Hrot.NED.Descriptors`.
- `BdcDescriptorType` in `Hrot.Network.BDC` must NOT reference `Hrot.Network.NED`.
- `DoctrineContractAttribute`/`DoctrineCategory` in `Hrot.Core` must NOT reference `Hrot.Presentation` or `Fdp.Toolkit.Behavior` (those reference `Hrot.Core`, not the other way around).
- `DoctrineSchemaDiscovery` must live in a project that already references BOTH `Hrot.Core` AND `Fdp.Toolkit.Behavior` (and optionally `Hrot.Presentation`). Candidate: `Hrot.Presentation` or `Hrot.CGF`. Verify with the `.csproj` dependency graph before deciding.
- `INetworkTranslator` in `Fdp.Core` is the root interface. All other network abstractions (`IDescriptorTranslator`, `INetworkEventTranslator`) live in the same `Fdp.Interfaces` namespace within `Fdp.Core`. No new project dependencies are created.
