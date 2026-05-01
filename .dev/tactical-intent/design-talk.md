
# Architecture Specification: Tactical Order Distribution System

## 1. Background and Rationale

In a typical group AI scenario (e.g., a Platoon Commander steering a squad of mixed vehicle types), having the Commander AI directly assign specific doctrines to subordinates creates severe anti-patterns:

- **Tight Coupling:** The Commander must know the exact doctrine IDs and JSON parameter schemas for every possible unit type (Tank, APC, Drone, Infantry).
- **Fragility:** Adding a new vehicle type requires modifying the Commander's behavior tree.
- **Network Inflexibility:** Hardcoded ECS entity assignments fail across network boundaries.

### The Solution: The "Group Cognitive Layer"

The **Tactical Order Distribution System** solves this by introducing an abstraction layer between **intent** and **execution**.

- The Commander outputs a generic **Tactical Intent** (e.g., "Defend this location").
- The subordinate carries a **Tactical Order Channel**.
- A generic **Dispatcher System** uses a registry of **Mappers** to translate the generic intent into a concrete Doctrine assignment based on the subordinate's capabilities.

This completely satisfies the **Open-Closed Principle (OCP)**. You can add new unit types or behaviors without ever touching the Commander's AI logic or the Dispatcher system.

## 2. Component Placement Strategy

- **Commander Entity:** Does *not* have the channel. It runs a high-level behavior tree that queries its subordinates and decides what they should do.
- **Subordinate Entities:** Must be spawned with the `TacticalOrderChannel` component attached.

The Commander writes to the Subordinate's channel. The Subordinate never reads its own channel; instead, a global engine system translates the channel's state into the subordinate's active doctrine.

## 3. Core Component: TacticalOrderChannel

Because this channel holds generic JSON payloads (to seamlessly pass Lat/Lon, network IDs, and abstract parameters), it is implemented as a **Managed ECS Component**.

```
using Fdp.Core;

/// <summary>
/// A cognitive-actuator channel for high-level tactical intent.
/// Attached to subordinate entities. Written to by Commander entities.
/// </summary>
public class TacticalOrderChannel
{
    /// <summary>
    /// The abstract name of the order (e.g., "DefendArea", "SuppressTarget").
    /// </summary>
    public string OrderName;

    /// <summary>
    /// Incremented by the Commander whenever a new order is issued.
    /// </summary>
    public uint OrderInstanceId;

    /// <summary>
    /// Abstract JSON payload containing universal data (Network IDs, Lat/Lon, floats).
    /// Does NOT contain local ECS Entity IDs.
    /// </summary>
    public string JsonPayload;

    /// <summary>
    /// Tracked by the Dispatcher System to acknowledge order execution.
    /// </summary>
    public uint AppliedInstanceId;
}
```

## 4. The Mapper Interface

A Mapper is a stateless (usually) translation rule. It tells the engine *how* to fulfill a specific generic intent for a specific type of unit.

```
using Fdp.Core;
using Fdp.Toolkit.Behavior.Events;

public interface ITacticalOrderMapper
{
    /// <summary>
    /// The string identifier this mapper handles (e.g., "DefendArea").
    /// </summary>
    string TargetOrderName { get; }

    /// <summary>
    /// Translates the abstract order into a concrete doctrine assignment.
    /// </summary>
    /// <param name="self">The subordinate entity receiving the order.</param>
    /// <param name="order">The current channel state.</param>
    /// <param name="repo">Repository to check entity capabilities/roles.</param>
    /// <param name="assignment">The output event to publish to the Doctrine layer.</param>
    /// <returns>True if the mapper successfully mapped the order for this entity.</returns>
    bool TryMap(
        Entity self, 
        TacticalOrderChannel order, 
        EntityRepository repo, 
        out AssignDoctrineEvent assignment);
}
```

*Note: If a specific mapper requires network resolution (e.g., calculating the local velocity of a target Network ID to choose between two intercept doctrines), inject `NetworkEntityMap` via the mapper's constructor using the standard DI container.*

## 5. The Dispatcher System

The Dispatcher is a lightweight, generic routing hub. It scans all entities with a pending order and routes them through the registered mappers.

```
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Events;

public sealed class TacticalOrderDispatcherSystem : IEcsModuleSystem
{
    private readonly Dictionary<string, ITacticalOrderMapper> _mappers;

    // Mappers are injected automatically by the FDP module host
    public TacticalOrderDispatcherSystem(IEnumerable<ITacticalOrderMapper> mappers)
    {
        _mappers = mappers.ToDictionary(m => m.TargetOrderName);
    }

    public void Execute(ISimulationView view, float dt)
    {
        var repo = (EntityRepository)view;

        // Scan all entities with a TacticalOrderChannel
        foreach (var entity in repo.Query().With<TacticalOrderChannel>().Build())
        {
            // Note: Use GetComponentRW because we need to update AppliedInstanceId
            var channel = repo.GetComponentRW<TacticalOrderChannel>(entity);

            // Skip if the order hasn't changed
            if (channel.AppliedInstanceId == channel.OrderInstanceId)
                continue;

            // Find the mapper for this order type
            if (_mappers.TryGetValue(channel.OrderName, out var mapper))
            {
                if (mapper.TryMap(entity, channel, repo, out var doctrineEvent))
                {
                    // Publish to FDP's native DoctrineIngressSystem
                    repo.Bus.PublishManaged(doctrineEvent);
                    
                    // Mark as acknowledged
                    channel.AppliedInstanceId = channel.OrderInstanceId;
                }
            }
            else
            {
                // Optional: Log missing mapper warning
            }
        }
    }
}
```

