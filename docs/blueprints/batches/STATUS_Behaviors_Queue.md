<!--STATUS
state: LIVE · updated: 2026-11-18 (after ccd1d05cf + the InertSop judge fix) · current-answer: §1
related-designs:
  - PROTOCOL_Autonomous_Lanes.md — the protocol this queue runs under
  - HANDOFF_Coordinator_To_Behaviors_Bug_Queue.md — the queue's source
-->

# STATUS — behaviors, the queue

Self-wake-up: `behaviors self-wake-up` (hourly at :02, into this session) — created 2026-11-18 09:02, no approval asked.
Started at the merge of `origin/coordinator` (2026-11-18 09:00).

## 1. Now

⭐ **STEER taken (`STEER_Coordinator_To_Behaviors_Critical_Reload_And_Street_Fight.md`, user: critical):** CE-4665 (crowd slots never released after a reload — cross-lane into navigation) → then the street fight CE-4668 → CE-4667 → CE-4666 → CE-4669 (`HANDOFF_Coordinator_To_Behaviors_Street_Fight.md`), BEFORE the rest of the queue.
CE-3464 paused at a safe point (no code changes): measured — the editor-tier run shows Pinned DOES fire (ammo 150→145) and lies 1.4 m from the wreck at 23–39 s, yet the checks fail both rows; a stepped (`/sim/run-to`) probe was running to tell a stepped-vs-played difference from a checks-reading defect. CE-3463 parked at 4/6.

## 2. The queue (in order)

