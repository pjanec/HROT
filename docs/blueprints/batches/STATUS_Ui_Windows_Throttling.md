<!--STATUS
state: LIVE — measured, done
updated: 2026-11-12
current-answer: the whole file (§0 is the verdict)
related-designs:
  - ../../DESIGN_Parallel_Simulation.md (on `coordinator`) — OWNS this: §19 Windows scheduling (power throttling …); CE-4452. ⚠ §19 should carry §0's verdict — not edited here (measure only, status file only)
  - HANDOFF_Coordinator_To_Ui_Windows_Throttling_Measure.md (on `coordinator`, dispatched at c3e9c903a) — the handoff this answers
  - STATUS_Ui_Windows_Egress_Anomaly.md / STATUS_Ui_Windows_Scaling.md / STATUS_Ui_Windows_Perf.md — every earlier Windows number, which ran THROTTLED (§3)
-->

# STATUS — is Windows throttling our hidden / background / console nodes? (CE-4452)

Measure only, no HROT code changed. The window modes and the throttling opt-out were applied from a scratch wrapper outside the repository.

## 0. Verdict

| | |
|---|---|
| ⭐⭐⭐ **YES — a node with no visible window runs at about HALF speed** | hidden (as every run so far): CGF **228 / 222 ms** (~10 fps), SimHost **101 / 90 ms**. The same nodes **opted out of power throttling: CGF 121 / 123 ms (~19 fps), SimHost 44 / 44 ms** ⇒ **1.8–1.9× faster, both nodes** |
| ⭐⭐ **minimised is as slow as hidden; the opt-out recovers it** | console minimised: CGF **235 ms** → with the opt-out **120 ms** |
| ⭐⭐ **a VISIBLE console is not throttled** | console shown (not minimised, see §4 on "foreground"): CGF **128 ms**, SimHost **50 ms** — about as fast as the opt-out |
| ⭐ **the mechanism is the clock, not the CPU share** | CGF's CPU time stays **1.13–1.19 cores** in every column, but the machine's processor performance is **~80 % of base clock (≈2.6 GHz)** throttled vs **~120–127 % (≈3.8–4.1 GHz)** opted out / visible. Forcing EcoQoS ON explicitly gives the same 238 ms as the default ⇒ **Windows already puts windowless nodes in EcoQoS by default** |
| ⭐ **power** | AC power, battery 100 %, plan **Balanced** (`381b4222-…`). Battery not tested (§4) |

## 1. How each column ran

| | |
|---|---|
| machine | AMD Ryzen 7 6800H, 8 cores / 16 logical, base 3.2 GHz, 31 GB, Windows 11 Home (laptop), AC, Balanced |
| branch | **`exp/perf` @ `1c79ba968`** (production code = `5add60bbe`), Release build, 0 errors |
| the node exe | **`C:\Program Files\dotnet\dotnet.exe`** running `Hrot.ClusterRunner.dll --headless` (`scripts/cluster_launch.py` starts it through `dotnet`, not the apphost), with `CREATE_NO_WINDOW` — a "hidden" node is a process with **no window at all** |
| rig | `scale-rig.py --terrain demo-city --men 1000 --vehicles 50 --aircraft 10 --warmup 30 --seconds 30 --pin`, unrecorded, `HROT_QUERY_TIMINGS=0`; real-time factor 1.0 everywhere |
| ⚠ **the exemption** | `powercfg /powerthrottling disable /path …` **needs an elevated prompt** — this session is not elevated (*"This command requires administrator privileges"*). ⇒ used the per-PROCESS equivalent instead, right after each node started: `SetProcessInformation(ProcessPowerThrottling, {ControlMask = EXECUTION_SPEED \| IGNORE_TIMER_RESOLUTION, StateMask = 0})` = "never throttle" (HighQoS); all three nodes, every call returned success. It lives and dies with the process ⇒ **nothing to undo** (no `powercfg` exemption was ever registered) |
| console columns | the wrapper starts the nodes with `CREATE_NEW_CONSOLE` instead of `CREATE_NO_WINDOW` (a classic console window, logs still to files): minimised = `SW_SHOWMINNOACTIVE`; shown = `SW_SHOWNOACTIVATE` + restore + `SetForegroundWindow` on CGF's console |
| also read | CGF / SimHost process CPU time over 15 s inside the measured window; `typeperf \Processor Information(_Total)\% Processor Performance` (% of base clock) over the same 15 s; the foreground window each second |

## 2. The matrix

| # | how the nodes run | throttling | CGF ms · fps | SimHost ms · fps | CGF cores | CPU % of base clock |
|---|---|---|---|---|---|---|
| **T-1** | hidden (as every run so far) | default | **228.3 · 9.8** | 100.6 · 21.5 | 1.19 | 80 |
| **T-2** | hidden | ⭐ opted out | **120.9 · 19.3** | 43.9 · 44.8 | 1.13 | 119 |
| extra | hidden | EcoQoS forced ON | 238.0 · 9.5 | 102.2 · 22.0 | 1.17 | 81 |
| **T-4** | console, minimised | default | **234.6 · 9.8** | 94.4 · 22.9 | 1.17 | 80 |
| **T-5** | console, minimised | ⭐ opted out | **119.8 · 19.2** | 43.6 · 44.3 | 1.15 | 120 |
| **T-3** | console, SHOWN (not foreground — §4) | default | **127.9 · 18.5** | 49.9 · 39.2 | 1.18 | 127 |
| T-1 repeat | hidden | default | 222.1 · 10.2 | 89.6 · 23.7 | 1.18 | 80 |
| T-2 repeat | hidden | opted out | 122.7 · 18.6 | 44.2 · 43.9 | 1.14 | 121 |

**Noise:** T-1 228.3 / 222.1 (6 ms) · T-2 120.9 / 122.7 (2 ms). The effect (~100 ms on CGF, ~50 ms on SimHost) is 15× the noise. Columns ran in the
order T-1, T-4, T-2, extra, T-5, T-3, T-1, T-2 (21:09–21:21) — throttled and unthrottled alternate, so it is not drift.

## 3. What this means for every earlier Windows number

⚠⚠ **Every Windows measurement before this one ran hidden ⇒ throttled.** POC 1 / POC 2 / `exp/perf` / the scaling curves / the egress anomaly:
their absolute ms are ~2× what an unthrottled node does. Ratios inside one run (speed-ups vs serial) were all measured under the same throttling and
probably hold, but are not re-measured here. ⚠ Not proven, but worth checking: the egress anomaly (252 → 337 ms, not reproducible) and that
evening's ~30 ms noise fit a node whose EcoQoS state or clock varied between runs.

## 4. Failed / limited

| | |
|---|---|
| ⚠ **T-3 "foreground" did not take** | the CGF console was shown (restored, not minimised), but `SetForegroundWindow` from the launcher was refused by Windows' focus rules — the foreground sample said "another application" in all 15 seconds. ⇒ T-3 measures a **visible, background** console. It already runs unthrottled, so the foreground case can only be the same or faster |
| ⚠ **`powercfg` exemption not used** | needs admin (§1). The per-process opt-out is the same switch, set by a different route. To check the `powercfg` route: in an elevated prompt `powercfg /powerthrottling disable /path "C:\Program Files\dotnet\dotnet.exe"`, rerun T-1, then `powercfg /powerthrottling reset /path "C:\Program Files\dotnet\dotnet.exe"` |
| ⚠ **T-6 battery not run** | the machine is on AC; unplugging is the operator's call. Plan: Balanced |
| ✅ no faults | every column has a result; real-time factor 1.0 |

---
_ui session on the Windows machine, 2026-11-12_