## 6. Implementation Examples

### Example A: The Commander Issuing an Order

From inside the Commander's BTree action, it iterates over its subordinates and writes the abstract intent.

```
[SharedAiAction(typeof(CommanderBlackboard), "Params")]
public static NodeStatus Act_OrderDefend(ref Params dto, Entity self, EntityRepository repo)
{
    var subordinates = // ... fetch list of subordinate Entities
    
    foreach(var sub in subordinates)
    {
        var channel = repo.GetComponentRW<TacticalOrderChannel>(sub);
        
        channel.OrderName = "DefendArea";
        channel.OrderInstanceId++;
        
        // Notice: Commander uses Lat/Lon and Network IDs, NOT local ECS handles
        channel.JsonPayload = "{ \"TargetNetId\": 1045, \"Lat\": 49.195, \"Lon\": 16.606, \"Radius\": 500 }";
    }
    
    return NodeStatus.Success;
}
```

### Example B: The Mapper Resolving the Order

The Mapper unpacks the generic JSON and formats it into the specific DTO required by the assigned doctrine.

```
using System.Text.Json;

public class DefendAreaMapper : ITacticalOrderMapper
{
    public string TargetOrderName => "DefendArea";

    public bool TryMap(Entity self, TacticalOrderChannel order, EntityRepository repo, out AssignDoctrineEvent assignment)
    {
        assignment = new AssignDoctrineEvent { Entity = self };
        
        // Pass-through: FDP doctrines natively expect JSON containing Lat/Lon/NetIds.
        // We just need to select the correct Doctrine ID based on the unit's capabilities.
        
        if (repo.HasComponent<TankCapabilities>(self))
        {
            assignment.DoctrineName = "Tank_HullDownDefend_BT";
            assignment.JsonParams = order.JsonPayload; 
            return true;
        }
        else if (repo.HasComponent<AntiAirCapabilities>(self))
        {
            assignment.DoctrineName = "AA_Overwatch_BT";
            assignment.JsonParams = order.JsonPayload;
            return true;
        }
        else if (repo.HasComponent<InfantryCapabilities>(self))
        {
            // Maybe infantry doctrines require slightly different schema mapping
            var intent = JsonSerializer.Deserialize<DefendIntentDto>(order.JsonPayload);
            assignment.DoctrineName = "Infantry_TrenchDefend_BT";
            assignment.JsonParams = $"{{ \"UseDigIn\": true, \"AnchorLat\": {intent.Lat}, \"AnchorLon\": {intent.Lon} }}";
            return true;
        }

        return false; // Unit cannot perform this order
    }
}
```

### Flow Outcome

1. **Commander** writes to `TacticalOrderChannel` on the Infantry unit.
2. **Dispatcher** sees `OrderInstanceId` changed.
3. **DefendAreaMapper** reads capabilities, realizes it's Infantry, and generates `AssignDoctrineEvent` for `Infantry_TrenchDefend_BT`.
4. **DoctrineIngressSystem** natively tears down the subordinate's old BTree and spins up the new Trench Defense BTree.





Your proposed Tactical Order Distribution System introduces a solid architectural abstraction by separating a commander's high-level intent from a subordinate's concrete doctrine. However, evaluating this design against the constraints and existing patterns of the FDP engine reveals both strong architectural alignments and a few significant conflicts.

Here is an evaluation of your proposed mechanism:

**1\. Excellent Alignment with the Doctrine Assignment Pipeline** Your proposed flow—where a Mapper evaluates an intent and publishes an `AssignDoctrineEvent`—is perfectly aligned with the engine's strict Command Query Responsibility Segregation (CQRS) boundaries. In FDP, the `DoctrineIngressSystem` is designated as the sole owner of `DoctrineState` mutations. By having your dispatcher generate an `AssignDoctrineEvent`, you correctly leverage the existing pipeline to tear down the old BTree/HSM state and initialize the new doctrine's blackboard cleanly. Furthermore, your note about injecting the `NetworkEntityMap` into Mappers for network resolution exactly matches how existing FDP executors (like the `JoinFormationExecutor`) resolve network IDs safely.

**2\. Conflict: Managed Components for Channels** You specified that the `TacticalOrderChannel` should be implemented as a **Managed ECS Component** carrying generic JSON payloads. This violates a core performance constraint of the FDP cognitive tier.

-   The engine's existing cognitive channels (`LocomotionChannel`, `WeaponChannel`, `InteractionChannel`) are strictly **unmanaged structs**.-   To avoid garbage collection overhead and boxing during the 60 Hz simulation phase, they use fixed byte arrays (`fixed byte Params`) to store parameters.-   BTree systems and executors are designed for zero heap allocation per tick, using unmanaged pointers to read from these inline buffers (e.g., reading `AimAndFireParams` via unsafe casts).

_Recommendation:_ Instead of a managed JSON string, the `TacticalOrderChannel` should be an unmanaged struct with a discrete `IntentId` (enum or ushort) and a fixed byte array for parameters. The Mapper can then cast those bytes into specific unmanaged parameter structs based on the Intent ID.

