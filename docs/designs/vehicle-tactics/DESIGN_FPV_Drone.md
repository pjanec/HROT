<!--STATUS
state: LIVE
updated: 2026-10-14
build-state: BUILT — the Muscle half (§6, backend B11; owned by MuscleGround, R-3005) and the Brain half as AirAttack's Dive profile (Air Operations §9i, 2026-10-16)
current-answer: §2 classes, §3 sequences, §4 who runs it, §5 rails, §6 as-built — §1 is the measured ground.
stale-below: nothing.
known-rot: none (the first draft's PointMassFlightSystem and Stride registration are in §HISTORY).
known-conflict: none. ⚠ §2's FpvAttackNodes / FpvAttackParams / FpvAttackState are SUPERSEDED by B5 (R-2015): the drone's attack is AirAttack's Dive profile (DESIGN_Air_Operations.md §7); the phases and DroneTargetDecision carry over.
related-designs:
  - docs/OVERVIEW_Rules_And_Demos.md — the INDEX of the rules and demos built across the behaviour designs (rule → what implements it → demo); this design OWNS the rows that cite it
  - docs/DESIGN_Map_3D_Mode.md — §6n draws the drone in the 3-D map: a quadcopter sized from its flight body, four rotors spinning
  - docs/designs/air-attack/DESIGN_Air_Operations.md — OWNS the drone's ATTACK now: AirAttack's Dive profile (B5, R-2015) replaces §2's FpvAttackNodes; this file keeps the Muscle half
  - docs/designs/motion-models/DESIGN_Motion_Domains.md — OWNS how the drone moves: RotorcraftModel in the AirMotionModule (on the MuscleGround node, R-3005), autonomous AND manual (FPS possession)
  - docs/designs/vehicle-tactics/DESIGN_Urban_Armour_Tactics.md — the PROGRAMME (item M7, decision D-E)
  - docs/DESIGN_Operations_Behaviour_Catalogue.md — OWNS R-263 D2 ("point-mass flight + acoustic signature + battery … follow a planned 3d path and … attack autonomously") and AIR-1…AIR-4; this file is AIR-2's first slice (FPV: search, select, pursue, dive)
  - docs/designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md — the drone detonates through that chain; its dive arrives from above ⇒ the Top face
  - docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md — the target's reaction (AirThreat, ShelterFromAir) and the danger of a drone (§2)
  - docs/DESIGN_Thermal_And_Acoustic_Sensing.md — OWNS heard contacts and sound classes; §2 adds Rotor (7)
  - docs/designs/navig-2/Navigation_Design_v2_0.md — OWNS NavigationIntent and the volumetric path provider; v1 flies straight legs between waypoints, no 3-D path planning
-->

# FPV drone — a small attack copter that flies a 3-D route, finds a target, and dives on its roof

**Item:** M7. **Decision applied:** D-E (a minimal FPV drone in this programme). **Scope v1:** a scenario-placed drone with its own
behaviour: fly the route → search → select → pursue → dive → detonate. Reconnaissance drones, loitering munitions, launch from an operator,
jamming and the full 3-D route type are later (R-263, catalogue AIR-1…AIR-4).

## 1. What exists — measured `2026-10-14`

| fact | where |
|---|---|
| no drone in code; air types (UH-60 400, F-16 401, C-130 402) are data only, with no kinematics and no behaviour; air TKB ids 403–499 free | `NedTkbCatalog.cs:284-330`, `TkbEntityTypes.cs` |
| entity routing to motion is by COMPONENT today (no motion-model seam): `VehicleState + VehicleParams + NavState` ⇒ CarKinematics; `CrowdAgent` ⇒ crowd; else `SimTransform + SimVelocity` ⇒ `LinearKinematicsSystem` (projectiles) — the seam is Motion Domains (MD-C) | `LinearKinematicsSystem.cs:91-111`, `GroundKinematicsModule.cs:88-102` |
| `NavigationIntent { Mode, Flags, MaxReplans, ReverseAllowed, Vector3 FinalDestination (Z carried), TargetSpeed, … }` is replicated Brain → Muscle and written by `MoveToExecutor`; the bridge skips an entity with no `NavState` unless it is `PathToPoint` | `NavigationComponents.cs:230-260`, `NavigationIntentBridgeSystem.cs:134-169` |
| `KinematicsMode.Flying = 7` declared, read by nothing; a volumetric provider exists but SimHost never passes it | `NavigationEnums.cs:23`, `SimHostNodeBootstrapper.cs:319` |
| sight works in 3-D (`LosStrategies.cs:211-233`), but `VisionBroadphase.Select` drops a target at the observer's exact X/Y (2-D epsilon), so hearing drops it too | `VisionBroadphase.cs:86-87` |
| hearing is 3-D; a source slower than 0.5 m/s is silent (a hovering drone would be); no rotor class (class 7 free) | `SoundEmissionSystem.cs:24-69`, `SignaturesDto.cs:84-93` |
| `DetonationNotification` may be published by anything; `DamageCalculationSystem` (direct) and `AreaEffectSystem` (warhead area) consume it; a kill goes through `DamageAssessedEvent` → `HealthApplicationSystem` on the `Health` owner | `DetonationNotification.cs:24`, `HealthApplicationSystem.cs:88` |
| turret elevation limits are enforced (`OutOfArc`); hit spread is horizontal; a target with no collider is a 0.3 m circle | `TurretSlew.cs:23-25`, `HitModel.cs:29,75-80` |
| R-136: entity state has two legal sources, the TKB or a published TransientLocal descriptor | `RULINGS.md` R-136 |

### INVENTORY — the queries run

| query | total | the set |
|---|---|---|
| `search_graph .*Drone.*\|.*Uav.*\|.*Flight.*\|.*Flying.*` (Class) | 0 relevant (1 hit: `FlightRecorderExample`) | — |
| `search_code Drone` (docs + code) | design mentions only (catalogue, Add Entity Picker `AboveLevel`) | — |
| grep `KinematicsMode.Flying` readers | 0 | — |

⇒ nothing to reuse for flight itself; everything else (motion integration, the intent on the wire, sensing, the damage chain, the threat
ranking) exists and is reused.

## 2. Classes

```mermaid
classDiagram
  class LocomotionDto { <<new TKB "Gen.Locomotion" — Motion Domains MD-A>> Class = Rotorcraft }
  class FlightParamsDto {
    <<new TKB descriptor "Gen.Flight">>
    MaxSpeedMps = 30
    ClimbMps = 8
    TurnRateDeg = 120
    EnduranceSeconds = 600
    ContactRadiusM = 0.8
  }
  class FlightParams { <<new ECS component, from the TKB, every node>> copy of FlightParamsDto }
  class FlightState { <<new ECS component, MuscleGround group>> Airborne · VerticalSpeed }
  class IMotionModel { <<new interface — Motion Domains MD-C>> Autonomous(frame, intent, dt) · Manual(frame, drive, dt) }
  class RotorcraftModel {
    <<new IMotionModel — Motion Domains MD-H>>
    Autonomous: turn ≤ TurnRateDeg, climb ≤ ClimbMps toward FinalDestination
    Manual: ManualDrive Forward · Lateral · Yaw · Vertical
    hover · a dead rotorcraft falls
  }
  class AirMotionModule { <<new — on the MuscleGround node>> AirMotionSystem: MotionModelTable → RotorcraftModel }
  class DroneImpactSystem {
    <<new, Muscle, AirMotionModule, after AirMotionSystem>>
    swept segment vs terrain and colliders
    publishes DetonationNotification, then kills the drone
  }
  class FpvAttackNodes { <<new static, Hrot.AI.Behaviors/Brains>> +FpvAttack(ref Params, ref State, self, world) NodeStatus }
  class FpvAttackParams {
    <<new ParamsDto>>
    W1 · W2 · W3 · W4 : Vector3  «the route, v1»
    SearchCentre · SearchRadius
    CruiseAltitudeM = 60
    SearchSpeed = 15 · AttackSpeed = 30
    DiveAngleDeg = 60
  }
  class FpvAttackState {
    <<new working state>>
    Phase : Route | Search | Pursue | Dive | Spent
    int Leg
    Entity Target
    double FlightStartedAt  «the battery»
  }
  class DroneTargetDecision { <<new utility decision>> freshness × my warhead's effectiveness × IsVehicle }
  class LocomotionMoveTo { <<existing>> Issue a 3-D point }
  class VisionBroadphase { <<existing, grows>> 3-D epsilon }
  class SoundEmissionSystem { <<existing, grows>> a flyer is audible while airborne }
  IMotionModel <|.. RotorcraftModel
  AirMotionModule ..> RotorcraftModel
  RotorcraftModel ..> FlightParams
  RotorcraftModel ..> FlightState
  RotorcraftModel ..> NavigationIntent
  RotorcraftModel ..> ManualDrive
  LocomotionDto ..> AirMotionModule : Class selects the domain
  DroneImpactSystem --> DetonationNotification
  FpvAttackNodes ..> FpvAttackParams
  FpvAttackNodes ..> FpvAttackState
  FpvAttackNodes ..> LocomotionMoveTo
  FpvAttackNodes ..> DroneTargetDecision
```

*What the picture shows that prose hid: the drone adds no mover of its own — it is the first `RotorcraftModel` in the Air domain
(Motion Domains §3), driven by the SAME two commands every mover takes: `NavigationIntent` from its behaviour, `ManualDrive` from a
possessing operator. The kill is the existing damage chain.*

| rule | why |
|---|---|
| a drone is a TKB air type (DIS domain 2, id 403 "FPV drone"): `Gen.Locomotion = Rotorcraft`, `FlightParamsDto`, a collider (r 0.3 m, h 0.3 m), `Health` 10, a weapon mount = the warhead (munition 6016, range = `ContactRadiusM`), an acoustic signature (class `Rotor` = 7, ~300 m), a visual sensor (~1500 m) | R-263: point-mass + acoustic + battery; one entity like any other — seen, heard, ranked, shot, possessed |
| it is owned by the ONE Muscle role, **`MuscleGround`** (its group: `SimTransform, SimVelocity, FlightState`, …), and moved only on that node | R-3005 (amends MD-B); "motion needs to stay on muscle node" |
| **autonomous**: the behaviour issues `MoveTo` to 3-D points; `RotorcraftModel.Autonomous` turns ≤ `TurnRateDeg`, climbs ≤ `ClimbMps`, speed → min(`TargetSpeed`, `MaxSpeedMps`); arrival within 2 m sets `NavigationStatus`, so `LocomotionMoveTo.Status` works unchanged | MD-D, MD-E (straight legs, v1) |
| **manual (FPS possession)**: the station's rotorcraft scheme (W/S forward, A/D lateral, mouse yaw, Space / C vertical) → `OperatorControlFrameEvent` → `PossessionSystem` → `OperatorControl` → `ManualDrive { Forward, Lateral, Yaw, Vertical }` → `RotorcraftModel.Manual` | MD-D; the existing possession chain (Motion Domains §3 sequence) |
| the bridge skips an entity whose locomotion is not Ground-domain (no ground path request for a flyer) | §1 |
| **the battery is the behaviour's**: `FlightStartedAt`; at `EnduranceSeconds` it dives on whatever is below, or crashes; under possession the operator has the same budget (the behaviour is paused, its clock is not) | R-136: no runtime state component for a timer |
| **impact**: each frame the swept segment `p → p + v·dt` against terrain (`IWorldQuery.SightBlocked`) and colliders within 10 m (circle + height band, M1); a contact publishes `DetonationNotification { Shooter = drone, Target, Hit, Penetration / Damage of the warhead mount, Ammo = 6016, Travel = v }` and a lethal `DamageAssessedEvent` on the drone | whoever flies it — AI or an operator; the rest is the existing chain (Top face by the dive, fragments, the wreck) |
| the overhead fix: `VisionBroadphase` skips only the observer itself (3-D epsilon); a flyer is audible while airborne at any speed | §1 |
| target choice: `DroneTargetDecision` = contact freshness × `WeaponEffectivenessVsTarget` (its warhead on the target's roof) × `IsVehicle` (vehicle 1, person 0.3) | R-263 "the existing sensor + threat ranking" |

## 3. Sequences

```mermaid
sequenceDiagram
  participant B as FpvAttack (CGF)
  participant N as NavigationIntent (wire)
  participant A as AirMotionSystem + RotorcraftModel (MuscleGround)
  participant S as Drone's own sensors
  participant I as DroneImpactSystem (MuscleGround)
  participant D as DamageCalculation / AreaEffect
  B->>N: MoveTo(W1 at CruiseAltitude, SearchSpeed)
  N->>A: FinalDestination (3-D), TargetSpeed
  A->>A: Autonomous: SimVelocity, SimTransform
  A-->>B: NavigationStatus arrived → next leg … then Search (circle the area)
  S-->>B: TargetMemory contacts
  B->>B: DroneTargetDecision → Target (vehicles first)
  B->>N: Pursue: a point above the target at CruiseAltitude
  B->>N: Dive: once horizontal distance ≤ altitude / tan(DiveAngle): the target's top, AttackSpeed
  I->>I: swept segment crosses the target's collider band
  I->>D: DetonationNotification { Target, Travel descending ≥ 45° }
  D->>D: Top face, shaped charge vs ArmorTopCe, fragments
  I->>I: DamageAssessedEvent on the drone (it is spent)
```

*Under possession the same picture holds with the first three rows replaced by the possession chain (Motion Domains §3) — the impact
and the damage are identical. The target's side: it hears the rotor (class 7), `AirThreat` fires, its SOP runs `ShelterFromAir`; under
the garage deck the dive meets the slab first (Exposure Cycle §8).*

## 4. Who runs it

```mermaid
graph TD
  subgraph CGF["CGF (Brain)"]
    FA["FpvAttack (BrainTickSystem) · DroneTargetDecision"]
    MT["MoveToExecutor → NavigationIntent"]
    PO["PossessionSystem → OperatorControl → ManualDrive"]
  end
  subgraph AIR["Muscle node holding MuscleGround (the SimHost)"]
    AM["AirMotionModule: AirMotionSystem (RotorcraftModel) → DroneImpactSystem"]
  end
  subgraph SIM["SimHost (Perception + damage)"]
    PER["visual + acoustic (Rotor) — targets hear and see the drone"]
    DMG["DamageCalculation · AreaEffect"]
  end
  subgraph ALL["every live node"]
    HA["HealthApplicationSystem"]
  end
  FA --> MT -->|NavigationIntent, replicated| AM
  PO -->|ManualDrive, replicated| AM
  AM -->|DetonationNotification| DMG -->|DamageAssessedEvent| HA
```

*What the picture shows: the drone's motion and impact live in the Air domain's module on the node holding the one Muscle role,
`MuscleGround` — the SimHost (R-3005: no dedicated air node).*

## 5. Rails — in the features' own suites

| rail | suite |
|---|---|
| `M7_ARotorcraft_FliesToA3DPoint_AndReportsArrival` · `M7_ARotorcraft_Hovers_ClimbsAndYaws_UnderManualDrive` · `M7_ADeadRotorcraft_Falls` | new `RotorcraftModelTests` |
| `M7_APossessedDrone_FollowsTheStation_AndTheAiResumesOnRelease` | `PossessionTests` (Entity Control's suite) |
| `M7_ADive_OnATank_HitsItsRoof_AndKillsTheDrone` · `M7_ADive_UnderADeck_MeetsTheSlab` | new `DroneImpactSystemTests` + `DamageCalculationSystemTests` |
| `M7_AHoveringDrone_IsHeard_AsRotor` · `M7_ADroneOverhead_IsInTheBroadphase` | `SoundEmissionSystemTests` · `VisionBroadphaseTests` |
| `M7_FpvAttack_FliesTheRoute_Searches_PicksTheTankOverTheRifleman_AndDives` · `M7_TheBattery_EndsTheFlight` | new `FpvAttackNodesTests` |
| `M7_TheBridge_SkipsAFlyer` | `NavigationIntentBridgeSystemTests` |
| `M7_TheAirModule_RunsOnTheMuscleNode` (SimHost, editor) | `SimHostCoreLogicPackTests` · `EditorCapabilitiesTests` |
| a TKB rail for type 403 | `NedTkbBuilderCombatTests` |
| demo `ua-drone-shelter` | `scripts/utility-demo-check.py` |

## 6. As-built `2026-10-14` (backend, handoff B11 — the Muscle half)

The design carries **15 classes** (§2) and **1 sequence** (§3); the Muscle half is built, the Brain half (`FpvAttackNodes`, `FpvAttackParams`,
`FpvAttackState`, `DroneTargetDecision`) is the behaviour lane's. Built: `FlightParamsDto` (`Gen.Flight`, + `BodyRadiusM` / `BodyHeightM`),
`FlightParams` (376), `FlightState` (377), `FlightTkbTranslator`, `RotorcraftModel` (autonomous + manual + a dead flyer falls),
`DroneImpactSystem`, TKB type **403 "FPV drone"**, `SoundSourceClass.Rotor = 7` + audible while airborne, the `VisionBroadphase` overhead fix,
the bridge skipping a non-ground mover. Deviations, argued:

| design said | built | why |
|---|---|---|
| owned by role ~~`MuscleAir`~~ | ✅ **owned by `MuscleGround`** — R-3005 (CE-3199, [Motion Domains §5a](../motion-models/DESIGN_Motion_Domains.md)): one Muscle role; `HasKinematics` applies to `Gen.Flight`; rail `HrotOwnershipGroupsTests.CE3199_…` | the user dropped per-domain roles |
| a collider r 0.3, h 0.3 | ✅ stamped by `FlightTkbTranslator` from `Gen.Flight`'s body size | no ground physics gives a flyer one |
| impact: terrain via `SightBlocked`, colliders within 10 m | ✅ + the ground under it (`GroundHeightAt`); a body's band grown by the contact radius; once met, `Airborne = 0` (spent: one detonation), the model then holds it still | a drone at speed would cross a hull in one frame; `Airborne` is the "spent" flag both systems read |
| a lethal `DamageAssessedEvent` on the drone | ✅ 10 × its max health | the existing chain kills it on its `Health` owner |
| the battery | not in the Muscle (R-136: the behaviour's clock) | as designed |

Rails (§5): ✅ `RotorcraftModelTests.M7_ARotorcraft_FliesToA3DPoint_AndReportsArrival`, `M7_…_UnderManualDrive`, `M7_ADeadRotorcraft_Falls`,
`CE2168_OnlyAirBodies_…` · `DroneImpactSystemTests.M7_ADive_OnATank_HitsItsRoof_AndKillsTheDrone`, `M7_ADroneFlyingPastOrOver_MeetsNothing` ·
`SoundEmissionSystemTests.M7_AHoveringDrone_IsHeard_AsRotor` · `VisionBroadphaseTests.M7_ADroneOverhead_IsInTheBroadphase` ·
`NavigationIntentBridgeSystemTests.M7_TheBridge_SkipsAFlyer` · `SimHostCoreLogicPackTests` (air motion then impact in the pack) ·
`NedTkbBuilderCombatTests.M7_TheFpvDrone_…` · ✅ (backend, handoff 2 C2) `DroneImpactSystemTests.M7_ADive_UnderADeck_MeetsTheSlab` — a
walkable SLAB over a parked tank stops the dive (the warhead goes off on the deck, the tank untouched, the wreck rests on the deck); ⚠ as
built it was RED on a 0.1 s frame: bodies were tested before the terrain, so one segment crossing the deck AND entering the hull's band
behind it hit the hull ⇒ `DroneImpactSystem` now takes whichever is FIRST along the segment (the terrain's first blocked fraction,
bisected on `SightBlocked`; the ground contact; the body's entry) · ⭐ **CE-2214 (`2026-10-16`, behaviors, cross-lane): a body is met only when nothing solid lies between the segment's entry into its contact-grown band and the hull's own surface** (`DroneImpactSystem.MetThroughTerrain`) — ⛔ the C2 rail's 4.5 m deck hid it: under a LOW deck (test-town's garage, 3.0 m, over an M1's 2.44 m roof) the band grown by the drone's 0.8 m contact radius reaches 3.24 m, ABOVE the slab, so a drone coming down onto the slab's top "entered the hull" first and the tank under the deck took the full direct hit (live `ua-drone-shelter`: 3000 → 2550, the wreck resting on the slab at 3.2 m). Rail `DroneImpactSystemTests.CE2214_ADive_OntoALowDeck_JustAboveTheHull_MeetsTheSlab_NotTheTankUnderIt`, red-proved (the test dropped) · ✅ (handoff 2 C3) `EntityOrdersAcrossNodesTests.M7_APossessedDrone_FollowsTheStation_AcrossNodes`
— SimHost possesses a drone whose brain is on CGF; the frames' `Vertical` crosses as `ControlFrameInteractionPayload`, comes back as
dtManualDrive, and SimHost (the one Muscle role, R-3005) climbs it 20 → 22 m in 17 frames, then flies it forward; release resumes the
brain. ⭐ **The Brain half is BUILT `2026-10-16` as AirAttack's Dive profile** (B5) — [Air Operations §9i](../air-attack/DESIGN_Air_Operations.md): `AirAttackProfile.Dive`, the Search / Pursue / Dive stages, the battery, and `DroneTargets` (this file's `DroneTargetDecision`, a static scorer — §9i D-5 argues why not a catalogue decision). The demo `ua-drone-shelter` is authored (Urban Armour §8).

> ⭐ **`2026-10-14` (R-3007, `CE-3202` — 📄 `DESIGN_Air_Operations.md` §6b):** `DroneImpactSystem` bursts the first mount ONLY for a flyer
> whose `Gen.Flight DetonatesOnImpact` is set — this drone (type 403). Any other aircraft that meets something crashes: destroyed, at rest,
> no burst. The drone also states its own acceleration (`AccelMps2` 15 m/s²): its manual flight reaches what the axes ask at that rate
> and brakes to a hover when they are released.

> ⭐ **`2026-10-14` (Air Ops slice 2 — 📄 `DESIGN_Air_Operations.md` §5b):** `Airborne = 0` now has two meanings, told apart by GEAR:
> a body WITH ground contacts (`Body.Geometry`) is standing on them and takes off again; one WITHOUT (this drone) is spent and stays
> down. `DroneImpactSystem` skips a flyer whose `NavigationStatus.Phase` is TakingOff / Landing / Landed / Unloading / Loading — a
> helicopter's landing meets the ground on purpose.

## ⛔ HISTORY

- *2026-10-14, first draft (superseded the same day):* the drone had its own `PointMassFlightSystem` integrating its position (because
  Stride's linear integration moves only projectiles) and was registered in both `GroundKinematicsModule` and `StrideKinematicsModule`.
  🔒 User: *"no ad hoc exceptions, rather initially limited but extendable architecture. Motion needs to stay on muscle node"* · *"Drone
  will need to be controllable also from FPS possess mode"* · *"We can ignore stride completely."* ⇒ replaced by `RotorcraftModel` in the
  Air domain (Motion Domains, MD-A…MD-H approved).
