# BS-1 Task Detail

**Workstream prefix:** `BS-1`  
**Design reference:** [`BS-1-DESIGN.md`](./BS-1-DESIGN.md)  
**Task tracker:** [`BS-1-TASK-TRACKER.md`](./BS-1-TASK-TRACKER.md)

---

## Phase 1: Event & Contract Foundations

---

### BS1-T001 — Define WeaponFire Pipeline ECS Event Structs

**Design Reference:** [BS-1-DESIGN.md §4.1](./BS-1-DESIGN.md#41-weaponfire-pipeline-contracts)

**Scope**

Add two new unmanaged ECS event structs representing the weapon-fire CQRS chain and the
corresponding simplified DDS message types:

- `WeaponFireIntent` (ECS event — Brain internal)
- `WeaponFireNotification` (ECS event — Muscle internal)
- `WeaponFireRequest` (DDS message — Brain → Muscle)
- `WeaponFire` (DDS message — Muscle → IG)

**NOT included:** translators, system changes, node reconfiguration.

**Files to create / modify**

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Combat/Events/WeaponFireEvents.cs` | Create — contains ECS event structs |
| `Hrot.NED/FireInteractionMessages.cs` | Extend — add DDS message structs |

**Constraints**

- All structs must be `unmanaged` (no heap allocations).
- `WeaponFireIntent` and `WeaponFireNotification` must carry enough data for `FireProcessingSystem`
  to spawn a bullet without world queries (shooter entity LUID, target entity LUID, weapon
  index).
- `WeaponFireRequest` and `WeaponFire` DDS types must be plain C# structs decorated with the
  existing `[DdsTopicName]` attribute and use `long` entity IDs, not ECS `Entity` handles.
- Topic names must match the design talk reference (`WeaponFireRequest`, `WeaponFire`).

**Struct reference (minimum fields — all are unmanaged):**

```csharp
// ECS event — published by AimAndFireExecutor on the Brain
public struct WeaponFireIntent
{
    public long ShooterEntityId;
    public long TargetEntityId;
    public int  WeaponIndex;
}

// ECS event — published by FireProcessingSystem on the Muscle
public struct WeaponFireNotification
{
    public long ShooterEntityId;
    public long TargetEntityId;
    public int  WeaponIndex;
}

// DDS — Brain → Muscle
[DdsTopicName("WeaponFireRequest")]
public struct WeaponFireRequest
{
    public long ShooterEntityId;
    public long TargetEntityId;
    public int  WeaponIndex;
}

// DDS — Muscle → IG
[DdsTopicName("WeaponFire")]
public struct WeaponFire
{
    public long ShooterEntityId;
    public long TargetEntityId;
    public int  WeaponIndex;
}
```

**Success Conditions**

1. *Compilation:* The solution compiles without errors after adding the new types. No existing
   project has a reference to the new types yet; they are inert additions.

2. *Struct layout — WeaponFireIntent:*  
   Setup: reflect `WeaponFireIntent`.  
   Assert: `typeof(WeaponFireIntent).IsValueType == true`;
   `Marshal.SizeOf<WeaponFireIntent>()` equals `sizeof(long) + sizeof(long) + sizeof(int)` = 20.

3. *Struct layout — WeaponFire:*  
   Same assertions for `WeaponFire`.

4. *DDS topic attribute:*  
   Assert: `typeof(WeaponFireRequest)` has exactly one `DdsTopicNameAttribute` with value
   `"WeaponFireRequest"`.

---

### BS1-T002 — Define Detonation & Damage Pipeline ECS Event Structs

**Design Reference:** [BS-1-DESIGN.md §4.2](./BS-1-DESIGN.md#42-detonation--damage-pipeline-contracts)

**Scope**

Add two new ECS event structs and two DDS message types for the detonation/damage CQRS chain:

- `DetonationNotification` (ECS event — Muscle internal)
- `DamageAssessedEvent` (ECS event — DamageAssessment module internal)
- `MunitionDetonation` (DDS — Muscle → all)
- `EntityHitDamage` (DDS — DamageAssessment → all)

**NOT included:** systems, translators, module changes.

**Files to create / modify**

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Combat/Events/DetonationEvents.cs` | Create |
| `Hrot.NED/FireInteractionMessages.cs` | Extend |

**Constraints**

- `DetonationNotification` must carry hit position (three `float` fields) and both entity IDs
  to allow the IG to place an explosion particle at the correct world position.
- `DamageAssessedEvent` carries the target entity LUID and the computed `float TotalDamage`.
- Both DDS types must use `long` entity IDs.
- Topic names: `MunitionDetonation`, `EntityHitDamage`.

**Struct reference:**

```csharp
public struct DetonationNotification
{
    public long  ShooterEntityId;
    public long  HitEntityId;
    public float HitX, HitY, HitZ;
}

public struct DamageAssessedEvent
{
    public long  HitEntityId;
    public float TotalDamage;
}

[DdsTopicName("MunitionDetonation")]
public struct MunitionDetonation
{
    public long  ShooterEntityId;
    public long  HitEntityId;
    public float HitX, HitY, HitZ;
}

[DdsTopicName("EntityHitDamage")]
public struct EntityHitDamage
{
    public long  HitEntityId;
    public float TotalDamage;
}
```

**Success Conditions**

1. *Compilation:* solution compiles with no errors or warnings in the modified assemblies.

2. *Struct layout:* `DetonationNotification` is unmanaged (`IsValueType == true`, no managed
   references); size = 2×8 + 3×4 = 28 bytes.

3. *DDS topic attribute:* `typeof(MunitionDetonation)` carries `DdsTopicNameAttribute("MunitionDetonation")`.

---

### BS1-T003 — Add HasAuthority Guard to DamageSystem

**Design Reference:** [BS-1-DESIGN.md §4.3](./BS-1-DESIGN.md#43-damagesystem-authority-guard)

**Scope**

Modify `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageSystem.cs` to check
`World.HasAuthority(evt.HitEntity)` before applying any damage.

**NOT included:** changes to how `HitEvent` is produced or consumed beyond this guard.

**Files to modify**

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageSystem.cs` | Add authority check |

**Constraints**

- Use the same `HasAuthority` API already used in other systems (e.g., `NavigationExecutionSystem`).
- If the local node does not own the hit entity, skip silently (do not log; high-frequency path).
- The `AllInOne` role always returns `true` from `HasAuthority`, so existing tests remain valid.

**Success Conditions**

1. *Non-owner skips damage:*  
   Setup: create a fake `ISimulationView` where `HasAuthority` returns `false` for entity A.  
   Action: publish `HitEvent { HitEntity = A, ... }`.  
   Assert: `Health.Current` on A is unchanged after `DamageSystem.OnUpdate`.

2. *Owner applies damage:*  
   Setup: `HasAuthority` returns `true` for entity B.  
   Action: publish `HitEvent { HitEntity = B, damage = 10f }`.  
   Assert: `Health.Current` on B is reduced by 10.

3. *AllInOne regression:* existing `DamageSystem` unit tests (in `Hrot.SimHost.Tests` or
   `FDP.Toolkit.Combat.Tests`) continue to pass unchanged — they use `AllInOne` where
   `HasAuthority` is always true.

---

## Phase 2: Weapon Fire CQRS Pipeline

---

### BS1-T004 — Refactor AimAndFireExecutor to Publish WeaponFireIntent

**Design Reference:** [BS-1-DESIGN.md §5.1](./BS-1-DESIGN.md#51-aimandfire-executor--weaponfireintent)

**Scope**

Modify `AimAndFireExecutor` to publish `WeaponFireIntent` (defined in BS1-T001) instead of
`FireRequestEvent`. Remove the `FireRequestEvent` struct if it has no remaining consumers after
this change.

**NOT included:** creation of translators, changes to `FireProcessingSystem` (covered in BS1-T007).

**Files to modify**

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Combat/Executors/AimAndFireExecutor.cs` | Replace `FireRequestEvent` with `WeaponFireIntent` |
| `FDP/Toolkits/FDP.Toolkit.Combat/Events/FireRequestEvent.cs` | Delete or mark `[Obsolete]` if unused |

**Constraints**

- `WeaponFireIntent` uses `long` (network entity ID) not ECS `Entity` handles, so use
  `EntityMap.GetNetId(entity)` to convert before publishing.
- Cooldown and ammo decrement logic remains in `AimAndFireExecutor` — do not move to
  `FireProcessingSystem`.
- `channel.Status` must stay `NodeStatus.Running` after publishing the intent (fire is not
  considered complete until the target is dead or ammo is exhausted).

**Success Conditions**

1. *Executor publishes WeaponFireIntent:*  
   Setup: world with entity A (Brain node, has `WeaponState { Ammo=5, CooldownTicksRemaining=0 }`
   and `SimTransform`), entity B as target.  
   Action: call `Execute` once.  
   Assert: `World.Bus.Consume<WeaponFireIntent>()` returns exactly one event with
   `ShooterEntityId == EntityMap.GetNetId(A)` and `TargetEntityId == EntityMap.GetNetId(B)`.

2. *No FireRequestEvent published:*  
   Same setup.  
   Assert: `World.Bus.Consume<FireRequestEvent>()` returns zero events (or FireRequestEvent no
   longer exists).

3. *Ammo/cooldown unchanged behaviour:*  
   Assert: after one call `weapon.Ammo == 4` and `weapon.CooldownTicksRemaining > 0`.

4. *No ammo → Failure, no intent published:*  
   Setup: `WeaponState { Ammo=0 }`.  
   Assert: `channel.Status == NodeStatus.Failure`; zero `WeaponFireIntent` events on bus.

---

### BS1-T005 — Create WeaponFireIntentEgressTranslator

**Design Reference:** [BS-1-DESIGN.md §5.2](./BS-1-DESIGN.md#52-brain-egress--weaponfireintentegress-translator)

**Scope**

Create a new translator that, on the Brain node, reads `WeaponFireIntent` from the local ECS
event bus and publishes a `WeaponFireRequest` DDS message.

**Files to create**

| File | Notes |
|---|---|
| `Hrot.SimHost/Network/Egress/WeaponFireIntentEgressTranslator.cs` | New class |

**Constraints**

- Extend the same base class pattern used by other event egress translators (e.g.,
  `FireInteractionEventTranslator`).
- Only publish if the local node is the shooter's authority (`view.HasAuthority(shooterEntity)`).
- Topic writer must be created once at construction, not per frame.

**Success Conditions**

1. *Intent → DDS message:*  
   Setup: create translator with a mock DDS writer; inject `WeaponFireIntent { ShooterEntityId=1,
   TargetEntityId=2, WeaponIndex=0 }` on the event bus.  
   Action: call `ScanAndPublish`.  
   Assert: the DDS writer's `Write` method was called once with `WeaponFireRequest { ShooterEntityId=1,
   TargetEntityId=2, WeaponIndex=0 }`.

2. *No authority, no publish:*  
   Setup: `view.HasAuthority(shooterEntity)` returns `false`.  
   Assert: DDS writer `Write` not called.

3. *Empty bus, no publish:*  
   Setup: no events on bus.  
   Assert: DDS writer's `Write` not called.

---

### BS1-T006 — Create WeaponFireRequestIngressTranslator

**Design Reference:** [BS-1-DESIGN.md §5.3](./BS-1-DESIGN.md#53-muscle-ingress--weaponfirerequest-ingress-translator)

**Scope**

Create a translator on the Muscle node that reads `WeaponFireRequest` DDS messages and
re-publishes them as local `WeaponFireIntent` ECS events on the Muscle's event bus.

**Files to create**

| File | Notes |
|---|---|
| `Hrot.SimHost/Network/Ingress/WeaponFireRequestIngressTranslator.cs` | New class |

**Constraints**

- Use the standard ingress pattern (`PollIngress`/`Decode`).
- Must not consume events from the local event bus — reads DDS only.
- Entity ID mapping: convert `long` entity IDs to local ECS `Entity` via `EntityMap`. If either
  entity is not found, skip silently.

**Success Conditions**

1. *DDS message → local event:*  
   Setup: mock DDS reader returns `WeaponFireRequest { ShooterEntityId=1, TargetEntityId=2 }`;
   `EntityMap` maps 1→entityA, 2→entityB.  
   Action: call `PollIngress`.  
   Assert: one `WeaponFireIntent { ShooterEntityId=1, TargetEntityId=2 }` on local event bus.

2. *Unknown entity → skip:*  
   Setup: `EntityMap` has no entry for ShooterEntityId=99.  
   Assert: zero events published; no exception thrown.

3. *Empty DDS reader → no-op:*  
   Assert: no events published; no exception thrown.

---

### BS1-T007 — Refactor FireProcessingSystem to Consume WeaponFireIntent and Emit WeaponFireNotification

**Design Reference:** [BS-1-DESIGN.md §5.4](./BS-1-DESIGN.md#54-fireprocessingsystem--emit-weaponfirenotification)

**Scope**

Modify `FireProcessingSystem` to:

1. Consume `WeaponFireIntent` instead of `FireRequestEvent`.
2. After spawning a bullet, publish a `WeaponFireNotification` event.

**Files to modify**

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Combat/Systems/FireProcessingSystem.cs` | Swap event types; add notification publish |

**Constraints**

- `WeaponFireIntent` carries entity IDs as `long`. Resolve to local ECS entities via `EntityMap`.
  Skip the event if either entity is not found.
- The bullet entity creation logic (adding `SimTransform`, `SimVelocity`, `BallisticProjectile`,
  `PhysicsCollider`) must remain unchanged.
- The `WeaponFireNotification` must be published **after** the bullet entity exists and before
  the frame ends.

**Success Conditions**

1. *WeaponFireIntent spawns bullet + fires notification:*  
   Setup: world on Muscle node; entity A has `WeaponState`, `SimTransform`; entity B exists.  
   Inject `WeaponFireIntent { ShooterEntityId=netId(A), TargetEntityId=netId(B) }`.  
   Action: run `FireProcessingSystem.OnUpdate()`.  
   Assert: one new entity with `BallisticProjectile` component exists;
   one `WeaponFireNotification` on event bus with `ShooterEntityId==netId(A)`.

2. *FireRequestEvent no longer consumed (no regression):*  
   Assert: publishing `FireRequestEvent` does not cause any system to crash or produce bullets
   (FireRequestEvent is inert / removed).

3. *Unknown entity → skip gracefully:*  
   Inject `WeaponFireIntent { ShooterEntityId=9999 }` (not in EntityMap).  
   Assert: no bullet spawned; no exception.

---

### BS1-T008 — Create WeaponFireNotificationEgressTranslator

**Design Reference:** [BS-1-DESIGN.md §5.5](./BS-1-DESIGN.md#55-muscle-egress--weaponfirenotificationegress-translator)

**Scope**

Create a translator on the Muscle node that reads `WeaponFireNotification` ECS events and
publishes `WeaponFire` DDS messages (for the IG to draw muzzle flashes).

**Files to create**

| File | Notes |
|---|---|
| `Hrot.SimHost/Network/Egress/WeaponFireNotificationEgressTranslator.cs` | New class |

**Constraints**

- Extends the same event-egress base class as BS1-T005.
- Does **not** require an authority check — the notification is only ever published by the
  authoritative Muscle node.

**Success Conditions**

1. *Notification → DDS:*  
   Inject `WeaponFireNotification { ShooterEntityId=1, TargetEntityId=2, WeaponIndex=0 }`.  
   Assert: DDS `Write` called with `WeaponFire { ShooterEntityId=1, TargetEntityId=2, WeaponIndex=0 }`.

2. *Multiple notifications → multiple DDS writes:*  
   Inject three notifications in one frame.  
   Assert: DDS `Write` called exactly 3 times.

---

### BS1-T009 — Create WeaponFireIngressTranslator for IG

**Design Reference:** [BS-1-DESIGN.md §5.6](./BS-1-DESIGN.md#56-ig-ingress--weaponfireingresstranslator)

**Scope**

Create an ingress translator for the Image Generator that receives `WeaponFire` DDS messages
and publishes a local `IgWeaponFireEvent` for the IG visual layer.

**Files to create/modify**

| File | Notes |
|---|---|
| `Hrot.IG/Translators/WeaponFireIngressTranslator.cs` | New class |
| `Hrot.IG/IgEvents.cs` | Add `IgWeaponFireEvent` struct (if not already present) |

**Constraints**

- `IgWeaponFireEvent` is an ECS event for the IG's local bus — it does not need to be unmanaged
  (IG-side rendering data can be managed).
- The translator must tolerate unknown entity IDs gracefully (entity may have been destroyed).

**Success Conditions**

1. *DDS message → IG event:*  
   Setup: mock DDS reader returns `WeaponFire { ShooterEntityId=1, TargetEntityId=2 }`;
   EntityMap maps both.  
   Action: `PollIngress`.  
   Assert: one `IgWeaponFireEvent` on local event bus.

2. *Unknown IG entity → still publish event:*  
   EntityMap does not know ShooterEntityId=5.  
   Assert: event still published (IG may still draw the tracer by position); no exception.

---

## Phase 3: Detonation & Damage Assessment Pipeline

---

### BS1-T010 — Refactor HitResolutionSystem to Emit DetonationNotification

**Design Reference:** [BS-1-DESIGN.md §6.1](./BS-1-DESIGN.md#61-hitresolutionsystem--emit-detonationnotification)

**Scope**

Modify `HitResolutionSystem` (or equivalent hit-resolution system in `FDP.Toolkit.Combat`) to
also publish a `DetonationNotification` for each bullet impact in addition to the existing
`HitEvent`.

**NOT included:** changes to `HitEvent` itself or `DamageSystem`.

**Files to modify**

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Combat/Systems/HitResolutionSystem.cs` | Publish DetonationNotification |

**Constraints**

- `HitEvent` must still be published (other systems depend on it).
- `DetonationNotification` uses world-space `HitX/Y/Z` coordinates (from the raycast hit point).
- Use entity IDs convertible via `EntityMap`.

**Success Conditions**

1. *HitEvent and DetonationNotification both published on impact:*  
   Setup: Muscle world; simulate a bullet collision.  
   Assert: one `HitEvent` AND one `DetonationNotification { HitEntityId=<target_net_id> }` on bus.

2. *LOS-check rays do not produce DetonationNotification:*  
   Assert: rays flagged as LOS checks (not bullets) produce no `DetonationNotification`.

3. *All existing HitResolution tests pass unchanged.*

---

### BS1-T011 — Create MunitionDetonationEgressTranslator

**Design Reference:** [BS-1-DESIGN.md §6.2](./BS-1-DESIGN.md#62-muscle-egress--munitiondetonationegress-translator)

**Scope**

Create a translator on the Muscle node that reads `DetonationNotification` events and publishes
`MunitionDetonation` DDS messages.

**Files to create**

| File | Notes |
|---|---|
| `Hrot.SimHost/Network/Egress/MunitionDetonationEgressTranslator.cs` | New class |

**Constraints**

- Same egress-translator base class pattern.
- Copies HitX/Y/Z directly from the ECS event to the DDS struct (no coordinate transform).

**Success Conditions**

1. *Event → DDS:*  
   Inject `DetonationNotification { HitEntityId=3, HitX=1, HitY=2, HitZ=3 }`.  
   Assert: DDS `MunitionDetonation { HitEntityId=3, HitX=1.0, HitY=2.0, HitZ=3.0 }` written.

2. *Multiple detonations in one frame → multiple DDS writes.*

---

### BS1-T012 — Create DamageAssessmentModule

**Design Reference:** [BS-1-DESIGN.md §6.3](./BS-1-DESIGN.md#63-damage-assessment-module)

**Scope**

Create `DamageAssessmentModule` in `FDP/Toolkits/FDP.Toolkit.Combat/` (or `Hrot.SimHost/Modules/`).
The module registers:

1. `MunitionDetonationIngressTranslator` — reads `MunitionDetonation` DDS and publishes local
   `DetonationNotification`.
2. `DamageCalculationSystem` — consumes `DetonationNotification`, computes flat damage
   (from `CombatConstants.DefaultBulletDamage`), publishes `DamageAssessedEvent`.

**NOT included:** the `DamageAssessedEgressTranslator` (BS1-T013) and health application
(BS1-T014).

**Files to create**

| File | Notes |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Combat/Modules/DamageAssessmentModule.cs` | Module class |
| `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageCalculationSystem.cs` | New system |
| `Hrot.SimHost/Network/Ingress/MunitionDetonationIngressTranslator.cs` | New translator |

**Constraints**

- `DamageCalculationSystem` must call `view.HasAuthority(targetEntity)` before publishing
  `DamageAssessedEvent` (only the authority node calculates final damage).
- POC: damage value = `CombatConstants.DefaultBulletDamage`; armor and penetration curves
  are deferred.
- `DamageCalculationSystem` runs in `SimulationSystemGroup` to ensure it accesses
  up-to-date transforms.

**Success Conditions**

1. *DDS → local event → DamageAssessedEvent:*  
   Setup: authority node; mock DDS reader returns `MunitionDetonation { HitEntityId=5 }`;
   entity 5 is known locally.  
   Action: `PollIngress` then `DamageCalculationSystem.OnUpdate`.  
   Assert: one `DamageAssessedEvent { HitEntityId=5, TotalDamage=CombatConstants.DefaultBulletDamage }`.

2. *Non-authority → no DamageAssessedEvent:*  
   Setup: `HasAuthority` returns false for entity 5.  
   Assert: zero `DamageAssessedEvent` published.

3. *DamageCalculationSystem does not mutate Health directly.*

---

### BS1-T013 — Create DamageAssessedEgressTranslator

**Design Reference:** [BS-1-DESIGN.md §6.3](./BS-1-DESIGN.md#63-damage-assessment-module)

**Scope**

Create a translator that reads `DamageAssessedEvent` from the local bus and publishes
`EntityHitDamage` DDS messages.

**Files to create**

| File | Notes |
|---|---|
| `Hrot.SimHost/Network/Egress/DamageAssessedEgressTranslator.cs` | New class |

**Constraints**

- Same egress-translator base class pattern.
- Entity ID: use the `HitEntityId` as-is (it is already a network ID).

**Success Conditions**

1. *Event → DDS:*  
   Inject `DamageAssessedEvent { HitEntityId=7, TotalDamage=25.5f }`.  
   Assert: `EntityHitDamage { HitEntityId=7, TotalDamage=25.5f }` DDS written.

2. *Zero events → no DDS write.*

---

### BS1-T014 — Create EntityHitDamageIngressTranslator and HealthApplicationSystem

**Design Reference:** [BS-1-DESIGN.md §6.4](./BS-1-DESIGN.md#64-health-application-pipeline)

**Scope**

On the authority node (Brain or Muscle depending on topology):

1. `EntityHitDamageIngressTranslator` reads `EntityHitDamage` DDS and publishes local
   `DamageAssessedEvent`.
2. `HealthApplicationSystem` consumes `DamageAssessedEvent`, checks `HasAuthority`, decrements
   `Health.Current`, updates `HealthData` mirror, and strips `ActorCapabilities` if entity
   reaches zero HP.

**NOT included:** entity destruction (deferred; just zero out HP and strip capabilities).

**Files to create**

| File | Notes |
|---|---|
| `Hrot.SimHost/Network/Ingress/EntityHitDamageIngressTranslator.cs` | New class |
| `FDP/Toolkits/FDP.Toolkit.Combat/Systems/HealthApplicationSystem.cs` | New system |

**Constraints**

- `HealthApplicationSystem` replaces the health-mutation logic previously in `DamageSystem`
  for the distributed path. `DamageSystem` retains the local path for `AllInOne`.
- Health floor: `Health.Current = Math.Max(0, Health.Current - damage)`.
- On reaching 0 HP: clear `ActorCapabilities.CanMove | CanShoot`; do NOT destroy the entity
  in this task (destruction is a separate concern).
- `HealthData` mirror component must be updated in the same tick.

**Success Conditions**

1. *EntityHitDamage → Health decremented:*  
   Setup: authority node; entity A has `Health { Current=100 }` and `HealthData`.  
   DDS reader returns `EntityHitDamage { HitEntityId=netId(A), TotalDamage=30 }`.  
   Action: `PollIngress` + `HealthApplicationSystem.OnUpdate`.  
   Assert: `Health.Current == 70f`; `HealthData` reflects the update.

2. *Health cannot go below zero:*  
   Setup: `Health.Current = 10`, `TotalDamage = 50`.  
   Assert: `Health.Current == 0`.

3. *Zero HP strips capabilities:*  
   Setup: entity has `ActorCapabilityState { Capabilities = CanMove | CanShoot }`.  
   Apply damage to reach 0 HP.  
   Assert: `CanMove` and `CanShoot` both cleared.

4. *Non-authority → no health change.*

---

### BS1-T015 — Create EntityDamageEgressTranslator

**Design Reference:** [BS-1-DESIGN.md §6.5](./BS-1-DESIGN.md#65-entitydamageegress-translator-simhost--ig)

**Scope**

Create `EntityDamageEgressTranslator` that tracks dirty `Health` components and publishes the
existing `EntityDamage` DDS message so the IG updates health bars.

Register the new translator in `SimHostApp.cs` egress list.

**Files to create / modify**

| File | Change |
|---|---|
| `Hrot.Map.Common/Replication/Egress/EntityDamageEgressTranslator.cs` | Create |
| `Hrot.SimHost/SimHostApp.cs` | Register translator in egress list |

**Constraints**

- Reuse the existing `EntityDamage` DDS message type (already in `Hrot.NED`).
- Track health changes using `SimulationView.GetDirtyEntities<Health>()` (or equivalent dirty
  tracking pattern used by other egress translators such as `EntityInfoEgressTranslator`).
- Only publish when `Health.Current` has actually changed (avoid flooding DDS on every tick).

**Success Conditions**

1. *Health change → EntityDamage published:*  
   Entity A: `Health.Current` changes from 100 to 70 (dirty).  
   Action: `ScanAndPublish`.  
   Assert: DDS `EntityDamage { EntityId=netId(A), Damage=... }` written once.

2. *No change → no publish:*  
   Entity A: `Health.Current` unchanged since last publish.  
   Action: `ScanAndPublish`.  
   Assert: DDS writer not called.

3. *Registered in SimHostApp:*  
   Assert: `EntityDamageEgressTranslator` is present in the `egressTranslators` list in
   `SimHostApp.cs` (verifiable by code inspection / CI compilation).

---

## Phase 4: Node Role Reconfiguration

---

### BS1-T016 — Update NodeBootstrapper Role Assignments

**Design Reference:** [BS-1-DESIGN.md §7.1](./BS-1-DESIGN.md#71-nodebootstrapper-changes)

**Scope**

Modify `Hrot.SimHost/NodeBootstrapper.cs`:

- Remove `CombatModule` from the `NodeRole.Brain` role.
- Add `DamageAssessmentModule` to the `NodeRole.MuscleGround` role.
- `AllInOne` retains all modules (including `CombatModule` and `DamageAssessmentModule`).

**NOT included:** translator registration (BS1-T017).

**Files to modify**

| File | Change |
|---|---|
| `Hrot.SimHost/NodeBootstrapper.cs` | Module assignment conditions |

**Constraints**

- The existing condition `if (role != NodeRole.ImageGenerator)` for `CombatModule` must be
  changed to `if (role != NodeRole.ImageGenerator && role != NodeRole.Brain)`.
- `AllInOne` must pass all guards — do not create a condition that accidentally excludes it.
- Do not change any other module assignments in this task.

**Success Conditions**

1. *Brain role modules:*  
   Setup: instantiate `NodeBootstrapper` with `NodeRole.Brain`.  
   Assert: returned module list contains `MissionControlModule`, `CognitiveRuntimeModule`,
   `ActionDispatchModule`; does **NOT** contain `CombatModule`.

2. *Muscle role modules:*  
   Assert: returned module list contains `GroundKinematicsModule`, `CombatModule`,
   `DamageAssessmentModule`; does **NOT** contain `MissionControlModule`.

3. *AllInOne role modules:*  
   Assert: returned module list contains `CombatModule`, `DamageAssessmentModule`,
   `MissionControlModule`, `CognitiveRuntimeModule`, `ActionDispatchModule`,
   `GroundKinematicsModule`.

---

### BS1-T017 — Register New Translators in SimHostApp

**Design Reference:** [BS-1-DESIGN.md §7.2](./BS-1-DESIGN.md#72-translator-registration-in-simhostapp)

**Scope**

Modify `Hrot.SimHost/SimHostApp.cs` to register all translators created in Phases 2–3
conditional on the current node role.

**Files to modify**

| File | Change |
|---|---|
| `Hrot.SimHost/SimHostApp.cs` | Add translator registrations |

**Registration map:**

| Translator | Node role(s) |
|---|---|
| `WeaponFireIntentEgressTranslator` | Brain, AllInOne |
| `WeaponFireRequestIngressTranslator` | MuscleGround, AllInOne |
| `WeaponFireNotificationEgressTranslator` | MuscleGround, AllInOne |
| `MunitionDetonationIngressTranslator` | MuscleGround, AllInOne (DamageAssessment) |
| `MunitionDetonationEgressTranslator` | MuscleGround, AllInOne |
| `DamageAssessedEgressTranslator` | MuscleGround, AllInOne |
| `EntityHitDamageIngressTranslator` | Brain, AllInOne |
| `EntityDamageEgressTranslator` | Brain, MuscleGround, AllInOne |

**Constraints**

- Follow the same pattern as existing conditional translator registration (guard by
  `role == NodeRole.X || role == NodeRole.AllInOne`).
- `AllInOne` must register both the Brain-side AND the Muscle-side translators — the
  event bus is the transport; DDS round-trip is skipped.
- Existing translator registrations must not be removed or reordered.

**Success Conditions**

1. *Solution compiles with no errors.*

2. *AllInOne integration:* an allocation-free integration test (or existing `AllInOne`
   smoke test) fires a weapon and confirms: (a) bullet is spawned, (b) bullet hits a
   target, (c) `Health.Current` decreases, (d) `EntityDamage` DDS message is published.

3. *Brain-only test (unit):* `WeaponFireIntentEgressTranslator` is present in the translator
   list when `SimHostApp` is initialised with `NodeRole.Brain`; `WeaponFireRequestIngressTranslator`
   is absent.

---

## Phase 5: Navigation CQRS Compliance

---

### BS1-T018 — Refactor FleeExecutor to Use NavigationIntent

**Design Reference:** [BS-1-DESIGN.md §8.1](./BS-1-DESIGN.md#81-fleeexecutor)

**Scope**

Rewrite `FleeExecutor.ComputeAndWriteFleeDestination` and `OnExit` to write a
`NavigationIntent` component (mode `NAV_DIRECT_POINT`) instead of mutating `NavState`
directly.

**Files to modify**

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/FleeExecutor.cs` | Replace NavState writes with NavigationIntent |

**Constraints**

- Use `NavigationConstants.ActionIdMoveTo` as the intent action ID (or a dedicated
  `ActionIdDirectPoint` if one exists).
- Increment `NavigationIntent.IntentId` when writing a new destination (preemption token).
- `OnExit`: set `NavigationIntent.Mode = NavigationMode.None` and increment `IntentId` to
  cancel locomtion — do NOT touch `NavState`.
- `Execute`: poll `LocomotionChannel.Status` (Success/Failure) for arrival detection.

**Success Conditions**

1. *OnEnter writes NavigationIntent, not NavState:*  
   Setup: Brain node world; entity A has `NavigationIntent`, `LocomotionChannel`; no `NavState`.  
   Action: call `FleeExecutor.OnEnter`.  
   Assert: `NavigationIntent.Mode` is set; `NavigationIntent.FinalDestination` is set;
   entity A does NOT have a mutated `NavState` (component absent or unchanged).

2. *OnExit clears intent:*  
   Action: call `FleeExecutor.OnExit`.  
   Assert: `NavigationIntent.Mode == NavigationMode.None`.

3. *AllInOne smoke-test:* entity with `FleeExecutor` active navigates away from a threat
   in an `AllInOne` topology; arrives (`LocomotionChannel.Status == Success`) without
   directly reading `NavState`.

---

### BS1-T019 — Refactor FollowRoadGraphExecutor to Use NavigationIntent

**Design Reference:** [BS-1-DESIGN.md §8.2](./BS-1-DESIGN.md#82-followroadgraphexecutor)

**Scope**

Rewrite `FollowRoadGraphExecutor.OnEnter`, `Execute`, and `OnExit` to use a `NavigationIntent`
of mode `NAV_ROAD_GRAPH` instead of writing to `NavState`.

**Files to modify**

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/FollowRoadGraphExecutor.cs` | Replace NavState with NavigationIntent |

**Constraints**

- `OnEnter`: write `NavigationIntent { Mode=NAV_ROAD_GRAPH, TargetNodeId=p.TargetNodeId, TargetSpeed=p.Speed }`.
- `Execute`: poll `NavigationStatus.Result` (via `World.GetComponent<NavigationStatus>`) for
  arrival; **do not** read `NavState.HasArrived`.
- `OnExit`: set `NavigationIntent.Mode = NavigationMode.None`.

**Success Conditions**

1. *OnEnter writes NavigationIntent:*  
   Assert: `NavigationIntent.Mode == NAV_ROAD_GRAPH`; `TargetNodeId` matches params; no `NavState`
   mutation.

2. *Execute polls NavigationStatus, not NavState:*  
   Setup: `NavigationStatus.Result = Arrived`; `NavState.HasArrived = 0` (mismatched).  
   Assert: executor returns `NodeStatus.Success` (trusts `NavigationStatus`, ignores `NavState`).

3. *All existing FollowRoadGraph integration tests pass.*

---

### BS1-T020 — Refactor FollowRouteExecutor to Use NavigationIntent

**Design Reference:** [BS-1-DESIGN.md §8.3](./BS-1-DESIGN.md#83-followrouteexecutor)

**Scope**

Rewrite `FollowRouteExecutor.OnEnter`, `Execute`, and `OnExit` to use a `NavigationIntent`
of mode `NAV_FOLLOW_ROUTE`.

**Files to modify**

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/FollowRouteExecutor.cs` | Replace NavState with NavigationIntent |

**Constraints**

- `OnEnter`: write `NavigationIntent { Mode=NAV_FOLLOW_ROUTE, TrajectoryId=p.TrajectoryId }`.
- Loop reset (when `p.Loop == true`): re-write `NavigationIntent` with incremented `IntentId`;
  do NOT reset `NavState.ProgressS`.
- `Execute`: poll `NavigationStatus.Result` for arrival/loop detection.
- `OnExit`: `NavigationIntent.Mode = NavigationMode.None`.

**Success Conditions**

1. *OnEnter writes NavigationIntent with correct TrajectoryId.*

2. *Loop reset re-writes NavigationIntent (new IntentId) without touching NavState.*

3. *All existing FollowRoute integration tests (e.g., `AreaAuthoringTests`) pass.*

---

### BS1-T021 — Remove NavState Poll from Action_Wander

**Design Reference:** [BS-1-DESIGN.md §8.4](./BS-1-DESIGN.md#84-action_wander--remove-navstate-poll)

**Scope**

Remove the secondary `NavState.HasArrived` check in `SimHostNodes.Action_Wander`.

**Files to modify**

| File | Change |
|---|---|
| `Hrot.SimHost/Brains/SimHostNodes.cs` | Remove NavState block in Action_Wander |

**Constraints**

- Remove ONLY the `NavState` block: the `if (!needsNewTarget && ctx.World.HasComponent<NavState>...)` section.
- The primary arrival detection via `channel.Status == NodeStatus.Success` MUST remain unchanged.
- Do not change any other wander logic.

**Success Conditions**

1. *Compilation:* no stray `NavState` references remain in `Action_Wander`.

2. *Wander still picks new target on Success:*  
   Setup: Brain node world (no `NavState` component on entity).  
   `LocomotionChannel.Status = NodeStatus.Success`.  
   Action: call `Action_Wander`.  
   Assert: `channel.ActiveAction` is reset; a new `MoveToParams` is written.

3. *Wander still runs when In-Progress:*  
   `channel.Status = NodeStatus.Running`.  
   Assert: no new target picked; returns `NodeStatus.Running`.

---

### BS1-T022 — Fix MissionDirectorSystem.ReachedDestination + UI Generator

**Design Reference:** [BS-1-DESIGN.md §8.5](./BS-1-DESIGN.md#85-missiondirectorsystemreacheddestination--ui-generator)

**Scope**

1. **MissionDirectorSystem**: Change the `MissionTrigger.ReachedDestination` case to poll
   `DoctrineFinishedEvent` (or map it to the `DoctrineFinished` trigger) instead of
   `NavState.HasArrived`.

2. **UI Generator**: Find the code that creates `MoveToLocation` missions (right-click handler
   `HandleRightClickForEntity`) and change the trigger from `MissionTrigger.ReachedDestination`
   to `MissionTrigger.DoctrineFinished`.

**Files to modify**

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs` | Fix ReachedDestination case |
| UI handler file (search for `HandleRightClickForEntity`) | Change trigger generation |

**Constraints**

- If both `ReachedDestination` and `DoctrineFinished` enum values exist, map `ReachedDestination`
  to the same logic as `DoctrineFinished` for backward compatibility; optionally mark
  `ReachedDestination` as `[Obsolete]`.
- Do NOT delete the `ReachedDestination` enum value in this task (may break serialised mission
  plans); only fix the runtime evaluation.
- Test that missions sent over DDS (with the old `ReachedDestination` trigger) still advance
  correctly by falling through to the `DoctrineFinished` path.

**Success Conditions**

1. *ReachedDestination mission advances (using DoctrineFinishedEvent):*  
   Setup: entity on Brain node only (no `NavState`); mission phase trigger is `ReachedDestination`.  
   Simulate `DoctrineFinishedEvent` for that entity.  
   Assert: `MissionPlanQueue.CurrentPhase` increments.

2. *NavState.HasArrived no longer consulted:*  
   Setup: `NavState.HasArrived = 1` on a Brain-only entity.  
   Assert: this alone does NOT advance the mission phase (trigger now requires DoctrineFinished).

3. *UI right-click generates DoctrineFinished trigger:*  
   Setup: simulate right-click for `MoveToLocation`.  
   Assert: the generated `MissionPhase.Trigger == MissionTrigger.DoctrineFinished`.

4. *Existing DoctrineFinished test path unchanged:* existing tests for
   `MissionTrigger.DoctrineFinished` still pass.
