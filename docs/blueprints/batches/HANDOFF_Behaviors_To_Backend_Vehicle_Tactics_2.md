<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-14); scope frozen there.
updated: 2026-10-14
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Vehicle_Tactics.md — the first handoff (B1–B11); this is its follow-up
  - docs/blueprints/batches/REPORT_Backend_Vehicle_Tactics.md — backend's report on B1–B11, whose open rows these items close
  - docs/designs/vehicle-tactics/DESIGN_FPV_Drone.md — §6's two ⛔ not-built Muscle rails (C1, C2)
  - docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md — §8's ⛔ not-built executor rail (C3); V4 / V8 as-built
  - docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md — §5a, the NLAW overfly (C5, CE-3198)
-->

# HANDOFF (2) — backend follow-ups after B1–B11: three rails, a merge, and the NLAW overfly (queued)

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at the commit that adds this file**:
your scope is FROZEN there; documents that change after it are FYI. If a later document invalidates an item, **STOP that item and report
it**; do every item that is not blocked (R-106).

## 0. Why

Your report on B1–B11 left three rails ⛔ not built, and one row is a duplicate of a fix already on `behaviors`. 🔒 User, `2026-10-14`:
*"backend finished, any more backend work for them?"* · on CE-3198: *"queue after drone attack"*. CE-3029 (corner rounding, R-3006) is
done on your side and merged into `behaviors` — thank you; it is not in this handoff.

## 1. Before anything

1. Merge `behaviors` at the dispatch commit or later (V4, V8 and this file live there). Push a `chore: started …` marker (rule 1b).
2. Ids: new findings from **your** block (`CE-3xxx`); progress on our rows (CE-2165) as an appended dated line.

## 2. The items — in this order

| # | item | design | acceptance | why now |
|---|---|---|---|---|
| **C1** | **merge `behaviors`** — it carries CE-2169, the root cause and fix of your **CE-3197** (`UtilityDecisionCatalog.RegisterAll` replaces `Shared`; the cached scorers kept the old registry ⇒ order-dependent, not a race; fixed by `ScorerOverShared`). The row is marked a duplicate on `behaviors` (`dc2a99bb9`) | Decision Layer §3.3 | `UtilityScorerTests.CE2069_*` green in 4 of 4 full `Fdp.Toolkits.Tests` runs on the merged tree; close CE-3197 | it is the flake you saw in your gates |
| **C2** | **`M7_ADive_UnderADeck_MeetsTheSlab`** — a drone diving at a vehicle parked under a deck meets the deck, not the hull. ⚠ A deck is a `TerrainWalkable` SLAB (measured in V3, Exposure Cycle §3): `TryStandAt` rejects any point inside a prism footprint at any height, so a deck modelled as a floating prism has nothing under it. ⭐ V3's `OverheadCoverTest` already sees a walkable slab through the same vertical `SightBlocked` (`TerrainEqsTests.V3_OverheadCover_*`), so expect `DroneImpactSystem` to see it too — the rail proves it | FPV §4, §6 ⛔ | the rail, in `DroneImpactSystemTests`, on a terrain world with one walkable slab | `ShelterFromAir` (V8) is only worth anything if the deck actually stops the drone |
| **C3** | **`M7_APossessedDrone_FollowsTheStation_AcrossNodes`** — possession → `ControlFrameInteractionPayload.Vertical` → the Muscle's `RotorcraftModel.Manual` climbs; it waited on CE-3199, now settled (R-3005: one Muscle role) | FPV §6 ⛔, Motion Domains §5 | the rail in the possession integration suite (the one that already carries a vehicle across nodes) | user: *"Drone will need to be controllable also from FPS possess mode"* |
| **C4** | **`V8_AnHmg_FiresAtASeenDrone_ATankGun_IsOutOfArc`** — the weapon-brain half: a pintle HMG (B9's TKB 104, −10…+70°) is granted rounds at a drone 40 m up; a tank's main gun at the same drone ends `WeaponTaskFailure.OutOfArc` (`TurretSlew.InArc`, `WeaponTaskDispatcherSystem.cs:62`). The behaviour half is `ShelterFromAirNodesTests.V8_ASeenDrone_IsFiredAt_AndAnOutOfArcFailure_IsRetriedLater` | Exposure Cycle §8, §10 | the rail in `AimAndFireExecutorTests` | `ua-drone-shelter`: the technical shoots back, the tank hides |
| **C5** ⏸ **QUEUED** | **CE-3198 — the NLAW overfly top attack** (your fix shape: a `MissileDto` OVERFLY mode that detonates as the round passes over the hull's height band, resolved as arriving from the Top with the round's penetrator) | Missiles §5a | your row's rails; `ua-javelin-lock` unchanged | ⛔ **do not start until this lane reports the drone attack (`FpvAttack`, FPV §2's Brain half) built** — 🔒 user: *"queue after drone attack"*. We will append the go to CE-2165 and tell you |

## 3. Fences

- ⛔ C1–C4 are RAILS plus whatever they expose; if a rail goes red against your own code, the fix is in scope, said in the report.
- ⛔ No combat number moves (Hit Model / Missiles data); no wire change expected — report any (R-169).
- ⛔ The behaviour lane owns `ShelterFromAir`, `VehicleFightFromCover`, `BasicVehicleSop`, `AirContacts`; a finding there is a REPORT, not an edit.

## 4. Report back — the gate report contract (CLAUDE.md rule 8)

One row per gate: verbatim command · pass / fail / skip · delta vs base · `--no-build` column · every red confirmed pre-existing against the
base sha · golden movement as a diff shape · tree clean after each suite · `tracker-counts.py --check` · every id allocated · ⭐ for C3 the
INTEGRATION suite that carries possession across nodes, or why it cannot gate. Then the sha of each item on `backend`.