| row | state | sha | rail | scenario |
|---|---|---|---|---|
| CE-4652 a near-missed mover does not drop | ✅ DONE — five causes: ① InertSop shooters broke (fixed), ② near misses double-counted (fixed), ③ the TakeCover branch's fresh sensor read first (OfTemplate: answered first), ④ a re-asked cover sensor scored 0 (holds its last answer), ⑤ the decision scored cover within 60 m, the branch searched 15 m ⇒ waited, failed, re-chosen for ever (the score now looks as far as the branch). Row (2) passes; row (4) now needs ~300 s ⇒ **D-4** | aa5a0d16c, ccd1d05cf, 7c0397f2b, (this commit) | `StandardInputReaderTests.CE4652_*` (2), `TacticsTreesTests.CE4652_*`, `StressSystemTests.CE4652_*`, `BallisticsSystemTests.CE4652_*` | ✅ row (2) · ⚠ row (4) → D-4 |
| CE-4653 a 10 HP man never asks to lie down | ✅ DONE — a stale check: 10 HP is under the incapacitated line (out of the fight, lies by LogicalStance); check restated, red-proved | (this commit) | — (scenario row) | ✅ `ua-fire-distribution` 5 / 5 |
| CE-3461 the militia never break | ✅ DONE — militia rows (CE-4652's causes) + the regulars' row (D-1) | aa5a0d16c | as CE-4652 | ⚠ rows (1), (3) red → D-1 |
| CE-4654 a man on a mine loses neither health nor mobility | ✅ DONE — answered by terrain's CE-2446 (merged): the AP burst now kills him (Health 0, no move / fire); pinned by a new scenario row; maim-instead-of-kill ⇒ **D-3** | (this commit) | — (scenario row (2b), red before CE-2446) | ✅ `mf-strike-and-halt` 7 / 7 |
| CE-2447 `mf-no-way-round`: a man enters before the squad reacts | ✅ DONE — after terrain's CE-4655 clearance the march went round; re-authored as KG-7's 'the order goes through' (objective inside the field) + an inside-the-field check (three never-near rows; the old x-only row caught the clearance ring) | (this commit) | — (scenario) | ✅ `mf-no-way-round` 10 / 10 |
| CE-9023 the immobilised APC's play never ends | ⚠ defect FIXED (a killed man held the boarding back — `b7a088964`); the rest is by design (PlatoonHold pauses the convoy while the ambush is reported, DA-100) ⇒ **D-5 SKIPPED — needs decision** | b7a088964 | `SquadManeuverTests.CE9023_*` (red-proved) | ⚠ last row red → D-5 |
| D-1 `ts-fire-discipline` re-tune (CE-3461 regulars) | ✅ DONE — 22 rounds at 1.3 s (the measured window between the slowest militia break, ~25 s, and a centre regular's, ~33 s) | (this commit) | — (scenario) | ✅ `ts-fire-discipline` 7 / 7 |
| D-3 the mine's trigger man maimed (CE-4654 follow-up) | ✅ DONE — `AreaEffectSystem.Maim` (cross-lane): shins, 0.75 of the incapacitation line, alive | (this commit) | `AreaEffectSystemTests.CE4654_*` (red-proved; suite 34 / 0) | ✅ `mf-strike-and-halt` 8 / 8 · `mf-at-vs-vehicle` · `mf-recon-and-mark` · `mf-way-round` |
| D-2 `SupportAsset.Engineers` (CE-2442) | ✅ DONE — Minefields §13 designed and built (recon when unknown, then Breach along the requester's line) | (this commit) | `SquadManeuverTests.CE2442_*` (2, red-proved; parallel env 5 / 0) | ✅ `mf-engineers-answer` 7 / 7 (new) |
| CE-3462 the waverer never breaks | ⚠ Coward FIXED (the three insurgents were InertSop — no break; re-authored to the default SOP under StayOnTask, `ebdbe7348`); the Waverer is killed at stress 0.11 before he can break ⇒ **D-6 SKIPPED — needs decision** | ebdbe7348 | (rail `CE4652_AnInertSop…` stands; `8872a8478` tried + reverted `8d0524e10`) | ⚠ 4 / 6 → D-6 |
| ⭐ CE-4665 crowd slots never released after a reload (STEER) | ⛔ premise REFUTED (the production crowd is a no-op stub; DtCrowd has no cap) and NOT reproduced (pc-defend-house ×4 in one editor: 16·15·13·14 movers) — asked the coordinator for real-1 / the exact steps (NOTE); no code | | | reload ×4 |
| ⭐ CE-4668 dead men still run behaviours (street fight) | ✅ FIXED — RetireTheDead + the ingress dead gate + SOP skip; rails red-proved; feature scenarios running | adf885111 | `BrainTickSystemBTreeArmTests.CE4668_*` · `BehaviorIngressSystemTests.CE4668_*` | pc-defend-house · rf-pinned-in-open · ts-house-defenders |
| ⭐ CE-4667 SupportByFire / Suppress never exits | ⏸ DECISION D-7 (no designed exit for an empty weapon / dead posts; lean ① two exits inside SupportByFire) | | | |
| ⭐ CE-4666 bunched in the open, no cover | ⚠ TWO FIXES — ① `7c223fdb2` the Break ⇄ order thrash; ② the reserve's stimulus-less stress = DF-1 'heard close' fed by shots heard through his own window (weapon fire no longer counts) ⇒ live breaks 1/12 at 61/91/120 s (were 3/11·5/10·8/10). OPEN: real-1's centreline posts' cover search + base of fire's position (not in repo); the reserve's ~0.27 s move re-issue | 7c223fdb2, (this) | `SquadManeuverTests.CE4666_*` · `StressSystemTests.CE4666_*` | pc-defend-house 7/7 (runFor 180) · sq-defend-house · ts-three-defenders pass; ia-area-defend / ts-house-defenders / uf-squad-under-fire only their known reds | ⏭ the calm line's late fire MEASURED: shots heard through our own windows → anonymous "enemy" SPOT at the reserve's own post → the platoon's flank turn faces the line NORTH (Blue is south) ⇒ **D-8**, waiting
| ⭐ CE-4669 squads idle, 'moving' while still | ✅ BOTH HALVES FIXED — ① the locomotion row cleared when no move runs (`fdf738b0d`); ② the reserve that reached its point now SquadDefends there (was: no task after its bound). Found: CE-2450 (an upstairs post re-sent every ~4 s) | fdf738b0d, (this) | `LocomotionDispatcherTests.CE4669_*` · `SquadManeuverTests.CE4669_*` | pc-defend-house — see CE-4666 row |
| ⏳ wide scenario set (all 232, stepped editor tier, after CE-4666/CE-4669) | RUNNING in the background; reds so far: `archer-ambush` "two patrol soldiers killed" (patrol 4 walks at 5 s where the golden stands) — ⚠ SUSPECT CE-4666's fire filter: the warrior is the Insurgent type, whose bow shot is a SmallArms sound ⇒ verify by reverting the filter once the run ends (the run uses the built binaries) | — | — | all |
| CE-2450 posted man re-sends a move every ~4 s, never moves | ⏭ BACKEND'S — measured: the post is on his floor (not the storey retry); the move plans 4.49 m and the mover holds him (Speed 0, Accel 2.5), `FailedBlocked` → re-send. `NOTE_Behaviors_To_Backend_Person_Held_On_A_Short_Path.md`; no behaviors change | (this) | — | pc-defend-house (live probe) |
| CE-3463 the guard does not fire first | ⚠ 4/6 — three fixes (quiet frame post leans; frame arc faces out of the window; a frame move stopped from outside is re-sent) + the guard inside the 6 m ring; Blue South ✅. OPEN: after Blue South is down the lone guard never rotates to an east post (round < 2 posts or never runs — unmeasured) ⇒ Blue East unseen. Parked (R-106), next on return. Found CE-2449 (ia-house-hunt, pre-existing) | 922d6110a · d0c021f07 | `TacticsTreesTests.CE3463_*` · `SquadManeuverTests.CE3463_*` · `PeekAndFireNodesTests.CE3463_*` | `ia-area-defend` 4/6 |
| CE-3464 a pinned man never fires, walks to the house | ⏸ PAUSED for the STEER — the live run shows him firing and at the wreck; the checks still fail (stepped vs played, or the check's read) | | | `rf-pinned-in-open` |
| CE-3465 no grenade at the wall-hugger | TODO | | | `rf-wall-hugger` |
| CE-3444 a unit thrashes between cover points | TODO | | | `uc-urban-battle` |
| CE-2442 no HQ answers an `Engineers` request | ✅ DONE via D-2 | | | |
| CE-3470 the mounted riders abort under the strict parallel env | TODO | | | |

## 3. Taken from new handoffs

- `2026-11-18` decisions DECIDED (user via coordinator): **D-1** ③ re-tune `ts-fire-discipline`'s fire · **D-2** ① build `SupportAsset.Engineers` + demo · **D-3** ① the trigger man maimed (Leg zone), not killed — each back in the queue.
- `NOTE_Ui_To_Behaviors_Scenario_Reds.md`: `bt-window-duel` (the Window Rifleman never fires, the Street Rifleman never bounds) and `ua-danger-crossing` (the DangerCrossing never runs) fail their own rows — queued after the P rows.
- `NOTE_Terrain_To_Behaviors_Mine_Edge_Inset.md` (FYI): mines keep `max(spacing/2, 1 m)` inside the outline (CE-4655); a cross-lane edit in `ReconMinefieldBehavior.Mined`; `mf-no-way-round`'s red unchanged by it.
- self-wake-up: KEPT when the queue empties (R-4292).

`HANDOFF_Coordinator_To_Behaviors_Bug_Queue.md` — 2026-11-18 09:00.

## 4. Notes sent / received

- sent: `NOTE_Behaviors_To_Terrain_Mine_Maims.md` — D-3's cross-lane edit in `AreaEffectSystem` (the AP mine maims the man on it).
- received: `NOTE_Terrain_To_Behaviors_Route_Clearance.md` (CE-4655 built — CE-2447 unblocked, being re-authored) · `NOTE_Backend_To_Behaviors_LyingCapRail.md` (their CE4649 rail fixed, test-only).
- self-wake-up: message updated to §1a verbatim (`2026-11-18` 12:07); never deleted (R-4292).
- sent: `NOTE_Behaviors_To_Terrain_NoWayRound_EdgeClearance.md` — CE-2447 is their CE-4655 (routes hug a known field's edge).
- sent: `NOTE_Behaviors_To_Backend_NearMiss_And_LyingCap.md` — the cross-lane near-miss fix; their `CE4649` allocation rail is order-sensitive under the strict guards.

## 4a. Baselines measured after CE-4652 (`aa633308c`)

| scenario | red rows |
|---|---|
| `ts-house-defenders` (CE-3462) | Coward breaks · Waverer breaks · Waverer surrenders |
| `ia-area-defend` (CE-3463) | Blue South fires before the guard · guard never fires at Blue East · Blue East within 30 m first |
| `rf-pinned-in-open` (CE-3464) | Pinned returns fire · Pinned crawls to the wreck |
| `rf-wall-hugger` (CE-3465) | a grenade at the Hugger |

## 5. Gates (rule 8)

| gate | result |
|---|---|
| `Hrot.SimHost.Tests` TacticsTrees + PostureScenario + PeekAndFire + Stress (CE-4652 ③–⑤) | 172 / 0 |
| `Fdp.Toolkits.Tests` StandardInputReader + Eqs (CE-4652 ③④) | 194 / 0 |
| mutants (③–⑤) | the answered-first tie-break removed ⇒ `CE4652_TwoOfTheRunsOwn…` red · pending reads 0 ⇒ `CE4652_AReAsked…` red · CoverReach 0 ⇒ `CE4652_ThePostures…` red |
| `Hrot.SimHost.Tests` Stress + TacticsTrees (earlier) | 109 / 0 (and with the parallel env: 1 red pre-existing at base — `CE2272_LowResolve_…`, an undeclared SimTransform read) |
| `Fdp.Toolkits.Tests` combat (ballistics, area effects, aim-and-fire, sensor edges) | 208 / 0; guarded ballistics: backend's `CE4649` alloc rail order-sensitive (noted) |
| mutants | the inert guard removed ⇒ `CE4652_AnInertSop…` red · the old clamped near-miss test ⇒ `CE4652_TwoMen…` red (2) |
