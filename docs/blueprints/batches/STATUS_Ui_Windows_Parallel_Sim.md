<!--STATUS
state: LIVE — measured, done
updated: 2026-11-12
current-answer: the whole file (§0 is the verdict)
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — OWNS the experiment (§7 determinism as a mode, §8 interleave / quantize, §9 as-built, §10 on coordinator: the D-5 / emitter lean)
  - HANDOFF_Coordinator_To_Ui_Windows_Parallel_Sim_Measure.md (on `coordinator`, dispatched at 64099ba1f) — the handoff this answers
  - STATUS_Backend_Parallel_Simulation_Experiment.md — the VM's results (2 threads, ±15 % noise) this completes
-->

# STATUS — the parallel simulation measured on the Windows machine (W-1..W-4)

Measure only, no code changed. Every node ran hidden (`HROT_HIDDEN_WINDOW=1`).

## 0. Verdict

| | |
|---|---|
| ⭐ **whole CGF frame** | serial **242 ms** (mean of 4) → Deterministic 8 threads **226 ms (1.07×)** · Throughput 6 threads **217 ms (1.12×)**. Noise on serial: **235–247 ms (±2.3 %)** ⇒ both gains are real, but small |
| ⭐ **why so small** | the converted systems are ~31 ms of the frame. The brains (`BrainTickSystem`, ~105 ms, **43 %**), `WeaponDispatcherSystem` (~21 ms) and egress (~28 ms) are serial and unchanged |
| ⭐ **the converted systems** | **Stress 16.8 → 6.3–6.9 ms (~2.5×)** — scales to 4 threads, then flat. **Threat 7.2 ms → under the top-8 cut-off at ≥ 2 threads** (≤ ~6.3 ms; the exact value is not in the report, see §5). **Callouts 15.3 → 13.9 ms — no gain beyond the noise** |
| ⭐ **D-5 commit by content** | **no measurable effect.** Live frame +5 ms, recorded frame −7 ms — both inside the noise. `RecorderTickSystem` **19.65 → 19.71 ms (unchanged)**. Recorder garbage **1.61 → 1.53 MB/frame (−5 %)**. Recording file **1.76 → 1.76 MB/s**. ⇒ on this scenario D-5 does not pay for the recorder; the emitter-side fix (design §10.2) is the one that matters |
| ⚠ **pinning** | `--pin` gives CGF **7 logical CPUs ≈ 3.5 physical cores**, not 8 — so "8 threads" is oversubscribed. An extra **unpinned** pair (W-5) gives the same ratio (**1.12×**) on a much slower frame (363 ms serial — the three nodes compete). ⇒ the gain is bounded by the serial 70 %, not by the core count |

## 1. Machine, branch, commands

| | |
|---|---|
| CPU | AMD Ryzen 7 6800H — **8 cores / 16 logical**, 31 GB RAM, Windows 11 (laptop) |
| branch | **`exp/parallel-sim` @ `553e3fc08`** (contains `d424929b0`); Release build of `Hrot.ClusterRunner` — 0 errors |
| pinning | `--pin` ⇒ orchestrator 2 · **cgf 7** · simhost 7 logical CPUs |
| scenario | `scale-rig.py`: demo-city, 1 000 men, 50 vehicles, 10 aircraft, warm-up 30 s, measured 30 s, ~12.5 k entities on CGF |

```
W-1  python scripts/parallel-sim-measure.py --pin
W-2  python scripts/parallel-sim-measure.py --pin --threads 6 --mode throughput
W-3  python scripts/parallel-sim-measure.py --pin --content
W-4  python scripts/parallel-sim-measure.py --pin --threads 6 --mode throughput      (repeat of W-2 — W-1 has no 6-thread column)
W-5  python scripts/parallel-sim-measure.py --threads 8                              (extra: UNPINNED, to check the pinning)
```
Each with `--out <scratch>/W<n>`. ⚠ `parallel-sim-results/` is **not** in `.gitignore` — the raw reports were kept outside the repository.

## 2. W-1 / W-2 — ms per frame, CGF node

W-3 re-ran serial and 1/2/4/8 threads (it is W-1 plus D-5), so every Deterministic column has **two samples**. Values are W-1 / W-3.

