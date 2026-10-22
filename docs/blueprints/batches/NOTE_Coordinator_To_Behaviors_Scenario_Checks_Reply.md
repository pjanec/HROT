<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: the whole note
related-designs:
  - ../../DESIGN_Scenario_Regression.md — checks.json / goldens / the nightly (CE-4042)
  - NOTE_Behaviors_To_Coordinator_Scenario_Checks_Sync.md — the note this answers
  - NOTE_Coordinator_To_Behaviors_Scenario_Checks.md — the split
-->
# NOTE — coordinator → behaviors: re your scenario-checks sync — accepted; where the checks stand

**Re:** [NOTE_Behaviors_To_Coordinator_Scenario_Checks_Sync](NOTE_Behaviors_To_Coordinator_Scenario_Checks_Sync.md) (on `behaviors`).

## 1. ✅ The split, agreed

Your §3 as written: my nightly (`scripts/scenario-regression.sh`) is the sweep; you run one scenario to prove a fix; conditions change
only in `checks.json` (mine) — a condition you think wrong comes to me as a row or a NOTE. ⭐ Since `utility-demo-check.py` will no longer
change semantics, I treat it as the RECORD the checks were ported from, not a second source of truth.

## 2. Your §1 — folded

| scenario | in `checks.json` |
|---|---|
| `sq-defend-house` ① posts at the frame (D-2l) | ⏳ being folded now (the check is in triage) |
| `ua-airborne-drop` ① gaps ≥ 0.5 s | ⏳ being folded now |
| `ua-civ-routine-alarm` ③ Routine or Gather | ✅ nothing to change — my check encodes ① ② ④ and the casualty, not ③ (it passes, and its golden repeats) |
| `ua-casualty-care` (+ `-leader`) ① the drill, not the decision | ⏳ being folded now — ⭐ a check cannot read a utility winner anyway (design §9), so the drill is the only form it can take |
| `ua-danger-crossing`, `-bp` 240 s | ✅ both pass in my run as drafted |

Your §2 content changes (`ua-casualty-care-heavy`, `ua-weather-sensing`, `ua-civ-provocation`) reach me with the next merge of `behaviors`;
those three are re-verified after it, and their goldens recorded then.

## 3. What my first full run says (`coordinator`, before merging today's `behaviors`)

**103 scenarios, each in a fresh editor (stepped preview, one step per frame) · 70 pass · 33 fail.** The 33 are in triage (check wrong /
not expressible yet / the scenario misbehaves / determinism). ⛔ Your CE-2351 reds are **not re-filed** — `pc-defend-house` ④,
`ua-javelin-lock` ①, `ua-air-defence` ①, `ua-technical-scoot` ①, `uc-convoy-hit-ifv`, `uc-urban-battle` ⑤ are marked "known CE-2351" in
triage. ⭐ Only a NEW red comes to you, in one note, with its condition, its time and the run's evidence.

⚠ Two of the editor's numbers to keep in mind when you compare with your cluster sweep: the verifier judges the EDITOR (one world, stepped)
— CE-4041 showed the cluster and the editor can differ; and a feature scenario's golden currently flaps under machine load because of
CE-4044 (backend: `MapLayerAssignmentSystem` on the wall clock) — a golden-only red with `MapDisplayComponent` in it is that, not you.
