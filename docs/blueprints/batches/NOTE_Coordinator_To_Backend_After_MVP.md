<!--STATUS
state: LIVE — coordinator → backend: after the armed-mode MVP — CE-4103 and CE-3343 first, write the CE-3339 row, then on call for ui's CE-4104 (the mode switches); amended 2026-10-29 before backend started (rule 1a)
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - HANDOFF_Coordinator_To_Ui_Mode_Switches.md — ui's CE-4104 / CE-4105
  - ../PLAN_Run_Modes_And_Content_Build.md — §4 the order (user, 2026-10-29)
-->
# NOTE — coordinator → backend: after the MVP — CE-4103, then the mode switches

🔒 **User, `2026-10-29`:** *"fixing the issues found and then continuing in the rest of the armed mode switching stuff, prioritizing the mode
switches like live->AAR, AAR->live, live->DAR, DAR->live … Only once this is working we can focus on change indication, auto-save and such."*

✅ The MVP is proven (CE-4102, ui): an editor live run records, the Replay Browser opens it, the director films it — no break was in your code.

## 1. CE-4103 — the three workarounds ui's rail needed (⚠ measured in-process by a Sonnet agent — VERIFY each first)

| # | the claim | expected |
|---|---|---|
| ① | an open in the editor's FIRST second leaves the host's view at Idle while the master is at `OperatingEdit` — the editor's own state is broadcast only once the master's roster has heard the slave's 1 Hz heartbeat | an open right after start ends in the same state on every view |
| ② | an UNSEEDED staging root parks a scenario load on the prefetch saga forever, with no error | it fails with the reason (R-4001: no silent forever-wait), naming what is missing |
| ③ | a SECOND `EditorSubsystem` in one process never leaves Idle | find the cause; fix if cheap, else record it (test-only today) |

Rails in each feature's own suite; fold what you learn into the owning design (orchestration / content layout).

## 1b. ⭐ Also now (user, `2026-10-29`: *"yes, do both"*) — amended before you started (rule 1a; ancestry checked)

| # | what | why |
|---|---|---|
| ① | **CE-3343** — route the 47 test-only repo-root walk-ups through the one existing walk-up (`ContentLayout.TryFindSourceTree`) | R-3066 step 4: the unification the user wants finished first. ⚠ `6196efa68` only FILED the row — no code moved (one file changed, the tracker). Mechanical ⇒ **Sonnet**, one agent at a time; it touches test files only, so it does not collide with ui's CE-4104 |
| ② | **write the CE-3339 row** (the two scenario hashes for AAR → Preview — the recorded copy's and each saved scenario's, `HANDOFF_Coordinator_To_Backend_Scenario_Hashes.md`) | the id was allocated in your pause note and is cited by the plan, but ⛔ **no row exists in the tracker**. Note the partly built worktree in it. It stays PARKED (plan §5) |

Order: CE-4103 → CE-3343 (one Sonnet agent at a time); the CE-3339 row any time.

## 2. Then on call for CE-4104

ui proves each switch end to end — Live → AAR, AAR → Live (the branch), Live → DAR, DAR → Live at the live pause point. ⛔ DAR → Live from the DAR moment is DEFERRED (CE-4105 — the user wants it to truncate and re-record the
same exercise, later, if required). The likely backend pieces: `FinalizeLive` → AAR, the
branch (`BranchLive`, the parent's scenario copy, the branch fields on the inventory), `ReviewProcessManager` (enter / exit a DAR, the recording
continuous across it). A break ⇒ ui's NOTE with the measurement; fix it first.

⛔ **Still waiting (user):** the ExCon retirement · CE-4099 autosave · more change detection · CE-3339 · CE-4095 · CE-4092 · CE-3342.

_coordinator lane, 2026-10-29_
