<!--STATUS
state: LIVE — the overnight experiment; VERDICT at the top
updated: 2026-11-12
current-answer: the E-6 VERDICT at the top; the item table below it; each section holds its evidence
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — the design this experiment tries (§5, §7, §8)
  - HANDOFF_Coordinator_To_Backend_Parallel_Simulation_Experiment.md — the handoff (dispatched at fb1e5d07b)
-->

# STATUS — backend: the parallel simulation EXPERIMENT (branch `exp/parallel-sim`)

**Branch:** `exp/parallel-sim` (head `d424929b0`), started from `coordinator@72b4ed0e6`. **Machine:** the cloud VM, **4 cores**, 15 GB (correctness and trends only —
the 8-core Windows numbers need the script left for that run).

## ⭐ E-6 VERDICT — **GO, with three changes** *(the VM, 4 cores / a 2-thread budget; the 8-core Windows numbers are one command away, below)*

| | before | after | |
|---|---|---|---|
| Threat + Stress + Callouts (CGF ms / frame, same binary, serial vs parallel) | 34.0 | **25.8** | −24 % on **2** worker threads; each system's rail is byte-equal to the serial loop at 1 / 2 / 4 / 8 threads |
| whole CGF frame (1 000 men) | 184.3 ms · 11.5 fps | 162.4 ms · 13.8 fps | ⚠ inside the VM's ±15 % run-to-run noise — the unchanged brain moved as much |
| garbage per frame from the parallel path | — | **0 B** | P-0 rail, 20 steady runs |
| no-op dirty chunks (M-1, the 4 KB blackboard tier) | 92 % of committed chunks dirty, half of them unchanged | **0 % unchanged** with commit by content (D-5) | the recorder / snapshot gain is NOT measurable on the rig (it does not record) |

**What was hard**
- not the runner (P-0 took an hour): it was finding **what else a "self-writing" system touches**. Stress publishes and starts reactions; Callouts
  decide in an order that changes who speaks; Threat filled a shared dictionary. Each needed a serial pre-pass + a deferred step + a rail that only
  goes red when neighbours sit in DIFFERENT batches (the first Callouts rail did not — fixed, now red 3 / 3).
- the **brains** (75 of the 189 ms): two behaviours are ORDER-DEPENDENT by design — cover claims read by friends in the same tick, EQS child
  sensors created and used at once. A parallel brain cannot reproduce the serial answer without changing what soldiers do (§E-5).
- **write amplification is everywhere and per-site fixes barely move it**: `GetComponentRW` first, early-out after. The biggest table's
  no-op writes come from slots that DO run and write back the same bytes — no call site can avoid that. ⇒ D-5.
- the VM: 2 worker threads and ±15 % noise. Trends only.

**Cost to finish** *(engineering days, rough)*
| piece | size |
|---|---|
| merge P-0 + the three systems + their rails, default OFF until the Windows numbers | ½ |
| SquadCoordination (~4 lazy adds / events), WeaponDispatcher main loop (mount entities + two locks) | 2 |
| brains P-3: shared ECB → batch ECB, defer `Finish` + publishes, `DecisionCadence` per unit, the two system dictionaries | 3–4 |
| brains: the two ORDER-DEPENDENT behaviours — a **user decision** (double-buffered cover claims = a friend sees a claim one tick later) | decision + 1 |
| D-5 as a per-table policy + its mid-frame-reader rule (§D-5) + a recorded Windows measurement | 2 |
| interleave (P-5): the helper is built; adopt per system with an error rail each (Stress done, opt-in) | ½ each |

**The three changes to the plan (§5)**
1. ⭐ **D-5 commit by content replaces most of P-6** (per-table check-before-write) — one policy, true by construction, measured to remove every
   no-op dirty chunk. Per-site fixes stay only where a writable ref also RACES (the `SquadAssignment` case).
2. ⭐ **P-3 runs in Throughput mode first**; Deterministic brains wait on the cover-claim decision. Rules and squads stay serial.
3. the batch size is FIXED in Deterministic mode (32) — the adaptive size made the result depend on the core count.

