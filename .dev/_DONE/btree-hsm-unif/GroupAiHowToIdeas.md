In the attached sources there is the simulation adngine (FDP/HROT) and the GUIDE that describes how to develop AI behaviors.
I would like to start developing complex AI behaviors, like for example the coordination of multiple vehicle entities - a group. I am not sure how to approach it. I guess we might need a logical entity playing the role of a group leader (the Commander) where the coordination behavior will run, and have some way of controlling the behavior of the subordinate group members - giving the "orders" and receiving "reports" or some other way of communication
 - forcing subordinates to execute a concrete behavior with concrete parameters
 - be able to access some shared state between subordinates and their leader
 - or be able to send and receive coordination messages
The messages might also cross a newtwork boundary as in a distributed system different members of the group might (theorethically, probaly not as a pragmatic decission) to live on different simulation nodes.





I will analyze the user's intent, the context, and the provided snippets.

**1. Establish user intent:**

The user wants to know how to develop complex AI behaviors for coordinating a group of vehicle entities (e.g., a leader coordinating subordinates) in the FDP/HROT engine.

They specifically ask about:

- Giving "orders" and receiving "reports" / communication.
- Forcing subordinates to execute a concrete behavior with concrete parameters.
- Accessing shared state between subordinates and their leader.
- Sending and receiving coordination messages, potentially across network boundaries.

**2. Analyze the context and retrieved files:**

From `GUIDE.md`:

- The engine uses a CQRS boundary. Brain tier (Cognitive) queries ECS state and writes intents to actuator channels (LocomotionChannel, WeaponChannel).
- Behavior: BTree, HSM, Hardcoded.
- BrainBlackboard: a 128-byte unmanaged memory bus.
- **Section on "Large data behaviors" (Snippet 10 in HROT or GUIDE):** When a behavior requires massive data, you don't pollute the BrainBlackboard. You lean into DOD/ECS and define a dedicated ECS component.
- "Shared Conditions and Actions" project via ECS Repository (`EntityRepository`).
- "If your heavy component requires complex setup, you simply inject it into the SpawnEntityCommand.InitialComponents list... The AI developer writes pure logic, and the engine flawlessly respects the memory boundaries."

From `HROT_117.txt` and `FDP_117.txt`:

- We see mentions of `MissionControlExecutionSystem`, `MissionPlanQueue`, `ActiveMissionPlan`.
- The user asks about "forcing subordinates to execute a concrete behavior with concrete parameters". This sounds exactly like what the "Mission Control" layer does.
- Snippet 34 (FDP_117): `AssignBehaviorHashEvent` and `AssignBehaviorEvent`. MissionDirectorSystem reads from `MissionPlanQueue.Phases` and delegates writes to `BehaviorIngressSystem` via `AssignBehaviorHashEvent`.
- Snippet 23 (FDP_117): MissionControlRequest sent over DDS. `CreateEntityCommand`, `MissionControlIntent`.
- Snippet 36 (FDP_117): `JoinFormationExecutor`! It writes to LocomotionChannel. `CmdJoinFormation`. The leader is specified by `LeaderNetworkId`.
- Snippet 16 (HROT_117): `TkbEntityTypes.Unit_TankPlatoon`. `Subordinates.Add(new TkbChildSlot { TkbType = ... Count = 4, RoleTag = "Tank" })`. Shows composite / ORBAT structures.
- Group behaviors and shared state: In ECS, state is shared by querying components. A leader could have a `SquadState` component, and subordinates could query it if they have the leader's entity ID.
- Network boundaries: Brain vs Muscle tiers. CGF (Brain) runs the behaviors. If subordinates are on different nodes, we use DDS. But wait, `JoinFormationExecutor` says "if `_entityMap.TryGetEntity(p.LeaderNetworkId, out var leaderEntity)`". This implies the leader entity must be replicated to the node where the subordinate's brain is running, or they all run on the same CGF node.
- DDS Mission Control: `MissionControlRequest` over DDS can assign tasks remotely. `MissionCommandScenario` shows `MissionDirectorSystem` consuming `MissionPlanQueue`.

