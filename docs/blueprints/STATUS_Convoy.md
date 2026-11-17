<!--STATUS
state: LIVE
updated: 2026-11-16
current-answer: §1 where we are (resume from its first open row) · §2 lane decisions · §3 gates
stale-below: nothing
related-designs:
  - ../designs/vehicle-tactics/DESIGN_Road_Convoy.md — THE design this lane builds (UML, decisions C-1…C-11, slices S-1…S-6)
  - batches/HANDOFF_Coordinator_To_Convoy.md — the dispatch (frozen at 74c53dd69)
-->

# STATUS — the `convoy` lane (CE-4631, R-4282)

Branch `convoy`, started from `origin/coordinator` at `ea4c01af8` (handoff dispatched at `74c53dd69`). Ids: CE-9000…CE-9999 (next free **CE-9006**),
R-9000…R-9999 (next free **R-9011**), components 108 / 109 / 157 / 158, descriptor ordinals 198–203.

## 1. Where we are — resume from the first open row

| step | state | what | evidence |
|---|---|---|---|
| 0 start | ✅ | branch, started marker | `chore: started convoy at ea4c01af8` |
| 1 MEASURE | ✅ | ①–⑧ + three more (road-use bits, mode 2's emission point, the upstream rule) | design §1 INVENTORY, each with file:line |
| 2 DESIGN | ✅ | `DESIGN_Road_Convoy.md` — proven solution, C-1…C-11, class / 2 sequences / module diagram (all parse); draft SUPERSEDED; links both ways (AQ93, Navigation v2, DA position, City Battle) | `mermaid-check` 4/4 |
| 3 S-1 priority lead (CE-9000) | ✅ | HROT: `RoadUse.InTrafficPriority` → handover `Priority` → the blue-light twin; the column's cap → `TrafficHandover.MaxSpeed` → `SetExternalVehicleMaxSpeed`. SumoSharp **H17 upstream** (`hrot/slice2` = `eb88338`, with T-7b upstreamed too) and synced (`--verify` OK). Traffic suite 90/90; upstream full suite 0 failures, bench hash unchanged. ⏳ the cap on a dedicated traffic node (C-8) | `852c0262e` |
| 4 S-2 column-follower (CE-9001) | ✅ | `ConvoyMember` 108 · `ConvoyTrail` 109 · `ConvoyProgress` 157 · `ConvoyLead/FollowSystem` · `KinematicsMode.Column` — `ConvoyColumnTests` 6/6 (gaps 11.8–17.1 m along the path, 1.1 m off the lead's path at a 90° corner, all stop 2.9 s after the lead, a slow tail holds the lead to 4.9 m/s, 0 B/frame); red-proved (gap term, lead cap) | `d79001c7c` |
| 5 S-3 no cut-in (CE-9002) | ✅ | `TrafficBodies.AddConvoyGaps` — rail `CE9002_…` red-proved; traffic neighbours 15/15 | `017815236` |
| 6 S-4 mode 2 lane (CE-9003) | ✅ (opt-in) | `RoadGraphRouter.KeepLane` for `RoadUse.StronglyPrefer` — `RoadGraphRouterLaneTests` red-proved; navigation neighbours 542/542. ⚠ global offset reddened 3 `CE-3128` rails ⇒ opt-in (design §6) | `4bfd1f57d` |
| 7 S-5 order + switch (CE-9004) | ✅ (editor) | `OrderKind.Convoy` · `ConvoyOrder` · `PlatformConvoy` · `ConvoyMembershipSystem` (CgfLogicPack) — `PlatformConvoyTests` 3/3, both rails red-proved; SimHost neighbours 120/120. ⏳ `ConvoyMember` on the wire (descriptor 198, C-8) — the cluster tier; `PlatoonConvoyBehavior` assigning `ConvoyOrder` | `edbfc877d` |
| 8 S-6 scenarios (CE-9005) | 🚧 1 of 6 | ✅ **`cv-offroad-join-leave` PASS 7/7** (editor tier, 310 s; the never-splits row proven non-vacuous: at threshold −1 it fails at 0.25 s). Its first runs found four defects, all fixed or filed: a backed-up lead knotted the column (trail forward-only + a metres guard), the lane offset on lane-line nets (R-9011), the column facing away from the road, `city-battle`'s hedges cutting its roads (CE-9006 → terrain). ⏳ `cv-red-lights`, `cv-no-cut-in`, `cv-blocked-road`, `cv-ambush`, `cv-mode-1-to-2` | `986c9639b` |

## 2. Lane decisions (for the user's review) — R-9000…R-9010

Design §3 C-1…C-11 (each with its rejected alternatives and what would change it); ledger rows in `RULINGS.md`.

## 3. Gates

| gate | result |
|---|---|
| `rulings-check.py` | 746/746 |
| `tracker-counts.py --check` | OK |
| ✅ H17 upstream | pushed (`eb88338` on `hrot/slice2`), synced, `sync-sumosharp.sh --verify` OK; upstream full suite 0 failures, bench `A134ED3716DDE7BC` unchanged |
| `design-digest.py --check` | fails identically at the base (pre-existing; none of this lane's files) |