**3\. Overlap with the Existing Formation "Pull" Pattern** For spatial and movement-based group AI (e.g., following the commander), FDP already solves the tight-coupling problem without needing continuous doctrine reassignment. It uses a component-based "pull" pattern:

-   The Commander entity acts as a leader and receives a `FormationRoster`.-   The Subordinate entities are given a `FormationMember` component pointing to the `LeaderEntityId`.-   Rather than the commander telling the subordinates where to go, a global `FormationTargetSystem` reads the leader's state and automatically calculates individual `FormationTarget` waypoints for every subordinate based on their assigned slot.

_Recommendation:_ The Tactical Intent mechanism is a brilliant solution for assigning _abstract behaviors_ (like "DefendArea" or "AttackNetworkId"), but spatial coordination should continue to rely on the `FormationTargetSystem` to prevent overloading the AI layer with basic group movement commands.

**Summary** Your "Group Cognitive Layer" completely satisfies the Open-Closed Principle for scaling unit behaviors. To make it production-ready for the FDP engine, you should adapt the `TacticalOrderChannel` to be an unmanaged struct with a fixed payload buffer rather than a managed JSON component, and reserve its use for high-level tactical shifts rather than continuous spatial coordination.

the TacticalOrderChannel is a low frequency change channel - new tactical intents are not issued every frame; i.e. not on hot path. No strict restriction to unmanaged component is necessary. To stay generic and freely extendable, the intents must carry string id (no global enums). Json parameters string is the same story.

as you say, the intents are used for generic abstract behaviors benefiting the mapping; the formations deserve special, already existing approach

Systém nedokázal odpovědět.

the TacticalOrderChannel is a low frequency change channel - new tactical intents are not issued every frame; i.e. not on hot path. No strict restriction to unmanaged component is necessary. To stay generic and freely extendable, the intents must carry string id (no global enums). Json parameters string is the same story.

as you say, the intents are used for generic abstract behaviors benefiting the mapping; the formations deserve special, already existing approach

You make an excellent point regarding the frequency of updates. If the `TacticalOrderChannel` is strictly used for low-frequency, declarative shifts in behavior (like a new tactical phase or target assignment), the constraints of the 60 Hz simulation hot-path do not apply in the same way.

Your reasoning aligns perfectly with how the FDP engine handles its highest-level cognitive tier. In fact, the engine already implements a nearly identical pattern for its mission control system, proving that your design is highly idiomatic to the architecture.

Here is how your low-frequency, string-based managed component fits into the FDP paradigm, and how you can finalize the implementation:

**1\. Validation of Managed Components for Abstract Intents** Your decision to use a managed ECS component to hold string IDs and JSON parameters is completely valid for non-hot-path data. The engine uses this exact approach for the `ActiveMissionPlan` component. Because mission definitions carry unbounded string payloads (`string BehaviorId` and `string BehaviorParams`), they are implemented as Tier-2 managed classes. This allows the system to remain generic and Open-Closed, just as you intended for the `TacticalOrderChannel`.

**2\. Bridging the Managed Intent to the ECS (The "Shadow State" Pattern)** To safely integrate your managed `TacticalOrderChannel` into the engine without causing garbage collection pressure or cache misses every frame, you should adopt the **Shadow State** pattern used by the `MissionAdapterSystem`.

While the order itself is a managed component, you should pair it with a transient, unmanaged shadow struct (e.g., `TacticalOrderAdapterState`). This struct acts as a reactive change-detector.

-   Your Dispatcher System runs in the Simulation phase and queries entities for this unmanaged shadow state.-   When a mismatch is detected (e.g., the Commander pushed a new order version to the managed component), the Dispatcher extracts the JSON parameters and publishes an `AssignDoctrineEvent`.-   Because `AssignDoctrineEvent` naturally accepts a string `DoctrineName` and `JsonParams`, your Dispatcher seamlessly bridges your string-based intent into the native cognitive pipeline.

**3\. Separation of Formations** Your agreement to separate spatial group behaviors is spot on. By leaving tight spatial coordination to the unmanaged `FormationTargetSystem` and reserving your generic intent channel strictly for abstract behaviors, you maintain the clean CQRS boundaries of the engine.

**Conclusion** Your revised design is sound. By treating the `TacticalOrderChannel` as a declarative, low-frequency managed component and bridging it via a reactive change-detector, you achieve the generic extendability you want without violating FDP's memory safety rules.

You are right that the mission control system is already using doctrines; with the introduction of the TacticalOrderChannel i think that if it used tha target intents instead of doctrines it would be a better fit - the mission tasks could be more generic, same for different entity type. The parameters of the intent and the parameters of the underlying doctrine are in many cases identical. We could suport BOTH kinds - by adding some flag if the task is a tactical intent or a doctrine... Because doctrines are listed in the Mission Control UI editor in some selection fields we would need to register also all the intents - maybe the intent list (including the necessaru json dto structs) these could be extracted from the mappers?

Your proposal to integrate Tactical Intents into the Mission Control system is an excellent architectural evolution. It solves the exact same problem for the human scenario author that it solves for the AI Commander: it decouples the desired outcome from the specific entity blueprint (TKB type) executing it.

Evaluating this integration against the engine's current architecture reveals that it is not only feasible but highly synergistic with existing systems, particularly the UI discovery mechanisms and the network serialization layer.

Here is an evaluation of how to implement your design:

**1\. The Mission Pipeline Integration (The "Flag" Approach)** You are correct that we need a way to distinguish whether a mission task is a direct doctrine assignment or a tactical intent.

