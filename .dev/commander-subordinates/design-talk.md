
I would like to design a group behavior for a hill attack, see the rough ideas in "initial-idea". To be compliant with FDP AI mechanisms. First we might need to establish an infrastructure for the leader (commander) of the military unit to control his subordinates. So the leader knows the list of his subordinates, and subordinates know their leader. Like the leader should have a component listing his direct subordinate entities. There should be a virtual entity for the unit commader that has no physical representation but has its own "brain", i.e. can execute behaviors and give orders (tactical intents) to subordinate entities and monitor their execution. There might be something already available for formations (formation roster?), best if we can unify what can be unified (roster = list of subordinates) but keep separate what is specific for formation (formation steering is high rate on contrary to the tactical intent which is sent infrequently)

To implement the "hill attack" group behavior while remaining perfectly compliant with FDP's AI mechanisms, we can map your conceptual ideas directly to the engine's existing ECS and Cognitive Tier components.

Here is how you can establish the commander-subordinate infrastructure, unify the roster, and orchestrate the tactical intents.

1\. The Virtual Commander & Subordinate Roster

FDP already supports virtual commander entities through composite units like `Unit_TankPlatoon` (TKB Type 301). This entity acts as the tactical brain but has no physical render model.

To link the commander and subordinates, you can absolutely unify the roster using the existing formation components, while safely decoupling the high-rate kinematic steering from the low-rate tactical logic.

-   **The Roster (Leader Side):** The commander entity should carry the `FormationRoster` component, which stores an inline, zero-allocation array of up to 16 `MemberEntities` along with the active `FormationType`.-   **The Subordinates (Tank Side):** Each tank carries a `FormationMember` component pointing back to the `LeaderEntityId`. They also carry `EntityInfo` with the `CommanderId` field for standard hierarchical tracking.-   **Decoupling Steering from Tactics:** High-frequency formation steering only happens when a tank's `NavState.Mode` is set to `KinematicsMode.Formation`. During the hill attack, the commander will issue a tactical order that temporarily changes the tank's behavior. The tank's new behavior will write `ActionIdMoveTo` to its `LocomotionChannel`, switching its `NavState` to `DirectPoint` or `CustomTrajectory`. This safely breaks them out of the rigid kinematic formation for the duration of the attack, while leaving the `FormationRoster` structurally intact.

2\. Commander's Brain: `PlatoonHillAttack_BT`

The commander requires a complex, multi-phase decision process, which perfectly suits the **Tier 2 FastBTree** paradigm.

Because the commander needs to calculate and store the "firing line" (palebná čára) and multiple target slots for its tanks, it will exceed the 60-byte limit of the standard `BrainBlackboard`.

-   **Heavy Working Memory:** You should create a dedicated ECS component (e.g., `FiringLinePlan`) or use the provided 1024-byte `Blackboard1024` component.-   **Actions:** The commander's BTree will use `[SharedAiHeavyAction]` delegates to calculate the firing line geometry and assign slots to the tanks, utilizing the `EntityRepository` to mutate the heavy data components.

3\. Giving Orders: Tactical Intents

Your idea of a `TacticalOrderChannel` is spot on, but FDP already has a standardized, built-in mechanism for this: **AssignTacticalIntentEvent**.

Instead of writing to a channel every frame, the commander's BTree executes an action (e.g., `Action_IssueTacticalIntent`) that publishes an `AssignTacticalIntentEvent` to the event bus for each tank.

-   **The Intent:** The event carries an `IntentId` (e.g., `"HillAttackRun"`) and a JSON parameter string containing the specific firing slot coordinates.-   **Resolution:** The `TacticalIntentResolutionSystem` intercepts this event. It uses a registered `ITacticalOrderMapper` to translate `"HillAttackRun"` into a concrete behavior assignment (like `"HullDownAttack_BT"`) specific to the M1 Abrams or T-72.-   **Ingress:** Finally, `BehaviorIngressSystem` smoothly transitions the tank's brain into the new behavior without manual ECS component manipulation.

4\. Tank Execution: `HullDownAttackRun_BT`

Once the tank receives the intent, its brain switches to the `HullDownAttackRun_BT` behavior.

-   **Execution:** The tank uses standard `[SharedAiAction]` delegates to write high-rate actuator intents to its `LocomotionChannel` (to creep forward/reverse) and `WeaponChannel` (to fire).-   **Monitoring & Reporting:** You do not need a custom `TacticalReport` component. In FDP, when a BTree's root node evaluates to a terminal state (`Success` or `Failure`), the `BTreeTickSystem` automatically publishes a **BehaviorFinishedEvent**.

**Closing the Loop:** The commander's BTree can utilize an Observer node (or a custom condition checking the tank's `BehaviorState.ActiveBehaviorHash`) to see when tanks have finished their run. Alternatively, the `MissionDirectorSystem` can listen for the `BehaviorFinishedEvent` to advance the commander's high-level mission phases automatically. Once all tanks report success, the commander issues a final intent (e.g., `"JoinFormation"`) returning the tanks to their standard escort formation.

reagrding the unification with formations: the current component naming is very formation oriented. We are moving towards more generic "unit subordination" orientation, where the formation is just one possible kind of subordination, next to executing tactical intents. I would like to separate the formation specific ECS data from the generic commaner-subordinate relationships. Pls suggest new component names and content to better separate the concerns and still be intuitive.

This is an excellent architectural shift. Decoupling the logical command hierarchy from physical kinematic steering is exactly how an Entity Component System (ECS) is meant to be composed. By separating these concerns, an entity can remain an active subordinate in a platoon while temporarily breaking formation to execute an independent tactical maneuver (like your hill attack), without losing its place in the unit's roster.

Here is a proposed structure to separate the concerns into **Generic Subordination** components and **Kinematic Formation** components.

1\. Generic Unit Subordination Components

These components define the pure command hierarchy. They are evaluated by the cognitive tier (Behavior Trees/HSMs) when making tactical decisions, such as a commander iterating over its units to issue an `AssignTacticalIntentEvent`.

**For the Commander:** `UnitRoster` This component strips out all the spatial formation data from the old `FormationRoster` and acts purely as a fast, zero-allocation array of subordinate entities.

```
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.UnitRoster)] // (Assign a new ID)
public unsafe struct UnitRoster
{
    public int Count; // Number of active subordinates (0-16)
    
    // Fixed-capacity array (zero GC, cache-friendly)
    public fixed long SubordinateEntities[1]; // Full Entity (8 bytes: ID + Generation)
    public fixed ushort TacticalDesignations[1]; // Logical role/index (e.g., Platoon Tank 2)
}
```

**For the Subordinate:** `UnitSubordinate` This replaces the hierarchical back-pointer of the old `FormationMember`, allowing the tank's brain to easily query its leader without needing to know anything about formations.

```
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.UnitSubordinate)] // (Assign a new ID)
public struct UnitSubordinate
{
    public int CommanderEntityId; // Entity ID of the platoon commander
    public ushort TacticalDesignation; // Matches the designation in the UnitRoster
}
```

2\. Formation-Specific Kinematic Components

These components hold the spatial and physical parameters required by the `FormationTargetSystem` and `CarKinematicsSystem` to calculate steering. They are strictly muscle-tier (or kinematic-tier) data.

**For the Commander:** `FormationController` This extracts the configuration parameters from the old `FormationRoster`. It dictates the shape of the formation that subordinates should slot into when they are in formation mode.

```
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.FormationController)] // (Replaces FormationRoster ID)
public struct FormationController
{
    public FormationType Type;        // Column, Wedge, Line, Custom
    public int TemplateId;            // Index into formation template blob
    public FormationParams Params;    // Spacing, break distances, etc.
}
```

**For the Subordinate:** `FormationFollower` This replaces the remaining state-tracking fields in `FormationMember`. It is only actively processed when the tank is actually trying to maintain a formation slot.

```
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.FormationFollower)] // (Replaces FormationMember ID)
public struct FormationFollower
{
    public ushort SlotIndex;            // Which slot in the template (0-15)
    public FormationMemberState State;  // InSlot, CatchingUp, Rejoining, etc.
    
    // State tracking
    public float SlotDistFiltered;      // Low-pass filtered distance to slot
    public float RejoinTimer;           // Time spent in Rejoining state
    public byte IsInFormation;          // 1 = active steering, 0 = temporarily suspended
}
```

How this benefits the "Hill Attack" Scenario

Under this unified architecture, your scenario flows cleanly without structural ECS friction:

-   **The Roster is Permanent:** The commander entity holds a `UnitRoster`, and the 4 tanks hold `UnitSubordinate`. These components are applied when the platoon spawns and **never change** during the mission, regardless of whether the tanks are driving in a wedge, hiding behind a hill, or repairing.-   **Assigning the Attack:** The commander's `PlatoonHillAttack_BT` reads the `UnitRoster`, computes the 4 firing slots, and fires an `AssignTacticalIntentEvent` for each tank.-   **Breaking Formation:** When the tanks receive the intent, their behavior changes to `HullDownAttackRun_BT`. As their new BTree writes `ActionIdMoveTo` into the `LocomotionChannel`, their `NavState.Mode` drops out of `KinematicsMode.Formation`.-   **Clean Decoupling:** Because you separated the components, the `FormationTargetSystem` naturally ignores the attacking tanks (since they aren't actively steering in formation), but the Commander still knows exactly who its subordinates are via the `UnitRoster` to monitor their `BehaviorFinishedEvent` success reports.

the CommanderEntityId in UnitSubordinate looks same as the CommanderId in the EntityInfo. Should we remove the CommanderId from the EntityInfo?

Systém nedokázal odpovědět.

the CommanderEntityId in UnitSubordinate looks same as the CommanderId in the EntityInfo. Should we remove the CommanderId from the EntityInfo? moreover in the Fdp.Core.EntityInfo component the CommanderId is 'int' which is wrong (should be Enity with index and generation)

Systém nedokázal odpovědět.

