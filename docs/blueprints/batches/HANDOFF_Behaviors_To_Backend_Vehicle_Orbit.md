<!--STATUS
state: LIVE
updated: 2026-10-16
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/motion-models/DESIGN_Motion_Domains.md — OWNS the ground domain's motion models and the follower (where the fix lives)
  - docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md — §4: the cycle's short hide ↔ firing-point drives (where it was measured)
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Vehicle_Overshoot.md — CE-2198, the overshoot (the same short-move family)
-->

# HANDOFF — behaviors → backend: a hull ORBITS a point inside its turning circle (CE-2206)

**Dispatched at the commit that adds this file** (behaviors). Your scope is frozen at that sha; nothing here stops an item.

## The measurement (live `ua-javelin-lock`, test-town, `2026-10-16`)

An M1 (100) on `LandPlatformAttack` started at (251, 110). Its first hide, a cover point, was **(251.25, 107.67)**, 2.3 m away; the move
was issued at 5 m/s with `ArrivalRadius` 2. For 60 s the hull drove a circle of ~5 m radius round (251.5, 105.5):

(251, 110) → (254, 109) → (256, 105) → (254, 102) → (251, 101) → (247, 107) → (251, 110) → … The point lies INSIDE that turning circle,
so the follower never passed within 2 m of it, and the move never ended.

## What we ask (your design)

⭐ **A move to a point inside the hull's turning circle ends.** Either the follower reverses / shunts onto it, or it reports Success
when it is as close as its turning circle lets it get (and never runs for ever). A rail: an M1 sent 2–3 m to its side arrives.

## What behaviors did meanwhile (no dependency on you)

**CE-2205**: `VehicleFightFromCover` now owns one arrival test, `MoveStatus`. A hull within 4 m (was 2) of its point HAS arrived; its move is
stopped, whatever the follower says. So the cycle no longer hangs on this, but any other caller of a short vehicle move still can. The rail is
`VehicleFightFromCoverNodesTests.CE2205_*`.