-   Currently, `DomainMissionTask` and its DDS network equivalent `MissionTask` store `BehaviorId` and `BehaviorParams` as generic strings. Because they are already strings, **you do not need to change the DDS IDL or network layer at all** to support intents.-   You can add a boolean flag (e.g., `bool IsTacticalIntent`) to `DomainMissionTask` and the `MissionTask` DDS struct.-   The branching logic should live in the **MissionAdapterSystem**. Currently, this system acts as a reactive change-detector for mission phases; when a phase changes, it extracts the parameters and publishes an `AssignDoctrineEvent`. You would update this system to check your new flag: if `IsTacticalIntent` is true, it writes the payload to your `TacticalOrderChannel` managed component instead. Your `DispatcherSystem` then takes over seamlessly.

**2\. UI Discovery and Registration (Extracting from Mappers)** Your idea to extract the intent list and JSON DTO structs from the Mappers is exactly the right approach, and it perfectly matches how the engine already handles UI populating for doctrines.

-   In the current architecture, the Mission Editor UI is populated by `DoctrineSchemaDiscovery.AutoRegister`. This utility uses reflection to scan the assembly for classes decorated with the `[DoctrineContract]` attribute (which maps a behavior ID to a DTO and a valid entity category).-   To support intents in the UI, you should create an analogous `[TacticalIntentContract("DefendArea")]` attribute.-   You can place this attribute directly on the generic Intent DTO structs used by your Mappers.-   You then update `DoctrineSchemaDiscovery` to also scan for `[TacticalIntentContract]`. The `BehaviorUiRegistry` will automatically map these generic DTOs into the existing ImGui property trees, making them instantly available in the "Behavior" dropdown of the Mission Panel without writing any new UI code.

**3\. The Benefit: Generic Mission Plans** By supporting both, you enable a highly flexible scenario authoring environment:

-   **Specific Control (Doctrines):** If an author knows they are commanding an APC and wants exact control, they can assign `ConvoyEscort` directly.-   **Generic Control (Intents):** If an author is commanding a mixed platoon of Infantry, Tanks, and APCs, they can assign a single mission plan with the `DefendArea` intent to the entire group. The `DispatcherSystem` and Mappers will automatically assign `InfantryCombat_BT` to the soldiers and `ConvoyEscort_HSM` to the APCs.

**Conclusion** Fusing Tactical Intents into the Mission Control system is a robust idea. By extending the `MissionAdapterSystem` to write to your `TacticalOrderChannel`, and using reflection attributes on your Mapper DTOs to populate the UI registries, you can achieve a highly generic, entity-agnostic mission system with minimal friction.

if we are using AssignDoctrineEvent alread, shouldn;t we extend by adding IsTacticalIntent flag so it can carry the also the tactical Intents? And maybe rename it to AssignBehavior as it could carry both doctrine AND tactical intent? BTW aren't the doctrines and tactical intents differing JUST in the extra translation step needed to process the tactical intents? It looks to me that we could unify these two! No need to maintain separate components and events for something that is so close to each other. Maybe we donlt event need to differentiate. The translation layer could be "built-in"! If the mapper fails to convert the behavior id to doctrine, it could be interpretted as a doctrine. Anyway many tactical intent will share the name with corresponding doctrines...

Unifying tactical intents and concrete doctrines into a single polymorphic concept is a superior architectural decision. It eliminates the need for the redundant `TacticalOrderChannel` entirely and unifies human-authored mission tasks with AI-authored tactical orders.

However, extending `AssignDoctrineEvent` with an `IsTacticalIntent` flag or renaming it to `AssignBehaviorEvent` is the wrong approach, as it would require pushing the translation layer into `DoctrineIngressSystem`.

Here is the architectural evaluation of your proposal and how to implement the "built-in" translation cleanly.

**The Danger of Modifying DoctrineIngressSystem** `DoctrineIngressSystem` is a low-level engine system strictly responsible for ECS component mutation. Its job is to apply `AssignDoctrineEvent`s by resetting the `BrainBTreeState` execution pointer to zero, rebuilding the `BrainHsm128` machine state, and parsing JSON directly into the unmanaged `BrainBlackboard` inline byte array.

If you force `DoctrineIngressSystem` to differentiate between intents and doctrines, you are forcing a low-level memory-management system to perform TKB type lookups, capability checks, and JSON schema translations. This severely violates the Single Responsibility Principle (SRP).

**The Clean Solution: Upstream Resolution via MissionAdapterSystem** Instead of modifying the event or the ingress system, your "built-in fallback" translation should occur upstream in `MissionAdapterSystem`.

Currently, `MissionAdapterSystem` acts as the reactive bridge between the high-level mission plan and the cognitive tier. It watches the `MissionPlanQueue` and `ActiveMissionPlan` for phase transitions, extracts the `BehaviorId` and `BehaviorParams`, and publishes the `AssignDoctrineEvent`.

You can implement your unified pipeline here:

