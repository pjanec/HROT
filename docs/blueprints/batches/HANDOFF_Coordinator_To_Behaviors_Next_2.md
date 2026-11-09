<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-09
current-answer: the whole file
related-designs:
  - ../../DESIGN_Scenario_Regression.md — goldens are re-recorded by the change that moved them
-->

# HANDOFF — coordinator → behaviors: the next batch

**For:** behaviors. **From:** coordinator. **Dispatched at `bd4994196`**, scope frozen there. Merge the other lanes first; push `chore: started
HANDOFF_Coordinator_To_Behaviors_Next_2 at <sha>`; status file `STATUS_Behaviors_Next_2.md`. Same [protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md): a status file after every item, fetch and merge the other lanes after every item. ⛔⛔ Never stop for a question (R-4156): an item that needs the user is SKIPPED with its question in its row; a failed premise stops that item only (R-106).

| # | id | what | done when |
|---|---|---|---|
| **G-1** | — | ⭐ **your Ask 1, authorised by the goldens' owner (coordinator):** re-capture the goldens of the ten scenarios that pass every check but differ only in representation (`ua-shootdown-eject`, `ua-car-bomb`, `ua-night-sensing`, `ua-weather-regions`, `ua-weather-sensing`, `ua-m2-engage`, `ua-weapon-systems`, `ua-casualty-care`, `ua-javelin-lock`, `ld-sentry-surprise-360`) — `--verify <list> --verify-capture`; ⛔ commit one only when its diff is representation-only (ids, param objects, passenger-list shape; positions equal) | per scenario: the diff shape in the report (CLAUDE.md gate row 3) |
| **G-2** | CE-4124 | ⏳ ONLY when the user's decision is in RULINGS (your Ask 2; lean: the ladder warns an armed man it SEES) — otherwise skip | its rail |
| **G-3** | CE-4041 (rest) · CE-4136 · CE-4137 · CE-4139 | the measured findings: fix the behaviour or correct the scenario's description — each row closed with its cause | one line per row; a rail where a defect was fixed |
| **G-4** | CE-4132 · CE-4133 | your verify PASSED — close the rows with the evidence | rows closed |
| **G-5** | CE-4035 | the tenth film scenario (the one not yet passing its checks) | it passes |
| **G-6** | CE-4151 | a TKB type's default task carries its params, like its default SOP (user request) | a rail: a type's default task arrives with its params |

---
_coordinator lane, 2026-11-09_
