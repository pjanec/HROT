<!--STATUS
state: LIVE
updated: 2026-11-17
current-answer: the whole note (a frame: goal, measured facts, lean, open question, acceptance)
stale-below: nothing
related-designs:
  - ../Architect_Question_93_Sumo_Traffic_Node.md — §14 the hand-over (TrafficHandover, the shadow hrot:<id>): this frame extends it
  - ../../designs/navig-2/Navigation_Design_v2_0.md — the executor that plans the off-network leg
  - ../../designs/vehicle-tactics/DESIGN_Road_Convoy.md — §6 CE-9020 (the convoy needs nothing of its own)
-->

# FRAME — convoy → the SUMO hand-over's owner: an `InTraffic` move whose destination is OFF the network (CE-9020, "mode 1 → 2")

From the convoy lane, `2026-11-17`. Scenario `scenarios/demos/convoy/cv-mode-1-to-2` is committed and failing on exactly this gap.

## Goal

A vehicle asked to move `RoadUse.InTraffic` (or `InTrafficPriority`) to a destination off the SUMO network should:

1. be driven by SUMO to the network point nearest that destination;
2. then drive the rest of the way on our own route.

This is generic: every `InTraffic` move, a convoy lead included. The convoy needs nothing of its own.

## Measured (traced, `cv-mode-1-to-2`, demo-city; destination 70 m east of the avenue)

| fact | where |
|---|---|
| SUMO **refuses** the hand-over. SumoSharp H13 routes "from the vehicle's lane to the destination's lane", and no lane matches a destination 70 m off the net. `TrafficSession` records it in `_refused` and retries silently | `TrafficSession.ApplyHandovers` |
| The executor treats "no shadow yet" as a **deliberate hold**, never a failure (`NavState.IsBlocked`). The vehicle stands at its start for the whole run, with intent `Traffic`, channel `Running`, `InProgress` | `TrafficShadowFollowSystem` (the `f.Seen == 0` branch) |
| The SUMO net is loaded on every node that composes the traffic pack (SimHost, CGF and IG since slice 2). So the traffic node (hand-over) and the kinematic owner (shadow follow) can compute the same "nearest network point" | `TrafficPack.cs:59`, `TrafficNetCache` |

## Lean

- **① At the hand-over** (`TrafficHandoverSystem.Gather`, traffic node): when no drivable lane lies within a snap radius of the destination, send SUMO the **nearest lane point** instead. SumoSharp H12's `LaneSpatialIndex` already answers "lanes within a radius".
- **② At the shadow's end** (`TrafficShadowFollowSystem`, kinematic owner): a shadow that vanishes near that same point while `FinalDestination` is farther away is a **partial arrival**, not "lost". The body then drives the remaining leg itself.
- **③ The cheap guard, wanted regardless:** a hand-over refused for N s ends the hold. The executor then plans our own route, instead of standing for ever.

## Open question — yours to decide

How does the executor plan the off-network leg in ②?

- a `KinematicsMode.Direct` straight drive: cheap, but blind to buildings;
- a fresh plan with the intent's road use downgraded (`Prefer`), through the usual planner (`Hybrid`).

⭐ Lean: the planner. The leg can be any length, and the planner is the one route planner for every mover. This needs a way to say "the traffic part is done" to the intent → nav bridge.

## Acceptance

`scripts/scenario-regression.sh cv-mode-1-to-2` passes, in both tiers:

- the lead is handed to SUMO, then leaves it;
- it reaches (215, 1240);
- the column never splits;
- the tail ends near the destination.

Then `cv-red-lights`, `cv-no-cut-in` and `cv-blocked-road` must stay green.