Let's organize the response based on the user's specific questions.

**User's Points:**

- **Logical entity playing the role of a group leader:** Yes, in HROT this is often a composite entity (like `Unit_TankPlatoon` or `Unit_InfantrySquad`). They have an ORBAT hierarchy. The leader can run a higher-level BTree/HSM that decides squad goals.
- **Forcing subordinates to execute a concrete behavior with concrete parameters:** You use the **Mission Control system**. The Leader's behavior logic can emit ECS events (or DDS messages if remote) to change a subordinate's `MissionPlanQueue`. Or use `AssignBehaviorEvent` locally.
- **Access shared state between subordinates and their leader:** Use standard ECS components. Since actions get `EntityRepository`, a subordinate's action can use the leader's `Entity` (e.g., found via `LeaderNetworkId` and `_entityMap` or stored on a component) to call `repo.GetComponentRO<LeaderStateComponent>(leaderEntity)`.
- **Sending and receiving coordination messages:** Use ECS Event Bus (e.g., `repo.Bus.Publish(new SquadMessageEvent {...})`) locally. If crossing network boundaries, you define a DDS topic and write a translator (`IDescriptorTranslator` or `INetworkEventTranslator`) to bridge the DDS topic to the local ECS event bus.

Let's write a comprehensive guide for them, referencing the engine's mechanics.

- *Coordination architecture overview:* Define a leader entity, give it a custom ECS component for shared state.
- *Shared State:* Explain how `[SharedAiAction]` injects `EntityRepository`, allowing subordinates to read the leader's component (e.g., `repo.GetComponentRO<SquadState>(leaderEntity)`). Mention the 128-byte `BrainBlackboard` limit and why custom ECS components are better for squad data.
- *Forcing Behaviors:* Explain the `MissionPlanQueue` and `MissionDirectorSystem`. Leaders can assign missions by modifying the subordinate's `MissionPlanQueue` or emitting a `MissionControlIntent`/`MissionControlRequest` over DDS.
- *Network boundaries:* Explain that entities are replicated via `NetworkEntityMap`. If a subordinate needs the leader, it uses the leader's `NetworkId` to resolve the local ghost `Entity`. For explicit messages, use DDS topics translated into local ECS events via `IDescriptorTranslator`.
- *Formations (Out-of-the-box):* Mention `JoinFormationExecutor` and `JoinFormation_BT` (Behavior ID 3003) which already exist in the engine for geometric coordination.

Let's refine the specific code structures:

1. **Shared State:**

C#

```
[SharedAiAction(typeof(MyBlackboard), "Params")]
public static NodeStatus Action_ReadLeaderState(ref MyParams dto, ref BehaviorTreeState state, ref BTreeContext ctx) {
    // Resolve leader from network map if needed, or from a component
    if (ctx.World.HasComponent<LeaderRef>(ctx.Self)) {
        var leader = ctx.World.GetComponentRO<LeaderRef>(ctx.Self).LeaderEntity;
        var squadData = ctx.World.GetComponentRO<SquadDataComponent>(leader);
        // React to squad data...
    }
    return NodeStatus.Running;
}
```

1. **Forcing Behaviors:**

   The mission layer drives this. To assign a mission across the network, send a `MissionControlRequest` (DDS) or use the `ICommandGateway.SendMissionControlRequestAsync`.

   Locally, you could push to the `repo.Bus` an `AssignBehaviorHashEvent` (though typically `MissionDirectorSystem` does this by reading `MissionPlanQueue`).

   So the leader action writes a new `MissionPlanQueue` to the subordinate.

2. **Network messaging:**

   If nodes are split, a custom DDS topic (e.g., `SquadCoordinationMsg`) mapped via a translator that implements `INetworkEventTranslator` or `IDescriptorTranslator` to publish an ECS event on `repo.Bus.Publish(new SquadCoordinationEvent...)`.

Let's format the response nicely.Based on the FDP/HROT engine architecture described in the provided documentation, your intuition is completely correct. The engine's rigid CQRS (Command Query Responsibility Segregation) boundary, ECS (Entity Component System) foundation, and DDS network layer provide exact mechanisms for all the group coordination patterns you described.