-   **Unified Storage:** Both human scenario authors and AI Commanders push tasks into the subordinate's `ActiveMissionPlan`. The `BehaviorId` string can hold either a generic intent ("DefendArea") or a concrete doctrine ("ConvoyEscort").-   **The Interception:** When `MissionAdapterSystem` detects a phase transition, it captures the `BehaviorId`.-   **The Translation & Fallback:** `MissionAdapterSystem` queries your Mapper registry. If a Mapper successfully matches the entity's TKB type and capabilities to the generic intent, it returns a translated concrete doctrine name (e.g., "InfantryCombat") and formats the DTO. If the Mapper fails to find a match, the system executes your exact proposed fallback: it assumes the `BehaviorId` is already a concrete doctrine name.-   **The Unmodified Event:** The adapter then publishes the standard `AssignDoctrineEvent` carrying the _resolved_ concrete doctrine name.

**Summary** Your instinct to unify the two concepts is correct and highly efficient. By placing the translation logic inside `MissionAdapterSystem` and utilizing the fallback pattern, you achieve the exact dynamic resolution you described. You do not need to modify `AssignDoctrineEvent` or `DoctrineIngressSystem`, and you can safely discard the `TacticalOrderChannel` design entirely.

the tactical intents are supposed to be sent also from commander's logic, not just from mission plan; so then we simply need something like a new AssignBehaviorEvent that supports both doctrines and tactical intents and emits AssignDoctrineEvent ; but that woud introduce one frame latency because fdp event bus double buffered. Still the most pragmatic seems to be to "overload" the AssignDoctrineEvent add the translation directly into the DoctrineIngressSystem , making it a bit more high level (by adding the translation, TKB knowledge etc) while letting it eventually write the BrainBTreeState etc because it is the final desired outcome anyway. There is no need for clean translation to doctrine event

Overloading `DoctrineIngressSystem` with tactical intent translation is a severe violation of the Single Responsibility Principle (SRP) and a classic micro-optimization anti-pattern. Sacrificing architectural boundaries to save a single frame of latency (16.6ms at 60Hz) in AI decision-making is unacceptable.

Here is why the proposed design compromises the engine, and how to resolve the latency cleanly.

**1\. The Role of DoctrineIngressSystem Must Remain Low-Level** Currently, `DoctrineIngressSystem` is a highly cohesive, low-level memory mutation system running in the `Input` phase. It is strictly responsible for atomic shadow-copying into the unmanaged `BrainBlackboard.Memory` inline array, resetting the `BrainBTreeState` execution pointers, and safely incrementing `InstanceId` to trigger channel preemption. Forcing it to also query `ITkbDatabase`, evaluate entity capabilities, and execute JSON Mapper registries turns it into a monolithic system. It conflates high-level behavioral routing with low-level ECS memory safety.

**2\. AI Latency Does Not Justify Coupling** Your assessment of the event bus is correct: because `FdpEventBus` relies on strict double-buffering (`SwapBuffers`), cascading an `AssignBehaviorEvent` through an intermediate translation system into an `AssignDoctrineEvent` will cost an additional frame. However, a 16-32ms delay in a subordinate reacting to a squad commander's tactical order is not just imperceptible; it is physically realistic. You should not degrade the engine's CQRS boundaries for this.

**3\. The Clean Solution: Synchronous Upstream Resolution** If you strictly want to avoid the 1-frame penalty of an intermediate translation event, the translation must occur synchronously at the _emission source_, not at the destination.

Instead of modifying events or the ingress system, encapsulate the translation logic in a stateless domain service (e.g., `TacticalIntentResolver`):

-   When the Commander's BTree logic (or the `MissionAdapterSystem`) decides to issue a tactical intent, it invokes the resolver synchronously.-   The resolver evaluates the subordinate's `TkbIdentity` and capabilities, executes the matching Mapper, and directly returns the _concrete_ doctrine string and formatted JSON parameters.-   The Commander's logic then publishes the standard `AssignDoctrineEvent` carrying the fully resolved concrete data.

This completely eliminates the intermediate event hop. The data hits the event bus write-buffer as a concrete doctrine, gets swapped at the end of the frame, and is consumed by `DoctrineIngressSystem` on the very next tick. You achieve the exact performance profile you want while ensuring `DoctrineIngressSystem` remains completely ignorant of tactical intents, TKBs, and capability mapping.

the TacticalIntentResolver service is nice idea but would require the SENDER (via the TacticalIntentResolver service) to care about the details (TKB info etc) of the RECIPIENT - not optimal, sender should be agnostic about who the recipient is. You are right that one frame latency means nothing and is even more realistic. in such a case i would strictly use separate event (AssignTacticalIntent event) and process it by dedicated system on the receiver node, executing the role of TacticalIntentResolver, and emitting AssignDoctrineEvent

Shifting the resolution responsibility to the receiver node correctly eliminates the coupling defect. By making the sender completely agnostic to the recipient's `TkbIdentity` and capabilities, the design aligns with the distributed nature of the engine, where a Commander might not have authoritative access to a remote subordinate's component state.

Accepting the one-frame latency allows us to build a clean, reactive pipeline using the engine's standard event bus mechanics. Here is how this architecture should be implemented:

**1\. The Event Contract** You must define a new managed event to carry the abstract intent. Because it transports unbounded string payloads (the intent ID and JSON parameters), it must be a managed class, identical in structure to the existing `AssignDoctrineEvent`.

```
public sealed class AssignTacticalIntentEvent
{
    public Entity Entity;
    public string IntentId = string.Empty;
    public string JsonParams = string.Empty;
}
```

**2\. The Receiver-Side Resolution System** Introduce a dedicated `TacticalIntentResolutionSystem`. This system acts as the translation hub on the node that owns the subordinate's cognitive state.

