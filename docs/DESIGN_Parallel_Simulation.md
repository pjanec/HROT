<!--STATUS
state: LIVE
build-state: DESIGN — options analysed, lean stated; NOT ruled
updated: 2026-11-12
current-answer: ⭐ §8 interleaving and quantizing (do less per frame) · §7 determinism is a switchable MODE (R-4207) and what per-batch buffers cost · §2 the lean · §1 what exists (measured) · §3 the options · §4 the hazards · §5 the staged plan · §6 open
stale-below: nothing
related-designs:
  - blueprints/batches/HANDOFF_Coordinator_To_Backend_Managed_Reads_Audit.md — CE-4445, the managed-read audit and the default flip (R-4210)
  - blueprints/batches/HANDOFF_Coordinator_To_Backend_Parallel_Sim_POC_4.md — CE-4444, POC 4: §13's one pool and the dependency-scheduler measurement
  - blueprints/batches/HANDOFF_Coordinator_To_Backend_Parallel_Sim_POC_2.md — CE-4440, POC 2: the serial 70 % on exp/parallel-sim-2
  - blueprints/batches/HANDOFF_Coordinator_To_Ui_Windows_Parallel_Sim_Measure.md — CE-4437, the 8-core Windows measurement of exp/parallel-sim
  - blueprints/batches/HANDOFF_Coordinator_To_Backend_Parallel_Simulation_Experiment.md — the overnight experiment (branch exp/parallel-sim) that tries §5 / §7 / §8
  - DESIGN_Threaded_Simulation_Presentation.md — the simulation on a worker thread (R-4205); this design parallelises INSIDE that worker's tick — the two add up, and share one core budget (its §11 F-20)
  - DESIGN_Evaluation_Cost_And_Cadence.md — §11h the measured per-system costs at 1 000 men these options target; §3 the brain cadence
  - DESIGN_Scale_Rig.md — §7.7 P-1 parallel EQS (built), the many-core Windows measurements
  - ../FDP/Engine/docs/Parallel-Processing-Production-Implementation.md — the existing ForEachParallel (adaptive batches, zero allocation)
  - ../FDP/Engine/docs/FDP-GDB-SoD-unified.md — FDP-DES-012: background modules on snapshots, commands back
-->

# DESIGN — run the simulation on many cores: parallel systems over many entities

## 0. The request

🔒 User, `2026-11-12`: *"I want to talk about how to parallelize much more than we do now. Single thread calculations of thousands of entities is
ineffective … There are many systems running over many entities. Many of those could be ok with writing stuff via command buffers, allowing parallel
runs. Pls analyze the options."*

## 1. What exists — measured `2026-11-12`

| fact | where |
|---|---|
| ⭐ the scheduler runs a phase's systems ONE AFTER ANOTHER, in `[UpdateAfter]` / `[UpdateBefore]` order — no system-level parallelism | `SystemScheduler.cs:222-231` |
| ⭐ a data-parallel loop EXISTS: `EntityQuery.ForEachParallel` — adaptive batches, a `ParallelHint` (light / medium / heavy), `FdpConfig.MaxDegreeOfParallelism`, pooled batch lists (zero allocation) | `EntityQuery.cs:~400-445`; `FDP/Engine/docs/Parallel-Processing-Production-Implementation.md` |
| ⭐ PER-THREAD command buffers EXIST: `GetCommandBuffer()` returns a `ThreadLocal` buffer; `FlushCommandBuffers()` plays them all back | `EntityRepository.View.cs:16-48`; `EntityCommandBuffer.cs:9-10` ("thread-safe for recording, each thread its own buffer; playback on the main thread") |
| who uses it today: car kinematics, linear kinematics, hull clearance, the raycast solver, the navmesh baker; the EQS solver runs parallel (P-1) | grep `ForEachParallel` / `Parallel.For` |
| ⛔ who does NOT: brains, weapons, stress, callouts, threat, squads — the CGF systems that cost the most | §11h of Evaluation Cost |
| the native event bus's `Write` is lock-free and safe for concurrent writers within a phase (only the end-of-frame swap must not overlap) | `NativeEventStream.cs:88-98` |
| background modules already run on other threads on snapshots, writing commands back (EQS, navigation, traffic) | FDP-DES-012; `EqsModule.cs:39` |

