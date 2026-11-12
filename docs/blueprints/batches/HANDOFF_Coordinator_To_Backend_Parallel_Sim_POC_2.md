<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — OWNS this: §7 determinism as a mode (R-4207) · ⭐ §10 R-4208 (cover claims one tick late if over-allocation recovers) and §10.2 the no-op blackboard writes (the emitter fix first) · ⭐ §11 the aggregate (where the 242 ms goes)
  - ../../DESIGN_Egress_Change_Detection.md — §7 the state egress off the frame (CE-3427 N-1, built, OFF by default, never measured on a 1 000-man node)
  - ../../DESIGN_Threaded_Simulation_Presentation.md — §14 the target picture: the worker pool, the replica R, off-frame egress and recorder
  - HANDOFF_Coordinator_To_Backend_Threaded_POC_2.md — the OTHER experiment (the threaded editor); independent, combined later
-->

# HANDOFF — coordinator → backend: an OVERNIGHT EXPERIMENT — POC 2 of the simulation's throughput: attack the serial 70 %

**For:** a backend session. **From:** coordinator. **Dispatched at `<stamp>`**, scope frozen there. ⭐ **Still an EXPERIMENT** — 🔒 user,
`2026-11-12`: *"write the handoffs for the next POC — still experimental."*

⛔⛔ **Autonomous — never stop for a question** (R-4156). Decide, record with a claim table, continue; a failed premise stops THAT item (R-106).

## Where to work

⭐ A NEW branch **`exp/parallel-sim-2`** from **`exp/parallel-sim`** (it carries `FdpWorkerPool`, `ParallelEntities`, `SimulationMode`, the M-1
write instrument, D-5, the converted Stress / Threat / Callouts, `scripts/parallel-sim-measure.py`), with **`coordinator`** merged in. Push as you
go. Status file `docs/blueprints/batches/STATUS_Parallel_Sim_POC_2.md` ON THAT BRANCH. ⛔ Nothing merges into a lane; ⛔ do not push to `backend`.
Everything opt-in, **default OFF**. The cloud VM proves correctness; ⭐ extend `parallel-sim-measure.py` so ONE command on the user's 8-core
Windows machine measures every item (every node HIDDEN), and fix its known blind spot first: it sums the rig's top 8 systems only — a system that
drops below the cut reads 0.00 (Windows run §5).

## Why — the Windows run (Parallel Simulation §11)

CGF at 1 000 men: **242 ms** — brains **105**, egress **28**, weapon dispatcher **21**, recorder **20** (1.6 MB garbage / frame), stress 17,
callouts 15. Parallelising the small systems gave **1.07–1.12×**. ⇒ the serial 70 % is the target.

## The items — in this order, as far as the night goes

| # | item | basis | done when |
|---|---|---|---|
| **Q-0** | ⭐ **PROFILE before building** (the interleaving lesson, §9: 0.3 of 10 ms): inside `BrainTickSystem` — per kind (BTree, HSM, blueprint tiers, sensors, EQS, decision cadence) and per behaviour; inside the weapon dispatcher and the recorder capture | §11 | a cost table per sub-part in the status file; the items below re-ordered by it if it says so |
| **Q-1** | ⭐ **the blueprint emitter writes only on change and reports it**; the tick system stamps the chunk only on a reported write (CE-4438) | §10.2 | M-1: the 4 KB blackboard's unchanged-dirty share → ~0; the brain ms and the recorder / egress ms before / after |
| **Q-2** | ⭐ **switch ON the off-frame state egress** (`FDP_EGRESS_OFFFRAME=1`, CE-3427) on the 1 000-man CGF node — no new code unless it breaks | Egress §7 | egress ms on the frame before / after; the replica sync cost; the replication rails green with it ON (Egress §7's gate list, resume B-9) |
| **Q-3** | **the recorder's capture off the frame on the same replica** (its file I/O is already async) | §14 picture | recorder ms on the frame before / after; a recording made both ways replays identically (a rail); its garbage per frame named |
| **Q-4** | ⭐ **brains in parallel, Throughput mode** — per-batch ECB, deferred `Finish` and publishes, `DecisionCadence` per unit (§E-5's ~6 easy changes); ⭐ **R-4208**: cover claims read one tick late, over-allocation resolved at commit (the earlier claim, then the lower id, keeps it; the other re-picks on its next decision); EQS child sensors per §E-5's finding; Deterministic keeps brains serial | §10.1, R-4208 | the brain ms at 1 / 2 / 4 / 8 threads; ⭐ the acceptance rail: no cover held by two units for more than a bounded number of ticks at 1 000 men; a Debug guard on a brain writing another unit's state |
| **Q-5** | the **weapon dispatcher** in parallel (mount entities + its two locks — the hazard verdict in the experiment's status) | §E-3 | its ms before / after; a serial-vs-parallel rail in Deterministic mode |
| **Q-6** | ⭐ **the verdict** — the 1 000-man CGF frame before / after (VM; the Windows command left); what each item gave; what was hard; the real cost to finish; go / change / stop per item | — | at the top of the status file; the as-built folded into Parallel Simulation §9–§11 |

## Rules

- **Correctness first:** every converted system has its serial-vs-parallel rail green in Deterministic mode; Throughput-only behaviour (brains)
  has its own acceptance rail; the determinism rails and a scenario-regression subset at the end (a red is a finding, base sha named).
- **0 B per steady tick** in every parallel body (R-2084). **One core budget:** `FdpConfig.MaxDegreeOfParallelism` (cores − 2).
- Run anything slow in the background; never poll. ⛔ No screenshots or recordings in git.

**Done when:** Q-6 is written — however far Q-0…Q-5 got.

---
_coordinator lane, 2026-11-12_