⭐ **Next: the Windows run** — `python scripts/parallel-sim-measure.py --pin --content` (serial + 1/2/4/8 threads + D-5 live and recorded).

| item | state | sha on exp/parallel-sim | result |
|---|---|---|---|
| E-1 baseline + M-1 | ✅ | `750282c35`, `1da4a9779` | CGF **189 ms/frame (13 fps)** at 1 000 men; ⭐ M-1: the 4 KB blackboard table stamps **149 chunks (9.5 MB) dirty per frame, 53% unchanged**; eight tables 75–99% no-op writes — §E-1, §M-1 |
| E-2 per-batch merge + harness + SimulationMode | ✅ | `1da4a9779` | `FdpWorkerPool` + `ParallelEntities`: same result at 1/2/4/8 threads, **0 B per steady run**, merge **57 µs** for 273 commands + 231 deferred; red-proven — §E-2 |
| E-3 self-writing hot systems | ✅ 3 of 7 | `1153fa15a`, `c76688443` | Threat, Stress, Callouts parallel, each with a serial-vs-parallel rail (1/2/4/8 threads), all red-proven; 34.0 → 25.8 ms on 2 threads. Squad, WeaponDispatcher, DroneImpact, FireProcessing NOT converted (hazard verdicts) — §E-3 |
| E-4 interleave / quantize | ✅ | `1c3300a25`, `c76688443`, `1989f5b2b`, D-5 commit | per-site read-before-write (blueprint tick, mission queue, SPOT, `SquadAssignment`); ⭐ **D-5 commit by content** (opt-in) removes every no-op dirty chunk; P-5 `CadenceGate.DueOnGrid` + Stress interleaved (opt-in, error rail ≤ 0.005) — §E-4, §D-5 |
| E-5 brains (stretch) | ✅ inventory | — | write inventory: ~6 easy changes, `DecisionCadence` medium, **two order-dependent behaviours** (cover claims, EQS child sensors) — §E-5. Debug guard and tier batching NOT built |
| E-6 verdict | ✅ | — | **GO, with three changes** — top of this file |

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

## E-3 — the converted systems, serial vs parallel (same Release binary, the same rig; VM budget = cores − 2 = **2 threads**)

| CGF system | baseline (before E-3/E-4) | serial2 (`HROT_PARALLEL_SIM` off) | par2 (`HROT_PARALLEL_SIM=1`) | par vs serial, raw | ⭐ par vs serial, noise-corrected¹ |
|---|---|---|---|---|---|
| CgfThreatEvaluationSystem | 6.34 | 7.07 | **4.06** | −43 % | **−32 %** |
| StressSystem | 14.97 | 17.14 | **10.56** | −38 % | **−27 %** |
| BrainTickSystem *(unchanged code — the noise reference)* | 75.3 | 95.97 | 80.86 | −16 % | — |
| whole CGF frame | 189.4 | 229.7 | 188.5 | | |

¹ ⚠ **The VM's run-to-run noise is ~±15 %** — the unchanged BrainTick moved 96 → 81 ms between two back-to-back runs of the same binary. The
corrected column scales the serial run by that ratio (0.84). ⭐ On 2 worker threads a perfect split is −50 %; −27…−32 % after noise is the serial
pre-pass + merge + the `FriendNear` lock. ⇒ ⭐ **the 8-core Windows run is the real number** — `python scripts/parallel-sim-measure.py` (below).

| rail | result |
|---|---|
| `ThreatEvaluationParallelRailTests` (Fdp.Toolkits.Tests) — 2 000 units, 3 frames, SHA1 of every `TargetMemory` + the event string, serial vs 1/2/4/8 | ✅ 568 edges equal; 🔴 red-proven (`_threatsOut[index]=0`) |
| Perception suite with `HROT_PARALLEL_SIM=1` | ✅ 46/46 |
| `StressSystemTests` serial / with `HROT_PARALLEL_SIM=1` | ✅ 24/24 · ✅ 24/24 |

