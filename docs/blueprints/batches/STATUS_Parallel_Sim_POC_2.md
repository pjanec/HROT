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
| Q-0 profile | ✅ | — | §Q-0 — ⭐ + the per-query timer costs ~13 ms of the brain on the VM (D-1) |
| Q-1 writes stamp only on change | 🔨 | `exp/parallel-sim-2` | D-2: done in the TICK SYSTEMS, not the emitter; the false writes are BTree / HSM, not blueprints (D-3) — measuring |
| Q-2 off-frame state egress | ✅ measured | — | ⛔ **no gain on the CGF**: egress 39 → 37 ms incl. the launch's 11.8 ms replica SYNC on the main thread (D-7); its biggest translator stays on the frame (D-4); replication suites OFF vs ON running |
| Q-3 recorder capture off the frame | | | |
| Q-4 brains in parallel (Throughput) | ✅ | `exp/parallel-sim-2` | ⭐ **BrainTick 92.4 → 57.4 ms (−38 %)**, CGF frame 212 → 160 ms, 11.0 → 13.6 fps on the VM's 2 threads (D-8); rail green at 1/2/4/8, red-proven |
| Q-5 weapon dispatcher | 🔨 partial | `exp/parallel-sim-2` | D-6: the 12 % turret-lookup waste removed (shared parts index); the executor itself NOT parallelised (D-6) |
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


## Decisions (claim tables)

### D-1 — the per-query timer (`QueryTimings`, ON by default) costs ~13 ms of the brain on the VM
| claim | basis |
|---|---|
| the trace put 38 % of the brain inclusive in `QueryTimings.End` | `q0trace/cgf.speedscope.json` (sampled; inlining may inflate it) |
| ⭐ the A/B says ~13 ms is real | `q0` vs `q0off` (`HROT_QUERY_TIMINGS=0`), both recorded: BrainTick **82.9 → 70.1 ms**, the recorder and egress unchanged (28 / 12 ms both) |
| ~37 k timed calls / frame (≈ 0.35 µs each, against 32 ns per raw clock read measured here) | the rig's query table; a 2 M-call micro-benchmark on this VM |
⇒ the measurement is not free at 1 000 men. ⭐ The Windows script runs a timer-off column (`--poc2`). ⚠ Not changed by this POC (it is an instrument the rig itself reads).

### D-2 — Q-1 is done in the tick systems (compare the bytes after the tick), not in the blueprint emitter
| claim | basis |
|---|---|
| the emitter fix (§10.2) needs a new tick contract (a "wrote" flag) and regenerates every blueprint emit golden | `StatementEmitter.cs:1296-1300` (edge memory), `TickDelegate` returns void (`BlueprintDelegates.cs:17`) |
| a payload compare after the tick gives the same precise stamp with no contract change, no shadow memory (a per-call reused buffer) | `BlueprintTickSystem.TickTier` (this branch) + `BlueprintTickStampOnChangeTests` (3 rails, 0 B steady) |
| it catches every same-value write, generated or not | by construction |

### D-3 — the 4 KB tier's no-op writes are BTree / HSM ticks, not blueprints
| claim | basis |
|---|---|
| with the blueprint-tick compare ON, the 4 KB tier still showed 48 % unchanged-dirty, 1 149 writable refs / frame | `q1m1` M-1 |
| the brain's per-tick store view stamps a block on its first writable lookup, whatever the tick then writes | `OccurrenceStoreAccess.ViewBlockRW` (`OccurrenceStoreAccess.cs:293`) |
⇒ the compare moved to the view's end too (`TickViewState.Defer` / `StampChanged`) — same knob, `HROT_BP_STAMP_ON_CHANGE=1`; rail `TheBrainTickView_StampsItsBlock_OnlyWhenTheTickChangedIt`.

### D-4 — on the CGF, the egress cost is one translator the off-frame path does not cover
| claim | basis |
|---|---|
| CGF egress 36–45 ms / frame (several systems), of which `EqsSensorConfigTopic` 17–18 ms | the rig's egress table, `recbase` / `recq2` |
| that translator builds a full topic (strings, `PointsOf`) for every sensor every frame, then compares it with the last one sent | `EqsSensorConfigEgressTranslator.cs:107-170` (`SameConfig` after the build) |
| it is not an `IOffFrameEgress` translator | resume B-9 ③ ("is it a pure scan?" — open) |
⇒ ⭐ the cheap fix is a change gate BEFORE the build (not parallelism) — recorded as a finding for the egress owner, not built here.

