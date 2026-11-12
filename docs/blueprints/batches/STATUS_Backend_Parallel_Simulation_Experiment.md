<!--STATUS
state: LIVE — the overnight experiment, running
updated: 2026-11-12
current-answer: the whole file — the VERDICT goes at the top when E-6 is written
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — the design this experiment tries (§5, §7, §8)
  - HANDOFF_Coordinator_To_Backend_Parallel_Simulation_Experiment.md — the handoff (dispatched at fb1e5d07b)
-->

# STATUS — backend: the parallel simulation EXPERIMENT (branch `exp/parallel-sim`)

**Branch:** `exp/parallel-sim`, started from `coordinator@72b4ed0e6`. **Machine:** the cloud VM, **4 cores**, 15 GB (correctness and trends only —
the 8-core Windows numbers need the script left for that run).

| item | state | sha on exp/parallel-sim | result |
|---|---|---|---|
| E-1 baseline + M-1 | ⏳ started | | |
| E-2 per-batch merge + harness + SimulationMode | | | |
| E-3 self-writing hot systems | | | |
| E-4 interleave / quantize | | | |
| E-5 brains (stretch) | | | |
| E-6 verdict | | | |

## Decisions (claim tables)

*(each decision taken during the night, with its basis)*
