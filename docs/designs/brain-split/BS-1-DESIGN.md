# BS-1 Design: Brain / Muscle Node Separation

**Workstream prefix:** `BS-1`  
**Design talk source:** [`docs/brain-split/design-talk.md`](./design-talk.md)  
**Task details:** [`BS-1-TASK-DETAIL.md`](./BS-1-TASK-DETAIL.md)  
**Task tracker:** [`BS-1-TASK-TRACKER.md`](./BS-1-TASK-TRACKER.md)

---

## 1. Context and Problem Statement

The simulation engine is designed around a distributed multi-node topology where different
simulation concerns can run on separate physical (or logical) processes connected via DDS:

- **Brain node** (`NodeRole.Brain`) — AI behaviors, mission management, behaviour trees / HSMs
- **Muscle node** (`NodeRole.MuscleGround`) — vehicle kinematics, physics, weapon simulation
- **Perception node** (`NodeRole.Perception`) — spatial queries, line-of-sight raycasting
- **Navigation Solver node** (`NodeRole.NavigationSolver`) — pathfinding over road graphs
- **AllInOne** (`NodeRole.AllInOne`) — all of the above in one process (development / testing)

The Brain → Muscle CQRS contract is already established for locomotion:

| Layer | Command | Status |
|---|---|---|
| Brain emits | `NavigationIntent` | via DDS |
| Muscle executes physics | `CarKinematicsSystem` | — |
| Muscle reports back | `NavigationStatus` | via DDS |

**However, several subsystems violate this separation:**

### 1.1 Combat Module on the Brain Node

`NodeBootstrapper.cs` registers `CombatModule` on every simulation role including `NodeRole.Brain`.
This means the Brain node independently runs ballistics, hit resolution, and damage application —
work that belongs exclusively on the physics / Muscle node.

### 1.2 No HasAuthority Guard in DamageSystem

`DamageSystem` (in `FDP.Toolkit.Combat`) consumes `HitEvent`s and unconditionally decrements
`Health.Current` without checking `view.HasAuthority(evt.HitEntity)`. In an `AllInOne` topology
this is harmless. In a Brain/Muscle split every node that receives a `HitEvent` computes damage
independently, breaking the single-source-of-truth principle.

### 1.3 EntityDamageEgressTranslator Does Not Exist

The `EntityDamage` DDS topic exists and the IG has an ingress translator ready
(`EntityDamageIngressTranslator` → `IgHealthState`), but SimHost never publishes
it. Health updates are never sent over the network.

### 1.4 Navigation Executors Bypass NavigationIntent

Several locomotion executors running on the Brain tier directly mutate or poll the
`NavState` Muscle-tier component instead of going through the `NavigationIntent` CQRS
channel. Because `NavState` is never replicated over DDS, these executors silently
fail in a distributed topology:

| Executor | Violation |
|---|---|
| `FleeExecutor` | Writes `NavState.Mode`, `.FinalDestination`, `.TargetSpeed` |
| `FollowRoadGraphExecutor` | Writes `NavState.Mode`, `.RoadPhase`, `.CurrentSegmentId`; polls `HasArrived` |
| `FollowRouteExecutor` | Writes `NavState.Mode`, `.TrajectoryId`; polls `HasArrived` |
| `SimHostNodes.Action_Wander` | Polls `NavState.HasArrived` as secondary arrival signal |

### 1.5 MissionTrigger.ReachedDestination Bug

`MissionDirectorSystem` evaluates the `ReachedDestination` mission trigger by polling
`NavState.HasArrived` directly. When the Brain runs on a separate node from the Muscle,
`NavState` is never updated and the mission phase will never advance — hanging indefinitely.

The UI code that generates `MoveToLocation` missions (`HandleRightClickForEntity`) also
hardcodes the `ReachedDestination` trigger.

---

## 2. Target Architecture

After this workstream the **Brain node is a pure cognitive tier**:

