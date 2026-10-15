<!--STATUS
state: LIVE — a STEER from ui (2026-10-15); it ADDS to the hit-location work (R-3010) and to the airborne handoff (CE-1117); it withdraws nothing.
updated: 2026-10-15
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md — backend's design these notes are about (§9 S2's "dead edge" line; IsPerson)
  - docs/designs/airborne/DESIGN_Airborne_Insertion.md — §9 (hit by part during a descent, D-10…D-12), D-1 (the Descent component)
  - docs/blueprints/batches/HANDOFF_Ui_To_Backend_Airborne_Insertion.md — the airborne handoff (CE-1117) this widens by one rail
-->

# STEER — backend: two notes from ui on hit-by-part (R-3010) and the airborne work (CE-1117)

**For:** `backend`. **From:** `ui`. The user relays it (*"if needed we can send them a steering note"*). Nothing here stops an item.

## 1. The "dead edge stays dead" line is stale — the SP gun is drawn from the replicated pose now

[Body Parts §9](../../designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md) (S2's sequence, the caption) says *"a `ui` SpArtillery is DRAWN
at a laid elevation (`LaidPose`), the sim poses its gun by `TurretPose` — the two agree only once the howitzer's gun publishes its lay"*.
📐 **ui's CE-1114 (`89b8f7159`, on `ui`)** made `LaidPose` use the replicated `TurretPose` as published — azimuth AND elevation, a level gun
included — and retired the round-based guess; your P2 (`b6ad14c83`) lays the gun before it fires. ⇒ **the drawing and the sim now pose the gun
from the same pose.** ⭐ Please fold that into §9 (the caption) and, if it is cheap, add the SP gun at a laid elevation to
`VehicleBodyParityTests`' turret poses.

## 2. Keep `IsPerson` on `VehicleParams` — a descending man must stay a person target

`PhysicsColliderReaders.IsPerson` keys on `VehicleParams.Class == Pedestrian` (`PhysicsComponents.cs:63-68`). The airborne design's D-1
([Airborne §9](../../designs/airborne/DESIGN_Airborne_Insertion.md)) makes `Locomotions.Of` answer **Parachute / Rope** while a `Descent` is
present. ⛔ **If `IsPerson` is ever rewritten over `Locomotions.Of`, a paratrooper becomes untouchable by rounds** (no person, and no vehicle
height either). ⭐ Add to CE-1117 B-1's acceptance: **★ a man with a `Descent` is still `IsPerson`, and a round through his torso under a canopy
hits Torso** (the `RaycastSolverSystemTests` suite).

Also agreed in Airborne §9 (no action unless you disagree): a descending man collides in his STANCE pose (HL-9 — upright, as he hangs); the
canopy and the rope are not parts (D-10); the ejection seat is not an entity (D-11); a man hit on the rope keeps sliding to the bottom (D-12).
