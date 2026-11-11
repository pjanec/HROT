<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-11
current-answer: the whole file
related-designs:
  - ../../DESIGN_Replay_Playback_Performance.md — OWNS the replay speed work: §3a as built (P1, P2, P4, P5, P6, CE-1479), measured on Linux with software GL
  - ../../DESIGN_Change_Gated_Systems.md — CE-1480, the change gate the replay frame now rides
  - MEASURE_Ui_Replay_Frame_Cost.md — the Linux measurements (§1–§9) this run repeats on real hardware
-->

# HANDOFF — coordinator → ui (on the user's WINDOWS machine, every window HIDDEN): how fast is replay now — the 1 000-man recording

**For:** the ui session that runs on the user's Windows machine. **From:** coordinator. **Dispatched at `d493c195f`**, scope frozen there. Merge
the lanes first (`coordinator`, `ui`, `backend`); push `chore: started HANDOFF_Coordinator_To_Ui_Windows_Replay_Speed at <sha>`; status file
`STATUS_Ui_Windows_Replay_Speed.md`. ⛔⛔ **Never stop for a question** (R-4156); a failed premise stops THAT item only (R-106).

⭐⭐ **EVERY run hidden** — 🔒 user, `2026-11-11`: *"again with windows hiding so it does not annoy me doing other light work in the machine."*
`HROT_HIDDEN_WINDOW=1` on every editor, Replay Browser and test run (L-0, CE-4386); ⛔ no window may appear, take focus or flash in the taskbar. Frame
times come from the off-screen render target (GPU-honest, no vsync / compositor — say so); uncapped (`--target-fps 0`). A VISIBLE confirmation run
only when the user says the machine is free.

**Why:** 🔒 user, `2026-11-10` (CE-4407): *"the replay for this 1000 entities scenario is almost impossible … something is eating all the time in
this AAR mode"* — the UI ran below 1 fps. Since then ui made replay much cheaper on Linux (CE-1478 P1/P2/P4/P6: frames prefetched, partial repair,
block deltas — format v8; CE-1479: the Browser plays by recorded TIME; CE-1480: change-gated systems) — ⚠ **but measured on a 4-core VM with
software GL**, so nobody knows what the user will actually see. This run answers that on the user's machine and fixes what is still too slow.

**Setup:** Release builds. The load: backend's rig — `python scripts/scale-rig.py --terrain scale-1 --men 1000 --vehicles 50 --aircraft 10
--write-only` (⚠ REGENERATE it: CE-4405 changed the generator to platoons and companies; `scale-1` from `python scripts/terrain-scale.py --trees` if
absent). A fresh recording in the v8 format: open the scenario LIVE in the editor (hidden), let it fight ~3 min, *End exercise* → save to AAR (the
user's path). ⚠ An older recording on this machine is format < v8 — measure it too, once, as the "before".

| # | id | what | done when |
|---|---|---|---|
| **W-1** | CE-4432 | ⭐ **the user's path, measured**: start screen → *Open recording* → the editor's review of that recording. Time: the open (double-click → first frame); then **1× playback** for 60 s (fps min / avg, frames applied per second, does recorded time keep pace with the clock); **seek** to 25 % / 75 % / end (seconds each); **step** one frame; at 3-D near / mid / far and 2-D whole terrain — with the split by `HROT_FRAME_STATS` (update · apply / repair · map preparation · world draw by layer · UI) and the replay counters under `HROT_REPLAY_*` (MEASURE §8–§9); garbage per frame; memory | a table in the status file AND in Replay Playback Performance §3a ("measured on Windows") beside the Linux rows |
| **W-2** | CE-4432 | the same for the **standalone Replay Browser** on the same recording (its own clock, CE-1479) | the same table, a second column |
| **W-3** | CE-1478 | ⭐ **fix what is under budget, biggest cost first** — the budget is the R-4192 one: **≥ 30 fps** while playing at 1×, a seek ≤ 2 s, **0 B garbage** per steady frame; each fix re-measured; P3 (the design's measure-gated phase) only if the numbers call for it | every view ≥ 30 fps, or a row naming the remaining cost, its owner and the number |
| **W-4** | CE-4407 | close the user's finding with the before / after: the < 1 fps of `2026-11-10` vs today, on the same kind of recording | CE-4407 closed with the numbers, or left open naming what remains |

**Rules:** performance only — the look does not change. Gates: the touched test classes + each new rail red-proved once; a 0 B rail per fixed
per-frame path; display rails hidden as usual (a skip is not a pass). ⛔ No recordings, screenshots or terrain data in git (stills to the
scratchpad and the chat only). `tracker-counts.py --check`.

---
_coordinator lane, 2026-11-11_