```
┌──────────────────────────────────────────────┐
│  Brain Node                                  │
│  • Mission management (MissionDirectorSystem)│
│  • BTree / HSM behavior runtime              │
│  • Sensor data consumption (TargetMemory)    │
│  • Navigation intents (NavigationIntent)     │
│  • Weapon fire intents (WeaponFireIntent)    │
│  NO bullets, NO damage, NO NavState         │
└────────────────┬─────────────────────────────┘
                 │ DDS: WeaponFireRequest, NavigationIntent
                 ▼
┌──────────────────────────────────────────────┐
│  Muscle Node (Physics Tier)                  │
│  • Vehicle kinematics (CarKinematicsSystem)  │
│  • Ballistics + CCD pipeline                 │
│  • Fire processing (spawns bullets)          │
│  • Hit resolution                            │
│  • Navigation execution (NavState)           │
└────────────────┬─────────────────────────────┘
                 │ DDS: WeaponFire (muzzle flash), MunitionDetonation
                 ▼
┌──────────────────────────────────────────────┐
│  Damage Assessment Module                    │
│  (collocated with Muscle or standalone node) │
│  • DamageCalculationSystem                  │
│  • Armor penetration curves (POC: flat hp)  │
└────────────────┬─────────────────────────────┘
                 │ DDS: EntityHitDamage → EntityDamage
                 ▼
┌──────────────────────────────────────────────┐
│  Brain / Authority Node                      │
│  • Apply damage to Health component          │
│  • Update HealthData mirror                  │
│  • MissionTrigger.HealthCritical still works │
└──────────────────────────────────────────────┘
```

### 2.1 Communication Contract (full pipeline)

```
Brain                    DDS                     Muscle
  │── WeaponFireIntent ──► WeaponFireRequest ──►│
  │                                             │── spawns bullet
  │                                             │── fires → WeaponFireNotification
  │                       WeaponFire ◄──────────│   (IG draws muzzle flash)
  │                                             │── bullet hits → DetonationNotification
  │                       MunitionDetonation ◄──│   (IG draws explosion)
  │                                   │
  │                             DamageAssessment Module
  │                                   │── DamageCalculationSystem
  │                       EntityHitDamage ◄─────│
  │◄── EntityDamage ──────────────────────────── │
  │   (Brain updates Health/HealthData)
```

---

## 3. Design Principles

1. **CQRS across node boundaries.** The Brain writes intents; the Muscle executes them and reports
   status. Neither tier reads the other's local ECS components directly.

2. **Single source of truth for health.** Only the authoritative node (checked via
   `view.HasAuthority(entity)`) applies damage. Other nodes receive health state via DDS.

3. **Proof-of-concept scope.** Message types (WeaponFireRequest, WeaponFire, MunitionDetonation,
   EntityHitDamage) are simplified C# structs — not the full IDL schema. Topic names match the
   target contract; attributes are minimal.

4. **Backward compatibility with AllInOne.** The `AllInOne` role must continue to work without a
   network round-trip. The unified node runs both the Brain and Muscle systems, so the local ECS
   event bus serves as the transport and no actual DDS round-trip is needed.

5. **NavigationIntent as the single locomotion command.** All Brain-tier locomotion executors must
   write `NavigationIntent` (not `NavState`). The existing `NavigationIntentBridgeSystem` on the
   Muscle translates this intent into `NavState`.

---

## 4. Phase 1: Event & Contract Foundations

**Goal:** Define the data contracts (ECS event structs and DDS message types) that the entire
pipeline depends on, and immediately guard the `DamageSystem` against authority violations.

### 4.1 WeaponFire Pipeline Contracts

Four new C# types for the weapon-fire CQRS chain (POC-simplified):

| ECS Event | Direction | DDS Message | Purpose |
|---|---|---|---|
| `WeaponFireIntent` | Brain internal | `WeaponFireRequest` | Brain issues fire command |
| `WeaponFireNotification` | Muscle internal | `WeaponFire` | Muscle confirms shot fired (IG: muzzle flash) |

Struct definitions — see Task **BS1-T001**.

### 4.2 Detonation & Damage Pipeline Contracts

| ECS Event | Direction | DDS Message | Purpose |
|---|---|---|---|
| `DetonationNotification` | Muscle internal | `MunitionDetonation` | Bullet impact (IG: explosion) |
| `DamageAssessedEvent` | DamageAssessment internal | `EntityHitDamage` | Computed HP loss |

Struct definitions — see Task **BS1-T002**.

### 4.3 DamageSystem Authority Guard

`DamageSystem.OnUpdate` must check `view.HasAuthority(evt.HitEntity)` before applying
damage. In the current codebase (`FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageSystem.cs`)
there is no such check. See Task **BS1-T003**.

---

## 5. Phase 2: Weapon Fire CQRS Pipeline

**Goal:** Replace the local `FireRequestEvent`-based firing loop with a network-transparent
CQRS chain — Brain emits intent, Muscle executes and reports back.