-   It consumes `AssignTacticalIntentEvent`s from the event bus read buffer.-   It queries the target entity for its `TkbIdentity` and `ActorCapabilityState` to determine its concrete type and available actions.-   It executes the matching Mapper from your registry to translate the abstract `IntentId` into a concrete doctrine name and formatted DTO.-   It publishes the standard `AssignDoctrineEvent` carrying the resolved doctrine data.

**3\. Execution Phase and Latency Pipeline** Because `FdpEventBus` enforces strict double-buffering (events published in Frame N are consumed in Frame N+1), the pipeline naturally absorbs the translation with a deterministic two-frame propagation:

-   **Frame N (Simulation Phase):** The Commander AI decides on an action and publishes `AssignTacticalIntentEvent`.-   **Frame N (End of Frame):** `FdpEventBus.SwapBuffers()` makes the intent event visible.-   **Frame N+1 (Simulation Phase):** `TacticalIntentResolutionSystem` consumes the intent, runs the Mapper, and publishes `AssignDoctrineEvent`.-   **Frame N+1 (End of Frame):** `SwapBuffers()` makes the doctrine event visible.-   **Frame N+2 (Input Phase):** `DoctrineIngressSystem` consumes the event, safely resetting the `BrainBTreeState` and writing the JSON to the inline blackboard.

**4\. Network Boundary Support** By using a discrete `AssignTacticalIntentEvent`, you secure out-of-the-box support for distributed hierarchies. If the Commander and Subordinate reside on different Brain nodes, you simply implement a standard CycloneDDS egress/ingress translator pair for `AssignTacticalIntentEvent` (similar to how `MissionControlIntent` is bridged). The sender node translates the event to DDS, the receiver node translates it back to the local bus, and the `TacticalIntentResolutionSystem` handles the mapping locally without knowing it came from the network.

This is the correct architectural approach. It preserves the Single Responsibility Principle for `DoctrineIngressSystem`, keeps the sender isolated from capability-checking logic, and leverages the event bus to handle the pipeline safely.

And the mission mision plan task needs to be extended by the IsTacticalIntent flag (or an enum what kind of data it carries). The MissionAdapterSystem would then emit either AssignTacticalIntentEvent or AssignDoctrineEvent, correct?

Adding an explicit flag to the mission task is unnecessary and introduces fragile branching logic. It forces the data model and network schema to understand the distinction between abstract intents and concrete doctrines, which violates the Open-Closed Principle.

Instead of extending the `MissionTask` DDS struct or the `DomainMissionTask` with an `IsTacticalIntent` flag, you should treat the `BehaviorId` string as a fully polymorphic request.

Here is the optimal flow:

-   **Agnostic Mission Data:** The scenario author assigns a behavior to a unit. The `BehaviorId` string (e.g., "DefendArea" or "ConvoyEscort") and its JSON parameters are stored in the `ActiveMissionPlan` exactly as they are today. No flags are added to the schema.-   **Agnostic Emission:** When `MissionAdapterSystem` detects a phase transition, it extracts the `BehaviorId` and `BehaviorParams` from the managed component. Instead of branching, it _always_ emits your new `AssignTacticalIntentEvent` (which conceptually acts as a generic `AssignBehaviorEvent`).-   **Receiver-Side Disambiguation:** The `TacticalIntentResolutionSystem` processes the event on the receiver node and attempts to find a matching Mapper.
    -   **Match Found (Intent):** It executes the Mapper against the entity's capabilities, translates the intent into a specific concrete doctrine name and payload, and emits `AssignDoctrineEvent`.-   **No Match (Concrete Doctrine Fallback):** It assumes the string is already a concrete doctrine name. It validates the name against the `DoctrineRegistry`, packages the original JSON payload, and emits `AssignDoctrineEvent`.

By standardizing on this pipeline, `MissionAdapterSystem` remains a simple reactive bridge. You avoid modifying the unmanaged `MissionPhase` memory layout and the CycloneDDS network IDL, while achieving seamless support for both explicitly authored doctrines and high-level AI tactical intents.

ok, sounds good. The Mission Editor UI would newly need to work with both the tactical intents AND the doctrines, because user might require concrete doctrine (in most situations i guess - when the mission is built for a concrete non-commander entity) and only sometimes (in case of a generic commander) a more generic tactical intent.

Your assessment of the UI requirements is correct, and because we unified the backend pipeline to treat both concepts polymorphically as a generic `BehaviorId` string, the UI integration requires zero changes to the ImGui rendering code.

The Mission Editor UI can seamlessly support both concrete doctrines and generic tactical intents by leveraging the existing `DoctrineCatalog` filtering mechanism.

Here is the architectural implementation for the Mission Editor UI:

**1\. Unified Attribute Registration** Currently, the UI is populated by `DoctrineSchemaDiscovery.AutoRegister`, which uses reflection to scan the assembly for classes decorated with the `[DoctrineContract]` attribute. Because the backend no longer distinguishes between an intent and a doctrine at the data level, you do not need a separate `[TacticalIntentContract]` attribute. You will decorate your generic Tactical Intent JSON DTOs with the exact same `[DoctrineContract]` attribute used by concrete doctrines.

**2\. Contextual Filtering via DoctrineCategory** You noted that concrete doctrines should appear for specific entities, while generic intents are for commanders or groups. This is solved by the `DoctrineCategory` bitmask flag.