Here is how you should architect complex, multi-entity AI behaviors (like group coordination) within the FDP/HROT constraints:

### 1. The Commander Entity & ORBAT Hierarchy

The engine naturally supports composite units (ORBATs) such as a `Unit_TankPlatoon` or `Unit_InfantrySquad`.  



- **The Leader:** You spawn a logical (or physical) "Commander" entity. This entity is assigned a higher-level BTree or HSM behavior (e.g., a "PlatoonCommander_BT").  

  

  

- **Subordinates:** Subordinates store a reference to their leader. Typically, this is done via a dedicated ECS component holding the leader's `NetworkId` or local ECS `Entity` handle (e.g., the `LeaderNetworkId` used in `JoinFormationParams` ).  

  

  

### 2. Accessing Shared State (The ECS DOD Approach)

The `BrainBlackboard` is rigidly restricted to 128 bytes to guarantee cache locality and zero-allocation performance on the hot path. **Do not use the blackboard for complex shared group state.** Instead, use standard ECS components. Because compiler-generated AI thunks pass the live `EntityRepository` into your actions, subordinates can read the leader's custom ECS component directly.  



**How to implement it:**

1. Define an unmanaged ECS component for the group state (e.g., `SquadTacticalState`) and attach it to the Commander entity.  

   

   

2. In a subordinate's `[SharedAiAction]`, use the `BTreeContext` (or `HsmKernelBridge`) to access the `EntityRepository`.  

   

   

3. Resolve the Commander's entity handle and read the state.

C#

```
[SharedAiAction(typeof(MyBlackboard), nameof(MyBlackboard.Params))]
public static NodeStatus Action_EvaluateSquadGoal(ref MyParams dto, ref BehaviorTreeState state, ref BTreeContext ctx) 
{
    // Assume subordinate has a component linking it to the leader
    if (ctx.World.HasComponent<SquadMember>(ctx.Self)) {
        var leaderEntity = ctx.World.GetComponentRO<SquadMember>(ctx.Self).LeaderEntity;
        
        // Read the shared state directly from the leader's ECS component
        ref readonly var squadState = ref ctx.World.GetComponentRO<SquadTacticalState>(leaderEntity);
        
        if (squadState.Order == SquadOrder.FlankLeft) {
            // execute flanking logic...
        }
    }
    return NodeStatus.Running;
}
```

### 3. Forcing Subordinates to Execute Concrete Behaviors

The engine abstracts all tactical execution behind a black-box system called the `MissionDirectorSystem`. To give an "order" that forces a subordinate into a specific behavior, the Commander should modify the subordinate's `MissionPlanQueue`.  



**Local Execution (Same Node):** If the Commander and Subordinate are simulated on the same CGF (Brain) node, the Commander's BTree action can directly retrieve the subordinate's `Entity` and overwrite its `MissionPlanQueue` component. The `MissionDirectorSystem` will automatically detect the new phase, clear the old behavior, and publish an `AssignBehaviorHashEvent` to start the new behavior with the specified parameters.  



**Distributed Execution (Network Boundary):** If subordinates might live on different nodes, you cannot directly mutate their ECS components. Instead, the Commander issues a **Mission Control Request**.  



- The Commander logic creates a `MissionControlIntent` (or sends a `BdcMissionControlRequest` / `NedMissionControlRequest` via the DDS `ICommandGateway`) targeting the subordinate's `NetworkId`.  

  

  

- Over the network, the subordinate's node receives this via the `MissionControlExecutionSystem`, which translates the request and applies the new `MissionPlanQueue` locally.  

  

  

### 4. Coordination Messages & Network Boundaries

For explicit "reports" (e.g., "enemy spotted at X") or coordination pings that cross the network, you use the engine's DDS ingress/egress translator pattern:

1. 

   **Define a DDS Topic:** Create a struct representing your message (e.g., `SquadContactReportMsg`) and mark it with `[DdsTopic]`.  

   

   