### 5.1 AimAndFireExecutor → WeaponFireIntent

Currently `AimAndFireExecutor` publishes `FireRequestEvent` which is consumed locally by
`FireProcessingSystem`. After the change it publishes `WeaponFireIntent` instead.
`FireRequestEvent` is removed. See Task **BS1-T004**.

### 5.2 Brain Egress — WeaponFireIntentEgressTranslator

A new translator on the Brain node watches the local event bus for `WeaponFireIntent` and
publishes a `WeaponFireRequest` DDS message. See Task **BS1-T005**.

### 5.3 Muscle Ingress — WeaponFireRequestIngressTranslator

A new translator on the Muscle node receives `WeaponFireRequest` from DDS and re-publishes
a local `WeaponFireIntent` into the Muscle's ECS event bus. See Task **BS1-T006**.

### 5.4 FireProcessingSystem — emit WeaponFireNotification

`FireProcessingSystem` currently consumes `FireRequestEvent`. After the refactor it consumes
`WeaponFireIntent` (the same struct regardless of origin) and, after spawning the bullet, also
publishes a `WeaponFireNotification` event. See Task **BS1-T007**.

#### ⛔⛔⛔ 5.4a — CE-198: **`FireProcessingSystem` MUST NOT gate on `NetworkAuthority`** *(as-built correction, `2026-09-05`)*

⚠ **`TD-6` (`BS-1-BATCH-04`) added exactly such a gate** — *"only spawns bullets if the node is
authoritative over the shooter"* — and it **could never pass on the only node that runs the system**,
silently disabling the whole kill chain on **every DISTRIBUTED** topology.

⚠⚠ **SCOPE — corrected `2026-09-05`; an earlier version of this section said "every topology" and that was
too strong.** ⭐ In **`--mode editor`** there is **one world at node 0** and the entities are created
locally, so `NetworkAuthority` reads `{PrimaryOwnerId: 0, LocalNodeId: 0}` ⇒ the gate **passed** and kills
always worked — measured `2026-09-04` and reproduced exactly `2026-09-05`
*(`1001 50/50 Ammo 41 · 1002 50/50 Ammo 41 · 1006 0/50 · 1007 0/50`)*. ⛔ **The gate bit only where the
Muscle's combatants are ghosts** — the 4-process cluster and `--mode all`. ⇒ 🔒 **that asymmetry is why the
defect survived: the topology exercised most often is the one it did not affect**, and the product's
shipping topology is the one it killed.

📐 **Measured live on `hill-attack-close`, 4-process cluster AND `--mode all`:**

| what | measurement |
|---|---|
| `NetworkAuthority` on the **Muscle**, every combatant | `{ HasAuthority: false, PrimaryOwnerId: -1, LocalNodeId: 1 }` |
| …because `EntityMasterIngressTranslator.cs:147` stamps ghosts with the **unknown-owner sentinel** `-1` | by design — see its own comment |
| `NetworkAuthority` on the **Brain** | `{ PrimaryOwnerId: 400, LocalNodeId: 400 }` ⇒ `HasAuthority` true |
| ⇒ with the gate: `WeaponFire`(81) **sent 0**, `MunitionDetonation`(82) **0**, `EntityHitDamage`(83) **0**, hostiles `50/50` after 6 engagements | — |
| ⇒ gate removed, same scenario: `WeaponFire` **5→5**, `MunitionDetonation` **6→6**, `EntityHitDamage` **6→6**, both hostiles **`0/50` on Brain AND IG** | causation proven |

⛔⛔ **The Brain must stay `PrimaryOwnerId`** — §6.4's `HealthApplicationSystem` applies damage behind
that very flag (`HealthApplicationSystem.cs:61`), and it worked in the probe run. ⇒ **making the Muscle
the owner would break damage application instead.** ⭐ §2.1's contract is the authority here: *"Brain ──
`WeaponFireIntent` ─► `WeaponFireRequest` ─► Muscle ── spawns bullet"* — **the Muscle executes the order
it was given; it is CORRECTLY not the owner.**

⭐⭐ **`TD-6`'s real concern — several nodes spawning duplicate bullets — is a COMPOSITION property**, and
is now structurally enforced by the capability seam: only the node whose `NodeRole` composes the combat
capability schedules these systems *(measured on `--mode all`: exactly one of three subsystems carries
`FireProcessingSystem`, `HitResolutionSystem`, `BallisticsSystem`, `DamageCalculationSystem`)*.
⛔ **A runtime flag that is false by construction cannot express it.**

