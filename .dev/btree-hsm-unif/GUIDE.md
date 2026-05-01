# Cognitive Tier Architecture: A Guide to AI Behavior Development in FDP

---

## Table of Contents

1. [The Cognitive Tier & Doctrine Paradigms](#1-the-cognitive-tier--doctrine-paradigms)
2. [Quick-Start: Your First AI Behavior](#2-quick-start-your-first-ai-behavior)
3. [The BrainBlackboard: The Universal Cognitive Bus](#3-the-brainblackboard-the-universal-cognitive-bus)
4. [Doctrine Parameters & Memory Projection](#4-doctrine-parameters--memory-projection)
5. [Unified AI Building Blocks: Shared Conditions and Actions](#5-unified-ai-building-blocks-shared-conditions-and-actions)
6. [Actuator Preemption and Channel Safety](#6-actuator-preemption-and-channel-safety)
7. [Decoupled Cognitive Interrupts](#7-decoupled-cognitive-interrupts)
8. [Mission Routing and Terminal States](#8-mission-routing-and-terminal-states)
9. [End-to-End Walkthrough: Writing a New Doctrine](#9-end-to-end-walkthrough-writing-a-new-doctrine)

---

## 1. The Cognitive Tier & Doctrine Paradigms

### Architectural Overview

The engine enforces a rigid **CQRS boundary** between two tiers:

- **Cognitive tier (Brain)** — queries ECS state, runs decision logic, and writes _intents_
  to actuator channels such as `LocomotionChannel` and `WeaponChannel`. It never touches
  physics transforms or simulation state directly.

- **Muscle tier (Executors)** — reads the active command from the channel, performs the
  physical action, and writes the resulting `NodeStatus` (`Success`, `Failure`, or `Running`)
  back to the same channel. The `LocomotionDispatcherSystem` and `WeaponDispatcherSystem`
  are muscle-tier systems.

From the perspective of the higher-level `MissionDirectorSystem`, the tactical brain of an
entity is a perfectly interchangeable black box called a **Doctrine**. The mission layer
assigns a doctrine and simply waits for a `DoctrineFinishedEvent`. It never knows, or cares,
whether the brain under the hood is a behavior tree or a state machine.

### Available Paradigms

There are three paradigms for authoring a doctrine, ranked by performance budget:

#### Tier 2 — FastBTree

A **polling-based behavior tree** interpreter. Every frame, the `BTreeTickSystem` traverses
the compiled `BehaviorTreeBlob` from the root, evaluating Selectors, Sequences, and leaf
Action/Condition nodes. State is persisted across frames in the `BrainBTreeState` component
(which tracks the currently running node index).

Choose FastBTree when:
- The behavior is complex and sequential (ambushes, route following, multi-phase combat).
- Designers need familiar selector/sequence/decorator composition.
- You need `Observer` nodes that reactively abort branches.

```csharp
// Brain tier constant used in DoctrineDefinition and DoctrineState
const byte BrainTierBTree = BehaviorConstants.BrainTierBTree; // == 2
```

#### Tier 1 — FastHSM

An **event-driven hierarchical state machine** that relies entirely on unmanaged C# function
pointers and packed memory structs (`BrainHsm64` and `BrainHsm128`). There is no heap
allocation during state machine execution. Transitions fire in response to explicit events
pushed into the machine's unmanaged event queue; the machine does not poll every frame.

Choose FastHSM when:
- The behavior is **reactive** rather than sequential (convoy escorts, vehicle patrol loops).
- You need zero-allocation guaranteed hot-path performance.
- The state topology is fixed and the number of distinct states is small.

Two instance sizes are available:
- `BrainHsm64` — for machines with few states (wraps `HsmInstance64`).
- `BrainHsm128` — for larger machines with history states or parallel regions.

```csharp
const byte BrainTierHsm = BehaviorConstants.BrainTierHsm; // == 1
```

#### Tier 0 — Hardcoded Scripts

For massive numbers of simple entities where even a compiled graph is unnecessary. A Tier 0
domain is simply a plain `IEcsModuleSystem` that queries for entities with
`SimTier.Value == 1` and writes commands directly to the channel.

The canonical example is `TrafficBrainSystem`, which drives civilian pedestrians and cars:

```csharp
[UpdateInPhase(SystemPhase.Simulation)]
public class TrafficBrainSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        var repo = (EntityRepository)view;
        var q = repo.Query()
            .With<SimTier>()
            .With<LocomotionChannel>()
            .With<ActorCapabilityState>()
            .Build();

        foreach (var entity in q)
        {
            var tier = view.GetComponentRO<SimTier>(entity);
            if (tier.Value != 1) continue;  // only Tier-1 civilians

            var caps = view.GetComponentRO<ActorCapabilityState>(entity);
            if (!caps.Capabilities.HasFlag(ActorCapabilities.CanMove)) continue;

            ref var channel = ref repo.GetComponentRW<LocomotionChannel>(entity);

            bool hasThreat = view.HasComponent<TargetMemory>(entity)
                && view.GetComponentRO<TargetMemory>(entity).Count > 0;

            channel.ActiveAction = hasThreat
                ? NavigationConstants.ActionIdFlee
                : NavigationConstants.ActionIdMoveTo;

            // Keep the channel alive -- ChannelArbitrationSystem guards on InstanceId.
            if (view.HasComponent<DoctrineState>(entity))
            {
                var doctrine = view.GetComponentRO<DoctrineState>(entity);
                channel.DoctrineInstanceId = doctrine.InstanceId;
            }
        }
    }
}
```

Tier 0 entities do not use `DoctrineState`, `BrainBlackboard`, or the doctrine registry.
They are controlled purely by the hardcoded system. `DoctrineFinishedEvent` is never
published for them.

---

## 2. Quick-Start: Your First AI Behavior

The architecture is designed so that behavior authors work entirely in the domain of typed
structs and fluent builder DSLs. The Roslyn source generators produce all unmanaged
projection thunks, preemption wrappers, and builder extension methods at compile time, with
no runtime overhead and no magic strings in your code.

This chapter shows two complete, production-style examples before Sections 3-9 explain the
mechanisms in depth. If you want to understand why the architecture works the way it does,
read those sections afterward. If you want to ship a behavior today, this chapter is
sufficient.

---

### Example A: A Combat Doctrine (FastBTree)

A complete BTree doctrine that checks ammo, fires a weapon, and falls back to holding
position when ammunition is exhausted. This example covers the four steps every BTree
doctrine requires.

#### A1. Define the DTO and Blackboard Wrapper

```csharp
/// <summary>Parameters for the Combat doctrine. Written at doctrine assignment from JSON.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CombatParams
{
    public int   AmmoCount;
    public float EngageRange;
}

/// <summary>
/// Blackboard wrapper. BTreeBuilder and the source generators use this type
/// to locate CombatParams inside BrainBlackboard.Memory via Marshal.OffsetOf
/// at build time. No sizes or offsets appear anywhere else in your code.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct CombatBlackboard
{
    public CombatParams Params;
}
```

#### A2. Write Shared Logic

Each method is a plain, pure `static` function. The `[SharedAiCondition]` and
`[SharedAiAction]` attributes instruct the Roslyn source generator to compute the memory
offset of `CombatBlackboard.Params` and emit the projection adapters automatically.

```csharp
public static class CombatBehaviors
{
    /// <summary>True when the entity has ammunition remaining.</summary>
    [SharedAiCondition(typeof(CombatBlackboard), nameof(CombatBlackboard.Params))]
    public static bool Condition_HasAmmo(
        ref CombatParams p, Entity self, EntityRepository repo)
    {
        return p.AmmoCount > 0;
    }

    /// <summary>
    /// Commands the weapon channel to fire and decrements ammo.
    /// [WritesChannel(Weapon)] causes the compiler to generate:
    ///   BTree  -- a failure-guard wrapper that resets WeaponChannel when the branch aborts.
    ///   HSM    -- an OnEntry_AimAndFire() StateBuilder extension that pairs this action
    ///             with its exit-cleanup thunk automatically.
    /// </summary>
    [WritesChannel(ChannelKind.Weapon)]
    [SharedAiAction(typeof(CombatBlackboard), nameof(CombatBlackboard.Params))]
    public static NodeStatus Action_AimAndFire(
        ref CombatParams p, Entity self, EntityRepository repo)
    {
        if (p.AmmoCount <= 0)
            return NodeStatus.Failure;

        ref var weapon = ref repo.GetComponentRW<WeaponChannel>(self);
        weapon.ActiveAction = CombatConstants.ActionIdAimAndFire;
        p.AmmoCount--;
        return NodeStatus.Running;
    }

    /// <summary>Fallback: stop moving and hold current position.</summary>
    [WritesChannel(ChannelKind.Locomotion)]
    [SharedAiAction(typeof(CombatBlackboard), nameof(CombatBlackboard.Params))]
    public static NodeStatus Action_HoldPosition(
        ref CombatParams p, Entity self, EntityRepository repo)
    {
        ref var loco = ref repo.GetComponentRW<LocomotionChannel>(self);
        loco.ActiveAction = NavigationConstants.ActionIdHoldPosition;
        return NodeStatus.Running;
    }
}
```

#### A3. Build the BTree

`BTreeBuilder` accepts expression-bound lambdas. The lambda `bb => bb.Params` is evaluated
once at builder initialisation via `Marshal.OffsetOf`; at runtime the compiled blob holds
the resolved offset and incurs zero allocation per tick.

```csharp
[BTreeDefinition("Combat_BT")]
public static BehaviorTreeBlob BuildCombatTree()
{
    return new BTreeBuilder<CombatBlackboard, BTreeContext>()
        .Selector(root => root
            .Sequence(engage => engage
                .Condition(bb => bb.Params, CombatBehaviors.Condition_HasAmmo)
                .Action(bb => bb.Params, CombatBehaviors.Action_AimAndFire)
            )
            // Fallback: no ammo -- stand still.
            .Action(bb => bb.Params, CombatBehaviors.Action_HoldPosition)
        )
        .Compile("Combat_BT");
}
```

`Fbt.SourceGen` reads `[BTreeDefinition("Combat_BT")]` and generates
`FbtTreeCatalog.GetCombat_BT()`, making the compiled blob available as a static property
without any builder construction cost at runtime.

#### A4. Register and Assign

```csharp
// In AiDoctrineFactory.BuildRegistrationAction():
const int CombatDoctrineId = 4001;
var combatBlob = FbtTreeCatalog.GetCombat_BT();

registry.Register(CombatDoctrineId, "Combat_BT",
    new DoctrineDefinition
    {
        Name             = "Combat_BT",
        BrainTier        = BehaviorConstants.BrainTierBTree,
        ParseParams      = (json, ptr) => CombatBehaviors.ParseParams(json, ptr),
        ParamsDtoType    = typeof(CombatParams),
        BTreeInterpreter = new Interpreter<CombatBlackboard, BTreeContext>(
            combatBlob, actionRegistry),
    });

// From mission code:
world.Bus.PublishManaged(new AssignDoctrineEvent
{
    Entity       = soldierEntity,
    DoctrineName = "Combat_BT",
    JsonParams   = @"{ ""AmmoCount"": 30, ""EngageRange"": 80.0 }",
});
```

`DoctrineIngressSystem` deserialises the JSON onto a `stackalloc` shadow of `CombatParams`,
writes it to `BrainBlackboard.Memory`, increments `DoctrineState.InstanceId`, and sets
`BrainTier = BrainTierBTree`. `BTreeTickSystem` begins evaluating the compiled selector on
the entity every simulation frame. Section 4 covers `ParseParams` and the full ingress
flow in detail.

---

### Example B: A Patrol Doctrine (FastHSM)

A complete HSM doctrine for a vehicle that follows a route until it is physically disabled.
This example shows the HSM authoring experience, where `Fhsm.SourceGen` emits type-safe
`StateBuilder` extension methods so that string keys and byte offsets never appear in your
builder code.

#### B1. Define the DTO and Blackboard Wrapper

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct PatrolParams
{
    public float WaypointX;
    public float WaypointY;
    public int   RouteId;     // index into a shared route table; -1 = unset
}

[StructLayout(LayoutKind.Sequential)]
public struct PatrolBlackboard
{
    public PatrolParams Nav;
}
```

#### B2. Write the Action

```csharp
public static class PatrolBehaviors
{
    /// <summary>
    /// Commands the entity to follow a predefined route.
    /// [WritesChannel(Locomotion)] causes Fhsm.SourceGen to emit:
    ///   - an OnEntry_MoveAlongRoute() StateBuilder extension that pairs
    ///     this action with its locomotion exit-cleanup thunk internally, so
    ///     the HsmGraphValidator constraint is satisfied without any manual OnExit call.
    ///   - an Activity_MoveAlongRoute() extension for states that need continuous ticking.
    /// </summary>
    [WritesChannel(ChannelKind.Locomotion)]
    [SharedAiAction(typeof(PatrolBlackboard), nameof(PatrolBlackboard.Nav))]
    public static NodeStatus Action_MoveAlongRoute(
        ref PatrolParams p, Entity self, EntityRepository repo)
    {
        if (p.RouteId < 0)
            return NodeStatus.Failure;

        ref var loco = ref repo.GetComponentRW<LocomotionChannel>(self);
        loco.ActiveAction = NavigationConstants.ActionIdFollowRoute;
        // ... copy RouteId into loco.Params ...
        return NodeStatus.Running;
    }
}
```

#### B3. Build the HSM

The `OnEntry_MoveAlongRoute()` extension was generated from the `[SharedAiAction]` and
`[WritesChannel]` attributes. Calling it is all that is required -- the exit-cleanup thunk
is wired internally and `HsmGraphValidator` is satisfied at compile time.

```csharp
public static HsmDefinitionBlob BuildPatrolHsm()
{
    var builder = new HsmBuilder("Patrol_HSM");
    builder.Event("MobilityLost", eventId: BehaviorConstants.EventId_MobilityLost);

    builder.State("Patrolling")
        .Initial()
        .OnEntry_MoveAlongRoute()       // generated: wires action + locomotion exit-cleanup
        .On(BehaviorConstants.EventId_MobilityLost).GoTo("Disabled");

    builder.State("Disabled")
        .Final();                       // entering here publishes DoctrineFinishedEvent

    var graph = builder.Build();
    HsmNormalizer.Normalize(graph);
    var flat = HsmFlattener.Flatten(graph);
    return HsmEmitter.Emit(flat);
}
```

When the vehicle is immobilised, `CognitiveInterruptSystem` sets byte 126 of the entity's
`BrainBlackboard`. `HsmTickSystem` reads that byte before the next tick and injects
`EventId_MobilityLost`, driving the machine into `Disabled`. The `LocomotionChannel` is
cleared by the exit-cleanup thunk wired inside `OnEntry_MoveAlongRoute()`. Section 8
covers how `MissionDirectorSystem` reacts to the `DoctrineFinishedEvent` published on
`Final` state entry.

#### B4. Register

```csharp
// In AiDoctrineFactory.BuildRegistrationAction():
const int PatrolDoctrineId = 4002;
var patrolHsmBlob = BuildPatrolHsm();

registry.Register(PatrolDoctrineId, "Patrol_HSM",
    new DoctrineDefinition
    {
        Name          = "Patrol_HSM",
        BrainTier     = BehaviorConstants.BrainTierHsm,
        ParseParams   = (json, ptr) => PatrolBehaviors.ParseParams(json, ptr),
        ParamsDtoType = typeof(PatrolParams),
        HsmDefinition = patrolHsmBlob,
    });
```

---

### What the Compiler Does For You

In both examples you wrote zero byte offsets, zero magic strings, and zero unsafe pointer
casts. The compiler generated all of the following automatically:

| What you write | What the compiler generates behind the scenes |
|---|---|
| `[SharedAiCondition(typeof(CombatBlackboard), nameof(CombatBlackboard.Params))]` | BTree adapter closure using `Unsafe.AddByteOffset`; HSM `Guard_HasAmmo()` `TransitionBuilder` extension |
| `[SharedAiAction(...)]` + `[WritesChannel(ChannelKind.Weapon)]` | BTree failure-reset wrapper for `WeaponChannel`; HSM `OnEntry_AimAndFire()` extension that pairs entry action with exit-cleanup thunk |
| `.Condition(bb => bb.Params, CombatBehaviors.Condition_HasAmmo)` | `Marshal.OffsetOf` call at builder init; zero-allocation offset lookup at runtime |
| `.OnEntry_MoveAlongRoute()` | Internal `.OnEntry(actionKey)` + `.OnExit(cleanupKey)` pair; `HsmGraphValidator` constraint satisfied automatically |

The remaining sections explain each mechanism in depth. For most behaviors, you will not
need that depth -- the compiler takes care of it.

---

## 3. The BrainBlackboard: The Universal Cognitive Bus

### Memory Layout

`BrainBlackboard` is an ECS component holding a **128-byte inline unmanaged buffer**:

```csharp
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.BrainBlackboard)]
public unsafe struct BrainBlackboard
{
    public fixed byte Memory[BehaviorConstants.BrainBlackboardByteSize]; // 128 bytes
}
```

The buffer is shared universally — both `BTreeTickSystem` and `HsmTickSystem<T>` operate
on the same `BrainBlackboard` component of the entity they are ticking.

### Conventional Memory Regions

The 128 bytes are split into three logically distinct regions by convention (the engine does
not enforce these boundaries at runtime, so you must respect them):

```
Byte offset   Purpose
──────────────────────────────────────────────────────────
[0 .. ~60]    Doctrine parameters: written once at ingress by ParseParamsDelegate.
              Struct layout matches your DTO (e.g. MoveToLocationParams at offset 0).

[~61 .. 125]  Contextual "soft advice": written by external systems such as
              RouteContextSystem that inject sensory hints for the running doctrine.

[126]         Interrupt register: MobilityLost (1 = fired this frame, 0 = clear).
[127]         Interrupt register: reserved for future use.
──────────────────────────────────────────────────────────
```

### Why Not Use a Regular Managed Object?

The entire cognitive hot path must avoid heap allocations. At 60 Hz with hundreds of
tactical entities, even a small per-entity allocation would generate significant GC
pressure. The fixed-size inline buffer means reads and writes become pointer arithmetic
inlined by the JIT — no boxing, no allocation, no GC pauses.

### Accessing the Blackboard in a Node

**FastBTree** action/condition delegates receive a `ref TValue dto` that is already
projected to the correct byte offset inside the blackboard — you never touch `Memory[]`
directly in most cases.

**FastHSM** action thunks receive a `void* contextPtr` which holds a pointer to an
`HsmKernelBridge`. To read the blackboard from an HSM thunk:

```csharp
[HsmAction]
public static unsafe void MyAction(void* instance, void* ctx, HsmCommandWriter* writer)
{
    var bridge = (HsmKernelBridge*)ctx;
    var repo   = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;
    ref var bb = ref bridge->Self.Get<BrainBlackboard>(repo);

    // Now read or write bb.Memory[...] as needed.
}
```

In practice you will use `[SharedAiAction]` (see Section 4) to avoid this boilerplate
entirely and receive your DTO directly via a typed `ref` parameter.

---

## 4. Doctrine Parameters & Memory Projection

### The Ingress Flow

When the mission layer assigns a behavior, it publishes an `AssignDoctrineEvent`:

```csharp
world.Bus.PublishManaged(new AssignDoctrineEvent
{
    Entity       = myEntity,
    DoctrineName = "FireAtTarget",
    JsonParams   = @"{ ""TargetNetworkId"": 42, ""MaxRounds"": 5, ""CooldownSeconds"": 1.5 }",
});
```

`DoctrineIngressSystem` (running in `InputSystemGroup`) consumes this event and:

1. Looks up the `DoctrineDefinition` in `DoctrineRegistry`.
2. Uses a `stackalloc` shadow copy of the blackboard to attempt parsing — if the JSON
   is malformed, the entity remains on its **previous doctrine uninterrupted** (atomic
   transition guarantee).
3. On success, copies the shadow buffer to the live `BrainBlackboard` component and
   increments `DoctrineState.InstanceId` (the preemption token).

### Defining a Parameter DTO

Author your parameter struct as a plain unmanaged value type decorated with
`[StructLayout(LayoutKind.Sequential)]`:

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct FireAtTargetParams
{
    /// <summary>Packed ECS entity value of the target. 0 = no target resolved yet.</summary>
    public long  TargetPacked;
    /// <summary>Maximum number of fire activations. 0 = unlimited.</summary>
    public int   MaxRounds;
    /// <summary>Seconds between successive shots.</summary>
    public float CooldownSeconds;
    /// <summary>Runtime counter — written back to the blackboard by the action node.</summary>
    public int   RoundsFired;
}
```

Important rules:
- The struct must be `unmanaged` (no managed references).
- Use `[StructLayout(LayoutKind.Sequential)]` to guarantee deterministic field ordering.
- The struct is placed at **offset 0** of `BrainBlackboard.Memory`. Its total size must fit
  within the doctrine-parameters region (roughly bytes 0–60).

### Writing the ParseParamsDelegate

The delegate signature is:

```csharp
public unsafe delegate void ParseParamsDelegate(string json, byte* memory);
```

`memory` is a pointer to `BrainBlackboard.Memory[0]`. Write your DTO directly:

```csharp
public static unsafe void ParseFireAtTargetParams(
    string json, byte* ptr, NetworkEntityMap entityMap)
{
    if (string.IsNullOrWhiteSpace(json))
    {
        Unsafe.Write(ptr, default(FireAtTargetParams));
        return;
    }

    var dto = JsonSerializer.Deserialize<FireAtTargetParamsJsonDto>(json, JsonOptions);
    if (dto == null)
    {
        Unsafe.Write(ptr, default(FireAtTargetParams));
        return;
    }

    long targetPacked = 0;
    if (dto.TargetNetworkId != 0 && entityMap.TryGetEntity(dto.TargetNetworkId, out var entity))
        targetPacked = (long)entity.PackedValue;

    Unsafe.Write(ptr, new FireAtTargetParams
    {
        TargetPacked    = targetPacked,
        MaxRounds       = dto.MaxRounds,
        CooldownSeconds = dto.CooldownSeconds,
        RoundsFired     = 0,       // always reset to zero on assignment
    });
}
```

Note the use of a private JSON DTO (`FireAtTargetParamsJsonDto`) to avoid exposing
JSON attributes on the hot-path struct.

### Accessing Parameters at Runtime (BTree)

`Fbt.SourceGen` handles the projection automatically. You annotate your action method
with `[BTreeAction]` and declare the DTO as the first `ref` parameter:

```csharp
[BTreeAction]
public static NodeStatus Action_FireAtTarget(
    ref FireAtTargetParams p,      // <-- projected from blackboard offset 0
    ref BehaviorTreeState state,
    ref BTreeContext ctx)
{
    // p is a live reference into BrainBlackboard.Memory — no copy, no allocation.
    // Writing to p.RoundsFired writes back to the blackboard in-place.
    p.RoundsFired++;
    ...
}
```

`Fbt.SourceGen` computes `Marshal.OffsetOf<FireAtTargetBlackboard>("Params")` at
compile time (where `FireAtTargetBlackboard` is the wrapper struct whose sole field
is `FireAtTargetParams Params`) and emits:

```csharp
// Generated in FbtActionRegistrar.g.cs
actionRegistry.Register("Action_FireAtTarget",
    static (ref BrainBlackboard bb, ref BehaviorTreeState state, ref BTreeContext ctx, int _) =>
    {
        ref FireAtTargetParams dto = ref Unsafe.As<byte, FireAtTargetParams>(
            ref Unsafe.AddByteOffset(ref bb.Memory[0], (nint)0));
        return CgfNodes.Action_FireAtTarget(ref dto, ref state, ref ctx);
    });
```

### Accessing Parameters at Runtime (HSM)

In an HSM thunk you project the bytes manually, or use `[SharedAiAction]` (preferred — see
Section 4). Manual projection looks like this:

```csharp
[HsmAction]
public static unsafe void Action_BeginPatrol(void* instance, void* ctx, HsmCommandWriter* writer)
{
    var bridge = (HsmKernelBridge*)ctx;
    var repo   = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;
    ref var bb = ref bridge->Self.Get<BrainBlackboard>(repo);

    // Project bytes 0..N as PatrolParams.
    ref var p = ref Unsafe.As<byte, PatrolParams>(ref bb.Memory[0]);

    ref var channel = ref bridge->Self.Get<LocomotionChannel>(repo);
    channel.ActiveAction = NavigationConstants.ActionIdFollowRoute;
    // ... fill channel params from p ...
}
```

---

## 5. Unified AI Building Blocks: Shared Conditions and Actions

### The Problem Without Unification

Historically, writing a "check if target is alive" condition required two separate
implementations:

- A `NodeLogicDelegate<BrainBlackboard, BTreeContext>` with `[BTreeCondition]` for the tree.
- An unmanaged guard thunk `unsafe static bool Guard(void*, void*, ushort)` with `[HsmGuard]`
  for the state machine.

The same domain logic was maintained in two places, with diverging semantics.

### The Solution: `[SharedAiCondition]` and `[SharedAiAction]`

Both attributes live in `Fbt.Kernel` and are recognized by **both** `Fbt.SourceGen` and
`Fhsm.SourceGen`. Annotate a single `static` method once and both generators will emit
the appropriate adapter automatically.

### Method Signature Contract

**Shared condition** — must return `bool`:

```csharp
[SharedAiCondition(typeof(TParentDto), nameof(TParentDto.FieldName))]
public static bool Condition_SomeName(ref TField dto, Entity self, EntityRepository repo)
{
    // Pure logic. No ECS structural changes.
    return dto.SomeValue > 0;
}
```

**Shared action** — must return `NodeStatus`:

```csharp
[SharedAiAction(typeof(TParentDto), nameof(TParentDto.FieldName))]
public static NodeStatus Action_SomeName(ref TField dto, Entity self, EntityRepository repo)
{
    // Logic. Write to existing components on self. No structural changes.
    return NodeStatus.Running;
}
```

The method receives `ref TField dto`, where `TField` is the type of the field
`TParentDto.FieldName`. It is NOT the full parent DTO — just the field slice. This keeps
each method's dependency surface minimal and makes reuse across multiple parent DTOs
possible.

### Semantic Offset Resolution

You do not write byte offsets in the attribute. Instead, you name the **parent DTO** and
the **field within it**:

```csharp
[SharedAiAction(typeof(CombatParams), nameof(CombatParams.Weapon))]
public static NodeStatus Action_AimAndFire(ref WeaponParams p, Entity self, EntityRepository repo)
{ ... }
```

At compile time, `Fbt.SourceGen` and `Fhsm.SourceGen` use the Roslyn Semantic Model to
analyze `CombatParams`'s struct layout and compute `Marshal.OffsetOf<CombatParams>("Weapon")`
exactly. The offset is baked into the compound registration key `"Action_AimAndFire@16"`
(where 16 is the computed byte offset of the `Weapon` field within `CombatParams`).

This means:
- The same method can carry multiple `[SharedAiAction]` attributes to share it across
  different parent DTOs — one adapter is emitted per attribute.
- There are no magic-number byte offsets anywhere in your code.

For `[SharedAiCondition]`, the BTree registrar uses a `"Condition_MethodName@N"` key and
the HSM generator emits a `TransitionBuilder.Guard_MethodName()` extension. In both cases
these internal compound keys are hidden behind the type-safe builder APIs described below.
You never type a byte offset or a compound key string in your own code.

### BTree Adapter (Generated)

`Fbt.SourceGen` registers a zero-allocation closure under the compound key:

```csharp
// Generated output — FbtActionRegistrar.g.cs (illustrative)
actionRegistry.RegisterAction(
    "Action_AimAndFire@16",
    static (ref BrainBlackboard bb, ref BehaviorTreeState state, ref BTreeContext ctx, int _) =>
    {
        ref WeaponParams dto = ref Unsafe.As<byte, WeaponParams>(
            ref Unsafe.AddByteOffset(ref bb.Memory[0], (nint)16));
        return CgfNodes.Action_AimAndFire(ref dto, ctx.Self, ctx.World);
    });
```

To wire these into a BTree, use the expression-bound overloads on `BTreeBuilder`. The
builder evaluates the lambda once at initialization via `Marshal.OffsetOf` to locate the
field and construct the compound key internally — your code contains no strings, no offsets,
and gets full IDE refactoring support:

```csharp
var builder = new BTreeBuilder<CombatBlackboard, CombatContext>();
builder.Sequence(s => s
    .Condition(bb => bb.Target, CgfNodes.Condition_TargetAliveAndVisible)
    .Action(bb => bb.Weapon, CgfNodes.Action_AimAndFire)
);
```

The string-key overloads (`.Condition("Condition_TargetAliveAndVisible@0")`, etc.) still
exist in `BTreeBuilder` for advanced use and legacy compatibility but must not appear in
new code.

### HSM Adapter (Generated)

`Fhsm.SourceGen` emits an unmanaged thunk and registers it in `HsmActionRegistrar.g.cs`:

```csharp
// Generated output — HsmActionRegistrar.g.cs (illustrative)
HsmActionDispatcher.RegisterAction(
    ComputeHash("Action_AimAndFire@16"),
    (IntPtr)(delegate* <void*, void*, HsmCommandWriter*, void>)&Action_AimAndFire_At16);

private static unsafe void Action_AimAndFire_At16(
    void* instancePtr, void* contextPtr, HsmCommandWriter* writer)
{
    var bridge = (HsmKernelBridge*)contextPtr;
    var repo   = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;
    ref var bb = ref bridge->Self.Get<BrainBlackboard>(repo);
    ref WeaponParams dto = ref Unsafe.As<byte, WeaponParams>(
        ref Unsafe.AddByteOffset(ref bb.Memory[0], (nint)16));
    CgfNodes.Action_AimAndFire(ref dto, bridge->Self, repo);
    // NodeStatus return value is intentionally discarded -- HSM is event-driven.
}
```

The `NodeStatus` return is **discarded by the HSM adapter**. This is by design: HSMs
advance via event-driven transitions, not by reading a node's polling result.

### Generated Builder Extensions for HSM

Because FastHSM cannot use managed expression-trees (they would violate the unmanaged
constraint), `Fhsm.SourceGen` instead emits **type-safe extension methods** on `StateBuilder`
and `TransitionBuilder`. For each `[SharedAiAction]` and `[SharedAiCondition]` it processes,
it emits one extension per hook type. When the action carries `[WritesChannel]`, the
`OnEntry_X` extension also wires the exit-cleanup thunk automatically, so you cannot
accidentally leave a channel dirty:

```csharp
// Auto-generated in SharedAiHsmExtensions.g.cs (illustrative)
public static class SharedAiHsmExtensions
{
    // [WritesChannel] action: OnEntry extension wires both the action and the exit cleanup.
    // HsmGraphValidator is satisfied without any explicit OnExit call.
    public static StateBuilder OnEntry_AimAndFire(this StateBuilder builder)
    {
        builder.OnEntry("Action_AimAndFire@16");
        builder.OnExit("ExitCleanup_Action_AimAndFire_At16");
        return builder;
    }

    // [SharedAiCondition]: guard extension for conditional transitions.
    public static TransitionBuilder Guard_TargetAliveAndVisible(this TransitionBuilder builder)
    {
        return builder.Guard("Condition_TargetAliveAndVisible@0");
    }
}
```

To wire the shared action into an HSM state, call the generated extension:

```csharp
builder.State("Firing")
    .Initial()
    .OnEntry_AimAndFire()    // generated extension: wires action and exit cleanup
    .On(EventId_StopFiring).GoTo("Idle");
```

> **Note on `[HsmAction]` XML doc comments:** If you hover over `[HsmActionAttribute]` in
> your IDE, the XML doc comment in `Fhsm.Kernel` currently shows the stale signature
> `void MethodName(void* instance, void* context, ushort eventId)`. The actual dispatcher
> passes `HsmCommandWriter*` as the third argument — not `ushort eventId`. Trust this guide
> over the attribute's IntelliSense until `Fhsm.Kernel` is patched.

### Engine-Only Nodes: `[BTreeAction]` / `[BTreeCondition]` / `[HsmAction]` / `[HsmGuard]`

If a node is **only ever used in one paradigm**, use the paradigm-specific attributes
instead of the shared ones:

| Attribute | Paradigm | Signature |
|-----------|----------|-----------|
| `[BTreeAction]` | BTree only | `static NodeStatus Method(ref TDto dto, ref BehaviorTreeState state, ref BTreeContext ctx)` — preferred; raw 4-param form also accepted (see below). |
| `[BTreeCondition]` | BTree only | Same signatures as `[BTreeAction]`. |
| `[HsmAction]` | HSM only | `unsafe static void Method(void* instance, void* ctx, HsmCommandWriter* writer)` — the IDE may show a stale signature; see note in Section 5. |
| `[HsmGuard]` | HSM only | `unsafe static bool Method(void* instance, void* ctx, ushort eventId)` |

`[BTreeAction]` / `[BTreeCondition]` differ from `[SharedAiAction]` / `[SharedAiCondition]`
in the context parameter: they receive the full `BTreeContext` (which includes `_deltaTime`,
float/int param arrays, etc.) whereas the shared variants receive only `Entity` and
`EntityRepository`. Use the paradigm-specific versions when you genuinely need the richer
`BTreeContext` or when you are writing a fire-and-forget HSM thunk that requires the
`HsmCommandWriter`.

`[BTreeAction]` and `[BTreeCondition]` support two valid signatures. The typed 3-parameter
form is what you will write in new code; the raw 4-parameter form matches the underlying
`NodeLogicDelegate` and appears in legacy nodes or the raw engine internals:

```csharp
// Expression-bound (preferred): Fbt.SourceGen emits an adapter that projects the DTO.
static NodeStatus MyAction(ref TDto dto, ref BehaviorTreeState state, ref BTreeContext ctx)

// Raw/unbound: the native NodeLogicDelegate<BrainBlackboard, BTreeContext> signature.
static NodeStatus MyAction(ref BrainBlackboard bb, ref BehaviorTreeState state,
                            ref BTreeContext ctx, int paramIndex)
```

### Strict ECS Mutation Constraint

> **Rule: shared action and condition methods must never make structural ECS changes
> (adding or removing components).**

The generated adapter thunks bypass FastHSM's deferred `HsmCommandWriter` and write
directly to the `EntityRepository`. Performing `repo.AddComponent(...)` or
`repo.RemoveComponent(...)` while the cognitive system is iterating ECS chunks will
corrupt the chunk arrays.

**Safe inside a shared action:** reading component data, writing fields of existing
components (e.g., `LocomotionChannel.ActiveAction`), writing to `BrainBlackboard.Memory`.

**Forbidden inside a shared action:** `repo.AddComponent(...)`, `repo.RemoveComponent(...)`,
creating or destroying entities.

---

## 6. Actuator Preemption and Channel Safety

### The Zombie Action Problem

An entity transitions from a `Firing` state to an `Idle` state. The cognitive layer moves
on. But the `WeaponChannel.ActiveAction` still holds the `AimAndFire` command ID — the
muscle-tier `WeaponDispatcherSystem` continues executing it because no one cleared the
channel. This is the "zombie action" bug.

### The `[WritesChannel]` Attribute

Annotate any action that writes to an actuator channel:

```csharp
[WritesChannel(ChannelKind.Locomotion)]
[BTreeAction]
public static NodeStatus Action_WriteMoveToChannel(
    ref MoveToLocationParams p,
    ref BehaviorTreeState state,
    ref BTreeContext ctx)
{
    ref var channel = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self);
    channel.ActiveAction = NavigationConstants.ActionIdMoveTo;
    // ...
    return NodeStatus.Running;
}
```

```csharp
[WritesChannel(ChannelKind.Weapon)]
[SharedAiAction(typeof(CombatParams), nameof(CombatParams.Weapon))]
public static NodeStatus Action_AimAndFire(ref WeaponParams p, Entity self, EntityRepository repo)
{
    ref var channel = ref repo.GetComponentRW<WeaponChannel>(self);
    channel.ActiveAction = CombatConstants.ActionIdAimAndFire;
    // ...
    return NodeStatus.Running;
}
```

Multiple `[WritesChannel]` attributes are allowed on a single method if it writes to more
than one channel.

### BTree Preemption (Auto-Generated Wrapper)

`Fbt.SourceGen` detects `[WritesChannel]` and wraps the registered delegate. If the tree
returns `NodeStatus.Failure` (branch aborted), the wrapper automatically resets the channel:

```csharp
// Illustrative generated wrapper
actionRegistry.Register("Action_WriteMoveToChannel",
    static (ref BrainBlackboard bb, ref BehaviorTreeState state, ref BTreeContext ctx, int _) =>
    {
        var status = CgfNodes.Action_WriteMoveToChannel(ref dto, ref state, ref ctx);
        if (status == NodeStatus.Failure)
        {
            ref var loco = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self);
            loco.ActiveAction     = 0;
            loco.ActionInstanceId = unchecked((uint)(loco.ActionInstanceId + 1));
        }
        return status;
    });
```

Incrementing `ActionInstanceId` is the handshake signal that causes the muscle-tier
dispatcher to invoke its `OnExit` cleanup routine, severing the physical command.

### HSM Preemption (`ExitCleanup_` Thunks)

`Fhsm.SourceGen` reads the same `[WritesChannel]` attribute and auto-generates a paired
cleanup thunk named `ExitCleanup_{MethodName}`:

```csharp
// Illustrative generated thunk
private static unsafe void ExitCleanup_Action_AimAndFire(
    void* instancePtr, void* contextPtr, HsmCommandWriter* writer)
{
    var bridge = (HsmKernelBridge*)contextPtr;
    var repo   = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;
    ref var weapon = ref bridge->Self.Get<WeaponChannel>(repo);
    weapon.ActiveAction     = 0;
    weapon.ActionInstanceId = unchecked((uint)(weapon.ActionInstanceId + 1));
}
```

The generator also emits a `RequiredExitCleanups` dictionary mapping each channel-writing
action name to its cleanup key:

```csharp
// Emitted in HsmActionRegistrar.g.cs
public static readonly IReadOnlyDictionary<string, string> RequiredExitCleanups =
    new Dictionary<string, string>
    {
        ["Action_AimAndFire@16"]           = "ExitCleanup_Action_AimAndFire_At16",
        ["Action_WriteMoveToChannel@0"]    = "ExitCleanup_Action_WriteMoveToChannel_At0",
    };
```

### Build-Time Enforcement

`HsmGraphValidator` runs during `HsmCompiler.Compile()`. If a state registers a
channel-writing action as `OnEntry` or `Activity` but does **not** assign the corresponding
`ExitCleanup_` thunk as `OnExit`, the compiler throws a **hard build error** naming the
offending state and the missing key. You cannot ship a broken HSM.

**Correct wiring — using generated extensions (preferred):**

```csharp
// OnEntry_AimAndFire() wires both the action and the exit cleanup.
// HsmGraphValidator is satisfied automatically; no explicit OnExit call is needed.
builder.State("Firing")
    .OnEntry_AimAndFire()
    .On(EventId_TargetDead).GoTo("Idle");
```

**Correct wiring — using raw string keys (advanced / legacy):**

```csharp
builder.State("Firing")
    .OnEntry("Action_AimAndFire@16")
    .OnExit("ExitCleanup_Action_AimAndFire_At16")  // REQUIRED when bypassing the extension
    .On(EventId_TargetDead).GoTo("Idle");
```

**What happens if you use a raw key and omit the `OnExit`:** compilation aborts with:
> State 'Firing' uses channel-writing action 'Action_AimAndFire@16' but is missing required
> OnExit cleanup 'ExitCleanup_Action_AimAndFire_At16'.

---

## 7. Decoupled Cognitive Interrupts

### The Problem

Physical systems should not know anything about AI internals. The old
`HsmDamageBridgeSystem` contained explicit queries for `BrainHsm64` and `BrainHsm128` and
completely ignored BTree-driven entities. Any new capability-loss signal required editing
that system.

### The Solution: Blackboard Interrupt Registers

Bytes 126 and 127 of every `BrainBlackboard` are reserved as **single-frame, edge-triggered
interrupt registers**:

| Byte | Name | Written by | Read by |
|------|------|------------|---------|
| 126 | `InterruptRegister_MobilityLost` | `CognitiveInterruptSystem` | `HsmTickSystem<T>`, BTree Observer nodes |
| 127 | (reserved) | — | — |

The constant is defined in `CognitiveInterruptSystem`:
```csharp
internal const int InterruptRegister_MobilityLost = 126;
```

### Writing Interrupts: `CognitiveInterruptSystem`

This system runs before all tick systems in `CognitiveRuntimeModule`. It performs
**edge-triggered detection** by comparing `ActorCapabilityState` against a
`PreviousCapabilities` shadow component:

```csharp
// Fires exactly once: the tick when CanMove transitions from set to cleared.
if (wasAbleToMove && !canMoveNow)
    bb.Memory[InterruptRegister_MobilityLost] = 1;
```

By only firing on the _transition_, the interrupt does not permanently latch when an
entity remains immobilized for many frames.

### Consuming Interrupts: FastHSM

`HsmTickSystem<T>` reads byte 126 **before** calling `HsmKernel.Update()`. If set,
it injects `EventId_MobilityLost` into the state machine's event queue:

```csharp
if (bb.Memory[CognitiveInterruptSystem.InterruptRegister_MobilityLost] == 1)
    HsmEventQueue.TryEnqueue(ref component, new HsmEvent { EventId = BehaviorConstants.EventId_MobilityLost });
```

This allows HSM states to react with a normal transition:

```csharp
// OnEntry_MoveTo() also wires the exit cleanup automatically.
builder.State("Moving")
    .OnEntry_MoveTo()
    .On(BehaviorConstants.EventId_MobilityLost).GoTo("Immobilized");

builder.State("Immobilized")
    .Final();
```

The byte is **not** cleared by `HsmTickSystem`. Clearing is handled unconditionally by
`CognitiveCleanupSystem`.

### Consuming Interrupts: FastBTree

BTree `Observer` decorator nodes poll the blackboard byte natively. Configure an Observer
to watch byte 126 and abort the currently running branch when it reads `1`. The Observer
does not need to be aware that the signal originated from a physical damage event — it just
reads a byte.

### Single-Frame Pulse: `CognitiveCleanupSystem`

`CognitiveCleanupSystem` runs as the **last** system in `CognitiveRuntimeModule`, after all
tick systems. It unconditionally zeros registers 126 and 127 for every entity that owns a
`BrainBlackboard`:

```csharp
internal sealed class CognitiveCleanupSystem : IEcsModuleSystem
{
    public unsafe void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo) return;
        var q = repo.Query().With<BrainBlackboard>().Build();
        foreach (var entity in q)
        {
            ref var bb = ref repo.GetComponentRW<BrainBlackboard>(entity);
            bb.Memory[CognitiveInterruptSystem.InterruptRegister_MobilityLost] = 0;
            bb.Memory[127] = 0;
        }
    }
}
```

This single system covers _all_ brain tiers. There is no tier-specific cleanup code.

### System Execution Order in `CognitiveRuntimeModule`

```
ChannelArbitrationSystem       -- clears stale channels from previous doctrine
CognitiveInterruptSystem       -- writes interrupt bytes (edge-triggered)
BTreeTickSystem                -- polls byte 126 via Observer nodes, ticks tree
HsmTickSystem<BrainHsm128>     -- reads byte 126, injects event, ticks machine
HsmTickSystem<BrainHsm64>      -- same as above for smaller instances
CognitiveCleanupSystem         -- zeros bytes 126 & 127 (single-frame pulse guarantee)
```

---

## 8. Mission Routing and Terminal States

### The Contract

The `MissionDirectorSystem` never inspects the underlying brain tier. It strings doctrines
together through `MissionPlanQueue` phases and reacts to `DoctrineFinishedEvent`.

### Signaling Completion from a FastBTree Doctrine

When the root node of a BTree evaluates to `NodeStatus.Success` or `NodeStatus.Failure`,
`BTreeTickSystem` publishes a `DoctrineFinishedEvent` exactly once per doctrine assignment:

```csharp
// BTreeTickSystem -- simplified
var rootResult = def.BTreeInterpreter!.Tick(ref blackboard, ref btState.State, ref context);

if (rootResult == NodeStatus.Success || rootResult == NodeStatus.Failure)
{
    if (!_publishedTerminalForInstanceId.TryGetValue(entity.Index, out uint prev)
        || prev != doctrine.InstanceId)
    {
        repo.Bus.Publish(new DoctrineFinishedEvent { Entity = entity, Result = rootResult });
        _publishedTerminalForInstanceId[entity.Index] = doctrine.InstanceId;
    }
}
```

The deduplication ensures the event fires **once** regardless of how many frames the tree
stays in a terminal state before the mission director advances to the next phase.

For BTree doctrines to terminate, their root node must eventually return `Success` or
`Failure`. An indefinitely-running action (like `Action_Wander` which always returns
`Running`) means the doctrine never finishes — this is intentional for "run forever"
missions.

### Signaling Completion from a FastHSM Doctrine

Mark a state as terminal using the `.Final()` builder extension:

```csharp
var builder = new HsmBuilder("IdleBehavior");
builder
    .State("Idle")
        .Initial()
        .OnEntry("StubIdle")
        .On(EventId_MissionComplete).GoTo("Done");
    .State("Done")
        .Final();   // <-- stamps StateFlags.IsFinal; kernel sets InstanceFlags.Terminated on entry
```

When the kernel enters a `Final` state, it sets `InstanceFlags.Terminated` in the instance
header. `HsmTickSystem<T>` detects this and publishes `DoctrineFinishedEvent`:

```csharp
// HsmTickSystem<T> -- simplified
ref var hdr = ref Unsafe.As<T, InstanceHeader>(ref component);
if ((hdr.Flags & InstanceFlags.Terminated) != 0)
{
    if (!_publishedTerminalForInstanceId.TryGetValue(entity.Index, out uint prev)
        || prev != doctrine.InstanceId)
    {
        _publishedTerminalForInstanceId[entity.Index] = doctrine.InstanceId;
        repo.Bus.Publish(new DoctrineFinishedEvent { Entity = entity });
    }
    // Immediately clear -- prevents the "terminal latch" bug on rapid doctrine reassignment.
    hdr.Flags &= ~InstanceFlags.Terminated;
    hdr.Phase  = InstancePhase.Idle;
}
```

### Configuring Mission Phases

Set `MissionTrigger.DoctrineFinished` so the `MissionDirectorSystem` advances on the event:

```csharp
var queue = new MissionPlanQueue();
queue.PhaseCount = 3;

// Phase 0: move to waypoint (BTree doctrine -- finishes when arrival is confirmed)
queue.Phases[0] = new MissionPhase
{
    DoctrineId = DoctrineIds.MoveToLocation,
    Trigger    = MissionTrigger.DoctrineFinished,
};

// Phase 1: fire at the target (BTree -- finishes when target is dead or ammo out)
queue.Phases[1] = new MissionPhase
{
    DoctrineId = DoctrineIds.FireAtTarget,
    Trigger    = MissionTrigger.DoctrineFinished,
};

// Phase 2: idle forever (HSM -- no trigger needed, this is the final phase)
queue.Phases[2] = new MissionPhase
{
    DoctrineId = DoctrineIds.IdleHsm,
    Trigger    = MissionTrigger.TimerElapsed,
    TriggerParam = 99999f,
};
```

BTree and HSM doctrines are interchangeable within the same `MissionPlanQueue`. The mission
director advances phases identically regardless of which tier is running.

---

## 9. End-to-End Walkthrough: Writing a New Doctrine

This section walks through adding a complete `PatrolAndEngage` doctrine to the project.
The doctrine uses:
- A **FastBTree** tree that sequences patrol movement with combat.
- Shared conditions and actions usable from both BTree and (hypothetically) an HSM variant.
- Proper channel safety via `[WritesChannel]`.

### Step 1: Define the Parameter DTO

Add this to `CgfNodes.cs` alongside the other param structs:

```csharp
/// <summary>
/// Parameters for the PatrolAndEngage doctrine, placed at offset 0 of BrainBlackboard.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PatrolAndEngageParams
{
    public float WaypointX;
    public float WaypointY;
    public long  TargetPacked;    // runtime state, filled by a condition node
    public float EngageRange;
}

/// <summary>
/// Blackboard wrapper required by [SharedAiAction] / [SharedAiCondition] attributes
/// and by BTreeBuilder's expression-bound overloads.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PatrolAndEngageBlackboard
{
    public PatrolAndEngageParams Params;
}
```

### Step 2: Write the JSON Parse Delegate

```csharp
public static unsafe void ParsePatrolAndEngageParams(string json, byte* ptr)
{
    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
    var dto  = string.IsNullOrWhiteSpace(json)
        ? default
        : JsonSerializer.Deserialize<PatrolAndEngageParamsJsonDto>(json, opts);

    Unsafe.Write(ptr, new PatrolAndEngageParams
    {
        WaypointX   = dto?.WaypointX ?? 0f,
        WaypointY   = dto?.WaypointY ?? 0f,
        TargetPacked = 0L,          // filled at runtime by the condition
        EngageRange  = dto?.EngageRange ?? 50f,
    });
}

private class PatrolAndEngageParamsJsonDto
{
    public float WaypointX   { get; set; }
    public float WaypointY   { get; set; }
    public float EngageRange { get; set; }
}
```

### Step 3: Write Shared Conditions and Actions

These go in `CgfNodes.cs` (or a separate `CgfPatrolNodes.cs`).

Target the `Params` field of `PatrolAndEngageBlackboard` so each method receives the full
`ref PatrolAndEngageParams` — all fields accessible, no field slicing:

```csharp
/// <summary>
/// Condition: returns true if any threat is within the doctrine's EngageRange.
/// Works in both BTree and HSM via [SharedAiCondition].
/// </summary>
[SharedAiCondition(typeof(PatrolAndEngageBlackboard), nameof(PatrolAndEngageBlackboard.Params))]
public static bool Condition_ThreatInRange(
    ref PatrolAndEngageParams p, Entity self, EntityRepository repo)
{
    if (!repo.HasComponent<TargetMemory>(self))
        return false;
    ref readonly var mem = ref repo.GetComponentRO<TargetMemory>(self);
    return mem.Count > 0 && mem.NearestThreatDistance <= p.EngageRange;
}

/// <summary>
/// Action: writes a MoveTo locomotion command toward the patrol waypoint.
/// Works in both BTree and HSM via [SharedAiAction].
/// </summary>
[WritesChannel(ChannelKind.Locomotion)]
[SharedAiAction(typeof(PatrolAndEngageBlackboard), nameof(PatrolAndEngageBlackboard.Params))]
public static NodeStatus Action_MoveToWaypoint(
    ref PatrolAndEngageParams p, Entity self, EntityRepository repo)
{
    if (!repo.HasComponent<LocomotionChannel>(self))
        return NodeStatus.Failure;

    ref var channel = ref repo.GetComponentRW<LocomotionChannel>(self);
    channel.ActiveAction = NavigationConstants.ActionIdMoveTo;
    // ... write p.WaypointX, p.WaypointY into channel.Params ...
    return NodeStatus.Running;
}

/// <summary>
/// Action: writes a weapon engage command when a threat is in range.
/// </summary>
[WritesChannel(ChannelKind.Weapon)]
[SharedAiAction(typeof(PatrolAndEngageBlackboard), nameof(PatrolAndEngageBlackboard.Params))]
public static NodeStatus Action_EngageTarget(
    ref PatrolAndEngageParams p, Entity self, EntityRepository repo)
{
    if (!repo.HasComponent<WeaponChannel>(self))
        return NodeStatus.Failure;

    ref var channel = ref repo.GetComponentRW<WeaponChannel>(self);
    channel.ActiveAction = CombatConstants.ActionIdAimAndFire;
    return NodeStatus.Running;
}
```

### Step 4: Compile the BTree

Add a `[BTreeDefinition]` factory method. `Fbt.SourceGen` will generate
`FbtTreeCatalog.GetPatrolAndEngage()` for you:

```csharp
[BTreeDefinition("PatrolAndEngage")]
public static BehaviorTreeBlob BuildPatrolAndEngage()
{
    // Use expression-bound overloads: the lambda is evaluated once at init time via
    // Marshal.OffsetOf. Byte offsets never appear in your code.
    return new BTreeBuilder<PatrolAndEngageBlackboard, BTreeContext>()
        .Selector(root => root
            // Branch 1: engage if threat in range
            .Sequence(engage => engage
                .Condition(bb => bb.Params, CgfNodes.Condition_ThreatInRange)
                .Action(bb => bb.Params, CgfNodes.Action_EngageTarget)
            )
            // Branch 2: patrol toward waypoint
            .Action(bb => bb.Params, CgfNodes.Action_MoveToWaypoint)
        )
        .Compile("PatrolAndEngage");
}

### Step 5: Register the Doctrine

In `AiDoctrineFactory.BuildRegistrationAction()`:

```csharp
// Stable ID -- add to the constant block and to CgfDoctrineIds in Hrot.CGF.
private const int PatrolAndEngage_BT = 3013;

// In BuildRegistrationAction:
var patrolBlob = FbtTreeCatalog.GetPatrolAndEngage();

return (DoctrineRegistry registry) =>
{
    // ... existing registrations ...

    registry.Register(PatrolAndEngage_BT, "PatrolAndEngage",
        new DoctrineDefinition
        {
            Name             = "PatrolAndEngage",
            BrainTier        = BehaviorConstants.BrainTierBTree,
            ParseParams      = (json, ptr) => CgfNodes.ParsePatrolAndEngageParams(json, ptr),
            ParamsDtoType    = typeof(CgfNodes.PatrolAndEngageParams),
            BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                patrolBlob, actionRegistry),
        });
};
```

### Step 6: Assign the Doctrine from Mission Code

```csharp
world.Bus.PublishManaged(new AssignDoctrineEvent
{
    Entity       = infantryEntity,
    DoctrineName = "PatrolAndEngage",
    JsonParams   = @"{ ""WaypointX"": 400.0, ""WaypointY"": 200.0, ""EngageRange"": 75.0 }",
});
```

`DoctrineIngressSystem` will:
1. Deserialize JSON into `PatrolAndEngageParams` on a stack shadow.
2. Write the shadow to `BrainBlackboard.Memory[0..19]`.
3. Set `DoctrineState.BrainTier = BrainTierBTree` and increment `InstanceId`.

For **Tier 1 (HSM)** doctrines, the ingress system performs two additional steps that are
critical for correctness:
- **Unmanaged queue scrub:** it physically zeroes the `ActiveLeafIds` array in the HSM
  instance header (`BrainHsm64` or `BrainHsm128`), preventing stale event IDs from a
  previous doctrine activation from being re-processed by the kernel on the next tick.
- **`MachineId` synchronization:** it binds `InstanceHeader.MachineId` to the
  `StructureHash` of the newly assigned `HsmDefinitionBlob`. If the kernel's
  `ValidateInstance` firewall detects a mismatch between the instance's `MachineId` and
  the current blob's hash, it locks the entity out of all ticks until the mismatch is
  resolved. The ingress system resolves it at assignment time, ensuring the entity is
  immediately tickable.

You do not call these steps yourself — they are handled automatically by
`DoctrineIngressSystem`. Understanding them is useful for diagnosing entities that appear
frozen after a rapid doctrine reassignment.

### Step 7 (Optional): Write an HSM Variant

If you later want an HSM-based `PatrolAndEngage_HSM` for high-frequency entities:

```csharp
private const int PatrolAndEngage_HSM = 3014;

// In BuildRegistrationAction:
var patrolHsmBuilder = new HsmBuilder("PatrolAndEngage_HSM");
patrolHsmBuilder
    .Event("ThreatInRange",    eventId: 100)
    .Event("ThreatGone",       eventId: 101)
    .Event("MobilityLost",     eventId: BehaviorConstants.EventId_MobilityLost)

    .State("Patrolling")
        .Initial()
        // OnEntry_MoveToWaypoint() also wires the exit cleanup automatically.
        .OnEntry_MoveToWaypoint()
        .On(100).GoTo("Engaging")
        .On(BehaviorConstants.EventId_MobilityLost).GoTo("Done")

    .State("Engaging")
        // OnEntry_EngageTarget() also wires the exit cleanup automatically.
        .OnEntry_EngageTarget()
        .On(101).GoTo("Patrolling")
        .On(BehaviorConstants.EventId_MobilityLost).GoTo("Done")

    .State("Done")
        .Final();

var patrolGraph    = patrolHsmBuilder.Build();
HsmNormalizer.Normalize(patrolGraph);
var patrolFlat     = HsmFlattener.Flatten(patrolGraph);
HsmDefinitionBlob patrolHsmBlob = HsmEmitter.Emit(patrolFlat);

registry.Register(PatrolAndEngage_HSM, "PatrolAndEngage_HSM",
    new DoctrineDefinition
    {
        Name          = "PatrolAndEngage_HSM",
        BrainTier     = BehaviorConstants.BrainTierHsm,
        ParseParams   = (json, ptr) => CgfNodes.ParsePatrolAndEngageParams(json, ptr),
        ParamsDtoType = typeof(CgfNodes.PatrolAndEngageParams),
        HsmDefinition = patrolHsmBlob,
    });
```

Because `Action_MoveToWaypoint` is a `[SharedAiAction]`, the **exact same C# method** is
called from both the BTree closure and the HSM unmanaged thunk. No duplication.

---

## Quick Reference

### Doctrine Tier Summary

| Tier | Component | Tick system | Termination | Use case |
|------|-----------|-------------|-------------|----------|
| 2 — BTree | `BrainBTreeState` | `BTreeTickSystem` | Root returns `Success`/`Failure` | Complex sequential logic |
| 1 — HSM | `BrainHsm64` / `BrainHsm128` | `HsmTickSystem<T>` | Entry into `.Final()` state | Reactive, zero-alloc behaviors |
| 0 — Script | none | Custom `IEcsModuleSystem` | Never (no `DoctrineFinishedEvent`) | Massive simple populations |

### Attribute Cheat Sheet

| Attribute | Location | Purpose |
|-----------|----------|---------|
| `[BTreeDefinition("Name")]` | `Fbt.Kernel` | Tags a factory method; `Fbt.SourceGen` generates `FbtTreeCatalog.GetName()`. |
| `[BTreeAction]` | `Fbt.Kernel` | Registers a BTree-only action delegate. |
| `[BTreeCondition]` | `Fbt.Kernel` | Registers a BTree-only condition delegate. |
| `[HsmAction]` | `Fhsm.Kernel.Attributes` | Registers an HSM-only action thunk. |
| `[HsmGuard]` | `Fhsm.Kernel.Attributes` | Registers an HSM-only guard thunk. |
| `[SharedAiAction(dtoType, fieldName)]` | `Fbt.Kernel` | Registers an action usable from both BTree and HSM. |
| `[SharedAiCondition(dtoType, fieldName)]` | `Fbt.Kernel` | Registers a condition usable from both BTree and HSM. |
| `[WritesChannel(kind)]` | `Fbt.Kernel` | Triggers generation of preemption wrappers and exit-cleanup thunks. |

### BTree Node Signature Variants

```csharp
// Full blackboard access (raw -- prefer the typed variants below)
static NodeStatus MyAction(ref BrainBlackboard bb, ref BehaviorTreeState state,
                            ref BTreeContext ctx, int paramIndex)

// Typed DTO projection (most common -- [BTreeAction] / [BTreeCondition])
static NodeStatus MyAction(ref TDto dto, ref BehaviorTreeState state, ref BTreeContext ctx)

// Shared action/condition (no BTreeContext, only Entity + repo)
static NodeStatus MyAction(ref TField dto, Entity self, EntityRepository repo)
static bool       MyCondition(ref TField dto, Entity self, EntityRepository repo)
```

### HSM Builder Reference

```csharp
var builder = new HsmBuilder("MachineName");
builder
    .Event("EventName", eventId: 42)          // declare event and assign ushort ID
    .RegisterAction("ActionName")             // declare action used in this machine
    .RegisterGuard("GuardName")               // declare guard used in this machine

    .State("StateName")
        .Initial()                            // marks the starting state
        .History()                            // history pseudostate (remembers last child)
        .Final()                              // terminal state; sets InstanceFlags.Terminated
        .OnEntry("ActionName")                // called when state is entered
        .OnExit("ExitCleanup_ActionName")     // called when state is exited
        .Activity("ActionName")               // called every tick while in this state
        .Child("ChildState", child => { ... })// nested composite state

        .On("EventName").GoTo("TargetState")  // transition on event by name
        .On(42).GoTo("TargetState")           // transition on event by ID
            .Guard("GuardName")               // conditional transition
            .Action("TransitionActionName")   // action executed during transition
            .Priority(1);                     // higher priority evaluated first

var graph = builder.Build();
HsmNormalizer.Normalize(graph);
var flat  = HsmFlattener.Flatten(graph);
HsmDefinitionBlob blob = HsmEmitter.Emit(flat);
```

### Blackboard Memory Map

```
[0   .. ~60 ]  Doctrine params DTO (your struct at offset 0)
[~61 .. 125 ]  Contextual soft-advice (written by external systems)
[126        ]  InterruptRegister_MobilityLost  (1 = fired, cleared by CognitiveCleanupSystem)
[127        ]  Reserved
```


# Additional notes

## HOw HSm events can be customized

FastHSM enforces a strict, zero-allocation memory model for event processing. You do not allocate custom event classes or reference types on the heap; instead, every event in the system utilizes the universal `HsmEvent` unmanaged struct, which is rigidly packed to exactly 24 bytes. 

To create and customize new events, you work within these strict memory and architectural boundaries:

**1. Defining and Registering Custom Events**
To create a new event, you simply define a unique `ushort` identifier for your domain and register it into your state machine's topology using the `HsmBuilder` API. 

```csharp
const ushort EventId_MyCustomTrigger = 42;

// Register the event in your HSM topology
builder.Event("MyCustomTrigger", EventId_MyCustomTrigger);
```
During registration, you can also statically define compiler-level constraints for the event, such as its expected `payloadSize`, or flags like `isIndirect` and `isDeferred`. 

**2. Customizing Event Execution Behavior**
When you enqueue an event at runtime, you instantiate an `HsmEvent` and customize how the kernel evaluates it by assigning specific properties to its 8-byte header:
*   **Priority:** You can elevate an event by setting its `Priority` field to `EventPriority.Interrupt`, allowing it to bypass normal queue constraints and forcefully overwrite lower-priority events in memory-constrained Tier 1 queues.
*   **Flags (e.g., Deferred Events):** You can apply bitwise `EventFlags` to change the lifecycle of the event. For example, setting `EventFlags.IsDeferred` instructs the kernel to skip the event if the current state cannot handle it; the kernel will automatically strip the flag and re-queue the event so it can be re-evaluated after the machine transitions to a new state.

**3. Customizing Event Data (The 16-Byte Payload Boundary)**
The 8-byte header leaves exactly 16 bytes of inline buffer space (`fixed byte Payload`) for you to attach custom data. From a data-oriented design perspective, you populate this by casting your data directly into the raw memory block using `unsafe` pointers.

*   **Inline Payloads (≤ 16 Bytes):** If your custom data is a primitive type (like an `int` or `float`) or a small, unmanaged DTO struct, you project it straight into the buffer.
    ```csharp
    var evt = new HsmEvent { EventId = EventId_MyCustomTrigger };
    var myData = new MyCustomStruct { A = 1, B = 2 };
    
    // Zero-allocation pointer cast directly into the fixed buffer
    *(MyCustomStruct*)evt.Payload = myData;
    ```
    This guarantees blisteringly fast execution and cache efficiency without polluting the garbage collector.

*   **Indirect Payloads (> 16 Bytes):** If your custom data struct exceeds the strict 16-byte limit, attempting to inline it will result in memory corruption. The architecture mandates that you use indirection. You must store the bulky payload elsewhere (like a tightly packed ECS buffer or dictionary) and pass only the integer ID or lookup key inside the 16-byte payload. When doing this, you should inform the compiler by registering the event with the `isIndirect: true` flag.

By strictly enforcing the 24-byte footprint and requiring `unsafe` projection for custom data, the engine guarantees that no matter how many custom events you define, your AI execution remains perfectly deterministic and allocation-free.


## How HSM events are used


A core data-oriented principle: **events do not execute logic or access memory themselves.** 

In FastHSM, an event like `EventId_MyCustomTrigger` is strictly a 24-byte unmanaged data container (`HsmEvent`) pushed into a ring buffer. It is the state machine's **Guards** and **Actions** that react to this event, cross the unmanaged boundary, and manipulate the `BrainBlackboard`.

Here is a concrete, step-by-step example of how this memory pipeline operates, from triggering the event to safely projecting and mutating the blackboard memory.

### 1. Firing the Event (The 24-Byte Data Structure)
When an external system (like a sensor or a mission script) wants to trigger a behavior change, it constructs an `HsmEvent` and injects it into the entity's HSM queue. We can safely pack up to 16 bytes of custom primitive data directly into the event's inline buffer using unsafe pointer casting.

```csharp
public const ushort EventId_MyCustomTrigger = 42;

// The 16-byte payload we want to send
[StructLayout(LayoutKind.Sequential)]
public struct CustomTriggerPayload 
{
    public int TargetEntityId;
    public float ThreatLevel;
}

// Inside a System (e.g., PerceptionSystem):
public unsafe void TriggerCustomBehavior(Entity entity, EntityRepository repo)
{
    // Grab the Tier 2 HSM instance pointer from the ECS chunk
    ref var hsm128 = ref repo.GetComponentRW<BrainHsm128>(entity);
    HsmInstance128* instPtr = (HsmInstance128*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref hsm128);

    // Construct the 24-byte event
    var evt = new HsmEvent 
    { 
        EventId = EventId_MyCustomTrigger, 
        Priority = EventPriority.Normal 
    };

    // Zero-allocation pointer cast directly into the fixed payload buffer
    var payload = new CustomTriggerPayload { TargetEntityId = 99, ThreatLevel = 0.8f };
    *(CustomTriggerPayload*)evt.Payload = payload;

    HsmEventQueue.TryEnqueue(instPtr, evt);
}
```

### 2. Reacting to the Event (The Execution Boundary)
When the FastHSM kernel ticks, it dequeues the event and evaluates transitions. If a transition succeeds, the kernel invokes the state's registered actions via unmanaged C# function pointers. 

This is where the magic happens. The kernel passes a `void* context` pointer, which in our FDP pipeline is always a pointer to an `HsmKernelBridge`. We unpack this bridge to cross from the unmanaged simulation loop back into the managed ECS world.

### 3. Accessing the Blackboard (Memory Projection)
Inside your `[HsmAction]`, you unpack the repository, retrieve the `BrainBlackboard`, and project its raw 128-byte array into your specific AI domain DTO without allocating a single byte on the heap.

```csharp
[HsmAction(Name = "OnEntry_HandleCustomTrigger")]
public static unsafe void OnEntry_HandleCustomTrigger(void* instance, void* context, HsmCommandWriter* writer)
{
    // 1. Unpack the bridge to cross the unmanaged boundary
    var bridge = (HsmKernelBridge*)context;
    
    // 2. Recover the live ECS EntityRepository using the GCHandle
    var repo = (EntityRepository)System.Runtime.InteropServices.GCHandle.FromIntPtr(bridge->WorldHandle).Target!;
    
    // 3. Get the 128-byte BrainBlackboard for this specific entity
    ref var bb = ref repo.GetComponentRW<BrainBlackboard>(bridge->Self);

    // 4. ZERO-ALLOCATION MEMORY PROJECTION
    // We treat the blackboard's memory as a typed struct (e.g., CombatParams).
    // Using Unsafe.As avoids boxing and dynamic reflection overhead.
    ref var combatParams = ref System.Runtime.CompilerServices.Unsafe.As<byte, CombatParams>(ref bb.Memory);

    // 5. Execute the business logic!
    if (combatParams.AmmoCount > 0)
    {
        combatParams.EngageRange += 10.0f; // Mutate the blackboard safely
        
        // Write an intent to the physical actuator channel
        ref var weaponChannel = ref repo.GetComponentRW<WeaponChannel>(bridge->Self);
        weaponChannel.ActiveAction = CombatConstants.ActionIdAimAndFire;
        weaponChannel.ActionInstanceId++;
    }
}
```

### The "Clean Architecture" Shortcut
Writing the `GCHandle` unpacking and pointer projection boilerplate manually inside every state action introduces the risk of human error. As an AI behavior developer, you should almost never write the code above by hand. 

Instead, you define your logic purely in the domain space using `[SharedAiAction]`. Our Roslyn compiler (`Fhsm.SourceGen`) automatically writes the `HsmKernelBridge` unpacking logic, computes the exact `Marshal.OffsetOf` for your DTO, and emits the unmanaged `unsafe void` thunk for you. This provides you with the ultimate clean architecture: you work exclusively with strongly-typed `ref CombatParams` references, while the compiler enforces the strict, zero-allocation memory constraints under the hood.



When I showed you above the `unsafe static void OnEntry_HandleCustomTrigger(void* instance, void* context, HsmCommandWriter* writer)` method, I was showing you the raw, paradigm-specific execution layer. This is the exact function signature that the FastHSM kernel *requires* to function, and you *can* write it manually using the `[HsmAction]` attribute.

However, adhering to Clean Architecture, **the AI behavior developer should almost never write that.**

Even if the custom trigger is only going to be used by a state machine right now, the best practice is to author the event handler as a pure domain method using `[SharedAiAction]`.

Here is what the AI developer *actually* writes to handle the custom trigger:

```
public static class CustomBehaviors
{
    // 1. The AI engineer writes pure, safe C#. No pointers, no GCHandles.
    [SharedAiAction(typeof(CombatParams), nameof(CombatParams))]
    public static NodeStatus HandleCustomTrigger(ref CombatParams p, Entity self, EntityRepository repo)
    {
        if (p.AmmoCount > 0)
        {
            p.EngageRange += 10.0f;

            ref var weaponChannel = ref repo.GetComponentRW<WeaponChannel>(self);
            weaponChannel.ActiveAction = CombatConstants.ActionIdAimAndFire;
            weaponChannel.ActionInstanceId++;
        }
        return NodeStatus.Success;
    }
}
```

### How the Compiler Helps

The moment you annotate that clean method with `[SharedAiAction]`, our Roslyn source generator (`Fhsm.SourceGen`) takes over at compile time. The AI engineer is completely done, but the compiler does two massive things for them:

**1. It Generates the Scary Thunk** The compiler sees your pure domain logic and automatically writes the unmanaged `unsafe void` thunk for you in a hidden `HsmActionRegistrar.g.cs` file. It automatically calculates the exact byte offset of the struct, does the `GCHandle` unpacking, projects the memory, discards the `NodeStatus` return value, and invokes your pure method.

**2. It Generates the Fluent Builder Extension** To hide the underlying string keys from the AI designer, the source generator also emits a strongly-typed extension method for the HSM builder.

Because of this, when the AI engineer goes to wire up their custom trigger in the HSM topology, they just use the fluent, type-safe DSL:

```
var handleTriggerState = builder.State("HandleTrigger")
    // This extension method was generated by the compiler!
    // It safely maps to the unmanaged thunk behind the scenes.
    .OnEntry_HandleCustomTrigger();

idleState.On(EventId_MyCustomTrigger).GoTo(handleTriggerState);
```

### When would an AI Engineer use `[HsmAction]` directly?

You only fall back to writing the raw `[HsmAction]` with pointers if you are writing a highly specialized, paradigm-specific engine node.

For example, if you need an action that interacts directly with the `HsmCommandWriter*` (FastHSM's internal deferred command queue) rather than writing to the standard ECS actuator channels, you cannot use a `[SharedAiAction]` because the BTree interpreter has no concept of an `HsmCommandWriter`.

But for 99% of domain logic, you stick to the pure DTO references and let Roslyn write the unmanaged boilerplate!







If AI engineer defines "HandleCustomTrigger" SharedAiAction, how is this exact method name registered to the state machine? How does the compiler knows that HandleCustomTrigger should be used as OnEntry_HandleCustomTrigger? I am missing the link here...





This is a brilliant architectural question. You have spotted a fundamental truth about state machines: a piece of logic (an action) is completely agnostic to *when* it executes. The AI engineer defining `HandleCustomTrigger` doesn't know if it will be used when entering a state, exiting a state, or continuously as an activity.

Because the compiler cannot read the designer's mind, it solves this by generating **all possible lifecycle extensions** for every single `[SharedAiAction]`.

Here is the exact link between your pure domain method and the state machine topology.

### 1. The Underlying Builder API

Under the hood, the raw `StateBuilder` and `TransitionBuilder` classes only accept strings. For example:

```
public StateBuilder OnEntry(string actionName) { ... }
public StateBuilder Activity(string actionName) { ... }
public TransitionBuilder Action(string actionName) { ... }
```

We want to completely hide these string-based methods from the AI engineer so they never have to type the compiler-computed offset keys (like `"HandleCustomTrigger@16"`).

### 2. The Brute-Force Generation Strategy

When the Roslyn source generator (`Fhsm.SourceGen`) scans your assembly and finds this method:

```
[SharedAiAction(typeof(CombatParams), nameof(CombatParams))]
public static NodeStatus HandleCustomTrigger(...)
```

It computes the byte offset (e.g., `16`) and constructs the true compound key: `"HandleCustomTrigger@16"`.

Then, it generates a static extension class that emits **four distinct extension methods** for that single action, mapping it to every possible HSM hook:

```
// Auto-generated by Fhsm.SourceGen
public static class SharedAiHsmExtensions
{
    // 1. For use when ENTERING a state
    public static StateBuilder OnEntry_HandleCustomTrigger(this StateBuilder builder)
    {
        return builder.OnEntry("HandleCustomTrigger@16");
    }

    // 2. For use when EXITING a state
    public static StateBuilder OnExit_HandleCustomTrigger(this StateBuilder builder)
    {
        return builder.OnExit("HandleCustomTrigger@16");
    }

    // 3. For use CONTINUOUSLY while in a state
    public static StateBuilder Activity_HandleCustomTrigger(this StateBuilder builder)
    {
        return builder.Activity("HandleCustomTrigger@16");
    }

    // 4. For use during a TRANSITION between states
    public static TransitionBuilder Action_HandleCustomTrigger(this TransitionBuilder builder)
    {
        return builder.Action("HandleCustomTrigger@16");
    }
}
```

For conditions (e.g., `[SharedAiCondition] Condition_HasAmmo`), the generator only emits one extension, because conditions can only be used as transition guards:

```
    public static TransitionBuilder Guard_HasAmmo(this TransitionBuilder builder)
    {
        return builder.Guard("Condition_HasAmmo@8");
    }
```

### 3. The Developer Experience (IntelliSense)

Because the compiler generates all valid permutations, the AI engineer simply relies on their IDE. When they type: `builder.State("HandleTrigger").`

IntelliSense will immediately suggest:

- `.OnEntry_HandleCustomTrigger()`
- `.OnExit_HandleCustomTrigger()`
- `.Activity_HandleCustomTrigger()`

They select the one they want.

### The Architectural Link

When the HSM compiler's `HsmFlattener` runs, it crawls the graph, finds the string `"HandleCustomTrigger@16"`, and hashes it using the FNV-1a algorithm (`ComputeHash`). At runtime, the unmanaged kernel looks up that exact hash in the `HsmActionDispatcher.ActionTable`, resolving the zero-allocation function pointer perfectly.

By having the source generator emit every lifecycle permutation, the domain code remains pure, the builder DSL remains strongly typed, and the underlying memory offset arithmetic is completely encapsulated.