2. **Egress Translator (Sender):** Create an `IDescriptorTranslator` (or `INetworkEventTranslator` for transient events) on the sending node. The sender's Brain logic publishes an ECS event (`repo.Bus.Publish(...)`). The Egress Translator polls the bus in the `ScanAndPublish` method and writes it to DDS.  

   

   

3. **Ingress Translator (Receiver):** On the receiving node, the Ingress Translator polls the DDS topic in `PollIngress`. It decodes the DDS message and publishes a corresponding local ECS event onto its own `repo.Bus`.  

   

   

4. **Network Entity Resolution:** Network IDs are strictly decoupled from local ECS Entity IDs. In your ingress translator, you must use the `NetworkEntityMap` (specifically `_entityMap.TryGetEntity(networkId, out var localEntity)`) to resolve the sender/target network IDs into local ECS handles before passing the data to the local AI logic.  

   

   

### Out-of-the-Box Tools

Note that for simple geographic coordination, the engine already provides a `JoinFormation` behavior (`JoinFormation_BT` / ID 3003) and a `JoinFormationExecutor`. The executor takes a `LeaderNetworkId`, resolves it via the `NetworkEntityMap`, and interfaces with the kinematics layer to physically group the vehicles. You can use this as a reference implementation for resolving network IDs into physical coordination.  





Mission Control mechanism is a bit high level with the tasks/behavior sequence and their pro-programmed. It is intended to be used with the leader as the top level behavior control. But the group member "steering" might benefit from a more direct approach - for example direct control of the behaviors for individual members; these behaviors could be assigned bu the leader according to leader's wishes (those requests sent from the actions executed within leader's HSM or BTree), while the leader keeps monitoring the state of his group members, possibly receiving some reports from them (reports published as entity component that is senced with the leader so the leader can access it), don't you think? 







You are absolutely right. Your thinking aligns perfectly with how high-performance ECS architectures handle tight, dynamic group AI.

While the `MissionPlanQueue` is excellent for macro-level routing (like a pre-programmed sequence of waypoints and engagement rules assigned by a scenario file), tight "steering" of a squad requires the Leader to act as a dynamic, real-time director.

Here is how your approach maps directly to the FDP/HROT engine mechanisms:

### 1. Direct Behavior Assignment (Top-Down Steering)

Instead of pre-programming a queue, the Leader's HSM or BTree acts as the active "brain" for the whole group. When the Leader decides a subordinate needs to change tactics (e.g., from "Formation Follow" to "Suppressing Fire"), the Leader's action does not need to use the high-level Mission Control DDS network.

Instead, from within the Leader's `[SharedAiAction]`, you can directly publish an **`AssignBehaviorHashEvent`** (or `AssignBehaviorEvent`) to the local Event Bus:

C#

```
[SharedAiAction(typeof(LeaderBlackboard), "Params")]
public static NodeStatus Action_OrderSubordinate(ref Params dto, ref BehaviorTreeState state, ref BTreeContext ctx) {
    var subordinateEntity = // ... resolve subordinate entity
    var behaviorHash = // ... hash of the "SuppressingFire" behavior

    // Force the subordinate to immediately switch behaviors
    ctx.World.Bus.Publish(new AssignBehaviorHashEvent {
        TargetEntity = subordinateEntity,
        Hash = behaviorHash
    });

    return NodeStatus.Success;
}
```

The engine's `BehaviorIngressSystem` listens to this bus. It will instantly tear down the subordinate's current behavior and spin up the new one, seamlessly preempting the high-level mission plan.

### 2. Component-Based Reporting (Bottom-Up Sensing)

Your idea of subordinates publishing reports as an ECS component is exactly the right Data-Oriented Design (DOD) approach. It avoids flooding the Event Bus with thousands of message objects per frame.

- **Subordinate Logic:** The subordinate's BTree periodically evaluates its environment and writes to an unmanaged component, for example, `SquadMemberReport` (containing fields like `AmmoCount`, `UnderFire`, `SpottedEnemyId`).
- **Leader Logic:** Because ECS components are stored in contiguous memory arrays, the Leader can iterate over all its subordinates and read their state essentially for free.

C#

