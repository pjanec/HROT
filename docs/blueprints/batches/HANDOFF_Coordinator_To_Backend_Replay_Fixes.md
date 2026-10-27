<!--STATUS
state: LIVE — coordinator → backend: the fix list for the replay / branch path, each with its repro and its acceptance
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Regression.md — §10-7: H3 (the branch harness) and its live runs — the acceptance rail for ①
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — §12w: your design for CE-4065 / CE-4066 — fold ① and ③ back into it
  - ../../DESIGN_People_Display.md — U10 / CE-1171: the blow's swing (②)
  - NOTE_Coordinator_To_Backend_After_Your_Fixes.md — the measurements this list is built from
-->
# HANDOFF — coordinator → backend: what to fix on the replay / branch path

**Dispatched at** the commit that adds this file (`coordinator`); ⚠ **re-stamped** by the next commit (rule 1a — not yet merged by `backend`): ② corrected after the probe ran end to end. Measured on `coordinator@be7dcd3a6` (your tree, merged, restored, rebuilt).
⭐ **Your CE-4065 fix is confirmed** — the branch starts on 5 s exactly and the second blow is gone. What is left, in priority order:

| # | row | what is wrong | reproduce (one command) | ✅ done when |
|---|---|---|---|---|
| **①** | ⛔ **CE-4067** | after a branch from a replay, every `/sim/step` of 1/60 s advances **0.023 s** (×1.39). Your master's own log: `[TimeSync] STEP #1. SimTime: 00:00:05.023, StepSize: 0.0167s` · `#2 05.046` · `#3 05.069`. 35 s of branch took 1509 steps (2100 expected). The live run BEFORE the branch steps exactly (2400 steps for 40 s) | `Hrot.ClusterRunner.dll --mode editor --headless --verify ua-civ-melee --verify-branch-at 5` | ⭐ that command exits **0**: `BRANCH at 5 s (came live on clock 5 s) same` — and the master logs `SimTime 05.017, 05.033, …` after the branch. ⚠ suspect, not read: the step's sim target after the CE-4065 wall→sim conversion |
| **②** | ⛔ **CE-4062** (reopened) | the blow's swing is still not drawn when a recording is STEPPED across it in the Replay Browser — fresh recording made with your merged binary: the rioter's arm UP; live at 4.4 s: the arm forward — the swing's follow-through ([side by side](../../img/ce4062-live-vs-rb-stepped.jpg), both made by the probe below). The policeman is down in both — ⛔ an earlier "mid-fall" live still was a coarser live stepping, NOT a replay difference | `python3 scripts/replay-blow-probe.py <out>` (new, `coordinator`: records a live run, then steps its recording across the blow over HTTP in a windowed RB; writes `live.png` and `replay.png`; `--recording <fdp>` reuses one) | ⭐ `replay.png` shows the rioter's arm as `live.png` does (forward). ⚠ Your rail stepped a real browser world — does the HTTP `/sim/step` in the RB reach the same `PublishCurrentOf` hand-off? 📐 Probe verified `2026-10-27`: ~4 min, reproduces the difference |
| **③** | ⚠ **CE-4066** (low) | a sim seek to 20 s once stood at 19.983 s (one frame short); not reproduced in the next 41. ⭐ My nod is **NO to "by design"**: 20 s IS a recorded frame, so on-or-before should land ON it. ⚠ inferred: `ReplayTimeMap.WallAt` interpolates between compressed points (`ReplayTimeMap.cs:177`), and an HTTP-stepped run's wall spacing is uneven, so the interpolated wall can fall a hair before that frame's own | `--verify ua-civ-melee --verify-replay` (H1b seeks every second); seen once | ⭐ a sim seek to a recorded frame's sim time (within `SimToleranceSeconds`) lands ON that frame — a rail over a map with uneven wall spacing. "Nearest" is NOT asked; on-or-before stays |
| **④** | **CE-1372** (from ui) | a process that has not registered a recorded component refuses the whole recording, though CE-4050 loads its assembly (`SchemaValidator.Validate`, `FDP/Engine/Fdp.Core/FlightRecorder/SchemaValidator.cs:60`) | the row's own repro (ui's live proof) | the row's acceptance |

⭐ **FYI, my edits in your files today (review, nothing to do):** `Program.cs` attaches the director of any subsystem that has one
(`ClusterDebugApiComposition.Director` — `/director/*` on the RB, user-approved); `POST /review/exit` builds through
`ReplaySessionIntents.ExitReview()`.

📌 **The verify harness is now your regression net for this path** — `--verify <s> --verify-replay` runs H1a (recording), H1b (replay in
order), H2 (shuffled seeks); add `--verify-branch-at T` for H3 (the branch). One process, under a minute for `ua-civ-melee`.

_coordinator lane, 2026-10-27_
