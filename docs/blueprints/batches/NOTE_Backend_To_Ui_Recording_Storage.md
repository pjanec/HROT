# NOTE — backend → ui: recording storage facts are on the cluster view (CE-4330 ③)

<!--STATUS
state: LIVE
updated: 2026-11-08
current-answer: the table below
related-designs:
  - docs/DESIGN_Recording_Storage.md — §7.2 the model, §7.3 what every station shows, §8 the mockup, §10 the as-built
-->

The handoff ([`HANDOFF_Coordinator_To_Backend_Recording_Storage.md`](HANDOFF_Coordinator_To_Backend_Recording_Storage.md)) asks for this note
after ③. What you can build the status bar, the Transfers popover and the picker chips (design §8 ①②⑤⑦) on — all read from `ClusterView`, the
same on every station:

| fact | where | what it holds |
|---|---|---|
| **① the jobs** | `ClusterView.StorageJobs` (`StorageJob[]`, `Fdp.Toolkit.Orchestration`) | every archive / import / delete-from-archive the master holds: `JobId`, `Kind` (Archive / Import / Discard / Delete), `ExerciseId`, `State` (Queued / Running / WaitingForNode / Done / Failed / Cancelled), `FilesDone/Total`, `BytesDone/Total`, `GivingWay` (a Live runs — the clock mark), `AskedBy`, `Reason` (why it failed). Active jobs first in queue order, then the newest 8 ended ones. Republished on every change, the numbers at most 4×/s |
| **the asks** | the existing ops — nothing new | `ExportArchive` / `ImportArchive` with JSON `{ExerciseId, AskedBy}`; a second ask for the same exercise JOINS (its `ClusterOpStatus` result carries the `StorageJob`); `DeleteExercise {ExerciseId, FromArchive: true}` = delete from the archive (§8 ⑦, put your confirmation in front of it); `CancelOperation(JobId)`; *Resume* = `ExportArchive` again (a new job; the pull skips files already whole) |
| **refusals** | the op's `ClusterOpStatus` result string | *"exercise 1a2b3c4d is being archived"* (a discard / another kind on a busy exercise) — show it as the reason |
| ⚠ **seats (Q7)** | yours to gate | Cancel / Resume only on the Author and Instructor seats; the master does not see seats (as every other op) |

Still to come from backend (the status file tracks it): fact ② the NAS state on `ExerciseInventoryItem` (complete / incomplete N of M, cached on), fact ③
each node's local recordings, the import to only the nodes that lack the file, and ⑥ the close-the-session model (`Archive / Keep / Discard`, default
Archive, no question when the run was not recorded) with its HTTP. A second note follows when they land.

---
_backend lane, 2026-11-08_