⚠ **Why the suite never caught it:** `FireProcessingSystemTests`' two `TD-6` rails hand-built
`NetworkAuthority` with `primaryOwnerId` **2** (a *known* other owner) and **1** (self). Production on
the Muscle produces **neither** — it produces `-1`. ⇒ ⭐ the replacement rails
(`..._ForAGhostShooterWithTheUnknownOwnerSentinel`, `..._WhenAnotherNodeOwnsTheShooter`) build the shape
production actually has, and redden under inverse edit.

⇒ 🔒 **§4.3's `DamageSystem` guard and §6.4's `HealthApplicationSystem` guard are UNCHANGED and correct.**
The authority rule governs **who applies damage**, never **who executes a shot**.

### 5.5 Muscle Egress — WeaponFireNotificationEgressTranslator

A new translator on the Muscle publishes a `WeaponFire` DDS message for each
`WeaponFireNotification`. The IG listens to this topic to trigger the muzzle-flash effect.
See Task **BS1-T008**.

### 5.6 IG Ingress — WeaponFireIngressTranslator

The IG receives `WeaponFire` DDS messages and publishes a local `IgWeaponFireEvent` for the
visual layer to draw muzzle flashes and tracers. See Task **BS1-T009**.

---

## 6. Phase 3: Detonation & Damage Assessment Pipeline

**Goal:** Establish the detonation → damage → health-update CQRS chain, create the Damage
Assessment Module, and add the missing `EntityDamageEgressTranslator`.

### 6.1 HitResolutionSystem — emit DetonationNotification

`HitResolutionSystem` (in `FDP.Toolkit.Combat`) currently emits a `HitEvent`. After the
refactor it additionally emits a `DetonationNotification` capturing the hit position and
entity identifiers. See Task **BS1-T010**.

### 6.2 Muscle Egress — MunitionDetonationEgressTranslator

Translates the local `DetonationNotification` event to the `MunitionDetonation` DDS message.
Also consumed by the IG for explosion particle effects. See Task **BS1-T011**.

### 6.3 Damage Assessment Module

A new `DamageAssessmentModule` (in `FDP.Toolkit.Combat` or as a collocated SimHost module)
contains:

- `MunitionDetonationIngressTranslator`: DDS `MunitionDetonation` → local `DetonationNotification`
- `DamageCalculationSystem`: computes HP loss (POC: flat value from `BallisticProjectile.Damage`),
  publishes `DamageAssessedEvent`
- `DamageAssessedEgressTranslator`: publishes `EntityHitDamage` DDS message

The module is registered on a node with authority over the target entity; in the POC this is
the same node as the Muscle (collocated). See Task **BS1-T012** and **BS1-T013**.

### 6.4 Health Application Pipeline

When `EntityHitDamage` is received:

- `EntityHitDamageIngressTranslator` (on the authority node) deserialises the DDS message
  and publishes a local `DamageAssessedEvent`.
- An entity-type-agnostic `HealthApplicationSystem` consumes the event, checks
  `HasAuthority`, decrements `Health.Current`, and strips
  `ActorCapabilities` if the entity is destroyed.

> ⚠ **CORRECTED 2026-09-05.** This step said the system *"updates the `HealthData` mirror"*.
> 📐 `HealthData` was **deleted** by `BUG2-A001`; only a reserved id remains in `GlobalComponentIds`.
> The system has no mirror to update, and `MissionDirectorSystem.cs:150` says so in its own words.

In the future, entity-type-specific damage modules can replace or override this system.
See Task **BS1-T014**.

### 6.5 EntityDamageEgressTranslator (authority node → IG)

> ⚠⚠ **CORRECTED 2026-09-05 (CE-196).** This heading said *"SimHost → IG"*. 📐 Measured on a live
> `--mode all`: the translator is **authority-gated** (`view.HasAuthority`), and the authority for
> descriptor 30 is **CGF** — SimHost published **0** samples, CGF published all of them. The publisher is
> whichever node owns the entity, which for `hill-attack` is the Brain.
>
> ⚠ **And the payload changed.** It no longer carries a precomputed 0–100 damage level; it carries
> `Current` + `Max`, and each consumer derives its own fraction.
> 🔒 User ruling, 2026-09-05: *"having both Max and Current makes sense as ECS component AND network
> descriptor, no precalculated percentages."*

