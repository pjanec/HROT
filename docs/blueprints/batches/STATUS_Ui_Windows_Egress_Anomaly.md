<!--STATUS
state: LIVE — measured, done
updated: 2026-11-12
current-answer: the whole file (§0 is the verdict)
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — §11.1 the anomaly as first measured (252 → 337–339 ms); §19 Windows scheduling
  - ../../DESIGN_Threaded_Work_Inventory.md — §7.4 "the off-frame egress alone makes the CGF slower" (unexplained); §7.2 F-15
  - HANDOFF_Coordinator_To_Ui_Windows_Egress_Anomaly.md (on `coordinator`, dispatched at d6f7d0fb2) — the handoff this answers
  - STATUS_Ui_Windows_Perf.md §3a — where the anomaly was measured twice
-->

# STATUS — why did off-frame egress ALONE make the CGF slower? (Windows, 8 cores / 16 logical)

Measure only, no code changed. Every process ran hidden. Columns ran one after another.

## 0. Verdict

| | |
|---|---|
| ⭐⭐ **the anomaly did NOT reproduce** | 14 columns, same production code. With off-frame egress + parallel sync ON, the CGF frame is **266 / 269 / 274 / 279 / 281 / 282 / 284 ms**; serial is **248 / 275 / 277 / 278 / 281 ms**. Both ranges overlap ⇒ **no slowdown beyond tonight's noise (~30 ms)**. The earlier run measured 252 → 337–339 |
| ⭐ **same code** | between `06a3bc69e` (the anomaly run) and `bd06a6d35` (this run) **no production file changed** — only `.md`, tests and the two measurement scripts (`git diff --name-only`) |
| ⭐ **ruled out, each in its own column** | contention with the other nodes (E-2, CGF on 6 cores of its own: 278 serial / 274 both) · the scan's worker count (E-3, `FDP_EGRESS_WORKERS=1`: 269) · the pool budget (E-3, `HROT_SIM_THREADS=4`: 282) · recording (unrecorded: 250) · the query timer (default sampled timer instead of `=0`: serial 281 / 275, both 284 / 279) |
| ⭐ **no CPU contention signal** | the CGF process uses **1.17–1.31 cores** in every column. The egress scan thread (`EgressPipeline.Loop` / `ScanOne`) uses **2–4.5 % of one core**; the main thread ~99 %. Turning both switches on adds ~0.1 core |
| ⚠ **what is left** | not determined. The anomaly run was one afternoon session: whole-frame serial then was **252** vs **275–281** today, and the same session had Claude Code reap a background job for **low system memory**. ⇒ most likely a machine-state effect of that afternoon, not the egress code. ⚠ An inference — it could not be re-created |
| ⭐ **the egress benefit holds** | SimHost **99–107 → 38–54 ms** with the switches on, every column |

## 1. Branch, commands

