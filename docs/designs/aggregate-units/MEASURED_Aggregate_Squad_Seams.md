<!--STATUS
state: LIVE — MEASUREMENTS, not a design (user, 2026-11-14: "No hurry in handoffs and design. Let's measure first. Let's make sure that we understand all the weak points.")
updated: 2026-11-14
current-answer: §1 the weak points, ranked · §2 the seams measured · §3 not measured yet
stale-below: nothing
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
