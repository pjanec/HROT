<!--STATUS
state: LIVE
updated: 2026-10-11
build-state: BUILDING (WL-1a §5a and WL-1b §5b built) — W-1..W-9 approved (U-W2), §6 ambient fire approved as revised (U-W5), W-10 turret dead reckoning noted (U-W5: design only, no implementation yet); W-11 approved (U-W6): the task lives on the turret part
current-answer: §1 the three layers (module diagram) · §2 classes · §3 sequences · §4 decisions W-1..W-9 (APPROVED), W-11 (APPROVED, supersedes W-2 per-mount) · §5 slices · §6 ambient fire
stale-below: nothing yet
known-rot: none
known-conflict: DESIGN_Ownership_Groups_And_Grants.md F-6 says weapon-mount parts are never created — stale, CgfComponentRegistry.cs:38
  registers WeaponMountInfo and CombatTkbTranslator.cs:92-122 creates them on CGF. DESIGN_Decision_Layer.md :1330 calls
  AimAndFireExecutor "the only weapon-channel executor" — stale since FireAtPointExecutor (DESIGN_Building_Interiors.md §3k).
related-designs:
  - DESIGN_Map_3D_Mode.md — §3.11 M23–M26 (R-255): the turret is a part child with a TurretPose, the weapon side aims it, it
    replicates as a multi-instance descriptor. THIS file decides which weapon layer on which node does the aiming.
  - DESIGN_Visual_Effects.md — VE-J..VE-L: Muzzle.Of(shooter, weaponIndex) from the posed barrel, used by the round AND the flash.
  - DESIGN_Utility_AI_Demo_Scenarios.md — §12 W1–W9: weapon choice per shot inside the executor (Mount=Auto), mount children on
    CGF; §9 the one ArmorModel (R-212). The weapon brain here takes over that choice.
  - DESIGN_Peek_And_Fire.md — D3 aim time per weapon, D4 seen-now gate, D9 reload, B7 rounds: the gates the weapon brain keeps.
  - docs/designs/brain-split/BS-1-DESIGN.md — the Brain writes intents, the Muscle executes (WeaponFireRequest per shot).
  - blueprints/Architect_Question_80_Animation_Channel_Ownership_Split.md — the request (Brain) + status (Muscle) split channel,
    R-180: the precedent for the Brain → Muscle weapon task.
  - blueprints/Architect_Question_74_Blueprint_Channel_Lifecycle.md — channel claim / release / preemption, unchanged here.
  - DESIGN_Decision_Layer.md — §4.4 ROE enforced once (R-200).
  - DESIGN_World_Query_Seam.md — R-252: the brain makes no terrain assumptions — elevation is solved on the Muscle.
  - PLAN_3D_World_And_Realism.md — B1 (live turret) and B5 (Muzzle.Of) are built through this design.
  - DESIGN_Dead_Reckoning.md — the ONE dead-reckoning model (sample + rate + sim stamp, pure projection, R1–R8); W-10 applies it
    to TurretPose instead of inventing a second one.
-->

# Weapon layers — behaviour brain, weapon brain, weapon muscle

> 🔒 **User, `2026-10-11` (U-W1):** *"Turrets and barrels are articulated part that should travel as multi-instance (multi part)
> descriptor where the part id is the turret index. Weapon brain should be something out of usual higher level behavior, doing low
> level weapon logic based on weapon channel requests … Could be a hardcoded system customizable per weapon category … Brain should
> own higher level logic of the weapon, muscle owns ballistics which affects aiming (barrel elevation) so muscle is well suited for
> 'keep weapon aimed at target', 'aim and shoot' tasks while the higher level weapon brain on brain node selects suitable weapon and
> ammo per target type etc. Behavioral brain decides what targets to attack, requests attacking targets, or asks the weapon brain
> 'track target' etc. AimSeconds can stay for simplest weapon brains not modelling turrets and barrels at all, or weapons not having
> ones — could be part of the weapon brain customization."*

## INVENTORY — measured `2026-10-11`, branch `ui`

Graph `search_graph` over `.*(WeaponChannel|AimAndFire|FireAtPoint|Executor|Dispatcher|Magazine|Munition|Armor|Ballistic|Warhead|
HitResolution|FireProcessing|ChannelCommand|Channel|Turret|Articulat).*` (label Class: 196, both pages read) plus grep.
`check_index_coverage` is not available through the CLI, so no completeness check was run.