we should resolve the friction with EntityInfo component. the CommanderEntityId in UnitSubordinate looks same as the CommanderId in the EntityInfo. Should we remove the CommanderId from the EntityInfo? moreover in the Fdp.Core.EntityInfo component the CommanderId is 'int' which is wrong (should be Enity with index and generation)

You are absolutely correct to point out the friction and the danger of using a raw `int` for an entity reference. In the FDP engine, storing a raw integer instead of the 8-byte `Entity` struct (which includes the generation counter) defeats ECS memory safety and leads to "zombie" references when entity indices are recycled.

However, the `int CommanderId` in `Fdp.Core.EntityInfo` exists for a very specific reason: **it is a Network ID, not a local ECS handle.**

Because `EntityInfo` is a direct mapping of the DDS network descriptor `Hrot.NED.Descriptors.EntityInfo`, its `CommanderId` field holds the globally unique Network Entity ID used to construct the ORBAT (Order of Battle) tree across the distributed cluster.

Here is how we should resolve the friction and properly separate the concerns:

1\. Rename the field in `EntityInfo` (Presentation / Network Tier)

We should keep the commander reference in `EntityInfo`, but explicitly rename it to clarify its network origin. This component should strictly remain UI/Presentation data.

```
[ComponentId(GlobalComponentIds.EntityInfo)]
public struct EntityInfo
{
    public FixedString64 Name;
    public ForceId ForceId;
    public int CommanderNetworkId; // Renamed to explicitly denote it is NOT a local ECS Entity
}
```

2\. Fix the `UnitSubordinate` Component (Cognitive / AI Tier)

Your proposed `UnitSubordinate` component is the correct place for the **local ECS reference**. It should use the generation-safe `Entity` struct so the AI BTree/HSM can safely query the commander without risk of accessing a destroyed entity.

```
[ComponentId(GlobalComponentIds.UnitSubordinate)] 
public struct UnitSubordinate
{
    public Entity Commander; // Safe 8-byte handle (Index + Generation) [1]
    public ushort TacticalDesignation;
}
```

3\. Bridge the Network ID to the Local ECS Entity

To connect the two, we can use the exact same pattern already established by the `GenesisMaterializationSystem`. Currently, the engine uses this system to resolve network IDs (like `InitialHierarchyIntent.ParentNetworkId`) into live `Entity` handles for the Vis2D hierarchy.

We can add a small routine to the materialization pipeline (or a dedicated system) that reads the `CommanderNetworkId` from `EntityInfo`, looks it up in the `NetworkEntityMap`, and attaches/updates the `UnitSubordinate` component with the live `Entity` handle.

**Summary of the Architecture:**

-   **EntityInfo.CommanderNetworkId** **(int):** Used by the ExCon/IG to draw the ORBAT tree based on network data.-   **UnitSubordinate.Commander** **(Entity):** Used by the `HullDownAttackRun_BT` and local AI systems to execute tactical intents, completely decoupled from the network layer and fully compliant with FDP's strict memory safety rules.

but internal ECS components do not need to keep the network ids of entities because we have network mapper in the ACL layer (the network interface), correct? Keeping it in ECS would duplicate the commander refernce across multiple components which is difficult to maintain. Can't we simply keep the Commander reference just in the generic UnitSubordinate component and remove it from the EntityInfo component?

While it is true that the ACL layer's `NetworkEntityMap` handles translations between local `Entity` handles and global network IDs, you cannot completely remove the `CommanderId` from the `Fdp.Core.EntityInfo` component.

Here is why keeping the network ID in `EntityInfo` is a necessary architectural decision rather than bad duplication:

**1\. The Network Protocol Contract** The `Fdp.Core.EntityInfo` ECS component acts as the 1:1 local data source for the `Hrot.NED.Descriptors.EntityInfo` DDS network descriptor. The DDS message explicitly requires the integer `CommanderId` to construct the distributed Order of Battle (ORBAT) tree across all nodes. The `EntityInfoEgressTranslator` reads this exact field from the ECS component to populate the outgoing network packet.

**2\. Separation of Concerns (Presentation vs. AI)** `EntityInfo` is a presentation and networking component used by tools like the Image Generator (IG) and Exercise Control (ExCon) to draw UI labels and hierarchies. If you removed `CommanderId` from `EntityInfo`, the `EntityInfoEgressTranslator` would be forced to query your new `UnitSubordinate` AI component, pull the local `Entity` handle, and run it through the `NetworkEntityMap` to figure out what integer to send over the network.

This would tightly couple the generic, low-level network replication layer to high-level, tactical AI logic. Presentation nodes (like the IG) do not run the cognitive AI tier, so coupling presentation networking to AI components would break the engine's strict decoupling rules.

**The Verdict: Intentional Boundary** The duplication here represents a clean boundary between two different domains:

-   **Presentation / Network Tier:** `EntityInfo.CommanderId` (integer) is used strictly for distributed UI and ORBAT synchronization.-   **Cognitive / AI Tier:** `UnitSubordinate.Commander` (local 8-byte `Entity`) is used strictly for zero-allocation AI BTree/HSM logic.

To manage the duplication seamlessly, you can use the `NetworkEntityMap` exactly as you suggested, but as a bridge during initialization: when a unit spawns or ingresses via the `EntityInfoIngressTranslator`, a lightweight initialization system can read the `CommanderId` integer, resolve it via the `NetworkEntityMap`, and safely populate the local `Entity` reference into your AI-focused `UnitSubordinate` component.

what you described for the EntityInfoEgressTranslator is exactly what it should be doing - translating in the ACL layer. Looks perfectly sound to me. And EntityInfoEgressTranslator can and should map the network descriptor into internal ECS components. There is no need to keep 1:1 relationship between network descriptor and internal ECS componentc. network ingress and egress translators are perfectly capable of accessing multiple ECS components, handling M:N relationships. I do not want to duplicate the commander references across components. It must be in one single component. Translating to network ids should happen when needed and on right place (in the ACL)

You are absolutely right, and this is a much cleaner, fully normalized approach. Keeping the 1:1 relationship between network descriptors and ECS components is not required by the FDP engine, and pushing the ID resolution into the ACL (translators) perfectly aligns with the engine's design.

The engine's translators are explicitly designed to handle M:N component relationships. For example, the `GeoSpatialEgressTranslator` currently combines data from `SimTransform`, `NetworkTransform`, and `NetworkVelocity` to construct a single `WorldPos` DDS message.

By offloading the network ID resolution to the ingress and egress translators, you eliminate the data duplication and guarantee a single source of truth in the ECS.

Here is how you can implement this cleanly in the ACL:

**1\. Clean Up the ECS Components** Remove the raw integer `CommanderId` from the presentation component entirely. The only place the relationship will exist locally is in your new AI-tier component:

```
[ComponentId(GlobalComponentIds.UnitSubordinate)]
public struct UnitSubordinate
{
    public Entity Commander; // The Single Source of Truth
    public ushort TacticalDesignation;
}
```

**2\. Update the Egress Translator (****EntityInfoEgressTranslator****)** The `EntityInfoEgressTranslator` already has the `NetworkEntityMap` injected into its constructor. You will expand its `ScanAndPublish` method to check for `UnitSubordinate` and translate the local `Entity` reference into the outgoing DDS integer.

-   **Query:** The translator queries the entity. If the entity has a `UnitSubordinate` component, it grabs the `Commander` entity handle.-   **Translation:** It calls `_entityMap.TryGetNetworkId(subordinate.Commander, out long commanderNetId)`.-   **Publish:** It assigns the resulting `commanderNetId` to the `CommanderId` field of the outgoing `Hrot.NED.Descriptors.EntityInfo` packet.

**3\. Update the Ingress Translator (****EntityInfoIngressTranslator****)** Similarly, `EntityInfoIngressTranslator` already has access to the `NetworkEntityMap` and the `IEntityCommandBuffer`.

-   **Translation:** When the `EntityInfo` packet arrives from the network, the translator extracts the `info.CommanderId` (int) and resolves it to a local entity using `_entityMap.TryGetEntity(info.CommanderId, out Entity commanderEntity)`.-   **Ingress:** It then uses the command buffer to either add or update the `UnitSubordinate` component on the target entity with the resolved `commanderEntity` handle.

**Why this is the perfect solution:** By making the `EntityInfoEgressTranslator` and `EntityInfoIngressTranslator` act as true Anti-Corruption Layers, your cognitive systems (like `HullDownAttackRun_BT`) only ever deal with safe, generation-checked `Entity` handles. Meanwhile, the network layer seamlessly constructs the integer-based ORBAT tree required by ExCon and the Image Generator without leaking networking concerns or duplicating state into the local ECS.

Introducing UnitSubordinate component means we need to extend the entity defintions in the TKB and make sure this component gets created on all relevant nodes when we create the entity

To ensure your new `UnitSubordinate` (and `UnitRoster`) components are consistently instantiated when an entity is created, you must indeed extend the Transient Knowledge Base (TKB) blueprints.

Here is how you can implement this cleanly across the TKB and ECS initialization pipeline:

**1\. ECS Component Registration** Before the TKB can use the new components, they must be registered with the engine.

-   Assign them a unique constant in your project's `ComponentIds` (or the 160–199 application-level block if using `HrotComponentIds`).-   Register them during world initialization (e.g., inside `CognitiveComponentRegistry.RegisterAll` or `HrotSharedComponentRegistry.RegisterAll`) using `world.RegisterComponent<UnitSubordinate>()`.

**2\. Extending the TKB Blueprints** You will add the zero-initialized components to the relevant templates in your builder (such as `NedTkbBuilder` or `DemoTkbSetup`).

-   **For Subordinates (Tanks, Infantry):** Add the component to tactical units inside the builder setup.-   **For Commanders (Platoons, Squads):** Attach the roster to composite units.

Because `AddComponent` copies the unmanaged struct by value, every entity spawned from this template will automatically carry these components in its `ComponentMask`.

