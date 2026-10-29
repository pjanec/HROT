<!--STATUS
state: LIVE — coordinator → backend: the user narrowed the effort to the armed-mode MVP; backend is on call for CE-4102
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - HANDOFF_Coordinator_To_Ui_Armed_Mode_MVP.md — ui's tasks and CE-4102's chain
  - ../PLAN_Run_Modes_And_Content_Build.md — §4 the MVP scope
-->
# NOTE — coordinator → backend: armed-mode MVP — on call, nothing new to start

🔒 **User, `2026-10-29`:** *"focus on making the armed mode switching work with whatever is requires, but in 'minimal viable' way - meaning
avoiding not absolutely necessary parts like change detection, autosave. excone retirement can wait. … bt-window-duel scenario failures are out of
scope … the goal here is to make it possible to easily record exercise so we can prepare movie clips in using director in replay browser"*

| | |
|---|---|
| ⭐ **on call** | **CE-4102** (ui drives it): editor live run → recording on disk → the Replay Browser opens it → the director films it. ui sends a NOTE with the measurement when a break is in your code (where the recording lands, the recorded scenario copy, the terrain staged from the NAS) — fix those first |
| ✅ already done for it | CE-3341 backend half (the armed mode on the wire), CE-4090 (record live runs, default ON), CE-4091 (the recording keeps its scenario), CE-4089 (content layout) |
| ⛔ **wait** (user) | the ExCon retirement (R-3067) · CE-4099 autosave · CE-3339 (the two hashes) · CE-4095 · CE-4092 · CE-3342 (`bt-window-duel`) |

_coordinator lane, 2026-10-29_