| what exists | where | what this design does with it |
|---|---|---|
| `WeaponChannel` + `WeaponDispatcherSystem` + `IActionExecutor` (Brain only) | `Behavior/Components/ChannelComponents.cs:26`, `Behavior/Systems/WeaponDispatcherSystem.cs:17`, registered `CgfLogicPack.cs:171-175` | ⭐ **kept** — the behaviour → weapon-brain request, unchanged for behaviours |
| `AimAndFireExecutor` — target alive · rounds · mount choice · ammo · seen-now + aim timer · cooldown · magazine · friendly line · ROE · `WeaponFireIntent` | `Combat/Executors/AimAndFireExecutor.cs:66-182` | ⭐ **split** — it is today's weapon brain AND gunner in one; §2 shows which half moves |
| `FireAtPointExecutor` (indirect, no aim gate) | `Combat/Executors/FireAtPointExecutor.cs:34` | kept as the indirect-fire profile |
| `WeaponChoice` / `WeaponSelectionDecision` / `ArmorModel` (R-212) | `Combat/WeaponChoice.cs:22`, `Utility/StarterPack/WeaponSelectionDecision.cs:17`, `Combat/ArmorModel.cs` | ⭐ **moved into the weapon brain** — no second chooser |
| `Magazine`, `WeaponState` (Brain group) | `Combat/Magazine.cs:14`, `CombatComponents.cs:13`, `HrotOwnershipGroups.cs:52` | stay Brain-owned (W-4) |
| mount part children (`WeaponMountInfo` + `PartMetadata`, CGF only) | `CombatTkbTranslator.cs:92-122` | reused as the weapon brain's per-mount state |
| `FireProcessingSystem` (aim point, deflection, muzzle 1 m, `ArcLaunch` for indirect only) | `Combat/Systems/FireProcessingSystem.cs:42-260` | ⭐ **reused** by the muscle's engage executor — the one place a round is born |
| `WeaponFireRequest` Brain → Muscle, one shot each | `Hrot.Network.NED/FireInteractionMessages.cs:32` | kept for the simplest profile (W-2) |
| multi-instance child config `EqsSensorConfigTopic{ParentNetworkId, LocalChildIndex}` | `Spatial/Eqs/EqsDdsTopics.cs:16-21`, translators `EqsSensorConfig*Translator.cs` | ⭐ **the wire pattern** for `WeaponTask` / `WeaponTaskStatus` / `TurretPose` (M26) |
| split request + status channel, Muscle dispatcher on a stack copy | `LookAtDispatcherSystem.cs:20,36`, `ReplicatedComponents.cs:30-91` (R-180, Q80) | ⭐ **the muscle-side pattern** for the weapon task dispatcher |
| `ArticulationPose` seam in the 3-D map (fed by nothing) | `Map3D/ShapeKit.cs:48`, `EntityBodyLayer3D.cs:33,97-105`, plug site `MapInteractionPack.cs:389` | fed from `TurretPose` |
| weapon CATEGORY | ⛔ **none** — searched FDP + Hrot; only implicit: `WarheadKind`, `WarheadDto.Indirect`, ammo `type`, `SoundSourceClass` | ⭐ new (W-5) |
| turret fields in the TKB (pivot, limits, rates, muzzle offset) | ⛔ **none** (`WeaponSuiteDto.cs:14-74`, `BodyGeometryDto`) | ⭐ new (M25) |
| anything on the Muscle that keeps a weapon aimed | ⛔ **none** — no turret angle anywhere | ⭐ new — the weapon muscle |

## 1. The three layers — who runs where

```mermaid
graph TD
  subgraph Brain["Brain node (CGF, or the editor)"]
    BB["behaviour brain<br/>BT / HSM / Blueprint<br/>WHAT to attack"]
    WC["WeaponChannel<br/>AimAndFire / FireAtPoint / TrackTarget"]
    WB["weapon brain<br/>WeaponDispatcherSystem + category profile<br/>WHICH weapon + ammo, ROE, seen-now, magazine"]
  end
  subgraph Muscle["Muscle node (SimHost)"]
    WM["weapon muscle<br/>WeaponTaskDispatcher<br/>slew, ballistic aim, fire timing"]
    FP["FireProcessingSystem<br/>(exists) the round is born here"]
  end
  subgraph Every["every node incl. IG and map hosts"]
    TP["turret part child<br/>PartMetadata + TurretPose"]
    MAP["3-D map body<br/>ArticulationPose"]
  end
  BB -->|"writes"| WC
  WC -->|"dispatched by"| WB
  WB -->|"WeaponTask per mount<br/>multi-instance, Brain-owned"| WM
  WM -->|"WeaponTaskStatus per mount<br/>Muscle-owned"| WB
  WB -->|"simplest profile: WeaponFireRequest per shot (exists)"| FP
  WM -->|"shot"| FP
  WM -->|"TurretPose per turret<br/>multi-instance, Muscle-owned"| TP
  TP --> MAP
```

*What the picture shows that prose hid:* the weapon brain has TWO ways down. A turreted weapon gets a standing per-mount TASK that
the muscle keeps true every frame. The simplest profile keeps today's one-request-per-shot path with `AimSeconds`. And the
turret pose is written only by the muscle, because the muscle solves the elevation (ballistics), and it is read by every node.

