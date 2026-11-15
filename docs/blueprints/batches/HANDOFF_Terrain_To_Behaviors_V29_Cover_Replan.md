<!--STATUS
state: LIVE
updated: 2026-11-14
current-answer: the whole file (a frame — the behaviors lane designs the details as step 1)
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — OWNS the squad defence; its D-2m L2 slice ("the open-ground OVERVIEW ask — a new all-round cover-points template") IS this work
  - docs/DESIGN_Ground_Patches_And_Road_Blockage.md — OWNS the cover the terrain now gives (§9.14 foxholes / ditches, §9.11 the crater rim, G-35 / V-29 / V-30)
-->

# HANDOFF — terrain → behaviors: a defending squad takes foxholes, ditches and craters (V-29's behaviour half)

**Dispatched at `437e529c`** (terrain lane, 2026-11-14). Your scope is frozen at that sha; later documents are FYI only. A FRAME (lane protocol): goal, fences,
what exists, the lean, acceptance — the behaviors lane designs the details as step 1. Lane decision [R-5031](../RULINGS.md).

## Goal
A squad defending a position with NO engagement area (the all-round defence) posts men in foxholes, ditches and crater rims the way it posts them at windows; and a
squad already posted picks up cover that appears LATER (a crater's rim once its gases clear) for the men still without a post.

## What exists (terrain side — built, measured)
| what | where | note |
|---|---|---|
| a foxhole's FOUR all-round cover points (crouch at 1 m) | `TerrainCoverProvider.AddGroundCover` | V-29: one faces any threat, so `CoverTowardTest` keeps one for an EA too |
| a ditch's floor points, a pair facing each bank every 2.5 m | same | V-30 |
| a crater's rim points, live from the view, once `BornAt + coverAfterSeconds` passed | `CraterCover.Points` via `CoverPointsGenerator` (:141) | G-35 — no bake: a new ask sees them at once |
| ⛔ the all-round defence asks only the windows | `SquadDefendBehavior.Plan` (roof and cover stages gated on `hasEa`) | D-2m row 1, DL-1 — and L2 is "the open-ground OVERVIEW ask (a new all-round cover-points template)" |
| ⛔ posts are planned ONCE | `SquadDefendBehavior.Plan` (`Ready` set, then only displacement) | a crater after the plan is never picked up |

## Lean (yours to change in your design)
1. **L2 is this work:** the all-round cover template (the existing cover generator with no target, as the window ask has `FindWindowPostsAllRound`), asked after the windows for
   the men without a post — so ground cover (foxholes, ditches, rims) is a candidate set for coverage (DL-1…DL-3) like the windows.
2. **Re-plan on a cover change:** when `CraterTable.Of(view)` returns a NEW instance (a crater added or gone) or a known crater's `BornAt + coverAfterSeconds` passes,
   ask the cover stage again for the men WITHOUT a post only (a held post is never moved by it — DL-4's "not erratic").
3. ⛔ Fences: parallel-phase rules (CLAUDE.md "behaviour code runs in parallel"): read the crater table from the view, no static state; no new component on the wire.

## Acceptance
1. An all-round squad at a position with two foxholes and no building: two men posted IN the foxholes (crouch), arcs outward.
2. A crater appears 20 m from a posted squad with a man left without a post: after `coverAfterSeconds` he is posted on its rim; nobody else moves.
3. The squad's existing rails stay green under `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1`.