A new `EntityDamageEgressTranslator` tracks dirty `Health` components and publishes
`EntityDamage` DDS messages so the IG updates health bars. Registered in `SimHostApp.cs`.
See Task **BS1-T015**.

---

## 7. Phase 4: Node Role Reconfiguration

**Goal:** Enforce the correct module assignments per node role and wire up the new translators.

### 7.1 NodeBootstrapper Changes

| Role | Before | After |
|---|---|---|
| `Brain` | MissionControl, CognitiveRuntime, ActionDispatch, **Combat** | MissionControl, CognitiveRuntime, ActionDispatch |
| `MuscleGround` | ActionDispatch, GroundKinematics, Combat | ActionDispatch, GroundKinematics, Combat, **DamageAssessment** |
| `AllInOne` | All | All (unchanged — AllInOne gets everything) |

ActionDispatch on the Brain keeps `AimAndFireExecutor` (which now emits `WeaponFireIntent`).
See Task **BS1-T016**.

### 7.2 Translator Registration in SimHostApp

Register the new translators in `SimHostApp.cs` for each applicable node role:

- Brain role: `WeaponFireIntentEgressTranslator` (egress)
- Muscle role: `WeaponFireRequestIngressTranslator` (ingress), `WeaponFireNotificationEgressTranslator` (egress), `MunitionDetonationEgressTranslator` (egress)
- Authority / Muscle role: `EntityHitDamageIngressTranslator` (ingress), `EntityDamageEgressTranslator` (egress)
- IG: `WeaponFireIngressTranslator` (ingress)

See Task **BS1-T017**.

---

## 8. Phase 5: Navigation CQRS Compliance

**Goal:** Remove all direct `NavState` mutations from Brain-tier executors and fix the
`MissionDirectorSystem.ReachedDestination` trigger.

The infrastructure is already in place: `NavigationIntent` has modes for `DirectPoint`,
`FollowRoute`, and `RoadGraph`. `NavigationIntentBridgeSystem` on the Muscle translates
intents into `NavState`. `NavigationStatus` carries `Arrived`, `InProgress`, and `Failed*`
results back to the Brain.

### 8.1 FleeExecutor

Replace direct `NavState` writes with a `NavigationIntent` of mode `NAV_DIRECT_POINT`
and use `LocomotionChannel.Status` / `NavigationStatus` for exit detection.
See Task **BS1-T018**.

### 8.2 FollowRoadGraphExecutor

Replace `NavState.Mode = KinematicsMode.RoadGraph` etc. with a `NavigationIntent` of
mode `NAV_ROAD_GRAPH`. Use `NavigationStatus.Result` for arrival polling.
See Task **BS1-T019**.

### 8.3 FollowRouteExecutor

Replace `NavState.Mode = KinematicsMode.CustomTrajectory` etc. with a `NavigationIntent`
of mode `NAV_FOLLOW_ROUTE`. Use `NavigationStatus.Result` for loop-reset detection.
See Task **BS1-T020**.

### 8.4 Action_Wander — Remove NavState Poll

The secondary `NavState.HasArrived` check inside `SimHostNodes.Action_Wander` is
redundant and breaks the Brain/Muscle boundary. Remove it; the primary check on
`LocomotionChannel.Status` is sufficient. See Task **BS1-T021**.

### 8.5 MissionDirectorSystem.ReachedDestination + UI Generator

`MissionTrigger.ReachedDestination` must be removed (or aliased to `BehaviorFinished` for
backward compatibility). The UI right-click handler that creates `MoveToLocation` missions
must emit `MissionTrigger.BehaviorFinished` instead. See Task **BS1-T022**.

---

## 9. Out of Scope

The following topics were discussed in the design talk but are **deferred** to later
workstreams:

- **Perception node** (`AutonomousPerceptionModule`, `SensorConfig`, `SensorTargets`) — already
  correctly designed for Brain/Muscle separation; no changes needed.
- **NavMesh / Navigation Solver node** — already correctly designed; path computation via
  `PathRequestBatch` / `RouteHandle` is network-transparent.
- **Full IDL-compliant message schemas** (BDC.SST.Msg.idl) — POC uses simplified structs.  
- **Entity-type-specific damage calculators** (tracked vs wheeled vs infantry) — basic
  `DamageCalculationSystem` is sufficient for the POC.
- **HsmDamageBridgeSystem / mobility-kill via HSM** — depends on the Damage pipeline being
  complete first.