## 2. Classes — what exists, what moves, what is new

```mermaid
classDiagram
  class WeaponChannel { <<exists, Brain>> ActiveAction, Params, Status }
  class WeaponDispatcherSystem { <<exists, Brain>> executor per action id }
  class IWeaponBrainProfile { <<new, Brain>> category; ChooseMount; ChooseAmmo; UsesTurretTask; AimSeconds }
  class AimAndFireExecutor { <<exists, split>> keeps: target alive, rounds, ROE, seen-now, magazine; delegates the rest to the profile }
  class WeaponChoice { <<exists, moves into the profile>> }
  class ArmorModel { <<exists>> R-212, one model }
  class WeaponTask { <<new, Brain-owned, on the turret part, W-11>> Mode Idle-Track-Engage; Target; Point; MountIndex; RoundsGranted; AmmoGuid; Epoch }
  class WeaponTaskStatus { <<new, Muscle-owned, on the turret part, W-11>> Epoch; State; Reason; RoundsFired }
  class WeaponTaskDispatcher { <<new, Muscle>> one executor per Mode }
  class BallisticAim { <<new, Muscle>> azimuth and elevation for a target; direct and ArcLaunch }
  class TurretPose { <<new, Muscle-owned, on the turret part>> Azimuth; Elevation }
  class TurretDto { <<new TKB>> pivot; traverse and elevation limits; slew rates }
  class WeaponMountDto { <<exists TKB>> + Category; + TurretIndex; + MuzzleOffset; AimSeconds kept }
  class FireProcessingSystem { <<exists, Muscle>> the round is born; muzzle via Muzzle.Of (B5) }
  WeaponDispatcherSystem --> AimAndFireExecutor
  AimAndFireExecutor --> IWeaponBrainProfile
  IWeaponBrainProfile ..> WeaponChoice
  IWeaponBrainProfile ..> ArmorModel
  AimAndFireExecutor ..> WeaponTask : writes (turret profile)
  WeaponTaskStatus ..> AimAndFireExecutor : read back
  WeaponTaskDispatcher ..> WeaponTask : reads
  WeaponTaskDispatcher ..> WeaponTaskStatus : writes
  WeaponTaskDispatcher --> BallisticAim
  WeaponTaskDispatcher ..> TurretPose : slews
  WeaponTaskDispatcher ..> FireProcessingSystem : fires
  TurretPose ..> TurretDto : limits and rates
  WeaponMountDto ..> TurretDto : TurretIndex
```

*What the picture shows that prose hid:* nothing that exists is duplicated. `WeaponChoice`, `ArmorModel`, `Magazine` and ROE stay
single, inside the weapon brain. `FireProcessingSystem` stays the only place a round is born, for both paths. The new types are
only the per-mount task and status pair, the muscle's slew-and-fire loop, and the turret's pose and TKB data.

## 3. Sequences

