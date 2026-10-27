<!--STATUS
state: LIVE — coordinator → behaviors (cc ui, backend): the full scenario verify on coordinator after the 10-25/26 merges
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Regression.md — §7 holds the run's row; §4 the golden rules this applies
  - NOTE_Coordinator_To_Behaviors_Scenario_Regression_After_Merge.md — the run before (10-22: 88 pass / 15 fail of 103)
  - NOTE_Coordinator_To_Behaviors_Scenario_Check_Findings.md, ..._Findings_2.md — where the older reds were handed over
-->
# NOTE — coordinator → behaviors: the scenario verify after the 10-25/26 merges

**Dispatched at** the commit that adds this file (`coordinator`). FYI only — nothing here changes a batch in flight.

**The run:** `--mode editor --headless --verify all`, every scenario in a fresh process, one stream, built from `coordinator@341da1eec`
(it already holds CE-1358, CE-1319, CE-4046 A, TS-2). ⚠ It hit its 7000 s limit after **98 of 103** — not run:
`uc-request-chain`, `uc-request-chain-dry`, `uc-urban-battle`, `uf-alone-under-fire`, `uf-squad-under-fire`.
⛔ **I re-captured no golden** — which moves are intended is yours to say.

| | count |
|---|---|
| pass their own checks | **77 of 98** |
| ⛔ fail their own checks | **21** — 14 already handed over, **7 new** (below) |
| golden `same` | 1 (`ua-civ-melee`, re-captured 10-26 for CE-3334) |
| golden `different`, checks pass | 62 |
| no golden (`missing` — every one also fails its checks, so a capture is refused) | 8 |
| no golden by design (demos, live runs) | 21 |

## ① ⛔ NEW reds — fail their own checks, in no earlier note (⚠ not confirmed green at a base commit)

| scenario | fails | what moved (the golden's rows) |
|---|---|---|
| `bt-grenade-posture` | the standing man, exposed over the wall, is hurt | was hurt at 4.75 s; now never |
| `ua-tank-ammo` | the M1 fires a soft-target round (< 100 mm) at the technical | the riflemen's first round 0.23 s is gone; the M1's kinetic round 8.53 → 8.93 s |
| `ua-fire-distribution` | the hurt man (10 HP) asks to lie down (HoldProne) | first round 0.92 → 1.22 s |
| `ua-armed-self-defence` | the police rifle fires at the Gunman · he is hit | hit at 12.35 s; now never |
| `df-lone-soldier-reflex` | Inert never gets a task · never moves west · never moves south | Inert now gets a task and moves |
| `bc-house-defended` | both insurgents are down | (a demo, no golden) |
| `bt-window-duel` | the Window Rifleman fires · the Street Rifleman bounds to Van 2's cover | first shot was the Window Rifleman at 5.83 s; now the Street Rifleman at 0.12 s |

Already handed over and still red (14): `bt-mortar-roof`, `hill-attack`, `pc-ambush-bypass`, `pc-urban-ambush`, `ua-air-defence`,
`ua-casualty-care-heavy`, `ua-civ-escalation`, `ua-civ-police-line`, `ua-rpg-window`, `ua-squad-danger-crossing-hold`,
`ua-technical-scoot`, `ua-universal-soldier`, `uc-convoy-hit`, `uc-reinforce-dry` — [findings](NOTE_Coordinator_To_Behaviors_Scenario_Check_Findings.md),
[2](NOTE_Coordinator_To_Behaviors_Scenario_Check_Findings_2.md), [after merge](NOTE_Coordinator_To_Behaviors_Scenario_Regression_After_Merge.md).

## ② Golden drift at t = 0 — a component's LAYOUT changed, not what a unit did

Every golden that differs, differs first at t = 0, so the report's "first differing snapshot" is the layout change and hides any later one.

| component (scenarios) | the change that moved it |
|---|---|
| `WeaponState` (66) | CE-1358 barrel heat — `Heat` added (ui, cross-lane into `Fdp.Toolkits`) |
| `SopState`, `SopStartRecord` (17) | CE-4046 group A — `SopStartRecord` an immutable record (backend) |
| `CombatTemperament` (6, golden `null`) | TS-2 / R2-2 temperament (behaviors) — a new component |
| `CommandOrderHistory`, `CommandReportHistory` (3, golden `null`) | CE-1319 B4 — the commander histories recorded (ui) — new components |
| `BrainDiagnostics` + `BlueprintBlackboard4096` (14), `BrainJournal` (4), the three channels + `LookAtChannelStatus` + `MissionPlanQueue` (6) | ⛔ not attributed — no change to their files since the capture; something they hold moved |

## ③ What units DO differently — the times, shots and blasts rows (33 scenarios)

⭐ These are not layout. The clearest pattern: **the first round comes ~0.3 s later** — `ua-fire-and-movement`(`-pairs`) 0.98 → 1.30 s,
`ua-fire-distribution` 0.92 → 1.22 s, `ua-tank-ammo` the M1 8.53 → 8.93 s. ⛔ Not run down. My lean (unmeasured): CE-1358's
*"the operator fires at the mount's own RateOfFire (0.25 s only when none is stated)"*.
Others with moved rows: `bc-house-clear`, `bc-room-clear`, `df-civilian-ordered`, `ua-armed-blend-back` (patrol A fires 66.2 → 22.3 s),
`ua-armed-bomber` (vest 36.25 → 39.25 s), `ua-armed-draw-aim`, `ua-armed-shield`, `ua-armed-surrender`, `ua-attack-approach`,
`ua-bounding-overwatch`, `ua-casualty-care`(`-leader`), `ua-civ-cower` (cleared 72.75 → 83 s), `ua-civ-neutral-gate` (Hostile out 13.25 → 38.5 s),
`ua-civ-riot-stone`, `ua-danger-crossing`(`-bp`), `ua-drone-shelter`, `ua-grenade-or-rifle`, `ua-ifv-standoff`, `ua-javelin-lock`,
`ua-squad-danger-crossing`(`-contact`), `ua-threat-ranking`, `ua-three-hosts`. Per-scenario reports: the run's `verify-all/<s>.json` (rerun to reproduce).

## ⏭ Asked of you

| | |
|---|---|
| ① the 7 new reds | a regression, or a check to change — yours to call; ⭐ lean: start with `bt-grenade-posture` and `ua-armed-self-defence` (a hit that no longer happens) |
| ② + ③ | confirm the moves are intended, then re-capture (`--verify <s> --verify-capture`; a failing run is refused). ⭐ ui's CE-1358 is the likely common cause of ③ — worth one check with ui before re-capturing 30 goldens on it |

_coordinator lane, 2026-10-27_
