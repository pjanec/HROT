<!--STATUS
state: LIVE — the overnight experiment; VERDICT at the top
updated: 2026-11-12
current-answer: the Q-6 VERDICT at the top; the item table; D-1…D-12 hold the evidence
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — OWNS this (§10 R-4208 + the emitter fix, §11 the aggregate)
  - HANDOFF_Coordinator_To_Backend_Parallel_Sim_POC_2.md — the handoff (dispatched at a89010765)
  - STATUS_Backend_Parallel_Simulation_Experiment.md (on `backend`) — POC 1 (the VM); STATUS_Ui_Windows_Parallel_Sim.md — POC 1 on Windows
-->

# STATUS — POC 2 of the simulation's throughput: the serial 70 % (branch `exp/parallel-sim-2`)

**Branch:** `exp/parallel-sim-2` from `exp/parallel-sim@9b9f6d04d` (POC 1 + the Windows run) with `coordinator@5f0761334` merged. **Machine:** the
cloud VM, 4 cores (correctness and trends); the Windows command is `scripts/parallel-sim-measure.py` (extended per item, below).

## ⭐ Q-6 VERDICT — **GO on the brain pre-score; CHANGE the plan for the rest** *(VM 4 cores / 2 worker threads, ±15 % noise; the Windows command at the end)*

