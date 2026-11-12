<!--STATUS
state: LIVE — the overnight experiment; VERDICT at the top
updated: 2026-11-12
current-answer: the T-6 VERDICT at the top; the sections below hold the evidence
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — OWNS this (§12 R-4209, §13 POC 2 as-built)
  - HANDOFF_Coordinator_To_Backend_Parallel_Sim_POC_3.md — the handoff (dispatched at 83314af49)
  - STATUS_Parallel_Sim_POC_2.md — POC 2 (the profile, D-1…D-12) this follows
  - ../../DESIGN_Egress_Change_Detection.md — §5 the visit gate T-2 reuses, §7 the off-frame egress T-5 extends
-->

# STATUS — POC 3 of the simulation's throughput: the cheap wins POC 2 found (branch `exp/parallel-sim-3`)

**Branch:** `exp/parallel-sim-3` from `exp/parallel-sim-2@b06adac9f` with `coordinator@5f5105d21` merged. **Machine:** the cloud VM, 4 cores
(2 worker threads; ±15 % run-to-run noise — trends only).

## ⭐ T-6 VERDICT — **GO on T-1, T-3, Q-1 (now effective) and the egress fixes; CHANGE T-2's gate and T-5's scope** *(VM: 4 cores / 2 worker threads, ±15 % noise — the Windows command at the end; POC 2's Windows numbers taken in)*

**The Windows run of POC 2 (`STATUS_Ui_Windows_POC_2.md` on exp/parallel-sim-2) — the baseline this batch builds on:** everything on, recorded:
CGF **266 → 181 ms (1.47×)**, 8.6 → 11.3 fps; the brain pre-score 104 → 56 ms, **2.0× from 1 to 8 threads** (flattening after 4); the timer −12.5 ms;
⭐ **off-frame egress takes the SimHost 92 → 40 ms** (it did nothing for the CGF — the VM agrees, below).

| item | VM result | verdict |
|---|---|---|
| ⭐ **T-1 the sampled timer** (R-4209, the default now) | BrainTick **74.3 (every call timed) → 64.7 (sampled) → 58.3 (off)** — sampling takes back ~60 % of the timer's cost; counts exact; rail + red-proof | ⭐ **GO** (built as approved) |
| ⭐ **T-3 the pre-score widened** (threat ranking) | BrainTick **71.4 → 51.2 ms (−28 %)**; the CombatPosture task on the main thread 24.4 → 8.9 ms; rail at 1/2/4/8, `ParallelGuard` silent, red-proven | ⭐ **GO** |
| ⭐ **Q-1 on this base** (T-4) | the 4 KB tier: dirty 112.9 → **62.3 chunks / frame (−45 %)**, unchanged-dirty **54 % → 0 %** — POC 2's "write-back" was measured before this branch's coordinator merge and does not reproduce | ⭐ **GO** — Q-1 now does what §10.2 meant |
| ⭐ **T-2 found the real cost** | the CGF's `EqsSensorConfigTopic` was 55 % ONE call: `SimpleClusterStateCache.AllNodeIds()` — a new list under a lock per sensor per frame. Fixed twice: a snapshot rebuilt only when the node SET changes (a shared fix, all callers), and the ids read once per scan | ⭐ **GO** (the mechanism trace below) |
| T-2 the visit gate on that translator | **skipped 0** of ~6 M visits: 5 776 of 28 111 begins were FULL scans forced by "authority / ownership changed" — `OwnershipEgressSystem` READS every entity's `DescriptorOwnership`, and a managed read STAMPS its chunk (A1-2) | **CHANGE**: the gate cannot work until managed reads stop stamping — built opt-in (`HROT_MANAGED_READ_NO_STAMP=1`), the default is the user's call (a reader mutating a managed object in place would no longer be recorded) |
| T-5 one replica, two consumers | the replica sync's table copies in parallel: the launch **11.9 → 8.4 ms** on 2 threads; SimHost frame with off-frame egress **106 → 49 ms** (Windows: 92 → 40) | **CHANGE the scope**: a sync fully OFF the main thread is not safe (the next frame writes the live world it would read); the parallel copy is the safe half. The recorder sharing the replica: not built (~2 days) |

**What was hard:** again the profile, not the code. T-2's premise ("it builds every topic, then compares") was true but not where the time was — a lock in a cluster-cache getter was; and the gate, the obvious fix, was defeated by a third mechanism (managed reads stamping). T-4's "write-back" vanished with the base. Each was found by a trace or a counter, not by reading code.

