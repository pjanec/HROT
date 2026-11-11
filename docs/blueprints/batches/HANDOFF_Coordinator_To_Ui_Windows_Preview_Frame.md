<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-11
current-answer: the whole file
related-designs:
  - ../../DESIGN_Evaluation_Cost_And_Cadence.md — §3.0–§3.2 the brain cadence (CE-2382; soldiers still every frame — D6 "a soldier / vehicle LOD later is a POLICY"), §11g-2 the 0.2 s decisions (CE-4395)
  - DESIGN_Time_Architecture.md — §17 stepped mode waits for background jobs (NOT the preview's mode — Play is continuous)
  - STATUS_Ui_Windows_Render_Under_Load.md — the first preview numbers (update 115 ms, ~40 ms named) this run attributes
-->

# HANDOFF — coordinator → ui (on the user's WINDOWS machine, every window HIDDEN): WHERE THE EDITOR'S FRAME GOES WHEN A 1 000-MAN PREVIEW PLAYS

**For:** the ui session on the user's Windows machine. **From:** coordinator. **Dispatched at `bc0c93253`**, scope frozen there. Merge the
lanes first; push `chore: started HANDOFF_Coordinator_To_Ui_Windows_Preview_Frame at <sha>`; status file `STATUS_Ui_Windows_Preview_Frame.md`.
⛔⛔ **Never stop for a question** (R-4156); a failed premise stops THAT item only (R-106). ⭐⭐ **EVERY run hidden** (`HROT_HIDDEN_WINDOW=1`,
uncapped `--target-fps 0`, Release) — 🔒 the user works on this machine; ⛔ no window may appear, take focus or flash.

**Why:** 🔒 user, `2026-11-11`: *"in the editor at 1000 man scenario a big 3d map frame rate drop when i unpaused"* · *"Same with the UI.
Unresponsive when complex sim running."* · *"I am not talking about deterministic mode, I am talking about normal. In the preview"*. The
coordinator measured in CODE (not on hardware):

| claim | where |
|---|---|
| Play switches the clock to CONTINUOUS — the kernel never waits for background jobs there (it waits only in stepped mode) | `MasterSyncController.cs:189-192`; `ModuleHostKernel.cs:678-683` |
| the position search (EQS) is a background job | `EqsModule.cs:39` `SlowBackground(10)` |
| the brains and combat logic run ON the drawing thread, and the brain writes the live world in place (~100 `GetComponentRW` sites) — it cannot run on a snapshot | `CgfLogicPack.cs:59` `Synchronous`; `BrainTickSystem.cs:146-149` |
| ui's first preview profile: update **115 ms / frame**, ~40 ms named (brains, weapons, stress, callouts, pathfinding); **~75 ms NOT attributed** | `STATUS_Ui_Windows_Render_Under_Load.md:30-31` |
| soldiers' and vehicles' brains tick EVERY frame — the interleaving built for rules (staggered `Periodic`, wake on senses, an event window per slot) is not used for them | `Behavior/Cadence.cs:12-20`; Evaluation Cost §3 D6 |

⇒ the question this batch answers with numbers: **what, exactly, runs on the drawing thread while a 1 000-man preview plays — and what would
interleaving the soldiers' brains save?** ⛔ It is a MEASUREMENT batch: no architecture change ships from it.

**Setup:** backend's rig regenerated (`python scripts/scale-rig.py --terrain scale-1 --men 1000 --vehicles 50 --aircraft 10 --write-only`); the
editor, hidden; open the scenario, *Play* in preview (the user's path); let the fight develop ~60 s before measuring.

| # | id | what | done when |
|---|---|---|---|
| **P-1** | CE-4433 | ⭐ **the drawing thread, playing vs paused**, at the same 3-D mid view and the 2-D whole terrain: frame time (min / avg / p95 over 30 s), and the update split **by module AND by system** (`HROT_FRAME_STATS` + the kernel's per-system timings) — every row ≥ 1 ms, so the 115 ms is accounted for to within 10 % | a table in the status file: playing − paused = the simulation's cost on the drawing thread, attributed |
| **P-2** | CE-4433 | ⭐ **the ~75 ms nobody has named**: measure each candidate on the drawing thread separately — the snapshot copy for background modules (`SharedSnapshotProvider.SyncFrom`), harvesting and playing back their command buffers, the egress translators, the event bus, the synchronous modules not yet named | each candidate with its ms per frame and how often it runs; the biggest three named with `file:line` |
| **P-3** | CE-4433 | **what the UI feels**: input-to-draw latency of a camera drag and of opening a panel while playing (frame-to-frame, from the render target), and the worst frame in 30 s — the "unresponsive" the user saw, as a number | the numbers beside P-1 |
| **P-4** | CE-4433 | **the interleaving experiment** (measurement only): behind an opt-in switch (`HROT_SOLDIER_CADENCE=<seconds>`, default off, never shipped on), give soldiers and vehicles `CadenceMode.Periodic` at 0.1 s and 0.2 s with the senses wake (the same gate rules use), and re-measure P-1 / P-3. ⚠ FIRST check and report whether a cadenced brain's tick receives the time ELAPSED since its last tick or one frame's dt (`BTreeRunner.cs:82`, `HsmRunner.cs:217` take `ctx.DeltaTime`) — if one frame's, timers inside brains would run slow: say so, measure anyway, do not fix | the saving per period, and the dt answer, in the status file |
| **P-5** | — | the **lean**, written for the user: from P-1…P-4, which of (a) interleave soldiers (D6), (b) cut the named non-brain cost, (c) the simulation's synchronous phase on a worker thread while the drawing thread shows a snapshot (the user's idea) — with the claim table | a short section the coordinator turns into an architect question |

**Rules:** measurement only — the opt-in switch of P-4 stays off by default and is named as experimental; no behaviour change ships. Numbers
GPU-honest from the hidden render target (say so). ⛔ No recordings, screenshots or terrain data in git. `tracker-counts.py --check` for the row.
Fold the numbers into Evaluation Cost and Cadence (a "measured on Windows, preview" block).

---
_coordinator lane, 2026-11-11_
