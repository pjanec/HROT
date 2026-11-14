<!--STATUS
state: LIVE — MEASUREMENTS, not a design (user, 2026-11-14: "No hurry in handoffs and design. Let's measure first. Let's make sure that we understand all the weak points.")
updated: 2026-11-14
current-answer: ⭐⭐ §8 THE DECISION (one member, R-4244) · ⭐ §4 the second sweep — the REPRESENTATIVE-MEMBER shape, which retires most of §1 · §5 the assets (vehicles, fires, air, drones, AT) · §6 calibration numbers · §7 still unknown; §1–§3 the first sweep (a squad with NO members)
stale-below: §4's "TWO members = the two fire teams" lean — SUPERSEDED by §8 (one member); §1's W-1, W-2, W-4, W-6, W-7 assume a squad with NO members — the §4 shape removes them (kept as the record of why)
known-rot: none
related-designs:
  - ../group-maneuvers/DESIGN_City_Battle_Showcase.md — §12 the test ladder this would add a rung to (between ② the puppet bench and ③ the dry twins)
  - ../group-maneuvers/DESIGN_Command_Chain_Example.md — the order-down / report-up seam the stand-in rests on; §8e the battalion's reserve
  - ../../DESIGN_Scenario_Rules.md — §10.2 the objective kinds that count men (ObjectiveCensus)
  - ../../DESIGN_Parallel_Simulation.md — §18.5–§18.8 the per-entity rules an attrition step must follow
-->

# MEASURED — what an "aggregate squad" (a squad with no men) would meet *(coordinator, 2026-11-14)*

🔒 User, `2026-11-14`: *"replace the squads with a puppet squad, meaning just the Squad commander will play for the whole squad … only the command
chain stays in the play … a great base for simulating larger battles without individual units … an introduction to aggregated unit fighting
constructive simulation."* Four read-only sweeps (reports up, orders down, everything that counts men, the plumbing); the claims below that
carry a decision were re-read by the coordinator (✔). Graph coverage could not be checked (the CLI has no coverage tool).

## 1. The weak points, ranked

