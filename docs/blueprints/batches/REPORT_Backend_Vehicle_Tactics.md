<!--STATUS
state: LIVE
updated: 2026-10-14
current-answer: the whole file — the report for HANDOFF_Behaviors_To_Backend_Vehicle_Tactics.md (B1–B11)
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Vehicle_Tactics.md — the handoff this answers
  - docs/designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md — B3–B5 as-built §6 (+ the M6 hull-height finding)
  - docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md — B6 §2a, B7 §4a, B8 §5a
  - docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md — B2 §5, B9 §6
  - docs/designs/motion-models/DESIGN_Motion_Domains.md — B10 §5, the open decision §5a
  - docs/designs/vehicle-tactics/DESIGN_FPV_Drone.md — B11 §6
-->

# Report — backend → behaviors: vehicle tactics (B1–B11)

Scope frozen at `4b9c5495c`. Branch **`backend`**. Every item's as-built (and every deviation, argued) is folded into its owning design.

## Where each item is

| item | what | sha on `backend` | design fold-back | state |
|---|---|---|---|---|
| B1 | CE-2166 — the cooldown drains once (the dispatcher) | `1405a734f` | Missiles §1 | ✅ |
| B2 | CE-2167 / V5 — AI reverse | `76d5360d3`, `1ee3a04f5` | Exposure Cycle §5 | ✅ (CE-3029 corners: a planner-side nod is needed — below) |
| B3–B5 | M1 3-D arrival + Top · M2 armour per face × penetrator · M3 RPG / TOW shaped charge | `537abac79` | Hit Model §6 | ✅ |
| B6 | M8 — the loadout and the weapon brain's round choice | `19091c64f` | Missiles §2a | ✅ |
| B7 | M4 guided rounds + D-L firing on the move | `9a6d52704` | Missiles §4a | ✅ |
| B8 | M5 / M6 data | `39e6acd12` (+ the hull-size fix in the B10 commit) | Missiles §5a, Hit Model §6 | ✅ — ⚠ it broke the hill attack until the hull fix (below) |
| B9 | M9 technicals | `9e44ff4a5` | Exposure Cycle §6 | ✅ |
| B10 | CE-2168 motion domains v1 | `4e4cd78b3` | Motion Domains §5 | ✅ except the `MuscleAir` ownership role — ❓ **CE-3199, a decision** |
| B11 | M7 the drone's Muscle half | `4e4cd78b3` (same commit) | FPV §6 | ✅ Muscle half; cross-node motion waits on CE-3199 |

## Gates — the final run, on the B10/B11 commit (B1–B9 each gated at their own commit, rows in the item sessions)

Built once per project (`dotnet build <proj> --no-restore`), every run after is `--no-build`; the TEST project was built, never only the
production one. Tree clean after every suite (no golden written by a test).

