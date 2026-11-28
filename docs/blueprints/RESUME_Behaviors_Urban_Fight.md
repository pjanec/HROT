<!--STATUS
state: LIVE
updated: 2026-11-22
current-answer: §0 state at 9204c0fb1 · §2 next, in order
stale-below: none
related-designs:
  - ../designs/group-maneuvers/DESIGN_Urban_Fight_Suite.md — the suite U1–U6 and its rubric (what "realistic" means here)
  - ../designs/group-maneuvers/DESIGN_Squad_Tactics.md — §7.6 break contact (CE-2465, CE-2472), §7.9 after a contact (CE-2466), §7.10 the ordered attack presses on (CE-2467)
  - ../DESIGN_Temperament_And_Stress.md — §7.8 fire is a RATE (CE-2470)
  - ../DESIGN_Thermal_And_Acoustic_Sensing.md — §8.6d one bang is one contact; a shot from a man in sight is him (CE-2474)
  - ../DESIGN_Scenario_Regression.md — §11.2 R-2141: per change only the changed features' scenarios; the whole set overnight on GitHub
-->

# RESUME — behaviors: urban fight realism (U1–U6)

Branch `behaviors`. Ids in the tracker ([Blueprint_Issues_Tracker.md](Blueprint_Issues_Tracker.md), next free `CE-2493`); rulings next free `R-2145`.

## 0. ⭐ State at `9204c0fb1` (2026-11-22) — read this first

- ✅ U2b `uf-corner-assault` PASSES; ✅ all five `pe-*` PASS (`pe-house-shot` since CE-2491); ✅ ia-house-hunt, ia-area-defend, ts-house-defenders, pc-defend-house, uf-squad-under-fire, agg-break-contact (one 12-scenario run).
- built today: CE-2481..2491 — the press-on share, a heard contact explained by a seen enemy, the opening at the ear, the corner point, the 3 m slack (straight hearings only, CE-2489), the muzzle flash binds the bang (R-2144: per man CE-2490, in the squad pool CE-2491, time-invariant). 📄 Sensing §8.6e–§8.6g.
- ✅ since: CE-2471 fixed (stable reads in the stress job), CE-2473 built (an attack on a place bounds to it, Squad Tactics §7.10.1), P6 + P7 written and passing (all seven `pe-*` pass), `main` fast-forwarded to `behaviors` at `d6a20c1d0` for the 02:17 UTC nightly.
- ✅ P2b `pe-wall-and-door` passes (CE-2492: a man only heard may have walked between shots) — all eight `pe-*` green.
- ⏳ open: P8 (one shot heard on two nodes — needs the cluster dump to carry heard contacts: runner infrastructure).

## 1. In flight at `ddfbd8c65` (2026-11-21)

