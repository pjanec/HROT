<!--STATUS
state: LIVE
updated: 2026-10-14
build-state: READY-TO-BUILD — 🔒 user, 2026-10-14: "Leans accepted. Write the designs" (D-D, D-I, D-L)
current-answer: §2 ammo loadout (M8), §3 missiles (M4), §4 fire on the move (D-L), §5 data (M5), §6 who runs it, §7 rails — §1 is the measured ground.
stale-below: nothing.
known-rot: none (§4a's CE-2170 row is fixed, 2026-10-14).
known-conflict: none.
related-designs:
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — OWNS the launchers as entities (Javelin, NLAW, LAW, Kornet, TOW as weapon systems; 2006 / 2007 as presets, R-2016); the rounds and guidance stay here
  - docs/designs/air-attack/DESIGN_Air_Operations.md — OWNS the free-fall bomb, the Air-domain launch velocity, BombBallistics and the weapon brain's Release profile (R-2015); a GPS bomb becomes a GuidanceKind here later
  - docs/designs/vehicle-tactics/DESIGN_Urban_Armour_Tactics.md — the PROGRAMME (items M4, M5, M8; decisions D-D, D-I, D-L)
  - docs/DESIGN_Weapon_Layers.md — OWNS the three weapon layers (R-261), the turret task (R-267, W-11), W-6 "the profile picks the ammo with the SAME ArmorModel" and W-12d "one AmmoGuid per mount until a mount declares a second round"; this file is W-6 / W-12d built, and the guided-weapon timing rule (WL-5) turned into guidance
  - docs/designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md — where a round lands (3-D arrival, Top face, penetrator kind); a top-attack missile reaches the roof through it by geometry alone
  - docs/DESIGN_Building_Interiors.md — OWNS R-217 (penetration looked up on the launcher × ammo pair) — keyed by the LOADED ammo from now
  - docs/DESIGN_Terrain_Combat_Tuning.md — OWNS the ammo reference library; §5's rounds join it
  - docs/DESIGN_Entity_Control.md §8e — OWNS the operator's round choice (the F key → AmmoChoice.Cycle, same one-reload cost) and the HUD's loaded round (WeaponAmmo grows)
  - docs/DESIGN_Peek_And_Fire.md — OWNS the aim time D3 (R-234) that is a fire-and-forget missile's lock
-->

# Missiles and ammunition — a mount's loadout, guidance, lock, and firing on the move

**Items:** M8 (ammo loadout + the weapon brain's choice), M4 (guided rounds), M5 (munition data). **Decisions applied:** D-I (the
weapon brain picks the round with the one `ArmorModel`, a switch costs one reload, the behaviour never names a round), D-D (three guidance
kinds as munition data on the existing round, lock = `AimSeconds`, no aerodynamics), D-L (per-mount `FireOnMove`; command-guided halts
for the flight, fire-and-forget only for the lock).

## 1. What exists — measured `2026-10-14`

| fact | where |
|---|---|
| two fire paths: **turret** — Brain `AimAndFireExecutor.TurretPath` writes `WeaponTask` on the turret part (Brain group, wire `EntityWeaponTask`); Muscle `WeaponTaskDispatcherSystem` slews and publishes `WeaponFireIntent`; **per-shot** — the Brain publishes `WeaponFireIntent`, wire `WeaponFireRequest`, Muscle re-publishes it | `AimAndFireExecutor.cs:67-221,321-407`, `WeaponTaskDispatcherSystem.cs:35-104`, `WeaponFireIntentEgressTranslator.cs:78` |
| **no ammo is chosen anywhere**: `WeaponTask.AmmoGuid` is only copied across the wire; `WeaponFireIntent` and `WeaponFireRequest` have no ammo field; `FireProcessingSystem` takes the round from `WeaponMountDto.AmmoGuid` | `WeaponTask.cs:59-60`, `FireProcessingSystem.cs:152-162` |
| ammo is counted PER MOUNT: one `WeaponState { Ammo, MaxAmmo, MagazineRounds, MagazineSize, ReloadSeconds, ReloadSecondsRemaining, CooldownSecondsRemaining, MuzzleVelocity }` per mount (mount 0 on the owner, ≥ 1 on `WeaponMountInfo` children, CGF only); `Magazine.Spend / Ready / Tick` | `CombatComponents.cs:13-41`, `Magazine.cs` |
| the mount is chosen once per engagement (`EngagementMount` → `WeaponChoice.Choose` over `WeaponSelectionDecision`); ⚠ its effectiveness input reads the mount's own `Penetration`, not the resolved round's `PenetrationOf` | `AimAndFireExecutor.cs:488-504`, `StandardInputs.cs:403-405` |
| R-217's penetration lookup is keyed by `mount.AmmoGuid` (`ParameterResolver.MountPenetration`) | `ParameterResolver.cs:226-254` |
| guided = a timing rule: `GuidedWeaponProfile.RoundsInFlight = 1`; `TurretPath` holds the next grant for `GuidedFlightSeconds` (flat distance ÷ speed + 0.5 s); the round itself flies straight | `WeaponBrainProfiles.cs:43-46`, `AimAndFireExecutor.cs:327-380,417-428` |
| a round lives 120 ticks (≈ 2 s) unless it is an arc round (3600); only `BallisticsSystem` reads the lifetimes | `CombatConstants.cs:80,88`, `BallisticsSystem.cs:132-133` |
| the aim time D3 runs on the PER-SHOT path only; the turret path has none | `AimAndFireExecutor.cs:139-151` |
| nothing checks the shooter stands still; moving doubles σ | `HitModel.cs:20-46` |
| `WeaponMountDto` has `Category, TurretIndex, MuzzleOffset*, WeaponGuid, InitialAmmunition, AmmoGuid, Penetration, DamagePerHit, Range, AimSeconds, MagazineSize, ReloadSeconds, DispersionMils, MuzzleVelocity` | `WeaponSuiteDto.cs:21-97` |
| ⚠ defect found on the way (**CE-2166**): a per-shot cooldown is drained TWICE a frame while an aim or point-fire action runs — by `WeaponDispatcherSystem` and again by the executor — ✅ **FIXED `2026-10-14` (backend, B1):** `WeaponDispatcherSystem.DrainCooldown` is the one drain (owner and mounts); the executors only read; a FOURTH drain was found and removed too (`AimAndFireExecutor`'s operator point-fire branch). Rail `AimAndFireExecutorTests.CE2166_ThroughTheDispatcher_A10SecondCooldown_FiresEvery10Seconds` (red-proved) | `WeaponDispatcherSystem.cs:55,66-72`, `AimAndFireExecutor.cs:154-156`, `FireAtPointExecutor.cs:78-80` |

### INVENTORY — the queries run

| query | total | the set |
|---|---|---|
| `search_graph .*Guided.*\|.*Missile.*\|.*Lock.*On.*` (Class) | 1 relevant | `GuidedWeaponProfile` |
| `search_graph .*Ammo.*` (Class, non-test) | 8 | `AmmoWeaponBallisticsDto`, `AmmoRefAttribute`, `WeaponAmmo`, `WeaponAmmoMirrorSystem`, `EntityWeaponAmmo` + its two translators, `RefAmmo` |
| grep `AmmoGuid` | 3 readers, 0 brain writers | §1 |

⇒ no ammo-choice class and no guided-round class exist; `WeaponAmmo` is a HUD mirror (up to 4 mounts), not a loadout — it is extended to show the loaded round, never used as the store.

## 2. Ammo loadout and choice (M8, D-I)

```mermaid
classDiagram
  class WeaponMountDto {
    <<TKB record, existing, grows>>
    AmmoGuid · InitialAmmunition  «kept: the one-round mount»
    Loadout : List~AmmoLoadDto~  «new, optional»
    FireOnMove : bool?  «new, D-L»
  }
  class AmmoLoadDto { <<new TKB record>> AmmoGuid ; Rounds }
  class AmmoLoadout {
    <<new ECS component 373, on the mount entity, Brain>>
    long Ammo0..3
    int Rounds0..3
    byte Count
    byte Loaded
    float SwitchRemaining
    Entity ChosenFor
  }
  class AmmoChoice {
    <<new static, Fdp.Toolkits Combat>>
    +Choose(world, mountEntity, mount, shooter, target, reloadS) bool switching
    +Best(world, load, mount, shooter, target) int
    +ValueOf(world, ammo, mount, shooter, target) float
    +ExpectedDamage(world, shooter, target, mount, ammo) float
    +ArmourFacing(world, shooter, target, ammo) float
    +LoadedAmmo(world, mountEntity) long
    +SpendLoaded(world, mountEntity)
  }
  class AimAndFireExecutor {
    <<existing, grows>>
    EngagementMount()  «kept»
    calls AmmoChoice.Choose beside EngagementMount
  }
  class Magazine { <<existing, unchanged>> AmmoChoice.SpendLoaded runs beside each Spend }
  class WeaponDispatcherSystem { <<existing, grows>> +DrainSwitch(ref AmmoLoadout, dt) }
  class CombatTkb {
    <<existing, grows>>
    +RoundOf(world, mount, long ammo) (penetration, damage)  «new»
    +WarheadOf(world, long ammo) WarheadDto  «new overload»
  }
  class WeaponFireIntent { <<event, existing, grows>> AmmoGuid «new» }
  class WeaponFireRequest { <<DDS, existing, grows>> AmmoGuid «new, R-169» }
  class WeaponTask { <<existing>> AmmoGuid «now written» }
  class FireProcessingSystem { <<existing>> round = intent.AmmoGuid, else mount.AmmoGuid }
  WeaponMountDto *-- AmmoLoadDto
  AimAndFireExecutor ..> AmmoChoice
  AmmoChoice ..> ArmorModel : same model (W-6, R-212 A1)
  AmmoChoice ..> CombatTkb
  AimAndFireExecutor --> AmmoLoadout : Loaded, SwitchRemaining
  AimAndFireExecutor --> WeaponTask : turret path
  AimAndFireExecutor --> WeaponFireIntent : per-shot path
  FireProcessingSystem ..> CombatTkb : penetration and warhead of the LOADED round
```

*What the picture shows that prose hid: the ammo GUID already travels Brain → Muscle on the turret path; only the two fire events and the
`FireProcessingSystem` read are new. One new component holds the loadout, beside the per-mount `WeaponState` that stays the magazine.*

| rule | why |
|---|---|
| a mount with no `Loadout` behaves exactly as today (one `AmmoGuid`, `InitialAmmunition`) | no existing type changes |
| `AmmoChoice.ValueOf` = expected damage of ONE round on the target's face (Armour Hit Model §2: face from the 3-D geometry, the round's penetrator) ÷ the target's max health; an HE round adds its warhead's area value at distance 0 (`AreaEffect` fragment chance vs that face, blast for an unarmoured target) | D-I: one model for the shot and the choice; HE wins against men, technicals and buildings, APFSDS against tanks |
| the choice is made once per engagement (beside `EngagementMount`) and kept while the target lives; a tie keeps the loaded round | no needless switches |
| a switch costs one `ReloadSeconds` (`SwitchRemaining`); the action reports `Reloading` meanwhile | D-I |
| `WeaponEffectivenessVsTarget` reads the mount's BEST round (and the resolved penetration) | fixes §1's ⚠ and makes the mount choice see HE |
| the behaviour never names a round | R-261 |

```mermaid
sequenceDiagram
  participant BT as Behaviour (PostureNodes.Fire)
  participant EX as AimAndFireExecutor (Brain)
  participant AC as AmmoChoice
  participant TP as TurretPath / per-shot path
  participant MD as WeaponTaskDispatcher (Muscle)
  participant FP as FireProcessingSystem (Muscle)
  BT->>EX: AimAndFireParams { Target, Mount = Auto }
  EX->>EX: EngagementMount
  EX->>AC: Best(world, mount, target)
  AC-->>EX: ammo (APFSDS / HEAT / HE)
  alt ammo differs from Loaded
    EX->>EX: SwitchRemaining = ReloadSeconds, hold
  end
  EX->>TP: turret: WeaponTask { AmmoGuid } · per-shot: WeaponFireIntent { AmmoGuid }
  TP->>MD: EntityWeaponTask on the wire (turret)
  MD->>FP: WeaponFireIntent { AmmoGuid }
  FP->>FP: penetration, warhead, damage of the LOADED round
```

### 2a. As-built `2026-10-14` (backend, handoff B6)

The design carries **11 classes** and **1 sequence** for M8; all are built (the class diagram above is updated in place). Deviations, argued:

| design said | built | why |
|---|---|---|
| `AmmoLoadout` with `fixed` arrays | named fields `Ammo0..3` / `Rounds0..3` (the `WeaponAmmo` pattern) **+ `ChosenFor`** | no unsafe code in a replicable component; `ChosenFor` makes "once per engagement" hold on EVERY path (a named mount never runs `EngagementMount`) |
| `EngagementAmmo()` in the executor | `AmmoChoice.Choose(…)` called beside `EngagementMount` | the choice, the switch and the "once per target" rule live in one static the effectiveness input also reads |
| `ValueOf` = expected damage ÷ max health | ✅, **plus a tie-break on the AREA damage** | capped at 1, every round kills a man ⇒ a tie, and a tie keeps the loaded round — "HE wins against men" could never happen. With the area tie-break HE wins against men and technicals, APFSDS against tanks (`M8_TheTankGun_…`) |
| `CombatTkb.PenetrationOf(world, mount, ammo)` | **`CombatTkb.RoundOf(world, mount, ammo)` → (penetration, damage)** | a round has its own damage too (`AmmoWeaponBallisticsDto.Damage`); another round of a loadout does NOT fall back to the mount's own `Penetration` (that number belongs to the mount's default round) |
| `Magazine.Spend` decrements the loadout | `AmmoChoice.SpendLoaded` beside each `Spend` (per-shot, blind, turret path) | `Magazine` stays free of ECS access |
| the switch | `SwitchRemaining` drained by `WeaponDispatcherSystem.DrainSwitch`, beside the reload; the per-shot path holds with `Reloading`, the turret path withdraws the grant (as for a reload) | one drain, every frame, fired or not (the CE-2166 lesson) |
| `WeaponEffectivenessVsTarget` reads the best round + resolved penetration | ✅ through `AmmoChoice.ExpectedDamage` (incl. the HE area at distance 0) | ONE expected-damage formula for the choice and the AI estimate (R-212 A1) |
| — | operator fire and blind fire also carry the loaded round; `FireAtPoint` and the hidden-target throw do not | grenade and mortar mounts have no loadout |