**Where the time goes at 1 000 men** (Evaluation Cost §11h, the user's Windows machine, ms per frame): the synchronous CGF simulation **38** — brains
9.8, weapons 7.3, stress 5.7, callouts 3.7, threat 2.0, squads 1.7, ~25 small ≈ 8 — the PostSimulation phase **9.5** (drone impacts 2.0, car
kinematics 1.7 — already parallel —, fire processing 1.4, map layers 1.1), the background snapshot copies 8.3.

**The write patterns of the hot non-brain systems** (grep of each file):

| system | in-place writes | to whom | bus publishes | loop |
|---|---|---|---|---|
| `StressSystem` | 2 × `Stress` | the man it loops over | 1 (`SensorChangedEvent`) | a cached list of people |
| `CalloutSystem` | 2 × `CalloutState` | the man it loops over | 0 | a cached list of men |
| `WeaponDispatcherSystem` | 6 (`WeaponChannel`, `WeaponState`, barrel heat, `AmmoLoadout`) | the mount / barrel / loadout it loops over | 0 | cached lists |
| `CgfThreatEvaluationSystem`, `SquadCoordinationSystem` | 0 | — | 0 | — |
| `BrainTickSystem` + the behaviours | ~100 sites | mostly the unit's own channels and state; some cross-entity (squad state, objective status, cover claims) | some | the tier queries |

⇒ ⭐ **the hot non-brain systems already have the shape that parallelises: a loop over entities, each writing only itself.** The brain is the hard one.

## 2. The lean

⭐ **Data-parallel loops INSIDE the hot systems (option A), made deterministic, starting with the systems that write only their own entity; the
brains last and behind a guard. System-level parallel scheduling (option B) not now.** Basis: §1 — the loop, the per-thread buffers and the batch
machinery exist; a handful of systems hold most of the cost; the per-entity loops write only their own entity.

| rejected (for now) | the one fact |
|---|---|
| B — run independent SYSTEMS concurrently from declared read / write sets | no system declares its reads and writes today (hundreds to annotate), in-place writes cannot be checked without an analyser, and a few systems dominate the cost — parallelising inside them gains more for less |
| C — move more systems to BACKGROUND modules (snapshot + commands) | each module pays its own snapshot copy (~4 ms each today, §11h) and adds a frame or more of latency; right for slow, lower-rate work (it is how EQS / navigation run), wrong for per-tick systems |
| D — split the men over more CGF NODES (processes) | ⏭ a cluster-level option for very large exercises; ⚠ UNVERIFIED whether more than one CGF node can own a partition of the units today |

## 3. The options in detail

| | A — parallel loops inside a system | B — parallel systems | C — background modules | D — more nodes |
|---|---|---|---|---|
| what runs at once | batches of entities of ONE system | different systems of one phase | a module on a snapshot, alongside the frame | whole simulations in other processes |
| what it needs | per-entity independence; cross-entity writes and structural changes via command buffers; deterministic merge | a read / write declaration per system and a scheduler that builds a parallel graph | the module rewritten to read a snapshot and write commands | ownership partitioning, the network |
| what exists | ✅ `ForEachParallel`, per-thread buffers | ⛔ | ✅ the kernel's async modules | ⚠ to check |
| determinism | ⚠ needs the merge of §4 | ⚠ the same plus ordering of systems | ✅ by slots (Time Architecture §17) | per node |
| cost | small per system (the self-writing ones); large for the brains | large (annotate everything) | medium per module | large |
| gain at 1 000 men (est., 24 cores) | the ~20 ms of weapons / stress / callouts / threat / squads → a few ms; the brains' ~10 → ~1–2 | overlaps systems — bounded by the biggest one | frees the tick, adds latency | scales out |

## 4. The hazards — what makes a system safe to run in parallel

| hazard | why it breaks | remedy |
|---|---|---|
| ⛔ **non-deterministic order** — per-thread buffers and events land in thread order, and which thread gets which batch varies run to run | stepped mode, replay, filming and the scenario regression's goldens require the same result every run — ⚠ only in the Deterministic MODE (§7) | ⭐ per-BATCH buffers (not per-thread), played back in batch order; events written per batch and appended in batch order — the result is then independent of the thread count |
| writes to ANOTHER entity in place | two batches writing one entity race | only through the command buffer (deferred to the batch merge) |
| structural changes (create, destroy, add / remove) mid-loop | invalidates the query | already command-buffer-only |
| shared caches: `Dictionary`, `ConditionalWeakTable`, static lists in systems and helpers | "collection modified", torn state | per-batch scratch, or computed before the loop |
| shared random number generators (2 files use `new Random` / `Random.Shared` in the AI) | not thread-safe; not deterministic across threads | a per-entity stream (seed from entity + tick), the expander's pattern |
| allocation in the parallel body (closures, LINQ) | GC pressure (R-2084) | `ForEachParallel` is zero-allocation; the body must be too |
| the cores — the threaded presentation's main thread (R-4205) and the background modules share the machine | oversubscription, UI stutter on small machines | one parallelism budget (cores − 2 when the presentation is threaded), `FdpConfig.MaxDegreeOfParallelism` |

⭐ **The safety net — a rail per converted system: run it SERIAL and PARALLEL on the same world and compare the world byte for byte**, at several
thread counts. A difference is a hazard the conversion missed. Plus the existing determinism rails and the scenario regression.

## 5. The staged plan *(once ruled)*

| step | what | gain | size |
|---|---|---|---|
| P-0 | ⭐ **deterministic per-batch merge** in `ForEachParallel` (per-batch buffers and event lists, merged in batch order) + the serial-vs-parallel rail harness | makes every later step safe | small, in Fdp.Core |
| P-1 | convert the self-writing hot systems: stress, callouts, weapon dispatcher, threat evaluation, squad coordination, and PostSimulation's drone impacts and fire processing | ~20 of the 47.5 ms → a few ms (to measure) | small each |
| P-2 | the inventory of the brain's writes (~100 sites): self vs cross-entity vs bus vs shared state; a Debug guard that knows "the entity this batch owns" and throws on a direct write to another | makes P-3 checkable | medium |
| P-3 | the brains in parallel: the tier loop over units in batches; cross-entity writes through the batch buffer | ~10 ms → ~1–2 ms | large (the behaviours' nodes) |
| P-4 | the long tail (~25 small systems) by measured cost | the rest | small each |
| later | B (parallel systems) only if, after P-1…P-4, many mid-sized systems remain | — | large |

## 6. Open

| question | why |
|---|---|
| which of the ~100 brain write sites are cross-entity | sizes P-3 |
| does any event consumer depend on the order of events within a frame | the per-batch merge makes order deterministic but different from today's serial order — goldens may need regenerating once |
| can more than one CGF node own a share of the units | option D |
| the parallelism budget with the threaded presentation | §4, Threaded design §11 F-20 |

## 7. ⭐ Determinism is a MODE, not a law — switchable against throughput *(user, `2026-11-12`; R-4207)*

🔒 User, `2026-11-12`: *"If Determinism mode goes against performance mode, these two are not necessary at the same time (determinism good for film
taking and regression test harnesses and special cases like stepped mode, real big scale simulation does not need) — needs to be switchable. What would
per batch buffers mean to performance?"*

| who needs determinism | who does not |
|---|---|
| the film director and its stills · the scenario regression and its goldens · the determinism rails · stepped mode and Run-to (Time Architecture §17, R-3035) · replaying a run to compare it | a big live exercise — 1 000 to 10 000 men at real time, where only throughput matters |

### 7.1 What per-batch buffers cost

⭐ **Almost nothing — the determinism is in the ORDER OF PLAYBACK, not in how work is shared out.** Threads still steal batches dynamically
(load balancing unchanged); each batch writes into its OWN pooled buffer instead of its thread's; at the end the buffers are played back in batch order
instead of thread order.

| | per-thread buffers (throughput) | per-batch buffers (deterministic) | difference |
|---|---|---|---|
| the parallel loop | the same batches, stolen dynamically | the same | none |
| recording a command | append to the thread's buffer | append to the batch's buffer | none |
| buffers alive | one per thread (~cores) | one per batch (~2 × cores, pooled — the adaptive batching targets 2 per core) | a few dozen small pooled objects, 0 B per steady frame once warm |
| playback | buffers in thread order — one serial pass over every command | buffers in batch order — the SAME serial pass | none: the same total commands |
| events | written straight into the bus, concurrently (lock-free) | written into the batch's list, appended to the bus in batch order | ⚠ one extra copy of each event — proportional to the EVENTS, not to the entities (tens to hundreds per tick) |
| a system that writes only its own entity (stress, callouts, weapons) | no buffer at all | no buffer at all | none |

⇒ the per-batch merge costs one copy of the events and a few pooled buffers — microseconds per tick. **It is not where determinism costs
performance.** ⭐ Measure it in P-0 to confirm.

### 7.2 Where determinism DOES cost performance

| deterministic behaviour | its cost | in throughput mode |
|---|---|---|
| ⭐ **stepped mode waits for background jobs on a step** (EQS, navigation — `ModuleHostKernel.cs:678-683`, Time Architecture §17) | the step waits the slowest job (EQS ~58 ms per run at 1 000 men, §11h) | never wait — results land when they land (today's continuous play already does this) |
| slow modules on SIM-time slots | a module can run only at its slot | wall-clock cadence (Scale Rig C-1, built) |
| results independent of the thread count — per-batch order, no parallel floating-point reductions in an unstable order | negligible (7.1); a stable reduction where one sums floats across batches | per-thread order, any reduction order |
| the same random stream every run | a per-entity seeded stream | the same (it is also faster than a shared locked one) |

⇒ ⭐ **the switch matters for the WAITS, not for the buffers.** Most of it already exists as the TIME mode: stepped (deterministic) vs continuous.

### 7.3 The switch

| | ⭐ lean |
|---|---|
| ONE setting, `SimulationMode { Deterministic, Throughput }`, read by the kernel and `ForEachParallel` | one place, not a flag per system |
| ⭐ chosen by WHAT IS RUNNING, not by hand: stepped mode, Run-to, filming, recording a regression, the test harnesses ⇒ Deterministic; continuous live play ⇒ Throughput | the user never has to remember it; stepped mode stays deterministic by construction (R-3035 unchanged) |
| the per-batch merge stays ON in both modes unless P-0 measures a cost | it is nearly free (7.1), and one merge path is simpler than two — ⛔ a second code path only if a number justifies it |
| the throughput mode is still CORRECT, only not repeatable — the same commands apply, possibly in another order | a run in throughput mode is valid; it simply cannot be compared byte for byte |
| the tests run BOTH modes: the serial-vs-parallel rails in Deterministic, a smoke run in Throughput | so the fast mode cannot rot |

## 8. ⭐ Do less every frame — INTERLEAVING and QUANTIZING *(user, `2026-11-12`; a lean, not ruled)*

🔒 User, `2026-11-12`: *"Also writing every frame and calculating every frame is what adds time. Interleaving and quantizing might help."*

Parallelism (§2–§5) makes the same work faster; these two make LESS work. They multiply.

### 8.1 Interleaving — an expensive per-entity system visits 1 / k of its entities each tick

| | |
|---|---|
| the idea | a system whose result changes slowly (stress recovery, callouts, threat evaluation, squad coordination, sight re-checks) updates each entity every k-th tick, the entities staggered by index — the cost per tick drops to 1 / k |
| ✅ precedents that exist | `CadenceGate` — brains and rules ticked `Periodic`, staggered `index % 10`, woken at once by a `SensorChangedEvent`, each slot reading every event since its own last tick (`Behavior/Cadence.cs`, CE-2382) · `DecisionCadence` — the combat decisions re-scored every 0.2 s on the man's own phase (CE-4395) · slow modules on a wall-clock cadence (Scale Rig C-1) · the presentation's `ChangeGate` (CE-1480) |
| ⚠ the trap, MEASURED | a slowed tick must receive the time ELAPSED since that entity's last update, not one frame's dt — ui found the brain's cadenced tick gets one frame's dt, so HSM timers run k times slow (Evaluation Cost §11h P-4) ⇒ ⭐ one rule: an interleaved system integrates `now − lastUpdate[entity]` |
| ⚠ the limit, MEASURED | the brains at 0.2 s saved only ~4 ms (§11h P-4): in a fight the senses wake most soldiers most ticks. Interleaving pays where the WAKES are rare — calm units, far from the fight, idle vehicles — so the period can follow a LEVEL OF DETAIL (near the fight / far / idle) |
| determinism | unchanged — the stagger is by entity index, the period by sim time |
| ⭐ lean | a shared interleave helper (the `CadenceGate` generalised: period, stagger, wake, elapsed dt) adopted by the slow-changing systems one by one, each with its measured saving and a rail that its result stays within a stated error of the every-tick result |

### 8.2 Quantizing — write only when the value MEANINGFULLY changes

| | |
|---|---|
| why a write costs more than the write | ✔ `GetRefRW` stamps the WHOLE 64 KB chunk's version whenever it hands out a writable reference (`NativeChunkTable.cs:158-178`) — whether or not the value then changes. A stamped chunk is then COPIED by every snapshot (the background modules' and the threaded presentation's, Threaded design §11 F-5), DIFFED by the recorder (format v8 block deltas) and SCANNED by egress |
| the idea | ① **check before write** — compare first, take the writable reference only when the value differs (FDP's own parallel doc names it, `Parallel-Processing-Production-Implementation.md` §4) · ② **quantize** slowly drifting floats (stress, suppression, morale, barrel heat, timers) to a step — the value changes only when it crosses a step, so a unit at rest writes nothing |
| ⚠ the limit | a chunk holds 128–4 096 entities: ONE changed man dirties his whole chunk. The gain is in tables where FEW entities change per tick (a calm unit's stress, an idle vehicle's heat), not in positions of a moving army |
| ⚠ what must NOT be quantized | the integrators' own state (positions, velocities, accumulated timers) — quantizing them changes the simulation. ⇒ quantize OBSERVED values, or keep the precise value where the integrator reads it |
| determinism | unchanged if the quantization is deterministic (a fixed step, no thread-dependent rounding) |
| ⭐ lean | MEASURE first which tables would gain, then convert those |

### 8.3 Measure first — one cheap instrument

⭐ **M-1, a per-table counter under `HROT_FRAME_STATS`:** per tick, how many writable references were handed out, how many of those left the value
unchanged (a compare in Debug), and how many chunks ended dirty. ⇒ it names the tables where check-before-write or quantizing pays, and the systems
whose writes are mostly no-ops. Same opt-in, 0 B pattern as ui's `KernelFrameTimings`.

### 8.4 In the plan (§5)

| step | what |
|---|---|
| M-1 | the write instrument (8.3) — with P-0 |
| P-5 | interleave the slow-changing systems by the shared helper, the elapsed-dt rule first; a level-of-detail period (near / far / idle) where M-1 and the wakes say it pays |
| P-6 | check-before-write and quantizing in the tables M-1 names; a rail per table that the observed behaviour stays within its stated error |

## 10. ⭐ After the overnight experiment *(`exp/parallel-sim`, verdict "GO, with three changes" — [status](blueprints/batches/STATUS_Backend_Parallel_Simulation_Experiment.md) on `backend`)*

### 10.1 Cover claims seen one tick late — ruled *(R-4208)*

🔒 User, `2026-11-12`: *"Yes friend may see cover frame One frame late if it can recover from over-allocated covers."*

The experiment found two ORDER-DEPENDENT brain behaviours (cover claims read by friends in the same tick; EQS child sensors created and used at
once). Ruled: in Throughput mode a brain may read its friends' cover claims ONE TICK LATE (double-buffered) — on the condition that an OVER-ALLOCATED
cover recovers: when two units hold the same cover after a commit, one keeps it (a fixed rule — the earlier claim, then the lower id) and the other
re-picks on its next decision. ⭐ The acceptance rail: no cover held by two units for more than a bounded number of ticks, at 1 000 men. Deterministic
mode keeps the brains serial (R-4207).

### 10.2 Why the brains write back unchanged bytes — and the fix *(measured `2026-11-12`)*

M-1 measured the 4 KB blackboard table: ~150–200 of ~170–220 chunks stamped changed per tick, **half of them with identical bytes**. Three causes,
all in how a blueprint is compiled and stored — none is a bug in one behaviour:

| cause | code | effect |
|---|---|---|
| ① the generated tick assigns its state on every pass, whether or not it changed | e.g. `__bb.St.Walking = false;` on every tick the branch runs (`DangerCrossingBp_*_Bp.g.cs`, generated by the blueprint emitter) | a write of the same value |
| ② edge detectors keep their memory by rewriting it every tick | `prev.Above = isAbove ? 1 : 0;` in every When-node (same file, the `WhenNode` block) | same — the value changes only at an edge |
| ③ the change unit is the 64 KB CHUNK, stamped when the blackboard is handed out WRITABLE | `BlueprintTickSystem.cs:101-155` hands every slot a writable span; `GetComponentRW` / the tier's `Memory` stamp the whole chunk (~16 units' blackboards) | the stamp happens before anyone knows if a byte will change; one unit's real change also marks 15 neighbours |

| fix | what it does | lean *(revised `2026-11-12` after the user's question below)* |
|---|---|---|
| ⭐ **the emitter writes only on change and reports it** — `if (x != v) { x = v; wrote = true; }` for state and edge memory; the tick system stamps the chunk only when a slot reports a write | removes ① ② at the source and makes ③'s stamp precise; the check is a few BYTES the generated code already holds, not a 64 KB block; no shadow memory | ⭐ **first** — the cheapest real fix, in one place (the emitter), every blueprint benefits |
| **D-5 commit by content** (prototyped, opt-in) | at frame end a stamped chunk with unchanged bytes gets its previous version back — one engine policy, every writer covered | ⚠ **a safety net, not the fix** — see the cost below |
| a finer change unit (per entity row, not per chunk) | ③'s neighbour effect | ⛔ a kernel-wide change; only if recording / snapshot copies show it |

🔒 User, `2026-11-12`: *"Isn't the checking if the block has actually changed as expensive as actually copying the block?"* — ✔ **yes, and worse.**
The prototype keeps a SHADOW copy of every chunk (`NativeChunkTable.cs` on `exp/parallel-sim`, `CommitByContent`: `_cshadow`, `bytes.SequenceEqual(sh)`,
`bytes.CopyTo(sh)`) ⇒ per stamped chunk: a compare that reads 2 × 64 KB (in full when unchanged, early exit when changed) PLUS a 64 KB copy into the
shadow when it did change, and a second copy of the table in memory. ⇒ It costs about ONE copy per stamped chunk, and pays only where the chunk has
SEVERAL or EXPENSIVE consumers downstream (the recorder's delta, compression and disk; each background module's snapshot; the UI sync) — for one
plain copy it is a wash. ⇒ D-5 stays opt-in for tables with costly consumers (the recorder), measured on Windows (CE-4437 W-3); the fix for the
brains is the emitter.

## 11. ⭐ Aggregate `2026-11-12` — what the three experiments measured, and where the time is *(coordinator)*

Sources: the VM experiment ([status](blueprints/batches/STATUS_Backend_Parallel_Simulation_Experiment.md) on `backend`), the 8-core Windows run
(`STATUS_Ui_Windows_Parallel_Sim.md` on `exp/parallel-sim`), the threaded-analysis instruments (`STATUS_Threaded_Analysis_Instruments.md` and the
threaded design's §13.8, on `exp/threaded-analysis`).

| finding (CGF node, 1 000 men, demo-city, Windows Ryzen 7 6800H unless said) | number |
|---|---|
| the CGF frame, serial | **242 ms** (±2.3 %) |
| parallel (Stress, Threat, Callouts converted) | **1.07×** Deterministic 8 threads · **1.12×** Throughput 6 threads — real but small |
| where the frame goes | ⭐ **brains 105 ms (43 %)** · egress ~28 · weapon dispatcher ~21 · recorder ~20 (when recording; 1.6 MB garbage / frame) · stress 17 · callouts 15 · threat 7 · squads 7 |
| per system | Stress **2.5×** (17 → 6.5 ms, flat beyond 4 threads) · Threat ≥ 1.1× · ⛔ Callouts no gain |
| interleaving Stress (§8.1) | ⛔ **0.3 of ~10 ms** — the per-entity step was not the cost |
| D-5 commit by content | ⛔ **no measurable effect** — recorder 19.65 → 19.71 ms, file 1.76 MB/s both ways ⇒ the emitter fix (§10.2), not D-5 |
| ⭐ managed component READS stamp the chunk version (threaded analysis A1-2: `GetComponent<T>` → `ManagedComponentTable.GetRW`, `ManagedComponentTable.cs:181-196`) | ~438 000 UI reads a run mark chunks changed — ⛔ a race once the UI leaves the tick thread, and ⚠ false changes for the recorder and egress TODAY |

⇒ ⭐ **Converting more small systems cannot move the frame much: the serial 70 % is brains + egress + weapons + recorder.** The next evidence is a
PROFILE of those four (what inside them costs, how much is false change), before any more parallel work.

## 12. The per-query timer is SAMPLED *(R-4209)*

🔒 User, `2026-11-12`: *"sampled timer approved"* — on the proposal: `QueryTimings` (CE-3427) stays ON by default but times 1 call in 16 and scales
the time up; the call counts stay exact. POC 2 measured the always-timed version at ~13 ms of the brain on the VM (37 000 timed calls / frame at
1 000 men: two clock reads + a per-thread record each). Built in [CE-4443](blueprints/batches/HANDOFF_Coordinator_To_Backend_Parallel_Sim_POC_3.md).

## 13. A job system? — what we have, and the lean *(coordinator, `2026-11-12`; user: "other engines use custom job/task systems, why is that, could we benefit?")*

**What exists** (graph `search_graph .*(WorkerPool|JobSystem|JobQueue|Scheduler|ThreadPool|Workers)$` + grep, non-test): ⚠ **FOUR independent
ways to use the cores, each with its own budget:**

| mechanism | used by | code |
|---|---|---|
| `WorkerPool` — persistent, 0 B, "the process's ONE pool" | the EQS solver windows, the off-frame egress scan (CE-3427) | `Fdp.ModuleHost/Threading/WorkerPool.cs` |
| `FdpWorkerPool` — persistent, 0 B, per-batch ordered merge | the parallel systems (stress, threat, callouts, the brain pre-score) | `Fdp.Core/Jobs/FdpWorkerPool.cs` on `exp/parallel-sim*` only — ⛔ a DUPLICATE of the one above (R-shared-and-unified) |
| .NET `Task.Run` / thread pool | background modules, the recorder's I/O, topology rebuilds | `ModuleHostKernel.cs:738`, `AsyncRecorder.cs:224` |
| `Parallel.ForEach` (allocates) | `EntityQuery.ForEachParallel`, the raycast solver | `EntityQuery.cs:444`, `RaycastSolverSystem` |

Plus 8 dedicated `new Thread` sites (egress pipeline, flight-recorder prefetch, SUMO, the id allocator …). ⛔ Systems declare NO component
access (only `BlueprintWritableAttribute`), so the scheduler cannot run two systems at once safely.

**Why engines build their own:** ① one pool sized to the cores — no oversubscription (POC 1 on Windows: "8 threads" on 7 pinned CPUs);
② tiny jobs at no allocation; ③ PRIORITIES — frame-critical work first, background work yields; ④ work stealing for uneven batches; ⑤ jobs with
DEPENDENCIES from declared reads / writes, so independent systems run at the same time and a race is caught when scheduled.

| ⭐ lean | why |
|---|---|
| ⭐ **ONE pool, two priorities** (frame-critical: parallel systems, the sync; background: egress, recorder, EQS, background modules) — merge `FdpWorkerPool` into `WorkerPool`, move `Parallel.ForEach` and the background modules' `Task.Run` onto it | cheap, removes the duplicate and the oversubscription; one core budget (cores − 2) by construction |
| **measure before a dependency scheduler**: the access tracer (threaded-analysis A-1) already records each system's reads / writes — compute which systems are disjoint and the critical path; build declared-access scheduling only if it shows a gain | the brain is 43 % of the frame in ONE system — running the others side by side is bounded by it |
| ⛔ not a Unity-style job system with safety handles and a compiler | .NET has no Burst; the per-batch merge + `ParallelGuard` already give determinism and race checks where they matter |

## 14. Managed component reads stop stamping — the default, after an audit *(R-4210)*

🔒 User, `2026-11-12`: *"yes, queue the audit; the other backend lane is free, if it can be done in parallel with POC4"* — on the lean: a managed
`GetComponent` READ must not bump the chunk version (A1-2); it becomes the DEFAULT once an audit has found and fixed every in-place mutation of a
managed component (already forbidden — CLAUDE.md: managed components are immutable and replaced whole). Both lanes hit it: the threaded editor
(a read off the sim thread is a write race) and the egress visit gate (POC 3 T-2: 0 skips, because `OwnershipEgressSystem`'s reads stamp every
chunk). Built and measured in [CE-4445](blueprints/batches/HANDOFF_Coordinator_To_Backend_Managed_Reads_Audit.md).

### 11.1 Update — `exp/perf` on the 8-core Windows machine *(`STATUS_Ui_Windows_Perf.md` on exp/perf, `2026-11-12`)*

| | serial | everything on | |
|---|---|---|---|
| CGF frame (1 000 men, recorded) | 256 ms (8.8 fps) | **163–173 ms (12.2 fps)** | **1.52×** (POC 2 alone: 1.47×) |
| `BrainTickSystem` | 96 ms | **42 ms** | the pre-score, posture + threat ranking |
| the one worker pool | everything on, 1 thread: 255 ms | at its budget: 173 ms | **the pool buys 81 ms** |
| SimHost with off-frame egress | 96–104 ms | **42–51 ms** | 20 → 40–45 fps |
| the sampled timer · T-2 gate · Q-1 | — | inside the 9 ms noise | the gate still skips 0 (CE-3442) |

⚠ **Two open anomalies:** ① off-frame egress + the parallel replica sync ALONE on a serial CGF frame makes it **slower** (252 → 337–339 ms,
reproduced; brain, weapons, ingress all grow) — invisible with everything on; suspected CPU contention on the 7 pinned logical CPUs, not measured.
② network ingress falls 14 → 3.6 ms with threads though it is not parallelised — unexplained.

## 18. ⭐ Per-entity parallel brains — the proposal and what decides it *(threaded POC 4 session + the user, `2026-11-12`; a lean, not ruled)*

🔒 User, `2026-11-12`: *"todays computers have 16+ physical cores so parallelization is the way to go."* Today only the brains' READ-ONLY scoring is
parallel (the pre-score: BrainTick 96 → 42 ms on Windows); the tree / HSM / action / blueprint execution is serial because it may write OTHER
entities (A-1: BrainTick writes 42 component types, squad-shared ones among them — `UnitRoster`, `SquadCognitiveState`) and a later brain may read them.

**The rule proposed** (the standard ECS discipline): a brain writes its OWN entity directly; anything aimed at another entity is a command in a
per-batch buffer, played in a fixed order after the phase; a read of another entity's brain-written component sees LAST tick's value. Precedents:
R-4208 (cover claims one tick late), `ParallelEntities` per-batch merge (POC 1), `ParallelGuard`.

| what decides it | how to measure | status |
|---|---|---|
| how many cross-entity WRITES, and where (each becomes a command) | a census: BrainTick sets a thread-local "current brain entity"; every component write / read classified own vs other, by type and call site, sampled stacks; one 1 000-man run | ⛔ not measured (A-1 counts per TYPE, not per entity) |
| cross-entity READS of brain-written components (must see last tick) | the same census | ⛔ not measured |
| ⭐ beyond components — the census must also count | ① STRUCTURAL changes, even on the own entity (create / destroy, add / remove: EQS child sensors are created in-tick) ⇒ deferred · ② bus publishes ⇒ per-batch event lists (exist) · ③ shared state outside components: `DecisionCadence`, dedup dictionaries, `BlueprintSharedState`, sim statics (`ObjectiveCensus.s_men`, `MemberEvaluationBehavior.s_current` — A-2 "sim-only scratch, unconfirmed") · ④ entities created this tick and read by a later brain · ⑤ leader → soldier orders within one tick (with commands they arrive a tick later — a behaviour change like R-4208, the user's call) | ⛔ |
| the prize | a trace splitting BrainTick's remaining ~42 ms (Windows) into scoring vs execution | ⛔ |

⭐ **Determinism comes for free with this rule.** The serial order is what makes today's result order-dependent; if every cross-entity effect is a
command merged in a FIXED order and every cross-entity read sees last tick, the result no longer depends on thread count or batch split — parallel
AND deterministic (Jacobi instead of Gauss-Seidel). It is a DIFFERENT result from today's serial one (a friend's claim seen a tick later), so the
regression goldens are re-baselined once — and R-4208's "Throughput only" could extend to Deterministic mode.

**The prize, bounded honestly:** the brains are one link of the measured chain (stress → threat → brain → callouts → weapons → squad, §16 J-3); the
same rule applies to every per-entity system in it, and that — not the brains alone — is what lets 16 cores pay. ⇒ census the brains first; if the
lists are short, the rule becomes the engine's rule for per-entity systems, enforced by `ParallelGuard`.

🔒 User, `2026-11-12`: *"basically every system can be parallelized including the brains, there is nothing why brains should be handled differently,
is it?"* — ✔ the RULE is the same for every per-entity system; the brains differ only in HOW MUCH they touch (42 written types, structural changes,
state outside components). Systems that are not per-entity (a spatial-hash build, ingress / egress, command playback) need a parallel algorithm
of their own (a partitioned build, a reduction), not the rule. ⇒ the census covers EVERY system of the phase, not only the brains
([CE-4449](blueprints/batches/HANDOFF_Coordinator_To_Backend_Entity_Access_Census.md)).

### 18.1 ⭐ The census verdict and the user's three calls *(R-4212, R-4213, `2026-11-12`)*

The census ([status](blueprints/batches/STATUS_Entity_Access_Census.md) on `exp/entity-census`): **GO — the rule is viable for the whole Simulation
phase.** CGF systems: 18 READY · 11 CONVERTIBLE (128 ms VM) · 6 GLOBAL · 36 no per-entity access in the run. Genuine cross-entity writes: callouts /
utterances and the command order journal (everything else was the brain's own child sensors); in-tick child-sensor creation ⇒ deferred; the
last-tick set = 18 types, a masked copy of them costs **CGF 1.9–2.7 ms, SimHost 0.9 ms** (a full copy 5.2–8.5 ms) ⇒ the last-tick view is that small
masked copy (settles D-4's half). Prize (Windows serial 256 ms): 2.05× at 4 cores, 2.5× at 8, 2.8× at 16 — the floor beyond 8 is egress, the
recorder and ingress (per-entity scans too).

🔒 User, `2026-11-12`: *"yes to all three"* — **R-4212:** ① a brain (and any per-entity system) reads other units' in-phase-written state from
LAST tick (the last-tick copy); ② a callout / an order to another entity is a command and reaches its listeners one tick later; ③ squad-level
systems run in parallel per SQUAD, not per man.

🔒 User, `2026-11-12`: *"we do not want a timing taking 12% so whatever helps"* — **R-4213** (supersedes R-4209's "sampled 1 in 16, on by
default"): the census measured the SAMPLED per-query timer still at 11.9 % of BrainTick ⇒ `QueryTimings` is OFF by default in every host; the
scale rig, the measurement scripts and `/diagnostics/queries?enable=1` turn it on when someone measures.

## 19. Windows scheduling: power throttling, hybrid cores, console pauses *(user, `2026-11-12`: "what about windows frontend / backend throttling and intel performance cores vs e-cores … i do not want headless mode running in console to be any slower")*

Measured: graph + grep for `PowerThrottling|SetProcessInformation|timeBeginPeriod|ThreadPriority|PriorityClass|EcoQoS|CpuSet|EfficiencyClass|ProcessorAffinity`
over `FDP` and `Hrot` ⇒ ONE hit (`OverlayCompileTracker.cs:36`, a BelowNormal compile thread). ⇒ ⛔ nothing today opts out of Windows' power
throttling, sets the simulation's thread priorities, or knows performance vs efficiency cores. ⚠ Every Windows measurement so far ran with
HIDDEN windows — a process Windows treats as background may already have been throttled (EcoQoS), so those numbers may be pessimistic.

| risk | what Windows does | ⭐ lean |
|---|---|---|
| background / hidden / console processes slowed | Windows 11 applies EcoQoS (power throttling: lower clocks, efficiency cores) to processes it deems background, and may ignore their timer-resolution requests | every HROT process opts OUT at start-up: `SetProcessInformation(ProcessPowerThrottling)` with `EXECUTION_SPEED` and `IGNORE_TIMER_RESOLUTION` off; per thread for the sim thread and the pool (`SetThreadInformation(ThreadPowerThrottling)`) |
| `Sleep(1)` / waits pace the loop at 15.6 ms | the default timer resolution | `timeBeginPeriod(1)` while a simulation runs (honoured once the opt-out above is in place) |
| background work delays frame work | equal thread priorities | the sim thread AboveNormal; the pool's FRAME lane Normal; its BACKGROUND lane (EQS, egress scan, recorder) BelowNormal |
| ⭐ Intel hybrid (P- + E-cores) | `Environment.ProcessorCount` counts both; a batch on an E-core finishes later and the whole parallel step waits for it | size the frame lane to the PERFORMANCE cores (`GetSystemCpuSetInformation`, `EfficiencyClass`), let the background lane use the efficiency cores; keep adaptive batches (stragglers absorbed). ⚠ The user's machine is AMD (no E-cores) ⇒ this can be designed and unit-tested, measured only on an Intel hybrid machine |
| ⭐ a console node FREEZES when someone clicks in its window | QuickEdit mode: a text selection blocks every write to the console ⇒ a thread that logs (a console line per step) stops, and the simulation with it | console hosts turn QuickEdit OFF at start-up (`SetConsoleMode`, clear `ENABLE_QUICK_EDIT_MODE`) |

### 18.2 ⭐ What goes into the last-tick copy — DECLARED by construction, not "copy all" *(user, `2026-11-12`: "why copying all, even blueprint knows what entity it reads and can say whenever it does what component it reads")*

Measured: the generated behaviour code reads components through TYPED calls only — `GetComponentRO<T>(entity)` on `self`, a child sensor, or an
entity variable (`__t0`, `__t5` …): 15 such reads in `Hrot.AI.Behaviors`' generated files, e.g. `UnitRoster`, `LocomotionChannel`,
`DangerAreaCognitiveBuffer` on a child; ⛔ no boxed / reflective / by-name reads (grep over the generated files). ⇒ the GENERATOR knows, for every
asset, which component types it reads and whether the entity is the unit itself / its own part or ANOTHER entity.

| content | how its cross-entity reads are known |
|---|---|
| blueprints, BTrees, HSMs (generated, incl. user assets compiled at run time) | ⭐ the generator emits, per asset, the set of component types read on entities other than self / own parts; registered with the asset; hot reload replaces it between ticks |
| hand-written systems and the library helpers generated code calls (`UnitSensors.Of`, `EnemyStrengthRatio` …) | a declaration on the system / helper (D-6), seeded from the census (N-2) |
| the last-tick copy's mask | the UNION over everything loaded — derived, never a hand list; changed chunks only (the 18 types the census saw: CGF 1.9–2.7 ms vs 5.2–8.5 ms for a full copy) |
| something undeclared slipping through | Debug / tests: the repository's read path (keyed by the census's per-thread current entity) THROWS on a cross-entity read of a type outside the mask ⇒ found by a rail, not in the field |

⚠ Not yet measured: the per-read cost of that check on the hot path (the tracer counted ~10⁹ accesses per run) — a static "parallel phase active"
flag checked first keeps serial code near free; to measure before the build.
