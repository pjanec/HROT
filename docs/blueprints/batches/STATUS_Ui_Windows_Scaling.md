<!--STATUS
state: LIVE — measured, done
updated: 2026-11-12
current-answer: the whole file (§0 is the verdict)
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — OWNS the census and its prize model (§18 / §18.1, which assumes perfect scaling inside a system); §11.1 the previous Windows numbers
  - HANDOFF_Coordinator_To_Ui_Windows_Scaling_Measure.md (on `coordinator`, dispatched at 89883010a, CE-4451) — the handoff this answers
  - STATUS_Ui_Windows_Perf.md — the previous Windows run on this branch (pinned only)
-->

# STATUS — does a parallel system keep scaling past 4 threads? (CE-4451, Windows, 8 cores / 16 logical)

Measure only. The only change is a measurement-script option, `--cgf-cpus N`, added to `scale-rig.py` and passed through by `parallel-sim-measure.py`. Every process ran hidden. Runs went one after another.

## 0. Verdict

| | |
|---|---|
| ⭐⭐ **the flattening is NOT the pin** | with CGF on **6 whole cores of its own** (S-3) the curve is the same as on the 7-CPU pin (S-1): whole frame at 8 threads **151.2 vs 150.2 ms**, BrainTick **43.1 vs 41.9 ms**. Giving CGF more CPUs bought nothing past 6 threads |
| ⭐⭐ **BrainTick has a serial floor of ~36 ms** | S-3 BrainTick 90.6 → 66.0 → 46.8 → 44.1 → 43.1 → 39.6 ms at 1/2/4/6/8/12 threads (**2.3× at 12**). Fitting `s + p/N` to 4 and 12 threads: **s ≈ 36 ms serial, p ≈ 43 ms parallel**. ⇒ the pre-score part scales; the serial walk after it does not. ⚠ A model fit, not a measurement — the pre-score has no timer of its own |
| ⭐ **per system, own cores (S-3), 1 → 12 threads** | **Threat 7.7 → 1.6 ms (4.7×)**, still falling · **Stress 17.4 → 6.4 ms (2.7×)**, flat from 6 · **BrainTick 2.3×** · **Callouts no gain** (15.2 → 16.3) |
| ⭐ **unpinned (S-2) is SLOWER, not faster** | serial **326.5 / 328.0 ms** (vs 229 pinned, 226 CGF-alone); 8 threads **193.7 / 187.0** (vs 150). SimHost slows too (116–136 ms vs 85–99). The unpinned speed-ups look larger (BrainTick 3.7× at 16) only because the 1-thread baseline is inflated. ⇒ on this laptop, three nodes left to the Windows scheduler contend; pinning helps every node |
| ⚠ **for the prize model** | "perfect scaling inside a system" holds for Threat only. Stress caps at ~2.5–2.7×, BrainTick at ~2.1–2.3× (its serial walk), Callouts at 1×. Whole CGF frame best case **1.50× vs 1 thread** (S-3, 12 threads) |

## 1. Machine, branch, commands

| | |
|---|---|
| CPU | AMD Ryzen 7 6800H — 8 cores / 16 logical (SMT pairs 2k, 2k+1), 31 GB, Windows 11 laptop |
| branch | **`exp/perf` @ `5add60bbe`**, Release `Hrot.ClusterRunner`, 0 errors |
| every run | `HROT_HIDDEN_WINDOW=1 HROT_QUERY_TIMINGS=0 HROT_BRAIN_PRESCORE=1`, Throughput, unrecorded; demo-city 1 000 men / 50 vehicles / 10 aircraft, warm-up 30 s, measured 30 s; real-time factor 1.0 everywhere |

```
S-1  python scripts/parallel-sim-measure.py --pin --threads 1 2 4 6 8 --mode throughput --out <scratch>/S1-pinned
S-2  python scripts/parallel-sim-measure.py --threads 1 2 4 6 8 12 16 --mode throughput --out <scratch>/S2-unpinned
S-3  python scripts/parallel-sim-measure.py --cgf-cpus 12 --threads 1 2 4 6 8 12 --mode throughput --out <scratch>/S3-cgf-alone
noise python scripts/parallel-sim-measure.py --threads 8 --mode throughput --out <scratch>/S2-noise
```

| run | CPUs (logical) |
|---|---|
| S-1 `--pin` | orchestrator 2 · **cgf 7** (CPUs 2–8, 3.5 cores) · simhost 7 |
| S-2 unpinned | every node may use all 16 |
| **S-3 `--cgf-cpus 12`** (new) | **cgf alone on CPUs 4–15 = 6 whole cores**; orchestrator + simhost share CPUs 0–3 (2 cores). ⚠ "Idle as far as the rig allows": they cannot be stopped — CGF needs them, and SimHost still ran its ~85–110 ms frame on its 2 cores — but they cannot take CGF's CPUs |

`HROT_SIM_THREADS` sets the pool's budget directly and has no cores − 2 cap (`FDP/Engine/Fdp.Core/Jobs/FdpWorkerPool.cs:33`), so 12 and 16 threads are real thread counts.

## 2. The curves — ms per frame (CGF node), speed-up vs 1 thread

