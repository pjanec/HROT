<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Threaded_Work_Inventory.md — ⭐ §8 the decision list (R-4216 two staging worlds; §8.4 what is still unproven); §7 the critical review
  - ../../DESIGN_Threaded_Simulation_Presentation.md — §14 the target shape
-->

# HANDOFF — coordinator → backend (VM): DAYTIME POC — the two staging worlds, and `--mode all` on one worker

**For:** a backend VM session. **From:** coordinator. **Dispatched at `a0cc3067e`**, scope frozen.
🔒 User, `2026-11-12`: *"we need to measure first, and once we are sure, then we will start the overnight batch"* · R-4216 *"Two-staging worlds.
One is used by the UI and the other is being written by the simulation thread."* ⛔⛔ **Autonomous** (R-4156). This POC exists so the overnight
build meets NO unproven premise — prove or disprove, and say which.

## Where to work
A NEW branch **`exp/poc-staging`** from **`exp/integration`**. Status `docs/blueprints/batches/STATUS_POC_Staging.md` on it. Switch default OFF.

| # | prove | done when |
|---|---|---|
| **S-1** | ⭐ the two staging worlds (R-4216) in the threaded editor (`HROT_THREADED_KERNEL=3`): after each tick the sim copies the masked state (tables + rows + singletons per §13 / §14.1) into staging B1 or B2 — never waiting; an atomic "latest complete" index; the UI, at its frame start, pulls the latest complete into A only when it is newer; the old lock between the threads REMOVED | the 1 000-man scripted session (`threaded-poc2-measure.py --mode 3`) green twice, 0 faults; ⭐ the worker's lock-acquire time (K-1's split) ≈ 0; the sim's tick period and the UI frame, playing and paused |
| **S-2** | ⭐ events, logs (shots, detonations, squad reports), edit outcomes queued per tick with each staging world and drained into A IN ORDER, none lost, none twice — also when the UI skips several ticks (a slow frame) | a rail: N ticks published faster than the UI pulls ⇒ A receives every event exactly once, in tick order; red-proved |
| **S-3** | memory: the two staging worlds at 1 000 men (`HROT_MEMORY_CENSUS=1`) | the MB per staging world; the editor's total vs before |
| **S-4** | the ORCHESTRATION bus (`_node.EventBus`) as a command queue — the race K-1 found ("Collection was modified") | the UI publishes / reads only through a thread-safe queue; the K-1 repro (fair hand-over or S-1) 0 of 5 |
| **S-5** | ⭐ `--mode all` (R-4214 ②): every kernel of the process on the ONE worker, in sequence; one sim job queue per kernel; the shared statics (`ParallelEntities.Mode`, `FdpWorkerPool`, timers) no longer contended | `--mode all` at 1 000 men threaded runs 2 min, 0 faults; each node's tick period |
| **S-6** | ⭐ the verdict: what is proven, what failed and why, what the overnight build must do differently | top of the status file; folded into inventory §8 |

Rules: the touched features' OWN rails only, no broad suites; 0 B per steady frame with the switch off (R-2084); slow things in the background,
never polled; ⛔ no screenshots in git.

---
_coordinator lane, 2026-11-12_
