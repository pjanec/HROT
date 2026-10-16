<!--STATUS
state: LIVE
updated: 2026-10-16 (§5c CE-2206 a point inside the turning circle is reached)
build-state: BUILT (v1) — 🔒 user, 2026-10-14: "MD-A to MD-H approved"; MD-B's ownership half AMENDED the same day (R-3005, CE-3199): ONE Muscle role, MuscleGround, owns every body's position (§5a)
current-answer: §3 (the proposal), §4 (decisions), §5 (as-built), §5a (the ownership decision, R-3005) and §5b (the ground model's arrival and walls, CE-2198) — §1 measured ground, §2 the requirement. · §5c (a point inside the turning circle, CE-2206)
stale-below: nothing.
known-rot: §4 MD-B's "one ownership role per domain" is SUPERSEDED by R-3005 (one Muscle role) — the module-per-domain half stands.
known-conflict: docs/designs/navig-2/Navigation_Design_v2_0.md §7.1 routes Naval / Flying as extra KinematicsMode values INSIDE CarKinematics ("impl TBD", never built); this proposal gives each domain its own module instead (MD-B) — reconcile on approval.
related-designs:
  - docs/designs/airborne/DESIGN_Airborne_Insertion.md — OWNS two new air classes (Rope, Parachute), their models (RopeSlideModel, ParachuteModel) and the replicated Descent component that makes a Pedestrian TYPE an air body for a descent (D-1…D-3)
  - docs/designs/air-attack/DESIGN_Air_Operations.md — OWNS the aircraft route follower (AirRoute / AirPath), FixedWingModel (this file's MD-H row, now designed) and RotorcraftModel's route, tilt, take-off and landing (R-2015)
  - docs/designs/navig-2/Navigation_Design_v2_0.md — OWNS NavigationIntent, the bridge, the path providers (ground navmesh, volumetric) and layer selection; this file decides which MOTION MODEL consumes the intent
  - docs/DESIGN_Entity_Control.md — OWNS possession and manual drive (R-270, R-271 (backend): ManualDrive, KinematicsMode.Manual, FPS = possession by one station); this file makes manual drive per locomotion class (MD-D)
  - docs/DESIGN_Body_Geometry_And_Ground_Contact.md — OWNS body geometry, resting pose and the future "flight / parked motion model" (§3.1, §5); MD-G uses it
  - docs/DESIGN_Ownership_Groups_And_Grants.md — OWNS ownership groups per ROLE (R-172) and the kinematic group (R-170); R-3005: a flyer joins that one MuscleGround group (no per-domain role)
  - docs/DESIGN_World_Query_Seam.md — OWNS "every engine capability behind an interface" (R-250, R-252; §4a: controllers "are motion models — exactly what the production engine will supply"); the motion-model interface is that seam for motion
  - docs/designs/vehicle-tactics/DESIGN_FPV_Drone.md — the first consumer: a rotorcraft on the Air domain, AI-flown and possessable
  - docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md — V5 (AI reverse) is a fix inside the ground domain's wheeled / tracked models
-->

# Motion domains — one extendable architecture for people, vehicles, aircraft, boats and amphibians

## 2. The requirement

🔒 User, `2026-10-14`: *"We will need aircraft and boats and amphibious vehicles so we should design for that early, no ad hoc exceptions,
rather initially limited but extendable architecture. Motion needs to stay on muscle node."* · *"Drone will need to be controllable also
from FPS possess mode as any possessable movable entity."* · *"We can ignore stride completely."*

🔒 User, earlier (`.dev/_DONE/modularizing/design-talk.md:171`): *"I want to have different nodes playing the Sim's role, each taking care
of different kind of entities (like aircraft flight model Sim, ground vehicle Sim, human motion Sim etc.) Actually, i want these to be
separated on a ModuleHost's Module level."* — only `GroundKinematicsModule` and `NodeRole.MuscleGround` were ever built.

## 1. What exists — measured `2026-10-14`

| fact | where |
|---|---|
| routing to motion is by COMPONENT, no motion-model seam: `VehicleState + VehicleParams + NavState` ⇒ `CarKinematicsSystem`; `CrowdAgent` ⇒ crowd; else `SimTransform + SimVelocity` ⇒ `LinearKinematicsSystem` (today: projectiles) | `CarKinematicsSystem.cs:80-90`, `LinearKinematicsSystem.cs:91-111` |
| inside CarKinematics the model is an `if`: `Pedestrian ? HumanGait : BicycleModel`, and the same `if` picks `ManualMotion.Person / .Vehicle`; 2.5-D (yaw only, no vertical control) | `CarKinematicsSystem.cs:165-195,339-389,434-436` |
| `TerrainMobility { Tracked, Wheeled, Infantry, Air, Naval }` collapses to `VehicleClass` — **Air and Naval become PersonalCar** | `SimVehicleDef.cs:3-10`, `BdcTkbBuilder.cs:431-439` |
| `KinematicsMode.Crowd / Naval / Flying`, `NavAgentProfile.MobilityProfile`, `NavLayerMask.Naval / Air`: declared, never read or written in production; SimHost composes no volumetric provider | `NavigationEnums.cs:13-27`, `PathfindingSolverSystem.cs:185-192`, `SimHostNodeBootstrapper.cs:319` |
| commands Brain → Muscle: `NavigationIntent` (autonomous, Vector3 destination) and `ManualDrive { Forward, Lateral, Steer, Yaw, Gait, Seq }` (manual) — both Brain group, replicated | `NavigationComponents.cs:230-333`, `ManualDrive.cs`, `HrotOwnershipGroups.cs:45,70` |
| possession: station → `OperatorControlFrameEvent` → `PossessionSystem` (Brain) → `OperatorControl` behaviour → `NavigationIntent.Mode = Manual` + `ManualDrive` → the Muscle's Manual branch; the station knows only person vs vehicle (`FpsCameraDriver.IsVehicle`); "possessable" is no marker — any addressable, live unit | `Possession.cs:117-234`, `OperatorControlBehavior.cs:53-82`, `PossessionStation.cs:194` |
| ownership: one motion role, `MuscleGround` (kinematic group: `SimTransform, SimVelocity, VehicleState, VehicleParams, NavState, NavigationStatus, …`), granted when the template has `VehicleParametersDto` | `HrotOwnershipGroups.cs:78-123`, `NodeRole.cs:89` |
| no naval / boat / amphibious / flight code anywhere | graph + grep |

### INVENTORY — the queries run

| query | total | the set |
|---|---|---|
| `search_graph .*Kinematic.*` (Class) + grep `SimTransform` writers | 6 movers | CarKinematics (+HumanGait, BicycleModel, ManualMotion), CrowdAgentUpdate, LinearKinematics, Ballistics (gravity), EmbarkedState (passengers), DeadReckoningSync (ghosts) |
| `search_graph .*Naval.*\|.*Boat.*\|.*Ship.*\|.*Amphib.*\|.*Flight.*` (Class) | 0 | — |

## 3. The proposal

```mermaid
graph TD
  TKB["TKB: Gen.Locomotion { Class }<br/>Pedestrian · Wheeled · Tracked · Rotorcraft · FixedWing · Surface · Amphibious"]
  subgraph BRAIN["Brain (CGF) — unchanged"]
    NI["NavigationIntent (autonomous)"]
    MD["ManualDrive (possession) + Vertical axis"]
  end
  subgraph MUSCLE["Muscle node(s) — one module per DOMAIN, ONE ownership role: MuscleGround (R-3005)"]
    G["GroundMotionModule<br/>Pedestrian · Wheeled · Tracked · Amphibious"]
    A["AirMotionModule (new)<br/>Rotorcraft (v1) · FixedWing (later)"]
    N["NavalMotionModule (new, later)<br/>Surface"]
  end
  TKB -->|"which domain owns the entity"| G
  TKB --> A
  TKB --> N
  NI --> G
  NI --> A
  NI --> N
  MD --> G
  MD --> A
  MD --> N
  classDef later fill:#fff3cd,stroke:#c90
  class N later
```

*What the picture shows that prose hid: the Brain side does not change at all — one autonomous command and one manual command for every
kind of mover; the domains differ only on the Muscle, each in its own module with its own ownership role, so one SimHost can hold all
three (the default) or three nodes can each hold one — the earlier requirement.*

```mermaid
classDiagram
  class IMotionModel {
    <<new interface, Fdp.Toolkits Motion>>
    +Class : LocomotionClass
    +Autonomous(ref MotionFrame, in NavigationIntent, dt)
    +Manual(ref MotionFrame, in ManualDrive, dt)
  }
  class MotionFrame {
    <<ref struct>>
    ref SimTransform · ref SimVelocity
    the domain's state + params
    IWorldQuery world
  }
  class LocomotionDto { <<new TKB descriptor "Gen.Locomotion">> Class }
  class LocomotionClass { <<enum>> Pedestrian · Wheeled · Tracked · Rotorcraft · FixedWing · Surface · Amphibious }
  class MotionModelTable { <<new static>> Model(LocomotionClass) IMotionModel }
  class GroundMotionModule { <<existing GroundKinematicsModule, grows>> }
  class AirMotionModule { <<new>> AirMotionSystem }
  class NavalMotionModule { <<new, later>> }
  class RotorcraftModel { <<new: point mass, climb, hover, yaw>> }
  class HumanGaitModel { <<wraps HumanGait + ManualMotion.Person>> }
  class BicycleVehicleModel { <<wraps BicycleModel + ManualMotion.Vehicle>> }
  class AmphibiousModel { <<later: Bicycle on land, Boat on water, by SurfaceTypeAt>> }
  class FlightState { <<new component, MuscleGround group>> }
  class FlightParams { <<new component, from TKB>> }
  IMotionModel <|.. HumanGaitModel
  IMotionModel <|.. BicycleVehicleModel
  IMotionModel <|.. RotorcraftModel
  IMotionModel <|.. AmphibiousModel
  MotionModelTable ..> IMotionModel
  GroundMotionModule ..> MotionModelTable
  AirMotionModule ..> MotionModelTable
  NavalMotionModule ..> MotionModelTable
  RotorcraftModel ..> FlightState
  RotorcraftModel ..> FlightParams
  LocomotionDto ..> LocomotionClass
```

*What the picture shows: one interface, one table from class to model; a module per domain only SCHEDULES its classes' models. A new mover
is a new model + a table row, never a branch in someone else's system. Ground's existing `if Pedestrian` becomes two table rows.*

```mermaid
sequenceDiagram
  participant ST as FPS station
  participant PS as PossessionSystem (Brain)
  participant OC as OperatorControl behaviour (Brain)
  participant AM as AirMotionSystem (Muscle, MuscleGround owner)
  participant RM as RotorcraftModel
  ST->>PS: OperatorControlFrameEvent (the drone's control scheme: throttle, yaw, climb)
  PS->>OC: assign OperatorControl (origin Possession, the AI task pauses)
  OC->>AM: NavigationIntent.Mode = Manual + ManualDrive { Forward, Lateral, Yaw, Vertical }
  AM->>RM: Manual(frame, drive, dt)
  RM-->>AM: SimTransform, SimVelocity
  Note over AM,RM: release ⇒ Mode back to the AI's intent ⇒ Autonomous(frame, intent, dt)
```

*Possession of a drone is the existing chain; only the axes and the model differ — the same holds for a boat later.*

## 4. Decisions — 🔒 APPROVED `2026-10-14` ("MD-A to MD-H approved")

| id | question | lean | blast radius / what would change it |
|---|---|---|---|
| **MD-A** | how a type says how it moves | a TKB `Gen.Locomotion { Class }` with the 7 classes; `VehicleClass` stays as the ground preset key; the lossy `Air/Naval → PersonalCar` mapping ends | TKB data + one translator; every existing type maps 1:1 (Infantry→Pedestrian, Wheeled, Tracked) |
| **MD-B** | where each domain runs | **one module per domain** (Ground = the existing module, Air new, Naval later). ⛔ ~~and one ownership role per domain (`MuscleGround`, `MuscleAir`, `MuscleNaval`) with its own kinematic group~~ — ⭐ **AMENDED `2026-10-14` (R-3005, CE-3199): ONE Muscle role, `MuscleGround` (name kept), owns every body's position;** the domain modules all run on the node holding it (§5a) | the earlier requirement; ownership groups + node plans; ⚠ supersedes Navigation v2 §7.1's "modes inside CarKinematics" |
| **MD-C** | the seam | `IMotionModel { Autonomous, Manual }` + a class → model table; new domains use it from day one; ground's two branches become `HumanGaitModel` / `BicycleVehicleModel` in its own slice (behaviour-identical, its rails unchanged) | R-250 (engine capability behind an interface); ground refactor is a slice of its own, not a prerequisite for Air |
| **MD-D** | commands and possession | the SAME two commands for every domain: `NavigationIntent` (autonomous, already 3-D) and `ManualDrive` + a `Vertical` axis (climb / dive); the FPS station picks a control scheme by locomotion class instead of `IsVehicle`; any unit with a motion model is possessable — no marker | one wire field; R-270 unchanged |
| **MD-E** | navigation per domain | ground: the navmesh layers (as today); air: the volumetric provider (v1 straight legs between waypoints, obstacles later); naval: the `Naval` navmesh layer (already in the bake parameters); layer selection by locomotion class | Navigation v2 owns the providers |
| **MD-F** | amphibious | ONE model in the Ground domain composing the wheeled and boat integrators, regime chosen each frame by the surface below (`SurfaceTypeAt`); the entity never changes domain on entering water (no ownership move) | needs the boat integrator (Naval) first |
| **MD-G** | grounding (R-249) | each model owns its Z: ground classes clamp to the surface, air never (it lands by `BodyGeometry.RestingPose`), surface rides the water level | ⚠ measured: `GroundClampingConfig` is registered only on IG, so SimHost clamps everything today (assumed, not run) |
| **MD-H** | v1 scope ("initially limited but extendable") | the interface + table, `Gen.Locomotion`, the Air module and role, `RotorcraftModel` (the drone: point mass, hover, climb, yaw, autonomous + manual), the station's air scheme; FixedWing / Surface / Amphibious are enum values and table rows that fail loudly until built | the drone needs exactly this; nothing ground changes in v1 |

⛔ **Rejected:**
- *More `KinematicsMode` values inside CarKinematics* (Navigation v2 §7.1) — every new mover becomes a branch in a ground system, and a
  per-domain node split becomes impossible.
- *The drone's own `PointMassFlightSystem`* (FPV design, first draft) — a one-off mover beside the architecture; it becomes
  `RotorcraftModel` in the Air module.
- *Flyers on `LinearKinematicsSystem`* — that is a projectile integrator with no control, manual or autonomous.

## 5. As-built `2026-10-14` (backend, handoff B10) — and the one decision it needs

The design carries **11 classes** (§3 class diagram), **1 sequence** (possession of a drone) and the domain graph. Built: `LocomotionClass`
+ `LocomotionDto` (`Gen.Locomotion`), the `Locomotion` component (id 375) and `LocomotionTkbTranslator`, `MotionDomain` + `Locomotions`,
`IMotionModel` + `MotionFrame` + `MotionModelTable`, `AirMotionSystem` + `AirMotionModule` (SimHost and the editor's Muscle, through
`SimHostCoreLogicPack`), `RotorcraftModel` (the drone's, B11), `ManualDrive.Vertical` end to end, the station's control scheme by class.
Not built: `GroundMotionModule`'s two-model refactor (MD-H: a later slice), `NavalMotionModule`, `AmphibiousModel` (table rows that fail loudly).

| design said | built | why |
|---|---|---|
| MD-B: ~~role `MuscleAir` with its own kinematic group~~ | ✅ **as decided by R-3005: the ONE `MuscleGround` group** — `HrotOwnershipGroups.HasKinematics` also applies to a template with `Gen.Flight` (`FlightParamsDto`), and `FlightParams` / `FlightState` sit in the group beside `VehicleParams` / `VehicleState`; rail `HrotOwnershipGroupsTests.CE3199_AFlyerIsMovedByTheOneMuscleRole_…` | 📐 before: the drone had no `VehicleParametersDto`, so no group applied and its position stayed with its creator — the SimHost's air module (gated `WithOwned<SimTransform>`) would not have moved it on a split cluster. `Locomotion` stays in no group: TKB classification, provisioned on every node, never written after spawn (like identity, Ownership §1.3) |
| the station picks a scheme by locomotion class | ✅ `ControlScheme { Person, Vehicle, Rotorcraft }` from `Locomotions.Of`; rotorcraft: W/S forward, A/D sideways, mouse yaw, Space / C vertical, the camera on the body | — |
| "one wire field" (`ManualDrive.Vertical`) | ✅ on `EntityManualDrive` **and** on `ControlFrameInteractionPayload` (appended) | ⚠ the station's frame reaches the brain through the interaction union, not the ManualDrive topic — the climb axis must ride both or it never reaches the `OperatorControl` behaviour. Same field, same feature |
| `MotionModelTable` (static) | ✅ a static switch: Rotorcraft → `RotorcraftModel`; ground classes → null (CarKinematics in v1); FixedWing / Surface / Amphibious → `NotSupportedException` | MD-H's "fail loudly" |
| — | `LinearKinematicsSystem` skips a body with a `Locomotion` | it integrated EVERY `SimTransform + SimVelocity` body without `VehicleState` — a drone would have moved twice |
| — | `NedTkbBuilder.WithPhysics` states `Gen.Locomotion` (Tracked / Wheeled / Pedestrian) and — B8 (M6) — the hull's `Body.Geometry`; `MapMobility(Air / Naval)` throws; `WithFlight` states an air type | MD-A: the lossy Air / Naval → PersonalCar mapping ends |

> ⭐ **`2026-10-14` (Air Ops slice 2):** `RotorcraftModel` grew a route follower (take-off, landing on the gear, holds, eased speed,
> pitch and roll) and `MotionFrame` carries the terrain; `AirMotionSystem.RouteSource` compiles the body's `AirPath`. 📄
> `docs/designs/air-attack/DESIGN_Air_Operations.md` §5b.

> ⭐ **`2026-10-14` (Air Ops slice 4):** MD-H's **FixedWing row is built** — `FixedWingModel` (kinematic `AirPath` follower ≥ stall,
> banked from the curvature, circles at the end); `MotionModelTable.Model(FixedWing)` no longer throws (Surface / Amphibious still do).
> 📄 `docs/designs/air-attack/DESIGN_Air_Operations.md` §6a.
>
> ⭐ **`2026-10-14` (Air Ops slice 6, backend half):** the manual side of both air models. `FixedWingModel.Manual` is the mouse-aim pilot
> on the same `ManualDrive` axes: `Yaw` = the look, `Vertical` = the climb angle, `Forward` = throttle. It makes a coordinated, self-banking
> turn and cannot stall. `RotorcraftModel.Manual` keeps its direct axes and gains the route's tilt and an explicit hover hold. No wire change.
> 📄 Air Ops §6b.
>
> ⭐ **`2026-10-14` (R-3007):** `Gen.Flight` + `AccelMps2` (per type; 0 ⇒ the model's own: rotorcraft 3, plane 5) and `DetonatesOnImpact`
> (only a munition-flyer bursts on impact). Manual rotorcraft flight now eases to the axes' velocity at that rate; the plane's throttle is held.

### 5a. The ownership decision — CE-3199 ✅ DECIDED `2026-10-14` (R-3005): option (A), one Muscle role

🔒 User, `2026-10-14`: *"lets drop the whole MuscleXXX (ground, air...) and use one single Muscle role, so all positions are owned by it,
as now."* · *"keep MuscleGround so we do not need to rename"*

| option | what it is | outcome |
|---|---|---|
| ✅ **(A) one kinematic group for every mover** | `MuscleGround` is "the Muscle" (name kept); `HasKinematics` also applies to a flyer; every domain module runs on the node holding it | **chosen** — one line in the table + two members; no wire, no rename |
| (B) domain-exclusive groups (my lean) | a component in several groups whose `AppliesTo` never both hold; `MuscleAir`, `MuscleNaval` | ⛔ rejected — the per-domain node split it kept is not wanted |
| (C) an air position descriptor | a separate dtAirPos | ⛔ a new wire descriptor |

⚠ What (A) gives up, stated so nobody rebuilds it by accident: a split deployment can no longer put the air module on a node of its own —
the earlier *"different nodes … aircraft flight model Sim, ground vehicle Sim"* requirement (§2) is withdrawn for ownership. The
module-per-domain structure (MD-B's first half, MD-C's table) stands.

## 5b. The ground model: a short move ARRIVES, a hull never enters a building — `CE-2198` (`2026-10-16`, backend)

📐 Measured live (`ua-tank-corner`, behaviors' handoff): an M1 sent 37 m at its top speed ran 9 m, then 26 m past its point, into a 4-storey
block, and was lifted onto its 15 m roof. Reproduced in `CarKinematicsSystemTests.CE2198_*`: the path mode drove **440 m** on a 37 m path.

```mermaid
sequenceDiagram
    participant K as CarKinematicsSystem (the ground model)
    participant C as SpeedController (proportional, AccelGain)
    participant W as IWorldQuery
    K->>K: target speed ≤ StoppingSpeed(d) = sqrt(2 · MaxDecel · (d − v / AccelGain)) — the envelope LESS the controller's lag
    K->>C: the target speed
    C-->>K: the acceleration (it follows a braking ramp late — hence the lag term)
    K->>K: arrived (inside ArrivalRadius) ⇒ LATCHED: the end-homing never re-engages
    K->>K: homing on an end BEHIND the hull (progress ran ahead) ⇒ the crawl speed while it turns
    K->>W: the step's hull-height line (0.5 m up): Trace(Fire) — any ClosedBarrier? / a rise steeper than 1 and taller than 0.5 m?
    W-->>K: a wall ⇒ the step is not taken (speed 0)
```

*Caption:* what the picture shows that the code comments hid — the overshoot was a FEEDBACK loop, not a braking constant: the controller's lag
carried the hull past the radius, the CE-2059 end-homing re-targeted a point now behind it at a speed capped only by DISTANCE, the hull could not
turn at speed, so the distance grew and the cap rose. Three cuts, one each; and the building is a wall to the MOVER (a ground vehicle), while
the surface sampler keeps its "a unit spawned inside a footprint stands on the roof" rule for spawns.

| decision (built) | where | rejected — one line each |
|---|---|---|
| one `StoppingSpeed(params, d, v)` for the three envelopes (direct, path end, end-homing), less the lag `v / AccelGain` | `CarKinematicsSystem` | a stronger gain — changes every vehicle's feel; a fixed margin — wrong at every speed but one |
| the arrival LATCHES the end-homing (`HasArrived == 0` to home) | `SampleCustomTrajectory` | re-homing after arrival — the runaway |
| a ground vehicle never takes a step through a `ClosedBarrier` or up a wall | the write-back (beside CE-3145's "a person never steps off a ledge") | the sampler refusing the roof — it would break a unit spawned inside a footprint; static colliders for buildings — a second geometry of the same walls |

## 5c. The ground model: a point inside the turning circle is REACHED, never orbited — `CE-2206` (`2026-10-16`, backend)

📐 Measured live (`ua-javelin-lock`, behaviors' [handoff](../../blueprints/batches/HANDOFF_Behaviors_To_Backend_Vehicle_Orbit.md)): an M1 sent
2.3 m to a cover point drove a ~5 m circle round it for 60 s; the move never ended.

```mermaid
graph LR
  H["homing on the point<br/>(direct, or a path's end)"] --> Q{"the point inside<br/>a turning circle?<br/>R = WheelBase / sin MaxSteer"}
  Q -- yes --> S["drive on STRAIGHT<br/>at TurnClearanceSpeed (2 m/s)"]
  S --> Q
  Q -- no --> A["one arc of radius ≥ R<br/>reaches it — pure pursuit"]
  A --> Z["arrived"]
```

*Caption:* what the picture shows that the code hid — pure pursuit at full lock traces the hull's SMALLEST circle; a point inside it is never
on that circle, so the follower orbits for ever. Driving straight moves both circles off the point (a driver's U-turn), after which a single
arc reaches it. Neither reversing nor a looser arrival is needed.

| decision (built) | where | rejected — one line each |
|---|---|---|
| `InsideTurningCircle(params, pos, fwd, point)`: inside either circle of radius `WheelBase / sin MaxSteerAngle` (the bicycle model's, as the cornering limit uses) ⇒ head straight on at `TurnClearanceSpeed` until it is not | `CarKinematicsSystem` — Direct mode and a path's end-homing | reversing onto it — the circles mirror, a point to the side stays inside; success "as close as the circle allows" — a 2.3 m move could end ~7 m off; a loop detector — state, and it still circles once |

⭐ Rail: `CarKinematicsSystemTests.CE2206_AHullSentToAPointInsideItsTurningCircle_Arrives` (an M1 sent 2.3 m to its left, both modes:
arrives within 2.5 m, the excursion bounded) — red-proofed. Behaviors' CE-2205 (the cycle's 4 m arrival) stays: it serves that caller.