**3\. Resolving Live Entity Handles at Runtime** The TKB blueprints are static and applied at spawn time. Because `UnitSubordinate.Commander` relies on a live 8-byte local `Entity` handle (Index + Generation), it cannot be baked into the TKB template. The TKB will correctly spawn the component, but the commander handle will default to `Entity.Null`.

To populate the live handles, you rely on the Anti-Corruption Layer (ACL) pattern we discussed, leveraging the engine's existing materialization mechanics:

-   In FDP, components holding local ECS handles are typically hydrated using transient "Intent DTO" components (like `InitialHierarchyIntent`).-   During the `SystemPhase.Input` tick, the **GenesisMaterializationSystem** queries the `NetworkEntityMap`.-   It reads the `EntityInfo.CommanderId` network integer, resolves it to the live `Entity` handle, and writes it directly to the ECS.-   Because FDP's `EntityRepository.SetComponent<T>` has built-in **upsert behavior**, calling it safely overwrites the zero-initialized TKB placeholder with the fully resolved command hierarchy without duplicating components or throwing errors.

pls detail the changes necessary in the network translators and GenesisMaterializationSystem to properly initialize the commander reference in the new UnitSubordinate component

To properly initialize the `Commander` reference in the new `UnitSubordinate` component, you must implement changes across two distinct initialization pipelines: **Scenario Loading (Genesis)** and **Live Network Traffic (DDS)**.

1\. Scenario Loading (Genesis Pipeline)

When loading a scenario from disk, network IDs must be resolved to live `Entity` handles. The engine uses "Intent" DTO components to temporarily hold network IDs until all entities in the scenario are spawned.

**Step A: Create an Intent Component** Create a transient managed component to store the commander's network ID during initialization.

```
[DataPolicy(DataPolicy.Transient)]
[ComponentId(HrotComponentIds.InitialUnitSubordinateIntent)] // Assign new ID
public sealed class InitialUnitSubordinateIntent
{
    public long CommanderNetworkId { get; set; }
    public ushort TacticalDesignation { get; set; }
}
```

**Step B: Write a Scenario Translator** Implement an `IEntityScenarioTranslator` (e.g., `UnitSubordinateTranslator`) to handle the JSON save/load process.

-   **Extract (Save):** Read the `UnitSubordinate.Commander` `Entity` handle and convert it to a stable GUID string using `IGuidResolver.Resolve(handle)`.-   **Inject (Load):** Read the GUID string from the JSON, resolve it to a network ID, and attach the `InitialUnitSubordinateIntent` to the entity.

**Step C: Update** **GenesisMaterializationSystem** Modify `GenesisMaterializationSystem.Execute` to process the new intent once all entities are alive in the `NetworkEntityMap`.

```
private void MaterializeUnitSubordinate(ISimulationView view, EntityCommandBuffer cmd, EntityRepository repo)
{
    foreach (var entity in view.Query().WithManaged<InitialUnitSubordinateIntent>().Build())
    {
        var intent = view.GetManagedComponentRO<InitialUnitSubordinateIntent>(entity);
        
        // Wait until the commander entity is spawned and alive in the local ECS
        if (!_entityMap.TryGetEntity(intent.CommanderNetworkId, out var commander) || !view.IsAlive(commander))
            continue;

        // Write the structural ECS component with the safe Entity handle
        repo.SetComponent(entity, new UnitSubordinate 
        { 
            Commander = commander,
            TacticalDesignation = intent.TacticalDesignation
        });
        
        // Remove the transient intent
        cmd.RemoveManagedComponent<InitialUnitSubordinateIntent>(entity);
    }
}
```

2\. Live Network Traffic (DDS Translators)

For live network traffic, the Anti-Corruption Layer (ACL) translators must map the raw integer `CommanderId` from the `Hrot.NED.Descriptors.EntityInfo` network packet to the local `UnitSubordinate` component.

**Step A: Update** **EntityInfoIngressTranslator** Modify the `ProcessSample` method to resolve the incoming integer ID into a local `Entity` handle and apply it to the `UnitSubordinate` component.

```
internal void ProcessSample(Hrot.NED.Descriptors.EntityInfo info, long netId, EntityRepository? repo = null)
{
    // ... existing EntityInfo extraction ...
    
    // Resolve the commander network ID to a local ECS Entity
    Entity commanderEntity = Entity.Null;
    if (info.CommanderId != 0)
    {
        _entityMap.TryGetEntity(info.CommanderId, out commanderEntity);
    }

    var subordinateData = new UnitSubordinate 
    { 
        Commander = commanderEntity 
    };

    if (repo != null && _entityMap.TryGetEntity(netId, out var entity))
    {
        repo.SetComponent(entity, subordinateData);
        return;
    }

    // Fallback for IG node: dispatch via event bus
    _eventBus.PublishManaged(new UpdateEntityCommand
    {
        NetworkId = netId,
        ComponentsToUpdate = new List<object> { subordinateData /*, existing igData */ },
        RequestId = Guid.Empty,
    });
}
```

_Note: If the commander entity does not exist yet when the packet arrives, you can rely on subsequent_ _EntityInfo_ _updates (which use TransientLocal durability) to eventually populate it__, or you can adapt a queueing mechanism similar to_ _MapRouteIngressTranslator__._

**Step B: Update** **EntityInfoEgressTranslator** Update `ScanAndPublish` to build the outgoing DDS packet using the AI-tier `UnitSubordinate` component rather than reading from presentation data.

```
// Query entities that have both NetworkIdentity and UnitSubordinate
var query = view.Query()
    .With<NetworkIdentity>()
    .With<UnitSubordinate>()
    .WithLifecycle(EntityLifecycle.All)
    .Build();

foreach (var entity in query)
{
    // ... authority checks and SmartEgress guards ...

    ref readonly var subordinate = ref view.GetComponentRO<UnitSubordinate>(entity);
    
    // Resolve the local Entity handle back to a global network ID
    int commanderNetId = 0;
    if (!subordinate.Commander.IsNull)
    {
        _entityMap.TryGetNetworkId(subordinate.Commander, out long netId);
        commanderNetId = (int)netId;
    }

    _writer.Write(new Hrot.NED.Descriptors.EntityInfo
    {
        EntityId        = (int)netId.Value,
        CommanderId     = commanderNetId,
        // ... populate Name and ForceIdentifier ...
    });
}
```

I think we need to adapt a queueing mechanism similar to MapRouteIngressTranslator because the entity creation order is not guaranteed. This is a general issue for any entity references...

You are completely right. Because DDS (especially with UDP/best-effort QoS) does not guarantee that network packets arrive in strict creation order, a subordinate's packet can easily arrive before the commander's `EntityMaster` packet has spawned the commander's ghost entity.

Using the highly optimized queueing pattern established by `MapRouteIngressTranslator` is the perfect solution for this.

Instead of doing an expensive O(N) scan of all pending references every frame, this pattern uses the `NetworkEntityMap.EntityRegistered` event to track exactly which network IDs became available this frame, allowing for an O(1) retry loop.

Here is how you can adapt this general queueing mechanism to safely resolve the `CommanderId` for your `UnitSubordinate` component inside the ingress translator:

1\. Define the State and Event Subscription

