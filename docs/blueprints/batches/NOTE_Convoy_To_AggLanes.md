<!--STATUS
state: LIVE
updated: 2026-11-17
current-answer: the whole note (short)
stale-below: nothing
related-designs:
  - ../../designs/vehicle-tactics/DESIGN_Road_Convoy.md — the convoy lane's design (a physical road column)
  - ../../designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md (on exp/agg-*) — §14 M-3 / M-4 the stochastic unit's column, §16a the split
-->

# NOTE — convoy lane → agg-backend / agg-behaviors: two "columns", one seam

Written at `convoy` while folding the convoy into `Column` formations (R-9012). The user asked the convoy lane to check the agg lanes'
column movement (`2026-11-17`). Measured on `origin/exp/agg-behaviors` and `origin/exp/agg-backend` (both based on `ea4c01af8`, like `convoy`).

## They are not duplicates

| | agg: `UnitFormation.Column` (M-3 / M-4) | convoy: a `Column` formation (R-9012) |
|---|---|---|
| what moves | ONE point: a stochastic unit, no bodies | N physical vehicles, each its own body |
| the shape | the head on `UnitRoute`, the tail `AggregateUnit.Depth` behind (`UnitMovement.cs:139-150`) | each follower on the lead's driven trail at rank × gap (`ConvoySystems.cs`) |
| driver | its own deterministic path follower, ⛔ not the body kinematics (M-4) | `CarKinematicsSystem` (`KinematicsMode.Column`) |
| ids | components 285–288, 119, 138; descriptors 220–239 | components 109, 157; descriptors 198 / 199 — no collision |

Both plan with the one `RoutePlanner` (agg M-1; convoy mode 2 through `MoveTo`). So the planner is shared, and each column is a different
resolution of the same concept. Neither replaces the other (ruling 9 is about implementations, not resolutions).

## The seam — a column unit that SPLITS

📐 `UnitSplits.FallbackPosts` (`UnitResolution.cs:83-92`, agg-backend) places every live slot in rows **across the frontage, square to the
heading**. A mounted unit in `Column` on a road therefore turns into a line of vehicles across the road, off it on both sides.

⭐ **Lean** (agg-backend's call, X-4):
- a unit in `Column` places its vehicles ALONG `UnitRoute`: `PointAt(head − k · spacing)`, heading = the segment's direction;
- its re-resolved order (X-2) gives each vehicle a `ConvoyOrder` (`OrderKind.Convoy = 15`, rank = k, lead = slot 0, `Destination` = the
  route's end), so the column keeps driving as one;
- merging back: the unit's head = the lead, `Depth` = the trail's head-to-tail arc.

Nothing in the convoy lane blocks this. `ConvoyOrder` / `PlatformConvoy` are built (CE-9004). Not built here: a cross-lane edit is
agg-backend's to make, so the convoy lane leaves it to them.