### D-3 — Stress's reactions run after EVERY man's decision, not interleaved per man
| claim | code | design |
|---|---|---|
| the break's reaction (`StartBreak` → `SopActions.React`) writes `BehaviorState` + the journal + publishes — not his own Stress | ✅ `StressSystem.cs` `JudgeApply` / `StartBreak` | ✅ `DESIGN_Parallel_Simulation.md` §5 (P-1: own writes in the batch, the rest deferred) |
| no man's Stress step reads ANOTHER man's `BehaviorState` reaction made the same frame | ✅ `StepCore` reads positions, `TargetMemory`, `SquadLeadership`, temperament, cover — never `BehaviorState` | — |
⇒ deferring every reaction to after the parallel pass changes no man's level or edge; only the ORDER of side effects inside one frame moves (batch order = entity order, the same as the serial loop's).

### D-4 — a thread-count knob for the sweep: `HROT_SIM_THREADS`, `HROT_SIM_MODE=throughput`
The handoff asks for ms at 1/2/4/8 threads on the REAL rig; `FdpConfig.MaxDegreeOfParallelism` is code-only. ⭐ `FdpWorkerPool.Budget()` reads
`HROT_SIM_THREADS` first; `ParallelEntities.Mode` reads `HROT_SIM_MODE`. Diagnostic env only — the default budget is unchanged (cores − 2).

## ⭐ The Windows measurement — one command (8 cores; every node hidden, `HROT_HIDDEN_WINDOW=1` is set by the script)

```
git checkout exp/parallel-sim
dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj -c Release
python scripts/parallel-sim-measure.py --pin          # serial + 1/2/4/8 threads, ~6 rig runs × ~3 min
```
It prints the converted systems' ms per column and leaves every raw report in `parallel-sim-results/`.

## E-4 — writes that stamp a chunk for nothing (check-before-write where M-1 points)

| writer | the defect | fix (`exp/parallel-sim`) |
|---|---|---|
| `BlueprintTickSystem.TickTier` | resolved every unit's blackboard WRITABLE before knowing a slot would run ⇒ the 4 KB tier stamped 149 / 167 chunks a frame | read-only resolve; stamped writable on the first slot that runs |
| `MissionAdapterSystem` (CGF) | took `MissionPlanQueue` AND `BehaviorState` writable for every unit; writes neither on any path | both read-only (`BehaviorState` ref was unused) |
| `MissionDirectorSystem` | `MissionPlanQueue` writable before the "complete" / "halted" early-outs | read the early-outs; writable only for a plan that can advance |
| `StaffSystems` SPOT report | `ReportOutbox` writable, then usually "nothing new to say" | read first; writable only to send |

⚠ **The pattern is everywhere** — `GetComponentRW` first, early-out after. Per-site fixes catch the measured ones only.

### D-5 — the general fix is a COMMIT-TIME content check, not a rule at every call site *(lean; prototype not built)*
| claim | basis |
|---|---|
| M-1 already compares each dirty chunk with last frame's bytes at frame end | ✅ `NativeChunkTable.SampleWrites` (this branch) |
| a dirty chunk whose bytes are unchanged could get its PREVIOUS version back at that point | ✅ consumers compare `version > lastSeen` (`NativeChunkTable.cs:148`, `SyncFrom` :539 copies when versions differ) — a restored older version never fires a false change |
| ⇒ every later snapshot / recorder diff / egress scan skips it | ⛔ assumed for the recorder; ✅ for `SyncFrom` |
| cost: a 64 KB compare + copy per dirty chunk (~9.5 MB / frame at 1 000 men before E-4) | ⛔ not measured — M-1's own overhead is the upper bound |
⇒ ⭐ **lean: build it as an opt-in table policy after the per-site fixes are measured** — it makes "a write that changed nothing is not a change" true by construction (R-shared-and-unified), where per-site fixes rot. ⚠ A system that took a ref mid-frame still sees the new version until the frame ends — harmless (it only over-copies).

## E-5 — the brains: what one unit's tick writes beyond itself (a read-only sweep; BrainTick = 75 ms of the 189)

⚠ Grep + reading, ~120 node files grep-scanned only; compiled blueprint ticks and HSM action bodies not traced — **not exhaustive** (coverage could not be checked).

