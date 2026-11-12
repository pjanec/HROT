<!--STATUS
state: LIVE — the overnight experiment, running
updated: 2026-11-12
current-answer: the whole file — the VERDICT goes at the top when T-6 is written
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — OWNS this (§12 R-4209, §13 POC 2 as-built)
  - HANDOFF_Coordinator_To_Backend_Parallel_Sim_POC_3.md — the handoff (dispatched at 83314af49)
  - STATUS_Parallel_Sim_POC_2.md — POC 2 (the profile, D-1…D-12) this follows
  - ../../DESIGN_Egress_Change_Detection.md — §5 the visit gate T-2 reuses, §7 the off-frame egress T-5 extends
-->

# STATUS — POC 3 of the simulation's throughput: the cheap wins POC 2 found (branch `exp/parallel-sim-3`)

**Branch:** `exp/parallel-sim-3` from `exp/parallel-sim-2@b06adac9f` with `coordinator@5f5105d21` merged. **Machine:** the cloud VM, 4 cores
(2 worker threads; ±15 % run-to-run noise — trends only).

| item | state | result |
|---|---|---|
| T-1 sampled query timer (R-4209, default) | ✅ built | §T-1 |
| T-2 change gate in `EqsSensorConfigEgressTranslator` | ✅ built, opt-in | §T-2 |
| T-3 pre-score the other decisions | ✅ built | §T-3 |
| T-4 the write-back hunt | 🔨 instrument built | §T-4 |
| T-5 one replica, two consumers | ⚠ part | §T-5 |
| T-6 verdict | | |

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
(kind, key, payload size) and the offset of the first changed byte (`OccurrenceStoreAccess.NoteWhere`). Measuring.

## T-5 — one replica, two consumers
| part | state |
|---|---|
| the replica sync's table copies on the worker pool (`EntityRepository.ParallelSync`, opt-in `HROT_PARALLEL_SYNC=1`) — the same bytes and versions as a serial sync | ✅ built; rail `ParallelSyncTests` (1/2/4/8 threads, no extra garbage) |
| the sync fully OFF the main thread | ⛔ not safe as stated: the live world is mutated by the next frame while a background copy would read it — only a copy-on-write store could; the parallel copy shortens the main-thread wait instead |
| the recorder's capture on the same replica | ⛔ not built (POC 2 D-12's design; ~2 days) |
