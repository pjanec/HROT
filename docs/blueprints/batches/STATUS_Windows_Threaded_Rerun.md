<!--STATUS
state: LIVE — measured 2026-11-13
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - HANDOFF_Coordinator_To_Windows_Threaded_Rerun.md (on coordinator) — the order this answers (CE-4511 … CE-4515)
  - STATUS_Windows_Threaded_Scaling.md — ⭐ the FIRST run (before the conversions): every BEFORE number here
  - STATUS_Mode_And_Serial_Systems.md / STATUS_Windows_Findings.md (exp/threaded-build) — what changed between the two runs
  - ../../DESIGN_Threaded_Simulation_Presentation.md — §0a the numbers this updates (not edited here: measure only)
-->

# STATUS — Windows rerun on real cores after the conversions (CE-4511 … CE-4515)

**Built:** `exp/threaded-build` @ **`d8e47f3c0`**, Release (`dotnet build Hrot/Runner/Hrot.ClusterRunner -c Release`, 0 errors). Merged into
`exp/windows-threaded-measure`. Same machine as the first run: Ryzen 7 6800H, 8 cores / 16 threads, Windows 11 26200, **cs-CZ locale**, on AC,
Balanced plan, hidden window, one run at a time. Scenario `scale-rig-demo-city-1000-2s` (1 000 men, ~10 000 live entities). Clock 118–125 % of base
in every run; every process read power-throttling ControlMask 5 / StateMask 0 (opted out).

## Report

### V-1 · `CE-4511` — the threaded editor, `HROT_THREADED_KERNEL=3` + `HROT_PARALLEL_SIM=1`, BEFORE → AFTER

