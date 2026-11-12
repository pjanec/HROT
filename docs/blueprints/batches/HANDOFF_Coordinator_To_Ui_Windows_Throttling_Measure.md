<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — OWNS this: §19 Windows scheduling (power throttling, timer resolution, priorities, hybrid cores, console QuickEdit); CE-4452
  - HANDOFF_Coordinator_To_Ui_Windows_Scaling_Measure.md — run that one FIRST; this follows in the same session
-->

# HANDOFF — coordinator → the Windows session: is Windows throttling our hidden / background / console nodes?

**For:** the ui session on the user's Windows machine. **From:** coordinator. **Dispatched at `c3e9c903a`**. ⭐ Run it AFTER
`HANDOFF_Coordinator_To_Ui_Windows_Scaling_Measure.md`, in the same session, never at the same time.
🔒 User, `2026-11-12`: *"what about windows frontend / backend throttling … i do not want headless mode running in console to be any slower"* ·
*"add both"*. ⛔ **Autonomous** (R-4156). ⛔ **Measure only — change no code.** ⛔ Windows other than the console runs below must stay hidden or
minimised — the user works on this machine.

## Why

Nothing in HROT opts out of Windows 11's power throttling (EcoQoS) — measured: one hit in the tree (§19). Every Windows number so far ran with a
HIDDEN window, so it may have been throttled. Windows can exempt a program from power throttling without code:
`powercfg /powerthrottling disable /path "<full path of the .exe>"` (and `… reset /path …` to undo; `powercfg /powerthrottling list` to check) —
so the effect can be measured before any code is written. Find which `.exe` actually runs the nodes (the `Hrot.ClusterRunner.exe` apphost or
`dotnet.exe`) and say which you exempted.

## The branch — exactly **`exp/perf`**, Release, `HROT_QUERY_TIMINGS=0` for every run

## The matrix — the 1 000-man rig (`scale-rig.py`), CGF frame ms + SimHost frame ms + the processes' CPU clock if you can read it

| # | how the nodes run | throttling exemption |
|---|---|---|
| T-1 | as every run so far: `HROT_HIDDEN_WINDOW=1` | none |
| T-2 | the same | ⭐ `powercfg` exemption ON for the node exe |
| T-3 | headless CGF / SimHost in a VISIBLE console window, the console in the FOREGROUND | none |
| T-4 | the same console, MINIMISED, another application in the foreground | none |
| T-5 | T-4 | exemption ON |
| T-6 | on battery vs plugged in, if the machine is a laptop on battery-saver settings — say which power plan is active (`powercfg /getactivescheme`) | — |

⚠ Undo every exemption at the end (`powercfg /powerthrottling reset /path …`) and say so. Repeat T-1 once for the noise.

## Report

`docs/blueprints/batches/STATUS_Ui_Windows_Throttling.md`, pushed onto **`exp/perf`** only: the machine, the power plan, the exe exempted, the
matrix (ms, fps, CPU clock if read), the noise, and the verdict — does a hidden / minimised / background node run slower, and does the exemption
recover it? ⛔ No screenshots or recordings in git.

---
_coordinator lane, 2026-11-12_
