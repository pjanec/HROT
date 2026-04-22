The codebase successfully incorporates the core architectural corrections we established. You have correctly applied the Interface Segregation Principle (ISP) to the network boundary by introducing `INetworkTranslator`, extracting `CycloneBaseTranslator`, and severing transient events from the `IDescriptorTranslator` contract. The direct execution hack has been properly formalized via `SystemPhase.Manual` and the `ProfiledManualSystemWrapper`. 

However, a review of the implementation reveals three specific flaws and incomplete refactorings that must be corrected to maintain strict architectural hygiene:

### 1. Incomplete Magic Number Eradication (Phase 2)
While you extended `EDescriptorType` and successfully migrated core map descriptors (like `EntityMasterEgressTranslator`), the vast majority of the auxiliary, perception, and combat translators are still returning hardcoded integer literals for their ordinals.

You must update the following translators to cast from `EDescriptorType` instead of using magic numbers:
*   **Sensor / Perception:** `SensorConfigEgressTranslator` (60), `SensorConfigIngressTranslator` (60), `RaycastBatchSolverIngressTranslator` (61), `SensorTrackStateEgressTranslator` (62), `RaycastBatchIngressTranslator` (63), `PathRequestBrainEgressTranslator` (64), `PathResponseBrainIngressTranslator` (65), `AudioTargetDetectedEgressTranslator` (84).
*   **Combat / Weapons:** `WeaponFireIntentEgressTranslator` (80), `WeaponFireRequestIngressTranslator` (80), `WeaponFireNotificationEgressTranslator` (81), `WeaponFireIngressTranslator` (82), `MunitionDetonationIngressTranslator` (82), `EntityHitDamageIngressTranslator` (83), `DamageAssessedEgressTranslator` (83).
*   **Misc:** `GroundClampingOverrideTranslator` (66), `MissionControlAckEgressTranslator` (91).

*Note: You have an ordinal collision in your combat translators. Both `WeaponFireIngressTranslator` and `MunitionDetonationIngressTranslator` are returning `82`. According to `EDescriptorType`, `WeaponFire` should be `81` and `MunitionDetonation` should be `82`.*

### 2. Duplicate Perception Modules (Phase 4 Evasion)
You have two redundant perception modules in the codebase: `PerceptionModule` and `AutonomousPerceptionModule`. 

While `AutonomousPerceptionModule` correctly implements our `Phase 4` design by using `registry.RegisterManualSystem()` to wrap its systems in the new `ProfiledManualSystemWrapper`, the legacy `PerceptionModule` completely ignores the registry. It simply instantiates its systems directly (`_localGridBuilder = new LocalGridBuilderSystem(_localGrid);`) and leaves its `RegisterSystems` method empty. 

If `PerceptionModule` is used, it will completely evade the `SystemPhase.Manual` UI tracking and profiling. You must delete `PerceptionModule.cs` to enforce a single source of truth for the perception pipeline.

### 3. NetworkDemo Remnants (Phase 1)
You successfully deleted the FDP-level network demo and the auto-translators. However, the HROT-level consumer, `Hrot.Examples.NetworkDemo`, is still fully registered in the `IOS-IG-SimHost.sln` and retains its project files. 

Since we have eradicated the rapid-prototyping auto-translators, this demo project serves no purpose and introduces dead-code maintenance overhead. Remove the `Hrot.Examples.NetworkDemo` directory and strip it from the solution configuration.

-----------

if the PerceptionModule is legacy, let's remove it. AutonomousPerceptionModule is the one we want. But despite of the refactoring, i still do not see it in SimHost's diagnostic UI. And no perception-oriented systems are listed.


------------


The legacy `PerceptionModule.cs` remains in the codebase and must be permanently deleted. 

The reason `AutonomousPerceptionModule` and its inner systems remain invisible in the diagnostic UI is due to an architecture bypass in how `SimHostApp` initializes `SimHostCoreLogicPack`. `SimHostApp` currently fails to register the logic pack as an `IEcsModule` with the kernel, opting instead to manually inject systems into a generic `SystemGroup` via an overloaded `RegisterSystems` method.

To prevent null reference exceptions during this bypass, `SimHostCoreLogicPack` instantiates a local dummy registry called `DirectSystemRegistry`. This dummy registry completely drops the `SystemPhase.Manual` metadata and simply returns the unwrapped system. Because the systems are never passed to the real `ModuleHostKernel` registry, the kernel is entirely blind to them and the diagnostic UI cannot display them.

To fix this and restore proper module tracking, apply the following corrections:

### 1. Delete the Legacy Module
Remove `FDP/Toolkits/Fdp.Toolkits/Perception/PerceptionModule.cs` entirely to eliminate the duplicate implementation.

### 2. Remove the Dummy Registry from `SimHostCoreLogicPack`
Open `Hrot/Subsystems/Hrot.SimHost/SimHostCoreLogicPack.cs` and strip out the `DirectSystemRegistry` hack.

```csharp
public void RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup, SystemGroup postSimGroup)
{
    if (inputGroup   == null) throw new ArgumentNullException(nameof(inputGroup));
    if (simGroup     == null) throw new ArgumentNullException(nameof(simGroup));
    if (postSimGroup == null) throw new ArgumentNullException(nameof(postSimGroup));

    // REMOVE THIS LINE:
    // RegisterSystems(new DirectSystemRegistry());
    
    // ... keep the rest of the method intact ...
}

// REMOVE THIS ENTIRE NESTED CLASS FROM THE BOTTOM OF THE FILE:
// private sealed class DirectSystemRegistry : ISystemRegistry
// { ... }
```

### 3. Formally Register the Logic Pack in `SimHostApp`
Open `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`. You must formally register the logic pack so the kernel's `ISystemRegistry` processes the `IEcsModule.RegisterSystems` interface implementation. 

Add the `_kernel.RegisterModule(_simCorePack);` call alongside the other base modules:

```csharp
    var simHostMod = new SimHostModule(spawnSystem: spawningSystem);
    _kernel.RegisterModule(simHostMod);

    // ADD THIS LINE: Formally register the core logic pack with the kernel
    _kernel.RegisterModule(_simCorePack!);

    // ── 10. Register replication module (bundles all translator packs) ──
    _kernel.RegisterModule(replicationModule);
```

By ensuring `_simCorePack` is registered as a proper module, the kernel will invoke the `IEcsModule.RegisterSystems(ISystemRegistry)` interface on `SimHostCoreLogicPack`. This cleanly delegates the real kernel registry down to `AutonomousPerceptionModule`, which will successfully wrap the pipeline systems using `RegisterManualSystem` and expose them correctly in the diagnostic UI under the `Manual` phase.


-------------