**Engage with a turreted weapon (the T-72's 125 mm):**

```mermaid
sequenceDiagram
  participant B as behaviour brain
  participant W as weapon brain (Brain)
  participant M as weapon muscle (Muscle)
  participant F as FireProcessingSystem (Muscle)
  participant A as every node
  B->>W: WeaponChannel AimAndFire(target, rounds 3, mount auto)
  W->>W: profile picks mount and ammo (ArmorModel), ROE, seen-now, magazine ready
  W->>M: WeaponTask(turret 0, fire mount 0, Engage, target, granted 3, ammo, epoch)
  loop every muscle frame
    M->>M: BallisticAim gives azimuth and elevation, slew within TKB limits and rates
    M->>A: TurretPose(turret 0)
    M-->>W: WeaponTaskStatus(epoch, Slewing)
  end
  M->>F: on target and cooldown over, so fire
  M-->>W: WeaponTaskStatus(epoch, Firing, roundsFired 1)
  W->>W: decrement WeaponState ammo and magazine (Brain-owned)
  M-->>W: WeaponTaskStatus(epoch, Done, roundsFired 3)
  W-->>B: channel Success
```

*What it shows:* the weapon brain never computes an angle (M24). The muscle never decides WHETHER to fight: it fires only the
rounds it was granted, so ROE, seen-now and ammo stay single and Brain-side. A lost sighting downgrades the task to Track (the
turret keeps following) instead of cancelling it.

**The simplest profile (a rifleman, no turret) is today's path, unchanged:** the profile's `UsesTurretTask` is false, the
executor runs its `AimSeconds` timer and publishes `WeaponFireIntent` per shot → `WeaponFireRequest` → `FireProcessingSystem`.

## 4. Decisions — ✅ ALL APPROVED `2026-10-11` (U-W2: *"approved."*)

| # | question | ⭐ lean | rests on (code · design) | rejected |
|---|---|---|---|---|
| **W-1** | the layer split | behaviour brain → `WeaponChannel` (unchanged) → **weapon brain** (Brain: weapon + ammo choice, ROE, seen-now, magazine, rounds) → **weapon muscle** (Muscle: slew, ballistic aim, fire timing) | `AimAndFireExecutor.cs:66-182` holds both halves today · U-W1, M24, R-180 | the brain computing angles — M24 / R-252 |
| **W-2** | what crosses Brain → Muscle | ⚠ *per mount SUPERSEDED by W-11 — per TURRET part, naming the mount* · a standing **`WeaponTask` per mount** (Idle / Track / Engage, target or point, rounds GRANTED, ammo, epoch), multi-instance keyed (parent, mount index), Reliable + TransientLocal + KeepLast 1; ⭐ the simplest profile keeps the existing per-shot `WeaponFireRequest` | `EqsSensorConfigTopic` pattern (`EqsDdsTopics.cs:16-21`) · M26, BS-1 | a per-shot request for turrets — the muscle could not keep the gun on target between shots |
| **W-3** | how the result returns | **`WeaponTaskStatus` per mount** (echoed epoch, Slewing / OnTarget / Firing / Done / Failed + reason, rounds fired), Muscle-owned; the executor turns it into the channel's Success / Failure / Running | `NavigationIntent` / `NavigationStatus` echo (`SimDescriptors.cs:148,196`) · R-180, R-241 (`ActionStatus`) | polling `TurretPose` for "on target" — needs ballistics on the Brain |
| **W-4** | who owns ammo and the magazine | the **Brain** (logistics is higher-level): it GRANTS rounds in the task; the muscle fires at most those and reports `RoundsFired`; the brain decrements | `WeaponState` in the Brain group (`HrotOwnershipGroups.cs:52`) · R-239 magazine model | moving `WeaponState` to the Muscle — re-homes every reader of ammo on the Brain (utility inputs, executors) |
| **W-5** | the weapon category | a new `WeaponCategory` on `WeaponMountDto` (SmallArms, MachineGun, Autocannon, TankGun, Atgm, Mortar, Grenade) selecting a **hardcoded `IWeaponBrainProfile`** (C# class per category, registered in one table); the default profile reproduces today's behaviour | ⛔ no category exists (searched) · U-W1 "hardcoded system customizable per weapon category" | profiles in data (a script per weapon) — no need yet |
| **W-6** | ammo per target type | the profile picks the ammo with the SAME `ArmorModel` it uses for the mount (R-212); v1 keeps one `AmmoGuid` per mount but the task already carries it, so switching rounds later changes no wire | R-217 *"switching later"* · `WeaponSuiteDto.cs:74` | a second effectiveness model for ammo — two models for one fact |
| **W-7** | the gates | ROE + seen-now on the Brain decide Engage vs Track (a lost sighting downgrades to Track); the **friendly-on-the-line** check runs at the moment of the shot, wherever that moment is decided — one shared function called by the executor (simple path) and the muscle (task path) | `AimAndFireExecutor` gate order · R-200 (ROE once), Peek D4 | the muscle reading `TargetMemory` — perception memory is Brain state |
| **W-8** | elevation | the muscle's `BallisticAim`: direct fire = the line to the aim point (`AimPoint.For`, as today — no gravity on direct rounds yet); indirect = `ArcLaunch`; limits clamp; a target outside the limits → `Failed(OutOfArc)` so the profile can try another mount or the behaviour can move | `FireProcessingSystem.cs:128-133, 249-260` · R-252 (no terrain assumption on the Brain) | elevation from the Brain — it has no ballistics |
| **W-9** | the turret and its pose | a turret part child per `TurretDto` on EVERY node (created by a TKB translator, derived — not saved), `TurretPose` Muscle-owned, replicated keyed (parent, turret index), BestEffort + KeepLast 1, sent when it changes; mounts name their turret (`TurretIndex`); with no task the turret holds its pose | M23, M25, M26 · ⚠ mount children exist on CGF only today (`CgfComponentRegistry.cs:38`) | the turret pose on the mount child — two mounts share one turret (Bradley: 25 mm + TOW) |
| **W-10** | turret moves without flooding the network *(U-W5: "the turret moves might need simple dead reckoning to avoid flooding the network. No implementation yet.")* | ⭐ the **same dead-reckoning model positions use** (`DESIGN_Dead_Reckoning.md`), applied to two angles: the `TurretPose` sample carries azimuth, elevation, their RATES and the cluster-synced **sim stamp**; receivers keep the sample read-only (R2) and project it FRESH every frame — `angle + rate × (simNow − stamp)`, clamped to the turret's limits (R3, R4); a pause freezes it with no guard (R5). The owner sends only when the projection would be off by more than a small threshold (a fraction of a degree), when a rate changes (start, stop, reversal), and on a slow heartbeat — so a steady slew or an ambient scan sweep costs one sample per change of motion, not one per frame | `DeadReckoningSyncSystem.cs` (the position model) · `DESIGN_Dead_Reckoning.md` R2–R6 | a separate turret smoothing scheme — two dead-reckoning models for one idea; sending every frame — the flood U-W5 names |

### W-11 — ✅ APPROVED `2026-10-11` (U-W6): the weapon task lives on the TURRET part, not per mount *(supersedes W-2's "per mount")*

> 🔒 **User, `2026-10-11` (U-W6):** *"Ad turret vs mounts. A movable mount on a turret is basically a sub-turret. I would ignore it
> now, accepting your lean."* ⇒ a movable mount on a turret (a sub-turret) is out of scope for now; when it comes it is another turret
> part, not a per-mount task.

W-2 keys `WeaponTask` / `WeaponTaskStatus` by (parent, **mount** index). Building WL-2 needs an entity on the Muscle to hold them, and
the measured shape argues for keying them by the **turret part** instead (the `TurretPose` key), with the task naming the mount that fires.

| the lean rests on | code — how it IS | design — how it was MEANT |
|---|---|---|
| two mounts share one turret | ✅ catalog Bradley: 25 mm and TOW both `TurretIndex = 0` (`BdcTkbCatalog.cs`) | ✅ W-9 "two mounts share one turret" |
| a turret aims at ONE point at a time | ✅ one azimuth + one elevation (`TurretPose.cs`) | ✅ M23 "the turret and its gun are ONE assembly" |
| per-part ownership needs a PART entity | ✅ `SyncPartClaims` walks `PartMetadata` entities only (`OwnershipRecomputeSystem.cs:125`) | ✅ Ownership G-3 (a multi-instance descriptor covers every instance) |
| turret parts exist on every node; mount children on CGF only, and mount 0 is the hull itself | ✅ `TurretTkbTranslator` in the base set; `CgfComponentRegistry.cs:38`; `CombatTkbTranslator.cs:90` | ⛔ searched `docs/` + `.dev/`: no design puts mount parts on the Muscle |

⭐ **Ruled: one task per TURRET part** — `WeaponTask { Mode, Target/Point, MountIndex, RoundsGranted, AmmoGuid, Epoch }` and
`WeaponTaskStatus` on the turret part, keyed (hull, `2000 + turret`) like `TurretPose`; a hull-fixed mount (`TurretIndex = −1`, the
rifleman) keeps the per-shot path (W-2's simple profile). Blast radius: the WL-2/3 wire and the Muscle's dispatcher only — nothing built
changes. What would change it: a weapon that must aim two mounts on one turret at two targets at once (none in the catalog).
Rejected: per-mount tasks — two tasks for one turret need a tie-break nobody owns, and the Muscle has no mount entities to hold them.

⚠ **New behaviour vocabulary (part of W-1):** `TrackTarget(target)` on the weapon channel — keep the gun on a target without
firing — beside `AimAndFire` and `FireAtPoint`; releasing the channel sets the task to Idle.

## 5. Slices

| slice | builds | shows |
|---|---|---|
| **WL-1** | TKB `TurretDto` + `WeaponMountDto.Category/TurretIndex/MuzzleOffset`; turret part children on every node; `TurretPose` + its multi-instance descriptor; the 3-D map's `poseOf` reads it | the drawn turret follows a pose set by hand on the Muscle |
| **WL-2** | `WeaponTask` / `WeaponTaskStatus` descriptors; the muscle's `WeaponTaskDispatcher` with **Track**; `BallisticAim` | a tank keeps its gun on a moving target |
| **WL-3** | **Engage** on the muscle through `FireProcessingSystem`; the executor split + the default profile; rounds granted / fired; `TrackTarget` on the channel | a tank slews, then fires 3 rounds; behaviours unchanged |
| **WL-4** | category profiles (tank gun, autocannon, ATGM, mortar, small arms); ammo choice | per-category gunnery |
| **B5** | `Muzzle.Of` from the posed barrel — the round, the flash and the tracer from one point | VE-K |

### 5a. As built — WL-1a *(2026-10-11)*

WL-1 was split: **WL-1a** = the TKB data, the turret part on every node, the 3-D drawing; **WL-1b** = the keyed wire message
(the pose leaves the Muscle). Until WL-1b, a pose set on a node draws on that node only.

```mermaid
classDiagram
  class TurretSuiteDto { <<TKB, Combat.Turrets>> List~TurretDto~ Turrets }
  class TurretDto { PivotX/Y/Z; Traverse min/max; Elevation min/max; rates deg/s }
  class WeaponMountDto { + Category; + TurretIndex = -1; + MuzzleOffsetX/Y/Z }
  class TurretTkbTranslator { <<Base set, after CombatTkbTranslator>> Inject: Ensure per turret }
  class TurretParts { <<static>> FirstPartId = 2000; Ensure; Find; PoseOf; IndexOf }
  class TurretPose { <<component 351, shared registry>> Azimuth; Elevation (radians) }
  class PartMetadata { <<exists>> ParentEntity; InstanceId = 2000 + index }
  class TurretPoseSource { <<Hrot.Presentation Map3D>> PoseOf(world, entity) }
  class EntityBodyLayer3D { <<exists>> poseOf seam }
  TurretSuiteDto o-- TurretDto
  TurretTkbTranslator ..> TurretSuiteDto : reads
  TurretTkbTranslator ..> TurretParts : Ensure
  TurretParts ..> PartMetadata : builds
  TurretParts ..> TurretPose : builds
  TurretPoseSource ..> TurretPose : reads turret 0 live
  EntityBodyLayer3D ..> TurretPoseSource : poseOf (MapInteractionPack only)
  WeaponMountDto ..> TurretDto : TurretIndex
```

*What the picture shows that prose hid:* the turret child is built by the BASE translator set, so every node that spawns the unit has
it (unlike mount children, CGF only) — that is what lets WL-1b's ingress land on an existing entity. Part ids on one parent: mounts
`1…N`, TKB sensors `1000+`, turrets `2000+`. Every generic part consumer filters on its own component (`WeaponMountQuery`,
`UtilityScorer`, `StandardInputs` on `WeaponMountInfo`; `SubEntityCleanupSystem` destroys the child with its parent). The catalog's
M1, T-72 and Bradley carry one turret each (the Bradley's 25 mm and TOW share it) and every catalog weapon a category. The muzzle
offset is in the DTO but the catalog does not set it yet — B5 (`Muzzle.Of`) is its first reader.

### 5b. WL-1b — the turret pose on the wire *(✅ built 2026-10-11, as drawn)*

```mermaid
graph TD
  subgraph Owner["node owning dtTurretPose (the Muscle via the MuscleGround group; the creator when no group applies)"]
    W["writer of TurretPose<br/>(hand-set today; the weapon muscle WL-2, ambient fire §6)"]
    E["EntityTurretPoseEgressTranslator<br/>scan PartMetadata + TurretPose,<br/>HasAuthority(part, PackKey(dtTurretPose, partId))"]
  end
  subgraph Every["every other node — Brain, Muscle, IG, Stride"]
    I["EntityTurretPoseIngressTranslator<br/>parent from NetworkEntityMap,<br/>part = TurretParts.Find"]
    P["turret part (built by the spawn, WL-1a)"]
    M["3-D map TurretPoseSource"]
  end
  SP["SharedTranslatorPack<br/>(both halves, every NED node)"]
  IG["EntityStatesIngressPack<br/>(pure IG — ingress only)"]
  W --> E
  E -->|"EntityTurretPose<br/>key ParentNetworkId + PartId"| I
  I --> P --> M
  SP -.registers.-> E
  SP -.registers.-> I
  IG -.registers.-> I
```

*What the picture shows that prose hid:* nothing new is called each frame by a host. Both translators ride the two packs every NED node
already ticks — the shared pack on Brain / Muscle / Stride, the IG ingress pack on a pure IG (the shared pack's ingress is skipped
there, `NedReplicationModule.cs:420-422`; the door, `EntityStatesIngressPack.cs:58`, is the precedent). The editor alone is one node
and needs no wire.

```mermaid
classDiagram
  class EntityTurretPose { <<new wire, hrot-sim-desc>> [Key] long ParentNetworkId; [Key] int PartId; Azimuth; Elevation; AzimuthRate; ElevationRate; DateTime Time }
  class EntityTurretPoseEgressTranslator { <<new>> last sent per (net id, part); 0.2 deg threshold; salted heartbeat }
  class EntityTurretPoseIngressTranslator { <<new>> skip when IsRecordedOwner; drop when parent or part is not built yet }
  class TurretParts { <<exists, WL-1a>> Find; IndexOf }
  class DescriptorOwnershipMap { <<exists>> RegisterMapping(dtTurretPose, TurretPose) }
  class HrotOwnershipGroups { <<exists>> MuscleGround += TurretPose }
  class SimStampCodec { <<exists>> Encode(simSeconds) }
  EntityTurretPoseEgressTranslator ..> EntityTurretPose : writes
  EntityTurretPoseIngressTranslator ..> EntityTurretPose : reads
  EntityTurretPoseIngressTranslator ..> TurretParts : Find
  EntityTurretPoseEgressTranslator ..> SimStampCodec : stamp
  DescriptorOwnershipMap ..> HrotOwnershipGroups : BindGroups
```

```mermaid
sequenceDiagram
  participant O as owner (Muscle)
  participant D as DDS EntityTurretPose
  participant R as other node
  O->>O: TurretPose on part 2000 changes by more than 0.2 deg (or heartbeat)
  O->>D: (net id of the hull, 2000) az, el, rates 0, sim stamp
  D->>R: sample
  R->>R: hull from NetworkEntityMap, part = TurretParts.Find(hull, 0)
  R->>R: recorded owner of (dtTurretPose, 2000)? skip : SetComponent(TurretPose)
```

| choice | why | rejected — one line each |
|---|---|---|
| a **new descriptor** `dtTurretPose = 124`, mapped to `TurretPose` and put in the **MuscleGround** group | the same three steps every Muscle-owned report took (`dtNavigationStatus`, `dtStanceStatus`); `SyncPartClaims` then gives the part the claim from `(dtTurretPose, 0)` | the `TurretPose` bit with no descriptor — it would ride `dtWorldPos` (`BindGroups` anchor) and travel with no topic of its own |
| **hand-written translator pair**, the door's shape | the production pattern for every new replicated datum | `MultiInstanceCycloneTranslator` — tests-only, needs a `ChildMap` nothing in production builds |
| key = **(hull network id, part id)** | the EQS sensor key (`EqsSensorKey.Resolve`); the part id names the turret (`2000 + index`) | the turret index alone — the part id is what both sides already hold |
| **BestEffort, TransientLocal, KeepLast 1** (the `WorldPos` QoS) + a salted heartbeat (120 ticks, ~2 s) and a 0.2° change threshold | W-9; a lost sample is repaired by the next change or heartbeat, a late joiner gets the last sample | Reliable — a reliable history for a value that is superseded many times a second |
| **rates and sim stamp on the wire, written 0 / now** | W-10 fills them with no wire change; receivers do not project yet (U-W5: "no implementation yet") | adding them later — a wire-format change on every node |
| ingress **drops** a sample whose hull or turret part is not built yet | the spawn builds the part from the TKB; the next heartbeat lands on it | a pending queue (`EqsSensorConfigIngressTranslator`) — needed for config sent once, not for a value resent every ~2 s |

## 6. Ambient fire — the impression of distant combat, with no weapon model *(U-W3, revised by U-W4, ✅ approved U-W5: "Ok.")*

> 🔒 **User, `2026-10-11` (U-W3):** *"What about low fidelity high count 'ambient' entities where the complicated weapon control model
> is too much? … some entities never firing, having fixed turrets, or just making mechanical movement resembling target scans and
> firing in approximate direction of target just to create fire effect (and detonation on predetermined place, no ballistics at all,
> no real hit detection or target model …) … That requires no weapon brain, and the goal is to be simple, low resource, performant
> also in high amounts of entities."*
>
> 🔒 **User, `2026-10-11` (U-W4), on the first version:** *"approved partly, there needs to be usual fire and detonation notification
> events (not requiring anyone to calculate any ballistics or complex damage, just for visualization — the entities playing the
> ambient combat could have extremely simplified damage model, i think they still should handle the fake hit which is somehow derived
> from the fake shot) as well as turret position updates allowing the dumb viewer sw apps to still see the battle."*

📐 **Measured — what an ambient shot reuses:**
- The Muscle already SENDS every fire, detonation and damage verdict: `WeaponFireNotificationEgressTranslator`,
  `MunitionDetonationEgressTranslator` and `DamageAssessedEgressTranslator`, registered for the MuscleGround role
  (`SimHostAuxiliaryTranslatorPack.cs:85-93`). The Muscle also runs `DamageCalculationSystem`, which turns a
  `DetonationNotification` with a live target into a `DamageAssessedEvent` (today through `ArmorModel`).
- Every viewer (HROT map hosts, IG, external DIS / NED listeners) draws effects from those notifications
  (`EffectSystems.cs:42-63`). **So an ambient shot that publishes the ordinary events is seen by every viewer, dumb ones included.**

```mermaid
graph TD
  subgraph Brain["Brain (or the editor)"]
    ORD["AmbientFire order<br/>scenario per instance, or a simple action node"]
  end
  subgraph Muscle["Muscle (or the editor)"]
    SYS["AmbientFireSystem<br/>timers, scan, approximate aim<br/>NO ballistics, NO hit detection"]
    POSE["TurretPose (Muscle-owned, W-9)"]
    FIRE["WeaponFireNotification"]
    DET["DetonationNotification<br/>at the fake impact point, Target = the hit entity or none"]
    DMG["DamageCalculationSystem (exists)<br/>+ SimpleDamage branch: damage = the shot's value"]
  end
  subgraph All["every viewer: map hosts, IG, external apps"]
    VIEW["the usual wire: WeaponFire, MunitionDetonation,<br/>TurretPose descriptor, Health"]
  end
  ORD -->|"order travels like an intent<br/>one sample per change"| SYS
  SYS --> POSE
  SYS --> FIRE
  SYS --> DET
  DET --> DMG
  POSE --> VIEW
  FIRE --> VIEW
  DET --> VIEW
  DMG -->|"DamageAssessedEvent, then Health"| VIEW
```

*What the picture shows that prose hid:* the ambient system sits where the real weapon muscle sits, so its shots, impacts, turret
movement and damage leave through the SAME senders as real combat. Nothing new appears on the wire, and a viewer cannot tell an
ambient shot from a real one. What it skips is everything expensive in between: the weapon brain, the turret limits and rates,
ballistics, projectile entities, raycasts, hit resolution and the armour model.

```mermaid
sequenceDiagram
  participant O as order (Brain)
  participant S as AmbientFireSystem (Muscle)
  participant D as DamageCalculationSystem (Muscle)
  participant V as every viewer
  O->>S: AmbientFire(target or point, interval, rounds, hit chance, damage per hit, scan)
  loop each frame
    S->>S: TurretPose = scan sweep, or a lagged turn toward the target
    S->>V: TurretPose (rate-limited)
  end
  S->>V: WeaponFireNotification (shot k)
  Note over S: hit or miss from the seed, impact point = the target or a miss offset beside it
  S->>V: DetonationNotification after the flight time, Target = the target if hit, else none
  S->>D: the same event, locally
  D->>V: DamageAssessedEvent(the shot's damage), then the target's Health drops
```

| # | question | ⭐ lean | rests on | rejected |
|---|---|---|---|---|
| **A-1** ✅ | where ambient fire lives | a **fourth path beside the three layers**: one `AmbientFire` order + one `AmbientFireSystem` (Fdp.Toolkits Combat); no weapon channel, no weapon brain, no weapon task | U-W3 (approved) | an "ambient" weapon-brain profile — it would still run the channel, the task and the muscle per entity |
| **A-2** ↻ | where it runs | ⭐ **on the Muscle** (the editor alone when it is one node), the node that already owns `TurretPose` (W-9), sends fire and detonation notifications and runs the damage step — so the ambient shot uses those senders unchanged; the order travels Brain → Muscle like an intent, one sample per change | `SimHostAuxiliaryTranslatorPack.cs:85-93` · BS-1 (Brain decides, Muscle executes) · U-W4 | ⛔ SUPERSEDED: computing the shots on every node with no wire traffic — dumb viewers would see nothing (U-W4) |
| **A-3** ↻ | the fake hit | the shot itself carries it: the system decides hit or miss from a seeded draw against the order's hit chance and publishes the ordinary `DetonationNotification` with `Target` = the hit entity (or none on a miss) and `Damage` = the order's damage per hit; the TARGET then handles it with an **extremely simplified damage model** — a `SimpleDamage` marker on entities playing ambient combat makes `DamageCalculationSystem` take the shot's damage as is (no armour, no penetration) | `DetonationNotification.Damage` exists (`DetonationNotification.cs:49`) · `DamageCalculationSystem` is the one damage step · U-W4 | ⛔ SUPERSEDED: the owner publishing a predetermined damage verdict with no detonation — the hit would not be visible as a hit |
| **A-4** ↻ | the turret | `TurretPose` written by the ambient system (a slow sweep in `Scan`, a lagged turn toward the target when firing; no limits, rates or ballistics) and **replicated through the ordinary turret-pose descriptor with dead reckoning (W-10)** — a sweep is one sample per reversal, so dumb viewers see it move. **Fixed turret** = no `TurretDto` in the TKB; **never fires** = no order | W-9 · U-W4 | ⛔ SUPERSEDED: skipping the pose egress for ambient entities |
| **A-5** ✅ | who sets the order | the **scenario** (per instance, saved) or a **simple action node** `AmbientEngage(target or point, duration)` any behaviour can call; an "ambient battle" generator is a behaviour handing out orders | approved | a weapon-channel request |
| **A-6** ↻ | what real AI makes of it | the events are the ordinary ones (U-W4), so real units hear and see ambient fire like any fire; ⭐ lean: no special flag on the wire (a dumb viewer could not use one either) — keeping real units out of a fake battle is a scenario matter (place, side, perception range) | U-W4 · R-207 hearing | ⛔ SUPERSEDED: an `IsAmbient` tag that hearing ignores — it would need a field on the wire messages |

⚠ **Cost at high count:** per entity per frame, one time compare and one pose update (sent at a capped rate). Per shot, the same two
wire messages a real shot sends (unavoidable if dumb viewers are to see it) and one cheap damage step on a hit. There are no
projectile entities, no raycasts, no ballistics and no armour model.