| # | weak point | evidence | cost to fix |
|---|---|---|---|
| **W-1** | ✔ **a unit is invisible and invulnerable by construction** — no sensor sees it, no round, burst or near miss touches it; two aggregate squads can never detect each other ⇒ no SPOT, no CONTACT, no fire-support request, no reinforce request: the chain never starts | `PhysicalPresence.IsHidden` = has `UnitEntity` (`Perception/PhysicalPresence.cs:27`); grid, LOS, `AreaEffectSystem.cs:172-176` skip it; perception is per man | NEW: an aggregate detection path (range + line of sight from the unit point, asked of the Muscle, R-2037) writing the squad's contact pool; an aggregate damage / suppression sink for direct fire and bursts. ⭐ The largest piece |
| **W-2** | ✔ **strength is a men count; the two shapes of a unit with no men both lie** — an EMPTY roster = wiped out (Black, out of the fight); NO roster = in the fight for ever (never lost, never routed) | `CombatEffective.cs:54-56`; `StaffSystems.StrengthOf :416-422`; read directly by `PlatoonDefend :321, :617-618`, `PlatoonAttack :711`, `SopConditions :111`, rout (CE-7015) | ONE seam: a unit with an aggregate strength answers `IsInTheFight` / `FightingMembers` / `StandingMembers` / `StrengthOf` from it (+ broken from its suppression) |
| **W-3** | ✔ **platoons order squads by CONCRETE behaviour name, not by the class-resolved order** — `Assign` publishes `IntentId = behaviour` (`CommandNodes.cs:80-86`); the class table (`Order.<name>` → `<Class><Order>`, CE-2142 / R-273, `TacticalIntentResolutionSystem.cs:134-142`) is used only by `CommandOrders.Order`. ⇒ an aggregate cannot get its own `SquadDefend` without a name clash | ✔ read | either move the platoons' squad orders to class-resolved orders (≈ 8 names; matches R-273's intent) or branch inside each squad behaviour. Lean: the first |
| **W-4** | **every existing squad play fails at once with no men** — `SquadManeuverRunner.AnyLive` loops the roster ⇒ Failure ⇒ TaskFailed ⇒ the platoon reads a broken squad | `SquadManeuverRunner.cs:711` | aggregate plays of their own (W-3) for the ordered set: SquadDefend, BoundingOverwatch, FireAndMovement, ClearBuilding, BreakContact, SquadRecover, Move / SquadMarch + the SOP's ReactToContact |
| **W-5** | **objectives count bodies** — HoldArea / ReachArea / ClearArea, kills, losses, the score: an aggregate counts 0; an enemy aggregate never occupies an area | `ObjectiveCensus.IsMan :24` (excludes units), `HoldAreaBehavior :62-109`, `ReachAreaBehavior :38`, `ClearAreaObjectiveBehavior :48`, `CounterBehavior :65`, `MeasuresEvidence :72` | the census counts an aggregate as strength-weighted men; losses from strength |
| **W-6** | **the fire-support loop has no aggregate meaning end to end** — the request point comes from contacts (W-1); the battery fires REAL rounds whose bursts skip units (W-1) | `SupportCommandBehavior.cs:85-107`, `StaffSystems.cs:317` | solved by W-1's two paths; nothing new in the HQ |
| **W-7** | **nothing moves a body-less unit** — its position is the centroid of live members, else frozen | `UnitCentroidSystem.cs:51-82` | cheapest: give the aggregate type a Pedestrian-class `VehicleParams` ⇒ the existing `LocomotionMoveTo` → `CarKinematics` walker moves it on the navmesh (⚠ the walker without stance / injury / animation not verified) |
| **W-8** | **convoys and embarkation read men directly** — the convoy would see the squad as aboard for ever | `PlatoonConvoyBehavior.cs:495-525`, `SquadEmbarkBehavior.cs:70` | out of scope for v1: aggregate squads do not ride (embark / dismount / fast-rope / airborne exit unsupported) |
| **W-9** | **own-casualty evacuation has no meaning** (SquadRecover of its own man) | `CasualtyCareBehavior.cs:138-148` | unsupported for own men; works for a real outsider |
| **W-10** | **the IG / exercise control would not see strength** — replication is opt-in per component, hand-written translators | `Replication/Map/Egress/` (pattern `EntityDamageEgressTranslator.cs`) | a descriptor + egress / ingress for strength and suppression; recording and seek come free (unmanaged, `DataPolicy.Default`) |
| **W-11** | **the dynamism measure does not exist** and, as designed, counts men | CE-7033 not built; `DESIGN_City_Battle_Showcase.md:614` | build it on unit positions, not men |

## 2. What works for us — the seams measured

| seam | measured |
|---|---|
| reports up | one pipeline: `ReportQueue.Enqueue` → `StaffComposerSystem.Flush` (3 s delay, posture mute) → `StaffInboxSystem.Fold` → the superior's `CommandPicture` (`Squad/Command/StaffSystems.cs:427-447, :522-563`); Set / TaskFailed come from `BehaviorFinishedEvent` (`:80-91`) ⇒ an aggregate that ends its plays correctly reports correctly, for free |
| what a commander reads | the picture (progress, colour, elements, requests), the squad's `SimTransform`, `BehaviorState` (origin + hash, for `StillRunning` / "fighting"), `IsAlive`, and men counts (W-2) — nothing else |
| company / battalion | read only reports and orders (`CompanyBehaviors.cs`, `SupportCommandBehavior.cs`) ⇒ unchanged |
| the parallel rule | Lanchester-style attrition fits it exactly: each squad computes its OWN loss from last tick's enemy strength and writes only itself (`DESIGN_Parallel_Simulation.md` §18.5) |
| where damage lives | the Brain (`CombatHealth` in the brain group, `HrotOwnershipGroups.cs:53, :171-175`); detection must ask the Muscle (R-2037) |
| drawing | a body-less unit keeps its 2-D symbol (`EntityPresentationGizmo.cs:37`, `PhysicalPresence.cs:33-34`); no 3-D body (`EntityClassShapeLibrary.cs:40`) |
| prior art | ⛔ none — no aggregate, constructive, puppet or attrition design in `docs/` or `.dev/`; `AggregateState` (Vis2D) is declared with no writer and no reader |