| gate | verbatim command | result | `--no-build` | delta |
|---|---|---|---|---|
| Fdp.Toolkits | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build` | ✅ 3271 / 0 / 1 skip | yes | +9 new rails (rotorcraft 4, drone impact 2, overhead vision, rotor sound, the bridge skips a flyer); the skip pre-exists |
| Hrot.Core | `dotnet test Hrot/Engine/Hrot.Core.Tests --no-build` | ✅ 239 / 0 / 0 | yes | +2 (`M7_TheFpvDrone_…`, `B8_ANedHull_StatesItsSizeAndLocomotion_…`) |
| Hrot.Presentation | `dotnet test Hrot/Engine/Hrot.Presentation.Tests --no-build` | ✅ 563 / 0 / 8 skip (after the 2 re-pins; the first run had them red) | yes | 2 re-pinned on purpose: `CE2138` picker golden +1 line (`403 FPV drone`, additive, nothing removed) · `LR1` explosive set + `TkbFpvWarhead` (the drone's own shaped charge, M7) |
| Hrot.SimHost | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --no-build` | ✅ 1338 / 0 / 6 skip | yes | +1 (the air system precedes the drone impact system in the pack); 6 skips pre-exist |
| Hrot.Network.NED | `dotnet test Hrot/Network/Hrot.Network.NED.Tests --no-build` | ✅ 141 / 0 / 0 | yes | the `Vertical` field round-trips through the existing codec rails |
| ⭐ integration (row 8: B6, B7, B10, B11) | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests --no-build --filter "…WeaponChoiceScenarioTests\|…FireDistributionScenarioTests\|…CE1056_AConvoyIsAmbushed\|…DistributedBrainMuscleIntegrationTests\|…DebugApiOrdersTests\|…EntityOrdersAcrossNodesTests\|…SubEntityCascadeDestroyTests\|…DistributedScenarioLoadTests"` | ✅ 24 / 0 / 1 skip | yes | the skip (`CgfAiIntent_ReachesSimHost_ViaDds`) pre-exists |
| ⭐ live hill attack | `scripts/hill-attack-check.py hill-attack-close 1000` on `ClusterRunner --mode all` | ✅ commander finished t=35.5, 1006 + 1007 Health 0 | — | was ⛔ after B8 (both at 50 by t=45), ✅ again with the hull fix; B7 baseline t=46 |
| ⭐ live ua-weapon-choice | `scripts/utility-demo-check.py ua-weapon-choice --launch` | ✅ PASS (TOW 2500 → 786, insurgent killed, 25 mm burst of 2) | — | — |
| docs | `design-digest.py --check` · `rulings-check.py` · `mermaid-check.mjs` (4 touched designs) · `tracker-counts.py --check` | ✅ · 196/196 · ✅ · open 106 / done 386 | — | ids allocated: **CE-3197, CE-3198, CE-3199** |

tt-convoy-ambush: the in-process `CE1056_AConvoyIsAmbushed` rail is in the integration row; the live scenario was not re-run.

## Follow-up decisions, same day (user, `2026-10-14`)

| | decision | built | sha |
|---|---|---|---|
| **CE-3199** (who owns a flyer's position) | 🔒 *"use one single Muscle role, so all positions are owned by it"* · *"keep MuscleGround"* — **R-3005** | `MuscleGround` applies to a template with `Gen.Flight`; `FlightParams`/`FlightState` in its group. No `MuscleAir`, no rename, no wire | `c5d5b7689` |
| **CE-3029** (vehicles cut corners) | 🔒 *"round each corner in the planned path to fit the vehicle's turning radius"* — **R-3006** | corners rounded in the solver for WheelBase / tan(MaxSteer) + (measured necessary) the follower's lookahead capped across a curve. Live `tt-nav-los`: **1.77 m** from Block A (was 0.16–0.7). ⚠ the scale-out DDS path request does not carry the radius — filed `CE-3200` | `b25c01e35` + this |

## Findings (ids allocated: CE-3197, CE-3198, CE-3199)

- **CE-3199 ❓ decision** — the `MuscleAir` role (MD-B) cannot exist while the ownership table allows a component in ONE group only; lean:
  domain-exclusive groups (one owner per component per entity). Motion Domains §5a.
- **CE-3198** — the NLAW's overfly top attack cannot be flown with one turn-rate model; it flies direct.
- **CE-3197** — `UtilityScorerTests.CE2069_*` intermittent (2 of 4 full runs); not root-caused.
- **Hill attack regressed after B8, fixed:** NED hulls had collider height 0 (the height came only from the Stride render descriptor) ⇒ no M1
  height band, and the blast rule took a 50 hp M1 for a person ⇒ the weapon brain chose HEAT-MP, which the 900 mm front stops. Fixed by the
  NED builder stating `Body.Geometry` (Hit Model §6).
- **Wire (R-169):** `WeaponFireRequest.AmmoGuid` (B6) and `Vertical` on `EntityManualDrive` **and on `ControlFrameInteractionPayload`** —
  the station's frame reaches the brain through the interaction union, so the climb axis rides both (Motion Domains §5).
- **CE-3029 (corner cutting)** still needs the planner-side corner rounding you were asked about (Exposure Cycle §5).
