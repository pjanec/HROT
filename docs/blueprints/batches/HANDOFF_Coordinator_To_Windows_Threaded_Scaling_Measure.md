<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - STATUS_Overnight_Threaded_Build.md (on exp/threaded-build) — the VM numbers this replaces with real cores (§6), the switches, N-14 the EcoQoS opt-out
  - NOTE_Backend_To_Coordinator_Parallel_Switches_And_Throughput.md (on exp/threaded-build) — why a run with no switch shows ~15 % CPU
  - HANDOFF_Coordinator_To_Backend_Mode_And_Serial_Systems.md — the VM lane running in parallel; W-3's ranking feeds its M-4 / M-5
  - ../../DESIGN_Parallel_Simulation.md — §11.1 / §11.2 / §19 the earlier Windows measurements (scaling, throttling)
-->

# HANDOFF — coordinator → WINDOWS session: MEASURE the threaded build on real cores (Release, 1 … 12 threads)

**For:** the Windows session (a place to RUN things — hidden window, `HROT_HIDDEN_WINDOW=1`, always). **From:** coordinator.
**Dispatched at `488a9f15a`**. ⛔ Autonomous: a step that fails is written up and the next step runs — never stop for a question.

## Why the VM numbers are not enough

The overnight build was measured on a **4-core VM in Debug**, with other node processes competing — ⚠ it cannot show scaling past 4 threads,
Debug hides the gain, and the EcoQoS opt-out (N-14) does nothing on Linux. The decisions that wait on real cores: which serial systems to
convert first (the VM lane's M-4 / M-5), whether the threaded editor's UI frame stays flat under load, and whether windowless nodes now run at
full speed.

## Where to work

Check out **`exp/threaded-build`** (head at dispatch or newer), build **Release**:
`dotnet build Hrot/Runner/Hrot.ClusterRunner -c Release`. Write results to a NEW branch **`exp/windows-threaded-measure`** (from the same commit) —
status file `docs/blueprints/batches/STATUS_Windows_Threaded_Scaling.md`. ⛔ Never commit to `exp/threaded-build` (the VM lane works there).
⛔ No screenshots in git. Record the machine once: CPU model, physical / logical cores, P/E split, RAM, power plan, Windows build.

## The measurements

| # | measure | command (Release, hidden window) | report |
|---|---|---|---|
| **W-1** · `CE-4489` | ⭐ **the threaded editor at 1 … 12 threads** (`HROT_THREADED_KERNEL=3`, brains / squads / movement parallel) — 1 000-man demo-city, playing then paused | `python scripts\threaded-scale-measure.py --out <dir> --threads 1,2,4,6,8,12 --dll <Release ClusterRunner.dll>` | per thread count: sim tick (ms), UI frame playing / paused, BrainTick, the 10 heaviest systems, staging MB, working set; ⭐ **total CPU %** of the process (Task Manager / `Get-Counter '\Process(*)\% Processor Time'`, sampled while playing) |
| **W-2** · `CE-4490` | the same with every switch **OFF** (today's editor) at the default threads — the baseline | the same script's off variant, or the editor started with no `HROT_*` switch and the same session | the same columns — before / after |
| **W-3** · `CE-4491` | ⭐ **what is still serial on real cores**: from W-1, each system's ms at 1 and at 8 threads — the ones that do NOT drop are serial; ⭐ the systems inside `TogglableSimulationGroup` listed individually, and `WeaponDispatcherSystem` | from W-1's output (`/diagnostics/architecture`) | the ranked table: system · ms at 1 · ms at 8 · % of the tick at 8 — **this feeds the VM lane's conversion order** |
| **W-4** · `CE-4492` | ⭐ **separate node processes** (orchestrator, CGF, SimHost — the production shape) with `HROT_PARALLEL_SIM=1` at `HROT_SIM_THREADS` 1 / 2 / 4 / 8 / 12, and once with the switch off | `python scripts\scale-rig.py --terrain demo-city --men 1000 --json <f>` with the env set per run | per node and thread count: work per frame, BrainTick, the top systems, peak memory, CPU % |
| **W-5** · `CE-4493` | ⭐ **the EcoQoS opt-out (N-14) works**: windowless CGF and SimHost run at full clock — the CPU's effective frequency on their threads (Process Explorer / `Get-Counter '\Processor Information(*)\% Processor Performance'`) and their frame time; compare with §19's earlier "half speed" numbers | W-4's run, sampled | effective GHz and frame time per node; confirm the opt-out's log line at node start |
| **W-6** · `CE-4494` | the stepped determinism check on Windows (different core count, different scheduler): the stepped 1 000-man run at 1 / 4 / 12 threads | the backend's stepped gate (`stepped-hash.py` / its committed successor on the branch) | equal or not; on a difference, `GET /world/hash?byType=1` and `?rows=<Type>` name what differs — reported, not fixed here |

⭐ **Rerun W-1, W-3 and W-4 once more** after the VM lane pushes its conversions (CE-4484 / CE-4485 ticked on `exp/threaded-build`) — if they
land during this session; otherwise say so and the coordinator dispatches the rerun.

## Rules

Each run warm-up excluded (the script handles it); one run at a time on the machine, nothing else heavy running; pinned vs unpinned only as the
scripts default. Long runs started and left — never poll in a foreground loop. A failing run: one retry, then the error and the log tail in the
status file, and on to the next step.

## Report (top of the status file)

The machine · W-1 and W-2 tables side by side · W-3 ranked list · W-4 per node · W-5 verdict · W-6 verdict · anything surprising, one line each.

---
_coordinator lane, 2026-11-13_