```
// Inside the Leader's BTree Action:
ref readonly var report = ref ctx.World.GetComponentRO<SquadMemberReport>(subordinateEntity);
if (report.UnderFire) {
    // Leader dynamically calculates a new behavior for the squad...
}
```

### The Pragmatic Catch: Node Colocation

The only constraint to keep in mind with this highly direct, component-driven approach is **colocation**.

For the Leader to directly publish an `AssignBehaviorHashEvent` to a subordinate, or cleanly read its `SquadMemberReport` component without latency, **both the Leader's Brain and the Subordinate's Brain must be simulated on the same CGF (Simulation) node.**

In practice, this is exactly how military simulations usually handle it: an entire Platoon or Squad is clustered and simulated on the same physical node to allow for this ultra-fast, zero-latency shared memory access. The higher-level Mission Control (DDS) is then reserved only for assigning macro-missions to the *Leader*, who then micromanages the subordinates using direct ECS events and components exactly as you described.





Your intuition is not just reasonable — it’s actually the *right abstraction layer* for this engine. The GUIDE describes a **single-entity cognitive architecture**, but what you’re designing is effectively a **distributed cognitive system** (group AI), and that requires stepping *above* the behavior model without breaking it.

Let’s ground this cleanly in the FDP architecture and then propose a concrete pattern.

------

# 1. First Principles (what the engine really gives you)

From the GUIDE:

- Behaviors are **per-entity black boxes** driven by:
  - `BrainBlackboard` (local, small, per-entity)
  - ECS components (global/shared data)
  - Event bus (intra-node)
- The brain:
  - reads ECS state
  - writes actuator intents
- It **never directly controls other entities**

👉 That last point is critical:
There is **no native “control other entity” primitive inside a behavior**.

So group AI must be built using:

- ECS state (shared data)
- Events (control signals)
- Behavior reassignment (hard control)

------

# 2. Your proposed model (Commander + subordinates)

You proposed:

- Leader entity with coordination behavior
- Subordinates executing orders
- Shared state
- Messaging (possibly cross-node)

This maps almost perfectly to a **3-layer control model**:

```
[ Commander Behavior ]   <-- decision maker
          ↓
[ Coordination Layer ]   <-- ECS + events (your design space)
          ↓
[ Subordinate Behaviors ] <-- execution
```

------

# 3. Two fundamentally different control styles

You’re implicitly mixing two patterns — it’s important to separate them:

## A. HARD CONTROL (behavior forcing)

> “force subordinates to execute a concrete behavior”

Mechanism:

- `AssignBehaviorEvent` / `AssignBehaviorHashEvent`
- processed by `BehaviorIngressSystem`

Effect:

- immediate brain replacement
- full control

✔ Pros:

- deterministic
- simple mental model

✖ Cons:

- expensive (brain reset)
- destroys local continuity
- poor for high-frequency steering

------

## B. SOFT CONTROL (data-driven steering)

> “shared state / reports / coordination messages”

Mechanism:

- ECS components
- subordinates read them inside their own behavior

✔ Pros:

- continuous control
- scalable
- no churn in behavior system

✖ Cons:

- requires discipline in design

------

👉 **Key insight:**
You want **soft control as the default**, and **hard control only for phase changes**.

------

# 4. Recommended architecture (clean and scalable)

## 4.1 Core components

### 1. Group membership

```csharp
public struct GroupMember
{
    public Entity Leader;
    public int Slot; // optional role index
}
```

------

### 2. Shared group state (on leader)

```csharp
public struct GroupState
{
    public byte Order;        // e.g. Attack, Defend, Retreat
    public Entity Target;
    public float FormationSpacing;
}
```

------

### 3. Member report (on each subordinate)

```csharp
public struct GroupReport
{
    public bool UnderFire;
    public int Ammo;
    public float Health;
}
```

------

## 4.2 How subordinates behave

Inside a `[SharedAiAction]`:

