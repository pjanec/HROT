<!--STATUS
state: LIVE
updated: 2026-10-25 (§5k barrel heat — CE-1358) · 2026-10-22 (§5f CE-3296 — a short throw is tossed, a throw nothing clears is refused) · 2026-10-20 (§5j fire pacing — CE-2323, R-2063)
build-state: BUILDING (WL-7 §5i the body turns to the shot BUILT 2026-10-14 — backend, W-14 / R-3003; WL-1a §5a, WL-1b §5b, WL-2a §5c, WL-2b §5d, WL-3 §5e built; WL-4 §5f building — backend; WL-6 §5h the weapon posture BUILT 2026-10-11 by the ui lane, W-13 / R-272) — ⭐ HANDED to the backend / behaviors lane 2026-10-11 (blueprints/batches/HANDOFF_Weapon_Layers_To_Backend_Or_Behaviors.md); the ui lane builds no more of it — W-1..W-9 approved (U-W2), §6 ambient fire approved as revised (U-W5), W-10 turret dead reckoning noted (U-W5: design only, no implementation yet); W-11 approved (U-W6): the task lives on the turret part
current-answer: §5k barrel heat (CE-1358) · W-14 + §5i the body turns to the shot (R-3003) · W-13 + §5h the weapon posture (R-272) · §1 the three layers (module diagram) · §2 classes · §3 sequences · §4 decisions W-1..W-9 (APPROVED), W-11 (APPROVED, supersedes W-2 per-mount), W-12 APPROVED · §5f WL-4 (backend, BUILT — the grenade at a hidden target, end to end; AQ89) · §5g WL-5 (W-12b/c, BUILT) · §5 slices · §6 ambient fire
stale-below: nothing yet
known-rot: none
known-conflict: DESIGN_Ownership_Groups_And_Grants.md F-6 says weapon-mount parts are never created — stale, CgfComponentRegistry.cs:38
  registers WeaponMountInfo and CombatTkbTranslator.cs:92-122 creates them on CGF. DESIGN_Decision_Layer.md :1330 calls
  AimAndFireExecutor "the only weapon-channel executor" — stale since FireAtPointExecutor (DESIGN_Building_Interiors.md §3k).
