<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-07
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §4c D23 autosave (CE-4099, READY-TO-BUILD, gated on A0)
  - ../../UX/DESIGN_Seats_And_Hats.md — §2.2a (CE-4305, R-4164)
  - ../../UX/DESIGN_Mission_Plan_Editor.md — §5.9 E9 (the Wait task's load warning)
  - ../../UX/DESIGN_Scenario_Lifecycle_Review.md — LC-12 (autosave is what an author needs most after a crash)
  - PROTOCOL_Two_Lanes_Self_Coordinating.md — the same protocol, three lanes now (ui, backend, behaviors)
-->

# HANDOFF — coordinator → backend: after the mission plan

**For:** backend. **From:** coordinator. **Dispatched at `21a1a5ac`**. ⭐ Scope FROZEN there.
Merge `coordinator`, `ui`, `behaviors` first (restore after), push `chore: started HANDOFF_Coordinator_To_Backend_After_Mission_Plan at <sha>`,
then the protocol — ⭐ three lanes now: fetch and read `behaviors` too ([note](NOTE_Coordinator_To_Ui_And_Backend_Behaviors_Joins.md)).
⛔⛔ Anything needing the user is skipped (R-4156).

🔒 User, `2026-11-07`: *"It looks like the backend has finished and needs some more work."*

## The items — in order

| # | id | what | design basis | done when |
|---|---|---|---|---|
| **X-1** | CE-4305 | server stations default **Author**; `"features": { "station.seat": {…} }` in the runner's `--config` file, below `--feature`, above the scenario preset | [NOTE](NOTE_Coordinator_To_Backend_Server_Seat_Config.md) · [Seats §2.2a](../../UX/DESIGN_Seats_And_Hats.md) (R-4164) | that note's rails (incl. the file and `--feature` beat a scenario's preset) |
| **X-2** | CE-4298 rest | your own open end: the LOAD WARNING for a Wait task (no behaviour) with no *then* — names the entity and the task, the load goes on | Mission plan §5.9 E9 | a rail: such a file loads with one warning naming the task |
| **X-3** | — (file a row in your block) | ⭐ **root-cause `EntityMission_MovesEntity`** ("moved 0.0 m", red at `2b50f0b92` too) — a mission plan that does not move its entity is the feature's core promise; ⛔ "pre-existing" is not a cause. Fix if it is ours, else file with the cause | Mission plan §5.4 (the start path, D2) | green, or a tracker row naming the cause and the owner |
| **X-4** | CE-4099 | ⭐ **autosave (D23)**: ① **A0 — the gate, measure first** (3 scenarios incl. `uc-urban-battle`, `pc-defend-house`: sandbox rebuild = live-world write); ② A1–A8 (the master's clock, the save round with `Autosave = true`, never a save, worker extraction, `.autosave/`, deleted by a real save, recovery offer, the never-saved case). If A0 FAILS, stop, write the measurement into §4c and a QUESTION note — ⛔ do not build on a failed gate | [AQ108 §4c](../Architect_Question_108_Run_Mode_Picker.md) | §4c's rails; a killed editor reopened offers the recovery |
| **X-5** | — | **the SimHost pre-existing reds** you listed (`TheDescriptorMapIsWired`, `CE1159` weather demo, `CE4208`, `NodeRolePersistenceRails`, `CE1431`, `CE3345`) + `The_registered_window_set_is_unchanged` (stale in all 3 modes): each FIXED, or FILED with its measured cause and owner — ⛔ never skipped or quarantined | the CLAUDE.md test tiers | your status file: one line per red, fixed / filed |

⏳ **Coming next, not yet approved:** the lifecycle review's backend parts ([DESIGN_Scenario_Lifecycle_Review](../../UX/DESIGN_Scenario_Lifecycle_Review.md) —
the Problems list LC-13, *Run to …* LC-10, the recordings folder LC-21) wait for the user's Q1–Q13. ⛔ Do not start them from this file.

## Gates

The CLAUDE.md gate contract, rows 1–8. ⭐ Row 8: X-4 touches the save round on every node ⇒ the save / load suites and a cluster save rail;
X-1 touches every server boot ⇒ the bootstrap / feature suites.

---
_coordinator lane, 2026-11-07_
