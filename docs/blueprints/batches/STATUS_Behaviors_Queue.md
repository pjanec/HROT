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

CE-4652 done (row 4 → D-4); CE-2447 blocked on terrain CE-4655. Next: CE-9023 (`uc-urban-battle`, the immobilised APC's play).

## 2. The queue (in order)

| row | state | sha | rail | scenario |
|---|---|---|---|---|
| CE-4652 a near-missed mover does not drop | ✅ DONE — five causes: ① InertSop shooters broke (fixed), ② near misses double-counted (fixed), ③ the TakeCover branch's fresh sensor read first (OfTemplate: answered first), ④ a re-asked cover sensor scored 0 (holds its last answer), ⑤ the decision scored cover within 60 m, the branch searched 15 m ⇒ waited, failed, re-chosen for ever (the score now looks as far as the branch). Row (2) passes; row (4) now needs ~300 s ⇒ **D-4** | aa5a0d16c, ccd1d05cf, 7c0397f2b, (this commit) | `StandardInputReaderTests.CE4652_*` (2), `TacticsTreesTests.CE4652_*`, `StressSystemTests.CE4652_*`, `BallisticsSystemTests.CE4652_*` | ✅ row (2) · ⚠ row (4) → D-4 |
| CE-4653 a 10 HP man never asks to lie down | ✅ DONE — a stale check: 10 HP is under the incapacitated line (out of the fight, lies by LogicalStance); check restated, red-proved | (this commit) | — (scenario row) | ✅ `ua-fire-distribution` 5 / 5 |
| CE-3461 the militia never break | ✅ militia rows PASS (same causes as CE-4652); the REGULARS now break at ~30 s ⇒ **D-1 SKIPPED — needs decision** (`DECISIONS_Behaviors.md`) | aa5a0d16c | as CE-4652 | ⚠ rows (1), (3) red → D-1 |
| CE-4654 a man on a mine loses neither health nor mobility | ✅ DONE — answered by terrain's CE-2446 (merged): the AP burst now kills him (Health 0, no move / fire); pinned by a new scenario row; maim-instead-of-kill ⇒ **D-3** | (this commit) | — (scenario row (2b), red before CE-2446) | ✅ `mf-strike-and-halt` 7 / 7 |
| CE-2447 `mf-no-way-round`: a man enters before the squad reacts | ⛔ **BLOCKED on terrain CE-4655** — measured: the route does not cross the field, it hugs its north edge with zero clearance (leader y 110.1, the squad's position inside the edge); the reaction fires only when the squad clips the corner. Note sent | | — | ⚠ 1 row red |
| CE-9023 the immobilised APC's play never ends | TODO | | | `uc-urban-battle` |
| CE-3462 the waverer never breaks | TODO | | | `ts-house-defenders` |
| CE-3463 the guard does not fire first | TODO | | | `ia-area-defend` |
| CE-3464 a pinned man never fires, walks to the house | TODO | | | `rf-pinned-in-open` |
| CE-3465 no grenade at the wall-hugger | TODO | | | `rf-wall-hugger` |
| CE-3444 a unit thrashes between cover points | TODO | | | `uc-urban-battle` |
| CE-2442 no HQ answers an `Engineers` request | **SKIPPED — needs decision** (D-2: who answers, with what — the design names the engineers' work, not who orders it) | | | |
| CE-3470 the mounted riders abort under the strict parallel env | TODO | | | |

## 3. Taken from new handoffs

`HANDOFF_Coordinator_To_Behaviors_Bug_Queue.md` — 2026-11-18 09:00.

## 4. Notes sent / received

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
