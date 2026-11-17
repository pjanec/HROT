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
| 3 S-1 priority lead (CE-9000) | ⚠ HALF | ✅ HROT: `RoadUse.InTrafficPriority = 6` → `MoveToExecutor` hands it over → `TrafficHandover.Priority` (rail `CE9000_TheEscortsHandover_CarriesItsPriority`, traffic suite 88/88). ⛔ SUMO: attaching upstream `pjanec/SumoSharp` (R-4046) was **denied by the session's permission check** — H17 is ready as [`H17_SumoSharp_Convoy_Escort.patch`](batches/H17_SumoSharp_Convoy_Escort.patch) — ⭐ purely additive (user, `2026-11-17`): the priority twin vType + a max-speed cap restored exactly; compiles; ⭐ **VERIFIED UPSTREAM `2026-11-17`** (a local clone of `pjanec/sumosharp` at `7da452be`, branch `hrot/convoy-escort-h17`, not pushed): the whole `Traffic.sln` suite in Release — **0 failures** (`Sim.ParityTests` 793 passed / 5 skipped, the emergency-vehicle goldens included; `Sim.LiveCity.Tests` 130/130 incl. 4 new `HrotConvoyEscortTests`; `Sim.Pedestrians.Tests` 331/331; the rest green); `Sim.Bench` hash **`A134ED3716DDE7BC`** = the pinned one, par == single. New tests: an ordinary shadow stops at a red, the priority twin runs it · a twin behind a car held at the red waits and never passes it · the max-speed cap slows it and `null` restores the free speed · `priority: false` is exactly the old call. Red-proved: twin not applied ⇒ red; restore wrong ⇒ red. The patch (now with the tests) applies to `src/` as `-p1` from the repo root. The first draft's red-light rule for other cars and its absolute speed factor were withdrawn; until it is synced a priority lead is spawned as an ordinary car (logged once) | `4c68cd211` |
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
| ✅ H17 upstream gate | full `Traffic.sln` Release 0 failures (parity 793/5, LiveCity 130/130), bench `A134ED3716DDE7BC` unchanged — ⏳ to apply upstream (this session cannot push there) and sync with `scripts/sync-sumosharp.sh` |
| `design-digest.py --check` | fails identically at the base (pre-existing; none of this lane's files) |
