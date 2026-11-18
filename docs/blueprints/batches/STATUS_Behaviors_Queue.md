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

IN PROGRESS: CE-4652 — fixes pushed (an InertSop man never breaks: `aa5a0d16c` + the judge commit; one near miss per man per pass:
`ccd1d05cf`); the three scenarios (`uf-squad-under-fire`, `ts-fire-discipline`, `ua-fire-distribution`) re-running to see what is left.
⚠ CE-4652 remaining cause (measured live): the hostile shooters (InertSop, CombatPosture in the open) flip TakeCover (2) ⇄ HoldProne (6) — the two
options mirror one input (EqsTopScore of the cover search); TakeCover runs PeekAndFire, which in the open does not fire, so each flip back stands
the fire down before the aim completes (WeaponChannel issued 37 times, ActiveAction 0, 1 round in 30 s) ⇒ the movers get no fire to drop from.
Next step: find when `uf-squad-under-fire` last passed (bisect the CombatPosture tree / PeekAndFire changes, CE-2366 / AQ102 B) — the same family
as CE-3444 (thrash between cover points).

## 2. The queue (in order)

| row | state | sha | rail | scenario |
|---|---|---|---|---|
| CE-4652 a near-missed mover does not drop | IN PROGRESS — causes ① InertSop shooters broke and fell silent (fixed), ② near misses double-counted (fixed) | aa5a0d16c, ccd1d05cf | `StressSystemTests.CE4652_*`, `BallisticsSystemTests.CE4652_*` | ⏳ re-running |
| CE-4653 a 10 HP man never asks to lie down | ✅ DONE — a stale check: 10 HP is under the incapacitated line (out of the fight, lies by LogicalStance); check restated, red-proved | (this commit) | — (scenario row) | ✅ `ua-fire-distribution` 5 / 5 |
| CE-3461 the militia never break | ✅ militia rows PASS (same causes as CE-4652); the REGULARS now break at ~30 s ⇒ **D-1 SKIPPED — needs decision** (`DECISIONS_Behaviors.md`) | aa5a0d16c | as CE-4652 | ⚠ rows (1), (3) red → D-1 |
| CE-4654 a man on a mine loses neither health nor mobility | ✅ DONE — answered by terrain's CE-2446 (merged): the AP burst now kills him (Health 0, no move / fire); pinned by a new scenario row; maim-instead-of-kill ⇒ **D-3** | (this commit) | — (scenario row (2b), red before CE-2446) | ✅ `mf-strike-and-halt` 7 / 7 |
| CE-2447 `mf-no-way-round`: a man enters before the squad reacts | TODO | | | `mf-no-way-round` |
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

- sent: `NOTE_Behaviors_To_Backend_NearMiss_And_LyingCap.md` — the cross-lane near-miss fix; their `CE4649` allocation rail is order-sensitive under the strict guards.

## 5. Gates (rule 8)

| gate | result |
|---|---|
| `Hrot.SimHost.Tests` Stress + TacticsTrees | 109 / 0 (and with the parallel env: 1 red pre-existing at base — `CE2272_LowResolve_…`, an undeclared SimTransform read) |
| `Fdp.Toolkits.Tests` combat (ballistics, area effects, aim-and-fire, sensor edges) | 208 / 0; guarded ballistics: backend's `CE4649` alloc rail order-sensitive (noted) |
| mutants | the inert guard removed ⇒ `CE4652_AnInertSop…` red · the old clamped near-miss test ⇒ `CE4652_TwoMen…` red (2) |