Wire: `WeaponFireRequest.AmmoGuid` (the only new wire field, R-169); `EntityWeaponTask.AmmoGuid` existed and is now written. Component id **373**
(registered in `CombatComponentRegistry`, so CGF, SimHost and the editor). Rails: `AmmoChoiceTests.M8_*` (2) · `AimAndFireExecutorTests.M8_ASwitch_…` +
`M8_ATie_…` · `FireProcessingSystemTests.M8_AMountWithNoLoadout_…` · `WeaponTaskDispatcherTests.M8_TheLoadedRound_ReachesTheMuscle_OnTheTurretPath` ·
`WeaponFireIntentEgressTranslatorTests.M8_TheLoadedRound_RoundTripsTheWire` · `StandardInputReaderTests.M8_Effectiveness_…`. ⚠ No built-in type carries a
loadout yet — that is B8's data (M5).

## 3. Guided rounds (M4, D-D)

```mermaid
classDiagram
  class MissileDto {
    <<new TKB descriptor "Combat.Missile", on a MUNITION type>>
    Guidance : Unguided | Command | FireAndForget
    SpeedMps
    MaxFlightSeconds
    MinRangeM
    TopAttack : bool
    TopAttackMinRangeM
    CruiseAltitudeM
    DiveAngleDeg
  }
  class GuidedRound {
    <<new ECS component, on the round entity, Muscle-local>>
    Guidance
    Entity Target
    Entity Gunner
    byte Phase : Climb | Cruise | Dive | Lost
    byte TopAttack
  }
  class GuidedRoundSystem {
    <<new, PostSimulation, before BallisticsSystem>>
    steers SimVelocity toward the target
  }
  class BallisticsSystem { <<existing, grows>> lifetime = MaxFlightSeconds for a guided round }
  class FireProcessingSystem { <<existing, grows>> speed = MissileDto.SpeedMps; adds GuidedRound }
  class AimAndFireExecutor { <<existing, grows>> MinRange gate · lock timer on the turret path too }
  class GuidedWeaponProfile { <<existing>> RoundsInFlight = 1 · hold = flight estimate }
  FireProcessingSystem ..> MissileDto
  FireProcessingSystem --> GuidedRound : on spawn
  GuidedRoundSystem ..> GuidedRound
  GuidedRoundSystem ..> IWorldQuery : sight (Command, FireAndForget seeker)
  GuidedRoundSystem ..> TurretPose : the launcher's sight line (Command)
  AimAndFireExecutor ..> MissileDto : min range, top-attack mode
```

