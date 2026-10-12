<!--STATUS
state: LIVE
updated: 2026-10-12
doc-type: HANDOFF — behaviors lane to the next behaviors session. Dispatched at a38e359d0 (branch `behaviors`).
current-answer: §1 where we stopped · §2 the work, in order · §3 the measured findings that seed AQ90 · §4 traps · §5 gates
stale-below: nothing
known-rot: none
known-conflict: none
supersedes: HANDOFF_Squad_Tactics_S3.md §1–§2 (its items 1–4 are BUILT). Its §3 traps and §4 rails still hold — read them.
related-designs:
  - docs/DESIGN_Entity_Interactions.md — OWNS embarkation: §7.1 plan (I-2d), §7.1b/§7.1d/§7.2b as built
  - docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md — OWNS the squad runner, the drills, the unit commander (§3.1 "order = AssignBehavior to the commander", §5, §6.1, §7)
  - docs/DESIGN_Entity_Control.md — OWNS the one order service (EntityOrders) for HTTP, menu and RTS (R-270); soldier-only today
  - docs/DESIGN_Operations_Behaviour_Catalogue.md — OWNS the convoy ambush slice (P1, D8), the injury states (D1 / R-263) and the MED / IND-5 casualty entries
  - docs/OVERVIEW_Behaviour_Building_Blocks.md — the map (§9 groups, §10 vehicles); extend it when you measure
-->

# Handoff — embarkation I-2d, and the three questions for `AQ90`

⭐ **This file points at designs; it is not one.** Draw no UML here. Extend the owning design instead.

## 1. Where we stopped *(`behaviors` @ `a38e359d0`, 2026-10-12)*

| commit | what | recorded in |
|---|---|---|
| `5a57ccb0b` | `CE-3110` I-2a: one `Embarkation` rules class and one applier, `EmbarkationSystem`, on the vehicle's owner | Entity Interactions §7.1b |
| `7c1aec226` | I-2b: `PassengerBuffer` replicates (`dtPassengerBuffer` = 131, Brain group); `EmbarkedStateSystem` derives the passenger side on every node | §7.1d |
| `6c605dc13` · `ac7121e21` | ⭐ The owner-side interaction systems are **cross-role infrastructure**: `CoreInfrastructureCapabilities.Interactions` (`cap:infra:interactions`), declared in all six host plans: CGF, SimHost, IG, both editor plans, Stride. It holds `EmbarkedStateSystem` (Input), `DoorCommandSystem` and `EmbarkationSystem` (Simulation). 🔒 User: *"any node that can own live entities … should be able use the features, so I hope you do no per-host initializations"* | §7.1d · Subsystem Composition §4.1ad |
| `85eb31962` | I-2c (R-271): a passenger is CARRIED (the position owner copies the vehicle's transform), HIDDEN in an enclosed vehicle (`PhysicalPresence.IsHidden`, honoured by the perception grid, the EQS area search, the collider hash, the blast and the line-of-sight occluders), and DIES with a destroyed vehicle (a lethal `DamageAssessedEvent` from the node that owns the passenger's Health). An open cabin is the TKB `PassengerExposure = Open` → `OpenPassengerCabin` (component 366) | §7.2a / §7.2b |
| `a38e359d0` | A body-less unit (`UnitEntity`) never boards and carries nobody. `TryEmbark` returns `Invalid` either way. 🔒 User: "sounds good" | §7.2b |

Gates at `85eb31962`:
- FDP Perception + CarKinem + Combat + Behavior: 947/947
- SimHost: 70 passed, 1 skipped (unrelated, 2026-07)
- IG full: 463 passed, 1 skipped (unrelated)
- Editor: 11/11
- ClusterRunner embark rails: 3/3
- SimHost.Integration and Stride: compile
- Every I-2c rail was red-proved.

Pre-existing red: `UrbanCombatFileLifecycleTests` live mode. Proven at `c5281a5bb`; recorded on `CE-2126`.

## 2. The work, in order *(lean)*

| # | item | basis | first move |
|---|---|---|---|
| **0** | ⭐⭐ **Write `Architect_Question_90`** (next free number across ALL branches at dispatch; re-check with `git fetch` first, per rule 3a). Three sub-questions, each with a lean, a blast radius and what would change the lean. Seed it from §3. ⛔ **Do not build A/B/C until the user approves.** Ask the relay before committing the question doc, if you use it (CLAUDE.md, the NotebookLM rules) | 🔒 the user's three questions, `2026-10-12` (quoted in §3) | `INVENTORY` block first (`search_graph` on `TacticalMapper`, `EntityOrders`, `PassengerBuffer`, `UnitEntity`) |
| **1** | **I-2d** — library nodes `Embark(vehicle)` / `Disembark` (they raise the commands); a commander-level `Dismount` drill (a play on `SquadManeuverRunner`) | Entity Interactions §7.1 I-2d · Squad Tactics §5 · catalogue §11 S3 | ⭐ Not blocked by `AQ90`: it uses today's fixed 8 slots. If `AQ90`-B is approved first, the convoy vehicle type declares its capacity |
| **2** | **Convoy-ambush scenario**: the acceptance for S3 | catalogue P1, D8 | Lay it out on a walkable spot (S3 handoff, trap ③); copy `tt-react-contact` |
| **3** | Build `AQ90` A / B / C as approved | `AQ90` | Each one: UML in the owning design → build → red-prove → fold the as-built |