## 3. Not measured yet

How macros create units · whether `BasicSquadSop` misbehaves with no members (beyond W-4) · the walker without stance / injury / animation (W-7) ·
the descriptor work for the IG (W-10) · real drill durations (the rails teleport men) — needed to calibrate aggregate move and assault times ·
the producer gaps the sweep found in passing: no producer for PROGRESS PhaseLine / ObjectiveSecure / FallingBack (read at `CompanyBehaviors.cs:804`) /
FireLifted, LACE and ammo colour.

## 4. The second sweep — a squad of TWO representative members *(2026-11-14; user: "carrying weapons and sensors")*

🔒 User, `2026-11-14`: *"The decision is already clear. We go with the aggregate squad … It would be nice to keep the vehicles in the play … if we
somehow approximate the squad position and firepower with the aggregate squad, carrying weapons and sensors, we might still achieve some
pseudo-realistic battle, enough for that simple level of constructive simulation."*

| claim | measured |
|---|---|
| ✔ only four things hide an entity: `UnitEntity`, an enclosed passenger, a round in flight, a map graphic | `Perception/PhysicalPresence.cs:26-29` ⇒ the squad UNIT stays hidden (as today); a separate MEMBER entity is seen, hit and shoots through every existing path |
| a member needs only data to be a full participant: sensors (`SensorCapabilitiesDto` → receptor, eye, `TargetMemory`), several built-in mounts (rifle, LMG, an RPG-class mount; the Bradley's 25 mm + TOW precedent), `Health`, a collider | `PerceptionTkbTranslator.cs:33-110`, `CombatTkbTranslator.cs:86-125`, `AimAndFireExecutor.cs:114-121` (a built-in mount fires as the platform — no gunner) |
| its memory feeds the squad's contact pool unchanged ⇒ SPOT, CONTACT, fire-support and reinforce requests flow again | `SquadPerceptionMergeSystem.cs:47-95` |
| as a PERSON (Pedestrian class) it gets stance exposure, blast near misses, `UnderFire`, `Stress` — suppression and rout for free | `RaycastSolverSystem.cs:287-298`, `AreaEffectSystem.cs:177, :544-547`, `StressSystem.cs:374` |
| ✔ the drills split a squad into TWO halves and FAIL when a half is empty | `FireAndMovementBehavior.cs:100-104` (`if (!any) return Failure`) ⇒ ONE member breaks bounding, rush and break contact; ⭐ **TWO members = the two fire teams** keep them working |
| firepower and toughness are data: `RateOfFire` × n, `Health` = n × 25 (a soldier is 25, a rifle hit 25 ⇒ one rifle hit ≈ one man) | `WeaponSuiteDto.cs:141`, `AimAndFireExecutor.cs:315`, `NedTkbBuilder.cs:279`, `NedTkbCatalog.cs:87` |
| the walker moves an entity with no stance / injury / animation (every read defaults); a bare Pedestrian entity is already in a test | `CarKinematicsSystem.cs:203-212`, `CarKinematicsSystemTests.cs:496-527` |
| a squad authored with no men has NO roster (lazy) ⇒ "in the fight for ever"; with two members the roster is real | `UnitHierarchySystem.cs:170-172` |

**The weak points of the two-member shape** (they replace §1):

| # | weak point | evidence | fix shape |
|---|---|---|---|
| **A-1** | strength is all-or-nothing per member: a member at 30 % health counts as one man in the fight (SITREP Green until it dies) | `CombatEffective.cs:54-56`, `StaffSystems.StrengthOf :416-422` | ONE seam: an aggregate member counts as `n × Health / Max` men in `FightingMembers` / `StrengthOf` / the census |
| **A-2** | one big round empties a member (a 125 mm hit does 1100; a member holds n × 25) | `HealthApplicationSystem.cs:126` | a per-hit cap for aggregate members (≤ ~2 men a hit) |
| **A-3** | a burst tests ONE person-sized body — a squad's 30 m spread is not represented; rounds have no spread knob at all | `AreaEffectSystem.cs:530-547`, `RaycastSolverSystem.cs:244-300` | a blast casualty fraction by the member's frontage (`PresentedAreaM2` is the only existing hook); accept rounds as one body (under-represents, never over) |
| **A-4** | all fire leaves one muzzle, one dispersion sequence | `HitModel.cs:46-62` | accept for v1 |
| **A-5** | injury and bleed-out would treat the pool as one man | `WithInjury` | omit injury on the member type |
| **A-6** | room clearing needs a stack of four | `ClearBuildingBehavior.cs`, `RoomDrill.cs` | an aggregate `ClearBuilding` (a timer per building by size, a strength exchange with the occupants) under the class-resolved order (§1 W-3) |
| **A-7** | the census counts each member as one man | `ObjectiveCensus.IsMan :24` | the A-1 seam (n-weighted) |
| **A-8** | targeting weights under-read a squad: FPV person 0.3, the spotter's 30 m cluster | `AirAttackBehaviors.cs:1050-1081`, `SpotForFireBehavior.cs:107-146` | weight by the men a member stands for |
| **A-9** | a member is drawn as ONE soldier body in 3-D | `EntityClassShapeLibrary.cs:40` | v1: one body with an "×n" card row; later a group figure |

## 5. The assets — keep the few real *(the second sweep)*

| asset | depends on men? | meets a representative member | lean |
|---|---|---|---|
| tanks, IFVs, bulldozer | no (crew abstract by design, `InteractionComponents.cs:118`; `NeedsGunner` stations excepted, `WeaponSystems.cs:88-91`) | sees and shoots it as an armed person (`ThreatDanger.cs:21-42`); is shot by its RPG mount through `ArmorModel` | ✅ keep real, unchanged |
| mounted infantry (carriers) | seats count men (`PassengerBuffer.Capacity = 12`) | two members = two seats; embark / dismount / `LiveMen` work on two bodies | ✅ works with the two-member shape (⚠ unmeasured live) |
| mortar / towed gun | ⛔ yes — a tube needs a man in its gunner seat (`WeaponSystemDto.cs:18`, `WeaponSystems.cs:94`; the showcase strike refusal) | fires at a point; its burst hits members by A-3 | ✅ keep real with its few gunners (or `NeedsOperator = false` later) |
| SP howitzer (M109) | no (fires from its own mount) | as above | ✅ keep real |
| HQ fire support, company smoke, aircraft | no — all fire at a POINT (`SupportCommandBehavior.cs:82-107`, `AirAttackBehaviors.cs:464, :784-788`) | as above | ✅ unchanged |
| spotter | any entity with memory and a position (`SpotForFireBehavior.cs:107-146`) | a member can spot | ✅ |
| drones (FPV, bomber, recon) | the operator only orders | dive on / bomb a member (A-8 weight); recon reports through squad membership | ✅ keep real; the operator stays a man (a drone team of 2) |
| AA (ZU-23 crew, Stinger, Igla gunners) | yes — men | AA only | ✅ keep as small real teams |
| AT (Javelin, Kornet, TOW teams; RPG) | Javelin / tripods: a carrier or gunner; RPG: the man's own mount | RPG becomes a mount on a member; ATGM teams | ✅ RPG on the member; ATGM teams stay real (few) |

## 6. Calibration numbers from LIVE runs *(for aggregate timings and attrition)*

| what | number | source |
|---|---|---|
| squad bounding overwatch ~100 m open field | ≈ 95 s (17 bounds) | `DESIGN_Squad_Tactics.md:1418-1420` |
| fire and movement | 84–88 m in 90 s · in pairs 67 m in 120 s | `DESIGN_Squad_Tactics.md:1538` |
| platoon bound | ≈ 140 s | `PLAN_Feature_Films.md:80` |
| platoon attack on a house | support Set 47 s · clear with the lift 341 s | `DESIGN_Defend_And_Attack_Position.md:1741` |
| building clear (6 rooms) | ≈ 200 s, 3 lost | tracker `:1722` |
| room clear | 38–50 s, 2–3 lost (3 of 4 runs) | tracker `:1802` |
| defence Set | 2.5 s | `DESIGN_Defend_And_Attack_Position.md:660` |
| break contact → resume | 4.9 s → 38.6 s | tracker `:1627` |
| morale: fall back | Green 46.9 s · Veteran holds 241 s | `STATUS_Showcase_Brains.md:109` |
| attrition | 4 down by 60 s (`pc-defend-house`) | `DESIGN_Defend_And_Attack_Position.md:1755` |

## 7. Still unknown

Whether each squad play behaves with TWO members (beyond the halves: posts, coverage, the room drill) — ⭐ measurable cheaply by running the
drill rails on a two-man squad · whether a reaction failing at Start reports `TaskFailed` up · CE-6015 (the hierarchy does not reach SimHost
or the Player) · the producer gaps confirmed: no production sender of PROGRESS FallingBack / PhaseLine / ObjectiveSecure / FireLifted, LACE or
the ammo colour (the designs name senders: `DESIGN_Platoon_Attack_Defend_Clear.md:163, :199, :203`; FallingBack superseded by Set,
`DESIGN_Defend_And_Attack_Position.md:1334-1335`) — `CompanyBehaviors.cs:804` reads a report nobody sends.

## 8. ✅ DECIDED — ONE representative member per squad *(user, `2026-11-14`, R-4244)*

🔒 User, `2026-11-14`: *"record the decision to have just one member. In addition to the virtual commander, which we already have. And Keep in
mind that we can always replace any behavior with an aggregated squad version, because these should come as orders, and the orders will be
translated to whatever the unit can do. So we do not necessarily need to hack the two half behaviors into a single member behaviors, because that
will for sure come with some issues. And either let's design these behaviors to work even with a single member squad, which is likely a realistic
situation when the squad loses most of their men and only one man standing lasts, or to replace with specific aggregated squad behaviors."*

| | the decision |
|---|---|
| ⭐ **the shape** | an aggregate squad = the existing hidden squad UNIT (the virtual commander: command link, reports, orders) + **ONE representative member** that stands for all its men: location, sensors, weapons, damage, suppression — and the squad reports through the unit as today |
| ⭐ **no hacks into the half-based plays** | the §8-earlier idea (a one-member squad counts its member in every half, inside `SquadGates.Element` / `Centre`) is **REJECTED** |
| ⭐ **per behaviour, one of two** | **(a) ROBUST TO ONE MAN** — the play is fixed to work when one man is left, because a REAL squad reaches that state (measured: `FireAndMovementBehavior.cs:100-104` fails the moment a half is empty, so a real squad down to its last man fails bounding / fire and movement / break contact today — a realism defect in its own right) · **(b) AN AGGREGATE VERSION** — a behaviour of the aggregate squad's class, chosen by the order routing (CE-2142 / R-273: `Order.<name>` → `<Class><Order>`) where the aggregate meaning differs (e.g. clearing a building, A-6) |
| ⚠ **the prerequisite of (b)** | measured: platoons order squads by CONCRETE behaviour name (`CommandOrders.Assign`, `CommandNodes.cs:80-86`), not by `Order.<name>` — the class routing is not on that path yet (§1 W-3). ⇒ the platoons' squad orders move to `CommandOrders.Order` first |
| ⏳ **to decide in the design** | which plays take (a) and which (b) — a table over the squad order set (§1 W-4 / the order sweep) |
