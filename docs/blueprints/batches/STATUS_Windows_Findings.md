<!--STATUS
state: LIVE — in progress
updated: 2026-11-13
current-answer: the whole file (NEXT is the resume point)
related-designs:
  - HANDOFF_Coordinator_To_Backend_Windows_Findings.md — the work F-1 … F-6 (CE-4501 … CE-4506), dispatched at abc319675
  - STATUS_Mode_And_Serial_Systems.md — M-D9 / M-D10 / M-D12: the stepped gate's differences measured to their cause (padding, one race)
  - ../../DESIGN_Parallel_Simulation.md — §18.7 the as-built gate; §19 Windows scheduling
  - ../DESIGN_Time_Architecture.md — §17 stepped mode waits for background jobs
-->

# STATUS — what the Windows run found: stepped repeatability, the gate, small fixes

Branch `exp/threaded-build`. Started after the mode + serial-systems batch (its M-7 gate already took F-3's root cause — the order the contract allows
when an item is blocked).

## 1. DONE

| item | what | commit |
|---|---|---|
| F-1 `CE-4501` | ⭐ the stepped gate committed: `scripts/stepped-hash.py` — the one session driver (hidden window on Windows, Xvfb on Linux), the field-bytes hash, `--rows <T> --bytes` names the entities and the bytes that differ, `--kernel 0|3`, exit code 0 / 1 / 2 (equal / differs / a run failed) | (this commit) |
| F-5 `CE-4505` | ⭐ R-4221 — no output is ever culture-formatted: `InvariantGlobalization` for every non-test Exe/WinExe, set ONCE in a root `Directory.Build.targets` (⚠ not the props: `OutputType` / `IsTestProject` are unknown there); measured first: no production `new CultureInfo("…")` / `CurrentCulture` / `CurrentUICulture`. The `[FrameStats]` parser is one function in the session driver and reads a decimal POINT only (⛔ never taught a comma). `WindowsScheduling.Describe` — the runner logs what the opt-out did, once | `541e48364` |
| F-3 `CE-4503` · a stepped run is never abandoned on the wall clock | ⭐ a module run dispatched on a STEPPED frame is awaited whole: its output is played back, no timeout reaches its circuit breaker (an overrun is reported once per module); continuous play keeps the hang guard. 📌 Measured: a stepped 8-thread run logged `NavigationSolver timed out after 100ms. Task abandoned` — the step's result depended on the machine's speed. Rail `PausedAndSteppedExecutionTests.F3_ASteppedRun_IsNeverAbandoned…`, red-proved. ⚠ Not THE cause of F-2's differences (F-D2) | `4ac2333ce` |
| F-3 tool · `stepped-hash.py --probe K` | steps in pieces of K and hashes by type after each: names the first piece where a run parts from run 0 | `a44448b89` |
| F-5 · `[FrameStats]` | formats every number invariant by construction (`FormattableString.Invariant`), not only through the process setting | `ba39e4005` |
| F-3 `CE-4503` (root cause, taken in M-7) | ⭐ the stepped run IS repeatable — the differences were the GATE: `/world/hash` hashed raw rows, padding included, and a padding byte keeps whatever the slot held. 📐 Measured with the row bytes (`?rows=<T>&bytes=1`): `ScenarioClock` bytes 10–12, `Utterance` 1–3 / 10–12 — padding only. With the hash over FIELD bytes, two identical switch-off 1 000-man runs: STATE EQUAL. With every phase on, one real race found by the read-check survey (the weapon phase read the target's `WeaponPosture` live) and fixed; the residual `WeaponChannel` difference is `Entity` padding inside an opaque buffer ⇒ CE-4507 | `STATUS_Mode_And_Serial_Systems` M-D9 / M-D10 / M-D12 |

## 2. GATES

| gate | command | result |
|---|---|---|
| F-1 the committed gate | `HROT_PARALLEL_SIM=1 python3 scripts/stepped-hash.py --threads 1,4` (Release, 1 000 men) | STATE EQUAL, exit 0 |
| ⭐ F-2 the repeatability table (`scripts/stepped-hash.py --chunk 40` = 240 steps, 1 000-man demo-city, Release, VM) | `--kernel 0` (every switch off) · `--kernel 3` + `HROT_PARALLEL_SIM=1`, each at 1, 1, 1, 4, 8 threads | **switch off:** three 1-thread runs EQUAL · 4 threads: 22 types differ, 100 positions, the entity count (10 755 vs 10 756) · 8 threads: 19 types, 18 positions — ⚠ **the THREAD COUNT changes the shipped product's stepped world** · **mode 3 + every phase:** 1, 1, 1, 4 EQUAL · 8 threads: 8 types, 15 positions. ⭐ Repeatable at a FIXED thread count in both; the 120-step gates (M-7) were too short to see it |
| F-5 rails | `python3 scripts/test_invariant_culture.py --live <Debug ClusterRunner.dll>` · Core `WindowsScheduling` | **34 executables invariant, 63 test projects not** (red-proved: the targets removed ⇒ the executables listed); **under `cs-CZ` the editor printed `mean 44.1 ms`** (a point); the runtimeconfig carries `System.Globalization.Invariant: true` · 4 / 0; the live log shows `[Scheduling] power throttling: …` |

## 3. DECISIONS

| # | question | decision | where |
|---|---|---|---|
| F-D2 | is the difference SYSTEMATIC or a RACE? | ⭐ **a race, rare.** 📐 Measured, switch off, 240 steps, Release, vs one 1-thread baseline: no diagnostic 4/4/8/4/8/4/8 threads → 1 of 6 differs (19 types), plus F-2's two; with the EQS verify pass (each window solve re-solved inline at its commit) 9 of 9 equal, 0 mismatches. ⇒ the EQS window's solves equal the serial ones every time they were checked; the module timeout (fixed above) logged in 2 runs that stayed EQUAL. The race's first diverging step is being located with `--probe` | this file |
| F-D3 | the coordinator's steer (`STEER_Coordinator_To_Backend_Preview_Extra_Step`): is F-D2 the EXTRA STEP Windows V-4 measured? | ⭐ **not on this VM.** 📐 every one of the 32 stepped runs here (240 steps, 1 / 4 / 8 threads, switch off) ended at `simTime` exactly **4.0** — the diverging ones too (a slipped step reads 4.0167; `simTime` is the world's own clock, `DebugApiService.GetWorldHash`). ⇒ F-D2 is something else here, chased with `--probe`. ⭐ The gate now compares only at equal sim time and exits 2 loudly otherwise (`0ed503430`). ⚠ The paused entry's slip does not reproduce on Linux, so its fix waits for a reproduction (the Windows session, or a rail that forces the window); read so far, without a cause: the master's `PublishClusterPause` (sim time frozen at once), the slave's barrier (`SlaveSyncController`), `/sim/step`'s ack wait (no re-send) | this file |
| F-D1 | what makes the switch-off product depend on the thread count? | ⛔ **REFUTED as stated (F-D2)** — a window solve equals its serial solve (0 mismatches in 9 runs), and `HROT_EQS_WORKERS=1`'s one equal run per thread count was too few for a 1-in-6 race. Original hypothesis: EQS's solve WINDOW — its size is the pool's thread count (`N-16`, `105705de9`), its places are solved ahead against the window-start state and committed in order; with one worker there is no window (the serial path). If a later place's solve depends on what an earlier commit in the same tick wrote (the shared-twin cache, the memory stage), the result depends on the window ⇒ on the thread count. 📐 Test: the same series with `HROT_EQS_WORKERS=1` | `EqsSolverSystem.SolveWindow` |

## 4. OPEN

| item | why | what it needs |
|---|---|---|

## NEXT (the resume point)
F-3: the probe series (`--probe 10`, 1 vs 8 × 4 threads, switch off) names the first step and types where a run parts → the race's source → a fix +
rail → the ≥5-run gate at 1/4/8, switch off and mode 3. Then F-4 (a timing profile: nothing else may run) · F-2 ticked with the final table · F-6.