Fences (🔒 user):
- *"No handoffs to backend"*: the behaviors lane builds all of it; name any cross-lane edit in its commit.
- ⛔ **No per-host initialisation.** A new owner-side interaction system goes into `CoreInfrastructureCapabilities.Interactions`, never into a host or a role pack. The editor and the hand-wired harnesses read its `Systems` list.
- Stride: its plan declares the capability like every other host, but Stride is not exercised here.

Open offer (no answer from the user yet): move `HealthApplicationSystem` (the one Health writer, registered only in `CgfLogicPack`) into the cross-role set. That would let a passenger whose Health is owned on a non-CGF node die there too.

Ids:
- Tracker ids come from the behaviors block `CE-2000`–`CE-2999`. The last one used is `CE-2126`, so the next is `CE-2127` (verify).
- Embarkation stays on `CE-3110`.
- Component ids: check `GlobalComponentIds` before taking one (366 is the last one taken here).

## 3. Measured findings that seed `AQ90` *(two read-only sweeps, 2026-10-12; spot-checked where marked ✅)*

### A — an order to a `UnitEntity` (squad or higher echelon) is handled at the unit's level

> 🔒 *"i need to be able to give an order to unitEntity (representing the squad in our case, but could a be a higher level unit as well) and expect the Unitentity will somehow handle the command on its own level (will send orders to its members or something), will that work?"*

| fact | where |
|---|---|
| Every order path ends in `TacticalIntentResolutionSystem`. It tries a mapper by intent id (`TryMap(entity, …)` receives the target), then falls back to *intent id = behaviour name*. There is no `UnitEntity` check | ✅ `Hrot/Subsystems/Hrot.CGF/Systems/TacticalIntentResolutionSystem.cs:123-148` |
| A squad drill ordered on the unit WORKS. `SquadMarch`, `BoundingOverwatch`, `SuppressAndManeuver`, `SquadHold`, `BreakContact` and `ReactToContact` run on the unit, and `SquadManeuverRunner.Issue` sends each live member `AdvanceTo` / `CombatPosture` at Superior origin | `SquadMarchBehavior.cs:59-65`, `SquadManeuverRunner` `:362-406` |
| The menu Move / Engage / Stop, the RTS tool and HTTP `move` / `attack` / `hold` go through `EntityOrders` → `MoveToLocation` / `CombatPosture` / `Idle` on the unit itself: a soldier behaviour on a body-less entity. A move never arrives (no `NavigationStatus`), and the members get nothing | `EntityOrders.cs:70-136`; `MenuOrders.cs:43-96`; `RtsCommands.cs:55-72`; `MoveToExecutor.cs:97-98` |
| Stop does not recall the members: releasing a march does nothing | ✅ `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/SquadMarchBehavior.cs:233` |
| An operator order to a member outranks the squad's Superior orders until it is released | `BehaviorOrigin.cs:56-84` |
| No order is passed down two levels: no platoon-of-squads composite, the runner issues only soldier behaviours, and `CommanderNodes.Action_IssueTacticalIntent` is a stub | `BdcTkbCatalog.cs:230-235`; `CommanderNodes.cs:66-90` |
| The data allows nesting: `UnitRoster` holds any entity (16); `UnitHierarchySystem` does no type check | `UnitRoster.cs:84-91`; `UnitHierarchySystem.cs:99-160` |
| No design covers translating an operator order for a unit, or passing one down echelons. Searched Entity Control, Squad Tactics, Squad Coordination / Wiring, AQ29, AQ83, the hill-attack docs | — |

**Lean A:** one order vocabulary at every echelon, translated by the receiver's kind. Add unit-aware mappers:
- Move → `SquadMarch`
- Stop → `SquadHold`
- Engage → the squad attack drill

A platoon drill then issues Move / Engage to its member units through the same mappers, so the order recurses down. Also:
- releasing a drill holds the members;
- `SquadMarch` takes a geo objective;
- a unit gets a unit menu, with no Possess.

What would change it: making the unit's SOP slot (R-189) the decomposer.

### B — carrying is a vehicle-TYPE fact, and the driver is a carried entity

> 🔒 *"of course the ability to carry passengers (and of course the driver as well - driver is just a special type of an entity to be carried) should be tied to the vehicle type"*

