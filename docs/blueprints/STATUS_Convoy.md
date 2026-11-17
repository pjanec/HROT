<!--STATUS
state: LIVE
updated: 2026-11-17
current-answer: §1 where we are (resume from its first open row) · §2 lane decisions · §3 gates
stale-below: nothing
related-designs:
  - ../designs/vehicle-tactics/DESIGN_Road_Convoy.md — THE design this lane builds (UML, decisions C-1…C-11, slices S-1…S-6)
  - batches/HANDOFF_Coordinator_To_Convoy.md — the dispatch (frozen at 74c53dd69)
-->

# STATUS — the `convoy` lane (CE-4631, R-4282)

Branch `convoy`, started from `origin/coordinator` at `ea4c01af8` (handoff dispatched at `74c53dd69`). Ids: CE-9000…CE-9999 (next free **CE-9022**),
R-9000…R-9999 (next free **R-9014**), components 108 / 109 / 157 / 158, descriptor ordinals 198–203.

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
| 7b folded into formations (R-9012) | 🚧 | `ConvoyMember` withdrawn: lead = `FormationController{Column, Spacing}` (min gap = spacing/2: no new field — its bytes are in 75 scenario goldens), follower = `FormationFollower{SlotIndex = rank, Leader = lead}` (`Leader` new: `Entity.Null` = commander as before). Road columns are selected by the explicit `Leader`, so commander-led `Column` formations keep `FormationTargetSystem`'s slots. Commander-subordinates §2.2 amended. Gates: Toolkits convoy/formation/kinematics 182/182 (the legacy commander-led `Column` rails `FormationTargetSystemTests` caught the first, too-wide predicate), Traffic 90/90, SimHost 60/60; the sweep's two new rows red-proved. Agg lanes checked: their column is a stochastic unit's single point (no duplicate) — split seam in `NOTE_Convoy_To_AggLanes.md` | this commit |
| 7c on the wire (CE-9007) | ✅ | `dtFormationController` 198 · `dtFormationFollower` 199, Brain group, every node; explicit-leader slots only (+ one left sample). `FormationWireTests` 4/4 (red-proved: left removes only the convoy's slot), NED 198/198, SimHost wire/replication 217/217, protocol checks OK | this commit |
| 7d shadow top speed (CE-9008, R-9013) | ✅ | handover `MaxSpeed` = the intent's asked speed, lowered on the traffic node for a stretched column's lead (chords, one `CapFactor`). Rail `CE9008_…` red-proved ×2; Traffic 91/91. ⚠ deviates from the chat plan (brain lowers its intent): a cross-entity read in the parallel brain phase — design §6 | this commit |
| 7e merged `coordinator` (the RT-5 cluster tier) | ✅ | `06c55f7ce`: conflicts were both-lanes appends (198/199 vs air 216–218 …), unions. Ordinals 198/199 measured free across every `DescriptorOrdinal` source (`NOTE_Coordinator_To_Convoy_Ordinals.md` asked for it — **taken: 198, 199; 200 left free**). ⚠ PRE-EXISTING at `origin/coordinator`: 7 descriptor-map rails + `protocol-doc-check` red on `dtMineClearing` 195 / `dtRamOrder` 196 (6ee04899a, "rails not yet verified") — the checkpoint lane's | `06c55f7ce` |
| 7f the scenarios judge in both tiers | 🚧 | the `between` cue (CE-9009, + `far`/`close` now hold in the cluster tier); lane rows as a band between two position marks; never-splits as `far` per pair. Found and fixed: a lead asked into traffic where none runs stood still for ever (CE-9010 → our route); the imported road graph kinks 12 m at every junction (CE-9011 → terrain). ⛔ My miss: `CgfLogicPackTests` had been red since S-5 (`ConvoyMembershipSystem` uncounted; my gates never ran that class) — fixed to the measured 43, of which +3 were already uncounted on `coordinator` | |
| 7h cluster tier after CE-9014…9018 (`2026-11-17`) | ✅ 4 of 4 (offroad, red-lights, no-cut-in ×2, blocked-road) | `cv-offroad-join-leave`, `cv-red-lights` PASS; `cv-no-cut-in` — its anti-vacuity row now asks a SUMO car within 12 m of ANY convoy vehicle (it asked 10 m of Escort 3; a real-time run had none) ⇒ editor + cluster ×2 PASS; `cv-blocked-road` — ✅ FIXED (CE-9021): traced on `--mode all`, the scripted `do` that clears the truck was REFUSED on the Scenario node (the truck's pose is SimHost's); the verifier now routes it to the owner ⇒ cluster 10/10 PASS | |
| 7g platoon as one column (CE-9012) | ⛔ on hold behind CE-9013 (→ behaviors: a unit memory grows the occurrence store inside the parallel brain phase → exit 134 in `uc-urban-battle`); reverted, `uc-urban-battle` PASS 20/20 again; kept `ConvoyOrder.Route` + one `WriteFollowRoute` | |
| 8 S-6 scenarios (CE-9005) | ✅ **6 of 6 PASS in BOTH tiers** (`2026-11-17`, editor + cluster): offroad, red-lights, no-cut-in, ambush, blocked-road and ✅ `cv-mode-1-to-2` (CE-9020 built in the AQ93 hand-over — SUMO drives to the road point on the carriageway the lead approaches on, our planner drives the rest; the scenario moved inside demo-city's world; CE-9022 the arrived column closes up). Found + fixed on the way: CE-9014/9015/9017/9018/9016/9021/9022; open: CE-9019 (a waiting SUMO shadow is lost) | ✅ **`cv-offroad-join-leave` PASS 7/7** (editor tier, 310 s; the never-splits row proven non-vacuous: at threshold −1 it fails at 0.25 s). Its first runs found four defects, all fixed or filed: a backed-up lead knotted the column (trail forward-only + a metres guard), the lane offset on lane-line nets (R-9011), the column facing away from the road, `city-battle`'s hedges cutting its roads (CE-9006 → terrain). 🚧 `cv-red-lights`, `cv-no-cut-in` authored (demo-city, SUMO traffic); ⏳ `cv-blocked-road`, `cv-ambush`, `cv-mode-1-to-2` | `986c9639b` |

## 2. Lane decisions (for the user's review) — R-9000…R-9010

Design §3 C-1…C-11 (each with its rejected alternatives and what would change it); ledger rows in `RULINGS.md`.

## 3. Gates

| gate | result |
|---|---|
| `rulings-check.py` | 749/749 |
| `tracker-counts.py --check` | OK |
| ✅ H17 upstream | pushed (`eb88338` on `hrot/slice2`), synced, `sync-sumosharp.sh --verify` OK; upstream full suite 0 failures, bench `A134ED3716DDE7BC` unchanged |
| `design-digest.py --check` | fails identically at the base (pre-existing; none of this lane's files) |