related-designs:
  - OVERVIEW_Rules_And_Demos.md — the INDEX of the rules and demos built across the behaviour designs (rule → what implements it → demo); this design OWNS the rows that cite it
  - designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md — poses its S2 turret and gun parts by TurretPose (W-9), so a round meets the turret where it is turned
  - docs/designs/indirect-fire/DESIGN_Indirect_Fire.md — OWNS mortars and artillery: the gun's fire solution (charges, low / high) beside the L-1 lob, and the first Mortar mounts
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — OWNS the weapon-system entities (placeable / mountable / carried / deployable weapons) and widens the weapon brain's mount list to the weapons a unit controls (WeaponSystems.ControllerOf, R-2016); the three layers and the one firing path stay here
  - docs/designs/air-attack/DESIGN_Air_Operations.md — OWNS the Release profile (the bomb cue) and the air attack profiles' weapon use under R-261 (R-2015)
  - docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md — BUILD design of W-6 / W-12d (ammo loadout chosen by the one ArmorModel), WL-5's timing rule turned into guidance (Command / FireAndForget, lock = AimSeconds), per-mount FireOnMove
  - docs/designs/vehicle-tactics/DESIGN_Urban_Armour_Tactics.md — urban vehicle tactics: names targets, asks for track-while-hidden; M8 gives a mount an ammo loadout (APFSDS + HEAT + HE) chosen by W-6's rule (unblocks W-12d); D-L adds a per-mount FireOnMove; M9 technicals (pintle HMG / ATGM mounts)
  - blueprints/Architect_Question_89_Suppress_And_Close.md — AQ89 (R-269): Suppress at a hidden target asks this file's weapon brain for BLIND fire; bravery/obedience (CombatTemperament) decides the close-to-throw
  - DESIGN_Map_3D_Mode.md — §3.11 M23–M26 (R-255): the turret is a part child with a TurretPose, the weapon side aims it, it
    replicates as a multi-instance descriptor. THIS file decides which weapon layer on which node does the aiming. ⭐ W-13 (R-272):
    its §3.5 block figure DRAWS this file's WeaponPosture (rifle in the hands, aim turn, run lean) — it owns the drawing, not the state.
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
  - DESIGN_Entity_Control.md — OWNS operator fire: AimAndFireExecutor's point target + operator mode (M-8, C-2)
  - docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md — the programme for later (CE-2162): LD-1 reuses W-14's body turn (HumanGait.FacingTurn) for a look direction, LD-2 points the vision cone along it
  - docs/DESIGN_Civilians_And_Crowd_Control.md — CV-6/CV-9: non-lethal warhead kinds and the thrown stone/Molotov reuse the grenade throw (W-12e/f).
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §5h (R-2025): the HANDS mount (throwables picked up, AmmoLoadout), W-13 posture graded by the CV-8 ladder, the one Armament rule.
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
| **W-8b** *(`CE-1307`, ui, `2026-10-23` — from the user's report; ⏳ for the user to confirm)* | the OPERATOR's aim outside the limits | ⭐ the operator's point (task `Point`, no target) is moved to the nearest reachable line at the same range before it becomes the task (`BallisticAim.Reachable` → `TurretSlew.ClampToArc`; `AimAndFireExecutor.OperatorPoint`): the turret traverses and stops at its limit, the muscle never fails, a shot leaves along the limit. ⛔ A BRAIN's aim is untouched — W-8 stands (it must fail so the profile can try another mount or the behaviour can move). Indirect mounts unchanged (their angles are a lob). 🔒 User, `2026-10-23`: *"the big gun mounted to the turret, it doesn't move at all. Doesn't matter where I'm aiming."* 📐 measured: the chase camera (CE-1293) looks down past the hull, the crosshair lands below the −8° depression ⇒ `Failed(OutOfArc)` every frame ⇒ the turret froze, traverse included | `TurretSlew.Step` already clamps elevation; `InArc` failed the task before it | W-8 for the operator too — a frozen gun; a new `WeaponTask` field — a wire change for a rule the brain side can apply |
| **W-8c** *(`CE-1359`, ui, `2026-10-25` — partly SUPERSEDES W-8b's "a shot leaves along the limit" for TRAVERSE)* | the OPERATOR's crosshair beyond a LIMITED traverse (a door gun's arc, CE-1356) | ⭐ the gun still turns to its limit (W-8b's `Reachable`), but while the crosshair is more than `BallisticAim.OperatorTraverseToleranceDeg` (3°) past the traverse edge the brain grants NO round (`BallisticAim.BeyondTraverse` → `AimAndFireExecutor` TurretPath: Track) and says `OutOfArc`; the station HUD asks the SAME rule and writes *"OUT OF ARC - the gun cannot turn there, fire held"* under the crosshair. ⛔ Unchanged: a full-circle turret (a tank turns to the point and fires when it bears), an elevation beyond the limits (the round leaves along the limit — W-8b's chase-camera case), a locked shot on a unit (the muscle's `OutOfArc` failure), indirect mounts. 🔒 User, `2026-10-25`: *"wepon should fire only if its barrel aiming point is close to the mouse driven aim point, otherwise refuses to fire (and writes some message on the HUD so user is not confused)"* · *"tank turret should keep working as now - it turns to the aiming cross/lock point and fires when it is reached"* | one rule, read by the brain and the HUD, so the message cannot disagree with the hold | the yellow-ring-to-crosshair screen distance — a presentation measure the brain node does not have; elevation too — would re-freeze W-8b's tank |
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
| two mounts share one turret | ✅ catalog Bradley: 25 mm and TOW both `TurretIndex = 0` (`NedTkbCatalog.cs`) | ✅ W-9 "two mounts share one turret" |
| a turret aims at ONE point at a time | ✅ one azimuth + one elevation (`TurretPose.cs`) | ✅ M23 "the turret and its gun are ONE assembly" |
| per-part ownership needs a PART entity | ✅ `SyncPartClaims` walks `PartMetadata` entities only (`OwnershipRecomputeSystem.cs:125`) | ✅ Ownership G-3 (a multi-instance descriptor covers every instance) |
| turret parts exist on every node; mount children on CGF only, and mount 0 is the hull itself | ✅ `TurretTkbTranslator` in the base set; `CgfComponentRegistry.cs:38`; `CombatTkbTranslator.cs:90` | ⛔ searched `docs/` + `.dev/`: no design puts mount parts on the Muscle |

⭐ **Ruled: one task per TURRET part** — `WeaponTask { Mode, Target/Point, MountIndex, RoundsGranted, AmmoGuid, Epoch }` and
`WeaponTaskStatus` on the turret part, keyed (hull, `2000 + turret`) like `TurretPose`; a hull-fixed mount (`TurretIndex = −1`, the
rifleman) keeps the per-shot path (W-2's simple profile). Blast radius: the WL-2/3 wire and the Muscle's dispatcher only — nothing built
changes. What would change it: a weapon that must aim two mounts on one turret at two targets at once (none in the catalog).
Rejected: per-mount tasks — two tasks for one turret need a tie-break nobody owns, and the Muscle has no mount entities to hold them.

### W-12 — ✅ APPROVED `2026-10-11` (U-W8: *"Approved."*) — what each category's profile does differently (⏸ the hold is LIFTED `2026-10-11`: the reorganisation landed, CE-3164; WL-4 = §5f)

> 🔒 **User, `2026-10-11` (U-W8):** *"Approved. Note that backend lane is now reorganizing behaviors so we should wait with any behavior
> changes."* ⇒ W-12a–f are the approved leans; WL-4 (the profiles, `CloseToThrow`, `ua-grenade-or-rifle`) is built only after that
> reorganisation lands — merge `backend` first and re-check the executor and `AttackApproachDecision` homes.

| # | question | ⭐ lean | rests on | rejected — one line |
|---|---|---|---|---|
| **W-12a** | when the mount is chosen | **once per engagement** (action entry, new target), with the same `WeaponChoice` / `WeaponSelectionDecision`; re-chosen only when that mount fails (OutOfArc, out of ammo) | ⚠ WL-3's known limit (§5e: a per-frame flip loses an unaccounted round) · `WeaponChoice.cs` (called per shot today) | per shot as today — flips the task's epoch mid-burst |
| **W-12b** | which categories use the turret task | TankGun, Autocannon, Atgm, and a MachineGun **on a turret** (coax); SmallArms, Grenade, Mortar and a hull MachineGun keep the per-shot path | U-W1 *"AimSeconds can stay for simplest weapon brains"* · W-5 | every turret mount alike (today's default) — an ATGM needs its own rule (below) |
| **W-12c** | the ATGM | grant **one round at a time**; the next only after the previous round's flight ended (its detonation), so the turret stays on target while a guided round flies | the muscle already tracks while granted rounds are 0 (Track) | firing a salvo — a guided weapon cannot guide two |
| **W-12e** *(U-W7, below)* | rifle or grenade at a HIDDEN target | the weapon brain: target **seen now** ⇒ the rifle (today); **not seen**, last-known position fresh in `TargetMemory`, within the grenade mount's range ⇒ **throw at that position** (the existing at-point path, `ArcLaunch` low); beyond it ⇒ hold, as today | Grenadier 2004: rifle mount 0, M67 mount 1, range 25 m (`UrbanCombatTkbCatalog.cs:95,181`) · `TargetMemory.PositionsX/Y/Z` (`PerceptionComponents.cs:53`) · R-261 (weapon choice is the weapon brain's) | the behaviour issuing `FireAtPoint` itself — every behaviour would re-implement the choice; ⚠ `DESIGN_Building_Interiors.md:1272` left "WHEN the AI throws" to the behaviors lane BEFORE R-261 split weapon choice out |
| **W-12f** *(U-W7)* | getting close enough | the behaviour's **Attack approach** gains a 4th option, **CloseToThrow** — a covered spot within grenade range of the last-known position — scored only for a unit carrying an indirect hand weapon; it competes with Flank / FiringPosition, so a rifleman flanks to SEE and a grenadier may close to THROW | `AttackApproachDecision.cs:33-46` (Direct / Flank / FiringPosition) · `ua-attack-approach` + `CE3084_U4` (the flank already works) | a new behaviour just for grenades — the choice belongs beside Flank in the one approach decision |
| **W-12d** | ammo per target | **not in WL-4**: one `AmmoGuid` per mount (W-6) until a mount declares a second round; the task already carries `AmmoGuid`, so it lands with no wire change | `WeaponSuiteDto.cs:86` (one ammo per mount) · W-6 | inventing AP/HE rounds now — no TKB data to choose between |

> 🔒 **User, `2026-10-11` (U-W7):** *"for the weapon brain logic there could be some interesting sample scenario demonstrating the
> choice of a rifle vs grenade if the target is hidden behind an obstacle and the attacker dare to come close enough (or flanks) to
> throw a grenade"* ⇒ W-12e/f, and the WL-4 demo scenario **`ua-grenade-or-rifle`** (lean): `bt-range`, a hostile crouched behind a
> 1 m wall; a Rifleman and a Grenadier start ~60 m out on the posture behaviour. Expected: both lose sight; the Rifleman flanks and fires
> the rifle once he sees; the Grenadier closes to < 25 m and throws at the last-known position; the hostile is hurt behind the wall
> (`bt-grenade-posture` measured: standing ≥ 0.5 exposure, prone ≤ 0.1). Rails: the cluster test asserts each one's mount and the
> hit; ⚠ unmeasured: whether the low throw arc clears a 1 m wall at 20–25 m (the apex is ~1.9 m by hand calculation — to be measured
> before the scenario is fixed).

⚠ **New behaviour vocabulary (part of W-1):** `TrackTarget(target)` on the weapon channel — keep the gun on a target without
firing — beside `AimAndFire` and `FireAtPoint`; releasing the channel sets the task to Idle.

### W-13 — the WEAPON POSTURE: at ease · ready · aimed, owned by the weapon brain *(🔒 user `2026-10-11` (U-W9), R-272; WL-6 §5h)*

> 🔒 **User, `2026-10-11` (U-W9):** *"the aiming/at ease needs to be weapon state controlled from the weapon brain. if not there we
> need to define and add"*

⛔ **Not there** (measured): the only aim state is the executor's private `AimState` timer and the diagnostic `ActionStatus.Weapon`
row, which is "never on the wire" (R-226); the animation stack's `LookAtChannel` is driven by no combat code and is not on DDS.
⇒ a soldier's 3-D figure had nothing to show (`DESIGN_Map_3D_Mode.md` §3.5: one standing pose, arms hanging, rifle floating).

```mermaid
graph TD
  subgraph Brain["Brain node (CGF, or the editor)"]
    EX["weapon brain executors<br/>AimAndFire · FireAtPoint · TrackTarget<br/>WeaponPostureOf.Report at every status point"]
    WD["WeaponDispatcherSystem<br/>no running action: WeaponPostureOf.Idle lowers it"]
    WP["WeaponPosture on the unit<br/>Brain group"]
    EG["EntityWeaponPostureEgressTranslator<br/>owner of dtWeaponPosture, on change"]
  end
  subgraph Every["every other node: SimHost, IG, Stride, the replay"]
    IN["EntityWeaponPostureIngressTranslator"]
    WP2["WeaponPosture (replica)"]
    FIG["EntityBodyLayer3D.FigurePose<br/>the block figure: rifle down / at the chest / shouldered toward the target"]
  end
  EX --> WP
  WD --> WP
  WP --> EG
  EG -- "EntityWeaponPosture (127)" --> IN
  IN --> WP2
  WP2 --> FIG
  WP -. "the editor: same world, no wire" .-> FIG
```

*What the picture shows that prose hid:* the posture is written in ONE place per cause — the executors' status points (the moments the
weapon brain already decides "aiming / firing / holding") and the dispatcher's idle branch — and read by nobody on the Brain; its only
reader is the picture, on every host. The editor reads it in its own world, so the networkless editor shows the same.

```mermaid
classDiagram
  class WeaponPosture { <<new, Brain group, id 359>> Kind AtEase-Ready-Aimed; Target; Point; IdleSeconds (never sent) }
  class WeaponPostureOf { <<new, Combat>> Report(unit, reason, target, point); Idle(unit, dt); LowerAfterSeconds = 5 }
  class AimAndFireExecutor { <<exists>> Say also reports the posture }
  class FireAtPointExecutor { <<exists>> Say also reports it, at its Point }
  class TrackTargetExecutor { <<exists>> Say also reports it }
  class WeaponDispatcherSystem { <<exists>> + Idle when no action runs }
  class BehaviorTkbTranslator { <<exists>> + provisions WeaponPosture beside WeaponChannel }
  class EntityWeaponPosture { <<new DDS, dtWeaponPosture 127>> EntityId key; Kind; TargetNetworkId; Point }
  class BlockFigure { <<exists, Map3D>> + arms in two parts; poses per WeaponPostureKind }
  class MotionPose { <<exists, Map3D>> + Lean (run tilt + aim pitch); Twist (aim yaw) }
  AimAndFireExecutor ..> WeaponPostureOf
  FireAtPointExecutor ..> WeaponPostureOf
  TrackTargetExecutor ..> WeaponPostureOf
  WeaponDispatcherSystem ..> WeaponPostureOf
  WeaponPostureOf ..> WeaponPosture : writes
  BehaviorTkbTranslator ..> WeaponPosture : adds at spawn
  EntityWeaponPosture ..> WeaponPosture : carries
  BlockFigure ..> WeaponPosture : reads
  BlockFigure ..> MotionPose
```

```mermaid
sequenceDiagram
  participant B as behaviour brain
  participant W as weapon brain (Brain)
  participant N as every other node
  participant F as 3-D figure
  B->>W: WeaponChannel AimAndFire(target)
  W->>W: target seen, aim timer running ⇒ Report(Aiming) ⇒ Aimed at target
  W->>N: EntityWeaponPosture(Aimed, target)
  N->>F: shouldered, upper body turned and pitched toward the target
  W->>W: target hidden ⇒ Report(HoldNotSeen) ⇒ Ready
  W->>N: EntityWeaponPosture(Ready)
  B->>W: action ends (Done)
  W->>W: Ready, then 5 s with no action ⇒ AtEase
  W->>N: EntityWeaponPosture(AtEase)
  N->>F: rifle carried low across the body
```

| # | question | ⭐ decision | rests on (code · design) | rejected — one line each |
|---|---|---|---|---|
| **W-13a** | the states | **AtEase** (rifle low across the body) · **Ready** (raised, not aimed: target hidden, reloading, throwing, just finished) · **Aimed** (shouldered at a target or point) | `ActionReason` already separates aiming from holding (`ActionStatus.cs:10-30`) · U-W9 | two states — a hidden target or a reload would snap the rifle down mid-fight |
| **W-13b** | who writes it | the weapon brain's executors, at the points they already report status, through ONE mapping (`WeaponPostureOf.Report`: Aiming / Firing / Cooldown / BlindFire / Hold-friendly / Hold-ROE / Slewing / Guiding ⇒ Aimed; HoldNotSeen / Reloading / Throwing / OutOfArc / Done / Failed / OutOfAmmo ⇒ Ready); the dispatcher lowers Aimed ⇒ Ready when no action runs and Ready ⇒ AtEase after `LowerAfterSeconds` (5 s) | every executor exit calls `Say` (`AimAndFireExecutor.cs:394`, `FireAtPointExecutor.cs:94`, `TrackTargetExecutor.cs:74`) · W-1 (the weapon brain keeps the gates) | reading `ActionStatus` — it is diagnostics and never on the wire (R-226) · deriving it on viewers from fire events — not the weapon brain (U-W9) |
| **W-13c** | where it lives | a component on the UNIT (a rifleman has no turret part), in the Brain ownership group, provisioned at spawn beside `WeaponChannel` | `HrotOwnershipGroups.cs` brain group · `BehaviorTkbTranslator.cs:140` | on a turret part — persons have none · inside `StanceIntent` — the animation contract, a different writer |
| **W-13d** | the wire | `dtWeaponPosture` = 127, keyed by the unit's network id, Reliable + TransientLocal + KeepLast 1, sent on change only (it changes a few times per engagement); the target crosses as its network id | `EntityWeaponTask` (`SimDescriptors.cs:118`), `SharedTranslatorPack.cs:93` (every node) | inside `dtBrainIntent` — a JSON slot written per change of task, SOP or ROE |
| **W-13e** | how it is drawn | each arm is an upper arm + a forearm; AtEase / Ready / Aimed rifle-and-hand poses for standing and crouched (prone is always aimed, dead unchanged); Aimed turns the upper body toward the target (≤ 75°) and pitches it (≤ 35°) — a soldier's body does not turn to face his target (⛔ SUPERSEDED by W-14 `2026-10-14`: the gait now turns an aimed soldier and the shot waits for him — CE-3195); an ARMED soldier's arms do not swing (they hold the rifle); running tilts the upper body forward (12° at a full run) | `ShapeKit.cs` `BlockFigure` · `MotionTracker.cs` | turning the whole body — the heading is the Muscle's (movement), not the picture's |

#### W-13e — the ACTIVE MOUNT on the posture (ui handoff P4, `2026-10-15`)

```mermaid
classDiagram
  class WeaponPosture { <<component, Brain group>> Kind · Target · Point · IdleSeconds · +ActiveMount byte ★ }
  class EntityWeaponPosture { <<dtWeaponPosture = 127>> Kind · TargetNetworkId · Point · +ActiveMount ★ (APPENDED, R-169) }
  class WeaponPostureOf { <<the ONE writer>> Report · Idle · +SetActiveMount(world, unit, mount) ★ }
  class AimAndFireExecutor { <<existing>> the mount it resolved this frame (Auto choice, a named mount, the primary) ★ }
  class FireAtPointExecutor { <<existing>> its mount (32 + k for a controlled weapon) ★ }
  AimAndFireExecutor ..> WeaponPostureOf
  FireAtPointExecutor ..> WeaponPostureOf
  WeaponPostureOf --> WeaponPosture
  WeaponPosture ..> EntityWeaponPosture : egress on change
```

*What the picture shows: the active mount rides the descriptor that already crosses the wire on every posture change, written by the same
executors at the moment they resolve which mount fires — the map shows the launcher on the shoulder BEFORE the first shot.* The value is the
unit's mount index (0 = its primary; 1… its own mounts; 32 + k a weapon system it controls); it keeps its last value when the weapon is lowered.

**As built (`2026-10-15`)** — `AimAndFireExecutor` writes it the moment the mount resolves (Auto choice / named / primary);
`FireAtPointExecutor` the moment the controlled index resolves — BEFORE any hold, so a gunner held on his station's arc still names the
weapon. `SetActiveMount` writes only on change and only where the unit has a `WeaponPosture`. Rails:
`AimAndFireExecutorTests.P4_…` (the pickup's station M2 = 32; a gunner's tube = 32; kept when lowered) ·
`EntityTurretPoseTranslatorTests.WL2b_…` (it crosses the wire).

### W-14 — the body turns to the shot: a fire arc, and a body that follows a lasting aim *(🔒 user `2026-10-14` (U-W10), R-3003; CE-3195; WL-7 §5i)*

> 🔒 **User, `2026-10-14` (U-W10):** *"I noticed the soldier when firing does not turn his body into the firing direction … i have
> seen it going away from the target and still shooting at him, basically shooting behind his own back."* — on the lean: *"Approved.
> Firing with high angle difference should be possible (like quick fire on suddenly appearing target) but i think The motion should
> try to align the body smoothly to the firing direction if that situation (target behind) lasts longer."*

#### INVENTORY *(graph CLI + grep, `2026-10-14`; the MCP server was down — the CLI ran `search_graph` / `search_code`; ⚠ `check_index_coverage` is not in the CLI)*

| query | result |
|---|---|
| `search_graph name_pattern=.*(Aim\|Facing\|Heading\|Orient\|LookAt\|Yaw).* label=Class` | 49 — the aim side is `AimAndFireExecutor` (+`AimState`), `BallisticAim`, `AimPoint`; the look side is the animation `LookAt*` channel (drawing only, unplayed — CE-3010); no facing class |
| `search_graph .*Weapon.*(Executor\|Dispatcher\|System\|Channel).*` · `.*(Locomotion\|Kinematic).*(Dispatcher\|System\|Executor).*` | `WeaponDispatcherSystem`, `WeaponTaskDispatcherSystem` · `CarKinematicsSystem`, `LinearKinematicsSystem`, `LocomotionDispatcherSystem` |
| writers of a person's heading *(grep `Rotation =` / `FromYaw` / `CreateFromAxisAngle(UnitZ`)* | `CarKinematicsSystem.FinishMove` (from `HumanGait.Integrate`, which turns only while moving, and `ManualMotion.Person`, the operator's) · `BulletCharacterMotor` on the Stride host (from velocity) · `EmbarkedStateSystem` (a passenger takes the vehicle's) — ⛔ nothing writes it from the aim |
| readers of `WeaponPosture` | `EntityBodyLayer3D.TryAim` (the drawn twist, clamped `MaxAimTwist` 75°) · the NED translators · `WeaponPostureOf` — ⛔ no kinematics reader |
| fire gates on angle | the turret path only (`Failed(OutOfArc)`, W-8) — the per-shot path (`AimAndFireExecutor.cs:123-213`) and `FireAtPointExecutor.ShootAtPoint` have none |
| sight | every shipped soldier sees 360° (`UrbanCombatTkbCatalog.cs:170`) — the sight cone cannot stop it |

#### Classes — ⭐ what the picture shows that the prose hid: ONE angle limit and ONE aim point, read by the shot, the gait and the drawing

```mermaid
classDiagram
  class WeaponPosture {
    <<component, replicated>>
    Kind : AtEase Ready Aimed
    Target : Entity
    Point : Vector3
  }
  class WeaponPostureOf {
    <<static, existing>>
    Report(world, unit, reason, target, point)
    +TryAimPoint(view, unit, out at) bool
  }
  class BodyAim {
    <<static, NEW, Combat>>
    +OffBody(view, unit, at) float
    +HoldsFire(world, shooter, at) bool
  }
  class HumanGait {
    <<static, existing, R-247>>
    TurnRate
    +MaxAimTwist 75 deg
    +AlignGain 1 per s
    +AlignDeadband 3 deg
    +FacingTurn(fwd, toAim, dt) float
    +FacingSpeedFactor(fwd, travel) float
    +IntegrateFacing(pos, fwd, state, wanted, moving, toAim, accel, dt, out travel) float
  }
  class ManualMotion {
    <<static, existing, M-2>>
    BackAndStrafeFactor 0.6
  }
  class AimAndFireExecutor {
    <<weapon brain, existing>>
    Execute() per-shot path + FireBlind
  }
  class FireAtPointExecutor {
    <<weapon brain, existing>>
    ShootAtPoint(..., obeyRoe, obeyArc)
  }
  class CarKinematicsSystem {
    <<muscle, existing>>
    UpdateVehicle() person branch
  }
  class BulletCharacterMotor {
    <<Stride host muscle, existing>>
  }
  class EntityBodyLayer3D {
    <<drawing, existing>>
    TryAim() twist clamp
  }
  AimAndFireExecutor ..> BodyAim : holds a round
  FireAtPointExecutor ..> BodyAim : holds a round
  AimAndFireExecutor ..> WeaponPostureOf : Report Turning = Aimed
  WeaponPostureOf --> WeaponPosture : writes
  BodyAim ..> HumanGait : MaxAimTwist
  CarKinematicsSystem ..> WeaponPostureOf : TryAimPoint
  CarKinematicsSystem ..> HumanGait : IntegrateFacing
  BulletCharacterMotor ..> WeaponPostureOf : TryAimPoint
  BulletCharacterMotor ..> HumanGait : FacingTurn
  HumanGait ..> ManualMotion : BackAndStrafeFactor
  EntityBodyLayer3D ..> WeaponPostureOf : TryAimPoint
  EntityBodyLayer3D ..> HumanGait : MaxAimTwist
```

#### Sequence — ⭐ what it shows: the shot waits for the body, and the body is turned by the Muscle, never by the brain

```mermaid
sequenceDiagram
  participant X as AimAndFireExecutor (Brain node)
  participant P as WeaponPosture (replicated)
  participant K as CarKinematicsSystem (Muscle node)
  participant T as SimTransform heading (replicated)
  X->>X: target seen, aim clock running
  X->>X: BodyAim.HoldsFire: target 120 deg off the body
  X->>P: Report(Turning) = Aimed at target, no round spent
  P->>K: Aimed + target
  K->>K: off by more than MaxAimTwist: turn at TurnRate (snap)
  K->>T: heading
  T->>X: next frame: 70 deg off, inside the arc
  X->>X: aim clock done: WeaponFireIntent (a snap shot)
  loop while the engagement lasts
    K->>K: inside the arc: turn at AlignGain x angle (smooth)
  end
  X->>P: action ends: Ready (not Aimed)
  K->>K: HumanGait again: walks where he faces (turn first when more than 30 deg off)
```

#### Modules — ⭐ who calls each piece every frame (no new system, no new wire, no dead edge)

```mermaid
graph TD
  subgraph CGF["Brain node (CGF)"]
    WD["WeaponDispatcherSystem<br/>runs the executors"] --> AF["AimAndFireExecutor / FireAtPointExecutor<br/>+ BodyAim.HoldsFire"]
  end
  subgraph SH["Muscle node (SimHost)"]
    GK["GroundKinematicsModule"] --> CK["CarKinematicsSystem<br/>person: HumanGait.IntegrateFacing"]
  end
  subgraph ST["Stride host"]
    BM["BulletCharacterMotor<br/>HumanGait.FacingTurn"]
  end
  subgraph VW["every viewer"]
    BL["EntityBodyLayer3D<br/>twist clamp = MaxAimTwist"]
  end
  AF -- "dtWeaponPosture (existing, on change)" --> CK
  AF -- "dtWeaponPosture" --> BM
  AF -- "dtWeaponPosture" --> BL
  CK -- "heading (existing attribute)" --> AF
```

| # | question | ⭐ decision | rests on (code · design) | rejected — one line each |
|---|---|---|---|---|
| **W-14a** | may he fire at a target off his body | yes, up to **`HumanGait.MaxAimTwist` (75°)** each side of his heading — a snap shot. Beyond it the round is HELD (`ActionReason.Turning` ⇒ Aimed, nothing spent) while he turns; the aim clock keeps running, so the shot comes the moment he is round. Every aimed round of a PERSON: the per-shot path, blind fire, `FireAtPoint`'s AI path. Not: operator fire (the operator turns the body, M-2), the turret path (its own arc, W-8), an embarked passenger (the vehicle's heading, `IsEmbarkedTag`), a non-person | the drawn twist is already 75° (W-13e, `EntityBodyLayer3D.cs:173`) ⇒ one constant, picture and shot agree · `PhysicsColliderReaders.IsPerson` | no arc (today: fire behind the back) · restart the aim clock on the turn — kills the quick shot the user asked for · a hit-chance penalty by angle instead — still a round out of his back |
| **W-14b** | who turns the body | the **Muscle's person gait** (R-247 — the heading is movement's), reading the replicated `WeaponPosture` (Aimed + target / point) through ONE helper `WeaponPostureOf.TryAimPoint` that the drawing already used | `WeaponPosture` reaches every node (W-13d) · R-158 / R-169: no new message | the brain writes the heading — a second writer of a Muscle-owned component · a new facing channel / message — the posture already says it · the animation LookAt channel — drawing only, unplayed (CE-3010) |
| **W-14c** | how fast | beyond the arc: **`TurnRate`** (an about-face in 0.5 s); inside it: **`AlignGain` × angle** (1 /s — 63 % of the way in 1 s, 95 % in 3 s), nothing inside `AlignDeadband` (3°). A brief snap shot barely moves him; a lasting aim brings the body round — U-W10 | `HumanGait.TurnRate` · a first-order lag needs no timer, so no new component | a dwell timer, then turn — needs per-unit state on the Muscle for the same picture · always snap — a flick at every new target |
| **W-14d** | moving while aimed | travel and facing separate: he keeps his path; the body follows W-14c; the "turn first when > 30° off" rule is off; speed × **1** within 45° of the body, **`ManualMotion.BackAndStrafeFactor` (0.6)** at 90° and beyond (back-pedal, sidestep). When the aim ends the gait rule returns — more than 30° off ⇒ he stops, turns, walks on | `ManualMotion.Person` already moves a person off his heading (M-2) · the user approved option ① (face the threat, walk back / sideways) | stop, turn, fire, move on — option ②, not chosen · keep the gait and only hold fire — he never fires on the move away |
| **W-14e** | standing | the same rule: an aimed soldier who stands turns to his target | `HumanGait.Integrate` turned only while moving (`HumanGait.cs:54`) | — |
| **W-14f** | the Stride host | `BulletCharacterMotor` faces the same wanted heading (`HumanGait.FacingTurn` toward the aim point), else travel as today | the one rule, two integrators (ruling 9 — one implementation of the rule) | leave it facing velocity — the Stride host would still shoot behind its back |


