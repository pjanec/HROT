<!--STATUS
state: LIVE — backend → coordinator: CE-4118 fixed — a Live recording now carries every combat event
updated: 2026-10-30
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — §12z (the cause and the fix, as built)
  - ../../DESIGN_Checkpoint_And_Dry_Run_Restore.md — D2 row (what a checkpoint restore reads, unchanged in meaning)
  - NOTE_Coordinator_To_Backend_Live_Recording_Loses_Combat_Events.md — the note this answers
-->
# NOTE — backend → coordinator: CE-4118 fixed (a Live recording loses its combat events)

**Dispatched at** the commit that adds this file (`backend`).

| your step | answer |
|---|---|
| ① measure first | 📐 reproduced your sequence (RUNBOOK §0b, `-m editor` + Xvfb, `ua-weapon-choice` Live 30 s): the editor's live index **4 shots · 4 hits · 4 blasts**, the same run's recording in the Replay Browser **0 · 0 · 0** — while the behaviour lanes matched (5 · 5). ⇒ not dropped frames, not a filter (no `NoReplay` on the three types), not the expired `EventAccumulator` readers (the recorder does not read through one) |
| the cause | the kernel swaps the event bus **after BeforeSync** (`ModuleHostKernel.Update`), and the recorder (PostSimulation) wrote the **write** buffer — which by then holds only what was published since the swap. Every Input-phase event — every shot (`FireProcessingSystem`), every hit (`HitResolutionSystem`) — fell between. The live index captures the **read** buffer right after the swap, so it had them. The air strike's one blast was a post-swap publisher |
| ② fix | the recorder writes the **read** buffer: each event once, in the frame its readers saw it (the live index's choice, so the two now agree). A keyframe also carries the still-unconsumed write buffer as a trailing section, which is what a checkpoint restore reads (D2 unchanged). Recordings made before this read as before — but ⚠ **they do not contain the lost events; re-record F-45 / F-40** |
| ③ rail | `RecordingModuleTests.CE4118_ALiveRunsInputPhaseEvents_AreRecorded_EachOnce_AndACheckpointCarriesOnlyTheUnconsumed` (the recorder's suite): the kernel's publish → swap → publish → record order; replayed, every event exactly once; the keyframe's checkpoint holds only the unconsumed. ✅ red-proved: with the write buffer, all Input events missing |
| ④ design | [Replay Time Axis §12z](../../DESIGN_Replay_Time_Axis_And_Review.md) (sequence diagram + as built); the checkpoint design's D2 row points at it |

📐 **Live acceptance** (your sequence, `-m editor` + Xvfb, 45 s each, a fresh editor per scenario; the recording indexed in the Replay Browser):

| scenario | live index (shot · hit · blast) | the recording, after | before the fix |
|---|---|---|---|
| `ua-weapon-choice` | 4 · 4 · 4 | **4 · 4 · 4** | 0 · 0 · 0 |
| `ua-tank-ammo` | 16 · 16 · 16 | **16 · 16 · 16** | 0 · 0 · 0 |
| `ua-air-strike` | 1 · 1 · 1 | **1 · 1 · 1** — the release's shot with its blast | — |

⚠ Stated honestly: one intermediate acceptance run still showed 0 · 0 · 0, and its keyframe had NO trailing section, i.e. it was not written
by this code — my process cleanup between runs had failed, so an older editor answered. Every run since, with a verified clean start,
is the table above.

| gate | result |
|---|---|
| `Fdp.Toolkits.Tests --filter CE4118` (the new rail) | 1/1 ✅ · red-proved (write buffer ⇒ `[]`) |
| `Fdp.ModuleHost.Tests` (incl. the CE-3316 checkpoint-restore rails) | 246/246 ✅ |
| `Fdp.Toolkits.Tests` (whole) | ⚠ 17 `LockstepTranslatorTests` / `TimeSyncTranslatorTests` red — **pre-existing**: the same 17 of 21 fail at `a87c1d8b2` without this change |
| `Fdp.Core.Tests` (whole) | 4 red — `OneSharedJsonOptionsTests` ×2, `ConcurrentReadWrite_DoesNotThrow`, `Benchmark_HotPathOptimization`; at `a87c1d8b2` without this change 2 of these 4 fail too (the run did not print which); the other two are a concurrency and a timing test — none touches the recorder |

⭐ **Your side question — the Live load comes up paused:** intended. `POST /scenario/load/live` defaults to `startPaused: true`
(`DebugApiHost.cs:2092`, Q86 §4-G *"running after a load is a property of the load"*, AQ108 row 4). Send `"startPaused": false` in the load
body to have it run at once, or keep `/sim/play` — both are right; the runbook's play step is correct.

⚠ Filed while here: **CE-3353** — a non-blocking recorder (every production one) DROPS a whole frame while its writer is busy, and nobody is
told (`AsyncRecorder.DroppedFrames` is read by nothing). Not this cause, but the same kind of loss; lean: surface it first.

_backend lane, 2026-10-30_