| hazard | where | how often | cost to make parallel |
|---|---|---|---|
| ⭐ the commander's `UnitRoster` + `SquadCognitiveState` taken WRITABLE to read the assigned target | `SquadAssignment.cs:35-38` | every soldier, every tick | ✅ **fixed on this branch** (read-only) — also an E-4 win: those are M-1's 95 % / 75 % unchanged tables |
| shared frame ECB handed to blueprint ticks | `BrainTickSystem.cs:173` | every blueprint tick | easy — the per-batch buffer |
| system dictionaries (dedup, blueprint layout) | `BrainTickSystem.cs:535/594, 647/660` | read every tick, write on finish | easy — merge after the batch |
| `Finish` → `BehaviorIngressSystem.Clear`: destroys parts, managed writes, publishes | `BehaviorIngressSystem.cs:1747-1808` | on a task's end | easy — defer the whole finish |
| bus publishes (orders, faults, SOP reactions) | many | events | easy — deferred |
| ⚠ `DecisionCadence` — one dictionary per world + a lazily rebuilt wake table | `DecisionCadence.cs:79, 96-111` | **every tick** (CombatPosture, AttackApproach, ThreatRanking) | medium — build the wake table in the serial prologue, entries per unit |
| ⛔ `CoverClaim`: written by one unit, read by its friends the same tick | `PeekAndFireNodes.cs:1196-1232` | every tick in PeekAndFire | **hard** — result depends on visit order; needs a claims pass or double-buffered claims (a behaviour change) |
| ⛔ `EqsChildSensor.Ensure`: CreateEntity + 4 adds, handle used at once | `EqsChildSensor.cs:83-90` | when an EQS tactic starts | hard — the deferred path costs a frame |
| ⛔ the repository's ONE `_windowReader` | `EntityRepository.View.cs:118` | every CADENCED tick | hard — per-thread reader; soldiers are not cadenced by default |
| lazy self-adds (`CoverClaim`, `UnderFireMove`, `ReportOutbox`, `EqsSensor`, fault latch) · store growth `AddBlock` | various | first use / rare | pre-add at ingress, or a serial pre-pass |
| rule / objective brains: static scratch lists + cross-rule writes | `MeasuresEvidence.cs:64`, `ObjectiveCensus.cs:44`, `ScenarioVerbs.cs` | rule ticks | keep serial |

⇒ ⭐ **Verdict for brains:** a parallel brain tick is ~6 easy changes + `DecisionCadence` (medium) + **two order-dependent behaviours** (cover claims, EQS child creation) that cannot give the serial answer without changing what soldiers do. ⭐ Lean: **soldier tier in batches with claims double-buffered (a claim is seen by friends NEXT frame)** — Throughput mode only until it is measured as acceptable; rules and squads stay serial.

### E-3, second pair — Threat + Stress + Callouts parallel, E-4 fixes in both (`c76688443`, Release, VM budget 2 threads)

| CGF ms / frame | serial3 | par3 | Δ |
|---|---|---|---|
| CgfThreatEvaluationSystem | 6.4 | **4.2** | −34 % |
| StressSystem | 14.4 | **9.7** | −33 % |
| CalloutSystem | 13.2 | **11.9** | −10 % ⚠ its cost is mostly the serial "say" + the pre-pass |
| BrainTickSystem *(unchanged — noise ref)* | 73.1 | 63.7 | −13 % ⚠ noise |
| **whole CGF frame** | **184.3** | **162.4** | −12 % · fps 11.5 → **13.8** |
| garbage B / frame | 873 263 | 764 518 | no new garbage from the parallel path |

⚠ The noise reference moved −13 % again, so the frame gain is mostly noise plus ~10 ms real (the three systems together: 34.0 → 25.8 ms).