| system | serial | 1 thr | 2 thr | 4 thr | 8 thr | 6 thr Throughput (W-2 / W-4) |
|---|---|---|---|---|---|---|
| **whole frame** | 246.3 / 240.6 | 242.9 / 239.2 | 238.0 / 234.4 | 234.7 / 234.9 | **225.3 / 227.3** | **213.1 / 221.3** |
| speed-up vs serial (mean 242.3) | 1.00 | 1.01 | 1.03 | 1.03 | **1.07** | **1.12** |
| frames/s | 9.2 / 9.5 | 9.2 / 9.5 | 9.3 / 9.4 | 9.7 / 9.7 | 9.7 / 9.9 | 10.3 / 10.1 |
| BrainTickSystem | 105.0 / 102.3 | 106.0 / 102.1 | 105.7 / 104.1 | 109.0 / 108.7 | 103.8 / 104.6 | 98.8 / 102.2 |
| **StressSystem** | 17.2 / 17.0 | 16.3 / 17.1 | 13.4 / 13.3 | 8.2 / 8.1 | **6.3 / 6.9** | **6.7 / 6.7** |
| **CgfThreatEvaluationSystem** | 7.1 / 7.3 | 7.6 / 6.9 | < cut-off | < cut-off | < cut-off | < cut-off |
| **CalloutSystem** | 15.0 / 16.3 | 13.1 / 13.4 | 13.4 / 14.6 | 14.9 / 14.3 | 13.6 / 14.3 | 14.5 / 13.6 |
| WeaponDispatcherSystem | 21.8 / 20.8 | 22.1 / 20.7 | 22.7 / 21.9 | 22.1 / 21.6 | 20.9 / 21.6 | 20.6 / 21.1 |
| SquadCoordinationSystem | < cut-off | < cut-off | 6.8 / 7.3 | 6.9 / 6.8 | 6.9 / 6.8 | 6.7 / 6.8 |
| garbage B/frame | 1.13 M / 1.03 M | 1.14 M / 1.13 M | 1.13 M / 1.05 M | 1.06 M / 1.13 M | 1.03 M / 1.04 M | 1.01 M / 1.03 M |

W-2 / W-4 serial: 235.4 / 246.7 ms. The SimHost node runs at 86–100 ms per frame, ~21 fps, in every column.

**W-5, unpinned:** serial **363.1 ms** (6.6 fps) → 8 threads **324.1 ms** (7.4 fps), **1.12×**. Stress 19.9 → 6.9 (2.9×), Callouts 15.4 → 13.3, Brain 140.5 → 133.4, Weapons 49.0 → 39.7.

## 3. W-3 — D-5 commit by content (8 threads, Deterministic)

| | without D-5 | with D-5 (`HROT_CONTENT_VERSION=1`) |
|---|---|---|
| live, whole frame | 227.3 (W-3) / 225.3 (W-1) | 232.6 — **+5 to +7 ms, inside the noise** |
| live, peak working set | 0.91 GB | 1.05 GB (+0.14 GB) |
| **recorded**, whole frame | 249.1 | 241.8 — **−7 ms, inside the noise** |
| recorded, `RecorderTickSystem` | 19.65 ms | 19.71 ms |
| recorded, `RecorderTickSystem` garbage | 1 614 579 B/frame | 1 529 905 B/frame (−5 %) |
| recorded, whole-frame garbage | 2.70 MB | 2.63 MB |
| recording file (`data/exercises/<id>/node_1.fdp`) | 135.6 MB over 77 s = **1.76 MB/s** | 133.9 MB over 76 s = **1.76 MB/s** |

⚠ **The compare's own ms is not isolated.** `ContentVersioning.EndFrame` runs after the kernel's timed section (`FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs:548`) and is not a profiled system, so the rig has no row for it. The only estimate is the live frame delta (+5 to +7 ms), which is the same size as the noise.
⚠ The recording's `meta.json` names `SimHost-1` / `TotalFrames 1` (written at start, never updated). The file can't be attributed to a node from the meta. Read bytes per frame as ≈ 1.76 MB/s ÷ ~9 fps ≈ **190 KB/frame**, the same both ways.

## 4. W-4 — noise

| column | samples | spread |
|---|---|---|
| serial whole frame (W-1, W-2, W-3, W-4) | 246.3 · 235.4 · 240.6 · 246.7 | **11.3 ms (±2.3 %)** |
| 6 threads Throughput (W-2, W-4) | 213.1 · 221.3 | 8.2 ms |
| 8 threads Deterministic (W-1, W-3) | 225.3 · 227.3 | 2.0 ms |
| StressSystem, 8 threads | 6.27 · 6.94 | 0.7 ms |

⇒ a whole-frame difference under ~10 ms is not a result on this machine. The Stress gain (~10 ms in a system whose own noise is < 1 ms) is.

## 5. What looked wrong or limited the measurement

| | |
|---|---|
| ⚠ **the per-system sums only see the top 8** | `parallel-sim-measure.py` sums `top_ms`, which `scale-rig.py` cuts at 8 systems. A system that drops below the 8th row prints **0.00**. That's why Threat shows 0.00 at ≥ 2 threads and SquadCoordination 0.00 in serial, and Stress 0.00 in the recorded runs (the recorder took a slot). ⇒ Threat's parallel value is only known to be under ~6.3 ms. The fix is to raise the rig's `top[:8]` for this script — not done (measure only) |
| ⚠ **"8 threads" on 7 pinned logical CPUs** | see §0; W-5 shows the ratio holds unpinned |
| ⚠ **budgets MISS (frame, garbage) in every run** | expected at 1 000 men on CGF (~240 ms/frame, ~1.1 MB garbage/frame). Not caused by the parallel path |
| ⚠ **only 1–4 of 12 sampled movers moved > 1 m** in the window — in **every** column, serial included | not a parallel regression. Flagged for whoever owns the rig |
| ✅ no faults | every node started, every load finished (≈ 10 s), every column has a result, real-time factor 1.0 throughout |

---
_ui session on the Windows machine, 2026-11-12_
