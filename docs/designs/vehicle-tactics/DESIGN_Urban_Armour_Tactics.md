<!--STATUS
state: LIVE
updated: 2026-10-14 (round 4: D-I…D-L APPROVED, "Leans accepted. Write the designs" — four BUILD designs written, see §4a)
build-state: READY-TO-BUILD (the four build designs in §4a) — 🔒 user, 2026-10-14: "Leans accepted. Write the designs"
current-answer: §4 (the programme) and §5 (decisions: D-A…D-H approved, D-I…D-L open with leans) — §1 is the measured ground (do not re-derive), §2 the threats and how a vehicle defeats each, §3 the behaviour.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/OVERVIEW_Rules_And_Demos.md — the INDEX of the rules and demos built across the behaviour designs (rule → what implements it → demo); this design OWNS the rows that cite it
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — OWNS the weapon systems the technicals (M9 / D-K) are rebuilt from; the host still fires its stations (R-2016)
  - docs/designs/motion-models/DESIGN_Motion_Domains.md — OWNS how every vehicle, the drone and later boats / aircraft move (domains, roles, the motion-model seam)
  - docs/designs/vehicle-tactics/DESIGN_FPV_Drone.md — a BUILD design of this programme (§4a)
  - docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md — a BUILD design of this programme (§4a)
  - docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md — a BUILD design of this programme (§4a)
  - docs/designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md — a BUILD design of this programme (§4a)
  - docs/DESIGN_Peek_And_Fire.md — OWNS the INFANTRY fight-from-cover node (hide / expose / fire / heat, FiringPositionMemory); G8 (R-257) keeps vehicles OUT of it, so this design's cycle is a vehicle sibling that reuses its memory and fire step
  - docs/designs/hill-attack/DESIGN.md — OWNS HullDownAttackRun (demo-local): creep until seen → fire → reverse to baseline; the one built vehicle exposure, which V4 generalises
  - docs/designs/group-maneuvers/Squad_Coordination_Design_v1_1.md — OWNS the HillCrestHullDown manoeuvre table (§8.4: waves, burned slots) — the PLATOON layer above this unit-level cycle
  - docs/DESIGN_Weapon_Layers.md — OWNS turrets, the weapon brain / muscle, weapon and ammo choice, the guided-weapon timing rule (W-5, W-11, W-12, WL-5) — M4 turns WL-5's timing rule into real guidance
  - docs/DESIGN_Utility_AI_Demo_Scenarios.md — OWNS the armour model (§9, R-212): facing, penetration chance, expected damage — M1/M2 add the top face and the shaped-charge column; V1 reads it the other way round
  - docs/DESIGN_Terrain_Combat_Tuning.md — OWNS the ammo reference library and the shaped-charge formula (§ L93, L123, L217) — M5 adds the AT rounds to it
  - docs/DESIGN_Sensors_And_Doctrine.md — OWNS threat = danger × freshness (§7.8, R-201) and the G1 lean "danger from weapons vs my armour" (§11.2) that V1 builds
  - docs/DESIGN_Thermal_And_Acoustic_Sensing.md — OWNS heard contacts and sound classes (R-207) — M7 adds a rotor class for drones
  - docs/designs/eqs-2/EQS_Design_v1.3_final.md — OWNS the cover generator and exposure tests that V3 sizes for a hull and extends with an overhead-cover test
  - docs/DESIGN_Operations_Behaviour_Catalogue.md — OWNS the operations GAP list and R-263 (D1 vehicle damage states, D2 drones: "follow a planned 3d path and … attack autonomously"); AIR-2 (FPV) / AIR-4 (counter-drone) / OverheadCoverTest / FindShelter are named there — M7 and V8 are their first slice
  - docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md — OWNS FOV per seat (LD-6); vehicles keep 360° here
-->

# Combat vehicles in urban terrain — hide, show briefly, reverse back; know what can kill you and kill it first

🔒 User, `2026-10-14`:
- *"a tank in an urban area which is supposed to hide behind a building to not be exposed to enemy fire and just go out of the cover for a
  short while for firing or watching and then move back again to the cover … enemy soldiers using RPG or anti-tank weapons … the tank will
  recognize that kind of enemy as a threat and will attack him with priority."*
- *"Leans approved."* (V1–V7, §4)
- *"Rpg should be realistic, the tactics is not just for tanks, maybe it can be used by lighter combat vehicles where rpg still matters and
  rpg might still penetrate thinner armour at the back or top. And rpg is not the only threat, lets count with missile systems like law or
  javelin. And also with small copter drone attacks which are so popular and deadly these days, making tanks an easy prey. No building,
  still designing."*