*What the picture shows: guidance is ONE system that steers the velocity of the round that already flies; the hit, the face and the damage
are the unchanged chain. Climb-and-dive is three phases of that steering — the Top face then follows from the arrival angle (Armour Hit
Model D-A), so nothing marks a hit "top attack".*

| guidance | each frame | what defeats it |
|---|---|---|
| `Unguided` (RPG, LAW) | nothing — today's straight round | the aim time and the dispersion |
| `Command` (TOW-like, ATGM technicals) | steer onto the LAUNCHER's sight line: a turret launcher's sight = its `TurretPose` (Muscle-owned), which the weapon brain keeps on the target during the flight hold (WL-5's hold already writes `Track`); a per-shot launcher's sight = the gunner's eye → the target while `IWorldQuery.SightBlocked` is false. Sight blocked ⇒ `Lost` — the round keeps its last velocity | ⭐ break sight during the flight (the vehicle reverses into its hide); an operator possessing the gunner guides it by aiming — no extra channel |
| `FireAndForget` (Javelin, NLAW) | the round's own SEEKER: steer toward the locked target while the ROUND has sight of it (its last seen point otherwise); with `TopAttack` (range ≥ `TopAttackMinRangeM`): Climb to `CruiseAltitudeM` → Cruise → Dive at `DiveAngleDeg` once the horizontal distance ≤ altitude / tan(dive) | ⭐ deny the LOCK — the gunner's aim time is the lock (D3), a short exposure never completes it; once launched, nothing in v1 |

| rule | why |
|---|---|
| steering = turn the velocity toward the aim point at a fixed turn rate (≤ 30°/s) at `SpeedMps` — no aerodynamics, no thrust curve | D-D |
| ⭐ **where it lives and what crosses the wire**: `GuidedRoundSystem` runs on the MUSCLE beside `BallisticsSystem` — the round, its target, the launcher's turret pose and the terrain are all on that node; **no new channel**: the fire intent already names the target, and the turret task (Brain → Muscle, `EntityWeaponTask`) keeps the sight on it | user: "where is missile steering living, how would it work, what channel?" |
| `MaxFlightSeconds` replaces the 2 s bullet lifetime for a guided round (Javelin ~20 s, TOW ~22 s) | §1: a 2 km missile vanished mid-flight |
| `SpeedMps` replaces the generated `range × 0.5` muzzle speed for a missile | §1: the TOW flew ~7× too fast |
| below `MinRangeM` the weapon brain refuses (`ActionReason.TooClose`); between `MinRangeM` and `TopAttackMinRangeM` a top-attack weapon fires direct | Javelin 65 m direct, 150 m top |
| the lock: a fire-and-forget mount's `AimSeconds` IS the lock — the per-shot path already times it (D3); the TURRET path gains the same continuous-sight timer before its first grant | D-D: one aim-time concept, not a second "lock" field |
| the one-in-flight hold (WL-5) stays; `Command` reports `Guiding` while its round flies | the vehicle behaviour reads it (§4) |

```mermaid
sequenceDiagram
  participant EX as AimAndFireExecutor (Brain)
  participant FP as FireProcessingSystem (Muscle)
  participant GS as GuidedRoundSystem (Muscle, PostSim)
  participant BS as BallisticsSystem (Muscle, PostSim)
  participant HR as HitResolutionSystem
  EX->>EX: target seen continuously for AimSeconds (the lock), range ≥ MinRangeM
  EX->>FP: fire (turret task grant or per-shot intent)
  FP->>FP: round at SpeedMps + GuidedRound { Guidance, Target, Gunner, Phase = Climb or Cruise }
  loop every frame until it hits or MaxFlightSeconds
    GS->>GS: Command: steer onto the launcher's sight line (TurretPose kept on target by Track) — blocked ⇒ Lost
    GS->>GS: FireAndForget + TopAttack: Climb → Cruise → Dive
    BS->>BS: swept segment, terrain carry, lifetime
  end
  BS-->>HR: RaycastRequest → hit
  HR-->>HR: DetonationNotification { Travel = descending ⇒ Top }
```

## 4. Firing on the move (D-L)

| mount | `FireOnMove` default (from `Category`, overridable per mount) |
|---|---|
| SmallArms · MachineGun · Autocannon · Grenade | yes (moving σ × 2, as today) |
| TankGun | yes — a stabilised gun; an old tank sets `false` |
| Atgm · Mortar | no |

| rule | why |
|---|---|
| `FireOnMove = false` ⇒ the weapon brain holds fire while the shooter moves faster than `HitModel.MovingSpeed` (0.5 m/s): `ActionReason.Moving` | D-L; the same gate shape as ROE |
| `Command` guidance reports `Guiding` for the flight; the vehicle cycle stays at its firing point while it reads `Guiding` (Exposure Cycle §3) | the gunner must keep sight; the behaviour, not the weapon, decides to stand |
| `FireAndForget` needs to stand only during the lock (`FireOnMove = false` until the round is away) | D-L |

### 4a. As-built `2026-10-14` (backend, handoff B7 — §3 and §4)

The design carries **8 classes** and **1 sequence** for M4 + D-L; all are built. Deviations, argued:

| design said | built | why |
|---|---|---|
| `GuidedRound { Guidance, Target, Gunner, Phase Climb/Cruise/Dive/Lost, TopAttack }` | ✅ + `SpeedMps`, `LifetimeTicks`, `CruiseZ`, `DiveAngleDeg`, `LastSeen` (component **374**, `NoScenario`, Muscle) | the system needs them per round; the TKB is read once, at spawn |
| Command: steer onto the LAUNCHER's sight line (a turret launcher's `TurretPose`) | ⚠ the GUNNER's eye → the target's middle, on both paths; blocked (`IWorldQuery.SightBlocked`) or a dead gunner ⇒ `Lost` | the turret pose is kept on the target by the Track hold anyway, and it is not on the brain-less muscle path the rails run; the eye line is the same line within the turret's offset. A turret-pose line is a later refinement if a scenario needs it |
| top attack: dive once horizontal ≤ altitude / tan(dive) | ⚠ the dive starts a TURN LEAD earlier (`R·sin θ + (h − R(1−cos θ)) / tan θ`, R = speed / turn rate) and holds at least the dive angle, then homes once the line to the target is steeper | 📐 at 140 m/s and 30°/s the turn is a 267 m arc: the design's trigger overshoots the target before the nose is down (red-proved: `M4_AJavelin_…` fails with it) |
| `ActionReason.TooClose`; the D-L hold as `ActionReason.Moving` | `TooClose` and a NEW `HoldMoving` | `Moving` is the LOCOMOTION row's word ("moving toward the destination"); a weapon hold must read as a hold (both are in `IsHold`, posture Ready) |
| `FireOnMove` per mount | ✅ `WeaponMountDto.FireOnMove : bool?` + `CombatTkb.FiresOnTheMove` (null ⇒ Atgm / Mortar stand, the rest fire) and `HitModel.IsMoving` — the ONE moving test the spread already used | — |
| the turret path gains the aim-time lock | ✅ only for a mount that DECLARES `AimSeconds`; the lock rides the aim clock (`Ready` = locked), which after the lock carries the guided-flight hold as before | the 32-byte channel state is full; every turret mount without an aim time is unchanged |
| `MaxFlightSeconds` / `SpeedMps` | ✅ `BallisticsSystem` reads `GuidedRound.LifetimeTicks`; `FireProcessingSystem` flies a missile at `SpeedMps` (an unguided missile gets speed + lifetime only) | — |
| ✅ CE-2170 FIXED `2026-10-14` (backend, Air Ops handoff E1) | the launcher's flight HOLD (`GuidedFlightSeconds`) reads the round's `MissileDto.SpeedMps` first (the loaded round, else the mount's ammo — the lookup the spawn uses), then the mount, then the state ⇒ a TOW at 2 km holds 7.6 s (was 1.57 s). Rail `AimAndFireExecutorTests.CE2170_ATowAtTwoKm_HoldsTheLauncherForItsFlight`, red-proved | the spawn and the hold now read ONE speed |

Wiring: `GuidedRoundSystem` in `CombatModule.PostSimulationSystems` before `BallisticsSystem` (SimHost and the editor's Muscle);
`GuidedRound` registered in `CombatComponentRegistry`. No wire change (§3's rule holds). Rails: `GuidedRoundSystemTests.M4_ACommandRound_…`,
`M4_AJavelin_ClimbsAndDives_AndArrivesOnTheRoof`, `M4_TheTurn_IsLimitedTo30DegreesASecond` · `BallisticsSystemTests.M4_AGuidedRound_LivesItsFlight_NotTwoSeconds` ·
`AimAndFireExecutorTests.M4_BelowMinRange_TheWeaponHolds`, `M4_TheTurretPath_TimesTheLock`, `DL_AnAtgm_HoldsFireWhileMoving_AnHmg_FiresAtDoubleSpread`.
⚠ No built-in munition carries a `MissileDto` yet — the TOW / Javelin / NLAW numbers are B8's data (§5).

## 5. Data (M5) — new munition types (ids 6010…) and mounts

Approximate public figures; tuning starts here (programme §2 sources).

| munition | id | penetrator | pen (mm) | damage | speed (m/s) | guidance | top attack | min / max range (m) | on mount: aim (s) · reload (s) · rounds |
|---|---|---|---|---|---|---|---|---|---|
| PG-7V (exists, 6009) | 6009 | ShapedCharge | 300 | 400 | 300 | — | — | — / 300 | 2.0 · 6 · 4 |
| PG-7VL | 6010 | ShapedCharge | 500 | 450 | 300 | — | — | — / 300 | 2.0 · 6 |
| PG-7VR | 6011 | Tandem | 650 | 500 | 280 | — | — | — / 200 | 2.2 · 6 |
| M72-class LAW | 6012 | ShapedCharge | 300 | 350 | 145 | — | — | — / 200 | 1.5 · — · 1 (disposable; a man carries 2) |
| NLAW | 6013 | ShapedCharge | 500 | 900 | 200 | FireAndForget | yes (shallow overfly) | 20 / 800 | 2.5 · — · 1 |
| Javelin | 6014 | Tandem | 760 | 1500 | 140 | FireAndForget | yes (150 m climb) | 65 (150 top) / 2500 | 4.0 lock · 20 · 2 |
| TOW-2 (exists as 6008) | 6008 | ShapedCharge | 800 | 2000 | 280 | Command | — | 65 / 3750 | 1.5 · 15 · 7 |
| technical ATGM (Kornet-class) | 6015 | Tandem | 1100 | 2000 | 300 | Command | — | 100 / 5000 | 1.5 · 20 · 4 |
| FPV warhead (the drone's, M7) | 6016 | ShapedCharge | 400 | 450 | — | (the drone flies) | (dives) | — | — |
| 120 mm HEAT-MP | 6017 | ShapedCharge | 600 | 1200 | 1400 | — | — | — / 3000 | tank loadout |
| 120 / 125 mm HE-FRAG | 6018 | Kinetic + HE warhead | 50 | 600 + area | 900 | — | — | — / 4000 | tank loadout |
| 12.7 / 14.5 mm AP | 6019 | Kinetic | 25 | 60 | 900 | — | — | — / 1500 | HMG, `FireOnMove` |

Mounts: M1 / T-72 main gun loadout APFSDS + HEAT-MP + HE (e.g. 20 / 10 / 10) + a coax MG (7.62, hull-fixed to the turret) · the RPG man
(`Insurgent`, 2003): RPG with 4 rockets + a rifle · an AT team type with Javelin or NLAW · technicals (Exposure Cycle §6).

### 5a. As-built `2026-10-14` (backend, handoff B8 — M5, with M6's data)

§5 adds no class and no sequence; it is data on the classes §2–§4 built. Everything in the table is built; deviations, argued:

| design said | built | why |
|---|---|---|
| a munition's numbers per §5 | each new type (6010–6019) states a generic `AmmoWeaponBallisticsDto` (pen, damage), a `WarheadDto` copied from the NEAREST library entry (`PG-7V HEAT` for the rockets, the NLAW and the FPV warhead, `TOW-2 HEAT` for Javelin and Kornet, `120mm HEAT`, `125mm HE-frag`; tandem set where §5 says Tandem) and a `MissileDto` | one source for the fragment / blast numbers; the names match no library entry, so nothing is picked up by name |
| TOW 6008, PG-7V 6009 "exist" | both gain a `MissileDto`: TOW command, 280 m/s, 15 s, min 65 m; PG-7V unguided 300 m/s, 4.5 s | 📐 the TOW flew at the NED builder's `range × 0.5` = 1875 m/s; their penetration and damage stay on the mounts (LR-1) |
| NLAW "yes (shallow overfly)" top attack | ⚠ **flies DIRECT** | the overfly attack fires DOWN as it passes over; §3's climb/cruise/dive at a 30°/s turn needs a ~380 m radius, longer than its 20–800 m range ⇒ a downward-fuze model is needed — **CE-3198** |
| a coax MG "7.62" | two new types **6020 / 6021** (7.62 × 51 ball, tracer 1 in 5) — §5's table had no 7.62 round, and R-271 wants every mount's ammo typed; on the turret beside the gun, 200-round belt, 6 s to change, MG ⇒ fires on the move | the T-72's PKT (7.62 × 54R) shares the type |
| tank loadout "e.g. 20 / 10 / 10" | M1 20 / 10 / 12, T-72 18 / 9 / 12 — **sums equal the old ammunition** (42, 39); the T-72's HEAT is 6017 (the 120 mm one) | no rounds appear or vanish; one HEAT type until a 125 mm one is needed |
| the RPG man | `Insurgent` (2003): RPG 4 rockets (was 1), lay 2 s, reload 6 s, `FireOnMove = false`, mount 0 as before; rifle on mount 1 | mount 0 stays the RPG so every scenario that named it keeps working |
| "an AT team type with Javelin or NLAW" | **both**: `JavelinTeam` (2006: 2 missiles, 4 s lock, 20 s reload) and `NlawGunner` (2007: 2 tubes, 2.5 s, 3 s), each + a rifle, sight = the launcher's range | round-out: one helper (`AntiTank`), two rows. Their mounts state no penetration — the munition's own is read |
| the TOW's "1.5 · 15 · 7" | `AimSeconds = 1.5` (the turret path's lock, §4a); reload left to the mount's 2 / min | the rate already spaces the shots by 30 s |
| M6: explicit health + top + CE | M1 3000 / top 60 / front CE 900, side CE 450 · T-72 2500 / 40 / 700 / 350 · Bradley 500 / 30 / 150 / 80 · `MilitaryAPC` (2001) armour 30 / 20 / 15 / top 10 (was none), health 500 · eye heights M1 2.5, Bradley 2.7, T-72 2.2 m | every stated health EQUALS the old front × 5, so no fight moves on it. ⚠ The APC change is a combat change: a rifle (5 mm) no longer scratches it; an RPG still does full damage |

Plumbing: `SimCombatDef` gains `ArmorTop`, the four `…Ce`, `MaxHealth`, `MobilityKillBelowFraction`, `EyeHeight`; `WeaponMount` gains `Loadout`,
`FireOnMove`, `AimSeconds`, `MagazineSize`, `ReloadSeconds`; `NedTkbBuilder.WithCombat` copies them (a stated health is no longer recorded as
Generated). Rails: `NedTkbBuilderCombatTests.M5_M6_TheTanks_…` (+ `Stage0_M1_…` re-pinned: health Explicit, eye 2.5) ·
`UrbanCombatCatalogRails.M5_M6_TheRpgMan_TheAntiTankTeams_AndTheArmouredApc` (+ count 7 → 9) · `EffectTests.M5_TheNewMunitions_…` (+ `LR1_…`
re-pinned: Javelin and NLAW join the explosive set).

## 6. Who runs it

```mermaid
graph TD
  subgraph BRAIN["Brain node — CGF / Editor (ActionDispatchModule → WeaponDispatcherSystem, CgfLogicPack)"]
    EX["AimAndFireExecutor: mount · AMMO (new) · lock (turret path, new) · min range (new) · FireOnMove gate (new)"]
  end
  subgraph WIRE["wire"]
    T["EntityWeaponTask (has AmmoGuid)"]
    R["WeaponFireRequest + AmmoGuid (new field)"]
  end
  subgraph MUSCLE["Muscle node — SimHost / Editor / Stride (CombatModule)"]
    MD["Input: WeaponTaskDispatcher → FireProcessing (ammo from the intent, GuidedRound, missile speed)"]
    GS["PostSimulation: GuidedRoundSystem (new) → Ballistics (guided lifetime) → LinearKinematics"]
  end
  EX --> T --> MD
  EX --> R --> MD
  MD --> GS
```

*What the picture shows: the ammo choice and the lock live on the Brain with the rest of the weapon brain; guidance lives on the Muscle
beside the round it steers — no new wire topic, one new field on one existing request.*

## 7. Rails — in the features' own suites

| rail | suite |
|---|---|
| `M8_AMountWithNoLoadout_FiresItsOneRound_AsBefore` | `FireProcessingSystemTests` |
| `M8_TheTankGun_PicksApfsdsForATank_HeForMenInABuilding_HeatOrHeForATechnical` | new `AmmoChoiceTests` |
| `M8_ASwitch_CostsOneReload_ATie_KeepsTheLoadedRound` | `AimAndFireExecutorTests` |
| `M8_TheLoadedRound_ReachesTheMuscle_OnBothPaths` | `WeaponTaskDispatcherTests` · `EntityTurretPoseTranslatorTests` (WL2b) · a `WeaponFireRequest` round-trip |
| `M8_Effectiveness_ReadsTheBestRound_AndTheResolvedPenetration` | `StandardInputReaderTests` |
| `M4_ACommandRound_FollowsTheTarget_WhileTheGunnerSeesIt_AndGoesStraightWhenSightBreaks` | new `GuidedRoundSystemTests` |
| `M4_AJavelin_ClimbsAndDives_AndArrivesOnTheRoof` | `GuidedRoundSystemTests` + `DamageCalculationSystemTests` |
| `M4_AGuidedRound_LivesItsFlight_NotTwoSeconds` | `BallisticsSystemTests` |
| `M4_BelowMinRange_TheWeaponHolds` · `M4_TheTurretPath_TimesTheLock` | `AimAndFireExecutorTests` |
| `DL_AnAtgm_HoldsFireWhileMoving_AnHmg_FiresAtDoubleSpread` | `AimAndFireExecutorTests` |
| `WL5_W12c_AnAtgm_FiresOneRoundPerFlight` stays green | `AimAndFireExecutorTests` |
| ⚠ CE-2166's fix (one drain per frame) re-times every cooldown rail — fix it FIRST, separately | `AimAndFireExecutorTests`, `WeaponInteractionDispatcherTests` |

> ⭐ **`2026-10-14` (Air Ops slice 4 — 📄 `docs/designs/air-attack/DESIGN_Air_Operations.md` §8a):** `WeaponCategory.Bomb` and a
> fourth weapon-brain behaviour, the **Release** profile: the per-shot path held in `AimAndFireExecutor.HoldOf` (`ActionReason
> .AwaitingRelease`) until `BombBallistics` says the impact reaches the target; munition 6022 (Mk-82). An Air-domain shooter launches
> with its carrier's velocity (`FireProcessingSystem`); ground shooters unchanged.