`python scripts\threaded-scale-measure.py --out <dir> --threads 1,2,4,6,8,12 --dll <Release dll>` (through a scratch wrapper that only adds the
CPU / clock / throttling probe; the script's own `[FrameStats]` parser). BEFORE = the first run (`HROT_THREADED_KERNEL=3`, no `PARALLEL_SIM`).

| threads | sim tick ms | UI playing ms | UI paused ms | BrainTick ms | the group ms | EQS ms (background) | cores used |
|---|---|---|---|---|---|---|---|
| 1 | 246.2 → **154.9** | 10.5 → 7.8 | 13.2 → 12.8 | 83 → 66.8 | 239 → 145.4 | 186 → 101.4 | 2.57 → 3.26 |
| 2 | 121.6 → **98.9** | 8.6 → 8.6 | 12.8 → 12.7 | 27.8 → 25.2 | 90 → 73.4 | 50 → 55.5 | 3.57 → 3.78 |
| 4 | 109.2 → **78.3** | 8.8 → 8.9 | 12.9 → 12.9 | 14.9 → 12.3 | 69 → 45.0 | 30 → 29.4 | 4.04 → 4.32 |
| 6 | 108.4 → **74.2** | 9.0 → 9.1 | 13.4 → 12.9 | 11.8 → 8.8 | 68 → 39.4 | 24 → 24.2 | 4.25 → 4.66 |
| 8 | 112.6 → **76.0** | 9.0 → 9.2 | 12.7 → 12.8 | 10.3 → 8.7 | 66.7 → 39.5 | 21 → 21.0 | 4.49 → 4.98 |
| 12 | 110.7 → **75.4** | 9.6 → 9.8 | 12.8 → 12.9 | 9.8 → 8.5 | 65.6 → 38.0 | 18 → 18.2 | 4.85 → 5.56 |

- ⭐ **The tick is ~1.45× faster at 4+ threads** (110 → 75 ms) and **1.9× vs the shipped editor** (every switch off, first run: 144 ms). It still
  stops improving at 4–6 threads.
- **1 thread is no longer slower than the switch off** (154.9 vs 144.4 ms — close; was 246). The group now equals the sum of its 68 members
  (138.5 ms at 1, 39.0 at 8) — the first run's 72 ms gap at 1 thread is gone.
- UI frame unchanged (8–10 ms playing, ~13 paused). Working set 2.24–2.49 GB (peak 2.75–3.08), staging 93 MB — unchanged.

### V-2 · `CE-4512` — what is still serial, ms at 1 → 8 threads (tick at 8 = 76.0 ms)

| system | ms @1 | ms @8 | speed-up | % of tick @8 | first run @8 |
|---|---|---|---|---|---|
| `TogglableSimulationGroup` (the group) | 145.5 | 39.7 | 3.67× | 52 % | 66.7 |
| `EqsSolverSystem` (background, not in the tick) | 101.4 | 21.0 | 4.84× | — | 21 |
| `BrainTickSystem` | 67.2 | 8.7 | 7.69× | 11.5 % | 10.3 |
| ⭐ `WeaponDispatcherSystem` | 10.5 | 5.6 | 1.89× | 7.4 % | 5.7 |
| ⭐ `SquadCoordinationSystem` | 12.8 | 5.0 | 2.56× | 6.6 % | (below top 5) |
| `StressSystem` | 22.2 | 4.3 | 5.23× | 5.6 % | **16.4** |
| `TogglableInputGroup` | 4.4 | 2.8 | 1.59× | 3.7 % | — |
| `TogglablePostSimulationGroup` | 6.9 | 2.7 | 2.59× | 3.5 % | — |
| ⭐ `StaffComposerSystem` | 2.7 | 2.6 | 1.02× (serial) | 3.5 % | 2.8 |
| `CalloutSystem` | 5.5 | 2.5 | 2.26× | 3.2 % | **10.3** |
| `CgfThreatEvaluationSystem` | 8.1 | 1.6 | 5.06× | 2.1 % | **5.9** |

- **Of the first run's top five:** Stress 16.4 → 4.3, Callout 10.3 → 2.5, Threat 5.9 → 1.6 — **converted and scaling.** WeaponDispatcher
  (5.7 → 5.6) and StaffComposer (2.8 → 2.6) **did not move.**
- ⭐ **The next order:** `WeaponDispatcherSystem` 5.6 · `SquadCoordinationSystem` 5.0 · `StaffComposerSystem` 2.6 (flat — fully serial) · then
  a long tail under 1 ms each (~25 ms of the group in members ≤ 1 ms).

### V-3 · `CE-4513` — separate node processes (`scale-rig.py --terrain demo-city --men 1000`), BEFORE → AFTER

CGF work ms per frame (fps); the two `CycloneEgressSystem` passes summed; SimHost work ms and its background EQS.

| run | CGF work ms | CGF fps | BrainTick | Callout | WeaponDisp. | Egress ×2 | SimHost work ms | SimHost EQS | cores CGF / SimHost |
|---|---|---|---|---|---|---|---|---|---|
| switch off | 108.5 → **102.8** | 21.2 → 22.4 | 36.2 → 36.5 | 9.2 → 3.4 | 9.4 → 10.0 | 16.7 → 16.3 | 22.3 → 22.0 | 15.9 → 15.5 | 1.22 / 2.73 |
| t1 | 112.9 → **104.5** | 20.3 → 22.1 | 40.3 → 37.6 | 9.7 → 3.4 | 9.1 → 11.2 | 15.7 → 15.4 | 22.3 → 22.8 | 77.8 → 77.1 | 1.21 / 1.68 |
| t2 | 81.2 → **74.8** | 27.5 → 29.3 | 19.5 → 18.5 | 8.2 → 3.0 | 8.8 → 7.9 | 15.3 → 15.4 | 22.6 → 22.6 | 44.8 → 45.7 | 1.58 / 1.80 |
| t4 | 71.1 → **61.8** | 30.7 → 34.0 | 12.3 → 11.1 | 8.1 → 2.5 | 8.2 → 6.8 | 15.9 → 15.3 | 23.3 → 23.9 | 27.8 → 27.3 | 1.95 / 1.93 |
| t8 | 69.0 → **57.5** | 31.2 → 36.0 | 9.1 → 8.0 | 8.7 → 2.4 | 8.4 → 6.3 | 16.6 → 15.6 | 25.3 → 25.1 | 19.5 → 19.4 | 2.36 / 2.35 |
| t12 | 70.4 → **64.7** | 30.2 → 32.4 | 9.3 → 9.7 | 8.9 → 3.2 | 8.7 → 8.2 | 16.3 → 16.6 | 25.9 → 26.6 | 17.0 → 16.5 | 2.91 / 2.76 |

- ⭐ **CGF at 8 threads: 57.5 ms, 1.79× the switch off** (was 1.57×). Still flat from 4 threads; 12 is worse than 8 in both runs.
- ⭐ **Egress is now the CGF's largest serial cost: ~15.6 ms, unchanged** — the two `CycloneEgressSystem` passes (7.1 + 6.6 ms at t8). Its
  biggest single topic is **`EqsSensorConfigTopic`: 6.2 ms, 28 134 samples** in the window — more than `WorldPos` (0.8 ms, 15 779).
- SimHost frame 22–27 ms whatever the setting (unchanged). Peak memory CGF 0.88–0.95 GB, SimHost 1.22–1.27 GB (unchanged).

### V-4 · `CE-4514` — stepped determinism with the committed gate

`HROT_PARALLEL_SIM=1 python scripts\stepped-hash.py --threads 1,4,12` (default `--chunk 20` = 120 steps) and `--kernel 0 --threads 1,1`.

| session | runs (threads) | sim time at the end | verdict vs run 0 | exit |
|---|---|---|---|---|
| mode 3, first | 1 · 4 · 12 | 2.0 · **2.0167** · 2.0 | 12: ✅ EQUAL · 4: ⛔ 57 types, 1 234 positions, 10 086 vs 10 079 entities | 1 |
| mode 3, retry | 1 · 4 · 12 · 4 | 2.0 · 2.0 · 2.0 · **2.0167** | 4: ✅ EQUAL · 12: ✅ EQUAL · second 4: ⛔ the same 57 types, 10 086 entities | 1 |
| switch off (`--kernel 0`) | 1 · 1 | 2.0167 · 2.0167 | ✅ STATE EQUAL (`EntityGuid` excluded) | 0 |

- ⭐ **Verdict: every run that took the same number of steps is EQUAL, at 1, 4 and 12 threads** (four equal pairs across two sessions), and
  the switch-off product is repeatable. ✅ The first run's "not repeatable" is gone (it was the padding, F-3).
- ⛔ **But the gate fails 2 runs in 7 in mode 3, and the cause is the STEP COUNT, not the thread count:** a failing run ends at sim time
  **2.0167 = 121 steps** where its peers end at 2.0 = 120. Every difference follows from that one extra step: the types are the same 57 both
  times, and the extra 7 entities are what spawned in that step. It hit a 4-thread run both times, but a 4-thread run also passed — 2 hits in 7
  does not show the thread count matters.
- ⭐ **Where the extra step comes from (unmeasured lean):** the switch-off runs ALWAYS end at 2.0167 (121 steps); mode 3 USUALLY ends at 2.0.
  ⇒ entering the preview paused (`/preview/enter {startPaused:true}`) lets one step through before the pause takes hold — always on the serial
  kernel, and only sometimes on the threaded one (a race between the pause and the worker's first tick). The gate should either compare runs
  only at equal `simTime` or read the step counter after `/preview/enter` and step to a fixed total. `--rows --bytes` was not run: the
  difference is a whole step, not bytes.
- ⚠ **Check against F-D2** (`STATUS_Windows_Findings.md`, landed during this run: "a race, rare", 1 of 6 runs differs): if those failing
  runs' `simTime` differs from the baseline's, F-D2's race is this extra step, not anything in the sim. The gate prints `simTime` per run.
- Logs: `%TEMP%\…\wtm\r\v4\k3`, `k3b`, `k0` (scratch, not committed).

### V-5 · `CE-4515` — language-neutral on the Czech machine (R-4221)

The machine's culture is **`cs-CZ`**. Both lines verbatim from this run's logs:

```
[FrameStats] 212 frames  mean 9.4 ms  p50 8.3  p95 17.6  max 22.1  | update 2.2  draw 2.2  ui 4.9 ms/frame  |   (editor, 8 threads, playing)
[Scheduling] power throttling: opted out (execution speed + timer resolution) · console QuickEdit: unchanged (no console, or the call failed)   (editor; CGF and SimHost nodes print the same)
```

- ✅ A decimal POINT everywhere: zero `<digit>,<digit> ms` matches in the editor log or the CGF / SimHost node logs (a node prints e.g.
  `… was 0.8 s`). The nodes print no `[FrameStats]` (the rig does not set `HROT_FRAME_STATS`), so the node check is on their other numbers.
- ✅ The opt-out line exists now and agrees with the external read (ControlMask 5 / StateMask 0 in every process).

## Surprises — one line each

- ⭐ The stepped gate's failures are an EXTRA STEP (121 vs 120) in 2 of 7 mode-3 runs — the preview's paused entry is racy on the threaded kernel; the switch-off kernel always takes the extra step.
- The node at 12 threads is slower than at 8 (64.7 vs 57.5 ms CGF) — as in the first run; 16 logical cores on 8 physical, so 12 workers share cores.
- CPU stays low: the editor uses at most 5.6 of 16 cores; the CGF node 2.9.
- `EqsSensorConfigTopic` is the most expensive topic on the wire from the CGF (6.2 ms, 28 k samples per window) — a configuration topic sent more
  often than positions.
- The logs are written in the OEM code page, not UTF-8: the `[Scheduling]` line's middle dot is byte `0xFA` (CP437/852), so a UTF-8 reader
  shows `�`. Culture is invariant; the output ENCODING is not — cosmetic, but any non-ASCII text in a node log will be mis-read by the scripts.

## Not done

- Nothing skipped. `DESIGN_Threaded_Simulation_Presentation.md` §0a should take V-1's AFTER column; not edited here (measure only).

---
_Windows session, 2026-11-13_
