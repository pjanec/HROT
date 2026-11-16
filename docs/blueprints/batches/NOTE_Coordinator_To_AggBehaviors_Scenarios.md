<!--STATUS
state: LIVE — a note to a RUNNING batch (exp/agg-behaviors)
updated: 2026-11-16
current-answer: the whole note
related-designs:
  - ../../designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md — §23.6 the 19 scenarios, §23.3 the slices
  - ../../DESIGN_Scenario_Regression.md — §3 the checks grammar, §11 the two tiers
-->

# NOTE — coordinator → `exp/agg-behaviors`: the stochastic unit's FEATURE-TEST SCENARIOS (R-4277, §23.6)

🔒 User, `2026-11-16`, verbatim: *"i think there should be a lot of feature testing scenarios for the aggregated entities, not sure if covered
already"* — 📐 not covered: §23.3 asked for one scenario (ST-4's). ⭐ The design's **§23.6** now lists **19**, one per situation, each with its
Judge lines.

| ⭐ | |
|---|---|
| **you author them** (you own the plays and ST-4's scenario) | `scenarios/demos/aggregates/<name>/` — `scenario.json` (What / Judge `description`), `checks.json` (one `expect` row per Judge line), `golden.json` |
| **a slice is DONE when ITS rows pass** | `scripts/scenario-regression.sh <name>` in the editor tier; ⭐ the cluster tier runs the same rows once backend's RT-5 lands |
| **a red that is an ENGINE defect** | filed to `exp/agg-backend` with the scenario and the failing row — ⛔ never a loosened check |
| **build them as their slice lands** | ST-0 / ST-1 / ST-2 now (their engine halves exist on `exp/agg-backend`); ST-3…ST-5 with their slices; list the waiting ones as ⏳ in your status |
| **a cue the grammar cannot say** | extend the grammar (§9 of the regression design), ⛔ never a check read from a log |