## 5. Slices

| slice | builds | shows |
|---|---|---|
| **WL-1** | TKB `TurretDto` + `WeaponMountDto.Category/TurretIndex/MuzzleOffset`; turret part children on every node; `TurretPose` + its multi-instance descriptor; the 3-D map's `poseOf` reads it | the drawn turret follows a pose set by hand on the Muscle |
| **WL-2** | `WeaponTask` / `WeaponTaskStatus` descriptors; the muscle's `WeaponTaskDispatcher` with **Track**; `BallisticAim` | a tank keeps its gun on a moving target |
| **WL-3** | **Engage** on the muscle through `FireProcessingSystem`; the executor split + the default profile; rounds granted / fired; `TrackTarget` on the channel | a tank slews, then fires 3 rounds; behaviours unchanged |
| **WL-4** | category profiles (tank gun, autocannon, ATGM, mortar, small arms); ammo choice | per-category gunnery |
| **WL-6** | W-13: `WeaponPosture` written by the weapon brain, `dtWeaponPosture` to every node, the block figure posed by it (elbows, rifle in the hands, aim, run tilt) — §5h | a soldier carries his rifle low, raises it when the target hides, shoulders it toward the target when he aims |
| **B5** ✅ | `Muzzle.Of` from the posed barrel — the round, the flash and the tracer from one point (built, DESIGN_Visual_Effects §6b) | VE-K |

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