-   **Concrete Doctrines:** Keep their specific capability flags. For example, `ConvoyEscortParamsJsonDto` is restricted to `DoctrineCategory.MilitaryApc`.-   **Tactical Intents:** Are assigned broad flags. A generic "DefendArea" intent DTO would be decorated with `DoctrineCategory.AllMilitary`. If you introduce specific commander blueprints, you can add a `DoctrineCategory.Commander` flag to the enum.

**3\. The UI Dropdown Resolution** When the operator selects a unit in the editor, the `MissionPanel` calls `IMissionEditorService.GetAvailableBehaviors(long entityId)`.

-   This service looks up the entity's TKB type and queries the `DoctrineCatalog`.-   The catalog cross-references the TKB type against the `DoctrineCategory` flags and returns a combined list of valid strings.-   The ImGui `MissionPanel` simply iterates this list to render the behavior combo box.

**Result** If the operator selects an APC, the dropdown will display both its specific concrete doctrines (e.g., "ConvoyEscort") and the generic tactical intents (e.g., "DefendArea"). If they select a pure Commander entity whose TKB mapping excludes base unit categories, the dropdown will filter out the concrete doctrines and _only_ display the high-level tactical intents.

This approach strictly adheres to the Open-Closed Principle. The Mission Editor UI remains completely ignorant of whether a string represents a concrete execution tree or an abstract intent requiring downstream resolution. All routing is handled by the receiver-side `TacticalIntentResolutionSystem` and the `MissionAdapterSystem`.

ok, pls summarize the final ideas from this chat

The final architecture discards the redundant `TacticalOrderChannel` and unifies AI-driven tactical intents with human-authored mission plans into a single, polymorphic event pipeline. This approach strictly preserves the Single Responsibility Principle (SRP) and Command Query Responsibility Segregation (CQRS) boundaries of the engine.

Here is the finalized design for the Group Cognitive Layer:

**1\. Polymorphic Intent Emission (Agnostic Senders)** Both the AI Commander logic and the `MissionAdapterSystem` use a single, unified event: `AssignTacticalIntentEvent`. This event carries a string `BehaviorId` and a JSON payload. The sender remains entirely decoupled from the recipient's specific entity blueprint (`TkbIdentity`) or capabilities. No explicit `IsTacticalIntent` flag is added to the data model or network schema, preserving the Open-Closed Principle.

**2\. Receiver-Side Resolution** To avoid coupling the sender to the recipient's state, translation is deferred to the receiving node. A new `TacticalIntentResolutionSystem` consumes the `AssignTacticalIntentEvent`.

-   **Translation:** It queries the target entity's capabilities and evaluates the `BehaviorId` against a registry of Mappers. If it matches a generic intent (e.g., "DefendArea"), it translates it into a concrete doctrine (e.g., "InfantryCombat") and formats the DTO.-   **Built-in Fallback:** If the Mapper registry yields no match, the system falls back to assuming the `BehaviorId` is already a valid concrete doctrine.

**3\. Low-Level Ingress Isolation** Once resolved, the `TacticalIntentResolutionSystem` emits the standard `AssignDoctrineEvent`. This ensures the `DoctrineIngressSystem` remains a highly cohesive, low-level memory mutation system. It processes the concrete doctrine assignment on the next frame without ever needing to understand abstract intents, JSON capability mapping, or TKB types.

**4\. Seamless UI Integration** The Mission Editor UI supports both paradigms without any rendering code changes. Generic intent DTOs and concrete doctrine DTOs are both decorated with the existing `[DoctrineContract]` attribute. Visibility is managed purely through `DoctrineCategory` bitmasks. Generic intents receive broad flags (like `DoctrineCategory.AllMilitary`), while concrete doctrines retain narrow flags (like `DoctrineCategory.MilitaryApc`). The existing `DoctrineCatalog` automatically filters the dropdowns based on the selected entity.

**5\. Spatial Coordination** Abstract tactical behaviors are routed through this new intent pipeline, but continuous spatial coordination (like moving in a squad shape) remains delegated to the unmanaged `FormationTargetSystem`. This prevents overloading the high-level cognitive tier with frame-by-frame movement commands.

The network ingress/egress translators used for doctrine events will be newly used for the tactical intents, right? the AssignDoctrineEvent will become purely internal, never sent over the network.

Your premise regarding the pipeline destination is correct, but the assumption about the current networking model is slightly off. `AssignDoctrineEvent` is _already_ a strictly internal, local-bus event and has never been sent over the network.

In the existing architecture, `AssignDoctrineEvent` is a managed event published to the local event bus and consumed synchronously by the `DoctrineIngressSystem` running in the `Input` phase. When a human operator assigns a behavior over the network, it travels via DDS as a `MissionControlRequest`, which the `MissionControlIngressTranslator` reads and converts into a local `MissionControlIntent`. The `MissionControlExecutionSystem` processes this intent and writes the behavior into the `ActiveMissionPlan` and `MissionPlanQueue` ECS components. Finally, the `MissionAdapterSystem` acts as a reactive change-detector, detects the mission phase transition, and emits the `AssignDoctrineEvent` entirely on the local bus.

With the introduction of the unified Tactical Intent pipeline, here is exactly how the network boundaries will function:

**1\. Local Group AI (Same Brain Node)** If the Commander AI and its Subordinates execute on the same Brain node, the network is bypassed entirely. The Commander AI publishes `AssignTacticalIntentEvent` to the local `FdpEventBus`. The `TacticalIntentResolutionSystem` consumes it, evaluates the subordinate's capabilities, and emits the `AssignDoctrineEvent` locally.