### M-1 after the per-site E-4 fixes (`m1b`)
| table | before | after | |
|---|---|---|---|
| MissionPlanQueue | 9.9 dirty / frame, 99 % unchanged | **out of the top 25** | ✅ the mission read-before-write |
| BlueprintBlackboard4096 | refs 1 066 · 149 dirty · 53 % unchanged | refs **751** · 203 / 220 dirty · 50 % unchanged | ⚠ fewer refs, NOT fewer dirty chunks — the blueprints DO run every tick and write back the same bytes |
| ReportOutbox | 94 % | 94 % | ⛔ the SPOT fix is not the writer that matters — other `ReportQueue` sites |
| SquadCognitiveState / UnitRoster | 75 % / 95 % | 75 % / 96 % | the `SquadAssignment` fix came AFTER this build |
⇒ ⭐ **per-site fixes cannot reach the biggest table** — a slot that ran wrote the same bytes. That is the measured case for D-5 (commit by content), now prototyped.

### D-5 measured — commit by content (`HROT_CONTENT_VERSION=1`, with M-1 on; Release, built in chain 3 from `1989f5b2b`^)

| table | M-1 before D-5 (`m1b`) | with D-5 (`d5m1`) |
|---|---|---|
| BlueprintBlackboard4096 | dirty 202.8 / 220.3 committed (**92 %**), 50 % of them unchanged | dirty 44.3 / 111.2 (**40 %**), **0 % unchanged** |
| SquadCognitiveState | 53.1 / 64.8, 75 % unchanged | 6.0 / 36.6, 0 % unchanged *(+ the `SquadAssignment` read-only fix)* |
| every other table | 75–99 % unchanged where listed | **0 % unchanged — every remaining dirty chunk is a real change** |

⚠ The run's committed counts differ (the world at the last window was not the same size), so compare SHARES, not counts. ⚠ **Frame time is not
resolvable on the VM:** d5par 200.7 vs par4 207.2 ms, inside the ±15 % noise; and the rig does not RECORD, so the recorder diff and the snapshot
copies — the consumers D-5 helps — are not in the measured path. ⇒ the Windows script should add a `--record` pair with and without D-5.

| rail | result |
|---|---|
| `ContentVersioningTests` (Fdp.Core) | ✅; 🔴 red-proven (the byte compare dropped ⇒ the real change reads as no change) |

## Gates (chain 3, built from `1989f5b2b`^, both `HROT_PARALLEL_SIM` modes)

| suite | serial | parallel | the reds |
|---|---|---|---|
| Fdp.Core.Tests | 1314 / 3 red | — | `OneSharedJsonOptionsTests` ×2, `NoSystemSchedulesOnTheWallClock` — all in files this branch never touched (`git diff 72b4ed0e6..HEAD` names none) ⇒ pre-existing |
| Hrot.SimHost.Tests | 2697 / 10 red | 2696 / 11 red | ✅ **9 of the 10 red at the base too** (`72b4ed0e6`, clean worktree): ShippedTreesAreCSharp ×6, StrictNetworkSeparation, OneBootstrap, CgfLogicPack CE454 · the 10th, LoadPhaseChain CE3345, passes its class 3 / 3 at HEAD ⇒ a suite-order flake · +1 in parallel: `ReadingARowAllocatesNothing(FrightFrom.Y)` — passes its class 243/243 twice with parallel on ⇒ a one-off |
| Fdp.Toolkits.Tests | 4949 / 5 red | 4951 / 3 red | ✅ **red at the base too** (`72b4ed0e6`, full suite): S0 type rails ×2, CQRS struct, MissionDirector · BuildingBreach alloc passes its class 3 / 3 at HEAD and was not red in the parallel run (the base run had its own one-off, ExerciseRetention) ⇒ suite-order flakes, none caused by this branch |

### Gates for the last commits (chain 4, HEAD `1989f5b2b`)
| rail | result |
|---|---|
| StressSystemTests + CalloutsTests, serial / `HROT_PARALLEL_SIM=1` | ✅ 30 / 30 · ✅ 30 / 30 |
| CadenceGateTests (incl. `P5_DueOnGrid`) | ✅ 11 / 11 |
| 🔴 Callouts rail, `Say` from the worker (batch-crossing world) | red **3 / 3** tries |
| 🔴 interleave rail, one frame's dt integrated instead of the elapsed time | red |
| tree clean after every mutation | ✅ |
