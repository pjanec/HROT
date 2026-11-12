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
| E-1 baseline + M-1 | ✅ | `750282c35`, `1da4a9779` | CGF **189 ms/frame (13 fps)** at 1 000 men; ⭐ M-1: the 4 KB blackboard table stamps **149 chunks (9.5 MB) dirty per frame, 53% unchanged**; eight tables 75–99% no-op writes — §E-1, §M-1 |
| E-2 per-batch merge + harness + SimulationMode | ✅ | `1da4a9779` | `FdpWorkerPool` + `ParallelEntities`: same result at 1/2/4/8 threads, **0 B per steady run**, merge **57 µs** for 273 commands + 231 deferred; red-proven — §E-2 |
| E-3 self-writing hot systems | | | |
| E-4 interleave / quantize | | | |
| E-5 brains (stretch) | | | |
| E-6 verdict | | | |

## Decisions (claim tables)

*(each decision taken during the night, with its basis)*

### D-1 — the terrain: `demo-city`, not `scale-1` *(E-1)*
| claim | basis |
|---|---|
| `scale-1` cannot be generated here | `terrain-scale.py` needs `hrot-terrain-synth` — not installed on this VM; `artifacts/terrain-synth` absent |
| `berlin` cannot be used | its `terrain.json` has no `world` file — the rig reads `world.hrot.bounds` (`scale-rig.py:55`) |
| ⭐ `demo-city` (800 × 800 m, roads) | the rig ran on it; 1 000 men on 800 m is a DENSE fight — heavier per man than a 5.5 km map. ⚠ The Windows `scale-1` numbers (§11h) are not directly comparable — compare before / after on the SAME rig |

### D-2 — run the rig from a script, never with the runner's path on the command line
📐 Measured twice: `scale-rig.py` stops stale nodes by command-line match; a shell whose own command line held `Hrot.ClusterRunner.dll` was killed with it (exit 144, before the first node came up). ⭐ The runner path goes in the environment inside a script file.

## E-1 — the baseline (Release, 4-core VM, 1 000 men / 50 vehicles / 10 aircraft, demo-city, 30 s warm-up + 30 s measured)

| node | work ms / frame | frames / s | garbage B / frame |
|---|---|---|---|
| **cgf** | **189.4** | 13.1 | 751 599 |

| CGF system | ms / frame |
|---|---|
| BrainTickSystem | **75.3** |
| WeaponDispatcherSystem | 17.5 |
| StressSystem | 15.0 |
| CalloutSystem | 14.7 |
| CycloneEgressSystem (×2 instances) | 12.7 + 11.8 |
| SquadCoordinationSystem | 6.5 |
| CgfThreatEvaluationSystem | 6.3 |

Inside the brain (the rig's query timings): `brain.task:CombatPosture` 24.7 ms · `utility.decision:Combat posture` 14.1 · `utility.input:EnemyStrengthRatio` 10.3 (1 348 calls / frame) · `Threat ranking` 4.1 · `Callouts.SaidNearby` 3.2.
⚠ A Debug run of the same rig gave 1 309 ms / frame — every number here is Release.

## The hazard inventory of the seven target systems (a read-only sweep, file:line in the sweep)

| system | verdict | why |
|---|---|---|
| CgfThreatEvaluationSystem | ⭐ **safe after 2 changes** | its command buffer is fetched ONCE on the main thread and used in the loop (→ per batch); a shared `_nextThreats` dictionary write (→ per-batch, merged) |
| StressSystem | ⭐ **safe after 2 changes** | `Ensure` ADDS `Stress` lazily (→ a serial pre-pass); the break edge publishes and assigns a reaction (→ deferred, serial) |
| SquadCoordinationSystem | safe after ~4 | lazy adds (`SquadLeadership`, sense components), `MarkDirty`, events |
| CalloutSystem | needs a split | reads OTHER men's `Utterance` while they write theirs (order decides who speaks) ⇒ parallel "decide", serial "say" |
| WeaponDispatcherSystem (main loop) | ⛔ not now | writes the mount / turret / controlled-system entities, two global locks (`LineOfFire` bodies grid, `PartIndex`) |
| DroneImpactSystem | ⛔ not worth it | few flyers |
| FireProcessingSystem | ⛔ not as written | creates entities and adds 5–8 components per shot, a shared scratch and ring |
| shared | — | ⚠ adding a NEW component is not thread-safe (`_structuralChanges++`, type logs); `FriendNear` / `LineOfFire` / `PartIndex` are correct under a LOCK ⇒ they cap the speed-up |

## M-1 — the write instrument (`HROT_WRITE_STATS=1`, CGF node, the same rig, 300-frame windows; the last window)

Per table, per frame: writable references handed out · chunks stamped dirty / chunks committed · of the dirty, how many hold the SAME bytes as last frame.

| table | refs | dirty / committed | unchanged |
|---|---|---|---|
| ⭐ BlueprintBlackboard4096 | 1 066 | **149.3 / 167.1** | 79.4 (**53 %**) — a 64 KB chunk each ⇒ ~9.5 MB stamped per frame, half of it for nothing |
| SquadCognitiveState | 1 011 | 39.3 / 51.2 | 29.6 (75 %) |
| BlueprintBlackboard1024 | 82 | 36.8 / 36.8 | 36.0 (**98 %**) |
| ReportOutbox | 200 | 28.2 / 28.2 | 26.6 (94 %) |
| UtilityDecisionLog | 2 123 | 25.0 / 27.7 | 0 (0 %) — real changes |
| TargetMemory | 320 | 22.0 / 35.7 | 0.3 (1 %) — real changes |
| BrainJournal | 87 | 21.5 / 59.1 | 0 (0 %) |
| BlueprintBlackboard256 | 997 | 11.2 / 15.6 | 11.0 (99 %) |
| MissionPlanQueue | 1 229 | 9.9 / 14.4 | 9.9 (99 %) |
| UnitRoster | 692 | 6.9 / 8.6 | 6.6 (95 %) |
| InteractionChannel | 1 229 | 3.8 / 5.5 | 3.8 (99 %) |
| AmmoLoadout | 507 | 2.8 / 4.1 | 2.8 (99 %) |
| WeaponChannel · LocomotionChannel · WeaponState | 1 519 · 1 382 · 1 805 | 3.8 · 3.8 · 2.8 | 26 % · 26 % · 30 % |

⇒ ⭐ **E-4's targets, by measurement:** check-before-write in the writers of the three blueprint blackboard tables, SquadCognitiveState, ReportOutbox, MissionPlanQueue, UnitRoster, InteractionChannel, AmmoLoadout. Every dirty chunk is copied by each snapshot (background modules, the threaded presentation), diffed by the recorder and scanned by egress.
