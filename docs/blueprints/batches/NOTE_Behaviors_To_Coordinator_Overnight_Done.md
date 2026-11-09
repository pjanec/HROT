<!--STATUS
state: LIVE — behaviors → coordinator: HANDOFF_Coordinator_To_Behaviors_Overnight, done; two asks
updated: 2026-11-09
current-answer: the whole note
related-designs:
  - STATUS_Behaviors_Overnight.md — the per-item record with evidence
  - ../../DESIGN_Scenario_Regression.md — goldens are re-recorded by the change that moved them
-->
# NOTE — behaviors → coordinator: **the overnight batch — what is done, and two asks**

The record, per item with shas and gates: [STATUS_Behaviors_Overnight](STATUS_Behaviors_Overnight.md).

| item | result |
|---|---|
| O-1 B-17 P7 | ✅ built (P7-A/B/C), every look side-by-side with the sheets the user approved |
| O-2 CE-4350 | ✅ the "+ Add task" list fits the entity's class (Orders design §14) |
| O-3 CE-4344 | ✅ a possessed press during a reload is dropped |
| O-4 | ✅ CE-4123 · ✅ CE-4134 `bt-mortar-roof` (passenger lists named no seats) · the rest measured into their rows (CE-4124 is now a design question with a lean) |
| O-5 | ✅ CE-4055 `ua-tank-ammo` · CE-4041 partly (`bt-mortar-roof`, `ua-civ-escalation` moved) |
| O-6 CE-4109 | ✅ one registrar discovery |
| O-7 CE-2423 | ✅ the Rule target through the catalogue |
| O-8 CE-4035 | 📐 re-verified: 9 of 10 film scenarios pass every check |

## Ask 1 — re-capture the goldens moved by FORMAT changes (yours: you own goldens)

Measured on the editor `--verify` (fresh process each): these pass every check but report **GOLDEN different from t = 0**, and every
differing value is representation only — new `EntityGuid`s (S8), behaviour / mission-plan params written as objects (P-1, `2b50f0b92`),
passenger-list shape; positions are equal:
`ua-shootdown-eject`, `ua-car-bomb`, `ua-night-sensing`, `ua-weather-regions`, `ua-weather-sensing`, `ua-m2-engage`,
`ua-weapon-systems`, `ua-casualty-care`, `ua-javelin-lock`, `ld-sentry-surprise-360`.
⇒ `--verify <list> --verify-capture` once, and read the diff shape before committing. I did not bless them: a golden re-capture across ten
scenarios overnight, for changes other lanes made, is the owner's call.

## Ask 2 — CE-4124 needs a decision (design, not tuning)

The warning rung warns only the man a policeman AIMS at (`AimAndFireExecutor.cs:665`), so a gunman out of the launcher's reach is never
warned. Lean: the ladder warns an armed man it SEES. Details in the row.

_behaviors lane, 2026-11-09 (overnight)_
