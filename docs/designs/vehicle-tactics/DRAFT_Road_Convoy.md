<!--STATUS
state: SUPERSEDED
superseded-by: DESIGN_Road_Convoy.md (2026-11-16, the convoy lane — every row of this draft is carried there, measured)
updated: 2026-11-16
current-answer: the whole file
stale-below: nothing
related-designs:
  - ../../blueprints/Architect_Question_93_Sumo_Traffic_Node.md — OWNS the SUMO traffic node: the handover (`TrafficHandover`, a shadow `hrot:<id>`), our bodies fed to SUMO (`TrafficBodies`), SUMO cars near us as replicas (`TrafficReplicas`)
  - ../navig-2/Navigation_Design_v2_0.md — OWNS the ground route planner (§5.2a: navmesh → road → navmesh, `RoutePlanner`, `RoadGraphRouter`) — mode 2 here
  - ../group-maneuvers/DESIGN_Defend_And_Attack_Position.md — OWNS the platoon's mounted convoy (§11a, `PlatoonConvoyBehavior`, CE-2347) this draft extends onto roads in traffic
  - ../../reference/NED_DDS_Protocol.md — the SUMO traffic protocol section (how SUMO and our entities meet on the wire)
-->

# DRAFT — the road convoy in SUMO traffic (CE-4631)

🔒 User, `2026-11-16`, verbatim: *"vehicle convoys, especially military ones. They should be part of the sumo traffic until they should act on something in the
scenario, like being under attack or switching from just transportation into some battle action."* · *"Convoy never split, ignores traffic lights, and the
sumo vehicles needs to react to it."* · *"I meant the leading sumo vehicle to be the one with priority, with beacon or siren. So no traffic lights apply …
that would fix the navigation part and route creation part and route clearing part as well as waiting if the route is blocked, because Sumo handles all
of that already … The route can stay as a second valid option because it works outside of Sumo, offroad, so it is worth supporting it."*

⚠ **A draft.** Before it becomes a design: an INVENTORY (graph + grep), the UML (class, sequence, module), the decisions with leans, the user's rulings,
the fixed ids. The measurements below were made `2026-11-16` (code + grep; ⚠ not yet with the code graph).

## 1. The rules (the user's)

| rule | |
|---|---|
| the convoy NEVER splits | one route for the whole column; followers never overtake; the column stops and starts together |
| it IGNORES traffic lights | the lead runs reds; followers follow the lead, never look at a light |
| SUMO's vehicles REACT to it | they queue behind it, stop for it at junctions, never cut into it |
| it acts when the scenario needs it | under attack, or switched to a battle task, the convoy's own plays take over at once |

## 2. Two modes, one column-follower

| | ⭐ MODE 1 — on the road network: a SUMO-driven priority lead | MODE 2 — off the network: our own route |
|---|---|---|
| the lead | OUR escort vehicle (beacon / siren), HANDED to SUMO (`TrafficHandover`: a shadow `hrot:<id>`, our entity follows it, ownership never moves) with a PRIORITY vehicle type | our vehicle on the route planner's route |
| route, lane, junctions, waiting behind a blockage | ✅ SUMO's: routed to the destination (`SetDestination` / `Reroute`), its own lane, its car-following | the route planner: off-road start → nearest point ON the network → the road graph (the SUMO net) → exit → the target (`RoutePlanner`, CE-3128); ⛔ it drives the centre line today (no lane offset) |
| red lights | ✅ the priority type ignores red (`jmDriveAfterRedTime`, `Engine.cs:7329-7346`) and may force junctions (`JmIgnoreFoe*`, `VehicleParamOverride.cs:34-41`) | not looked at |
| when | the whole leg on the network | off-road legs, joining and leaving the network, a terrain with no SUMO net |
| the followers | ⭐ **the same in both modes**: OUR vehicles driving the LEAD'S ACTUAL PATH (its recorded trail) at a gap measured ALONG the path; never overtaking; stop together, close up to a minimum gap; the lead slows when the column stretches | |

## 3. SUMO's vehicles and the convoy

| | |
|---|---|
| they see our followers | ✅ each of our bodies is an obstacle on every lane it overlaps (`TrafficBodies`, AQ93 §13) — cars behind queue, crossing cars stop at a junction we occupy |
| no cutting in | ⭐ NEW: the column is reported as ONE continuous obstacle along its lane from the first vehicle to the last, so no car slips into a gap |
| we see SUMO's cars | ✅ a SUMO car near any of our bodies is a real entity (`TrafficReplicas`) — sensors, raycasts, blasts see it; ⚠ not measured whether our vehicles' AVOIDANCE brakes for it (mode 2's lead needs it) |
| making way for the priority lead | ⛔ not in SumoSharp today: no blue-light / rescue-lane behaviour found; `KraussModel.cs:434` says "No lane-change model exists in phase 1" (⚠ other lane-change code exists — to measure). ⭐ Lean, later: cars queued AHEAD of a priority vehicle at a red may enter the junction too (a small rule in our own SumoSharp) — more convincing than lane changes; until then the escort waits behind them, as real escorts sometimes do |

## 4. Switching to action

The vehicles are OURS throughout. When the convoy is attacked or ordered into a battle task, its orders change ⇒ the lead's shadow is removed from SUMO
(the handover already does this when the intent changes) and our own driving takes over; the convoy-under-attack plays (push through, dismount and fight —
`uc-convoy-hit`, `PlatoonConvoyBehavior`) run as today. ⛔ No vehicle is ever re-created; nothing is handed back from SUMO but the lead's driving.

## 5. What is new

| # | piece |
|---|---|
| 1 | the handover takes a VEHICLE TYPE for the shadow (the escort's priority type); today it spawns a default one |
| 2 | ⭐ the COLUMN-FOLLOWER along the lead's path — the main new piece, shared by both modes (check whether the existing `FormationFollower` extends to it) |
| 3 | the column as ONE continuous obstacle to SUMO |
| 4 | mode 2: a lane offset when the route's points are emitted (the outer lane on the driving side; the road graph has lane counts and widths) — and the driving side of the net (⚠ to measure) |
| 5 | later: cars ahead of a priority vehicle at a red clear into the junction |
| 6 | feature scenarios (R-4277): a convoy crosses a town through red lights without splitting; cars queue behind it and never cut in; an ambush mid-route switches it to the convoy-hit play; mode 2 joins and leaves the network off-road |

## 6. To measure before the design

- whether our vehicles' avoidance brakes for SUMO car replicas (mode 2's lead)
- whether SumoSharp can command a lane change (for "making way")
- the net's driving side (left- or right-hand traffic)
- how far `FormationFollower` (CarKinem) gets toward a path-following column