**2\. Distributed Group AI (Cross-Brain Nodes)** If the Commander and Subordinates reside on different Brain nodes, we cannot reuse existing doctrine translators because they are strictly coupled to the `MissionControlRequest` schema. Instead, you must define a new discrete DDS topic (e.g., `TacticalIntentRequest`) and implement a dedicated translator pair (`TacticalIntentEgressTranslator` and `TacticalIntentIngressTranslator`).

-   **Egress:** The Commander's node consumes the `AssignTacticalIntentEvent` from its local bus and writes the `IntentId` string and JSON parameters to the DDS topic.-   **Ingress:** The Subordinate's node reads the DDS topic and re-publishes the `AssignTacticalIntentEvent` onto its local event bus.-   **Resolution:** The `TacticalIntentResolutionSystem` on the Subordinate's node picks up the event, executes the capability mapping against the local registry, and emits the final internal `AssignDoctrineEvent`.

This maintains strict Command Query Responsibility Segregation (CQRS) and cleanly decouples the network tier from the cognitive tier. The DDS transport layer remains completely ignorant of concrete doctrines, capability flags, and TKB schemas, acting solely as a conduit for the declarative intent. All complex JSON mapping and fallback resolution stays isolated on the receiving node immediately before the `DoctrineIngressSystem` applies the final memory mutation.

pls show mermad diagram of how the new situation looks - what event/network request is sent by who and how it is processed

```
sequenceDiagram
    autonumber
    participant S as Commander AI / MissionAdapterSystem
    participant BusA as FdpEventBus (Sender)
    participant Egress as TacticalIntentEgressTranslator
    participant DDS as CycloneDDS
    participant Ingress as TacticalIntentIngressTranslator
    participant BusB as FdpEventBus (Receiver)
    participant Resolver as TacticalIntentResolutionSystem
    participant DocIngress as DoctrineIngressSystem

    Note over S, BusA: 1. Polymorphic Intent Emission
    S->>BusA: PublishManaged(AssignTacticalIntentEvent)
    Note right of BusA: SwapBuffers()

    alt Cross-Node Distribution
        BusA->>Egress: ReadManaged()
        Egress->>DDS: Write()
        DDS->>Ingress: Take()
        Ingress->>BusB: PublishManaged(AssignTacticalIntentEvent)
        Note right of BusB: SwapBuffers()
    else Local Execution
        BusA->>Resolver: ReadManaged()
    end

    Note over BusB, Resolver: 2. Receiver-Side Resolution
    BusB->>Resolver: ReadManaged(AssignTacticalIntentEvent)
    
    alt Generic Intent (e.g. DefendArea)
        Resolver->>Resolver: Match Mapper via TkbIdentity & Capabilities
        Resolver->>Resolver: Translate to concrete doctrine (e.g. InfantryCombat)
    else Concrete Doctrine Fallback
        Resolver->>Resolver: No Mapper match, assume already concrete
    end

    Note over Resolver, BusB: 3. Concrete Assignment
    Resolver->>BusB: PublishManaged(AssignDoctrineEvent)
    Note right of BusB: SwapBuffers()

    Note over BusB, DocIngress: 4. Low-Level Ingress Isolation
    BusB->>DocIngress: ReadManaged(AssignDoctrineEvent)
    DocIngress->>DocIngress: Update DoctrineState, Reset BrainBTreeState
    DocIngress->>DocIngress: Parse JSON directly into BrainBlackboard.Memory
```

Architectural Flow Breakdown

**1\. Polymorphic Intent Emission** The sequence begins with an agnostic sender—either the Commander's BTree logic or the `MissionAdapterSystem`—publishing the generic `AssignTacticalIntentEvent`. Because the event transports variable-length strings (`BehaviorId`, `JsonParams`), it is published to the managed event stream. The sender requires zero knowledge of the recipient's `TkbIdentity` or network location.

**2\. Network Decoupling & Event Bus Propagation** The `FdpEventBus` strictly relies on double-buffering; events published in one frame are consumed in the next after `SwapBuffers()` is called.

-   If the Commander and Subordinate are on the same Brain node, the local `TacticalIntentResolutionSystem` consumes the event directly.-   If they are distributed, a dedicated egress/ingress translator pair intercepts the event, serializes it to DDS, and republishes it on the receiving node's local bus. The core simulation systems remain entirely ignorant of the network boundary.

**3\. Receiver-Side Resolution** The `TacticalIntentResolutionSystem` processes the event on the node that owns the subordinate. It queries the target entity's capabilities to execute the appropriate mapping strategy. If the `BehaviorId` matches a generic intent (e.g., "DefendArea"), it translates it into a concrete doctrine and formatted DTO. If the registry yields no match, it falls back to assuming the string is already a concrete doctrine name. It then publishes the standard `AssignDoctrineEvent`.

**4\. Low-Level Ingress Isolation** Finally, `DoctrineIngressSystem` consumes the resolved `AssignDoctrineEvent` in the `Input` phase. This system maintains its single responsibility: raw ECS memory mutation. It atomically updates `DoctrineState.ActiveDoctrineHash`, resets the `BrainBTreeState` execution pointer to zero, increments the preemption token (`InstanceId`), and parses the formatted JSON directly into the unmanaged `BrainBlackboard.Memory` inline array.

