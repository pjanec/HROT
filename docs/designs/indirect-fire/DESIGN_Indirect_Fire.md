<!--STATUS
state: LIVE
updated: 2026-10-21 (§ CE-1238 at the end — a man firing his own mortar crouches; a crewed tube's round no longer kills its gunner; bt-mortar-roof fires crewed tubes) · 2026-10-16
build-state: IF-1 BUILT (backend, 2026-10-15 — §3.6 as built); IF-2 BUILT (behaviors, 2026-10-15 — §3.7); IF-3 BUILT (behaviors, 2026-10-15 — §3.8); laying the gun BUILT (backend, ui handoff P2, 2026-10-15 — §3.10); IF-4 BUILT (behaviors, 2026-10-15 — §3.9, passes live); IF-5 smoke and IF-6 counter-battery are DESIGN
current-answer: §4 decisions (IF-A…IF-G, FM-1…FM-8), §3.6 IF-1 as built, §3.7 IF-2 as built, §3.8 IF-3 as built (§3.5 is the adjust sequence AS BUILT), §3.9 IF-4 as built, §3.10 laying the gun on the wire, §5 slices, §6 the demo
stale-below: nothing
known-rot: IF-E's `FireMissionAdjustEvent` was never built — every correction is a Strike (§3.8)
known-conflict: none
related-designs:
  - docs/blueprints/Architect_Question_95_Sound_Signatures_And_Lossless_Hearing.md — OPEN: how a gun standing off the terrain is HEARD (B) and what it sounds like (A)
  - docs/OVERVIEW_Rules_And_Demos.md — the INDEX of the rules and demos built across the behaviour designs (rule → what implements it → demo); this design OWNS the rows that cite it
  - docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md — §8d OWNS the heard launch as a SPOT (HQ-1, `CE-2309` — "finding the mortar", Urban Battle §4 ②) and the headquarters that sends a battery on a FireSupport request (HQ-7)
  - docs/DESIGN_Node_Roles_And_Policies.md — §3.5 (CE-1148 S2): every node receives the bursts; supersedes §3.8's Brain-only ingress branch.
  - docs/designs/structure-damage/DESIGN_Structure_Damage.md — OWNS what a heavy round does to a structure (a bunker, a building)
  - docs/DESIGN_Map_3D_Mode.md — §6n draws the tubes, the towed and SP howitzers, their crews, the laid elevation (a default until IF-1 replicates it) and the firing blast (the §5 ui row)
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — OWNS the weapon-as-an-entity, the Station / Gunner seats and ControllerOf (a ground mortar is a weapon system its gunner controls)
  - docs/DESIGN_Entity_Interactions.md — §7.5 OWNS the team carry (the 81 / 120 mm tubes are TwoMan loads)
  - docs/designs/air-attack/DESIGN_Air_Operations.md — OWNS the Strike order (§7) this design extends; mortars and artillery fulfil it here
  - docs/DESIGN_Building_Interiors.md — §3k OWNS the indirect warhead and the arc flight (Stage 6, W-8); who calls fire is this design
  - docs/DESIGN_Weapon_Layers.md — §5f OWNS the L-1 lob (ThrowArc) for hand weapons; this design adds the gun's fire solution beside it
  - docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md — OWNS munitions and loadouts; the mortar and artillery rounds here are new entries
  - docs/DESIGN_Orders_Params_Entity_Classes.md — OWNS OrderResolver (<Class><Order>) and the designations (§13.3) an operator's call for fire uses
  - docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md — §3 OWNS SquadManeuverRunner / ISquadPlay; the battery is one more play
  - docs/designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md — OWNS the roof hit a falling round makes on a vehicle
  - docs/DESIGN_Operations_Behaviour_Catalogue.md — lists CallFireMission (AIR-1, SQD-9), built here
  - docs/DESIGN_Thermal_And_Acoustic_Sensing.md — OWNS what a brain may perceive (anonymous sensor answers, D); §3.8 here adds the burst feed to a Brain-only node and why it stays within that
-->

# DESIGN — Indirect fire: mortars, artillery and the fire mission

🔒 **User, `2026-10-14`:** *"I think we might already start thinking about the mortars and artillery - new ammo, new high arc ballistic mode and
automatic selection between low arc and high arc based on visibility and similar stuff - carrying and deploying and operating using a seat is
already covered."* · *"Maybe also how a typical fire mission could be executed - if a behavior of a virtual commander entity of a group of
artillery guns, commander telling individual guns how/where to fire (your subject matter expetise needed again)"* → the leans →
*"Artillery approved including demo acenario please write design"* (**R-2018**, tracker **CE-2176**).

## 1. What exists — measured `2026-10-14` (after merging backend WS-2 / WS-3)

| fact | where |
|---|---|
| an INDIRECT warhead flies a gravity arc: `ArcLaunch(from, to, speed, high)` is a closed form at ONE speed; out of reach ⇒ 45° (falls short) | `FireProcessingSystem.cs:344` |
| the arc is chosen by speed: ≥ 40 m/s ⇒ always high; slower tries low and lobs high when `Clears` (12 samples through `TerrainPenetration.Carry`) says the low arc is stopped | `ThrowArc.cs:24-56`, `CombatConstants.HighArcMinMuzzleVelocity = 40` |
| ⛔ an indirect round's launch REPLACES the dispersed direction — **arc rounds have no dispersion today** | `FireProcessingSystem.cs:151-158` (σ, θ) then `:177` (`velocity = ThrowArc.Launch(...)`) |
| dispersion is replayable: σ from the mount's `DispersionMils` × state, deflection `d(k) = 2·frac(k·φ⁻¹) − 1` on the shooter's shot ordinal — no random source | `HitModel.cs:41-70` (AQ85, R-216) |
| ⛔ an arc round lives **60 s** (`ArcRoundLifetimeTicks = 3600`) — a high-angle howitzer round flies longer | `CombatConstants.cs:88`, `BallisticsSystem.cs:133` |
| gravity acts only on Arc rounds; no drag anywhere | `BallisticsSystem.cs:130` |
| the round's speed per ammo × weapon pair already has a home: `AmmoWeaponBallisticsDto { WeaponGuid, MuzzleSpeed, Damage, PenetrationMm }` on the AMMO template, read for damage and penetration | `AmmoWeaponBallisticsDto.cs:12`, `ArmorModel.cs:220`, `ParameterResolver.cs:233` |
| `WeaponCategory.Mortar` exists, but no mount declares it (2005's mount has no Category) — the Weapon Layers inventory says *"no `MachineGun` / `Mortar` mount yet"* | `DESIGN_Weapon_Layers.md:180, 871` |
| mortar content: one munition (81 mm HE, 6002) and `MortarTeam` 2005 — a soldier with a built-in mount, 60 m/s, 360 m | `MunitionTkbCatalog.cs:22`, `UrbanCombatTkbCatalog.cs:114, 215-229` |
| the reference library lists 60 / 81 / 120 mm mortar HE; no howitzer, smoke or illumination | `Tkb/Reference/Data/ammo.json` |
| `FireAtPoint` (executor + library behaviour): N rounds at a point, cooldown, ROE, an indirect round exempt from the friendly-on-line hold; params 21 of the channel's 32 bytes | `FireAtPointExecutor.cs:14-130`, `FireAtPointParamsJsonDto.cs`, `FireAtPointBehavior.cs:25-38` |
| ⛔ `FireAtPoint` cannot fire a CONTROLLED weapon: `MountEntity(owner, 32+k)` falls back to the owner (WS-3 taught only `AimAndFireExecutor`) | `WeaponMountQuery.cs:88-97`, commit `f656cb38b` |
| a ground-manned weapon's controller is the man in its Gunner seat; his mount list gains it at index 32+k | `WeaponSystems.cs` `ControllerOf`, `ControlledMountBase = 32` |
| an order resolves to `<Class><Order>` for the most specific class first, then broader ones | `OrderResolver.cs:37-55` (R-278) |
| classes exist: `SelfPropelledArtillery` 1.1.*.4 · `TowedArtillery` 1.1.*.5 · `Mortar` 1.1.*.10 · `ArtilleryUnit` 11.1.*.4 | `EntityClassTable.cs:130-131, 136, 184` |
| `StrikeOrder { Location, Target }`; only `AirPlatformStrike` fulfils it | `OrderDtos.cs:72`, `AirAttackBehaviors.cs:199-227` |
| a commander drives members through `ISquadPlay` + `SquadManeuverRunner`, sending each a behaviour by `AssignTacticalIntentEvent` (Superior); `SquadGates.Track` counts each member's rounds | `SquadManeuverRunner.cs:274-330, 534-540`, `SquadPlayState.Gate` |
| the designation store is OPERATOR UI state per station — an operator's call for fire fills Strike from it; an AI never reads it | `OrderDesignations.cs:17-22` |
| the demo terrains are flat ground plus buildings and walls (no relief; ≤ 400 m of features) | `Recipes/Terrain/*/…world.geojson` (`height` on buildings only) |

## 2. INVENTORY

| query | total | relevant |
|---|---|---|
| `search_graph name_pattern=.*(Arc\|Ballistic\|Indirect\|FireMission\|Battery\|Artillery\|Mortar\|Howitzer\|Charge\|Observer\|CallFor).* label=Class` | 76 (case-insensitive; most are `Hierarchy`/`Archive`/`Search` noise) | `AmmoWeaponBallisticsDto`, `BallisticAim`, `BallisticProjectile`, `BallisticsSystem`, `BombBallistics`, `ThrowArc` — **no fire mission, battery, observer or charge class** |
| same, `label=Interface` / `label=Enum` | 9 / 3 | none relevant |
| grep `FireMission\|CallForFire\|Battery` over `Hrot/ FDP/` (`*.cs`) | 0 | — |
| corpus: `docs/`, `.dev/` for mortar, artillery, fire mission, indirect | see §7 | — |

⚠ `check_index_coverage` is not reachable through the CLI this session; the "no class" claim rests on graph + grep.

## 3. The design — diagrams first

### 3.1 Modules — who runs it, on which node, each frame

```mermaid
graph TD
  subgraph CGF["CGF node (brains)"]
    TIR["TacticalIntentResolutionSystem<br/>Strike → OrderResolver"]
    BTK["BrainTickSystem"]
    BAT["ArtilleryUnitStrike ★<br/>(battery commander, a unit)"]
    SPS["SelfPropelledArtilleryStrike ★<br/>(one gun)"]
    RUN["SquadManeuverRunner.Tick&lt;BatteryFireMissionPlay ★&gt;"]
    GUN["gun member: FireAtPoint behaviour<br/>(+fireAt, +arc ★)"]
    FAP["FireAtPointExecutor<br/>WeaponChannel"]
    FS1["FireSolution.For ★"]
    OBS["PersonObserveFire ★<br/>on the observer"]
    DMS["DetonationMemorySystem ★<br/>RecentDetonations"]
    DIN["MunitionDetonationIngress<br/>(★ on a Brain-only node)"]
  end
  subgraph SIM["SimHost node (combat)"]
    FPS["FireProcessingSystem<br/>flies the intent's charge + arc ★"]
    BAL["BallisticsSystem<br/>gravity, lifetime from TOF ★"]
  end
  TIR --> BTK --> BAT --> RUN
  BTK --> SPS --> RUN
  RUN -- "AssignTacticalIntentEvent (Superior)" --> GUN
  GUN --> FAP
  BAT --> FS1
  FAP --> FS1
  FAP -- "WeaponFireIntent +Charge +High ★" --> FPS --> BAL
  OBS -- "Strike (Adjust · 1 round, then FireForEffect)<br/>AssignTacticalIntentEvent, crosses brain nodes" --> TIR
  BAL -- "DetonationNotification → MunitionDetonation (DDS)" --> DIN --> DMS
  DMS -- "bursts since the call, from the battery" --> OBS
  MTB["MortarStrike / TowedArtilleryStrike"]:::dead
  classDef dead fill:#fdd,stroke:#c00,stroke-dasharray: 4 4
```

*What the picture shows that prose hid: nothing new runs on SimHost except the flight honouring the brain's choice — the decision
(charge, arc, when) is made ONCE on the brain node and carried in the intent. The red box is deliberately absent: a ground-manned tube or
towed gun has no brain of its own (W-D), so a `MortarStrike` on the weapon would never tick — fire missions go to the battery unit, or to a
self-propelled gun, which is a vehicle with a brain.*

### 3.2 Classes

```mermaid
classDiagram
  direction LR
  class FireSolution {
    <<static ★ Fdp.Toolkits Combat>>
    +ChargesOf(db, mount, ammo) speeds
    +For(world, from, to, mount, charges, pref, penetration) Solution
  }
  class Solution {
    <<struct ★>>
    Ok · Refusal FireRefusal · Charge 1-based · High · Launch · TimeOfFlight · Elevation
  }
  class ArcPreference { <<enum ★>> Auto · Low · High }
  class FireRefusal { <<enum ★>> None · TooFar · TooClose · Masked }
  class IndirectArc { <<enum ★>> Unset · HighOnly · Both · LowOnly }
  class ActionReason { existing · +AwaitingFireTime ★ · +OutOfRange ★ · +Masked ★ }
  class BallisticProjectile { existing · +ArcLifetimeTicks ★ }
  class WeaponMountDto { existing · +Arc IndirectArc ★ · +MinElevationDeg ★ · +MaxElevationDeg ★ }
  class AmmoWeaponBallisticsDto { existing MuzzleSpeed · Damage · PenetrationMm · +ChargeSpeedsMps ★ }
  class ThrowArc { existing Launch · Clears }
  class HitModel { existing Sigma · Deflection · NextOrdinal }
  class WeaponFireIntent { existing AtPoint · TargetPoint · +Charge ★ · +HighArc ★ }
  class FireProcessingSystem { existing ArcLaunch }
  class FireAtPointParams { existing Point · Cooldown · Rounds · Mount · +Arc ★ · +FireAt double ★ }
  class FireAtPointExecutor { existing · +fires a controlled weapon ★ · +holds until FireAt ★ }
  class StrikeOrder { existing Location · Target · +Method · Rounds · Sheaf · SheafWidthM · Control · ImpactInS · Munition · Observer · DangerClose ★ }
  class BatteryFireMissionPlay { <<ISquadPlay ★>> Adjust · FireForEffect · Complete }
  class SquadManeuverRunner { existing Tick · OrderFor }
  class MemberOrderKind { existing Hold … Embark · +FireAtPoint ★ }
  class IndirectFireNodes { <<static ★ behaviors>> FireMission · MountOf · Reaches }
  class PersonObserveFireBehavior { <<★ IF-3>> Observe · Correct · Sees · FromBattery }
  class ObserveFireOrder { <<★ IF-3 order>> Location · Target · Battery · Locating · Rounds · Sheaf · Control · DangerClose }
  class ObserveFireParams { <<★ IF-3>> Target · Call · Battery · Phase · Step · LastSide · Splitting · CalledAt · SeenSerial }
  class RecentDetonations { <<★ IF-3 recorded singleton>> Count · Slots · Add · Since }
  class DetonationMemorySystem { <<★ IF-3, CGF pack>> }
  class AcousticPerception { existing Error hash }
  class WeaponSystems { existing ControllerOf · Controlled }

  FireSolution ..> ThrowArc : Clears (generalised)
  FireSolution ..> AmmoWeaponBallisticsDto : charges
  FireSolution ..> WeaponMountDto : arc, elevation
  FireSolution --> Solution
  Solution --> FireRefusal
  FireAtPointExecutor ..> ActionReason : why it holds / fails
  FireProcessingSystem --> BallisticProjectile : lifetime from TOF
  FireAtPointExecutor ..> FireSolution
  FireAtPointExecutor ..> WeaponSystems : controlled mount
  FireAtPointExecutor --> WeaponFireIntent
  FireProcessingSystem ..> WeaponFireIntent
  FireProcessingSystem ..> HitModel : arc dispersion ★
  IndirectFireNodes ..> SquadManeuverRunner
  BatteryFireMissionPlay ..|> SquadManeuverRunner : ISquadPlay
  BatteryFireMissionPlay ..> FireSolution : time of flight per gun
  BatteryFireMissionPlay ..> MemberOrderKind : FireAtPoint
  BatteryFireMissionPlay ..> StrikeOrder
  PersonObserveFireBehavior ..> ObserveFireOrder : resolves
  PersonObserveFireBehavior --> ObserveFireParams
  PersonObserveFireBehavior ..> StrikeOrder : every call
  PersonObserveFireBehavior ..> RecentDetonations : bursts
  PersonObserveFireBehavior ..> AcousticPerception : location / judgement error
  DetonationMemorySystem --> RecentDetonations
```

*As built (IF-1, `2026-10-15`): `IndirectArc` gained `Unset` = 0 (today's `ThrowArc` rule — every pre-IF-1 mount), `FireRefusal`, three
`ActionReason`s and `BallisticProjectile.ArcLifetimeTicks` — §3.6.*

*What the picture shows that prose hid: the only new runtime type on the combat side is `FireSolution` (a pure function); the charge table
lives on the EXISTING ammo × weapon profile, the arc mode on the EXISTING mount, the timing in the EXISTING `FireAtPoint` params (32 bytes
exactly: 12 + 4 + 4 + 1 + 1 + pad 2 + 8). The battery is one more `ISquadPlay`, not a new runner.*

### 3.3 Low or high — the choice (`FireSolution.For`, pref = Auto)

```mermaid
flowchart TD
  A["mount.Arc"] -->|HighOnly: a mortar| H
  A -->|LowOnly| L
  A -->|Both: a howitzer, a grenade launcher| B{"lowest charge whose LOW arc reaches?"}
  B -->|none| H
  B -->|yes| C{"ThrowArc.Clears(low arc)<br/>through terrain + buildings,<br/>to 0.3 m short of the point?"}
  C -->|yes| L["LOW: lowest charge that reaches<br/>(shortest time of flight)"]
  C -->|no: a ridge, a wall, a street canyon| H{"lowest charge whose HIGH arc<br/>reaches within MaxElevation?"}
  H -->|yes, and clears| HI["HIGH"]
  H -->|no| R["Refused: TooFar / TooClose / Masked"]
```

*What the picture shows: "visibility" is the ARC's clearance, not the gun's sight line — a gun firing indirect rarely sees the target, and
the straight sight line is not the trajectory. A target in a courtyard or a street fails the low arc's last samples against the walls around
it, so it falls to high without a special rule.*

### 3.4 Fire for effect, time on target

```mermaid
sequenceDiagram
  participant Op as operator / observer
  participant B as battery (ArtilleryUnitStrike)
  participant P as BatteryFireMissionPlay
  participant G as gun i (FireAtPoint)
  participant S as SimHost (FireProcessingSystem)
  Op->>B: Strike(point, FireForEffect, Rounds 3, Sheaf Parallel, Control TimeOnTarget)
  B->>P: Start: per gun FireSolution.For (refuse a gun out of range)
  P->>P: danger close? a friend within 600 m and no flag, then Fail
  P->>P: BeginIssue: aim_i = point + sheaf offset_i, lay_i = TurretSlew.SecondsTo(gun i's pose, its solution)
  P->>P: T = now + max_i(lay_i + tof_i) + 2 s (CE-1109)
  P->>G: FireAtPoint(aim_i, rounds 3, fireAt T - tof_i)
  G->>G: hold until fireAt (laid, waiting)
  G->>S: WeaponFireIntent(aim_i, Charge, HighArc)
  S->>S: launch at that charge and arc, plus HitModel dispersion
  Note over S: every gun's first round lands at T
  P->>P: SquadGates: every gun's rounds gone, then Complete
  P-->>Op: rounds complete, end of mission
```

### 3.5 Adjust fire — AS BUILT (IF-3, `2026-10-15`)

```mermaid
sequenceDiagram
  participant O as observer (PersonObserveFire)
  participant P as battery (ArtilleryUnitStrike)
  participant A as adjusting gun
  participant S as SimHost
  participant M as RecentDetonations (observer's node)
  O->>O: sees the target? else Failure
  O->>P: Strike(target + location error, Adjust, Rounds 1, Observer self)
  P->>A: FireAtPoint(call, rounds 1) — the gun nearest the battery's centre
  A->>S: WeaponFireIntent
  S-->>M: DetonationNotification (ingress on a Brain-only node)
  O->>M: a burst after my call, from the battery's guns, that my eye sees
  O->>O: judge it (1 % of the distance), split into range and lateral
  O->>P: Strike(call corrected, Adjust): lateral out at once, range drop 800
  Note over O,P: add 400, drop 200, drop 100 — the bracket halves once a round has burst on each side
  O->>P: Strike(call, FireForEffect, the mission's rounds and sheaf) — within 50 m, or split to 50
  O->>M: the next burst: end of mission (Success)
  Note over O: no seen round for 180 s: observer lost (Failure) · killed: no call comes
```

*What the picture shows that prose hid: there is no adjust event — the observer sends Strikes, the battery runs a fresh one-round mission per
call, and so the corrections cross brain nodes by the order channel that already exists (`TacticalIntentEgress/Ingress`). The battery never
learns where its rounds burst; only the observer does.*

### 3.6 IF-1 as built — backend, `2026-10-15`

| item | where it landed | deviation from §3.2 / §5, and why |
|---|---|---|
| **I1** `FireSolution` | `Fdp.Toolkits/Combat/FireSolution.cs` — `ChargesOf` (the weapon's own ammo × weapon profile first, else the round's generic one) and `For` (low then high, charges ascending, `ArcLaunch` per charge, elevation window, `ThrowArc.Clears` sampled every 25 m, 12–400 samples) | ⭐ a masked LOW arc stops the low search (a faster low arc is flatter — it will not clear either), a masked HIGH arc tries the next charge (a faster high arc is steeper). `Clears` gained a `samples` overload; hand throws keep 12 |
| **I2** mount / ammo fields | `WeaponMountDto.Arc / MinElevationDeg / MaxElevationDeg`; `AmmoWeaponBallisticsDto.ChargeSpeedsMps` | ⭐ `IndirectArc.Unset = 0` added: the field defaults to "no solution, `ThrowArc` as today", so no existing mount moved. ⛔ named `Unset`, not `Default` — `default` is an IDL keyword and the IDL generator scopes a descriptor file's enum members together |
| **I3** intent | `WeaponFireIntent.Charge` (1-based, 0 = none) + `HighArc`; the wire `WeaponFireRequest` APPENDS both (R-169) | — |
| **I4** arc dispersion | `FireProcessingSystem`: an indirect round's launch = the charge's `ArcLaunch` (or `ThrowArc` when Charge = 0), then rotated by θ = `HitModel.Deflection` and scaled by `1 + ½·σ·HitModel.RangeSequence(k)` (a second Weyl stream on √2 − 1) | ⭐ every existing indirect mount (M67, 2005) has `DispersionMils = 0` ⇒ σ = 0 ⇒ no number moved |
| **I5** lifetime | `FireProcessingSystem.ArcLifetimeTicks` = max(60 s, time of flight + 10 s) onto `BallisticProjectile.ArcLifetimeTicks`; `BallisticsSystem` honours it | never shorter than the 60 s it was |
| **I6** `FireAtPoint` | a controlled weapon (32 + k, or mount 0 on a unit with no weapon of its own = its primary) fires AS ITSELF (shooter = the tube, mount 0), behind `SystemHold` + the station arc; holds Running + `AwaitingFireTime` until `FireAt`; a mount with an `Arc` gets a solution or fails `OutOfRange` / `TooClose` / `Masked` | `FireAtPointParams` = 32 bytes (12 + 4 + 4 + 1 + 1 + pad 2 + 8). ⛔ the shared node `FireAtPointNodeParams` and the behaviour DTO `fireAt` / `arc` are **IF-2** (behaviors lane) — not touched |
| **I7** rounds | `MunitionTkbCatalog` 6026 60 mm · 6002 81 mm (charges added) · 6027 120 mm · 6028 105 mm · 6029 155 mm HE; `ammo.json` + "105mm howitzer HE", "155mm howitzer HE"; charges `v = √(g·R)` per charge range table | charges on the round's GENERIC profile (each round has one gun today) |
| **I8** guns | weapon systems 609 60 mm (OneMan) · 610 81 mm · 611 120 mm (TwoMan) — Mortar 1.1.*.10, HighOnly 45–85° · 612 105 mm towed (Static) — TowedArtillery 1.1.*.5, Both −5–70°; vehicle 107 M109 — 1.1.225.4, turret, Both −3–75°, crew of five; unit 304 Artillery Battery — 11.1.225.4, three M109s | ⭐ `WeaponCategory.Howitzer = 10` appended (a howitzer is neither `Mortar` nor `Cannon`: per-shot brain, no fire on the move). `MortarTeam` 2005 unchanged |

Rails: `FireSolutionTests` (4) · `BallisticsSystemTests.IF1_*` · `FireProcessingSystemTests.IF1_*` · `AimAndFireExecutorTests.IF1_*` (the
weapon-systems fixture: a gunner fires the mortar he mans, nothing before `FireAt`; no solution fails with why; params ≤ 32) ·
`NedTkbBuilderCombatTests.IF1_*` · `HrotEnvironmentTests.IF1_*` · the LR-1 explosive set and the type-picker golden re-pinned on purpose.
Report: [`REPORT_Backend_Indirect_Fire.md`](../../blueprints/batches/REPORT_Backend_Indirect_Fire.md).

### 3.7 IF-2 as built — behaviors, `2026-10-15`

| piece | where it landed | deviation from §3.2 / §5, and why |
|---|---|---|
| Strike fields (IF-D) | `StrikeOrder.Method / Rounds / Sheaf / SheafWidthM / Control / ImpactInS / Observer / DangerClose` + enums `FireMissionMethod`, `FireMissionSheaf`, `FireMissionControl` (`Orders/OrderDtos.cs`) | ⚠ `Munition` not added yet (one round per gun today); ⚠ `Method = Adjust` fires for effect with a warning until IF-3 |
| the member order (FM-2) | `MemberOrderKind.FireAtPoint = 7`; `MemberOrder.Rounds / FireAt / Mount / Arc`; `SquadManeuverRunner.Fire` publishes the library `FireAtPoint` (a unit member holds) | — |
| the shared node | `FireAtPointNodeParams.Arc / FireAt` (appended after `Started`) → the executor's params; `FireAtPointParamsJsonDto.fireAt / arc` | — |
| the battery (FM-1) | `ArtilleryUnitStrike` → `BatteryFireMissionPlay` over `SquadManeuverRunner`, maneuver `BatteryFireMissionManeuver` (kind 14: Firing · Aborted) | ⭐ the plan (each gun's mount, aim, time of flight, rounds at the start) lives in the play's PARAMS (`BatteryFireParams`), made once at Start; every issue gives each gun only the rounds it has LEFT (a resumed mission never fires twice) |
| sheaf / TOT / danger close (FM-3/4/5) | `IndirectFireNodes.AimPoint`, `BatteryFireMissionPlay.Plan`, `IndirectFireNodes.DangerClose` (= `HiddenTargetThrow.FriendlyInBurst` at 600 m — reused, not rewritten) | an aim point out of reach falls back to the point; open-sheaf width defaults to guns × max(2 × injury radius, 20 m) |
| the mount | `IndirectFireNodes.MountOf`: the gun's own first mount with an `IndirectArc`, else the first controlled weapon that has one (32 + k) | — |
| one gun (FM-1) | `SelfPropelledArtilleryStrike` → the library `FireAtPoint` node (danger close or no indirect weapon ⇒ Failure at once) | no sheaf, no TOT across guns — `ImpactInS` only delays its first round |
| rails | `SquadManeuverTests.CE2176_*` ×4 (TOT, sheafs, out of range + danger close, resumed mission + rounds complete) · `TacticalIntentResolutionSystemTests.CE2176_*` | red-proofed by four mutants (§5 IF-2) |

### 3.8 IF-3 as built — behaviors, `2026-10-15`

| item | where it landed | deviation from §3.2 / §4, and why |
|---|---|---|
| **the burst on the brain** | ⛔ superseded `2026-10-17` by [Node Roles §3.5](../../DESIGN_Node_Roles_And_Policies.md) (CE-1148 S2): the ingress is in the shared pack on every node, own samples dropped. Prior: `MunitionDetonationIngressTranslator` now also on a Brain with neither MuscleGround nor Perception (`SimHostAuxiliaryTranslatorPack`, cross-lane — backend's file); `RecentDetonations` (a recorded world singleton of 64 bursts, like CE-3117's `DetonationTraces` — which it is NOT: that one fills only where damage is assessed, keeps 8 and no shooter) + `DetonationMemorySystem` in `CgfLogicPack` (`Fdp.Toolkits/Combat/RecentDetonations.cs`) | 📐 measured first: no brain node saw a detonation (the ingress was MuscleGround / Perception-only), and no brain behaviour reads the bus — brains tick on their own period. On a Brain only the memory and the map's `EffectSpawnSystem` read the event (no damage, heat or sound system runs there), so the CGF map now shows bursts too. ⚠ The sensing design keeps brains to anonymous sensor answers (`DESIGN_Thermal_And_Acoustic_Sensing.md` D); this is narrower than omniscience — the observer still needs its own line of sight (`IWorldQuery.SightBlocked`, eye 1.7 m → burst + 2 m) and judges with an error |
| **the corrections** | `PersonObserveFireBehavior` sends every call as a `StrikeOrder` to the battery (`Order.Strike`, Superior) | ⛔ **`FireMissionAdjustEvent` NOT built** (IF-E superseded): a Strike already crosses brain nodes and replaces the running mission (FM-8); a new event would need a new topic. IF-E's reason survives — the observer owns the adjust, so killing him stops it |
| **the order** | `OrderKind.ObserveFire = 5`, `ObserveFireOrder { Location, Target, Battery, Locating, Rounds, Sheaf, SheafWidthM, Control, DangerClose }`; `Person` class → `PersonObserveFire` | — |
| **the adjusting round** | `BatteryFireParams.Adjust`: one round, converged, when ready, from the able gun nearest the able guns' centre (FM-6); `SelfPropelledArtilleryStrike` fires 1 round | the battery has no adjust *phase*; each call is its own mission |
| **the bracket** (FM-6) | `PersonObserveFireBehavior.Correct`: lateral removed at once; range first step = the smallest of 50 / 100 / 200 / 400 / 800 that covers the error; halve once a round has burst on each side; fire for effect within 50 m, or when split to 50 | — |
| **errors** (IF-F, observer half) | the first call off by ≤ 150 m (map) / 10 m (laser); each judged burst off by ≤ 1 % of its distance; both deterministic (`AcousticPerception.Error`, the hash hearing uses) | ⚠ not keyed on the mission id `k` as IF-F said: keyed on (observer, battery) and (observer, burst serial) — replay-exact all the same |
| **observer lost** | no battery round seen 180 s after a call ⇒ Failure; after fire for effect ⇒ Success (*"end of mission (not observed)"*) | FM-7's *shot* / *splash* messages to the observer are not sent — he waits for what he sees |

Rails (`SquadManeuverTests`): `CE2176_AnAdjustingRound_IsOneRoundFromTheGunNearestTheBatteryCentre`,
`CE2176_TheObserver_BracketsTheRoundsOntoTheTarget_ThenCallsFireForEffect` (800 → 400 → 200 → 100, then fire for effect),
`CE2176_TheObserver_IgnoresAStrangersBurst_AndIsLostWhenHeSeesNoRound`, `CE2176_TheBurstMemory_RecordsEachDetonation`; the pack rails
(`SimHostAuxiliaryTranslatorPackTests`, `CgfLogicPackTests`) moved by one each.

### 3.9 IF-4 as built — behaviors, `2026-10-15`

⭐ The demo passes live (`python3 scripts/utility-demo-check.py ua-indirect-fire --launch`): every §6 vignette, at sim time ≈ 131 s.

| vignette | measured |
|---|---|
| ① | 3 adjusting rounds from ONE tube (119 → 85 → 14 m), then fire for effect from all three: 6/6 within 120 m of the squad behind the ridge block |
| ② | the three first rounds within 2 ticks (time on target); the farthest burst 41 m from the point (dispersion + parallel sheaf) |
| ③ | the courtyard round flies the HIGH arc — 416 ticks against ②'s 172–204 |
| ④ | refused *"danger close"* with friends 400 m off; fires once resent with `DangerClose` |

| found live | fixed where | why the rails missed it |
|---|---|---|
| an HTTP order's `Location` is a geodetic `[lat, lon]`; a local `[y, x]` was dropped silently on the brain | `OrderMenu.MalformedIn` refuses it 400; `BehaviorIngressSystem` logs a dropped order (cross-lane); the check sends lat/lon (`geo_of`) | rails build the order in-process, never through the wire |
| the gunners boarded the LOADER's seat (a plain `Embark` takes the first Passenger seat) | the library behaviour `ManWeapon` (`EmbarkNodeParams.Role = Gunner`) — [Weapon Systems §9 WS-6](../weapons/DESIGN_Weapon_Systems.md) | the IF-1 rails seat the man with `TryEmbark(…, Gunner)` directly |
| a man in a weapon's Gunner seat lost `CanShoot`, so `WeaponDispatcherSystem` failed every FireAtPoint before the executor ran | `Embarkation.StrippedOn` — that seat takes only `CanMove` (cross-lane) | the IF-1 rails call the executor directly, skipping the dispatcher's gate (their `MannedMortar` premise now asserts it) |
| ⚠ not a defect: §6's **6 m** walls are right | 📐 swept against the real M109 arcs: the low arc is masked at every height 4–20 m; the high arc clears for all three guns at ≤ 10 m, for two at 12 m, for none at 20 m | — |

Layout: the M109s at (−3500, −40) / (−3800, 0) / (−4100, 40) (≈ 4.4–5.0 km, three ranges); the 81 mm section at x = −1700 (≈ 2 km from ①'s point).

### 3.10 Laying the gun — the elevation on the wire (ui handoff P2, `2026-10-15`)

📐 **Measured:** the weapon muscle already slews a turret part's `TurretPose` — replicated (`dtTurretPose`, W-9) and read by the map —
from a `WeaponTask` (`WeaponTaskDispatcherSystem`), but **(a)** the mortars and the towed gun have NO turret part (`turret: null`),
**(b)** `FireAtPoint` never orders a turret (it publishes the round straight away), and **(c)** the muscle's indirect aim is ONE speed's arc
(`BallisticAim.Solve`), not the fire solution. ⇒ three small changes, no new replication.

```mermaid
classDiagram
  class FireAtPointExecutor { <<existing, grows>> +lays an indirect mount's turret: WeaponTask Track at the point ★ · fires once OnTarget ★ }
  class WeaponTask { <<existing, dtWeaponTask, grows>> Mode · Point · MountIndex · AmmoGuid · Epoch · +Arc ArcPreference ★ (appended) }
  class WeaponTaskDispatcherSystem { <<existing muscle>> slews TurretPose, reports OnTarget }
  class BallisticAim { <<existing, grows>> +Solve(…, arc, ammo): a LAID order (arc Low or High) aims along FireSolution.For(no mask).Launch ★ }
  class FireSolution { <<IF-1>> ChargesOf · For }
  class TurretPose { <<existing, dtTurretPose>> Azimuth · Elevation }
  class BdcTkbCatalog { <<existing>> mortars 609–611, towed 612: + a laying turret 0 ★ · mount TurretIndex 0 ★ }
  FireAtPointExecutor --> WeaponTask : writes (brain)
  WeaponTaskDispatcherSystem ..> WeaponTask : reads (muscle)
  WeaponTaskDispatcherSystem ..> BallisticAim
  BallisticAim ..> FireSolution : Arc ≠ Unset
  WeaponTaskDispatcherSystem --> TurretPose : slews
```

*What the picture shows that prose hid: the brain still never computes an angle (M24) — it names the point, the arc its solution chose and
the round; the muscle solves the same `FireSolution` for that arc and round, and the pose it slews is the one the map already reads on
every node.*

```mermaid
sequenceDiagram
  participant B as FireAtPointExecutor (Brain, CGF)
  participant T as WeaponTask on the tube's turret part
  participant M as WeaponTaskDispatcherSystem (Muscle, SimHost)
  participant P as TurretPose (replicated)
  B->>B: FireSolution.For (charge, arc) as IF-1
  B->>T: Track at the point, the chosen arc (Low or High), the round (a new epoch only when one changes)
  M->>M: BallisticAim.Solve: FireSolution.For(no mask, that arc and round).Launch, then hull-frame angles
  M->>P: slew at the turret's rates, then OnTarget
  Note over P: every node sees the laid elevation now, before any round
  B->>B: FireAt not reached: AwaitingFireTime (laid and waiting)
  B->>B: status not OnTarget for this epoch: Slewing (held)
  B->>B: OnTarget and time: ShootAtPoint (the round flies the intent's charge and arc)
```

| rule | why |
|---|---|
| only a mount that declares an `IndirectArc` is laid; every other FireAtPoint fires as before | ⛔ no number moves for a grenade, a rifle burst at a window, a tank at a point |
| a mortar / the towed gun get a **laying turret** (all round — the crew shifts the baseplate / trails — inside the mount's elevation window; slow rates) rather than a separate elevation field | ⭐ the handoff's "reuse W-9": the turret part is what replicates; cheaper than a field and a translator |
| the order is re-issued (a new epoch) only when the point, the arc or the mount changes | the muscle's OnTarget report is per epoch |

#### As built (`2026-10-15`, ui handoff P2 — report [`REPORT_Backend_Presets_Elevation_Attitude.md`](../../blueprints/batches/REPORT_Backend_Presets_Elevation_Attitude.md))

| what | as built | deviation from the design above |
|---|---|---|
| the order | `FireAtPointExecutor.Lay` writes `WeaponTask { Track, Point, MountIndex, AmmoGuid = the loaded round, Arc }` on the mount's turret part; a new epoch when the point, the arc, the mount or the round changes; reads `WeaponTaskStatus` under that epoch: OnTarget ⇒ fire, Failed OutOfArc ⇒ the action fails `OutOfArc` (turrets released), else held `Slewing` | ⭐ **the arc on the order is the arc the brain's solution CHOSE (Low / High), not the caller's preference** — the muscle then never re-decides it |
| the muscle | `BallisticAim.Solve(…, arc, ammo)`: arc Low / High and an `IndirectArc` mount ⇒ `FireSolution.For(world: null, pivot, point, mount, ChargesOf(round), arc).Launch`; arc **Auto** (every Engage order) ⇒ the one-speed arc as before | ⭐ **no mask check on the muscle** (it runs per frame; the brain already chose the arc with it). Same charge as the brain's unless a mask made the brain step up a charge on the HIGH arc — then the lay differs by that step and the round still flies the brain's charge. ⭐ **Engage keeps the one-speed aim**: its rounds carry no charge, so aim and flight stay matched |
| the order of holds | solution (fail with why) → lay → `FireAt` (`AwaitingFireTime`) → OnTarget (`Slewing`) → bomb cue → `ShootAtPoint` | — |
| `OnExit` | releases the turrets (Idle — the pose STAYS laid) | new: before, FireAtPoint ordered no turret |
| the guns | 609–611: laying turret 0, all round, 45–85°, pivot 0.3 m; 612: −5–70°, pivot 1.0 m; all **30 °/s traverse, 15 °/s elevation** (a full lay ≈ 6 s; ⭐ since `CE-1109` FM-4 reads each gun's own lay, so a slow turret only moves T later — ⛔ SUPERSEDED: "inside FM-4's 10 s"); mount `TurretIndex = 0`. The M109 keeps its own turret (10 / 5 °/s) | ⚠ the M109's worst case (180° at 10 °/s = 18 s) exceeds FM-4's lay time — its first round can be late on a time-on-target mission (report lean) |
| the wire | `WeaponTask.Arc` + `EntityWeaponTask.Arc` (byte, APPENDED, R-169) | — |

Rails: `AimAndFireExecutorTests.P2_AMortar_IsLaid_OnTheSolutionsElevation_BeforeItsFirstRound` (pose within 0.5° of the solution 10 s
before `FireAt`, nothing before it) · `P2_ASelfPropelledGun_TurnsItsTurret_BeforeItFires` · `EntityTurretPoseTranslatorTests.WL2b_…`
(the arc crosses) · `NedTkbBuilderCombatTests.IF1_…` (the laying turrets).

### 3.11 The spotter — an observer who picks his own target *(`CE-2308`, behaviors, 2026-10-19 — Urban Battle §4 ⑥, §5 step 5; build-state: BUILT — §3.11a; leans SP-1…SP-5, built under the user's "go ahead autonomously")*

📐 **Measured first (INVENTORY, 2026-10-19 — grep `ObserveFire|Spotter|SpotFor` over `Hrot/Subsystems/Hrot.AI.Behaviors` ⇒ `PersonObserveFire`
and its order only; reads of `PersonObserveFireBehavior.Observe` (§3.8), `DroneTargets.Pick`, `KillZone.Entered` (CE-2306); backend's note
`NOTE_Backend_To_Behaviors_Urban_Battle_Data` (BR-14: mortars carry a report)):** the observer loop is built and needs a TARGET handed to
it (`ObserveFireOrder.Target` / `Location`) — ⛔ **nothing picks one from what a man sees**, so an insurgent mortar fires only where a
scenario aims it.

```mermaid
classDiagram
  direction LR
  class SpotForFireBehavior { <<NEW, Hrot.AI.Behaviors/Brains>> SpotForFire(Battery, MaxMetres, Rounds, RestSeconds) · Spot(p, ws, self, world) }
  class SpotForFireParams { <<NEW>> Observe (ObserveFireParams) · MaxMetres · RestSeconds · Watching · RestUntil }
  class FireTargets { <<NEW static>> Pick(world, observer, maxMetres) — from HIS memory }
  class PersonObserveFireBehavior { <<existing IF-3>> Observe(ref ObserveFireParams) — call, watch, correct }
  class TargetMemory { <<existing>> what he saw }
  SpotForFireBehavior --> SpotForFireParams
  SpotForFireBehavior ..> FireTargets : a target
  FireTargets ..> TargetMemory
  SpotForFireBehavior ..> PersonObserveFireBehavior : the mission, unchanged
```
*What the picture shows that prose hid:* the spotter is the observer with a target picker in front of it — the adjust loop is called as it
is, so every IF-3 rule (seen bursts only, the bracket, observer lost) holds for the insurgent too.

```mermaid
sequenceDiagram
  participant S as the spotter (SpotForFire)
  participant M as his memory
  participant O as Observe (IF-3, unchanged)
  participant B as the mortar (battery)
  S->>M: a hostile he identified, within MaxMetres — the biggest group, vehicles first
  S->>O: Target = where it is
  O->>B: Strike Adjust … then fire for effect
  O-->>S: Success (end of mission) or Failure (lost)
  S->>S: rest RestSeconds, then watch again
```
*What the picture shows that prose hid:* the spotter never ends on its own — it is a standing job, one mission after another.

| id | decision (⭐ lean) | rejected — one line each |
|---|---|---|
| **SP-1** | ⭐ a library behaviour `SpotForFire {Battery, MaxMetres 1500, Rounds, RestSeconds 30}` — watches, picks, runs the IF-3 `Observe` on the pick, rests, repeats; Running for good | a reaction in his SOP: a reaction ends, and the spotter's job is the task itself |
| **SP-2** | ⭐ **what he picks** — from HIS memory only: an identified (not anonymous), live, hostile (another force, not neutral) contact within `MaxMetres`; the one with the most such contacts within 30 m of it, a vehicle counting double | the nearest: a lone rifleman beats a halted convoy · ground truth: the spotter is the thing to kill or blind |
| **SP-3** | ⭐ **the target is where it was SEEN when picked** (a fixed point); Observe's own "I cannot see the target" check refuses one out of his sight | a moving aim: the adjust loop brackets a point, not a mover |
| **SP-4** | ⭐ **after a mission (either end) he rests `RestSeconds`**, then watches again — the cell's tempo, and time for the target to move | at once: a mortar that fires on the same men for ever |
| **SP-5** | ⭐ danger close stays the battery's (FM-5, `DangerClose`); the spotter adds nothing | a second friend check: two owners for one rule |

**Slice.** SP-1…SP-4; rails: the pick (a group beats a lone man, a vehicle doubles, a neutral / an unidentified / one beyond `MaxMetres`
are never picked); the spotter calls an adjusting Strike on the pick, then rests after the mission and calls again. ⛔ Not in it: the
insurgents' own battery unit (an author's mortar team — the demo), counter-battery (IF-6).

#### 3.11a As built *(2026-10-19 — the diagrams above hold)*

| piece | where | as built |
|---|---|---|
| the behaviour | `Brains/SpotForFireBehavior.cs` (`SpotForFireBehavior`, `SpotForFireParams`), `BehaviorNames.SpotForFire` | a plain `Action` over `SpotForFireParams` — it embeds the IF-3 `ObserveFireParams` and calls `PersonObserveFireBehavior.Observe` on it unchanged; Running for good (SP-1); Failure only when the battery is gone |
| the order | `Orders.SpotForFireOrder` (same file; `[ParamsDto]`) | `Battery` · `MaxMetres` (1500) · `RestSeconds` (30) · `Locating` · `Rounds` · `Sheaf` · `SheafWidthM` · `Control` — the IF-3 mission fields minus the target |
| the pick | `FireTargets.Pick(world, observer, maxMetres, out at)` | his `TargetMemory`: identified, alive, `KillZone.Hostile` (the trigger man's test, shared), within `maxMetres`; score = the weights within 30 m (a vehicle 2, a man 1); `at` = the REMEMBERED position (SP-3) |

⚠ **Choices the leans did not settle:** the hostility test is the trigger man's (`KillZone.Hostile`, one copy, not a third); the
drone's own (`DroneTargets`) stays separate because there an UNKNOWN side counts as hostile; the first `Observe` runs in the tick
that picks (so the Adjust call goes at once, not a frame later).

Rails `SquadManeuverTests.CE2308_*` ×2 (in the IF-3 suite, its `Battery` harness) — mutants 6/6 killed. ⛔ Not live yet.

## 4. Decisions — approved `2026-10-14` (R-2018)

| # | decision | rejected (one line each) |
|---|---|---|
| **IF-A** | ammunition first: **HE for 60 / 81 / 120 mm mortars and 105 / 155 mm howitzers** (reference `ammo.json` gains the howitzer rows); **smoke** in IF-5 (a sight-blocking cloud); illumination later | all natures at once — smoke needs a cloud the sight model reads, a design of its own |
| **IF-B** | **charges**: `AmmoWeaponBallisticsDto.ChargeSpeedsMps` — a speed per charge for this round from this weapon. ⭐ The speeds are *vacuum-equivalent* (`v = √(g · R_max)` per charge) so the RANGE table is right without drag; the time of flight is short of a real one — accepted. `FireSolution.For` picks the lowest charge that reaches on the chosen arc and returns the time of flight from the same closed form | a fixed speed per tube (today: one range for every distance, 45° when short) · drag (a numeric integrator for a number nobody reads) |
| **IF-C** | **low or high per mount**: `WeaponMountDto.Arc` — `HighOnly` for a mortar, `Both` for a howitzer and a grenade launcher (⭐ `ThrowArc` stays for hand throws); `Auto` takes low when the low arc clears to the point (§3.3), else high, else refused with a reason | choosing by the gun's sight line (the trajectory is not the sight line) · a separate "dead ground" query (the arc's own clearance already answers it) |
| **IF-D** | **Strike is the fire mission.** `StrikeOrder` gains optional fields: `Method` (FireForEffect · Adjust), `Rounds` (default 3), `Sheaf` (Converged · Parallel · Open) + `SheafWidthM`, `Control` (WhenReady · TimeOnTarget) + `ImpactInS`, `Munition` (0 = the gun's own), `Observer`, `DangerClose`. RTS defaults: fire for effect, when ready, converged, 3 rounds. An aircraft reads only `Location` / `Target` | a separate `FireMission` order (two orders for "hit that place"; the menu would offer both) |
| **IF-E** | **the observer** (slice 3): `ObserveFire` on any soldier who sees the point — it sends Strike(Method Adjust, Observer self) to a battery and corrections as `FireMissionAdjustEvent` *(⛔ superseded as built: every correction is another Strike — §3.8)*; the battery waits for corrections, and an observer who is killed or loses sight ends the mission (*"observer lost"*). An operator calls fire from the menu: Strike filled from the designations | the battery computing the corrections itself (kill the observer and the adjust goes on — no observer) |
| **IF-F** | **error, replayable**: gun dispersion on the ARC (azimuth θ from `HitModel.Deflection`, range error as a speed factor on a second ordinal stream; the mount's `DispersionMils`) — fixing the measured loss at `FireProcessingSystem.cs:177`; target-location error on the observer's call from `d(k)` with k = the mission id, scaled by how it was located (laser ~10 m, map/eye ~150 m) | a random generator (none exists; replay would diverge) · perfect first rounds (no reason to adjust) |
| **IF-G** | **the guns**: 60 mm a `OneMan` weapon system; 81 / 120 mm `TwoMan` (team carry, §7.5) — all class `Mortar` 1.1.*.10, crewed through the Gunner seat; a 105 mm towed howitzer `TowedArtillery` 1.1.*.5, an emplaced weapon system (no towing yet); a 155 mm self-propelled howitzer `SelfPropelledArtillery` 1.1.*.4, a vehicle with a turret and a preset crew (W-I) | howitzers as soldiers with built-in mounts (the `MortarTeam` 2005 shape — nothing to crew, carry or kill separately) |
| **FM-1** | **a virtual battery commander**: a unit entity of class `ArtilleryUnit` (11.1.*.4) whose roster is its guns (or their gunners); Strike resolves to **`ArtilleryUnitStrike`**, which runs `BatteryFireMissionPlay` over `SquadManeuverRunner`. A Strike to a self-propelled gun resolves to **`SelfPropelledArtilleryStrike`** — the same play with a roster of one | every gun deciding for itself (no sheaf, no time on target) · `MortarStrike` on a manned tube (the weapon has no brain — W-D) |
| **FM-2** | **one new member order**: `MemberOrderKind.FireAtPoint { Point, Rounds, FireAt, Arc }` → the library `FireAtPoint` behaviour, whose params DTO gains `fireAt` and `arc`; the member's mount is its first INDIRECT mount, own or controlled (`IndirectFireNodes.MountOf`) | a gun-specific behaviour (`FireAtPoint` already is the mechanism) |
| **FM-3** | **the sheaf**: Converged — every gun at the point; Parallel — each gun's aim offset by its own lateral offset in the battery, perpendicular to the line of fire; Open — guns spread evenly over `SheafWidthM` (default guns × 2 × the warhead's burst radius) | one aim point always (a 6-gun sheaf on one spot) |
| **FM-4** | **time on target**: T = now + max over the guns of (its own LAY time + its time of flight) + a 2 s margin (`LayMarginSeconds`: the OnTarget settle and one think), floor `ImpactInS`; gun i fires at T − tof_i, so every first round lands together and every turret is laid by its fire time; `WhenReady` fires on arrival of the order. ⭐ `CE-1109`: the lay is `IndirectFireNodes.LaySecondsFor` = `TurretSlew.SecondsTo` (`Step`'s arithmetic in closed form, beside it) from the gun's CURRENT `TurretPose` to the azimuth / elevation `BallisticAim.Solve` gives for its solution's arc — 0 for a mount with no turret part (it fires unlaid), the worst case (turned all the way round) when this node holds no pose. ⛔ SUPERSEDED: a fixed 10 s lay for every gun | a fixed delay per gun (wrong as soon as two guns stand at different ranges) · ⛔ a fixed lay time (an M109 at 10 °/s turning 180° needs 18 s) · raising the catalogue's turret rates (the user: the number should be real) |
| ✅ **FM-4 closed (`2026-10-16`, behaviors, [CE-1109](../../blueprints/batches/HANDOFF_Ui_To_Behaviors_FM4_Lay_Time_From_The_Turret.md))** | built as the FM-4 row says. Rails: `WeaponTaskDispatcherTests.CE1109_SecondsTo_IsStepsOwnArithmetic_InClosedForm` (full circle short way, limited arc long way inside it, the slower axis, the elevation limit, a rate of 0, and stepping `Step` lands within a frame); `SquadManeuverTests.CE1109_TimeOnTarget_LeavesEachTurretItsOwnLayTime_AGunFacingAwayIsLaidBeforeItFires` (three M109-rate guns, one facing away: ≈ 18 s lay, every gun's fire time after its own lay, the first rounds together). A mortar's lay (≤ 6 s at 30 / 15 °/s) + 2 s is below the old 10 s, so a mortar mission's T moves EARLIER | the open row (the fixed 10 s against a real M109) — kept as this row |
| **FM-5** | **danger close**: a friendly unit within 600 m of the point (any gun's round) and `DangerClose` not set ⇒ the play fails with *"danger close"*; set ⇒ fire, and the adjust (slice 3) creeps in by 100 m steps | firing regardless (a fire mission is the easiest way to kill your own platoon) |
| **FM-6** | **adjusting**: one adjusting gun (the one nearest the battery's centre), one round per correction, bracketing 800 → 400 → 200 → 100 → 50 m along the observer–target line; within 50 m ⇒ fire for effect with every gun | adjusting with the whole battery (wastes rounds, warns the target) |
| **FM-7** | **messages**: the play reports `shot` (fired), `splash` (5 s before impact), `rounds complete` and `end of mission` on the behaviour log and to the observer | silent missions (nobody can follow one live) |
| **FM-8** | **one mission at a time**: a new Strike replaces the running one (same rank, Superior); the replaced observer gets *"end of mission"* | a mission queue (later, when a scenario needs priorities of fire) |

## 5. Slices

| # | lane | what | rails (the features' own suites) |
|---|---|---|---|
| **IF-1** ✅ BUILT `2026-10-15` (§3.6) | backend | `FireSolution` + `Solution`; `WeaponMountDto.Arc / Min/MaxElevationDeg`; `AmmoWeaponBallisticsDto.ChargeSpeedsMps`; `WeaponFireIntent.Charge / HighArc` (APPENDED, R-169); `FireProcessingSystem` flies the intent's charge and arc, adds arc dispersion (IF-F); an arc round lives its time of flight + 10 s; `FireAtPointExecutor` fires a CONTROLLED weapon as itself and holds until `FireAt`; the munitions (IF-A) and the guns (IF-G) | `ThrowArcTests` / a new `FireSolutionTests` in the same folder: lowest charge, low vs high by clearance, too far / too close / masked; `FireProcessingSystemTests`: the round lands at the point within the dispersion, a 90 s flight still lands; `FireAtPointExecutorTests`: a gunner fires his ground mortar, nothing before `FireAt` |
| **IF-2** | behaviors | `StrikeOrder` fields (IF-D); `MemberOrderKind.FireAtPoint`; `FireAtPoint` DTO `fireAt` / `arc`; `BatteryFireMissionPlay` (fire for effect, sheaf, time on target, danger close, rounds complete); `IndirectFireNodes`; `ArtilleryUnitStrike`, `SelfPropelledArtilleryStrike` | `SquadManeuverTests` (the play's own suite): three guns' first rounds land within one tick of T; a parallel sheaf's aim points; a gun out of range is left out; danger close refuses · `TacticalIntentResolutionSystemTests`: Strike on a battery / SP gun resolves, on a rifleman does not |
| **IF-3** | behaviors | `ObserveFire`, `FireMissionAdjustEvent`, the adjust phase, target-location error, observer lost. ⛔ **measure first**: how the observer learns where a round burst (no detonation event a brain reads was found — `Combat/Events`, `Perception` searched; hearing exists in `HeardTraces`) and whether the adjust event crosses nodes the way `AssignTacticalIntentEvent` does | `SquadManeuverTests`: the bracket closes 800 → 50 and turns to fire for effect; observer killed ⇒ end of mission |
| **IF-4** ✅ BUILT `2026-10-15` (§3.9) | behaviors | the demo `ua-indirect-fire` (§6) + its range terrain; `utility-demo-check.py` entry | the live check |
| **IF-5** | both | smoke: a cloud the sight model reads; the play's `Munition` picks it | — (DESIGN) |
| **IF-6** | both | counter-battery: a radar that locates a gun from its rounds' arcs; a battery that fires back and moves (shoot and scoot) | — (DESIGN) |
| ui | ui | draw the tubes, the towed and SP howitzers, the gunners in their seats, a shell in flight, the burst; the sheaf's aim points on the map overlay (after IF-1) | — |

⛔ **Order**: IF-1 needs the weapon-systems batch (WS-1…WS-4, two steers) and the air-weapons handoff to land first; IF-2 builds on IF-1
(the gun's mount and the time of flight). IF-2's play can be red-proved with a stand-in gun before IF-1 lands only for the sheaf and danger
close — time on target needs `FireSolution`.

## 6. The demo — `ua-indirect-fire`

📐 The world has no relief (§1), so the range is a new small terrain `indirect-fire-range`: open ground, a **20 m-high, 300 m-long block
standing in for a ridge**, and a **walled compound** (a courtyard with 6 m walls).

| vignette | who | what it shows |
|---|---|---|
| **① adjust, then fire for effect** | an observer (soldier) on a 10 m roof; an 81 mm mortar section of three `TwoMan` tubes and gunners, 2 km back | Strike(Adjust) from the observer; one tube adjusts — over, short, over, within 50 m (FM-6); then all three in a **parallel sheaf** on infantry **behind the ridge block** (high arc, the only one a mortar has) |
| **② time on target** | a 155 mm SP battery of three, 4 km back, staggered at different ranges | Strike(FireForEffect, TimeOnTarget) from the operator menu on a vehicle group in the open: **low arc**, three first rounds land in the same second (FM-4) |
| **③ low or high** | the same battery | a second Strike into the **courtyard**: the low arc fails `Clears` on the wall, the guns go **high** (§3.3) — the HTTP log shows `High` |
| **④ danger close** | a friendly squad 400 m from vignette ①'s point | the first Strike refuses *"danger close"*; the observer resends with `DangerClose` and the adjust creeps in |

The live check (`utility-demo-check.py`): each vignette's shot, splash and rounds-complete lines; the three impact times of ② within one tick;
③'s solution `High`; ④'s refusal line.

## 7. Design docs checked

| doc | applies? |
|---|---|
| `DESIGN_Building_Interiors.md` §3k | ✅ owns the indirect warhead and the arc flight this extends; line 1282 hands *who calls fire* to the behaviours lane — this design |
| `DESIGN_Weapon_Layers.md` §5f | ✅ the L-1 lob (`ThrowArc`) — kept for hand throws; its *"no Mortar mount yet"* (line 871) is what IF-1 builds |
| `DESIGN_Weapon_Systems.md` | ✅ a tube and a towed howitzer are weapon systems; `ControllerOf` decides who fires (§1) — and why `MortarStrike` cannot exist (FM-1) |
| `DESIGN_Entity_Interactions.md` §7.5 | ✅ the 81 / 120 mm tubes are `TwoMan` loads |
| `DESIGN_Air_Operations.md` §7 | ✅ owns Strike; its line 393 *"mortars / artillery can fulfil Strike later"* is IF-D |
| `DESIGN_Orders_Params_Entity_Classes.md` §8, §13.3 | ✅ `<Class><Order>` resolution (FM-1); the designations fill an operator's Strike, never an AI's |
| `DESIGN_Squad_Tactics.md` §3 | ✅ the runner and the play shape; the battery adds a member order, not a runner |
| `DESIGN_Missiles_And_Ammo.md` | ✅ the munitions home; ⚠ it says a mortar mount has no loadout and does not fire on the move — unchanged here |
| `DESIGN_Armour_Hit_Model.md` line 183 | ✅ a falling round hits a vehicle's roof — the top-attack path, no new rule |
| `DESIGN_Operations_Behaviour_Catalogue.md` line 193 | ✅ `CallFireMission(point, effect)` (AIR-1, SQD-9) is IF-2 + IF-3 |
| `DESIGN_Terrain_Combat_Tuning.md` | ⚠ its 60 / 81 / 120 numbers calibrate effect radii, not ranges — IF-1 takes ranges from the reference table |
| `DESIGN_Visual_Effects.md` | ⚠ every round already has a TKB type to draw; the shell in flight is a ui item, not a rule |

## CE-1238 — the mortarman crouches; the crewed tubes in `bt-mortar-roof` *(`2026-10-21`, ui — cross-lane at the user's request)*

🔒 **User, `2026-10-21`:** *"Maybe the scenario is just wrong and mortars should be separate entities?"* · *"Maybe the guy operating a
mortar is crouching next to it usually (for smaller mortars)."* · on doing both: *"Best if you do both changes yourself"*.

| | as built |
|---|---|
| ⭐ the crouch | `FireAtPointBehavior.CrouchBesideOwnMortar` (a step before the shared fire node): a man firing a mortar of his OWN — `MunitionTkbCatalog.IsMortar` (category Mortar, or none + a bomb round: the MortarTeam 2005) — asks `StanceRequest.Set(Crouched)`; the sim sees and hits him crouched, the 3-D figure draws him so (People Display §7b). A crewed tube's gunner is not touched — he is in its seat. Rails `CrouchBesideOwnMortarTests` (red-proved) |
| ⭐ one "is a mortar" rule | `MunitionTkbCatalog.IsMortar` — read by the figure (`HeldWeapons`) and the brain alike |
| ⭐ `bt-mortar-roof` | the two MortarTeam shooters became riflemen SEATED as gunners of two crewed 81 mm tubes (610, IF-G — this design's shape for a gun), firing one bomb each through the tube (`FireAtPoint`, mount 32). ⚠ The tubes stand **250 m** south of their aim points: the 81 mm's lowest charge reaches 1.0 km (≈ 99 m/s), so at the old 84 m there is NO fire solution (≈ 170 m at 85°). Live `utility-demo-check.py --launch bt-mortar-roof` **PASS** `2026-10-21`: roof burst (104.7, 103.3, 6.1), courtyard (94.7, 91.3); downstairs 99.9 HP, the courtyard man killed |
| 🔴 found on the way | every crewed tube's FIRST bomb killed its own gunner (he stands at the tube's origin in the sim) — fixed in the round's hit test (Weapon Systems §9 "riders and the ROUND") |
| ⏳ not done | the MortarTeam 2005 stays (IF-G kept it; Urban Battle names it) — retiring it is this design's owner's call |

