<!--STATUS
state: LIVE — a note to RUNNING batches (FYI + follow-ups), not an amendment of any handoff
updated: 2026-11-16
current-answer: the whole note
related-designs:
  - ../RULINGS.md — R-4278 (these approvals), R-4279 (the regression suite)
  - ../../designs/minefields/DESIGN_Minefields.md — §11 the approved deviations
-->

# NOTE — coordinator → every lane: your lane decisions are APPROVED (R-4278), and the follow-ups

🔒 User, `2026-11-16`, verbatim: *"approved the work for the lanes (not yet RT-1…RT-9) but do not write handoff yet, firrst we finish the RT-1…RT-9
and then we write all at once"* · then *"approved. pls write handoffs"*. ⭐ Mark them approved in your status files.

| lane | approved | ⭐ follow-up |
|---|---|---|
| `backend` | L-A1, L-A3…L-A30 (animals) — L-A8 (two fear storages) as an INTERIM | CE-4628 (civilians' fear onto `Fear`) is filed, not scheduled. ⭐ Your next batch: `HANDOFF_Coordinator_To_Backend_Regression_Suite.md` |
| `terrain` | R-5042…R-5048 — ⭐ R-5047 (the side in the layer mask) supersedes KR-13's pad byte; folded into the design §11 | ⭐ behaviors waits on TWO things of yours: the per-mover BREADCRUMB record (MR-5, MS-T2) — then the struck unit backs out (R-2125 ends) — and the NAME of the clearer's equipment switch (behaviors' `ClearerEquipment.SetActive` is a stand-in, R-2126) |
| `behaviors` | R-2125 (hold in place — INTERIM), R-2126, R-2127 | when terrain's breadcrumb record lands, switch MineStrike to backing out (MR-5 stands); re-bind the clearer switch; the minefield scenarios note (`NOTE_Coordinator_To_Behaviors_Minefield_Scenarios.md`) stands |
| `ui` | R-1054 (the P-3 gate absent: truth unless a sided seat and the station setting), R-1055 | — |
| `exp/agg-backend` | R-8000…R-8004 | ⭐ agg-ui's finding: `AggregateUnit`'s fields need attribute-vocabulary rows (component + wire) for the unit form's Resolution section |
| `exp/agg-behaviors` | R-8300…R-8311 | ⭐ the 19 feature-test scenarios — `NOTE_Coordinator_To_AggBehaviors_Scenarios.md` |
| `exp/agg-ui` | UI-1…UI-9 (R-8600, R-8601) | ⭐ the NATO symbols (UI-12…UI-14) serve PHYSICAL units too, so the product will want them before the experiment merges: keep them in self-contained commits (the symbol geometry, the `MilStd2525Renderer` route, the ORBAT icon) so they can be merged into `ui` on their own when the user says |

⭐ All lanes: R-4279 — the regression suite (`docs/DESIGN_Scenario_Regression.md` §11). The verdict is the `checks.json` expectations; the golden
compares visible state and only warns; the nightly runs are GitHub's. How a lane proves its own work before merging is its own business (the user).