- *"D-A to D-H approved. Tank should be given antitank piercing rounds as well, not just high explosive rounds. We need vehicles equipped
  with heavy machine gun (like popular white toyota pickup) and missile weapon system on it (as a weapon mount) so they could basically use
  same shoot and scoot tactics, not being stationary."*

**Scope:** every combat vehicle — main battle tank, infantry fighting vehicle, armoured personnel carrier, armoured car, **technical (an armed pickup)** — and the threats
that kill them: small arms and MGs (light vehicles only), unguided AT rockets, guided AT missiles, attack drones.

## 1. What exists — measured `2026-10-14` (do not re-derive)

### 1.1 The hit chain — where realism is lost

```mermaid
graph TD
  FIRE["FireProcessingSystem: a ROUND ENTITY, fixed velocity, aimed at the target's middle"]:::built
  FLY["BallisticsSystem: straight line (gravity only for arc rounds)<br/>lifetime 120 ticks ≈ 2 s"]:::partial
  HIT["RaycastSolverSystem: a person = body band · a VEHICLE = 2-D circle, no height"]:::partial
  FACE["ArmorModel.FacingOf: 2-D bearing → Front / Side / Rear<br/>source straight above ⇒ FRONT"]:::partial
  ARM["CombatPlatformDefDto: ArmorFront / Side / Rear, one mm each"]:::partial
  PEN["PenetrationChance · expected damage (R-212)"]:::built
  DMG["Health −= damage · MobilityKill opt-in"]:::partial
  FIRE --> FLY --> HIT --> FACE --> ARM --> PEN --> DMG
  GUID["guided weapon = a timing rule only (WL-5): one round in flight, hold for distance ÷ speed"]:::partial
  GUID -.-> FLY
  TOP["Top face · arrival angle · shaped charge vs kinetic · ERA / cage · steering · top-attack profile · min range · lock"]:::missing
  TOP -.-> FACE
  classDef built fill:#d4edda,stroke:#2a7
  classDef partial fill:#fff3cd,stroke:#c90
  classDef missing fill:#f8d7da,stroke:#c00
```

*What the picture shows that prose hid: rounds are already real flying entities, so the arrival direction exists at impact — it is just thrown
away before the face is chosen. Top attack is one field and one angle away, not a new ballistics system.*

| fact | where |
|---|---|
| a source straight above the hull resolves to **Front**, the thickest face | `ArmorModel.cs:29-38` |
| a vehicle is hit as a 2-D circle at any height — a round passing high over it hits it | `RaycastSolverSystem.cs:157-170` (people get a body band, vehicles not) |
| a round lives 120 ticks (≈ 2 s): a missile over 2 km vanishes mid-flight; arc rounds live 3600 | `CombatConstants.cs:80,88` |
| missile speed is generated as range × 0.5 (TOW 1875 m/s, ~7× too fast) | `BdcTkbBuilder.cs:244`, `EngineFallbacks.cs:58` |
| no guidance, no lock-on, no min range, no top-attack profile anywhere; `WeaponMountDto` has `AimSeconds`, `ReloadSeconds`, `MagazineSize`, `DispersionMils`, `Range`, `Category` | `WeaponSuiteDto.cs:21-97` |
| `WarheadKind.ShapedCharge` exists but only labels a log line; the "RPG-7 rocket" / "TOW missile" names do not match the library's "PG-7V HEAT" / "TOW-2 HEAT", so neither has a warhead | `WarheadDto.cs:18`, `MunitionTkbCatalog.cs:26-31` |
| ammo library: PG-7V 298 mm, TOW-2 806 mm, 120 mm HEAT, 40 mm HEDP; no LAW / AT4 / NLAW / Javelin / PG-7VL / PG-7VR | `Tkb/Reference/Data/ammo.json:16-18` |
| vehicles: M1 600/350/200, Bradley 100/60/40, T-72 500/250/150, health = front × 5; MilitaryAPC no armour; no light vehicle with armour | `BdcTkbCatalog.cs:50-52,98-100,171-173`, `BdcTkbBuilder.cs:217` |
| R-263 D1 vehicle damage states (mobility / firepower kill / destroyed, per compartment) — decided, not built | Operations catalogue §10.1 |

### 1.2 The vehicle side

