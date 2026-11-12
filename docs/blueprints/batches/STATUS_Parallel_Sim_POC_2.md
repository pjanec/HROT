<!--STATUS
state: LIVE — the overnight experiment, running
updated: 2026-11-12
current-answer: the whole file — the VERDICT goes at the top when Q-6 is written
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — OWNS this (§10 R-4208 + the emitter fix, §11 the aggregate)
  - HANDOFF_Coordinator_To_Backend_Parallel_Sim_POC_2.md — the handoff (dispatched at a89010765)
  - STATUS_Backend_Parallel_Simulation_Experiment.md (on `backend`) — POC 1 (the VM); STATUS_Ui_Windows_Parallel_Sim.md — POC 1 on Windows
-->

# STATUS — POC 2 of the simulation's throughput: the serial 70 % (branch `exp/parallel-sim-2`)

**Branch:** `exp/parallel-sim-2` from `exp/parallel-sim@9b9f6d04d` (POC 1 + the Windows run) with `coordinator@5f0761334` merged. **Machine:** the
cloud VM, 4 cores (correctness and trends); the Windows command is `scripts/parallel-sim-measure.py` (extended per item, below).

| item | state | sha | result |
|---|---|---|---|
| measurement blind spot | ✅ | `exp/parallel-sim-2` | the rig reports EVERY system (`all_ms`); the script sums it — a system under the top-8 cut no longer reads 0.00 |
| Q-0 profile | ✅ | — | §Q-0 |
| Q-1 emitter writes on change | | | |
| Q-2 off-frame state egress | | | |
| Q-3 recorder capture off the frame | | | |
| Q-4 brains in parallel (Throughput) | | | |
| Q-5 weapon dispatcher | | | |
| Q-6 verdict | | | |

## Q-0 — the profile (CGF, 1 000 men, serial, recording; `dotnet-trace` sampled thread time 15 s inside the measured window + the rig's query timings)

Frame 231 ms: **BrainTick 82.9** · **Recorder 27.7** · WeaponDispatcher 18.6 · Ingress 15.4 · Stress 12.7 · Egress 12.1 + 11.3 + … · Callouts 11.0 ·
Squad 5.7 · Threat 5.5.

### Inside the brain tick (share of `BrainTickSystem.ExecuteCore`, inclusive)
| sub-part | share | what |
|---|---|---|
| ⭐ the CombatPosture **utility decision** (`UtilityScorer.SelectPosture` / `ChooseOption`) | **67 %** | the per-unit scoring of the posture options, 375 decisions / frame |
| · its input `EnemyStrengthRatio` | 20 % | 1 873 calls / frame, 7.3 µs each (`ThreatDanger` over the man's memory) |
| · the EQS-tactic reads `TopThreat` / `KeepPair` / `TopAim` (`PostureNodes.PostureSensors`) | 15 % | the posture's sensors |
| · `EqsTopScore` / `UnitSensors.OfTemplate` / `PartIndex.PartsOf` | 11 % / 11 % / 9 % | template lookups (`PartsOf` 3 800 calls / frame, behind a lock) |
| the SOP slots (`TickSopSlots`) | 6 % | |
| BTree interpretation itself (`Interpreter.Execute*`) | ~0 % self | the tree walk is not the cost — the leaves are |
| ⚠ `QueryTimings.End` (the per-query timer) | 38 % inclusive in the trace | ⛔ **suspect** — 37 k timed calls / frame × ~0.1 µs ≈ 4 ms, not 15; a sampling / inlining artefact is likely. ⭐ Being settled by a run with `HROT_QUERY_TIMINGS=0` |
⇒ the brain's cost is **per-unit scoring in leaves** — pure reads of the man's own memory and the world, the shape that parallelises (Q-4).

### Inside the weapon dispatcher (share of `WeaponDispatcherSystem.Execute`)
| sub-part | share |
|---|---|
| `AimAndFireExecutor.Execute` | 85 % |
| · `LineOfFire.Blocked` (friends in the line, a grid behind a lock) | 35 % |
| · `WeaponChoice.Choose` (a utility decision per shooter) | 31 % |
| · `ReleaseTurrets` → `TurretParts.Find` (a man with no turret, looked up every tick) | 12 % |
| · `PersonBody.PoseOf` | 9 % |

### Inside the recorder (share of `RecorderTickSystem.Execute`)
| sub-part | share | ⇒ |
|---|---|---|
| `RecordDeltaFrame` | 94 % | |
| · `Buffer.Memmove` — copying the DIRTY chunks | **52 %** | ⭐ proportional to the chunks stamped changed ⇒ **Q-1** (the 4 KB blackboard's false changes) cuts it directly |
| · `SequenceEqual` + `CommonPrefixLength` + `BuildBlockRuns` — the block delta | 15 + 8 + 9 % | the same ⇒ Q-1 |
| · managed tables (`RecordManagedTable` + the auto-serializer) | 18 % | |
| keyframes | 5 % | |

### The order stays as the handoff has it, with one sharpening
Q-1 first is confirmed by the recorder (its two biggest parts scale with false-dirty chunks). Q-4 is the big one (the brain's 67 % is per-unit
scoring). Q-5's cheapest win is not parallelism: 12 % of the dispatcher looks up turrets a soldier does not have.
