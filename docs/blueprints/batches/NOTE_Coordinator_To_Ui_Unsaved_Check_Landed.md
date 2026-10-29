<!--STATUS
state: LIVE — coordinator → ui: CE-4093's master check round is on `backend` (`53d8d5795`) — wire CE-4097's unsaved-edits seam to it
updated: 2026-10-28
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — D20 / §4b (the round as built on backend), §2.8 (ui's CE-4097 core and its seam)
  - NOTE_Coordinator_To_Ui_CE4098_Review_And_Next.md — the earlier note that said "build against one seam"
-->
# NOTE — coordinator → ui: the unsaved-changes check round has landed; wire your seam to it

Merge `backend` (`53d8d5795` or later). What CE-4097's one *"are there unsaved edits?"* seam calls — ⛔ nothing else, no round of your own:

| step | the existing member |
|---|---|
| ask | `IClusterMasterControl.CheckUnsaved(new CheckUnsavedIntent { … })` (publishes on the host's bus; the master runs ONE round: every in-sync node hashes its world against its baseline, plus the master's own "a save failed since the load" fact) |
| read the answer | `ISubsystemDebugProvider.UnsavedCheck(requestId)` → `UnsavedCheckOutcome` (null until it arrives) — `Changed` and `ChangedNodes` |
| can this host ask at all? | `ISubsystemDebugProvider.ObservesUnsavedChecks` — false ⇒ the host has no read model; ⛔ do not wait for an answer that cannot arrive (ask nothing, go on as unchanged — the same rule the HTTP gate uses: `ALoad_WhereThereIsNothingToLose_OrNoWayToSeeTheAnswer_AsksNoRound`) |
| last verdict (display only) | `ScenarioDirty` — ⛔ reading it never runs a round; never decide a move on it |
| refused | outside `OperatingEdit` the master refuses (`UnsavedCheckRefusedReason`) — so ask only for moves that leave editing (D20's list) |

⭐ The HTTP side already gates `POST /scenario/load/live` and `load/edit` of another scenario (409 unless `discardUnsaved`) — your dialog and that
gate must ask the SAME round, so a rail that drives the move from the menu and one from HTTP should both see one `CheckUnsaved` per move.

Still to come on backend: CE-4091 (the recorded copy), and [the branch origin on the inventory](HANDOFF_Coordinator_To_Backend_Run_Origin.md)
for ■ (D5) — ⚠ your note said no recording knows its parent; `RecordingMetadata.ParentExerciseId` / `BranchWallTicks` already exist, only the
inventory lacks them.

_coordinator lane, 2026-10-28_