| fact | where |
|---|---|
| AI reverse: `MoveTo` + `ReverseAllowed` backs up while the point is BEHIND the hull; only `HullDownAttackRun` uses it; no AI reverse rail; no pivot turn | `CarKinematicsSystem.cs:329-335` |
| turret part slews on its own; `TrackTarget` keeps the gun on a target; elevation M1 −10…+20°, Bradley −10…+60°, T-72 −6…+14° | `TurretSlew.cs`, `BdcTkbCatalog.cs:69,113,181` |
| `ThreatDanger.Of(view, self, target)` = armed 1 / unarmed 0.3 — `self` is unused | `ThreatDanger.cs:21-28` |
| `WeaponEffectivenessVsTarget` = MY mount vs HIS armour + facing + hit chance — "danger to me" is the same maths swapped (⚠ it reads the mount's penetration, not the resolved round's) | `StandardInputs.cs:398-434` |
| cover points 0.75 m off walls (man-sized); exposure judged from a soldier's eye | `TerrainCoverProvider.cs:22-23,70` |
| **firing on the move is allowed**: a moving shooter (> 0.5 m/s) gets dispersion × 2; nothing in the fire path demands a halt (the hill-attack run halts by its own choice) | `HitModel.cs:20-46` |
| a mount sits on a turret (`TurretIndex` ≥ 0) or is fixed to the hull (−1); one `AmmoGuid` per mount; `WeaponTask.AmmoGuid` exists, never written | `WeaponSuiteDto.cs:27,74`, `WeaponTask.cs:59-60` |
| ammo choice is DESIGNED and waits on data: W-6 *"the profile picks the ammo with the SAME `ArmorModel`"*, W-12d *"one `AmmoGuid` per mount … until a mount declares a second round"* | `DESIGN_Weapon_Layers.md:178,219` |
| no pickup / technical type; the `PersonalCar` and `Truck` kinematic presets exist (car 25 m/s, 4.5 × 2 m) | `VehicleClass.cs:8-55` |

### 1.3 Above the vehicle — drones and overhead cover