You will need a dictionary to hold deferred subordinate assignments (keyed by the missing Commander's Network ID), and a `HashSet` to track which IDs just arrived.

```
// Key: Commander Network ID. Value: List of Subordinate Entities waiting for this commander.
private readonly Dictionary<long, List<Entity>> _pendingSubordinates = new();

// Network IDs registered since the last PollIngress call.
private readonly HashSet<long> _recentlyRegistered = new();

public EntityInfoIngressTranslator(..., NetworkEntityMap entityMap, ...)
{
    _entityMap = entityMap;
    
    // Subscribe to registration events so the retry loop only runs when relevant
    // entities become available, rather than scanning all pending IDs every tick [3].
    _entityMap.EntityRegistered += OnEntityRegistered;
}

private void OnEntityRegistered(long netId, Entity _)
{
    // If we are waiting for this ID to become a commander, flag it for retry [4].
    if (_pendingSubordinates.ContainsKey(netId))
    {
        _recentlyRegistered.Add(netId);
    }
}
```

2\. Defer Missing References

Inside your `ProcessSample` method, if the commander's network ID is valid but the entity does not exist yet, push the subordinate entity into the queue and bail out early.

```
internal void ProcessSample(Hrot.NED.Descriptors.EntityInfo info, long netId, EntityRepository? repo = null)
{
    if (!_entityMap.TryGetEntity(netId, out var subordinateEntity))
        return; // Subordinate itself isn't spawned yet

    if (info.CommanderId != 0)
    {
        if (_entityMap.TryGetEntity(info.CommanderId, out var commanderEntity))
        {
            // The commander exists! Apply immediately.
            ApplyUnitSubordinate(subordinateEntity, commanderEntity, repo);
        }
        else
        {
            // Commander doesn't exist yet. Defer until it spawns.
            if (!_pendingSubordinates.TryGetValue(info.CommanderId, out var pendingList))
            {
                pendingList = new List<Entity>();
                _pendingSubordinates[info.CommanderId] = pendingList;
            }
            pendingList.Add(subordinateEntity);
        }
    }
}
```

3\. Process the Queue efficiently

At the very top of `PollIngress`, before you take any new DDS samples, drain the `_recentlyRegistered` set and apply the resolved commanders to the deferred subordinates.

```
public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
{
    // Retry deferred samples — only for net IDs registered since the last poll [6].
    if (_recentlyRegistered.Count > 0 && _pendingSubordinates.Count > 0)
    {
        foreach (var commanderNetId in _recentlyRegistered)
        {
            if (_pendingSubordinates.TryGetValue(commanderNetId, out var waitingSubordinates) 
                && _entityMap.TryGetEntity(commanderNetId, out var commanderEntity))
            {
                foreach (var subordinateEntity in waitingSubordinates)
                {
                    // If the subordinate is still alive, apply the component.
                    if (view.IsAlive(subordinateEntity))
                    {
                        cmd.SetComponent(subordinateEntity, new UnitSubordinate 
                        { 
                            Commander = commanderEntity 
                        });
                    }
                }
                _pendingSubordinates.Remove(commanderNetId);
            }
        }
    }
    _recentlyRegistered.Clear();

    // ... continue taking new DDS samples ...
}
```

4\. Memory Cleanup

Finally, don't forget to prevent memory leaks if an entity is permanently destroyed before its dependencies ever resolve.

```
public void Dispose(long networkEntityId)
{
    // Clean up if a missing commander is explicitly disposed, or clean up dead subordinates [7].
    _pendingSubordinates.Remove(networkEntityId);
}
```

By adapting this pattern, the ECS elegantly handles cross-entity foreign keys arriving out of order. It completely bypasses O(N) scanning every frame and complies with FDP's zero-allocation performance rules during steady-state ticks.

How can we solve the same entity-does-not-yet-exist issue for the UnitRoster component?

This is a great architectural question, but there is a brilliant ECS-native shortcut here: **you actually do not need to solve the network ordering issue for** **UnitRoster** **because the roster should never be sent over the network.**

If you look at the DDS network schema for `Hrot.NED.Descriptors.EntityInfo`, it only contains a single `CommanderId` field. The DDS protocol defines hierarchical relationships strictly **bottom-up**. There is no array of subordinate IDs transmitted in the network packets.

Because of this, `UnitRoster` is purely a **locally derived cache** (a read-model) used by the cognitive AI tier. You can perfectly solve its initialization by piggybacking on the exact same queueing mechanism we just built for `UnitSubordinate`.

Here is how you handle it efficiently and safely:

1\. The Strategy: Local Reconstruction

Instead of trying to sync a top-down list over the network, you build the top-down `UnitRoster` locally at the exact moment the bottom-up `UnitSubordinate` reference resolves.

This mirrors the engine's existing `VehicleCommandSystem.ProcessJoinFormationCommands`, which sets the follower's state and simultaneously adds the follower to the leader's roster.

2\. Hooking into the Ingress Queue

We reuse the `_pendingSubordinates` queue from the previous step. When the missing commander finally spawns and the `EntityRegistered` event triggers the queue drain, we assign the `UnitSubordinate` component to the tank, and **simultaneously inject the tank into the commander's** **UnitRoster**.

Expand the processing loop inside your `PollIngress` method (or a dedicated `ApplyUnitSubordinate` helper):

```
private unsafe void ApplyUnitSubordinate(Entity subordinate, Entity commander, ISimulationView view, IEntityCommandBuffer cmd)
{
    if (!view.IsAlive(subordinate)) return;

    // 1. Write the bottom-up reference (AI AI-tier Single Source of Truth)
    cmd.SetComponent(subordinate, new UnitSubordinate 
    { 
        Commander = commander 
    });

    // 2. Write the top-down reference (Local Roster Cache)
    // Note: To read-modify-write immediately, we need the concrete EntityRepository
    if (view is EntityRepository repo && repo.HasComponent<UnitRoster>(commander))
    {
        var roster = repo.GetComponent<UnitRoster>(commander);
        
        if (roster.Count < 16) // Prevent buffer overrun
        {
            // Reinterpret Entity to long (matches FDP's FormationRosterExtensions pattern)
            roster.SubordinateEntities[roster.Count] = *(long*)&subordinate;
            roster.Count++;
            
            // SetComponent uses Upsert/Overwrite, instantly updating the chunk
            repo.SetComponent(commander, roster);
        }
    }
}
```

3\. Handling Scenario Loading (Genesis Pipeline)

The same logic applies to loading offline scenarios from disk. In FDP, hierarchical collections (like embarked passengers) are resolved locally.

If you look at `GenesisMaterializationSystem.MaterializePassengers`, it waits until all passenger Network IDs resolve to live entities before creating the `PassengerBuffer`.

Since `UnitRoster` is derived, you don't even need a custom translator for it. Your `InitialUnitSubordinateIntent` materializer (which we defined in the previous step) can simply perform the exact same two-way assignment:

```
private unsafe void MaterializeUnitSubordinate(ISimulationView view, EntityCommandBuffer cmd, EntityRepository repo)
{
    foreach (var entity in view.Query().WithManaged<InitialUnitSubordinateIntent>().Build())
    {
        var intent = view.GetManagedComponentRO<InitialUnitSubordinateIntent>(entity);
        
        if (!_entityMap.TryGetEntity(intent.CommanderNetworkId, out var commander) || !view.IsAlive(commander))
            continue;

        // 1. Set Subordinate
        repo.SetComponent(entity, new UnitSubordinate { Commander = commander, TacticalDesignation = intent.TacticalDesignation });
        
        // 2. Add to Commander's Roster
        if (repo.HasComponent<UnitRoster>(commander))
        {
            var roster = repo.GetComponent<UnitRoster>(commander);
            if (roster.Count < 16)
            {
                roster.SubordinateEntities[roster.Count] = *(long*)&entity;
                roster.TacticalDesignations[roster.Count] = intent.TacticalDesignation;
                roster.Count++;
                repo.SetComponent(commander, roster);
            }
        }
        
        cmd.RemoveManagedComponent<InitialUnitSubordinateIntent>(entity);
    }
}
```

Why this is the ultimate ECS solution:

-   **Zero Network Bandwidth:** You aren't transmitting redundant 16-element arrays across DDS.-   **Eliminates Race Conditions:** The commander's roster only ever populates when the subordinate physically exists in the local ECS, guaranteeing there are no "null" or missing entities in the `UnitRoster.SubordinateEntities` array.-   **Memory Boundary Respected:** The presentation/network layer stays completely ignorant of the AI-tier `UnitRoster`, fulfilling the strict CQRS boundaries.

What saving the unit roster component to the scenario and restoring the entity references when loadign the scenario, will that all work or does it need some changes?

Saving the `UnitRoster` component directly to the scenario file will **not work** out of the box and attempting to do so will result in corrupted or zeroed data upon loading.

This is because the `FdpAutoSerializer` cannot natively serialize `fixed` buffer arrays or raw integer-based entity handles. The fixed-buffer backing structs are treated as opaque, meaning their contents are lost on every JSON round-trip. Additionally, local ECS entity handles are highly volatile and must be converted to stable GUID strings to survive being saved to disk.

However, as established in our previous architectural decision, you **do not need to save the** **UnitRoster** **at all**. Because it is a top-down cache derived purely from the bottom-up `UnitSubordinate` components, you can reconstruct it dynamically at load time.

Here are the specific changes needed to implement this perfectly:

**1\. Mark** **UnitRoster** **with** **NoSave** You must explicitly instruct the engine's serialization pipeline to ignore the `UnitRoster` component so the auto-serializer does not attempt to process its fixed arrays. Add the `DataPolicy.NoSave` attribute, which excludes the component from scenario JSON serialization.

```
[DataPolicy(DataPolicy.NoSave)]
[ComponentId(GlobalComponentIds.UnitRoster)]
public unsafe struct UnitRoster
{
    // ...
}
```

**2\. Create a Custom** **UnitSubordinateTranslator** You must create a custom class implementing `IEntityScenarioTranslator` specifically for the `UnitSubordinate` component. This translator acts as a bridge during the scenario save/load pipeline:

-   **Extract (Save):** Use the provided `IGuidResolver.Resolve()` method to safely convert the volatile `UnitSubordinate.Commander` entity handle into a stable GUID string.-   **Inject (Load):** Read the GUID string from the scenario JSON, convert it back into the original Network ID, and write an `InitialUnitSubordinateIntent` transient DTO to the entity.

**3\. Update** **HrotScenarioSerializerFactory** Register your new `UnitSubordinateTranslator` in `HrotScenarioSerializerFactory.Build()`. The serializer will run this custom translator first, and then clear the `UnitSubordinate` bit from the entity's mask so the auto-serializer knows to skip it.

**4\. Materialize the Roster in** **GenesisMaterializationSystem** The final step happens in the `GenesisMaterializationSystem`, which runs during the `SystemPhase.Input` phase. You will add a method (e.g., `MaterializeUnitSubordinate`) that queries for all entities carrying the `InitialUnitSubordinateIntent`.

Once this system confirms that both the subordinate and the commander are alive and resolved in the `NetworkEntityMap`, it executes the exact two-way assignment we established earlier:

-   It writes the structural `UnitSubordinate` component containing the live `Entity` handle to the follower.-   It safely appends the follower into the commander's `UnitRoster` inline array.-   It removes the transient intent component.

By applying these changes, your scenario files remain perfectly lean, memory-safe, and fully compatible with the engine's deterministic serialization rules.

what if some subordinate entity is created AFTER the commander entity? How the system ensures that the commander's roaster get updated? The order of creating the entities in scenario is not guaranteeing that subordinates will always be created first so we can not rely on it. The issue is there not just during scenario loading. In general, how to control the process of adding a subordinate unit during runtime?

If a subordinate entity is created _after_ the commander entity, the system we designed actually handles this natively without any friction. The ordering issue is only problematic when the commander is missing (which we solved using the deferral queue).

Because we decided to make the relationship strictly **bottom-up** over the network and in scenario files, the `UnitRoster` is just a local cache. If the commander spawns first, it simply sits there with an empty `UnitRoster` (Count = 0). When the subordinate is subsequently created, its ingress translator or scenario materializer resolves the commander's ID, sees that the commander is already alive, and immediately performs the two-way assignment: it writes its own `UnitSubordinate` component and appends itself to the commander's `UnitRoster`.

However, you are raising a very important point regarding **runtime control**: how do we dynamically add or reassign a subordinate to a commander during live simulation (for example, when reinforcements arrive or a mission script reassigns a tank)?

To control this process at runtime, you must follow the engine's established **Command Event Pattern**, exactly as FDP currently handles assigning vehicles to formations via `CmdJoinFormation`.

Here is how you implement the runtime assignment process for your generic subordination architecture:

1\. Define a Command Event

You create an unmanaged event struct that any system, UI button, or AI behavior can publish to the event bus when they want to assign a subordinate to a unit.

```
[EventId(GlobalEventIds.CmdAssignSubordinate)] // Assign a unique ID
public struct CmdAssignSubordinate
{
    public Entity Subordinate;
    public Entity Commander;
    public ushort TacticalDesignation;
}
```

2\. Create a Centralized Management System

You should never manually mutate the `UnitRoster` and `UnitSubordinate` components directly from random behaviors or UI panels. Instead, you create a dedicated system (e.g., `UnitHierarchySystem`) that runs in the `SystemPhase.Simulation` phase. This system consumes the commands and guarantees the two components are kept perfectly in sync.

This mirrors exactly how the engine's `VehicleCommandSystem` processes `CmdJoinFormation`:

```
[UpdateInPhase(SystemPhase.Simulation)]
public class UnitHierarchySystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo) return;

        var events = repo.Bus.Read<CmdAssignSubordinate>();
        foreach (ref readonly var cmd in events)
        {
            if (!repo.IsAlive(cmd.Subordinate) || !repo.IsAlive(cmd.Commander))
                continue;

            // 1. Write the bottom-up reference on the subordinate
            repo.SetComponent(cmd.Subordinate, new UnitSubordinate 
            { 
                Commander = cmd.Commander,
                TacticalDesignation = cmd.TacticalDesignation
            });

            // 2. Update the top-down cache on the commander
            if (repo.HasComponent<UnitRoster>(cmd.Commander))
            {
                var roster = repo.GetComponent<UnitRoster>(cmd.Commander);
                
                // Ensure we don't exceed the fixed buffer capacity
                if (roster.Count < 16) 
                {
                    unsafe 
                    {
                        roster.SubordinateEntities[roster.Count] = *(long*)&cmd.Subordinate;
                        roster.TacticalDesignations[roster.Count] = cmd.TacticalDesignation;
                    }
                    roster.Count++;
                    
                    // Upsert the mutated roster back into the chunk [4]
                    repo.SetComponent(cmd.Commander, roster);
                }
            }
        }
    }
}
```

Why this is the correct approach:

-   **Thread and Phase Safety:** By publishing an event, an AI behavior running deep in a BTree evaluation doesn't cause structural race conditions. The event is safely queued and processed centrally.-   **Single Source of Truth:** The `UnitHierarchySystem` becomes the absolute authority on updating rosters. Whether the assignment comes from the Anti-Corruption Layer (network ingress), the Genesis Materializer (scenario load), or a live Mission Script, they all just publish `CmdAssignSubordinate`.-   **Decoupled:** The entity spawning systems (like `NetworkSpawningSystem`) do not need to know anything about rosters. They just spawn the entity. If the entity needs to join a unit, a subsequent `CmdAssignSubordinate` handles it safely after the entity is fully alive.

how could ve REMOVE an entity from subordination? How to CHANGE the entity commander at runtime - this requires updating 2 rosters (removing from original comamnder and adding to the new commander)

To elegantly remove an entity from subordination or change its commander at runtime, we must handle the top-down local cache (`UnitRoster`) and the bottom-up absolute truth (`UnitSubordinate`) simultaneously.

In FDP, the engine relies on the **"Compact-Remove" (Swap-and-Pop)** pattern when removing items from fixed-capacity unmanaged arrays (like our `fixed long SubordinateEntities`). We can see this exact pattern used in the engine's `ActiveSensorTracksUpdateSystem`.

To accomplish this safely without race conditions, we expand your centralized `UnitHierarchySystem` to handle both a new `CmdRemoveSubordinate` event, and to make the existing `CmdAssignSubordinate` event smart enough to automatically detach an entity from its old unit before attaching it to the new one.

1\. Define the Removal Command

Create an unmanaged event struct that can be fired to detach an entity from the command structure.

```
[EventId(GlobalEventIds.CmdRemoveSubordinate)] 
public struct CmdRemoveSubordinate
{
    public Entity Subordinate;
}
```

2\. Implement the Removal Logic (Compact-Remove)

Add a dedicated helper method to your `UnitHierarchySystem` that executes the two-way decoupling.

-   It queries the follower's `UnitSubordinate` to find the old commander.-   It iterates the old commander's `UnitRoster`. When it finds the leaving entity, it overwrites that slot with the very last element in the array and decrements `Count` (O(1) removal, zero GC).-   It removes the `UnitSubordinate` component from the follower.

```
private unsafe void RemoveFromHierarchy(EntityRepository repo, Entity subordinate)
{
    if (!repo.HasComponent<UnitSubordinate>(subordinate)) return;

    var subData = repo.GetComponent<UnitSubordinate>(subordinate);
    Entity oldCommander = subData.Commander;

    // 1. Remove from old commander's top-down roster via Compact-Remove
    if (repo.IsAlive(oldCommander) && repo.HasComponent<UnitRoster>(oldCommander))
    {
        var roster = repo.GetComponent<UnitRoster>(oldCommander);
        long subPacked = *(long*)&subordinate;

        for (int i = 0; i < roster.Count; i++)
        {
            if (roster.SubordinateEntities[i] == subPacked)
            {
                int last = roster.Count - 1;
                if (i < last) // If it's not already the last element, swap them
                {
                    roster.SubordinateEntities[i] = roster.SubordinateEntities[last];
                    roster.TacticalDesignations[i] = roster.TacticalDesignations[last];
                }
                roster.Count--;
                
                // Upsert the mutated roster back into the chunk
                repo.SetComponent(oldCommander, roster);
                break;
            }
        }
    }

    // 2. Remove the bottom-up AI structural link
    repo.RemoveComponent<UnitSubordinate>(subordinate);
}
```

3\. Smart Reassignment in `UnitHierarchySystem`

Now, update the execution loop of your `UnitHierarchySystem` (running in `SystemPhase.Simulation`).

By making the system process removals _first_, and then process assignments, a runtime commander change happens naturally: you simply publish `CmdAssignSubordinate` with the new commander. The system will detect the old commander, automatically invoke the `RemoveFromHierarchy` helper, and then safely link it to the new unit.

```
[UpdateInPhase(SystemPhase.Simulation)]
public class UnitHierarchySystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo) return;

        // 1. Process explicit Removals (e.g., entity is destroyed or explicitly left)
        var removeEvents = repo.Bus.Read<CmdRemoveSubordinate>();
        foreach (ref readonly var cmd in removeEvents)
        {
            RemoveFromHierarchy(repo, cmd.Subordinate);
        }

        // 2. Process Assignments and Reassignments
        var assignEvents = repo.Bus.Read<CmdAssignSubordinate>();
        foreach (ref readonly var cmd in assignEvents)
        {
            if (!repo.IsAlive(cmd.Subordinate) || !repo.IsAlive(cmd.Commander)) continue;

            // SAFE REASSIGNMENT: Check if already subordinated to a DIFFERENT commander
            if (repo.HasComponent<UnitSubordinate>(cmd.Subordinate))
            {
                var currentSub = repo.GetComponent<UnitSubordinate>(cmd.Subordinate);
                if (currentSub.Commander == cmd.Commander) 
                    continue; // Already in this unit

                // Unlink from the old commander's roster before proceeding
                RemoveFromHierarchy(repo, cmd.Subordinate);
            }

            // Apply new Subordinate reference
            repo.SetComponent(cmd.Subordinate, new UnitSubordinate 
            { 
                Commander = cmd.Commander,
                TacticalDesignation = cmd.TacticalDesignation
            });

            // Add to new Commander's roster
            if (repo.HasComponent<UnitRoster>(cmd.Commander))
            {
                var roster = repo.GetComponent<UnitRoster>(cmd.Commander);
                if (roster.Count < 16) 
                {
                    unsafe 
                    {
                        roster.SubordinateEntities[roster.Count] = *(long*)&cmd.Subordinate;
                        roster.TacticalDesignations[roster.Count] = cmd.TacticalDesignation;
                    }
                    roster.Count++;
                    repo.SetComponent(cmd.Commander, roster);
                }
            }
        }
    }
    
    // ... (RemoveFromHierarchy method goes here) ...
}
```

4\. What happens over the Network?

Because we previously established the Anti-Corruption Layer rule where only the bottom-up integer (`CommanderId`) is published over the DDS Network,:

-   When you reassign the commander locally, `EntityInfoEgressTranslator` automatically detects the new `CommanderId` and publishes the updated DDS packet.-   When you explicitly remove the subordinate, `EntityInfoEgressTranslator` sees that `UnitSubordinate` is gone, translates it to `CommanderId = 0`, and publishes.-   The remote node's `EntityInfoIngressTranslator` receives `CommanderId = 0` (or the new ID). It can simply publish a local `CmdRemoveSubordinate` or `CmdAssignSubordinate` on its own event bus, and its own local `UnitHierarchySystem` will identically unlink/relink the entity on the remote peer, ensuring both node's `UnitRoster` caches stay perfectly in sync without ever sending array data over the wire.

In the ORBAT tree UI, i would like to add the possibility to make this add/remove subordinate changes manually. maybe by drag/drop of the entity in the tree? Dragging to empty place would remove the subordination. Dragging under some other entity will create the subordination.

I see some uncovered issues:

1.  we should not be allowed to drop entity onto a non-commander virtual entity (not having unit roster)
2.  I would like to be able to perform these operations also in the hrot.editor, when SimTime is paused, before I start the simulation (preview). The subordination changes there in the enditor should be applied immediately because they are changing the initial state of the entities

Systém nedokázal odpovědět.

In the ORBAT tree UI (both in the hrot.editor and in the distributed 'clusterrunner -m all' setup, i would like to add the possibility to make this add/remove subordinate changes manually. maybe by drag/drop of the entity in the tree? Dragging to empty place would remove the subordination. Dragging under some other entity will create the subordination.

I see some uncovered issues:

1.  we should not be allowed to drop entity onto a non-commander virtual entity (not having unit roster)
    
2.  I would like to be able to perform these operations also in the hrot.editor, when SimTime is paused, before I start the simulation (preview). The subordination changes there in the enditor should be applied immediately because they are changing the initial state of the entities
    

To implement manual drag-and-drop subordination while solving the unhandled constraints (commander-only validation and offline editor pausing), we need to update the `SharedOrbatPanel`, extend the `IOrbatController` interface, and handle the logic differently in the offline Editor versus the distributed ExCon.

Here is the step-by-step implementation.

1\. Update the UI Model & Shared Panel

First, add a flag to the View-Model so the UI knows if an entity is allowed to accept drops.

**Hrot.UI.Common.Models.OrbatNodeViewModel**

```
public sealed record OrbatNodeViewModel(
    int EntityId,
    string Name,
    int Depth,
    bool HasChildren,
    bool IsPendingDelete,
    bool CanAcceptSubordinates); // <-- NEW FLAG
```

Next, update **SharedOrbatPanel.DrawContent** to enforce drop rules and add a "background" drop target for removing subordination:

```
// 1. Validating Drops on Commanders
// Only open the drag-drop target if this node is allowed to accept subordinates.
if (node.CanAcceptSubordinates && ImGui.BeginDragDropTarget())
{
    unsafe
    {
        var payload = ImGui.AcceptDragDropPayload("ORBAT_ENTITY");
        if (payload.NativePtr != null)
        {
            int subordinateId = *(int*)payload.Data;
            if (subordinateId != node.EntityId)
                ctrl.RequestAssignSubordinate(subordinateId, node.EntityId);
        }
    }
    ImGui.EndDragDropTarget();
}

// ... (end of the node rendering loop)

// 2. Empty Space Drop (Remove Subordination)
// Create a dummy widget filling the remaining vertical space
ImGui.Dummy(new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, Math.Max(50f, ImGui.GetContentRegionAvail().Y)));
if (ImGui.BeginDragDropTarget())
{
    unsafe
    {
        var payload = ImGui.AcceptDragDropPayload("ORBAT_ENTITY");
        if (payload.NativePtr != null)
        {
            int subordinateId = *(int*)payload.Data;
            ctrl.RequestRemoveSubordinate(subordinateId);
        }
    }
    ImGui.EndDragDropTarget();
}
```

2\. Extend the Controller Interface

Add the new explicit hierarchy operations to **IOrbatController**:

```
/// <summary>Requests that an entity be subordinated to a new commander.</summary>
void RequestAssignSubordinate(int subordinateId, int commanderId);

/// <summary>Requests that an entity be removed from its commander.</summary>
void RequestRemoveSubordinate(int subordinateId);
```

3\. Editor Implementation (Local / Paused Mode)

In the offline Editor, `EditorOrbatAdapter` has direct access to the live `EntityRepository` and local `FdpEventBus`.

When `SimTime` is paused (TimeScale = 0), the `EditorSubsystem.Update` method still ticks the `ModuleHostKernel` every frame. Because `UnitHierarchySystem` is registered in `EditorSimulationModule`, it will process the event buffer immediately, guaranteeing the structural changes are applied instantly before you click Play or Save.

**EditorOrbatAdapter.cs**

```
// In GetVisibleNodes, populate the new flag:
bool isCommander = _world.HasComponent<UnitRoster>(entity);
result.Add(new OrbatNodeViewModel(..., CanAcceptSubordinates: isCommander));

// Implement the new IOrbatController methods:
public void RequestAssignSubordinate(int subordinateId, int commanderId)
{
    if (_indexCache.TryGetValue(subordinateId, out var subEntity) && 
        _indexCache.TryGetValue(commanderId, out var cmdEntity))
    {
        _bus.Publish(new CmdAssignSubordinate { 
            Subordinate = subEntity, 
            Commander = cmdEntity 
        });
    }
}

public void RequestRemoveSubordinate(int subordinateId)
{
    if (_indexCache.TryGetValue(subordinateId, out var subEntity))
    {
        _bus.Publish(new CmdRemoveSubordinate { 
            Subordinate = subEntity 
        });
    }
}
```

_(Note: Ensure_ _EditorOrbatAdapter_ _builds its tree using the new AI-tier_ _UnitSubordinate.Commander_ _rather than the old_ _EntityInfo.CommanderId__, so the UI instantly reflects the hierarchy changes!)_

4\. ExCon Implementation (Distributed Cluster)

In the distributed environment, the ExCon has no `EntityRepository` and only knows about the network presentation data.

To populate the `CanAcceptSubordinates` flag, ExCon checks if the `TkbType` matches a known composite/commander unit blueprint.

**ExConOrbatAdapter.cs**

```
// In GetVisibleNodes, populate the new flag:
bool isCommander = IsCompositeType(entity.TkbType); // Simple helper checking TkbEntityTypes
result.Add(new OrbatNodeViewModel(..., CanAcceptSubordinates: isCommander));

// Implement the new IOrbatController methods:
public void RequestAssignSubordinate(int subordinateId, int commanderId)
{
    // Send a JSON patch targeting the DDS EntityInfo descriptor
    _logic.EgressWriters.WriteUpdateEntityDescriptor(new UpdateEntityDescriptorCommand
    {
        EntityId = subordinateId,
        DescriptorJson = $"{{\"CommanderId\": {commanderId}}}",
        BaseVersion = 0
    });
}

public void RequestRemoveSubordinate(int subordinateId)
{
    _logic.EgressWriters.WriteUpdateEntityDescriptor(new UpdateEntityDescriptorCommand
    {
        EntityId = subordinateId,
        DescriptorJson = $"{{\"CommanderId\": 0}}",
        BaseVersion = 0
    });
}
```

5\. Bridging the Network ACL (SimHost)

Because we completely decoupled the AI-tier `UnitSubordinate` from the Network-tier `EntityInfo`, `UpdateEntityAttributeRequestSystem` won't automatically map the `CommanderId` JSON patch to your local ECS hierarchy.

To bridge this seamlessly, update the SimHost's `EntityInfoIngressTranslator` (or write a tiny interceptor) to detect incoming changes to `CommanderId` and publish `CmdAssignSubordinate` / `CmdRemoveSubordinate` on the local bus. This ensures the single-source-of-truth `UnitHierarchySystem` takes over and strictly validates the ECS chunk alignment for the incoming network request.

what is the meaning of the "ushort TacticalDesignation" field? Where to fill it from? Shouldn't we add it to the EntityInfo network descriptor? Default value (zero) must be supported as 'undefined/default'.

The **ushort TacticalDesignation** field is a lightweight, zero-allocation integer used to define the specific logical role or identifier an entity holds under its commander.

Instead of just knowing "I belong to Platoon Alpha," the designation tells the AI "I am the Wingman," "I am the Squad Leader," or "I am Vehicle #3." This allows a commander's BTree to assign specific tactical intents (like flanking left or right) based on the subordinate's exact role, rather than relying on arbitrary array indices.

Because it is a `ushort`, the default memory-zeroed value is `0`, which perfectly aligns with your requirement to support `0` as **'undefined/default'** for entities that are just generic members of a unit without a specialized role.

Here is the breakdown of where it should be filled from and how to integrate it into the network layer without breaking our Anti-Corruption Layer (ACL) design:

1\. Adding it to the Network Descriptor

You are absolutely correct that it belongs in the network descriptor. Because the designation strictly qualifies the command relationship, it should travel over the network alongside the `CommanderId`.

You should add it to the DDS wire struct, but **not** to the local `Fdp.Core.EntityInfo` component, preserving our rule that `UnitSubordinate` is the single source of truth for the local ECS hierarchy.

Update **Hrot.NED.Descriptors.EntityInfo**:

```
[DdsTopic("EntityInfo")]
public partial struct EntityInfo
{
    [DdsKey]
    public int EntityId;
    public string Name;
    public eForceIdentifier ForceIdentifier;
    
    // ORBAT Hierarchy
    public int CommanderId; 
    public ushort TacticalDesignation; // 0 = undefined/default [1]
}
```

2\. Where to fill it from

The value originates from three distinct places depending on how the entity is created or commanded:

**A. TKB Composite Spawning (Initial Creation)** When the engine automatically spawns a composite unit like an Infantry Squad, it uses the `TkbCompositionDef`. This definition already contains a `RoleTag` string for each child slot:

```
comp.Subordinates.Add(new TkbChildSlot { 
    TkbType = TkbEntityTypes.Infantry_Officer, 
    Count = 1, 
    RoleTag = "SquadLeader" // <-- The string origin [3]
});
```

When the network spawning system creates these children, it should hash or map the `"SquadLeader"` string into a `ushort` and place it into the `TacticalDesignation` field of the outgoing `CreateEntityRequest` and `EntityInfo` descriptor.

**B. Live Network Traffic (DDS Ingress)** During live simulation, if an ExCon operator reassigns a unit via the ORBAT UI, they publish an updated `EntityInfo` DDS packet. Your updated `EntityInfoIngressTranslator` will extract both fields and publish the command:

```
// Inside EntityInfoIngressTranslator.ProcessSample:
_eventBus.PublishManaged(new CmdAssignSubordinate
{
    Subordinate = subordinateEntity,
    Commander = commanderEntity,
    TacticalDesignation = info.TacticalDesignation // Read from DDS
});
```

The central `UnitHierarchySystem` then writes this safely into the local `UnitSubordinate.TacticalDesignation` and the commander's `UnitRoster.TacticalDesignations` array.

**C. Scenario Loading (Genesis Pipeline)** For offline scenario loading, we previously created `InitialUnitSubordinateIntent`. You simply ensure the JSON extraction/injection translator saves the `TacticalDesignation` alongside the `CommanderNetworkId`. When the `GenesisMaterializationSystem` resolves the live ECS handles, it writes the designation directly into the components.

for the TacticalDesignation, shouldn't we use enums on both sides (separate enum on the network descriptor side, separate enum in the internal ECS world) instead of the string RoleTag = "SquadLeader" ? If these two enums are kept in sync, it would simplify thier conversion (we should still keep a conversion method, even though it just cast the type)

Systém nedokázal odpovědět.

for the TacticalDesignation, we should use enums on both sides (separate enum on the network descriptor side, separate enum in the internal ECS world) instead of the string RoleTag = "SquadLeader" And keep these two enum defintion in sync (by adding proper comment on top of them), to simplify their conversion. We should still keep a conversion method, even though it just casts the type from one enum to another.

Using separate enums on both sides of the network boundary perfectly aligns with the engine's established **Dual-Enum Pattern** (as seen in `EClampingMode` and `ENavigationMode`). This prevents the DDS network layer from taking a compile-time dependency on internal AI components, while eliminating string-parsing overhead entirely.

Here is how to implement this synchronized dual-enum architecture and update the TKB definitions.

1\. The Internal ECS Enum (Cognitive Tier)

Create the AI-tier enum inside the Behavior toolkit. It inherits from `ushort` to guarantee a 2-byte memory footprint.

**Fdp.Toolkit.Behavior.Components.TacticalDesignation**

```
namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// Logical role of an entity within its command hierarchy.
    /// IMPORTANT: Must be kept in sync with Hrot.NED.Descriptors.eTacticalDesignation!
    /// </summary>
    public enum TacticalDesignation : ushort
    {
        Undefined = 0,
        Commander = 1,
        SquadLeader = 2,
        Wingman = 3,
        Support = 4
        // Add more specific roles here
    }
}
```

2\. The Network Descriptor Enum (DDS Wire)

Create the mirrored enum in the network schema layer.

**Hrot.NED.Descriptors.eTacticalDesignation**

```
namespace Hrot.NED.Descriptors
{
    /// <summary>
    /// Logical role of an entity within its command hierarchy over DDS.
    /// IMPORTANT: Must be kept in sync with Fdp.Toolkit.Behavior.Components.TacticalDesignation!
    /// </summary>
    public enum eTacticalDesignation : ushort
    {
        Undefined = 0,
        Commander = 1,
        SquadLeader = 2,
        Wingman = 3,
        Support = 4
    }
}
```

3\. The Conversion Methods (Anti-Corruption Layer)

Even though the integer values match perfectly, you should maintain a strict boundary in the translator layer using explicit casting methods.

Add these helpers to your translator utilities (e.g., `NedTranslationHelper`):

```
internal static class TacticalDesignationMapper
{
    public static Fdp.Toolkit.Behavior.Components.TacticalDesignation ToEcs(Hrot.NED.Descriptors.eTacticalDesignation ddsValue)
    {
        return (Fdp.Toolkit.Behavior.Components.TacticalDesignation)ddsValue;
    }

    public static Hrot.NED.Descriptors.eTacticalDesignation ToDds(Fdp.Toolkit.Behavior.Components.TacticalDesignation ecsValue)
    {
        return (Hrot.NED.Descriptors.eTacticalDesignation)ecsValue;
    }
}
```

4\. Updating the Components and Network Descriptor

With the enums defined, apply them as the strong types in both your ECS components and the DDS descriptor.

**Update** **Hrot.NED.Descriptors.EntityInfo****:**

```
[DdsTopic("EntityInfo")]
public partial struct EntityInfo
{
    [DdsKey] public int EntityId;
    public string Name;
    public eForceIdentifier ForceIdentifier;
    public int CommanderId; 
    public eTacticalDesignation TacticalDesignation; // Using the new DDS enum
}
```

**Update** **UnitSubordinate** **(ECS):**

```
[ComponentId(GlobalComponentIds.UnitSubordinate)]
public struct UnitSubordinate
{
    public Entity Commander;
    public TacticalDesignation Designation; // Using the new ECS enum
}
```

_(Note: For_ _UnitRoster__, the inline fixed buffer should remain_ _public fixed ushort TacticalDesignations__;_ _because C#_ _fixed_ _arrays require unmanaged primitive types. You will safely cast to_ _TacticalDesignation_ _when reading/writing from it.)_

5\. Replacing the String in the TKB Blueprint

Finally, eliminate the `string RoleTag` allocation from the Transient Knowledge Base. Update `TkbChildSlot` to use the ECS enum directly.

**Update** **TkbChildSlot****:**

```
public struct TkbChildSlot
{
    public long TkbType { get; set; }
    public int Count { get; set; }
    public Fdp.Toolkit.Behavior.Components.TacticalDesignation Designation { get; set; }
}
```

Now, when you define composite units in `NedTkbCatalog`, you assign the enum explicitly:

```
.AsComposite(TkbEntityTypes.Unit_InfantrySquad, comp =>
{
    comp.Subordinates.Add(new TkbChildSlot { 
        TkbType = TkbEntityTypes.Infantry_Officer, 
        Count = 1, 
        Designation = TacticalDesignation.SquadLeader 
    });
    // ...
})
```

By removing the `RoleTag` string, the `NetworkSpawningSystem` can directly read `Designation` and pass it down the pipeline into the local ECS hierarchy and across the DDS network, entirely free of string allocations or parsing overhead.







The architectural refactor proposed in the transcript is highly sophisticated and aligns exceptionally well with the strict Data-Oriented Design (DOD), CQRS boundaries, and zero-allocation constraints of the FDP engine. Separating generic hierarchy from kinematic formation and pushing network ID resolution strictly to the Anti-Corruption Layer (translators) are excellent design decisions.

However, a close technical review of the implementation details reveals several critical gaps, memory leaks, and ECS lifecycle flaws that will cause issues during runtime and scenario loading.

Here is the analysis of the flaws and how to revise them.

### 1. The Ingress Queue Memory Leak & Stale Data
**The Flaw:** In the `EntityInfoIngressTranslator`, the code pushes subordinates into `_pendingSubordinates` if their commander hasn't spawned yet. The `Dispose(long networkEntityId)` method only removes the queue if the *missing commander* is disposed. 
If a subordinate entity is destroyed before its commander arrives, it remains trapped in the `List<Entity>` forever. Furthermore, if a subordinate is repeatedly reassigned to different missing commanders over the network, it will be added to multiple waitlists simultaneously, causing race conditions when those commanders finally spawn.

**The Fix:**
You must scrub the subordinate from any existing queues before adding it to a new one, and clean up dead subordinates.
```csharp
// Inside ProcessSample, before adding to the deferral queue:
RemoveSubordinateFromAllQueues(subordinateEntity);

if (!_pendingSubordinates.TryGetValue(info.CommanderId, out var pendingList)) { ... }
```

### 2. Scenario Materialization Race Condition (Genesis Pipeline)
**The Flaw:**
In the `GenesisMaterializationSystem`, the code iterates over `InitialUnitSubordinateIntent`. If the commander is not yet alive, it executes `continue;`. 
If entity materialization happens in an arbitrary order (which is standard for ECS chunk processing), subordinates processed before their commanders will `continue`, bypassing their initialization. If the materialization system only runs once per entity, those subordinates will permanently lose their command hierarchy.

**The Fix:**
Hierarchy resolution must be guaranteed to run *after* all baseline entities in the scenario are spawned and registered in the `NetworkEntityMap`. You should either:
1. Move the `MaterializeUnitSubordinate` logic into a strictly ordered `PostMaterializationPhase`.
2. Do not remove the `InitialUnitSubordinateIntent` until it succeeds, allowing it to retry on the next frame until the scenario is fully loaded.

### 3. C# Fixed Buffer Syntax Error
**The Flaw:**
The struct definition for `UnitRoster` uses an old C/C++ struct hack that is invalid/unsafe for C# ECS chunk sizing:
```csharp
public fixed long SubordinateEntities[1]; // Incorrect for capacity of 16
public fixed ushort TacticalDesignations[1]; 
```
In C#, fixed buffers define the *exact* byte footprint of the struct in the unmanaged chunk. Sizing it to `[1]` means the component only allocates 8 bytes for the array. Writing to `roster.SubordinateEntities[2]` will overwrite adjacent components in the ECS chunk memory, causing catastrophic engine corruption.

**The Fix:**
Explicitly size the fixed buffers to the maximum allowed capacity.
```csharp
public const int MaxSubordinates = 16;
public fixed long SubordinateEntities[MaxSubordinates];
public fixed ushort TacticalDesignations[MaxSubordinates];
```

### 4. Silent Failures on Capacity Limits
**The Flaw:**
In both `UnitHierarchySystem` and the Ingress translators, the code checks `if (roster.Count < 16) { add... }`. If a 17th entity is assigned to a commander, the system silently ignores it. The subordinate will have its `UnitSubordinate` updated (bottom-up is true), but it will *not* be in the commander's `UnitRoster` (top-down cache is false). This breaks the Single Source of Truth synchronization.

**The Fix:**
If 16 is a hard limit, the `UnitHierarchySystem` must explicitly reject the assignment, log an engine error, and *revert* the bottom-up assignment so the two components don't desync.
```csharp
if (roster.Count >= 16) 
{
    FdpLog.Error($"Cannot assign entity to Commander. UnitRoster capacity exceeded.");
    repo.RemoveComponent<UnitSubordinate>(cmd.Subordinate); // Revert
    return;
}
```

### 5. Commander Destruction (Dangling References)
**The Flaw:**
The system perfectly handles what happens when a *subordinate* is removed, but completely ignores what happens when the *commander* is destroyed. If a platoon commander entity is killed or deleted, the subordinates will be left with a `UnitSubordinate.Commander` pointing to a dead entity generation. AI BTrees might access it and throw exceptions.

**The Fix:**
The `UnitHierarchySystem` must intercept entity destruction events (e.g., `CmdDestroyEntity` or monitoring the `EntityLifecycle` state). If the destroyed entity has a `UnitRoster`, the system must iterate through all `SubordinateEntities` and remove the `UnitSubordinate` component from each child.

### 6. Swap-and-Pop Instability
**The Flaw:**
The `RemoveFromHierarchy` method uses a Swap-and-Pop array removal (`roster.SubordinateEntities[i] = roster.SubordinateEntities[last];`). 
While highly performant (O(1)), this alters the index order of the remaining subordinates. If any presentation logic, UI list, or AI behavior implicitly relies on the array index (e.g., assuming `index 0` is the first tank to spawn), it will break.

**The Fix:**
Because you introduced `TacticalDesignation`, relying on array indices is no longer strictly necessary (which is good). However, you must explicitly document that `UnitRoster` order is non-deterministic. If maintaining visual/logical order is required by the ExCon UI, you must change Swap-and-Pop to an `Array.Copy` memory shift (O(N)), which is acceptable given N is extremely small (16).





The review in the GAPS document is highly accurate. It correctly identifies edge cases in memory management, C# fixed buffer syntax, and ECS lifecycle synchronization that would cause instability or data corruption in the FDP engine if left unaddressed. 

Here is an analysis of each gap's validity and the concrete fixes required to make the architecture perfectly compliant with FDP's strict rules.

### 1. The Ingress Queue Memory Leak & Stale Data
**Validity: Highly Valid.** 
The queueing mechanism I previously proposed suffered from a classic leak: if a subordinate is destroyed before its commander arrives, it remains in the `_pendingSubordinates` list indefinitely. Furthermore, if the network sends an updated `CommanderId` for an entity already in the queue, it could be registered in multiple waitlists simultaneously.

**The Fix:** 
Update the `EntityInfoIngressTranslator` to scrub the entity from all pending queues before adding it to a new one, and handle cleanup in `Dispose`.

```csharp
// 1. Scrub from existing waitlists before deferring
foreach (var list in _pendingSubordinates.Values)
{
    list.Remove(subordinateEntity);
}

// 2. Safely add to the new commander's waitlist
if (!_pendingSubordinates.TryGetValue(info.CommanderId, out var pendingList))
{
    pendingList = new List<Entity>();
    _pendingSubordinates[info.CommanderId] = pendingList;
}
pendingList.Add(subordinateEntity);

// 3. Ensure cleanup on destruction
public void Dispose(long networkEntityId)
{
    // If it was a commander, drop its queue
    _pendingSubordinates.Remove(networkEntityId);
    
    // If it was a pending subordinate, remove it from all queues
    if (_entityMap.TryGetEntity(networkEntityId, out var entity))
    {
        foreach (var list in _pendingSubordinates.Values)
            list.Remove(entity);
    }
}
```

### 2. Scenario Materialization Race Condition (Genesis Pipeline)
**Validity: Partially Valid / Needs Clarification.**
The reviewer is correct that ECS chunk processing order is arbitrary and a subordinate might be processed before its commander is spawned. However, the fix is already natively supported by FDP's `GenesisMaterializationSystem`. 

**The Fix:** 
We must ensure that the transient `InitialUnitSubordinateIntent` is **only removed upon successful resolution**. By issuing `continue;` when the commander is missing, we skip the `RemoveManagedComponent` call. Because `GenesisMaterializationSystem` runs every tick during the `SystemPhase.Input` phase, it will naturally retry the unresolved intents on the next frame until all cross-entity references are satisfied, matching how `InitialHierarchyIntent` is materialized.

```csharp
// Inside GenesisMaterializationSystem.MaterializeUnitSubordinate
if (!_entityMap.TryGetEntity(intent.CommanderNetworkId, out var commander) || !view.IsAlive(commander))
    continue; // Intent is preserved; will retry next tick!

// ... [Apply structural components] ...

// Only remove the intent once successfully resolved
cmd.RemoveManagedComponent<InitialUnitSubordinateIntent>(entity);
```

### 3. C# Fixed Buffer Syntax Error
**Validity: 100% Valid.**
My previous code snippet omitted the array size (`public fixed long SubordinateEntities;`), which is invalid for defining fixed unmanaged buffers in C# structs. Omitting the size or sizing it to `` would cause memory corruption when writing to subsequent indices, as it would overwrite adjacent component data in the ECS chunk.

**The Fix:** 
Explicitly size the fixed buffers to the absolute maximum capacity (16).

```csharp
[DataPolicy(DataPolicy.NoSave)]
[ComponentId(GlobalComponentIds.UnitRoster)] 
public unsafe struct UnitRoster
{
    public int Count; 
    public fixed long SubordinateEntities;   // Explicit capacity
    public fixed ushort TacticalDesignations; // Explicit capacity
}
```

### 4. Silent Failures on Capacity Limits
**Validity: Highly Valid.**
In `UnitHierarchySystem`, if the 17th tank tries to join the platoon, applying the `UnitSubordinate` component but silently ignoring the `UnitRoster` append breaks the Single Source of Truth rule. The top-down and bottom-up data would instantly desync.

**The Fix:** 
The assignment transaction must be atomic. We must validate the commander's roster capacity *before* we write the `UnitSubordinate` component to the follower.

```csharp
// Inside UnitHierarchySystem processing CmdAssignSubordinate
if (repo.HasComponent<UnitRoster>(cmd.Commander))
{
    var roster = repo.GetComponent<UnitRoster>(cmd.Commander);
    if (roster.Count >= 16) 
    {
        FdpLog<UnitHierarchySystem>.Warn($"UnitRoster on Commander {cmd.Commander.Index} is full. Assignment rejected.");
        return; // Abort transaction; bottom-up link is NOT applied.
    }
    
    // Both components are updated together
    roster.SubordinateEntities[roster.Count] = *(long*)&cmd.Subordinate;
    roster.TacticalDesignations[roster.Count] = cmd.TacticalDesignation;
    roster.Count++;
    repo.SetComponent(cmd.Commander, roster);
    
    repo.SetComponent(cmd.Subordinate, new UnitSubordinate { Commander = cmd.Commander, Designation = cmd.TacticalDesignation });
}
```

### 5. Commander Destruction (Dangling References)
**Validity: Highly Valid.**
If a commander is destroyed, its subordinates are left with a `UnitSubordinate` handle pointing to a dead entity. AI BTree/HSM logic polling this commander reference could encounter exceptions or undefined behavior.

**The Fix:** 
Intercept entity destruction events in `UnitHierarchySystem`. FDP uses `DestructionOrder` (or `DestroyEntityCommand`) to signal teardowns. We must listen to this and unlink the hierarchy.

```csharp
// In UnitHierarchySystem.Execute
var destructionEvents = repo.Bus.Read<DestructionOrder>();
foreach (ref readonly var evt in destructionEvents)
{
    // 1. If it's a commander, release all subordinates
    if (repo.HasComponent<UnitRoster>(evt.Entity))
    {
        var roster = repo.GetComponent<UnitRoster>(evt.Entity);
        for (int i = 0; i < roster.Count; i++)
        {
            Entity sub = new Entity((ulong)roster.SubordinateEntities[i]);
            if (repo.IsAlive(sub))
                repo.RemoveComponent<UnitSubordinate>(sub);
        }
    }
    
    // 2. If it's a subordinate, cleanly detach from its commander
    RemoveFromHierarchy(repo, evt.Entity);
}
```

### 6. Swap-and-Pop Instability
**Validity: Valid for Presentation/Logic Stability.**
While Swap-and-Pop (`roster[i] = roster[last]`) is fast, it scrambles the order of the remaining elements. If the ExCon UI relies on the roster order to draw the ORBAT consistently, or if AI assumes index 0 is the "Executive Officer", swapping will cause visual jumping and logical errors. 

**The Fix:** 
Because N is capped at a tiny number (16), an O(N) memory shift loop is perfectly acceptable and preserves the deterministic order of the roster array.

```csharp
// Inside RemoveFromHierarchy
for (int i = 0; i < roster.Count; i++)
{
    if (roster.SubordinateEntities[i] == subPacked)
    {
        // Shift remaining elements left to preserve insertion order
        for (int j = i; j < roster.Count - 1; j++)
        {
            roster.SubordinateEntities[j] = roster.SubordinateEntities[j + 1];
            roster.TacticalDesignations[j] = roster.TacticalDesignations[j + 1];
        }
        
        // Zero out the last element to be safe
        int last = roster.Count - 1;
        roster.SubordinateEntities[last] = 0;
        roster.TacticalDesignations[last] = 0;
        
        roster.Count--;
        repo.SetComponent(oldCommander, roster);
        break;
    }
}
```



ad "The Fix: Explicitly size the fixed buffers to the absolute maximum capacity (16)." pls use named constant for the fixed array size and set it properly to 16 (not 4)


To perfectly align with FDP's strict coding standards against magic numbers in production code (§CODE-STANDARDS §1), you should define a public constant directly inside the struct. This mirrors how existing fixed-capacity engine components, such as `PassengerBuffer`, define their limits.

Here is the corrected `UnitRoster` component using the named constant for its absolute maximum capacity:

```csharp
[DataPolicy(DataPolicy.NoSave)]
[ComponentId(GlobalComponentIds.UnitRoster)] 
public unsafe struct UnitRoster
{
    /// <summary>
    /// The absolute maximum number of subordinates a single unit can hold.
    /// </summary>
    public const int Capacity = 16;

    public int Count; 
    
    // Explicitly sized fixed buffers using the named constant
    public fixed long SubordinateEntities[Capacity];   
    public fixed ushort TacticalDesignations[Capacity]; 
}
```

By defining `Capacity = 16` centrally, you must also update your systems (like `UnitHierarchySystem`) to use this constant instead of hardcoding `16`. This guarantees that your bounds checking dynamically adapts if the capacity is ever changed in the future, fixing the silent failure gap where array indices were hardcoded:

```csharp
// Inside UnitHierarchySystem processing CmdAssignSubordinate
if (repo.HasComponent<UnitRoster>(cmd.Commander))
{
    var roster = repo.GetComponent<UnitRoster>(cmd.Commander);
    
    // Use the named constant for the bounds check
    if (roster.Count >= UnitRoster.Capacity) 
    {
        FdpLog<UnitHierarchySystem>.Warn($"UnitRoster on Commander {cmd.Commander.Index} is full. Assignment rejected.");
        return; // Abort transaction; bottom-up link is NOT applied.
    }
    
    // ... complete the atomic transaction ...
}
```