### 5c. WL-2a — the weapon muscle keeps the gun on a target (Track) *(✅ built 2026-10-11, as drawn; W-11 / R-267)*

WL-2 is split like WL-1: **WL-2a** = the task and status components on the turret part, the Muscle's dispatcher with **Track**,
`BallisticAim` and the slew — runnable on one node (the editor) by writing a task by hand; **WL-2b** = the task and status on the
wire (Brain → Muscle, Muscle → Brain), the `TurretPose` pattern twice.

```mermaid
graph TD
  subgraph Muscle["Muscle node — SimHostCoreLogicPack (SimHost, and the editor as its one node)"]
    CM["CombatModule.InputSystems"]
    D["WeaponTaskDispatcherSystem<br/>(before FireProcessingSystem)"]
    F["FireProcessingSystem (WL-3 fires through it)"]
    CM -->|"each frame"| D
    CM -->|"each frame"| F
  end
  T["turret part: WeaponTask (Brain group) · WeaponTaskStatus (Muscle group) · TurretPose"]
  D -->|reads| T
  D -->|writes pose + status| T
```

*What the picture shows that prose hid:* the dispatcher is reached each frame only where `CombatModule` is composed — the same
composition gate `FireProcessingSystem` relies on (`FireProcessingSystem.cs` CE-198 note: no authority gate, one node schedules it). A
Brain or IG never runs it, so it needs no runtime authority check of its own.

```mermaid
classDiagram
  class WeaponTask { <<new component 352, Brain group>> Mode; Target; Point; MountIndex; RoundsGranted; AmmoGuid; Epoch }
  class WeaponTaskStatus { <<new component 353, Muscle group>> Epoch; State; Reason; RoundsFired }
  class WeaponTaskDispatcherSystem { <<new, Muscle, Input phase>> Idle holds; Track slews }
  class BallisticAim { <<new static>> Solve(world, hull, turret, mount, aimPoint) azimuth elevation in the hull frame }
  class TurretSlew { <<new static, pure>> Step(pose, desired, TurretDto, dt); InArc }
  class AimPoint { <<exists>> + Middle(world, target) — the target's mid-silhouette, shared with FireProcessingSystem }
  class CombatTkb { <<exists>> + TurretOf(world, hull, index) }
  class TurretPose { <<exists, WL-1>> }
  class TurretDto { <<exists TKB, WL-1>> pivot, limits, rates }
  WeaponTaskDispatcherSystem ..> WeaponTask : reads
  WeaponTaskDispatcherSystem ..> WeaponTaskStatus : writes
  WeaponTaskDispatcherSystem --> BallisticAim
  WeaponTaskDispatcherSystem --> TurretSlew
  WeaponTaskDispatcherSystem ..> TurretPose : writes
  BallisticAim ..> AimPoint : Middle
  BallisticAim ..> TurretDto : pivot
  TurretSlew ..> TurretDto : limits and rates
  WeaponTaskDispatcherSystem ..> CombatTkb : TurretOf, MountOf
```

```mermaid
sequenceDiagram
  participant D as WeaponTaskDispatcherSystem
  participant A as BallisticAim
  participant S as TurretSlew
  participant P as turret part
  D->>P: read WeaponTask (Track, target, epoch 5)
  D->>A: Solve(hull pose, turret pivot, target middle)
  A-->>D: azimuth, elevation (hull frame), in arc?
  alt out of the turret's limits
    D->>P: status Failed(OutOfArc), pose holds
  else in arc
    D->>S: Step(pose, desired, rates x dt)
    S-->>D: new pose
    D->>P: TurretPose, status Slewing or OnTarget (within 0.5 deg)
  end
```

