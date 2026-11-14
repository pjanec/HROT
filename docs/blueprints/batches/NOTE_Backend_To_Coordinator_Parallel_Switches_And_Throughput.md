<!--STATUS
state: ANSWERED — by HANDOFF_Coordinator_To_Backend_Mode_And_Serial_Systems (R-4218 / R-4219); D1–D5 built and measured (STATUS_Mode_And_Serial_Systems, Parallel Simulation §18.7)
updated: 2026-11-13
current-answer: §1 – §4
stale-below: none
related-designs:
  - docs/DESIGN_Parallel_Simulation.md — §18.5 / §18.6 own the per-entity rule and the squad phase this note explains
  - docs/DESIGN_Threaded_Simulation_Presentation.md — §14.8 owns the threaded node (HROT_THREADED_KERNEL=3)
  - docs/blueprints/batches/STATUS_Overnight_Threaded_Build.md — the build's gates, decisions D-21…D-43 and numbers
-->

# NOTE — backend → coordinator: the parallel switches, how the per-entity rule works, and what "Throughput" really changes

Branch `exp/threaded-build` (after `3abbb3511`). Written after the user asked why a 1 000-man Windows run showed ~15 % CPU and little gain.

## 1. Everything new is OFF by default — the switches

| switch | what it turns on |
|---|---|
| `HROT_THREADED_KERNEL=3` | the threaded node (editor): the sim on its own worker, the UI draws a published copy and never waits for the tick; the brain and squad phases parallel |
| `HROT_PARALLEL_SIM=1` | the per-entity phases on ANY node (brains, squads, threat, stress, callouts) — the CGF node's lever when nodes are separate processes |
| `HROT_SIM_THREADS=<n>` | the ONE pool's budget (default cores − 2); EQS's solve window now follows it too (`105705de9`) |
| always on | movement (`CarKinematicsSystem`), ray casts, EQS window |

⇒ the user's ~15 % CPU is the expected picture with no switch set. ⚠ Use Release; Debug hides the gain.
📐 Measured (Debug, 4-core VM, 1 000 men, mode 3): sim tick 1 393 / 1 015 / 830 ms at 1 / 2 / 4 threads · BrainTick 352 / 182 / 135 ms ·
UI frame ~45 ms flat. ⚠ The tick does not scale with cores: `TogglableSimulationGroup` (~600 ms) and `WeaponDispatcherSystem` (~200 ms)
are still serial — **the next conversions**. The Windows 1…12 sweep: `python scripts\threaded-scale-measure.py --out <dir> --threads 1,2,4,6,8,12`.

## 2. How the brain parallelism works (the per-entity rule, enforced by the repository)

| access | rule | where |
|---|---|---|
| OWN entity (and its parts; a squad job also its members via `OwnAlso`) | read and value-write DIRECT, no buffer | `EntityRepository` hooks, `IsOwnPartOfCurrent` |
| ANOTHER entity, a type written in the phase (declared) | read from the TICK-START VIEW (a shadow copy taken serially before the phase) | `CaptureLastTick` / `EnsureLastTickView`, `ParallelReadCheck.OtherUnmanagedRO` |
| ANOTHER entity, a type nobody writes in the phase | declared stable per job, read live | `IStableReads` |
| a write to another entity · add / remove / create / destroy · a publish | into the BATCH's command buffer, played back serially in batch order after the phase — lands a tick later (R-4212) | `BatchContext`, `ParallelEntities.Run` |
| serial follow-ups (callouts, incoming orders, egress marks, rule messages) | `BatchContext.Later`, in batch order after playback | `IPhaseFollowUp` |
| an undeclared cross read | `HROT_READ_CHECK=1` throws · `=2` lists every one with its site (the survey) | `ParallelReadCheck` |

**Why a buffer per 32-entity BATCH, not per thread:** threads take batches dynamically, so a per-thread buffer holds whatever the schedule
gave it ⇒ playback order (last write wins) would follow the schedule. A batch is a fixed index range ⇒ playback in batch order = entity-index
order at any thread count, with no sort. Contexts are pooled (0 B steady). 32 = the load-balancing grain.

## 3. Throughput mode — what it really changes (measured by reading every consumer)

A search finds exactly TWO readers of `ParallelEntities.Mode`:

| | Deterministic (default) | Throughput (`HROT_SIM_MODE=throughput`) |
|---|---|---|
| ① batch size, every per-entity phase | fixed 32 | `max(32, n / (threads × 4))` |
| ② `UtilityPrescore` (also needs `HROT_BRAIN_PRESCORE=1` + `HROT_PARALLEL_SIM=1`) | off | on — but ONLY for the serial brain walk; skipped whenever brains run as the parallel phase (`!Parallel`, `!Defers`) |

| ⭐ finding | evidence |
|---|---|
| ① is a performance knob, **probably not** a behaviour change: batches are contiguous index ranges merged in batch order, so cross-entity effects land in index order at any batch size | `ParallelEntities.BatchSize` / `RunBatch` — ⚠ **unmeasured**: every determinism run used Deterministic |
| ② is a real semantic change (scores see the world at the phase start, R-4207) but **dormant** whenever brains are parallel | `BrainTickSystem.cs:178`, `UtilityScorer.cs:209/269` |
| ⚠ **nothing switches the mode when stepping starts** — it is read once from the environment; `ParallelEntities.Mode`'s comment ("set by the kernel from the time mode") is wrong | no production writer of `Mode` |
| ⚠ the pre-score and the parallel brain phase are **two mechanisms for one concept** (parallelising the brain work); the phase makes the pre-score unreachable in every parallel configuration | ruling 9 — a candidate to retire, ⛔ not removed ("no rush removals": its record is R-4207, Parallel Simulation §10–§11) |

## 4. Decisions for the coordinator — each with a lean

| # | question | ⭐ lean | what would change it |
|---|---|---|---|
| D1 | measure Throughput's determinism? | **yes** — the stepped 1 000-man gate (`scratchpad/stepped-hash.py`) with `HROT_SIM_MODE=throughput` at 1/2/4/8 (~40 min) | — |
| D2 | if D1 is equal | Throughput is a pure performance knob: fix `Mode`'s comment, nothing else | — |
| D3 | if D1 differs | the kernel forces Deterministic while the clock is stepped (CE-3292's "stepped mode is deterministic" — one line where `_steppedFrame` is computed, + a rail), restores the configured mode in continuous play | if the pre-score is the only difference, fix the pre-score instead |
| D4 | retire `UtilityPrescore`? | **lean yes**, after the design check — it duplicates the per-entity brain phase and is dormant in every parallel configuration | a host that must keep SERIAL brains and still wants the scoring parallel |
| D5 | next performance work | convert the still-serial heavy systems (`TogglableSimulationGroup`'s members, `WeaponDispatcherSystem`) to per-entity phases; run the Windows sweep first to rank them on real cores | the sweep ranks something else first |