**S-1 pinned (7 CPUs)**

| | serial | 1 | 2 | 4 | 6 | 8 |
|---|---|---|---|---|---|---|
| whole frame | 229.0 | 213.4 | 197.2 | 161.9 | 150.6 | **150.2 (1.42×)** |
| BrainTick | 95.6 | 88.2 | 74.7 | 45.8 | 41.2 | **41.9 (2.11×)** |
| Stress | 17.0 | 16.9 | 14.1 | 8.8 | 7.1 | **6.7 (2.53×)** |
| Threat | 7.3 | 7.2 | 5.7 | 3.1 | 2.3 | **2.1 (3.40×)** |
| Callouts | 15.5 | 13.6 | 13.5 | 16.2 | 15.0 | 14.3 |
| fps · CGF threads | 9.8 · 27 | 10.4 · 27 | 11.1 · 28 | 13.1 · 30 | 14.0 · 32 | 14.3 · 34 |

**S-2 unpinned (16 CPUs, shared with the other two nodes)**

| | serial | 1 | 2 | 4 | 6 | 8 | 12 | 16 |
|---|---|---|---|---|---|---|---|---|
| whole frame | 326.5 | 330.5 | 240.0 | 205.0 | 197.1 | 193.7 | 187.1 | **185.5 (1.78×)** |
| BrainTick | 125.3 | 128.9 | 74.6 | 49.8 | 41.8 | 40.0 | 36.5 | **34.7 (3.72×)** |
| Stress | 20.5 | 20.9 | 12.4 | 8.4 | 7.2 | 6.9 | 7.3 | 7.7 (2.72×) |
| Threat | 8.3 | 8.1 | 4.7 | 2.9 | 2.1 | 1.9 | 1.5 | **1.4 (5.86×)** |
| Callouts | 16.7 | 17.3 | 16.9 | 16.4 | 18.1 | 17.2 | 16.6 | 18.1 |
| WeaponDispatcher | 34.4 | 35.5 | 25.5 | 25.6 | 26.4 | 26.5 | 26.3 | 24.8 |
| fps · CGF threads | 7.4 · 28 | 7.3 · 28 | 9.7 · 29 | 11.0 · 31 | 11.4 · 33 | 11.2 · 34 | 11.9 · 39 | 11.9 · 44 |

**S-3 CGF alone (6 whole cores)**

| | serial | 1 | 2 | 4 | 6 | 8 | 12 |
|---|---|---|---|---|---|---|---|
| whole frame | 226.3 | 219.3 | 185.8 | 157.7 | 150.6 | 151.2 | **146.2 (1.50×)** |
| BrainTick | 93.0 | 90.6 | 66.0 | 46.8 | 44.1 | 43.1 | **39.6 (2.29×)** |
| Stress | 17.9 | 17.4 | 15.1 | 9.4 | 7.4 | 6.6 | **6.4 (2.74×)** |
| Threat | 7.3 | 7.7 | 5.8 | 3.3 | 2.4 | 1.8 | **1.6 (4.68×)** |
| Callouts | 16.5 | 15.2 | 14.9 | 14.2 | 14.6 | 15.9 | 16.3 |
| fps · CGF threads | 9.9 · 27 | 10.1 · 27 | 11.2 · 28 | 13.2 · 30 | 14.2 · 32 | 14.4 · 34 | 14.5 · 38 |
| SimHost ms (on 2 cores) | 95.3 | 108.6 | 87.8 | 86.9 | 90.4 | 84.8 | 76.8 |

**The pre-score (S-4).** It has no timer of its own: `UtilityPrescore.Run` (`BrainTickSystem.cs:176`) is inside BrainTick, and `HROT_QUERY_TIMINGS=0` removes the query counters too. The `s + p/N` fit is the only split available. On S-3, 4 and 12 threads give s ≈ 36 ms, p ≈ 43 ms. On S-2, 8 and 16 threads give s ≈ 29 ms, p ≈ 85 ms; that fit is inflated by contention.

## 3. Noise

| repeat | first | second | spread |
|---|---|---|---|
| S-2 serial | 326.5 | 328.0 | 1.5 ms |
| S-2 8 threads | 193.7 | 187.0 | 6.7 ms |
| S-2 8 threads BrainTick | 40.0 | 38.0 | 2.0 ms |

Pinned serial on this branch: 229.0 (S-1) vs 226.3 (S-3). A whole-frame difference under ~7 ms is not a result.

## 4. Failed / limited

| | |
|---|---|
| ⚠ **S-5 not done** | `threaded-poc2-measure.py` is not on `exp/perf` (it lives on `exp/threaded-poc-3`). The decimal-comma regex fix belongs there; this report may push to `exp/perf` only |
| ⚠ unpinned is slower for every node | not explained. Likely the scheduler spreading three busy nodes over shared SMT pairs on a laptop; not measured |
| ⚠ S-3's other nodes are not idle | SimHost still runs its frame (77–109 ms) on 2 cores; the orchestrator is light |
| ✅ no faults | every column has a result; real-time factor 1.0; sampled movers 1–7 of 12 (as in every earlier run) |

---
_ui session on the Windows machine, 2026-11-12_