| fact | where |
|---|---|
| **no drone** in code; air types (UH-60, F-16, C-130) are data with no flight model; `KinematicsMode.Flying` declared, unread; `LinearKinematicsSystem` already moves any entity by a 3-D velocity | `BdcTkbCatalog.cs:284-318`, `NavigationEnums.cs:23`, `LinearKinematicsSystem.cs:91-110` |
| a 3-D path provider exists but returns a straight line; no 3-D route type | `EngineBackedVolumetricPathProvider.cs:14-39` |
| sight works in 3-D (the LOS line is raised from the target's own Z); the broadphase is 2-D and skips a target at exactly the observer's X/Y (a drone straight overhead) | `LosStrategies.cs:211-233`, `VisionBroadphase.cs:86` |
| hearing is 3-D; no rotor sound class (3 bits, one value free); a hovering entity (speed 0) makes no sound | `SignaturesDto.cs:84-93`, `AcousticPerception.cs:36` |
| any code can publish a `DetonationNotification`; `AreaEffectSystem` then applies the warhead — a drone can blow itself up with today's code | `DetonationNotification.cs:19-53` |
| ⭐ **overhead cover is answerable today**: a vertical `SightBlocked(p + hull height, p + (0,0,N))` is blocked by slabs, decks and templated roofs; no EQS test makes that call | `TerrainWorld.cs:390-412`, `IWorldQuery.cs:47-65` |
| a vehicle fits UNDER the test-town garage deck (slab at 3 m, vehicle layer 2 m) and the highway-rest overpass (6 m); it cannot enter any building (template doors 0.9 m; a solid building has no inside); trees have no canopy | `test-town.world.geojson:360`, `TerrainHeightTests.cs:230-253` |
| design intent already names the pieces: AIR-2 FPV (search, select, pursue, dive), AIR-4 counter-drone (shoot, hide), `ShelterFrom(drone)`, `OverheadCoverTest`, `FindShelter` | Operations catalogue :130-134, :180, :210 |

## 2. The threats — and how a vehicle defeats each

Figures are public, approximate and disputed between sources; they are tuning starting points, not claims.

| threat | penetration (mm RHA vs shaped charge) | lands on | the gunner needs | ⭐ how the vehicle defeats it |
|---|---|---|---|---|
| rifle / MG (7.62, 12.7 AP) | ~10 / ~20 | the face toward him | sight | armour — only light vehicles care: stand off, hide |
| **RPG-7** PG-7V / PG-7VL / PG-7VR (tandem) | ~300 / ~500 / ~650 | the face toward him — **the TOP from an upper floor close in** | aim ~2 s, big dispersion, reload ~6 s, ~200–300 m | front toward him; exposure shorter than his aim; stand off beyond ~300 m; kill him first |
| **LAW** (M72 class), disposable | ~300 | the face toward him | aim ~1.5 s, one shot, ~200 m | as RPG; a light-vehicle killer, not a tank killer |
| **TOW** class, wire-guided (SACLOS) | ~800 | the face toward him | sight of the target for the WHOLE flight (~13 s to 3.75 km) | ⭐ break sight during the flight — reverse into the hide when a launch is seen |
| **NLAW** (overfly top attack) | ~500 | **the top** | tracking ~2–3 s, 20–800 m | deny the track: exposure shorter than his aim while he sees you |
| **Javelin** (fire-and-forget, top attack) | ~760 tandem | **the top** (direct mode close in / under cover) | **lock before launch** (seconds), 65 / 150 – 2500 m, ~14 s to 2 km | ⭐ deny the lock: never stay seen by him that long; once launched, nothing (APS / smoke are later) |
| **FPV drone** (often an RPG warhead) | ~300–500 | **the top / the rear deck** | a pilot, ~100+ km/h, km range | hear it early → **overhead cover**; keep moving; shoot it if seen (MG); cages / jammers later |
| drop-grenade drone | fragmentation | the top | hovers over the target | overhead cover; keep moving |
| **technical** with an HMG (12.7 / 14.5 mm) | ~20–30 (AP) — deadly to APCs, armoured cars, light IFV sides; harmless to an MBT | the face toward it | sight, fires on the move | an MBT kills it first with HE or MG at range; a light vehicle hides from it like from an AT gun |
| **technical** with an ATGM (wire / beam-riding class) | ~800–1200 tandem | the face toward it | halts and keeps sight for the flight | break sight during the flight; rank it first (it is an AT team that drives) |

**The technicals' own tactic** — *shoot and scoot*: no armour, so speed and surprise are the protection. Drive out of a hide, fire a burst
(the HMG on the move) or one missile (halted for the flight), and **drive off to a DIFFERENT hide**, never back to the same corner; one
position, one engagement. Mirror of the tank's cycle with the withdrawal turned forward.

📌 Sources: [Javelin (Wikipedia)](https://en.wikipedia.org/wiki/FGM-148_Javelin) · [Javelin (CSIS)](https://missilethreat.csis.org/missile/fgm-148-javelin/) ·
[RPG-7 (Wikipedia)](https://en.wikipedia.com/wiki/RPG7) · [drones vs top armour (Plasan)](https://plasan.com/drone-threats-and-top-attack-protection-does-modern-armor-defeats-fpv-and-loitering-munitions/) ·
[cope cages (Defense News)](https://defensenews.com/global/europe/2024/05/14/ukraine-russia-race-to-shield-battle-tanks-from-exploding-drones) ·
[tank vs drones (TWZ)](https://www.twz.com/tank-runs-for-its-life-under-constant-barrage-of-attack-drones) ·
[Bradley armour](https://en.wikipedia.org/wiki/M2_Bradley) · [Chieftain: use of cover](https://worldoftanks.com/en/news/history/The_Chieftains_Guide_Use_of_Cover/)

⭐ **The unifying idea:** every defence is one of four moves — *armour faces it*, *don't be seen by it long enough* (aim / lock / track
time), *be out of its range*, *have something solid above you*. The behaviour does not need a branch per weapon: it needs each threat's
**danger to me**, **aim time**, **range** and **direction (ground or air)**, all read from the threat's TKB type.

## 3. The behaviour

```mermaid
stateDiagram-v2
  [*] --> Hidden
  Hidden --> Exposing : target to engage, or watch period due — and no air threat
  Exposing --> Engaging : at the firing point
  Engaging --> Withdrawing : rounds fired · watch over · a seen AT threat's aim time nearly spent · launch seen · hit / near miss
  Exposing --> Withdrawing : threatened on the way out
  Withdrawing --> Hidden : back at the hide (reverse, front to the threat)
  Hidden --> Relocating : every firing point burned
  Relocating --> Hidden
  Hidden --> Sheltering : air threat sensed (heard or seen)
  Engaging --> Sheltering : air threat sensed
  Sheltering --> Evading : no overhead cover reachable in time
  Sheltering --> Hidden : air clear
  Evading --> Hidden : air clear
  Hidden --> [*] : order ends
```

*What the picture shows: ground threats are beaten by the exposure cycle (time and front armour), air threats by a different refuge —
the hide behind a building protects nothing from above. One exposure is the hill-attack run; the loop, the burned points and the air
branch are what make it the urban behaviour. In Hidden the turret tracks the expected target; in Sheltering / Evading a weapon that can
elevate fires at a SEEN drone.*

| rule | from |
|---|---|
| ⭐ **exposure budget from the enemy**: withdraw before any SEEN AT threat has had sight of me for k × his `AimSeconds` (the lock time of a Javelin, the aim of an RPG); with no such threat, a fixed `ExposeSeconds` cap | R-246 (if I see him, he sees me) + `SightNow` + his TKB mount |
| ⭐ **stand-off**: a firing point beyond a threat's effective range scores higher; an IFV with a 25 mm engages RPG men from 400 m | danger to me falls to 0 beyond his `Range` |
| **front to the threat**: drive out forward, back in reversing — never turn the side to the most dangerous seen threat | the armour model rewards it |
| **launch seen → break sight**: a wire-guided missile in flight is defeated by reversing into the hide | M4 command guidance |
| **air threat → shelter**: the nearest point with overhead cover reachable on the vehicle layer; else keep moving (no exposure cycle while a drone hunts) | M7, V8 |
| **light vehicles**: the same node — their danger profile already includes rifles and MGs, so they hide more and stand off further | V1 |
| ⭐ **withdraw mode — REVERSE or SCOOT**: an armoured-front vehicle backs into the hide it came from (front to the threat); a vehicle whose front is no better than its side (a technical, an armoured car) **drives forward to the NEXT hide**, a different one, and may turn its back — speed, not armour, protects it | D-J |
| **burn after N**: a firing point is burned after N exposures — a technical after 1 (one position, one engagement), a tank after 2 | `FiringPositionMemory` heat, per-type N |
| **fire on the move**: a weapon fires while driving when its mount allows it (HMG, autocannon, a stabilised tank gun — dispersion × 2, already in `HitModel`); a command-guided missile halts the launcher for its flight; a fire-and-forget missile halts only for its lock | D-L |
| **ammo is the weapon brain's choice, never the behaviour's**: the behaviour names the target, the brain picks AP against armour, HE against men and buildings (R-261, W-6) | D-I |

```mermaid
graph LR
  H1["hide A"] -->|drive out| F1["firing point 1"]
  F1 -->|"REVERSE (armoured front)"| H1
  F1 -->|"SCOOT forward (technical)"| H2["hide B"]
  H2 -->|drive out| F2["firing point 2"]
  F2 -->|SCOOT| H3["hide C"]
```

*What the picture shows: the two withdraw modes differ only in WHERE the withdrawal ends — the same hide, reversing, or the next hide, driving
forward. One node, one parameter derived from the type's armour (D-J).*

## 4. The programme

**Approved `2026-10-14` (V1–V7, "Leans approved"), extended in round 2:**

| id | owner | what | lean |
|---|---|---|---|
| **V1** ✅ ⭐ BUILT `2026-10-14` | behaviors | **danger to me** in `ThreatDanger.Of`: the threat's best weapon (its resolved round) on the face it would hit — from M1's 3-D direction, or the top for a top-attack munition — × 0 outside its range / below its min range, as its PENETRATION CHANCE (⚠ as-built: not ÷ my max health — Exposure Cycle §2); a drone = its warhead on my top; a floor above 0 for armed-but-harmless; an UNARMOURED self keeps armed 1 / unarmed 0.3 | the one place (R-194, R-201), the armour model (R-212 A1); a HEARD contact keeps its sound-class estimate (R-207) |
| **V2** ✅ → see M5 / M6 | backend (data) | was: tank eye height, coax MG + HE, RPG rockets + rifle, tandem round | widened into M5 / M6 |
| **V3** ✅ ⭐ BUILT `2026-10-14` (Exposure Cycle §3 as-built rows) | behaviors (EQS, cross-lane) | hull-sized hide and firing points, judged at the vehicle's sight height; **+ `OverheadCoverTest`** (vertical `SightBlocked` from the hull top) **and a `FindShelter` template** (catalogue :210); **+ a "beyond threat range" score** | parametrise from the querier (R-137) |
| **V4** ✅ ⭐ BUILT `2026-10-14` (Exposure Cycle §4 as-built rows) | behaviors | the vehicle exposure cycle (§3), a sibling of PeekAndFire (G8), scope: any combat vehicle; with the exposure budget and stand-off rules | generalises `HullDownAttackRun`; reuses `FiringPositionMemory` + the fire step |
| **V5** ✅ | backend | AI-reverse rail; CE-3027 / CE-3029 corner defects | before V4 runs live |
| **V6** ✅ | behaviors | the AT side: RPG / LAW men ambush from windows (upper floors give top hits by M1 geometry), fire, relocate; a Javelin / NLAW team stays back, needs its lock time with sight; a TOW-type gunner holds sight through the flight | infantry library composition first |
| **V7** ✅ | behaviors | demos — see the table below | `scripts/utility-demo-check.py` |
| **V8** 🆕 | behaviors | **counter-drone reaction**: an SOP reaction row above Contact — Sheltering → Evading (§3); fire at a seen drone with a weapon that can elevate | AIR-4's first slice |

**New model items (round 2) — mostly the combat engine, so backend:**

| id | owner | what | lean |
|---|---|---|---|
| **M1** | backend | **3-D arrival**: the round's velocity reaches `DetonationNotification`; `FacingOf` takes the incoming direction and gains **Top** (and Bottom for mines later); fix "overhead ⇒ Front"; a vehicle hit test with the hull's height | D-A |
| **M2** | backend | **armour per face × penetrator**: `ArmorTop` (+ optional `ArmorBottom`) and an optional shaped-charge column per face (absent ⇒ the same as kinetic, so every current type is unchanged); a tandem round ignores ERA | D-B, D-C |
| **M3** | backend | **the penetrator on the round**: kinetic / shaped charge / tandem from the warhead; fix the RPG / TOW name mismatch so both get their HEAT warhead | — |
| **M4** | backend | **missiles**: per-munition lifetime and real speed; three guidance kinds as munition data — unguided (RPG, LAW), command (TOW: hits only while the gunner keeps sight), fire-and-forget (Javelin, NLAW: lock = the existing `AimSeconds`, then steers to the locked entity) — plus `TopAttack` and `MinRange` | D-D |
| **M5** | backend (data) | munitions: PG-7V / VL / VR, M72-class LAW, NLAW, Javelin, an FPV warhead, a drop grenade; weapons with real aim, reload, dispersion, rounds carried; the RPG man with a rifle and 3–4 rockets; tank coax MG + HE; vehicle eye heights | §2 numbers |
| **M6** | backend (data) | vehicles: MBT, IFV, APC, armoured car with per-face kinetic / shaped-charge / top values and an explicit health (not front × 5); light vehicles opt into mobility kill | D-F |
| **M8** 🆕 | backend | **ammo loadout per mount + the weapon brain's choice**: a mount lists its rounds with counts (tank gun: APFSDS + HEAT-MP + HE; coax MG); the profile picks by the SAME `ArmorModel` (W-6) — the highest expected damage on the target's face, HE by its expected area damage against men, buildings and soft vehicles; switching the loaded round costs one `ReloadSeconds`; `WeaponTask.AmmoGuid` carries it (no wire change, W-12d) | D-I |
| **M9** 🆕 | backend (data) | **technicals**: a pickup type (`PersonalCar` / `Truck` preset, ~25 m/s, no armour, low health, mobility kill on a penetrating hit, open cab) in three fits — **HMG** (12.7 / 14.5 mm on a pintle = a fast-slewing turret part, elevation to ~+70°, so it can also fire at drones), **ATGM** (a command-guided launcher on a turret part with limited traverse), and both; usable by either side | D-K, D-L |
| **M7** | backend + behaviors | **a minimal FPV drone**: a TKB air type on `LinearKinematicsSystem` (point-mass: speed, climb, turn), a rotor sound class, a small collider with a height, a warhead it detonates on contact (itself as shooter); its behaviour: fly a 3-D route → search → select (threat ranking) → pursue → dive on the top / rear; the overhead broadphase fix (`VisionBroadphase.cs:86`) | D-E; R-263 D2 (AIR-2's first slice) |

## 4a. The build designs — the UML lives there

| design | items | what it decides in classes and sequences |
|---|---|---|
| [DESIGN_Armour_Hit_Model.md](DESIGN_Armour_Hit_Model.md) | M1, M2, M3, M6 shape | the travel direction on `DetonationNotification`, `Facing.Top`, the shaped-charge column, `Penetrator`, the vehicle height band |
| [DESIGN_Missiles_And_Ammo.md](DESIGN_Missiles_And_Ammo.md) | M4, M5, M8, D-L | `AmmoLoadout` + `AmmoChoice`, `MissileDto` + `GuidedRound` + `GuidedRoundSystem`, the lock, min range, `FireOnMove`, the munition table |
| [DESIGN_Vehicle_Exposure_Cycle.md](DESIGN_Vehicle_Exposure_Cycle.md) | V1, V3, V4, V5, V6, V8, M9 | `ThreatDanger.ToMe`, the vehicle EQS templates and `OverheadCoverTest`, `VehicleFightFromCover` + `LandPlatformAttack`, reverse, technicals, `ShelterFromAir` + `BasicVehicleSop` |
| [DESIGN_FPV_Drone.md](DESIGN_FPV_Drone.md) | M7 | `FlightParams`, `PointMassFlightSystem`, `DroneImpactSystem`, `FpvAttack` |

⚠ **Found while designing, recorded, not fixed (design only):** CE-2166 — a per-shot cooldown drains twice a frame; CE-2167 — AI reverse is
clamped to ≥ 0 (`BicycleModel.cs:34`), so no AI vehicle reverses today (V5 fixes it).

**Demos (V7):**

| demo | terrain | shows |
|---|---|---|
| `ua-tank-corner` | test-town | the cycle; the RPG man engaged before the rifleman; never the same point twice; a near miss cuts an exposure |
| `ua-ifv-standoff` | test-town | a 25 mm IFV kills RPG men from beyond their range; up close an RPG kills it |
| `ua-javelin-lock` | basic-desert or test-town | the tank's short exposures deny a Javelin team its lock; a long exposure is punished from above |
| `ua-drone-shelter` | test-town (garage deck) | an FPV drone hunts a tank; the tank hears it and backs under the deck; a tank in the open is hit on the roof — ✅ **LIVE PASS `2026-10-16` (`scripts/utility-demo-check.py ua-drone-shelter --launch`, two runs): ① the tank by the deck backs under Garage Deck 1 (~10.5 s after the drone is heard), the drone's dive meets the slab, the tank is untouched (3000 → 3000); ② the tank in the open, evading, is hit from above (3000 → 2550).** Three defects found on the way and fixed: CE-2212 (the shelter query was reset every perception update), CE-2213 (the strike overshot an evading tank — and the rails' stand-in flight had hidden it), CE-2214 (a low deck let the hull's grown band poke through the slab). |
| `ua-technical-scoot` | test-town | an HMG technical and an ATGM technical shoot and scoot (a new hide every time, the HMG firing on the move, the ATGM halted for its flight); the tank ranks the ATGM technical first, kills it with HE, and breaks sight when it launches — ⏸ **live `2026-10-16`, varies run to run**: after CE-2219 the tank fires back (it killed the ATGM technical after its first Kornet in one run); the HMG technical shoots and scoots every run; the ATGM technical fired from two points in one run and not at all in another (where its firing points see the tank depends on the EQS). See [Exposure Cycle §6](DESIGN_Vehicle_Exposure_Cycle.md) |
| `ua-rpg-window` *(V6)* | bt-range (House A) | an RPG man at an upper window ambushes two IFVs in the street, fires, and moves to another window after each rocket (`WindowDuel` shape A, `exposuresPerPosition` 1); the IFVs fire back on the move — ✅ **LIVE PASS 4 / 4 `2026-10-16`** (`scripts/utility-demo-check.py ua-rpg-window --launch`), after CE-2219 (an IFV driving to its hide now fires) and with CE-2218 (backend: a rocket into the shooter's own wall) avoided by a 50 m start |
| `ua-tank-ammo` | basic-desert or test-town | one tank, three targets: an enemy tank (APFSDS), a technical (HEAT or HE), an infantry team in a building (HE) — the weapon brain switches rounds, paying one reload per switch |

**Order:** M1 → M2 → M3 (the hit chain) · V1 · M5 / M6 / M8 / M9 data · V3 → V4 (+V5) → V6 → `ua-tank-corner`, `ua-ifv-standoff`,
`ua-tank-ammo`, `ua-technical-scoot` · M4 → `ua-javelin-lock` · M7 → V8 → `ua-drone-shelter`. **Later:** ERA / cage / slat numbers, active protection, smoke, jammers and
electronic warfare, hull-vs-turret armour, R-263 damage states, pairs and infantry cooperation, gun-elevation stand-off, loitering munitions.

⭐ **Scheduled `2026-10-14`** (🔒 user: *"Also demo scenario for shoot and scoot and drone attack"*): ⏸ none of the six demos is built yet. **`ua-technical-scoot`** is built after the weapon-systems catalogue (Weapon Systems WS-4): the technicals become presets with a real, killable gunner (W-I), so the demo shows the HMG technical scooting and a gunner shot off his gun. **`ua-drone-shelter`** is built after the FPV attack exists — the `Dive` profile of the one air attack (Air Operations B5, slice 6); it then also shows a drone hitting a tank in the open on the roof.

## 5. Decisions

🔒 User, `2026-10-14`: **"D-A to D-H approved."** then **"Leans accepted. Write the designs"** — every row below (D-A…D-L) is DECIDED as leaned.

| id | question | lean (round 3, approved) | blast radius / what would change it |
|---|---|---|---|
| **D-I** | ammo choice | **a loadout per mount, chosen by the weapon brain with the one `ArmorModel` (W-6)**; HE valued by its expected area damage; a switch costs one reload; the behaviour never names a round | backend weapon brain + TKB; W-12d is unblocked by this data. ⛔ not a behaviour-level ammo node — R-261 keeps it in the weapon layer |
| **D-J** | reverse or scoot | **derived from the type**: front armour ≥ 2 × side ⇒ REVERSE to the same hide; else SCOOT forward to the next hide; a behaviour param overrides | one rule in the V4 node; an MBT and an IFV reverse, a technical and an armoured car scoot |
| **D-K** | the technical's gunner | **vehicle-level for now** — no separate gunner person; the open cab makes the vehicle weak to rifles through its armour of ~0; a killable exposed gunner (= firepower kill) comes with R-263's damage states and the carrier's per-seat exposure (R-2009) | data only now |
| **D-L** | firing on the move | **per mount: `FireOnMove`** (HMG, autocannon, stabilised tank gun: yes, with the existing dispersion × 2); command-guided missiles halt for the flight, fire-and-forget only for the lock | weapon brain + V4 |

**Round 2 (approved):**

| id | question | lean | blast radius / what would change it |
|---|---|---|---|
| **D-A** | when is a hit on the TOP? | the incoming direction descends ≥ **45°**, or the munition is `TopAttack`; so an RPG from a 4th-floor window 10 m away hits the roof, from 50 m the side | the armour model and its rails; every existing hit keeps its face (they are near-flat) |
| **D-B** | a shaped-charge armour value separate from kinetic? | **yes, optional per face** (absent = the kinetic value) — without it an RPG can never be both "harmless to an M1's front" and "deadly to its side or roof" with credible numbers | `CombatPlatformDefDto` (TKB schema, backend); R-212 A3 still holds (fixed per type) |
| **D-C** | ERA, cages, slat armour now? | **later**, as per-face data: ERA scales a single shaped charge down (tandem ignores it), a cage defeats a shaped charge with a probability (expected value, no dice — R-212 A2) | data only once M2 exists |
| **D-D** | how far do missiles go? | **three guidance kinds as munition data on the existing round** (steer toward the locked entity, climb-and-dive for top attack); lock = `AimSeconds` | backend ballistics; WL-5's timing rule stays for the one-in-flight limit. ⛔ not a full flight model (no aerodynamics) |
| **D-E** | build a drone now, or wait for the whole air programme (catalogue P5)? | **a minimal FPV drone in this programme** (M7) — the vehicle demos need a top-attack hunter; it is R-263 D2's first slice, the rest of AIR later | a new TKB air type + one motion rule on an existing system; reconnaissance and loitering drones stay later |
| **D-F** | vehicle health | **explicit per type** (an RPG penetration knocks out an APC or IFV; an MBT survives 1–2 side penetrations; a Javelin top hit kills it); light vehicles opt into mobility kill; R-263's per-compartment states stay their own item | data; changes every vehicle demo's numbers ⇒ re-check `ua-weapon-choice` |
| **D-G** | separate hull and turret armour? | **no, later** — the face is chosen from the hull; the top face covers the turret roof | — |
| **D-H** | counter-drone means in v1 | **shelter, keep moving, shoot it if seen**; jammers (they need a radio-link model), active protection, smoke later | V8 only |

Next: a backend handoff for M1–M9 and V5 (the combat engine and kinematics), citing §4a's designs by section; the behaviour items follow in the order above.