### D-5 — Q-4 as a parallel PRE-SCORE, not a parallel tree walk
| claim | basis |
|---|---|
| 67 % of the brain is the posture decision's `Evaluate` (step 1 of `SelectPosture`) | Q-0 |
| its nine inputs only read the world; the part index serialises behind its own lock; the timers are per thread; the TKB dictionary is read-only | `CombatPostureDecision.cs` inputs; `StandardInputs.cs` (no statics); `PartIndex.cs:65` (lock) |
| the tree walk is tangled with shared state (cadence windows, dedup dictionaries, finish, the frame ECB) | E-5's inventory (`STATUS_Backend_Parallel_Simulation_Experiment.md` §E-5) |
⇒ `UtilityPrescore`: the (unit, decision) pairs that scored last frame are scored in parallel at the brain tick's start; `ChooseOption` applies only the hysteresis. ⚠ Semantics: a pre-scored decision sees the world as it was at the START of the brain phase (Throughput only, R-4207 — the class of R-4208). Rail: `Q4_ThePrescoredPosture_IsTheSerialPosture_AtEveryThreadCount` (200 units, all 200 taken, equal at 1/2/4/8).

### D-6 — Q-5: the dispatcher's cheapest win is not parallelism
| claim | basis |
|---|---|
| 12 % of `WeaponDispatcherSystem` is `ReleaseTurrets` → `TurretParts.Find`, which BUILT a query and scanned every turret of the world for each soldier each tick (a soldier has none) | Q-0 trace; `TurretParts.cs:52-61` (before) |
| the shared parts index answers "this parent's parts" (CE-2412), already used for the same lookup by `StandardInputs.TryFindMountChild` | `PartIndex.PartsOf`; `StandardInputs.cs:659-662` |
| a turret's instance id is unique per parent ⇒ the same answer | `TurretParts.FirstPartId + index` |
| ✅ Combat + Turret + dispatcher suites | **538 / 538** |
| the executor itself (942 lines) interleaves reads and writes per shooter (aim state, weapon state, channel accounting, turret tasks, `WeaponFireIntent`) | `AimAndFireExecutor.cs` — a decide / fire split is a refactor of the whole executor, not done tonight |

## Red-proofs (POC 2)
| rail | mutation | result |
|---|---|---|
| `Q4_ThePrescoredPosture_IsTheSerialPosture_AtEveryThreadCount` | the pre-score job leaves its buffer unscored | ✅ red |
| `TheBrainTickView_StampsItsBlock_OnlyWhenTheTickChangedIt` | the view stamps at once (the deferral never taken) | ✅ red (⚠ a first attempt with `if (false)` did not compile — unreachable code is an error here — and ran a stale binary; redone with a runtime-false condition) |
| `BlueprintTickStampOnChangeTests` (blueprint tick) | the knob off | ✅ red (its own knob-off rail) |

### D-7 — Q-2 measured: off-frame egress gives the CGF nothing
| claim | basis |
|---|---|
| it was really on | `GET /diagnostics/egress` on the kept CGF: `mode: OffFrame`, 31 translators, 1 838 scans, 0 faults, `lastScanMs` 6.4, `lastSyncMs` 12.2 |
| the replica SYNC runs on the MAIN thread, in the launch | `EgressPipeline.Launch` (`EgressPipeline.cs:184-188`); the rig times `EgressPipelineLaunchSystem` at **11.8 ms** |
| egress on the frame: 39.0 ms OFF → 37.4 ms ON (25.6 egress + 11.8 launch) — a wash | `recbase` vs `recq2k`, both recorded |
| the sync copies every chunk whose version moved — so the SAME false-dirty chunks Q-1 attacks also make the sync expensive (~300 × 64 KB of the 4 KB table alone) | M-1 (`q1wm1`) + `NativeChunkTable.SyncFrom` (`:539`, copies on a version change) |
⇒ ⭐ on the CGF, Q-2 pays only after (a) `EqsSensorConfigTopic` gets a change gate or goes off-frame (D-4) and (b) the no-op dirty chunks are gone (Q-1 / D-5). ⚠ On the SimHost (Egress §7's own measurement: sync 0.86 ms) the trade is different — not measured here.

### D-8 — Q-4 measured: the pre-score is the big one
| | thr (parallel, Throughput) | thrps (+ `HROT_BRAIN_PRESCORE=1`) |
|---|---|---|
| BrainTickSystem | 92.4 ms | **57.4 ms (−38 %)** |
| CGF frame | 212.2 ms · 11.0 fps | **159.7 ms · 13.6 fps** |
| garbage / frame | 955 KB | 730 KB |
⚠ One pair on a noisy VM (±15 %), but the brain moved 35 ms where the noise reference systems (dispatcher, egress, stress) moved < 5. ⭐ On 8 cores the scoring share (67 % of the brain) divides further; the serial remainder of the walk becomes the floor.
