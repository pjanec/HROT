# NOTE — backend → ui: recording storage, the rest you can build on (CE-4330 / CE-4331)

<!--STATUS
state: LIVE
updated: 2026-11-08
current-answer: the table below
related-designs:
  - docs/DESIGN_Recording_Storage.md — §10 the as-built, row by row
  - docs/blueprints/batches/NOTE_Backend_To_Ui_Recording_Storage.md — the first note (fact ①, the jobs)
-->

The second note the first one promised. Everything below is on `backend` and read the same on every station.

| for | what to read / call | |
|---|---|---|
| **⑤ the picker's location chips** (§8) | `ExerciseInventoryItem.NasState` (None / Incomplete / Complete), `NasFiles`, `NasFilesTotal` | *archived* · *⚠ archive incomplete 1 / 2* · *on the nodes* (an unarchived item with NasState None). ⚠ *cached on* is not on the item yet: the master knows it (`ClusterMaster.NodesHolding`) |
| **③ ⏏ Close the session** (§3 P1) | `SessionClose.Question(recorded, exercise, length, scenario)` (null ⇒ ask nothing, Q3) · `SessionClose.Answers` (Archive first = the default, Q1) · `SessionClose.IntentFor(answer, exercise, askedBy)` → publish it like `DiscardRecording` does | replaces *Keep this recording?* in `RunModeChoice.AfterLiveEnded` — yours to swap (HTTP `POST /session/close` already uses the model) |
| **⑥ the quit guard** | nothing to build — `TransferExitGuard` asks through the run choice's ONE pending slot your toolbar already draws (`RunModeChoice.Confirm`, a small public entry I added to your file — cross-lane, one method) | |
| **⑦ Delete from the archive** | `StorageIntents.DeleteFromArchive(exercise, askedBy)` after YOUR confirmation | the master queues it as a job |
| **the AAR start on another cluster** (§9a) | the import job's `Reason` names what is not played and who listens (null ⇒ every recording plays on its own node) | ask *Start / Cancel* with it, only when it is not null |
| **Go Live from a replay** (§9b) | refused by the master with the reason when the cluster differs | grey it with that text |

⚠ Seen while gating, yours: `TheStartGateIsComposedOnceTests.AnOrchestratorOnlyProcess_StartOffersOpenNewAndRecording_*` looks for a *"New Scenario…"* button no code builds any more (your Start-screen rework).

---
_backend lane, 2026-11-08_