**`exp/perf` @ `bd06a6d35`** (production code identical to `5add60bbe` and to the anomaly's `06a3bc69e`), the Release build from `5add60bbe`, 0 errors.
Every column: `python scripts/scale-rig.py --terrain demo-city --men 1000 --vehicles 50 --aircraft 10 --warmup 30 --seconds 30 --json <scratch>/<col>.json <args>`
with `HROT_HIDDEN_WINDOW=1 HROT_QUERY_TIMINGS=0` unless stated. "both" = `FDP_EGRESS_OFFFRAME=1 HROT_PARALLEL_SYNC=1`. Recorded (`--record`)
unless stated, like the anomaly run. Driver + sampler are scratch scripts outside the repository.

Under `--pin`, CGF sees 7 CPUs, so the scan ALREADY runs on **one** worker by default (`min(4, 7/4)`, `EgressPipeline.cs:102`) and the pool
budget is 5. The scan thread runs at normal priority (`EgressPipeline.cs` creates it with no priority set) — not BelowNormal.

## 2. The table — CGF ms per frame (`all_ms`, same-named entries summed)

| # | column | args | CGF frame | Brain | Weapons | Ingress | Egress | Launch | CGF threads | SimHost | CGF cores used |
|---|---|---|---|---|---|---|---|---|---|---|---|
| E-1 | serial (smoke run) | `--pin` | 247.8 | 92.9 | 19.4 | — | — | — | 27 | 98.9 | — |
| E-1 | serial | `--pin` | 276.5 | 98.6 | 23.2 | 21.4 | 36.0 | 0 | 26 | 106.9 | 1.18 |
| E-1 | off-frame egress alone | `--pin` | 268.3 | 94.1 | 25.3 | 22.8 | 28.9 | 7.0 | 28 | 42.2 | 1.25 |
| E-1 | parallel sync alone | `--pin` | 274.5 | 109.5 | 23.2 | 17.0 | 34.6 | 0 | 27 | 103.7 | 1.17 |
| E-1 | **both** | `--pin` | **266.2** | 94.2 | 23.8 | 25.1 | 28.6 | 5.4 | 32 | 48.0 | 1.29 |
| E-2 | serial, CGF alone | `--cgf-cpus 12` | 277.7 | 97.0 | 24.3 | 20.5 | 34.9 | 0 | 28 | 105.1 | 1.18 |
| E-2 | **both, CGF alone** | `--cgf-cpus 12` | **273.7** | 95.3 | 23.3 | 22.1 | 28.2 | 17.0 | 38 | 45.6 | 1.31 |
| E-3 | both, `FDP_EGRESS_WORKERS=1` | `--pin` | 269.4 | 95.2 | 24.9 | 23.0 | 30.0 | 5.4 | 31 | 53.6 | 1.30 |
| E-3 | both, `HROT_SIM_THREADS=4` | `--pin` | 281.6 | 92.8 | 24.1 | 27.5 | 28.7 | 20.0 | 31 | 51.9 | 1.25 |
| extra | both, unrecorded | `--pin` | 249.7 | 94.1 | 21.9 | 20.4 | 26.3 | 13.3 | 32 | 43.0 | 1.29 |
| noise | both (repeat) | `--pin` | 281.2 | 108.5 | 25.0 | 22.5 | 29.8 | 5.3 | 33 | 44.8 | 1.31 |
| timer | serial, default timer | `--pin` | 281.4 | 106.7 | 25.0 | 23.6 | 34.7 | 0 | 27 | 101.0 | 1.19 |
| timer | both, default timer | `--pin` | 283.5 | 111.9 | 23.5 | 22.9 | 28.7 | 5.3 | 32 | 52.8 | 1.28 |
| timer | serial, default timer (2nd) | `--pin` | 274.7 | 96.9 | 26.1 | 26.2 | 34.6 | 0 | 28 | 99.8 | 1.21 |
| timer | both, default timer (2nd) | `--pin` | 279.2 | 96.9 | 23.9 | 22.0 | 30.6 | 5.4 | 32 | 37.8 | 1.30 |
| *(anomaly)* | *T-5 alone, 06a3bc69e* | `--pin` | *339.3 / 337.2* | *132.0 / 119.1* | *31.2 / 41.6* | *26.0 / 22.7* | | | | *51.3* | |

**Noise:** serial pinned recorded **247.8 – 281.4** (5 runs), both pinned recorded **266.2 – 283.5** (6 runs). BrainTick varies 93–112 ms run to run
on the SAME settings, which is most of the frame's spread. ⇒ tonight a difference under ~30 ms is not a result. (The scaling run earlier the same
day had a much tighter spread: 1.5 ms on a repeated serial.) Note `Launch` (the replica sync on the main thread, `EgressPipelineLaunchSystem`)
is 5–7 ms pinned but 13–20 ms with CGF on 12 CPUs, with the pool at 4, or unrecorded — the parallel sync's cost depends on the pool it gets.

## 3. E-4 — CPU per thread (CGF process, 15 s inside the measured window)

Sampled with `Get-Process … .Threads` (TotalProcessorTime) at two moments; threads labelled with `dotnet-stack report` (no `dotnet-trace` /
`dotnet-counters` on this machine; nothing installed).

| thread | serial | both |
|---|---|---|
| main (the frame) | 99–100 % | 98–99 % |
| one unlabelled thread (no managed stack — native: GC or DDS) | 13–15 % | 16–19 % |
| egress scan (`EgressPipeline.Loop` / `ScanOne`) | — | **2–4.5 %** |
| pool workers (`FdpWorkerPool.WorkerLoop`), each | — | 1–1.5 % |
| **process total** | **1.17–1.21 cores** | **1.25–1.31 cores** |

⇒ with CGF on 7 pinned CPUs and only ~1.3 of them busy, the scan cannot be starving the frame of CPU. The raw per-thread lists and stack dumps
are in the scratch directory.

## 4. Failed / limited

| | |
|---|---|
| ⚠ the anomaly could not be re-created | the cause is undetermined; §0 names the inference |
| ⚠ tonight's noise is large | ~30 ms on the whole frame; the per-system numbers vary as much |
| ⚠ the CE-4452 throttling measurement (`HANDOFF_Coordinator_To_Ui_Windows_Throttling_Measure.md`) was NOT run before this one | the user asked for this handoff; throttling is still open. All columns here ran hidden, as before |
| ✅ no faults | every column has a result; real-time factor 1.0 |

---
_ui session on the Windows machine, 2026-11-12_
