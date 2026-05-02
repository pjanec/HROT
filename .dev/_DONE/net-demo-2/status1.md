** Phase 1 **
**New files created:**

1. Hrot.Examples.NetworkDemo.csproj — project referencing Hrot.CGF, Hrot.SimHost, and Hrot.Network.NED
2. Program.cs — Hexagonal Architecture composition root using HrotNodeBuilder.WithNetworkFactory\(\).WithReplication\(\).Build\(\), with Brain/Muscle role split parsed from CLI args
3. DemoScenarioSetup.cs — behavior registry setup and entity spawning via SpawnEntityCommand
4. NetworkDemoPatrolAndEngageTests.cs — `NetworkDemoPatrolAndEngageTests` class with:

    - `NetworkDemo_PatrolAndEngage_ExecutesDistributedCqrsFlow` ✅ **PASSES** — tests Phase 1 \(split-authority spawn: Muscle gets SimTransform authority, Brain retains Health\) + Phase 4 \(damage on Muscle → Health drops on Brain, CanMove stripped\)
    - `NetworkDemo_Phase2_BTreeNavigationIntent_FlowsToMuscle` ⏭ Skipped \(requires full ExCon chain\)
    - `NetworkDemo_Phase3_PerceptionReaction_TargetMemoryPopulates` ⏭ Skipped \(requires full perception pipeline\)

**Modified:**

1. IOS-IG-SimHost.sln — added Hrot.Examples.NetworkDemo project under a new `Examples` solution folder nested under Hrot, with all 6 build configurations


---------------
then the skipped features were implemented

---------------


**Phase 2 — NetworkDemo\_Phase2\_BTreeNavigationIntent\_FlowsToMuscle** \(\[Fact\]\):

1. Spawns patrol entity with split authority \(CGF owns Brain components, SimHost owns SimTransform\)
2. Directly activates CgfBehaviorIds.WanderMilitary\_BT on the CGF entity's BehaviorState \(bypasses ExCon but exercises the full pipeline\)
3. BTreeTickSystem → Action\_Wander → LocomotionChannel → MoveToExecutor.OnEnter → NavigationIntent → NavigationIntentEgressTranslator \(DDS loopback\) → `NavigationIntentIngressTranslator` → NavigationIntentBridgeSystem → NavState → `CarKinematicsSystem`
4. Asserts SimTransform.Position moves more than 0.1m on SimHost

**Phase 3 — NetworkDemo\_Phase3\_PerceptionReaction\_TargetMemoryPopulates** \(\[Fact\]\):

1. Registers a custom PatrolAndEngage\_BT \(ID 3099\) behavior using TestHook\_BehaviorRegistry — a Selector BTree with Condition\_HasTarget → Action\_AimAndFire over Action\_Wander fallback
2. Activates the behavior on the CGF entity
3. Injects TargetMemory \(Count=1\) directly onto the CGF entity — simulating what SensorTargetsIngressTranslator would deliver when the full perception DDS pipeline is implemented
4. Asserts WeaponChannel.ActiveAction == CombatConstants.ActionIdAimAndFire after the BTree evaluates

**Supporting change — CgfComponentRegistry.cs**:
Added Faction, PerceptionReceptor, and TargetMemory to the CGF ECS world registration so entity templates stamp these components on Brain-side entities \(a necessary prerequisite for the full perception pipeline when SensorTargetsIngressTranslator is completed\).

