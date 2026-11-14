<!--STATUS
state: LIVE — K-1…K-5 done; VERDICT at the top
updated: 2026-11-12
current-answer: ⭐ "K-5 VERDICT" (top) · then the evidence per item
related-designs:
  - ../../DESIGN_Threaded_Work_Inventory.md — OWNS the overnight plan these measurements finish (§2 run controls / tick rate / hot reload: updated from this file)
  - ../../DESIGN_Parallel_Simulation.md — §18.1 / §18.2 per-entity parallelism (K-2 adds to the census), R-4213 (K-4)
  - ../../DESIGN_Threaded_Simulation_Presentation.md — §14 the target shape (K-1's finding on the orchestration bus belongs to §14.1's commands row)
  - HANDOFF_Coordinator_To_Backend_Pre_Build_Measurements.md — the handoff (CE-4450, dispatched at 89883010a)
  - STATUS_Entity_Access_Census.md (on exp/entity-census) — the first census K-2 is compared with
-->

# STATUS — the last measurements before the overnight threaded + parallel build (branch `exp/measure-2`, CE-4450)

**Branch:** `exp/measure-2` = `exp/threaded-poc-4@251c68538` + `exp/entity-census@1769bfc0a` + `coordinator@9d7253663`. **Machine:** the cloud VM
(4 cores; software GL — the drawing frame is ~130–150 ms here, 15.6 ms on Windows). Every instrument opt-in; K-4 changes a default (R-4213).

## ⭐ K-5 VERDICT — **three things change the overnight plan; one does not need to**