| item | VM result | verdict |
|---|---|---|
| ⭐ **Q-4 the brains' posture scoring, pre-scored in parallel** (Throughput) | **BrainTick 92.4 → 57.4 ms (−38 %)**, CGF frame 212 → 160 ms, 11.0 → 13.6 fps; rail equal at 1/2/4/8 threads; a Debug guard proves the scoring takes nothing writable | ⭐ **GO** — the first step that moved the serial 70 %. Next: the same shape for the other decisions (threat ranking, attack approach, weapon choice) and the posture SENSORS (`TopThreat` / `KeepPair`, 15 %) |
| Q-0 the per-query timer (ON by default) | ~13 ms of the brain (82.9 → 70.1 with it off) | **CHANGE**: make it opt-in for production / sample it — one line, needs the user's call (the rig reads it) |
| Q-5 the turret lookup | 12 % of the dispatcher was scanning every turret per soldier per tick; fixed via the shared parts index (538 / 538 green) | **GO** (a fix, not parallelism). The executor's decide/fire split: ~2 days, after the brains |
| Q-1 stamp only on a real change | built in both tick paths, rails + red-proofs green — but the 4 KB tier is still ~50 % unchanged-dirty: real in-tick changes that are reverted within the frame (D-11) | **CHANGE**: keep the tick-path compare (it is precise and 0 B); find the write-back pair (D-11's next measurement); D-5 commit-by-content is the catch-all if a recorder / sync consumer needs it |
| Q-2 off-frame state egress on the CGF | a wash: 39 → 37 ms on the frame, because the replica SYNC (11.8 ms) runs on the main thread and the biggest translator (`EqsSensorConfigTopic`, 17 ms) is not off-frame (D-4, D-7); replication gate: no ON-only red in 44 shared tests | **CHANGE**: first a change gate in `EqsSensorConfigEgressTranslator` (it builds every sensor's topic every frame, then compares); then Q-3 makes the sync pay for two consumers |
| Q-3 recorder capture off the frame | not built (D-12) | **GO as a design**: one replica sync, egress scan + recorder delta both off the frame (~2 days) |

**What was hard:** the profile, not the code — every first assumption was off by a layer: the brain's cost was the SCORING not the tree; the no-op writes were BTree / HSM not blueprints, and then not even "no-op" but written-back; the CGF egress was one translator; the off-frame sync sits on the main thread. Each was found by measuring before building (the handoff's Q-0 rule paid four times).

**Cost to finish (rough engineering days):** pre-score the other decisions + sensors ½–1 · the D-11 write-back hunt ½ · the `EqsSensorConfig` change gate ½ · Q-3 replica-shared recorder 2 · the dispatcher decide/fire split 2 · a parallel tree walk (R-4208 claims, per-batch ECB, deferred finish) 4+, only if the pre-score's floor demands it.

⭐ **The Windows command** (8 cores, every node hidden): `git checkout exp/parallel-sim-2 && dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj -c Release && python scripts/parallel-sim-measure.py --pin --poc2` (recorded serial · timer off · Q-1 · Q-2 · Throughput · Throughput + pre-score · all together), and `--pin --threads 1 2 4 8 --mode throughput` with `HROT_BRAIN_PRESCORE=1` set for the thread curve.

| item | state | sha | result |
|---|---|---|---|
| measurement blind spot | ✅ | `exp/parallel-sim-2` | the rig reports EVERY system (`all_ms`); the script sums it — a system under the top-8 cut no longer reads 0.00 |
| Q-0 profile | ✅ | — | §Q-0 — ⭐ + the per-query timer costs ~13 ms of the brain on the VM (D-1) |
| Q-1 writes stamp only on change | ⚠ built, not effective | `exp/parallel-sim-2` | the tick systems stamp only on a byte change (D-2, D-3), rails + red-proofs green — ⛔ yet the 4 KB tier stays ~50 % unchanged-dirty: the stamps that remain are REAL in-tick changes that end the frame reverted (D-11, open) |
| Q-2 off-frame state egress | ✅ measured | — | ⛔ **no gain on the CGF**: egress 39 → 37 ms incl. the launch's 11.8 ms replica SYNC on the main thread (D-7); its biggest translator stays on the frame (D-4); replication suites OFF vs ON: no ON-only red in the 44 tests both runs reached (D-10) |
| Q-3 recorder capture off the frame | ⛔ not built | — | D-12: the design point — the recorder's capture belongs on the egress replica the launch ALREADY syncs (its 52 % memmove disappears into a sync already paid); measured cost / order below |
| Q-4 brains in parallel (Throughput) | ✅ | `exp/parallel-sim-2` | ⭐ **BrainTick 92.4 → 57.4 ms (−38 %)**, CGF frame 212 → 160 ms, 11.0 → 13.6 fps on the VM's 2 threads (D-8); rail green at 1/2/4/8, red-proven |
| Q-5 weapon dispatcher | 🔨 partial | `exp/parallel-sim-2` | D-6: the 12 % turret-lookup waste removed (shared parts index); the executor itself NOT parallelised (D-6) |
| Q-6 verdict | ✅ | — | top of this file |

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
| ⭐ `ParallelGuard` (Debug) under the pre-score rail | the score job takes a `UtilityResultBuffer` writable | ✅ throws `a writable UtilityResultBuffer was taken inside a READ-ONLY parallel body`; without the mutation the rail passes ⇒ the posture scoring takes nothing writable |

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

### D-9 — Q-4's acceptance items, as they apply to a pre-score
| handoff item | as built |
|---|---|
| "no cover held by two units for more than a bounded number of ticks" (R-4208) | ⭐ **not needed by this cut**: cover claims are made by the tree walk, which stays SERIAL — the pre-score only computes the posture decision's scores. R-4208 is the rule for the NEXT step (a parallel tree walk) |
| "a Debug guard on a brain writing another unit's state" | ⭐ stronger for this cut: `ParallelGuard` makes ANY writable reference inside the parallel scoring throw in Debug (`NativeChunkTable.GetRefRW`) — proven on the rail, red-proven |
| "brain ms at 1 / 2 / 4 / 8 threads" | VM: 2 threads only (D-8); the Windows column is `--poc2`'s `rec-all` (+ add `HROT_SIM_THREADS`) |

### D-10 — Q-2's gate: the replication classes of `Hrot.ClusterRunner.Integration.Tests`, `FDP_EGRESS_OFFFRAME=0` then `=1` (resume B-9's filter)
| | OFF | ON |
|---|---|---|
| reached before the host crashed | 86 (82 pass / 4 fail) | 45 (43 / 2) |
| the crash | `[EqsSensorConfig][REFUSED] … malformed sensor config` | `CycloneDDS … ReturnCode: BadParameter` (a write after shutdown) |
| tests that ran in BOTH | 44 — ⭐ **no ON-only red**; 1 OFF-red passed ON (`SplitAuthoritySpawnTests.BrainSpawn_WithWorldPosDelegation…`, a flake); 2 red in both (`AllSubsystemsSpawnMovingVehicle…`, `SpawnMovingVehicleWithGateway…`) |
⚠ Both crash causes are the PRE-EXISTING ones resume B-9 names ("the host crashes at random"). ⇒ "no regression found in 44 tests", not "proven" — the same state B-9 left it in.

### D-11 — Q-1: who still takes the 4 KB blackboard writable (M-1 caller sampling, `HROT_WRITE_STATS_STACKS=BlueprintBlackboard4096`, Q-1 ON)
| sampled 1 in 32, one 300-frame window | refs |
|---|---|
| `TickViewState.StampChanged` (the view's end, the block's bytes CHANGED during the tick) | 859 + 158 + 76 + 8 |
| the tier's `Memory` accessor from other systems (`<For>b__31_1` → `GetComponentRW`) | 290 |
| `BehaviorIngressSystem` start / clear / detach (`TryFindSlot` RW) | 124 + 69 + 59 + 40 + 15 + 11 + 3 |
| `PeekAndFire` attaching its slot, `AddBlock` | 5 + 4 + 1 |
⇒ ⭐ the per-tick stamps that remain are REAL byte changes within the tick — and still ~50 % of the dirty chunks end the frame byte-identical to the last ⇒ some bytes are written and written BACK within the same frame (a pair of writers, or a transient the tick leaves and another system resets). ⛔ Not found tonight. Next measurement: in `StampChanged`, record the first differing slot / offset of a changed block, and at frame end which of those blocks reverted.
⇒ For the recorder and the replica sync this means **D-5 (commit by content) is the tool that actually removes them** — it compares at frame end, where the revert is already done. Its cost (a compare + copy per stamped chunk) is now the open trade (Windows W-3: no measurable effect on the recorder ms).

### D-12 — Q-3 not built: the recorder's capture belongs on the egress replica
| claim | basis |
|---|---|
| 52 % of `RecorderTickSystem` is copying dirty chunks (`Buffer.Memmove` in `RecordDeltaFrame`), + 32 % the block delta | Q-0 |
| the egress launch ALREADY copies every changed chunk into a replica each frame (`EgressPipeline.Launch` → `SyncFrom`, 11.8 ms on the main thread) | D-7 |
| the recorder's capture could read that replica on the scan thread — the delta + serialize off the frame, the copy paid once for both | `EgressPipeline.Loop` runs the off-frame scan on a background thread (`EgressPipeline.cs:216`) |
| ⚠ the recorder needs EVERY recorded table and the managed tables (18 %), the replica may carry only what egress needs; keyframes; ordering vs the async file writer | not measured |
⇒ ⭐ Q-3 = "one replica sync per frame, two consumers off the frame (egress scan + recorder delta)". Cost: ~2 days (the replica's table set, the recorder reading a view, a recorded-replay identity rail). It turns Q-2's wash into a win on a recording node.