```csharp
[SharedAiAction(typeof(UnitBlackboard), nameof(UnitBlackboard.Params))]
public static NodeStatus Act_FollowGroupOrder(
    ref UnitParams p, Entity self, EntityRepository repo)
{
    if (!repo.HasComponent<GroupMember>(self))
        return NodeStatus.Failure;

    var leader = repo.GetComponentRO<GroupMember>(self).Leader;

    if (!repo.HasComponent<GroupState>(leader))
        return NodeStatus.Failure;

    ref readonly var group = ref repo.GetComponentRO<GroupState>(leader);

    switch (group.Order)
    {
        case GroupOrder.Attack:
            // write WeaponChannel
            break;

        case GroupOrder.Move:
            // write LocomotionChannel
            break;
    }

    return NodeStatus.Running;
}
```

👉 This is **zero-allocation, fully compliant, and fast**.

------

## 4.3 How the leader behaves

Leader behavior:

- reads all `GroupReport` components
- computes decision
- writes `GroupState`

```csharp
foreach (var member in groupMembers)
{
    ref readonly var report = ref repo.GetComponentRO<GroupReport>(member);

    if (report.UnderFire)
        dangerCount++;
}

groupState.Order = dangerCount > 2
    ? GroupOrder.Retreat
    : GroupOrder.Attack;
```

👉 This is your **centralized coordination brain**.

------

# 5. When to use direct behavior forcing

Use **hard control** only for:

### Phase transitions

Examples:

- “switch to retreat behavior”
- “enter formation behavior”
- “start patrol route”

From leader:

```csharp
repo.Bus.Publish(new AssignBehaviorEvent
{
    Entity = subordinate,
    BehaviorName = "Retreat_BT",
    JsonParams = "{ ... }"
});
```

------

### Why not for steering?

Because:

- it resets blackboard
- breaks running actions
- expensive at scale

------

# 6. Messaging vs components (your key question)

You asked about:

> “orders / reports / messages / network”

Here is the clean separation:

------

## Within same node → ECS components (BEST)

Use:

- `GroupState`
- `GroupReport`

Reasons:

- zero latency
- no allocation
- cache-friendly

------

## Cross-node → DDS messages

Only if entities are not colocated.

Pattern:

1. Define DDS topic
2. Translate → ECS event
3. Apply to component

------

### Important constraint

> If leader and subordinates are on different nodes,
> **you lose shared memory semantics**

So you must:

- replicate state OR
- send messages

------

👉 **Pragmatic recommendation:**

> Always colocate a group on one node.

This matches:

- performance model
- ECS assumptions
- your coordination needs

------

# 7. The missing abstraction (what you’re really designing)

You are effectively introducing:

> **“Group Cognitive Layer”**

This is *not* a behavior — it is:

- an ECS-level coordination system
- driven by one entity (leader)
- consumed by many entities (members)

------

# 8. How this fits the GUIDE philosophy

The GUIDE enforces:

- per-entity cognition
- shared data via ECS
- no cross-entity direct control

Your approach respects all of that:

✔ Leader writes ECS state
✔ Members read ECS state
✔ Behaviors remain isolated
✔ No violations of CQRS

------

# 9. What NOT to do (important pitfalls)

### ❌ Don’t use blackboard for group state

- only 128 bytes total
- per-entity only
- not shareable

------

### ❌ Don’t spam behavior reassignment

- breaks continuity
- heavy

------

### ❌ Don’t build message-heavy coordination locally

- ECS already solves it better

------

# 10. If you want to push this further

There are two advanced directions you could explore:

------

## A. Hierarchical groups

Leader of leaders:

```
Company Commander
    ↓
Platoon Leaders
    ↓
Units
```

Each level:

- writes its own `GroupState`
- consumes higher-level state

------

## B. Role-based behavior

Instead of uniform members:

```csharp
public enum Role
{
    Leader,
    Assault,
    Support,
    Scout
}
```

Subordinates interpret orders differently.

------

# Final verdict

Your idea:

> “Leader directly steering subordinates via behaviors + shared state + reports”

is correct — but the optimal formulation is:

### ✅ Use:

- **ECS components for continuous coordination**
- **Leader behavior as decision engine**
- **Subordinate behaviors as interpreters**

### ⚠️ Use behavior reassignment only for:

- discrete behavior phase changes

### 🚫 Avoid:

- message-heavy local communication
- blackboard sharing
- frequent behavior resets

------