**Cost to finish (rough days):** make `HROT_MANAGED_READ_NO_STAMP` the default after an audit of in-place managed mutations ½–1 (it unlocks every egress gate on the CGF, and is the coordinator's A1-2) · the recorder on the egress replica 2 · the posture SENSORS (`KeepPair`, EQS handles) ½–1 (not a pure read — a deferred-handle design) · Q-1 / T-3 / T-1 / the cache fixes into a lane: ½ (all small, all railed).

⭐ **The Windows command** (8 cores, every node hidden): `git checkout exp/parallel-sim-3 && dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj -c Release && python scripts/parallel-sim-measure.py --pin --poc3` (recorded: sampled / every-call / no timer · the EqsSensorConfig gate · Throughput · the pre-score · off-frame egress + parallel sync · all together). ⚠ Add `HROT_MANAGED_READ_NO_STAMP=1` to a `rec-t2` repeat to see the gate engage.

| item | state | result |
|---|---|---|
| T-1 sampled query timer (R-4209, default) | ✅ built | §T-1 |
| T-2 change gate in `EqsSensorConfigEgressTranslator` | ✅ built, opt-in | §T-2 |
| T-3 pre-score the other decisions | ✅ built | §T-3 |
| T-4 the write-back hunt | ✅ | §T-4 — does not reproduce on this base; Q-1 effective |
| T-5 one replica, two consumers | ⚠ part | §T-5 — parallel sync built; fully off-thread unsafe; recorder not built |
| T-6 verdict | ✅ | top of this file |

## T-1 — the per-query timer samples 1 call in 16 (R-4209)
| claim | basis |
|---|---|
| `Begin` returns a timestamp for 1 call in 16 (a per-thread xorshift draw — a fixed every-16th counter aliases with a loop timing 16 calls in a fixed order), else a count-only marker; `End` counts EVERY call and scales a sampled call's time and bytes ×16 | `QueryTimings.cs` (`SampleOneIn`, `CountOnly`) |
| counts exact; total time within 30 % of timing every call (8 000 × ~5 µs, ~500 samples); 0 B steady | rail `R4209_TheSampledTimer_CountsEveryCall_…` ✅; 🔴 red-proven (scale-up dropped ⇒ ~1/16) |
| still ON by default; `HROT_QUERY_TIMINGS=0` off; `HROT_QUERY_SAMPLE=1` times every call (the measurement knob) | |

## T-2 — `EqsSensorConfigEgressTranslator` behind the shared egress visit gate (opt-in `HROT_EQS_CONFIG_GATE=1`)
| claim | basis |
|---|---|
| it built every sensor's topic every frame, then compared (17 of the CGF's ~39 ms of egress) | POC 2 D-4; `EqsSensorConfigEgressTranslator.cs` |
| ⭐ reused, not new: the CE-2407 `EgressVisitGate` (Egress §5) — declared inputs `EqsSensor`, `SensorConfigPayload`, `EqsContextPoints`, `PartMetadata`; it forces a visit on authority / ownership / parts writes, seek / restore / wipe (H3), structural changes, dirty marks; the sweep bounds the rest | `EgressVisitGate.cs` |
| a sensor HELD for a context slot's network id has no input of its own that changes when the id appears ⇒ visited every frame until it publishes | `_held` |
| a solver re-pick (cluster cache) is an input no signal names ⇒ the sweep's bound: ≤ `SweepPeriod` (8) frames late | the gate's H5 rule |
| a skipped sensor stays in `_seen` — the gone pass must not treat it as ended | `_seen.Add` on skip |
| NED EQS tests gate 0 / 1 | ✅ 5 / 5 · ✅ 5 / 5 |

## T-3 — the pre-score covers candidate rankings too
| claim | basis |
|---|---|
| "Attack approach" is an OPTION decision through `ChooseOption` — already pre-scored by POC 2 | `AttackApproachDecision.cs:29` (`PostureSelect`) |
| "Threat ranking" (1 300 calls / frame) is a CANDIDATE decision through `TopCandidate` — now demanded and pre-scored with `EvaluateCandidates` | `UtilityScorer.TopCandidate`, `UtilityPrescore.ScoreJob` |
| `EvaluateCandidates` uses no instance state beyond the registry and takes nothing writable | read; `ParallelGuard` silent on the rail |
| rail `T3_ThePrescoredThreatRanking_IsTheSerialRanking_AtEveryThreadCount` (200 units, all taken, equal top / score / buffer at 1/2/4/8) | ✅ |
| weapon selection is called with a per-call TARGET (context) and costs 0.16 ms ⇒ not worth it; the posture SENSORS (`KeepPair`) drive EQS handles — not a pure read ⇒ not pre-scored (`TopThreat` → threat ranking IS covered) | POC 2 rig query table; `PostureNodes.KeepPair` |