| # | settled | ⇒ the overnight plan |
|---|---|---|
| **K-1** | ⭐ the worker spends **150–175 ms per tick waiting to ACQUIRE the world lock playing, 82–106 ms paused** (work 270–400 / 40–55 ms; the sync's hold only 13–17 / 3 ms) — the prediction (100–130 ms) holds. ⚠ But on the VM that wait is **≈ ONE drawing frame**, not starvation over many: the main thread holds the world for ~its whole frame (here 130–150 ms), and the worker gets it at the next exit (2–3 main exits per tick while it waits). ⇒ the **fair hand-over changed nothing on the VM** (acquire 139–170 / 75–145 ms, same ~2.1 Hz). On Windows the frame is 15.6 ms, so a ~100 ms wait THERE is many frames = genuine unfairness, which the fair hand-over would fix — ⭐ **the Windows run decides** (command below) | ⭐ the remedy that works on BOTH: **shorten the main thread's hold** — release before `EndDrawing` / draw from the snapshot (D-4). Keep `HROT_FAIR_HANDOVER` only if Windows shows it |
| **K-1 ⛔ new** | ⭐ **a race the guard cannot see: the ORCHESTRATION bus.** The worker's node frame publishes to and swaps `_node.EventBus` OUTSIDE the world lock; the main thread READS it (`DrainStagingRemap` → "Collection was modified", **3 of 3 runs** once the hand-over was fair) and the UI PUBLISHES onto it (time commands, preview / master controls, intents). The starved worker HID it (0 of 4 baseline runs) | ⭐ **a blocking item before any hand-over change**: the orchestration bus becomes a COMMAND queue for the UI (§14.1 row 5) and its readers move to the worker or read a published copy. Patched for this measurement only (the read inside the worker's locked sync) |
| **K-3** | ⭐ the hot-reload **DRAIN** (applying the rebuilt behaviours: registrars, staging commits) **blocks the node frame 3.7–4.4 s** — the same OFF (main thread) and `=3` (worker); the master ejects a node silent for **5 s** (`HeartbeatTimeoutSeconds`), an eject removes it from the in-sync set (`EjectNode` → `ForgetSync`) and a node coming back while the cluster OPERATES is not re-admitted (`AdmitJoinedNode`: Idle only) — exactly POC 3's symptom. The margin is 0.6–1.3 s; POC 3's run had the world GUARD on (every frame slower). ⚠ The eject itself did not reproduce here (OFF, `=3`, `=3` + guard: in-sync never changed, edit and save after the reload worked) | ⭐ **not threading** — fix the drain: build the staging off the frame (the registrar invocation on the background thread that loads the assembly), keep only the swap on the frame; or hold the heartbeat during a reload. ⛔ The inventory's "the build blocks the frame — refuted" stays true: it is the DRAIN, not the build |
| **K-4** | ⭐ R-4213 built: the per-query timer is **OFF by default**; BrainTick **59.0 → 54.4 ms (−8 %)**, CGF frame 159 → 155 ms, garbage 501 → 487 KB / frame; the rig's query table still produced when it asks (19 rows vs 0); rails 5 / 5 | done — the overnight build inherits it |
| **K-2** | combat ~2× heavier (hit marks 15 399 → 31 422): **no change to the census verdict** — CGF: 2 new cross-entity writes (`CommandOrderHistory` from the brain's order journal; `SquadMemberSense`, squad-level), 1 new last-tick type (`CoverClaim`); SimHost: 2 more systems seen, no new cross writes, 2 new last-tick types (`BallisticProjectile`, `NetworkIdentity`). The masked last-tick copy, now 21 types on the CGF: **2.6–2.7 ms** (a full copy 10.8–11.3 ms); SimHost 19 types 0.7 ms | §18's conversion list gains the order journal (already a command candidate) and squad sense (per-squad jobs) |

⭐ **The Windows command** (8 cores, every window hidden; the K-1 line prints every 5 s):
`git checkout exp/measure-2 && dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj -c Release && python scripts/threaded-poc2-measure.py --out <dir> --mode 3 --no-guard`
then the same with `HROT_FAIR_HANDOVER=1` set; read `[K-1]` (work · lock acquire · sync hold · yield, PLAYING / PAUSED) and `[X-4F]` from `editor-*.log`.

## The evidence

### K-1 — the threaded tick, split *(`EditorSubsystem.KernelWorkerLoop`, `ThreadedKernelGate.WorkerEnter / WorkerExit`; `HROT_FAIR_HANDOVER=1`)*

| per tick (VM, 1 000 men, `=3`, 60 s playing then stepping) | base (2 runs) | fair hand-over (after the bus patch) |
|---|---|---|
| work (`TickNodeFrame`), playing / paused | 273–405 / 39–53 ms | 284–352 / 38–55 ms |
| ⭐ lock ACQUIRE, playing / paused | **147–175 / 82–106 ms** | 139–170 / 75–145 ms |
| sync hold, playing / paused | 13–17 / 2.5–4.4 ms | 14–17 / 2.6–5.5 ms |
| yield (the worker letting the main thread in), paused | 6–25 ms | 1–17 ms |
| main exits while the worker waited | ~2–3 per tick | ~2–3 per tick (hand-over waits ≈ 1 per tick) |
| ticks playing | ~10–11 per 5 s (~2.1 Hz) | ~10 per 5 s |
| drawing frames with the world (`[X-4F]`) | 129–158 ms | 130–152 ms |
| fair hand-over BEFORE the bus patch | — | ⛔ 3 / 3 crashed at load: `DrainStagingRemap` "Collection was modified" (acquire had dropped to ~20 ms, drawing frames 24–26 ms during the load) |

### K-2 — the census on heavier combat *(`--patrol 80`, 90 s measured after a 25 s warm-up, `HROT_SIM_THREADS=1`)* — tables `entity-census-report.py`; compared with the first census

### K-3 — the hot-reload desync *(`HROT_INSYNC_LOG=1`: every in-sync change with its cause; the drain timed where the node frame calls it)*

| run (stages load, play, edits, hotreload, edit2, save) | in-sync changes | drain | edit + save after the reload |
|---|---|---|---|
| OFF | only "+1 joined at rest" | 4.1 s (load) · 3.7 s (reload), main thread | ✅ |
| `=3` | same | 4.4 s · 3.8 s, the worker | ✅ |
| `=3` + world guard | same | 4.1 s, the worker; node frames 80–670 ms with the guard | ✅ |

### K-4 — the per-query timer off by default *(R-4213)*

`QueryTimings.Enabled` = `HROT_QUERY_TIMINGS == "1"`; `/diagnostics/queries?enable=true|false`; `scale-rig.py`, `parallel-sim-measure.py`,
`threaded-poc2-measure.py` set it. Rails: `QueryTimingsTests` (now switch it on for themselves) + `R4213_TheTimerIsOffByDefault_AndOffItCountsNothing` — 5 / 5.
