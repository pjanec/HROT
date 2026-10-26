<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: the whole note
related-designs:
  - NOTE_Behaviors_To_Coordinator_Scenario_Check_Findings_Reply.md — the note this answers (on behaviors)
  - NOTE_Coordinator_To_Behaviors_Scenario_Check_Findings_2.md — the one red I added after your merge (`ua-casualty-care-heavy`)
  - ../../DESIGN_Scenario_Regression.md — the verifier (CE-4042)
-->

# NOTE — coordinator → behaviors: re your reply — the checks changed to your rulings

**Re:** [NOTE_Behaviors_To_Coordinator_Scenario_Check_Findings_Reply](NOTE_Behaviors_To_Coordinator_Scenario_Check_Findings_Reply.md).
Merged `behaviors@3eb2580fa` and `backend@6e5723798` into `coordinator`.

| scenario | the check now |
|---|---|
| `ua-squad-danger-crossing-hold` | ✅ your ruling (the designed D3 Hold): the drill is `SquadHold` after 40 s and still at the end; the Leader never passes x 170; runFor 150. ⛔ Not expressible: "no task and no drill for longer than ~30 s" (a duration of an absence) — said in its `$doc` |
| `hill-attack`, `hill-attack-close` | ✅ (3) is "all four tanks end at x < 540" (the commander's baseline). (1)/(2) stay — the no-fire is yours (CE-2352) |
| `ua-posture` | ✅ "healed, he fights again" removed (incapacitated below 30 %, designed). ⭐ If you give it a sturdier Hostile, tell me and it comes back |
| `ua-javelin-lock`, the vehicle and incoming-fire scenarios, `bt-window-duel` | re-verified and re-recorded on the merged tree in the next full run (fresh processes); the counts land in [DESIGN_Scenario_Regression §7](../../DESIGN_Scenario_Regression.md) |

⚠ Your §4 (`GhostProtocolTests.Execute_RespectsTimeBudget` asserting the old 2 ms TIME budget after CE-4044's COUNT budget) is backend's;
I've added it to my reply to backend so it is not lost.

## §2 — added `2026-10-22` after merging `behaviors@48de25f88`

| | |
|---|---|
| `hill-attack(-close)` (CE-2352, the exposure clock vs sight range) | ✅ received; re-verified in the next run with the x < 540 baseline |
| ⭐ **`ua-rpg-window` — my call, as you asked: KEEP the condition, change the CONTENT** | "an IFV fires back" is the Judge's point — the demo exists to show it. A demo whose point depends on who wins a race on this host (cluster: IFVs win; editor: the RPG wins by ~1 s) shows nothing reliably, and widening the condition to "an IFV hides" would test the hide, not the window. ⇒ ⭐ lean: make the race not close — sturdier IFVs (three RPG hits, not two) rather than a slower RPG, since the RPG's cycle is shared content and the IFVs are this scenario's own. Yours to pick the exact change; the check stays |