## T-4 — the write-back hunt: the instrument
`HROT_WRITE_STATS=1 HROT_WRITE_STATS_NOTES=1`: when the brain's tick view stamps a block (its bytes changed during the tick), it names the slot
(kind, key, payload size) and the offset of the first changed byte (`OccurrenceStoreAccess.NoteWhere`).

| M-1 on this base, CGF 1 000 men | Q-1 OFF (`t4base`) | Q-1 ON (`t4notes`) |
|---|---|---|
| BlueprintBlackboard4096 | refs 435 · dirty **112.9** / 130.8 · unchanged **54 %** | refs 134 · dirty **62.3** / 148.3 · unchanged **0 %** |
⇒ ⭐ on this base the stamp-on-change compare removes EVERY unchanged-dirty chunk; POC 2's ~50 % residual (D-11) was measured on `exp/parallel-sim-2` before
the coordinator merge and does not reproduce. The remaining stamps are real per-tick changes, named: three BTree slots at fixed offsets (`4760c0fe` +48,
`03168a7e` +4, `5559a772` +72 — 10 608 / 4 602 / 4 526 per 300 frames) and one unit memory (`6fdf6412` +216) — counters / timers the trees advance.

## T-5 — one replica, two consumers
| part | state |
|---|---|
| the replica sync's table copies on the worker pool (`EntityRepository.ParallelSync`, opt-in `HROT_PARALLEL_SYNC=1`) — the same bytes and versions as a serial sync | ✅ built; rail `ParallelSyncTests` (1/2/4/8 threads, no extra garbage) |
| the sync fully OFF the main thread | ⛔ not safe as stated: the live world is mutated by the next frame while a background copy would read it — only a copy-on-write store could; the parallel copy shortens the main-thread wait instead |
| the recorder's capture on the same replica | ⛔ not built (POC 2 D-12's design; ~2 days) |

## T-2, what the traces said *(the mechanism, not the gate)*
| claim | basis |
|---|---|
| 55 % (POC 2 trace) / 38 % (after the snapshot) of `EqsSensorConfigEgressTranslator.ScanAndPublish` was `SimpleClusterStateCache.AllNodeIds()` | `q0trace`, `t2trace` speedscope |
| it built `_nodes.Keys.ToList()` under a lock for EVERY sensor EVERY frame (`SolverFor`) | `SimpleClusterStateCache.cs:79-85` (before) |
| fix 1 (shared): one snapshot, dropped only when the node SET changes (a heartbeat refreshing a known node keeps it) | rail `SimpleClusterStateCacheSnapshotTests` (same instance, 0 B, add / remove / prune seen) |
| fix 2 (the caller): the ids read once per scan | `_knownThisScan` |
| the visit gate skipped 0: full scans forced by "authority / ownership changed" every frame | `HROT_EGRESS_GATE_STATS=1`: begins 28 111, full 5 776 (authority 5 746) |
| ⭐ the cause: `OwnershipEgressSystem` reads every entity's `DescriptorOwnership` with `GetComponent`, and a managed READ stamps the chunk version (`UnsafeShim.ManagedAccessor.GetRO` → `GetRW`) — the coordinator's A1-2 | `OwnershipEgressSystem.cs:40`, `UnsafeShim.cs` ("Managed RO uses RW delegate") |
| built opt-in: `HROT_MANAGED_READ_NO_STAMP=1` — a read through a non-stamping ref; a write still stamps | rail `ManagedReadNoStampTests` |
| the EQS integration tests with the gate OFF / ON | 156 / 157 both ways — the one red is the same test in both (a recording/replay-browser test the "Eqs" filter caught) ⇒ equivalent |

## Final gates (HEAD of `exp/parallel-sim-3`, every opt-in OFF)
| gate | result |
|---|---|
| `DeterminismRails` | ✅ **5 / 5** |
| scenario regression subset `bc-house-defended`, `ld-sentry-surprise`, `cc-platoon-bound` | ✅ **3 / 3 PASS** |
| Fdp.Core.Tests | ✅ except the 3 source-scan rails red at the base (JSON options ×2, wall clock) |
| Hrot.Network.NED.Tests | ✅ 197 / 197 |
| Toolkits: StarterPack (pre-score rails) + blueprint stamp + QueryTimings | ✅ 47 / 47 |
| ⚠ POC 1's `P0_ASteadyRun_AllocatesNothing` was FLAKY on POC 1's own commit (4 of 6 red): it measured process-wide garbage | fixed: the calling thread's own counter + a warm-up long enough that the caller has blocked once (6 / 6) |
| red-proofs | T-1 ✅ red · T-3 ✅ red · the cache snapshot / managed-read rails carry their own off-case asserts |