| item | state | what is left |
|---|---|---|
| **CE-2474** one shot heard by several men is ONE contact (the ring's place as the bang's token, fused in the squad pool) and a shot from a man in sight is HIM (`AttributeToSeen`, 12°) — Sensing §8.6d | built `2026-11-22` (`11013d0f6`); the half-weight first try reverted | ✅ gated: 70/70 toolkit rails, SimHost 455/457 (the 2 = CE-2471); scenarios 7/8 — ⭐ `uf-corner-fight` (U2) now PASSES; ⛔ `uf-corner-assault` (U2b) still holds at 16.25 s — probe its pool (`probe_u2bdet7.py`) |
| **CE-2476** perception proven belief against truth — the `perceives` cue + suite `pe-*` (Sensing §8.6e) | cue built + red-proved (`4f61d15f7`); P1 P3 P4 P5 written | run them; then CE-2475 + P2 / P2b |
| **CE-2477** a fused heard circle never sharper than a lone ear | ✅ built (`69d41f3b6`) — found by `pe-seen-shooter` | — |
| **CE-2479 / 2480 / 2475** walls at their real loss · a gunshot loud (rifle 1.5 km) · the first wavefront (R-2143, R-2142) | built (`c88623084`), rails green | scenario run in flight: pe-* · U2b · U2 · U1 · bw-unheard · ia-house-hunt · ts-house-defenders · pc-defend-house · rf-wall-hugger · ia-area-defend · agg-break-contact · uf-squad-under-fire |
| **CE-2481** U2b: one grazed man stopped a 6 v 2 attack (exactly 3 : 1) | built (`c88623084`): a running attack presses until 1.5 : 1 | the U2b run |
| **CE-2472** break contact ends only with nobody in an enemy's sight (each man's own perception, `LookAt.InSightNow`; R-2037 — the brain never reads terrain) | code pushed; ⚠ `bad8282b8` did NOT compile, fixed in `ddfbd8c65` | ⛔ NOT YET GATED on a good build. Re-run: `SquadManeuverTests` + `BasicSquadSop` + `StressSystemTests` under `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1`; red-proof = drop `&& !AnyInSight(in ctx)` in `BreakContactPlay.Outcome` ⇒ `CE2465_…` red; then the scenarios below. ⛔ Check the build says `0 Error(s)` before trusting any green |
| **CE-2470** stress: fire is a rate (`FirePerSecond` 0.06/s while a round passed within 2 s, half in cover) | built, rails red-proved; `ts-*` 4/4 PASS (`ts-house-defenders` check follows D-6) | U2 went red with it — that is CE-2472 |
| **CE-2467** an ordered attack presses on while 3 : 1 (`PressesTheAttack` in `NotDefending` + the care row) | built, rail red-proved | `uf-corner-assault` (U2b) has NO `checks.json` yet — write it (the assault goes through; casualties cared for after) |
| **CE-2471** the parallel stress phase reads another man's `SimTransform` undeclared (`FriendNear`) | filed, ⚠ pre-existing (red at `6e765e589`) | two `StressSystemTests` surrender rails red under the read check only |
| **GitHub nightly** (`.github/workflows/scenario-regression.yml`, 20 shards per tier + one summary job) | works (run #2); `main` fast-forwarded to `behaviors` by hand, user-approved | the 02:17 UTC run checks out `main` — merge `behaviors` into `main` when its state should be tested |

### 1a. ⭐ The gated run at `ddfbd8c65` (good build, 2026-11-21)
- tests 403/405 under the parallel checks — the 2 reds are CE-2471 (pre-existing); CE-2472's red-proof ✅ (rail red with the sight clause dropped)
- PASS: `agg-break-contact`, `ia-area-defend`, `ts-fire-discipline`, `ua-squad-danger-crossing-contact`, `uf-car-shootout`, `uf-squad-under-fire`
- ❌ `uf-corner-fight` (U2): the SAME two rows — L3 by Blue Rifleman 1 at 108.75 s (⚠ BEFORE the break contact starts, ~117 s in the live probe: CE-2472 does not cover it — what is he doing 98–108 s in the DETERMINISTIC run? probe it there, not live) and L1 by the Red Leader at 152 s
- ❌ `ts-house-defenders`: the deterministic run's Waverer never surrenders (the live probe's did, calm, at 150 s) — probe the deterministic run
- `uf-corner-assault`: no checks.json ⇒ not started (expected)

## 2. Next, in order
1. Gate CE-2472 on a GOOD build (§1), then run: `uf-corner-fight`, `uf-car-shootout`, `agg-break-contact`, `uf-squad-under-fire`, `ia-area-defend`, `ua-squad-danger-crossing-contact`, `ts-house-defenders`, `ts-fire-discipline` (`JOBS=2 NO_BUILD=1 scripts/scenario-regression.sh a,b,…`). U2's red rows were L3 (Blue Rifleman 1 left in Red's sight) and L1 (Red Leader upright in the open, 152 s) — the second is not explained yet.
2. `uf-corner-assault` checks.json (CE-2467).
3. CE-2466 leftovers: the wounded-man rule (R2/S8), the platoon learning R1 (S3).
4. CE-2471.

## 3. Rules learned this session (also in CLAUDE.md / RULINGS)
- R-2141: after a change run ONLY the changed features' own scenarios (`docs/CAPABILITIES.md`); the whole set runs overnight on GitHub, never in a session.
- The brain asks the world only through sensors and EQS (R-2037) — never the terrain or a sight grid.
- A background chain must STOP when a build fails: a stale binary prints green (it did, `2026-11-21`).