| fact | where |
|---|---|
| Capacity is `PassengerBuffer.Capacity = 8` on every carrier. No TKB field declares capacity or seats; the only per-type carry fact is `PassengerExposure` | `InteractionComponents.cs:16`; `VehicleParametersDto.cs:89` |
| Every brained entity, soldiers included, gets an empty `PassengerBuffer`. ⇒ a soldier can be named as a "vehicle" today | `BehaviorTkbTranslator.cs:161-162`; `Embarkation.cs` `TryEmbark` |
| There are no crew, driver, gunner or seat types. The vehicle's own brain drives; `ManualDrive` is written onto the vehicle; the "gunner" is code (`AimAndFireExecutor`) | `BdcTkbCatalog.cs:70-180`; `DESIGN_Entity_Control.md` M-1/M-3; `DESIGN_Weapon_Layers.md:52` |
| Precedent for a per-type carry fact: `VehicleKinematicsTkbTranslator` stamps `OpenPassengerCabin` | `CarKinem/Tkb/VehicleKinematicsTkbTranslator.cs:81-82` |
| The design already rejected per-seat exposure *"until seats exist"* and left `PassengersMayFire` as a lean | Entity Interactions §7.2 (`:439`, `:441`) |

**Lean B:**
- The type declares the carry capacity and seats (with roles: driver, gunner, passenger).
- Only a declaring type gets a `PassengerBuffer`, which closes the soldier-as-vehicle gap.
- Crew are seated passengers with a role; per-seat exposure comes later.

### C — a soldier carrying or dragging a wounded comrade: is it "embark"?

> 🔒 *"a medic or fellow soldier takes a wounded colleague on his back to carry him to safety … it is wort thinking if this case is not what the 'embark' concept already covers (a special case could be that a wounded soldier is dragged by a medic)"*

| Embark already gives | Wrong for a human carrier |
|---|---|
| carried with the carrier · CanMove / CanShoot stripped · one arbiter + replication | dies with a destroyed vehicle · hidden when enclosed · no attach offset · the carrier is not slowed |

| fact | where |
|---|---|
| There is no wounded / incapacitated state: `Health {Current, Max}`. Injury states were decided (D1 / R-263) but not built | `Combat/Components/Health.cs:18-21`; catalogue `:224,281,298` |
| `Treat` / `Drag` / `Carry`, the casualty sensor, `FindCasualtyCollectionPoint`, MED-1..3 and IND-5 are catalogue entries only | catalogue `:102,146-152,179,196,216` |

**Lean C:**
- Carrying a person = embark with a carrier profile declared per type: capacity 1, exposed, does not die with the carrier, a speed penalty, an attach offset.
- A drag is the same profile with another offset and a bigger penalty.
- One mechanism, not two.
- Depends on the R-263 injury states (build them first) and composes with B: a profile is what a type declares.

## 4. Traps paid for in this run *(add these to the S3 handoff's ①–⑪)*

| ⑫ | ⛔ **Per-host registration was rejected by the user.** It is the cross-role capability or nothing. Pack counts in `CgfLogicPackTests` (input 5, sim 20, total 25) and the sequence tails in `EditorCapabilitiesTests` move when the capability changes; update them deliberately |
| ⑬ | **IG ran passenger ingress without registering `PassengerBuffer`.** Shared interaction components belong in `HrotSharedComponentRegistry` (every host), not only in `EmbarkationComponentRegistry` |
| ⑭ | `HasComponent<T>` of a type the world never registered is **false**, not a throw. So a hidden check needs no registration guard, but a rail must register what it asserts on |
| ⑮ | The hit filter for a hidden passenger lives in the **collider hash** (`SpatialHashSystem` → `RaycastSolverSystem`), not in `HitResolutionSystem` |
| ⑯ | Recorded and not covered: Stride `CrowdAgentUpdateSystem` does not skip an embarked body · enemy `TargetMemory` of a passenger decays rather than purges · `HiddenTargetThrow.FriendlyInBurst` counts a friend inside a vehicle |

## 5. Gates and rails

Run the S3 handoff's §4 gates, plus these feature rails (T-1, run first):

| rail | project |
|---|---|
| `EmbarkationTests` · `EmbarkedStateSystemTests` · `EmbarkExecutorTests` | `FDP/Toolkits/Fdp.Toolkits.Tests` |
| `LocalGridBuilderSystemTests` · `SpatialHashSystemTests` · `AreaEffectSystemTests` · `CarKinematicsSystemTests` (the I-2c rails) | `FDP/Toolkits/Fdp.Toolkits.Tests` |
| `CgfLogicPackTests` · `UnitCommanderTests` · `SquadManeuverTests` · `PassengerReplicationTests` | `Hrot/Subsystems/Hrot.SimHost.Tests` |
| `EditorCapabilitiesTests.CE3110_*` | `Hrot/Subsystems/Hrot.Editor.Tests` |
| `HrotSharedComponentRegistryTests` | `Hrot/Engine/Hrot.Core.Tests` |
| the ClusterRunner embark rails (live, T3, in the background) | `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests` |