| choice | why | rejected — one line each |
|---|---|---|
| angles solved in the **hull frame** (inverse hull rotation) | a hull on a slope tilts its turret plane; `TurretPose` is relative to the hull (M23) | world bearing minus yaw — wrong on any slope (R-248) |
| direct fire = the line pivot → target middle; indirect (the mount's warhead `Indirect`) = `FireProcessingSystem.ArcLaunch` | W-8; the same arc the round will fly | a second ballistic model |
| aim point = `AimPoint.Middle` (one helper, `FireProcessingSystem` routed onto it) | the round and the gun aim at the same point | the dispatcher computing its own aim height |
| full-circle traverse wraps the short way; a limited arc slews inside it | `TurretDto` −180…180 = all round | always the short way — would swing a limited turret through its dead zone |
| out of limits ⇒ `Failed(OutOfArc)`, pose holds | W-8 — the profile tries another mount or the behaviour moves | clamping silently — the brain would think it is aimed |
| `OnTarget` within 0.5° of both angles | a status the Brain can gate the shot on (WL-3) | exact equality — never reached at a finite rate |

⚠ **As built (WL-2a):** `WeaponTaskFailure`'s zero member is `NoFailure`, not `None` — the DDS code generator emits one IDL scope
per file and two enums with a `None` member collide there. `FireProcessingSystem` now takes the target's middle from
`AimPoint.Middle` (the same three calls it inlined before), so the round and the turret aim at one point by construction.

### 5d. WL-2b — the task and its report on the wire *(✅ built 2026-10-11)*

```mermaid
sequenceDiagram
  participant B as Brain (owns dtWeaponTask)
  participant M as Muscle (owns dtWeaponTaskStatus, dtTurretPose)
  participant V as every node
  B->>M: EntityWeaponTask (hull net id, 2000+turret) mode, target net id, point, mount, rounds, ammo, epoch
  M->>M: target net id to its own entity, WeaponTaskDispatcherSystem slews
  M->>V: EntityTurretPose (WL-1b)
  M->>B: EntityWeaponTaskStatus (same key) epoch, state, reason, rounds fired
```

```mermaid
classDiagram
  class EntityWeaponTask { <<wire, Reliable TransientLocal KeepLast 1>> [Key] ParentNetworkId; [Key] PartId; Mode; TargetNetworkId; Point; MountIndex; RoundsGranted; AmmoGuid; Epoch }
  class EntityWeaponTaskStatus { <<wire, Reliable TransientLocal KeepLast 1>> [Key] ParentNetworkId; [Key] PartId; Epoch; State; Reason; RoundsFired }
  class TurretPartKey { <<egress helper>> TryResolve(part, descriptor) hull net id, part id, owner gate }
  class TurretPartLookup { <<ingress helper>> Find(map, hull net id, part id); IsRecordedOwner }
  class EntityTurretPoseEgressTranslator { <<WL-1b, routed onto TurretPartKey>> }
  class EntityTurretPoseIngressTranslator { <<WL-1b, routed onto TurretPartLookup>> }
  EntityWeaponTask ..> TurretPartKey
  EntityWeaponTaskStatus ..> TurretPartKey
  EntityTurretPoseEgressTranslator ..> TurretPartKey
  EntityTurretPoseIngressTranslator ..> TurretPartLookup
```

*What the picture shows that prose hid:* the three turret-part topics share ONE key rule (`TurretPartKey` out, `TurretPartLookup` in)
— the pose translators were routed onto it, so the key cannot drift between them. Ownership: `dtWeaponTask = 125` in the Brain group
(`WeaponTask`), `dtWeaponTaskStatus = 126` in the MuscleGround group (`WeaponTaskStatus`), both registered in `SharedTranslatorPack`
(Brain and Muscle both run it; a pure IG needs neither). A target the Muscle does not know arrives as `Entity.Null` with no point,
so the muscle reports `NoTarget` instead of aiming at the origin.

### 5e. WL-3 — Engage: the brain grants rounds, the muscle fires them *(✅ built 2026-10-11 — as drawn, plus the notes below)*

```mermaid
sequenceDiagram
  participant X as AimAndFireExecutor (Brain, weapon brain)
  participant P as turret part
  participant D as WeaponTaskDispatcherSystem (Muscle)
  participant F as FireProcessingSystem (Muscle)
  X->>X: target alive, rounds asked, mount (choice), ammo, magazine, ROE, seen-now
  alt seen, ROE permits, magazine ready
    X->>P: WeaponTask Engage, RoundsGranted = fired so far + rounds left in the magazine (capped by the rounds asked)
  else not seen, ROE holds, or reloading
    X->>P: WeaponTask Track (same epoch — the gun keeps following)
  end
  D->>P: slew, OnTarget
  D->>D: cooldown over, RoundsFired below RoundsGranted, no friendly on the line
  D->>F: WeaponFireIntent(hull, target, mount)
  D->>P: status Firing, RoundsFired + 1
  X->>X: status RoundsFired grew by n: Magazine.Spend n times, rounds fired + n
  X->>X: rounds asked reached or target dead: Success, task Idle
```

```mermaid
classDiagram
  class AimAndFireExecutor { <<exists, split>> profile.UsesTurretTask(mount) ? turret path : per-shot path (unchanged) }
  class IWeaponBrainProfile { <<new>> UsesTurretTask(world, unit, mount) }
  class DefaultWeaponBrainProfile { <<new>> true when the mount names a turret the unit has }
  class WeaponBrainProfiles { <<new static table>> For(category) — WL-3 registers the default for every category }
  class WeaponTask { <<WL-2>> + CooldownSeconds }
  class WeaponTaskDispatcherSystem { <<WL-2>> + Engage: fire when on target, granted, cooled, line clear }
  class WeaponMuscleState { <<new local component on the turret part>> CooldownRemaining }
  class LineOfFire { <<exists>> BlockedByFriendly — the ONE check, called by both paths (W-7) }
  AimAndFireExecutor --> WeaponBrainProfiles
  WeaponBrainProfiles --> IWeaponBrainProfile
  DefaultWeaponBrainProfile ..|> IWeaponBrainProfile
  AimAndFireExecutor ..> WeaponTask : writes
  WeaponTaskDispatcherSystem ..> WeaponMuscleState : cooldown
  WeaponTaskDispatcherSystem ..> LineOfFire
  AimAndFireExecutor ..> LineOfFire : per-shot path only
```

| choice | why | rejected — one line each |
|---|---|---|
| `RoundsGranted` is **cumulative per epoch**; a Track ↔ Engage switch keeps the epoch, a new target or mount bumps it | the brain accounts the DELTA of `RoundsFired`; the muscle never re-fires after a mode flip | a new epoch per grant — the counter resets and a round in flight is double-counted or lost |
| the grant = rounds left in the **magazine** (capped by the action's rounds) | W-4: logistics stay Brain-side; a reload withdraws the grant (Track) and re-grants after it | granting the whole ammo load — the muscle would fire through a reload it knows nothing of |
| the **cooldown** travels in the task (`CooldownSeconds`, the action's parameter); the muscle keeps the remaining time in a LOCAL `WeaponMuscleState` | fire timing is the muscle's (W-1); the number is the behaviour's, as today | a TKB rate of fire — not every mount declares one, and the action already carries it |
| seen-now / ROE / magazine gate **Engage vs Track** on the Brain; the friendly line is checked by the muscle at the shot (W-7) | the seen-now memory is Brain state; the shot's moment is the Muscle's | the muscle reading `TargetMemory` — W-7 rejected |
| no AimSeconds on the turret path — the slew is the aim | U-W1 *"AimSeconds can stay for simplest weapon brains not modelling turrets"* | both — a turreted gun would wait twice |
| `Failed(OutOfArc)` → the action fails (Failure); `NoTarget` with a dead target → Success | the behaviour moves or picks another target; WL-4's profiles may try another mount first | silently holding — a behaviour would wait forever |

⚠ **As built (WL-3):**
- the muscle's fire intent is marked `IsRemote = true` — it is already at the muscle, and the only reader of that flag on a fire intent
  (`WeaponFireIntentEgressTranslator`) must not forward it, or a Brain+Muscle node would fire the round twice;
- `TrackTargetExecutor` (`ActionIdTrackTarget = 3` on the weapon channel) is registered in `CgfLogicPack`; ⚠ **its BTree / Blueprint
  node is NOT built** — exposing it to behaviour authors is the behaviors lane's vocabulary (as `FireAtPoint` was, `FireAtPointBehavior`);
- two diagnostic reasons, `Slewing` and `OutOfArc`, appended to `ActionReason` (the gizmo shows "slewing" / "out of arc");
- ⚠ **known limit:** `Mount = Auto` re-chooses the mount every frame (`WeaponChoice`); a flip to a mount on the SAME turret starts a new
  epoch, and a round the muscle fired under the old one but the brain had not yet accounted is not spent on the brain. WL-4's
  profiles choose the mount once per engagement, which removes it.

### 5f. WL-4 — the grenade at a hidden target, end to end *(backend, `2026-10-11`; build-state: BUILT — cluster rail green)*

> 🔒 **User, `2026-10-11`:** *"Other lanes are on hold. We should take the grenade scenario end to end."* ⇒ the backend lane builds
> W-12a, W-12e, W-12f, the `TrackTarget` node and `ua-grenade-or-rifle` (handoff
> [`HANDOFF_Weapon_Layers_To_Backend_Or_Behaviors.md`](blueprints/batches/HANDOFF_Weapon_Layers_To_Backend_Or_Behaviors.md)).
> W-12b/c (turret task per category, ATGM one round at a time) are not needed by the scenario — built next, §5g.

#### INVENTORY *(graph CLI + grep, `2026-10-11`)*

| query | result |
|---|---|
| graph `.*WeaponChoice.*`, `.*WeaponSelection.*`, `.*AimAndFire.*`, `.*FireAtPoint.*`, `.*TrackTarget.*` (Class) | `WeaponChoice` (`Combat/WeaponChoice.cs:13`, one per-shot caller: `AimAndFireExecutor.cs:94`) · `WeaponSelectionDecision` · `AimAndFireExecutor` · `FireAtPointExecutor` + `FireAtPointNodes` + `FireAtPointBehavior` · `TrackTargetExecutor` (action 3, CGF) — ⛔ no `TrackTarget` node |
| graph `.*Approach.*`, `.*Flank.*`, `.*Throw.*`, `.*Grenade.*` | `AttackApproachDecision` (`Approach` Direct 1 · Flank 2 · FiringPosition 3), `FindFlankingPosition`, `FindOpenFiringPosition`, `EqsTacticsNodes.Flank/FiringPosition`, `PostureNodes.ApproachSensors` · ⛔ no throw/grenade type (searched) |
| grep `TargetMemory` in `Combat/` | ⛔ none — nothing throws at a remembered position; the precedent for a remembered point is `PeekAndFireNodes`' blind burst (`:537-547`, `EqsTacticsNodes.ThreatPosition`) |
| grep `HasIndirect\|HandWeapon\|HasGrenade` | ⛔ none — no utility input says "carries a throwable" |
| walls on `bt-range` | 0.5 m Low Wall (100–120, 40) · 1.2 m sandbags (145–153, 40) · ⛔ no 1 m wall |

#### The low throw does NOT clear a wall at close range — measured *(the U-W7 open point)*

`ArcLaunch` low arc, 15 m/s (M67), aim point on the ground, height of the round above a wall standing **k m in front of the aim point**:

| throw distance | eye 1.7 m: k = 2 · 3 · 4 | eye 1.1 m (crouched): k = 2 · 3 · 4 |
|---|---|---|
| 12 m | 0.73 · 1.02 · 1.27 | 0.63 · 0.88 · 1.09 |
| 16 m | 0.87 · 1.23 · 1.55 | 0.81 · 1.14 · 1.42 |
| 20 m | 1.10 · 1.57 · 2.00 | 1.08 · 1.54 · 1.94 |

⇒ a man crouched 1–3 m behind a 1–1.2 m wall is **unreachable by the low arc** from 12–16 m — exactly where `CloseToThrow` takes the
thrower. A real thrower lobs. ⭐ Lean **L-1** below.

```mermaid
classDiagram
  class AimAndFireExecutor { <<exists, extended>> per-shot path · W-12a mount once · W-12e not seen ⇒ HiddenTargetThrow.TryThrow }
  class HiddenTargetThrow { <<NEW, Combat>> TryThrow(world, unit, target, dt) bool · ThrowableMount(world, unit, out mount) · LastKnown(world, unit, target, out point) }
  class WeaponChoice { <<exists>> Choose — now called once per engagement }
  class AimState { <<exists, extended>> + ChosenMount, HasChoice (its two padding bytes) }
  class FireAtPointExecutor { <<exists>> }
  class FireProcessingSystem { <<exists, extended>> L-1: low arc blocked ⇒ the high arc (lob) }
  class ThrowArc { <<NEW, Combat>> LowArcClear(terrain, from, to, speed) bool }
  class TargetMemory { <<exists>> PositionsX/Y/Z, Freshness }
  class ThreatFreshness { <<exists>> IsLive }
  class CarriesThrowable { <<NEW utility input>> 1 when an indirect hand mount has ammo }
  class AttackApproachDecision { <<exists, extended>> + Approach.CloseToThrow = 4 }
  class FindThrowingPosition { <<NEW EQS template>> donut around the LAST-KNOWN point, hidden from it, near it }
  class EqsTacticsNodes { <<exists, extended>> + CloseToThrow (anchored at the last-known point) }
  class PostureNodes { <<exists, extended>> ApproachSensors + a third sensor }
  class CombatPostureBehavior { <<exists, extended>> + IsOption(CloseToThrow) branch }
  class TrackTargetNodes { <<NEW>> TrackTarget shared node + curated TrackTarget behaviour }
  class TrackTargetExecutor { <<exists, action 3>> }
  AimAndFireExecutor --> WeaponChoice : once
  AimAndFireExecutor --> HiddenTargetThrow : not seen
  HiddenTargetThrow ..> TargetMemory
  HiddenTargetThrow ..> ThreatFreshness
  HiddenTargetThrow ..> FireProcessingSystem : WeaponFireIntent AtPoint
  FireProcessingSystem --> ThrowArc
  AttackApproachDecision ..> CarriesThrowable
  AttackApproachDecision ..> FindThrowingPosition : EqsTopScore
  CombatPostureBehavior --> EqsTacticsNodes : CloseToThrow
  CombatPostureBehavior --> PostureNodes
  TrackTargetNodes ..> TrackTargetExecutor : weapon channel action 3
```
*What the picture shows that prose hid: the throw is decided in ONE place (the weapon brain), the arc in ONE place (the muscle), and
the behaviour only moves the thrower — no behaviour issues `FireAtPoint` for a grenade.*

```mermaid
sequenceDiagram
  participant B as CombatPosture (behaviour brain)
  participant D as AttackApproachDecision
  participant E as EqsTacticsNodes.CloseToThrow
  participant X as AimAndFireExecutor (weapon brain)
  participant H as HiddenTargetThrow
  participant F as FireProcessingSystem (muscle)
  B->>D: target hidden, carries a throwable
  D-->>B: CloseToThrow wins (EqsTopScore of FindThrowingPosition)
  B->>E: move to a covered spot near the last-known point
  B->>X: AimAndFire(target, Mount=Auto) (fire while moving)
  X->>X: mount chosen once (W-12a), target not seen now
  X->>H: TryThrow
  H->>H: throwable mount ready, last-known point live, in range, ROE, no friendly in the lethal radius
  H->>F: WeaponFireIntent AtPoint = last-known, mount = grenade
  F->>F: low arc blocked by the wall ⇒ high arc (L-1)
  H-->>X: thrown: the round counts, the grenade mount cools down
```

```mermaid
graph TD
  CGF["CGF node (Brain)"] --> WD["WeaponDispatcherSystem — every frame"]
  WD --> AF["AimAndFireExecutor (action 1)"]
  WD --> TT["TrackTargetExecutor (action 3)"]
  AF --> HT["HiddenTargetThrow (static)"]
  HT -->|"WeaponFireIntent AtPoint"| EG["WeaponFireIntentEgressTranslator"]
  EG -->|"dtWeaponFireRequest"| SH["SimHost (MuscleGround)"]
  SH --> FP["FireProcessingSystem — every frame"]
  FP --> TA["ThrowArc.LowArcClear"]
  CGF --> BT["BrainTickSystem — CombatPosture"]
  BT --> EQ["EqsTacticsNodes.CloseToThrow"]
  EQ --> EQS["EQS solver (FindThrowingPosition)"]
```
*Who calls it each frame: the throw runs inside the weapon dispatcher on the Brain; the arc choice inside FireProcessingSystem on the
Muscle — on `--mode editor` both are the same world, on the cluster the request crosses the wire as every shot does today.*

#### Decisions *(within the approved W-12 leans)*

| # | question | ⭐ lean | rests on (code · design) | rejected — one line |
|---|---|---|---|---|
| **L-1** | the low throw cannot clear the wall | the **muscle** lobs: `FireProcessingSystem` samples the low arc against the terrain; blocked ⇒ the high arc (in reach whenever the low one is) | table above · W-8 *"elevation is the muscle's"* · `TerrainPenetration.Carry` already stops arc rounds (`BallisticsSystem.cs:156`) | always lob a hand throw — changes `bt-grenade-posture`'s measured flight for no reason; placing the thrower 20 m out — the EQS would need a 1-m band |
| **L-2** | W-12e's conditions | not seen now · a throwable mount (indirect warhead, hand speed < 40 m/s) with ammo and cooled · the target's slot in the unit's `TargetMemory` live (`ThreatFreshness.IsLive`, not anonymous) · flat distance ≤ the mount's `Range` and the arc's reach · ROE · **no friendly (and not the thrower) inside the warhead's lethal radius of the point** | `ThreatDanger.cs:89` · `WarheadDto.BlastLethalRadiusM` · R-200 (ROE once) | the friendly LINE check — exempt for indirect fire already (`FireAtPointExecutor.cs:64`); the danger is the burst, not the line |
| **L-3** | how often | one throw, then the grenade mount cools **6 s** (fuze 4.5 s + a look) — on the mount's own `WeaponState`, which the dispatcher already drains | `WeaponDispatcherSystem.cs:55` · R-239 | the action's `CooldownSeconds` — the rifle's 1 s would empty four grenades in four seconds |
| **L-4** | W-12a — where the choice lives | the chosen mount in `AimState`'s two padding bytes (the channel state is full at 32 B); re-chosen on a new target, an empty mount, or `OutOfArc` | `BehaviorConstants.ActionStateByteSIze = 32` · §5e known limit | a new component per unit — state of one action belongs in the action |
| **L-5** | the planner must not see through walls | `CloseToThrow`'s sensor is anchored at the **last-known point** (`ContextPoint1`), never the live entity | `EqsContext.cs:59-75` · W-7 (memory is Brain state) | the live entity — the thrower would walk to where the hidden man IS, which he cannot know; ⚠ `Flank` does this today — filed, not changed here |
| **L-6** | `CloseToThrow` scoring | `HaveLiveTarget` (Step) × `ThreatInSight` (Inverse) × `CarriesThrowable` (Step) × `EqsTopScore(FindThrowingPosition)`, the last with exponent 0.6 (weights are exponents in a weighted product, ≤ 1 by rule UT0131; v^0.6 > v) — so a grenadier with a reachable covered spot prefers to throw, a rifleman (score 0) flanks | W-12f · `AttackApproachDecision.cs:33-46` | a separate grenade behaviour — W-12f rejected it |
| **L-7** | the scenario wall | `bt-range`'s existing **1.2 m sandbags** (145–153, 40); the hostile crouched 2.5 m behind them (a crouched man's head is 1.1 m — hidden) | `bt-range.world.geojson` · premise "sandbags stop the rifle" | a new 1 m wall — a terrain change and new premises for a number the sandbags already give |
| **L-8** | `TrackTarget` vocabulary | a shared node `TrackTargetNodes.TrackTarget` + a curated `TrackTarget` behaviour (`Library/Basic`), the `FireAtPoint` precedent | `FireAtPointBehavior.cs` · §5e "its node is NOT built" | a Blueprint channel catalog entry now — palette goldens; left as `FireAtPoint` left it |

#### ⭐ What the scenario found, and how it was closed — AQ89 *(R-269)*

📌 Measured on the cluster: an equally armed hidden hostile makes the posture **Suppress**, not Advance. The approach (and so
`CloseToThrow`) is nested under the Advance branch, and Suppress at a hidden target held until the memory faded. ⇒ the Grenadier
never threw. ✅ Fixed by [AQ89](blueprints/Architect_Question_89_Suppress_And_Close.md) §4, with no change to the posture scores:
- Suppress asks the weapon brain for **blind fire** (`AimAndFireParams.BlindFire`). The weapon brain throws if it can (W-12e above), else
  fires at the last-known point, else fires aimed once he is seen.
- A **willing** grenadier closes inside the Suppress branch, running the same `CloseToThrow` with its own state. Willing means a
  `CombatTemperament` (bravery / obedience, defaults 0.5 / 0.8) at or above 0.6.
- The scenario authors the Grenadier brave (`"Temperament": {"riskAcceptance": 0.8}`).

#### ⭐ `CE-3296` — a short throw is TOSSED, and a throw nothing clears is REFUSED *(backend, `2026-10-22`; behaviors' [note](blueprints/batches/NOTE_Behaviors_To_Backend_Thrown_Grenades_At_A_Doorway.md); build-state: BUILT)*

📐 **Measured by behaviors** (`bc-room-clear`, AssaultRiflemen 2009 at a Suspected room): ① the drill's stun grenade, a 2–3 m throw into a
doorway whose low arc was judged blocked, was lobbed at the full 15 m/s — apex ≈ 11 m, flight ≈ 3 s — and its 1.5 s fuze burst it 12 m up;
② the weapon brain's hidden-target M67 hit the jamb and burst at the thrower's feet (143 to himself).

```mermaid
sequenceDiagram
    participant M as muscle: FireProcessingSystem
    participant A as ThrowArc.TryLaunch
    participant L as brain: ShotLedger
    M->>A: a throw (below HighArcMinMuzzleVelocity), no charge
    A->>A: the low arc at full speed — clears? done
    A->>A: ★ else the SOFTEST TOSS that clears: the slowest reaching speed × 1.05 … 3.2, low then high
    A->>A: else the full-speed lob — clears? done
    A-->>M: false: nothing clears
    M-->>L: ★ refused, Masked (no round) — the weapon brain's TryThrow already waits on a held weapon
```
*What it shows that prose hid:* the check lives where the geometry is (R-2037, Q94 A) — the brain never asks whether a throw clears; the
muscle answers, and the ledger the brain already reads turns the answer into a hold.

| ★ | rule | rejected (one line) |
|---|---|---|
| the toss | the slowest speed that reaches, v² = g·(h + √(d² + h²)), × 1.05 … 3.2, each low then high, before the full-speed lob | a fixed soft speed — a 15 m throw needs more than a 3 m one |
| the refusal | a throw no arc clears is refused `Masked` on the muscle | behaviors' lean "the weapon brain checks the arc before it throws" — that is a geometry read on the brain (R-2037); the refusal reaches it through the ledger instead |
| the fuze | ★ a TIME-fused round takes no arc that outlasts its fuze (`maxFlightSeconds` = the round's fuze); none left ⇒ refused | measured on behaviors' check: after the toss fix a stun grenade was still lobbed at full speed and burst 12.4 m up |
| the friend check | unchanged, round the TARGET point — a throw that flies now clears, so it stops at the point | a friend check at a computed stop point — only needed for a throw that does not clear, which no longer flies |

⚠ **Not fixed here:** why the drill's low arc was judged blocked although its 2-D line crosses the doorway 0.1 m inside the jambs — an
origin (eye / barrel) offset, or the open leaf. Every arc lies in one vertical plane, so if that plane meets the jamb no toss clears and
the throw is now REFUSED (no burst at the feet). Rails: `ThrowArcTests.CE3296_AShortThrowOverCover_IsASoftToss_NotAFullSpeedLob`
(red-proved), `CE3296_AThrowNothingClears_IsReportedSo`, `CE3296_ATimeFusedRound_NeverTakesAnArcThatOutlastsItsFuze` (red-proved). 📐 **behaviors' check** (`CE2257` with the squad on 2009 and the room Suspected, two runs, before the fuze rule): the drill's stun now bursts on the room's floor (45.3, 107.8, 0.0); no M67 on the jamb; one lob still burst 12.4 m up ⇒ the fuze rule. The twin's remaining reds (③ an insurgent up, ④ no RoomClear) involve no grenade damage — behaviors'.

### 5g. WL-5 — W-12b/c: which categories use the turret task, and the ATGM one round at a time *(✅ built `2026-10-11`, as drawn; backend; 🔒 user: *"Approved, add the Rifleman check then W-12b/c"*)*

#### INVENTORY *(graph + grep, `2026-10-11`)*

| query | result |
|---|---|
| graph `.*(WeaponBrainProfile\|UsesTurretTask\|TurretPath\|WeaponTask).*` | **one** profile: `DefaultWeaponBrainProfile` (`WeaponBrainProfiles.cs:21`), returned by `For(category)` for EVERY category · the brain's turret path `AimAndFireExecutor.TurretPath` · the muscle `WeaponTaskDispatcherSystem` · the wire pair `EntityWeaponTask(Status)` |
| grep `WeaponCategory.` in the TKB catalogs | Abrams `TankGun` turret 0 · Bradley `Autocannon` + `Atgm` (TOW) on turret 0 · infantry `SmallArms` (no turret) · Grenadier mount 1 `Grenade` (no turret). ⛔ no `MachineGun` / `Mortar` mount yet |
| what says a round's flight ENDED | ⛔ nothing reaches the brain: `WeaponTaskStatus` = {Epoch, State, Reason, RoundsFired} · `BallisticProjectile` carries the `Shooter`, not the mount · projectiles live on the muscle |
| free state in the brain's 32-byte weapon channel | ⛔ full (target 0–7 · `AimState` 8–23 · rounds 24–27 · accounted 28–31) — ⭐ but `AimState.Elapsed` (the per-shot aim clock) is unused on the turret path |

#### The decision *(within the approved W-12b/c leans)*

| claim | code — how it IS | design basis |
|---|---|---|
| every category goes through the turret task when the mount names a turret | `WeaponBrainProfiles.For` returns `Default` (`WeaponBrainProfiles.cs:33`) | W-12b lists the categories; WL-3 left the table for WL-4 |
| a guided round is granted with the magazine, so a TOW salvo leaves on the cooldown | `TurretPath`: `grant += inHand` (`AimAndFireExecutor.cs`) | W-12c: *"a guided weapon cannot guide two"* |
| the brain cannot see the missile land | INVENTORY rows 3–4 | W-7 (projectiles are muscle state) |

```mermaid
classDiagram
  class IWeaponBrainProfile {
    <<existing>>
    +UsesTurretTask(world, unit, mount) bool
    +RoundsInFlight int  new
  }
  class TurretWeaponProfile {
    <<renamed from DefaultWeaponBrainProfile>>
    TankGun, Autocannon, MachineGun, Unknown
  }
  class PerShotWeaponProfile {
    <<new>>
    SmallArms, Grenade, Mortar
    UsesTurretTask false
  }
  class GuidedWeaponProfile {
    <<new>>
    Atgm
    RoundsInFlight 1
  }
  class WeaponBrainProfiles {
    <<existing table>>
    +For(category) IWeaponBrainProfile
  }
  class AimAndFireExecutor {
    <<existing>>
    -TurretPath() grants one guided round per estimated flight
  }
  IWeaponBrainProfile <|.. TurretWeaponProfile
  IWeaponBrainProfile <|.. PerShotWeaponProfile
  TurretWeaponProfile <|-- GuidedWeaponProfile
  WeaponBrainProfiles ..> IWeaponBrainProfile
  AimAndFireExecutor ..> WeaponBrainProfiles
```
*What the picture shows: the table stays the one place a category is answered (W-5); the ATGM is a turret profile with one rule more,
not a fourth weapon path. A hull machine gun needs no rule: it names no turret.*

```mermaid
sequenceDiagram
  participant X as AimAndFireExecutor.TurretPath (Brain)
  participant P as turret part
  participant D as WeaponTaskDispatcherSystem (Muscle)
  X->>P: Engage, RoundsGranted = accounted + 1 (guided)
  D->>D: on target, cooldown over: launch
  D->>P: status RoundsFired + 1
  X->>X: RoundsFired grew: spend it, hold = distance / muzzle velocity
  loop while hold > 0
    X->>P: Track, RoundsGranted = accounted (the launcher stays on the target)
  end
  X->>P: Engage, RoundsGranted = accounted + 1
```
*Who runs it each frame: unchanged from §5e — the brain on the Brain node's weapon dispatcher, the muscle on SimHost's.*

| # | lean | rests on | rejected — one line |
|---|---|---|---|
| **G-1** | `For`: SmallArms / Grenade / Mortar ⇒ **per-shot** even if a turret is named; Atgm ⇒ **guided**; TankGun / Autocannon / MachineGun / **Unknown** ⇒ the turret profile (today's rule) | W-12b · Unknown keeps today's behaviour, so no untagged mount changes silently | Unknown ⇒ per-shot — every untagged turret mount would change path |
| **G-2** | a guided mount is granted **one round**, and the next only after the round's **estimated flight** (flat distance to the target ÷ the mount's muzzle velocity, +0.5 s) has run out since the brain saw it fired | W-12c · R-212 (expected values, no dice) · the muscle already Tracks while nothing is granted (§5e) | a `RoundsLanded` field on `WeaponTaskStatus` — a wire/IDL change for what the brain can estimate; the muscle holding launches while a projectile of the shooter flies — the projectile has no mount, so the coax would block the TOW |
| **G-3** | the hold lives in `AimState.Elapsed`, unused on the turret path | INVENTORY row 4 | a new component per unit — state of one action belongs in the action (L-4's rule) |

⚠ **Known limit (G-2):** an estimate, not the detonation itself — a round stopped early by cover still holds the launcher for the full
estimate. Stated, not hidden.

| as built | |
|---|---|
| `WeaponBrainProfiles`: `TurretWeaponProfile` (was `DefaultWeaponBrainProfile`, no other user) · `PerShotWeaponProfile` · `GuidedWeaponProfile : TurretWeaponProfile` · `IWeaponBrainProfile.RoundsInFlight` | as G-1 |
| `AimAndFireExecutor.TurretPath(…, roundsInFlight, mount, dt)` · `GuidedFlightSeconds` (public, so the rail checks the estimate) · `ActionReason.Guiding` ("guiding") while the hold runs | as G-2/G-3 |
| ⚠ the hold shares `AimState.Elapsed` with the per-shot aim clock | harmless while a vehicle's mounts all sit on turrets (the Bradley's do); a unit mixing a guided turret mount and a per-shot mount on ONE target could start its aim with a leftover hold — filed here, not built |
| rails | `AimAndFireExecutorTests.WL5_W12b_TheProfileTable` (8 categories) · `WL5_W12b_ASmallArmsMountOnATurret_FiresPerShot` · `WL5_W12c_AnAtgm_FiresOneRoundPerFlight` (3 rounds ≥ 1 s apart on a 0.5 s cooldown) |

### 5h. WL-6 — W-13: the weapon posture *(✅ built `2026-10-11` by the ui lane, as drawn in W-13; R-272)*

| piece | as built | where |
|---|---|---|
| the state | `WeaponPosture { Kind, Target, Point, IdleSeconds }`, component 359, `NoScenario`, Brain group; provisioned at spawn beside `WeaponChannel` | `Combat/Components/WeaponPosture.cs`; `BehaviorTkbTranslator.cs`; `HrotOwnershipGroups.cs` |
| the writer | `WeaponPostureOf.Report` from each executor's `Say` (AimAndFire, FireAtPoint at its point, TrackTarget); `WeaponPostureOf.Idle` in `WeaponDispatcherSystem` whenever no action runs (and when the unit cannot shoot); `LowerAfterSeconds` = 5 | `Combat/WeaponPostureOf.cs`; the three executors; `WeaponDispatcherSystem.cs` |
| the wire | `dtWeaponPosture` = 127, `EntityWeaponPosture { EntityId (key), Kind, TargetNetworkId, Point }`; egress gated on owning `(127, 0)`, on change; ingress skips the recorded owner and drops an unknown unit. ⭐ **DEVIATION found by the cluster rail:** a PURE IG does not run the shared pack's ingress (`NedReplicationModule`'s `!pureIg` guard) — it reads through `EntityStatesIngressPack`, so the ingress is registered there too (the turret pose's precedent, WL-1b); the first run showed Aimed on CGF and SimHost and AtEase on the IG | `SimDescriptors.cs`; `EntityWeaponPosture*Translator.cs`; `SharedTranslatorPack.cs`; `EntityStatesIngressPack.cs`; `NedOwnershipGroupBinding.cs` |
| the figure | arms point to point (shoulder → elbow → hand); per posture, standing and crouched, the rifle and both hands; prone always on the rifle; `MotionPose.Lean / Twist / HipZ` move the `Upper` parts about the hip; `EntityBodyLayer3D.TryAim` (turn ≤ 75°, pitch ≤ 35°); run lean 12° (`MotionTracker.RunLean`) | `Map3D/ShapeKit.cs`, `MotionTracker.cs`, `EntityBodyLayer3D.cs` |

⭐ **Rails:** `AimAndFireExecutorTests.WL6_*` (the weapon brain writes it; every status maps) · `Map3DTests.WL6_*` (hands on the rifle,
elbows joined, civilian swing, run lean, aim turn/pitch/clamp) · frame rail `Map3DFrameRail.WL6_SoldiersHoldTheirRiflesInTheWeaponBrainsPosture`
· cluster `GrenadeOrRifleScenarioTests` asserts the Rifleman's posture is Aimed on CGF, SimHost and IG, aimed at the hostile on IG.
⭐ **WL-6b (`2026-10-11`, user: *"Yes, build the posture blend"*) — the picture TIMES a posture change; nothing new is sent.**
`PostureBlend` (beside `DeathFall`, `Map3D/ShapeKit.cs`) remembers each soldier's last posture and when it changed (SIM time): the
rifle and hands move between the two poses over 0.3 s, eased (`BlockFigure.Parts(stance, soldier, from, to, t)` — every part keeps its
role, so the legs keep swinging); a change mid-blend starts from the nearer end; a posture change during a stance blend blends both. The
aim turn and pitch ease toward the aim with a 0.12 s time constant and back to 0 when no longer aimed. A pause freezes both; a unit first
seen (load, late host, replay jump) is simply in its posture, and a clock going back snaps. Rejected: replicating a transition progress
like `StanceStatus` — the stance needs one because the Muscle plays it and the Brain waits for it; nothing waits on a rifle being raised.
Rails: `Map3DTests.WL6b_*`.

⭐ **WL-6c (`2026-10-11`, user: *"can we use 'modern athletic rifle fighting stance' when the weapon is aimed and duck-walk when aimed
and the soldier is moving? soldiers are not usually standing straight in close quarters and direct engagement"*) — the legs get KNEES.**
Each leg is a thigh + shin, the knee solved from hip and ankle (two-bone IK, `BlockFigure.Knee`). **Aimed, standing still ⇒ the
athletic fighting stance**: hips 0.10 m lower, knees bent, support foot forward and firing-side foot back, upper body leaning ~10° into
the rifle (`AthleticLean`). **Aimed and moving ⇒ the combat glide**: feet under the lowered hips, knees bent, steps 45 % shorter
(`BlockFigure.Glide`, weight = aimed × stride). **Every walker's shin bends back as its leg swings through** (`MotionPose.KneeFlexL/R`
from the gait phase) — civilians too. All of it follows the posture blend, so a soldier SINKS into the stance as he raises the rifle.
⛔ **Drawing only:** the LOGICAL stance stays Standing — the hit model, shot heights and perception body points do not change
(Buildings §3f: an animation shows the stance, never gates it); the move speed is unchanged (the Muscle's, not the picture's).
Rails: `Map3DTests.WL6c_*`, frame rail `posture-3-athletic-and-glide`.

⭐ **WL-6d (`2026-10-11`, user: *"the prone pose is not bad; the crawling motion would be nice"*) — the CRAWL.** A prone figure that
moves was a statue sliding at 20 % speed (`StanceSpeed`); now it crawls: an elbow-and-knee combat crawl, one cycle per 0.7 m crawled
(`MotionTracker.CrawlMetres`, its own phase — a crawl is too slow to read as a walk, so its weight is `MotionPose.Moving`, from speed
alone). The elbows take turns reaching forward and pulling (lifted as they come forward, out wide so they read from above), the
OPPOSITE knee draws up and out to the side, the body stays flat with a small shimmy; a soldier carries the rifle in his right hand —
never aimed while crawling — and blends back into his prone pose (unchanged — *"not bad"*) as he stops (`BlockFigure.Crawl` /
`CrawlPose`). Drawing only: stance and speed unchanged. Rails: `Map3DTests.WL6d_*`, frame rail `posture-6/7-crawl`.

⚠ **Not built:** a vehicle's posture is written but nothing draws it (its turret has `TurretPose`); the Stride skeletal aim layer could
read the same component — not wired.

### 5i. WL-7 — W-14: the body turns to the shot *(backend, `2026-10-14`; CE-3195, R-3003; build-state: BUILT — as drawn in W-14)*

| piece | as built | where |
|---|---|---|
| the twist | ONE constant, `HumanGait.MaxAimTwist` (75°); the drawn twist `EntityBodyLayer3D.MaxAimTwist` is now an alias of it | `HumanGait.cs` · `EntityBodyLayer3D.cs:175` |
| the aim point | `WeaponPostureOf.TryAimPoint` (Aimed ⇒ the target's chest or the point) — moved out of `EntityBodyLayer3D.TryAim`, which now calls it | `WeaponPostureOf.cs` |
| the fire arc | `BodyAim.HoldsFire` (a person, not embarked, more than the twist off) holds: the per-shot path (before the friendly-line check), blind fire (turning toward the REMEMBERED point), and `FireAtPointExecutor.ShootAtPoint(…, obeyArc)` — required argument: the AI passes `true`, operator fire `false` | `BodyAim.cs` · `AimAndFireExecutor.cs` · `FireAtPointExecutor.cs` |
| the reason | `ActionReason.Turning` (appended — 17), label "turning", posture **Aimed** | `ActionStatus.cs` · `WeaponPostureOf.For` |
| the gait | `HumanGait.FacingTurn` (snap beyond the twist, `AlignGain` × angle inside, 3° dead band) · `FacingSpeedFactor` · `IntegrateFacing` (moves along the way, not the heading); `CarKinematicsSystem`'s person branch uses them when `TryAimPoint` answers — standing too — else `HumanGait.Integrate` exactly as before | `HumanGait.cs` · `CarKinematicsSystem.cs` |
| the Stride host | `BulletCharacterMotor` turns an aimed character by `FacingTurn` from its body's current heading (less the model offset), else faces travel as before | `BulletCharacterMotor.cs` |

Gates `2026-10-14`: `Fdp.Toolkits.Tests` 3225 / 0 (1 skip) · `Hrot.Presentation.Tests` Map3D 139 / 0 (8 skip) · cluster scenarios (`PostureScenarioTests`, `GrenadeOrRifleScenarioTests`, `SquadTacticsScenarioTests`, `EqsCombatNodesTests`) 31 / 0 — no baseline moved · `Hrot.Stride.Core` builds clean, ⚠ its tests cannot run on Linux (net8.0-windows runtime) and the motor's aimed branch has no rail — verify on the Windows host.

⚠ Known, not fixed here: blind fire's ROUNDS-in-progress posture still names the hidden target ENTITY (W-13b), so between held rounds the gait turns toward where he really is, not where he was last seen — the gate itself uses the remembered point. Small in practice (the point is where he was a moment ago).

### 5j. Fire pacing — each man's own rhythm, seeded *(behaviors, `2026-10-20`; `CE-2323`, R-2063; build-state: BUILT — as drawn)*

> 🔒 **User, `2026-10-20`:** *"Soldiers when asked to shoot they shoot in sync unisono probably not having any kind of reproducible
> randomization to desync the phasing — needed"* · *"Yes i want also different gap on automatic weapons"*

```mermaid
classDiagram
  class FirePacing {
    <<static, Fdp.Toolkit.Combat — NEW>>
    +AimFactor(shooter, target) float
    +Gap(interval, shooter, round, action) float
  }
  class SimRng {
    <<existing, CE-2188>>
    +FromSim(index, salt, time)
  }
  class AimAndFireExecutor {
    <<existing — the brain's ONE aimed-shot step>>
    aim clock: AimSecondsFor x AimFactor
    per-shot gap and blind gap: Gap
  }
  class WeaponTaskDispatcherSystem {
    <<existing — the muscle's turret clock (WL-3)>>
    each round's gap: Gap
  }
  FirePacing ..> SimRng : seeded draws
  AimAndFireExecutor ..> FirePacing
  WeaponTaskDispatcherSystem ..> FirePacing
```
*What the picture shows that prose hid: every round in the sim is timed at exactly TWO places, one per layer — the brain's per-shot
path and the muscle's turret clock — so ONE helper called from both varies every weapon, automatic ones included; no behaviour
needs to know.*

| what | rule | why |
|---|---|---|
| the first shot | the mount's aim time × `AimFactor` ∈ [0.7, 1.6), drawn once per (shooter, target) | the opening volley was one frame for every man ordered on the same tick |
| every later gap | the interval (asked, or the mount's own 60 / rate of fire — a cyclic rate too) × `Gap` ∈ [0.8, 1.25), drawn per round (shooter, round, action) | a fixed identical gap keeps men locked in step for ever, once in phase |
| reproducible | `SimRng.FromSim(shooter index, salt, 0)` — no time input: the same scenario run gives the same rhythm | CE-2188 (user 2026-09-04): looks random, the same every run |
| untouched | operator fire (a station pulls the trigger itself), the proficiency factors (DA-2 — this sits on top) | the person decides; the skill tier is a separate scale |

**Rejected:** jitter in each play — every play must remember, and an order that skips a play fires in unison; staggering the order
dispatch by entity index — it offsets the first shot only, identical gaps keep them locked.

### 5k. Barrel heat — a gun fires at its cyclic rate until it is too hot *(ui, `2026-10-25`; `CE-1358`; cross-lane: `Fdp.Toolkits` Combat + Perception, `Hrot.Network.NED`; build-state: BUILT — as drawn)*

> 🔒 **User, `2026-10-25`:** *"automatic gun also needs to cool down after it gets too hot … gun fires at its fast automatic rate
> untit it gets too hot and that cooldown blocks netx bursts"* · *"barrel heat needs to be modelled also for single shot guns, because
> of thermal view simulation"*

**INVENTORY** *(2026-10-25)*: `search_graph name_pattern=".*(Heat|Overheat|Barrel).*" label=Class` → **total 6**, none a barrel model
(`FiringPositionMemory.HeatRules`/`PositionHeat` = a firing position's exposure, `ThermalHeatSystem` = the body's heat CE-3061, two
heat-map renderers). grep `Magazine.Spend|Magazine.Ready` → every fire path asks `Ready` and every round leaves through `Spend`
(AimAndFire ×3, FireAtPoint, the turret grant, HiddenTargetThrow, Countermeasures, ShotLedger.Answer, StandardInputs).
⇒ nothing to reuse but `Magazine`'s shape, which this rule mirrors.

```mermaid
classDiagram
  class BarrelHeat {
    <<static, Fdp.Toolkit.Combat — NEW: the ONE heat rule>>
    +ResumeFraction = 0.5
    +For(mount) (perRound, coolSeconds)
    +Load(ref WeaponState, mount)
    +Fire(ref WeaponState)
    +Tick(ref WeaponState, dt)
    +IsOverheated(WeaponState) bool
    +RoundsLeft(WeaponState) int
  }
  class Magazine {
    <<existing, CE-3136 P-5>>
    +Spend() one round leaves ⇒ BarrelHeat.Fire
    +Ready() ⇒ and not overheated
  }
  class WeaponState {
    <<existing, per mount — appended>>
    +Heat 0..1 (1 = too hot)
    +HeatPerRound
    +HeatCoolPerSecond
    +Overheated latch
  }
  class WeaponMountDto {
    <<existing TKB — appended>>
    +HeatPerRound (0 = by category)
    +BarrelCoolSeconds (0 = by category)
  }
  class WeaponDispatcherSystem {
    <<existing — drains cooldown, reload>>
    +BarrelHeat.Tick every mount, every frame
  }
  class ThermalHeatSystem {
    <<existing, CE-3061>>
    ThermalState.BarrelHeat = hottest barrel of the unit
  }
  class WeaponAmmo {
    <<existing, replicated (EntityWeaponAmmo)>>
    +Hot bitmask, Heat0..3 percent
  }
  WeaponMountDto ..> BarrelHeat : For (spawn, CombatTkbTranslator)
  Magazine ..> BarrelHeat
  BarrelHeat ..> WeaponState
  WeaponDispatcherSystem ..> BarrelHeat
  ThermalHeatSystem ..> WeaponState : reads Heat
  WeaponAmmo ..> WeaponState : mirrored for the station HUD
```
*What the picture shows that prose hid: every round in the sim leaves through `Magazine.Spend` and every fire path asks
`Magazine.Ready` first — the per-shot path, the at-point path, the turret grant, the thrown and dispensed rounds. ⇒ ONE rule beside
the magazine heats and blocks EVERY weapon, AI and operator alike, with no executor knowing.*

```mermaid
sequenceDiagram
  participant Op as OperatorControlBehavior
  participant X as AimAndFireExecutor (brain)
  participant M as WeaponTaskDispatcherSystem (muscle)
  participant W as WeaponState (BarrelHeat)
  participant D as WeaponDispatcherSystem
  Op->>X: trigger held, CooldownSeconds 0 = the mount's cyclic rate
  X->>W: Ready? (magazine and not overheated)
  X->>M: WeaponTask Engage, RoundsGranted capped by RoundsLeft
  M-->>X: rounds fired (status)
  X->>W: Magazine.Spend per round ⇒ Heat += HeatPerRound
  Note over W: Heat reaches 1 ⇒ Overheated latched
  X->>X: say Overheated, grant nothing (Track)
  D->>W: every frame Heat -= HeatCoolPerSecond x dt
  Note over W: Heat at or below 0.5 ⇒ the latch opens, the next burst may fire
```
*What the picture shows: the muscle never needs to know the barrel — the brain's grant stops at the round that makes it too hot, so a
burst cannot overshoot by the rounds already granted.*

| the rule | value | why |
|---|---|---|
| ⭐ one round heats the barrel by `HeatPerRound` (1 = too hot); it cools linearly to cold in `BarrelCoolSeconds` from too hot | — | a level, not a round counter: the thermal sensor reads the same number |
| too hot (`Heat ≥ 1`) **latches**: no round until it cools to `ResumeFraction` (0.5) | latch | 🔒 *"that cooldown blocks next bursts"* — without the latch it would trickle one round per cooled tick |
| default for an AUTOMATIC weapon (MachineGun, Autocannon, SmallArms) | per round = 1 ÷ (cyclic rounds/s × 4 s); cools in 12 s | at the cyclic rate ~6 s of fire from cold, then ~6 s silent; an AI firing at its deliberate gaps never reaches it |
| default for a SINGLE-SHOT gun (TankGun, Howitzer, Mortar, Rocket, Atgm, Sam, Unknown) | per round 0.15; cools in 60 s | 🔒 *"also for single shot guns … thermal view"* — the barrel is warm for ~9 s after a round and stacks over a fight; at a catalogue 6/min it never locks |
| no barrel | Grenade, Bomb, Countermeasure, a person's Hands | as `ThermalHeatSystem` already says: a dispenser and a hand heat no barrel |
| the operator | `CooldownSeconds` 0 = the mount's own cyclic rate (`CooldownFor`); 0.25 s only for a mount stating no rate | ⛔ before: a fixed 0.25 s — an M134 fired 9 rounds in 2 s against its 3000/min (measured live) |
| thermal | `ThermalState.BarrelHeat` = the hottest barrel of the unit (its own mounts and the station weapons it carries); `Signature` uses `max(Heat, BarrelHeat)` | the body's heat and the gun's are two sources; the sensor sees the hotter |
| the station HUD | `WeaponAmmo.Hot` bit + `Heat0..3` percent (appended to `EntityWeaponAmmo`, like `LaidDown`) | the gunner sees *"HOT 72%"* / *"OVERHEATED"* on the weapon line |

⚠ **One round per tick at most:** the muscle's clock resets to the next gap, it does not carry a remainder, so a cyclic rate above the
tick rate fires at the tick rate (an M134 at 60 Hz: 50/s fits). Not changed here.

**Rejected:** heat on the muscle's `WeaponMuscleState` — only the turret path has it, the per-shot path and the thermal sensor would
need a second copy; a rounds-in-a-burst counter — it cannot cool partially and gives the thermal sensor nothing; a separate
`Overheated` refusal from the muscle — every round of an overheated burst would round-trip a refusal, where `Ready` holds at the ask.

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
