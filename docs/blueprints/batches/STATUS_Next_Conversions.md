<!--STATUS
state: LIVE
updated: 2026-11-13
current-answer: the item table and each item's section
stale-below: none
related-designs:
  - docs/DESIGN_Parallel_Simulation.md — §18 owns the per-entity parallel rule this batch converts systems to
  - docs/DESIGN_Egress_Change_Detection.md — §5 owns the egress visit gate (X-4 changes its H1 rule)
-->

# STATUS — Next conversions (CE-4516 … CE-4522)

Handoff: `HANDOFF_Coordinator_To_Backend_Next_Conversions.md` (dispatched at `90416dc3e`). Branch `exp/next-conversions`, started from
`exp/threaded-build@1c07af9f0`.

| item | tracker | state | result |
|---|---|---|---|
| X-1 | CE-4516 | ✅ | one `lock (bodies)` around the line-of-fire walk queued every shooter — lock-free walk; 20.1 → 9.4 ms at 4 threads (4 now beats 1). Left: the serial pre-pass over mounts / barrels / loadouts (X-5 lever ②) |
| X-2 | CE-4517 | ✅ | not the batch size first: ~45 % of the squad jobs was `PartIndex.PartsOf` under one lock — lock-free once prepared; plus `ICoarseJob` (2 a batch). Squads 5.2 → 4.1 ms; BrainTick 39 → 24 ms (it read the same index) |
| X-3 | CE-4518 | ✅ | the staff composer a per-commander phase, launches bucketed — 3.6 → 2.3 ms; ⭐ `HROT_READ_CHECK=2` survey clean for this batch (below) |
| X-4 | CE-4519 | ✅ | the gate skipped 0: H1 forced a full scan on any ownership write anywhere — per entity now; repeat ownership writes skipped; managed inputs by reference. Full scans 5 281 → 30, skipped 0 % → 75 %; egress 30.3 → 23.4 ms. The EQS samples themselves are real config changes (re-aims, suspend toggles) |
| X-5 | CE-4520 | ✅ | a plateau explained: ≈ 48 ms serial floor (egress 23 ms the largest) — levers named |
| X-6 | CE-4521 | ✅ | stepped gate EQUAL at 1 / 4 / 8 · threaded session ×2 ok, 0 guard hits · 0 B rails |
| X-7 | CE-4522 | ✅ | numbers before / after (below), the Windows commands, folded into Parallel Simulation §18.8 and Egress §5c |

**CGF frame, 1 000 men, 4 threads: 140.9 → 99.6 ms (14.2 → ~20 fps).**

## Baseline (VM, Release, 4 threads, `HROT_PARALLEL_SIM=1`, 1 000-man demo-city, `scripts/scale-rig.py`)

| CGF | ms / frame |
|---|---|
| work | 140.9 (14.2 fps) |
| BrainTickSystem | 39.2 |
| WeaponDispatcherSystem | 20.1 |
| CycloneEgressSystem (two passes) | 14.0 + 12.8 |
| egress, EqsSensorConfigTopic | 11.5 (19 165 samples) |

## X-1 · CE-4516 — the weapon dispatcher

**Cause (measured, `dotnet-trace`, 12 s, 4 threads):** the dispatch phase spreads evenly over the 4 threads, but ~73 % of each thread's
time is `LineOfFire.Blocked` (friendly / neutral on the line, called by `AimAndFireExecutor`), and both checks held `lock (bodies)` — ONE
lock per world — across the grid refresh, the segment walk and the per-body rule. Every shooter of the phase queued on it.

**Fix:** the lock covers only `Bodies.Refresh` (the frame's grid build, once per frame). The walk is the new
`FrameEntityGrid.AnySegmentCandidate<T>` — the same cells and reach as `Segment`, writes nothing (no shared `Found` buffer), stops at the
first accepted candidate; the per-body rules moved into struct `ICandidateTest`s (`FriendOnLine`, `NeutralOnLineTest`) — the M-5 precedent
(`UnderFireMoveSystem.FriendNear` over `AnyCandidate`). The answer is a boolean OR over the candidates ⇒ order-free, deterministic.

**Rails:** `FrameEntityGridTests.CE4516_TheLockFreeSegmentWalk_AnswersAsTheSegmentWalk_FromManyThreadsAtOnce` (vs `Segment` for 300
lines incl. a late joiner; 8 threads × 20 rounds; 0 B). 🔴 red-proved: the late list not walked ⇒ fails. The line-of-fire and weapon rails
(`AimAndFireExecutorTests` incl. CE-2411's full-scan equivalence, `WeaponInteractionDispatcherTests`, `DroneImpact…`, grid): 156 / 0
serial and 156 / 0 with `HROT_PARALLEL_SIM=1`.

## X-4 · CE-4519 — CGF egress, EqsSensorConfigTopic

**What the samples are (ddsmonitor 1.2.20, 30 s):** 12 964 samples on 6 350 keys; 4 712 consecutive-sample changes are
`(ContextSlot1NetworkId, Epoch)` — four child sensors re-aimed together (1 192 each), 804 of them A→B→A flips; child 5 toggles `Suspended`.
⇒ real config changes the brain makes, not a publish defect.

**Where the ms go (trace):** the per-sensor scan, not the writes — DDS writes ~15 % of the translator; the rest is the per-sensor bookkeeping
for ~10 k sensors every frame. The visit gate that should skip untouched sensors (CE-2407) skipped **0**: `HROT_EGRESS_GATE_STATS=1` —
begins 27 361, full 5 281, **5 205 forced by a `DescriptorOwnership` write anywhere**, non-full visits 0 — the open CE-3442.

**Fix (two halves):**
1. `OwnershipApplier.Apply` writes nothing on a REPEAT (same owner, nothing pinned) — neither the record nor `NetworkAuthority`.
2. `EgressVisitGate` H1 is per entity: it remembers, per entity, the three facts `AuthorityExtensions.HasAuthority` reads — its
   `PartMetadata` root, the root's `NetworkAuthority` bytes, the root's `DescriptorOwnership` reference (immutable, replaced whole) — and
   visits when one differs. A table move anywhere no longer forces a full scan.

**Gates (X-1 + X-4, `--no-build` after building the test projects):**

| suite | result | note |
|---|---|---|
| `Fdp.Toolkits.Tests` (full) | 4 967 / 3 / 1 skip | the 3: `S0ComponentTypesRailTests` (`TerrainBlock/TerrainFeature.HeadingDeg`) and `S0IdTypesRailTests` (`Orchestration.PlacedFile/PlacementNode.NodeId`) name types in files this branch does not touch (`git diff be0f264f7` — none) ⇒ pre-existing; `WeaponAmmoMirrorSystemTests.CE4366_ASteadyFrame_AllocatesNothingPerUnit` green alone (suite interference on a per-thread allocation counter) |
| `Hrot.Network.NED.Tests` | 198 / 0 | the gated translators' users |
| `Hrot.Animation.Replication.Tests` | 48 / 0 | `StanceIntentEgressTranslator` is gated |
| X-4 red-proof | `AuthorityMoved` off ⇒ H1 + the authority equivalence rail fail (2 / 12) | |
| X-1 red-proof | the late list not walked ⇒ CE4516 fails | |

## SimHost gate — what is pre-existing and what this batch caused *(measured `2026-11-13`)*

| run | base `be0f264f7` sources | `HEAD` | verdict |
|---|---|---|---|
| SimHost, the touched classes (`ParallelBrainsDeterminism · CgfLogicPack · Callouts · AirDefenceNodes · SquadManeuver · StressSystem · Staff · Report`), `HROT_PARALLEL_SIM=0` | 335 / **1** | before the isolation fix 307–309 / 27–29 · after **335 / 1** | the 1 = `CE454_TheSquadLayerRunsThroughTheRealCgfPack` (0 contacts merged, 2 expected) — pre-existing |
| SimHost, `SquadManeuver · CgfLogicPack · ScenarioRules`, `HROT_PARALLEL_SIM=1` (process-wide) | 238 / **146** | ⏳ re-run pending — ⛔ the first "minus X-3 / minus X-2" numbers (147–148) are VOID: commit `99febbce8` had swept the bisect's staged base files into HEAD, so those steps ran base sources | at base the suite does not run under the process-wide switch; parallel behaviour is gated by each feature's own serial-vs-parallel rails |

⭐ **The 27–29 were test ISOLATION, not a defect in X-2 / X-3** (CE-4518): xUnit runs collections concurrently; `CalloutsTests` /
`StressSystemTests` flip the process-wide `ParallelEntities.Enabled` and resize the one worker pool, and once the staff composer read that
switch live, `SquadManeuverTests` running beside them took the parallel path on the shared pool. Proof chain: base 1 · HEAD 27–29 · HEAD minus
X-3 → 1 · HEAD minus X-2 → 1 · HEAD with only the pack + manoeuvre classes → 1. Fix: `ParallelSwitchMutatorCollection`
(`DisableParallelization`), the `ComponentTypeRegistryMutatorCollection` precedent.

⚠ **Process note:** `git checkout <sha> -- <file>` STAGES the old file, so a commit made during a bisect swap carries it. `99febbce8` reverted all
seven X-1…X-4 source files that way; `<next commit>` restores them from `94205eefd`. Later swaps use `git show <sha>:<file> > <file>` (index untouched).

## X-2 · CE-4517 and X-3 · CE-4518 — built and verified at `HEAD` *(timings: the rig run, next)*

**X-3 — the staff composer is a per-commander phase.** A commander writes only its own `ReportOutbox`; the outbox add is a serial pre-step;
the heard launches are sorted by commander once a frame (stable insertion sort) and each commander reads its own run by binary search
(was every unit × every launch); reports go up as managed events, which a phase defers into its batch (`FdpEventBus`, N-10) ⇒ the superiors
receive them in the serial order; `SentCount` is summed from a per-index slot. Rail `StaffComposerParallelTests.CE4518_…`: 60 commanders,
120 frames of sitreps and interleaved launches — the parallel reports equal the serial ones, in order, at 4 and 8 threads, and every commander
whose men heard a launch SPOTs it. 🔴 red-proved: the sort off ⇒ fails (a first version compared serial with parallel only and stayed green
under the bug — both sides ran the same broken lookup; the absolute assertion is what catches it).

**X-2 — coarse jobs name their batch size (`ICoarseJob`).** The squad phase's widest level is ~100 commanders = 4 batches of the fixed 32 ⇒ at
most 4 threads; the levels above one batch. Now `SquadJob` takes 2 a batch, the staff `ComposeJob` 4. Result-neutral by §18.7 M-1 (contiguous
batches merged in batch order = entity order). Rail `ParallelEntitiesTests.CE4517_…`: a coarse job runs in batches of its size in both modes
and its result equals the fine job's, byte for byte, at 1 / 4 / 8 threads. 🔴 red-proved: the hint ignored ⇒ fails.

| gate at `HEAD` | serial | `HROT_PARALLEL_SIM=1` |
|---|---|---|
| `Fdp.Toolkits.Tests` — squad, command, starter pack, threat-eval parallel, frame grid, egress gate, aim-and-fire, weapon dispatcher | 376 / 0 | 376 / 0 |
| `Fdp.Core.Tests` — ParallelEntities, PerEntityRule | 15 / 0 | 15 / 0 |
| `Hrot.SimHost.Tests` — the touched classes | 335 / 1 (`CE454`, pre-existing) | — |
| `Hrot.SimHost.Tests` — SquadManeuver + CgfLogicPack + ScenarioRules | — | 239 / 145 (base: 238 / 146 — pre-existing; replaces the void row above) |

## Numbers — the 1 000-man demo-city rig (VM: 4 cores, Release, `HROT_PARALLEL_SIM=1`, `scripts/scale-rig.py`) *(`2026-11-13`)*

| CGF, ms / frame | before, 4 threads | after, 4 threads | after, 1 thread |
|---|---|---|---|
| **work (the frame)** | **140.9** (14.2 fps) | **125.5** (16.3 fps) | 151.2 (13.4 fps) |
| BrainTickSystem | 39.2 | 32.1 | 53.6 |
| WeaponDispatcherSystem (X-1) | 20.1 | **12.2** | 15.9 |
| CycloneEgressSystem, both passes (X-4) | 30.3 | 28.4 | 25.9 |
| StaffComposerSystem (X-3) | 3.6 | **2.5** | 3.8 |
| SquadCoordinationSystem (X-2) | 5.2 | 5.1 | 6.1 |
| StressSystem | 8.3 | 8.9 | 11.9 |

| item | verdict from these numbers |
|---|---|
| X-1 | ✅ the lock was the cause: at 4 threads 20.1 → 12.2 ms; now 4 threads beat 1 (12.2 vs 15.9 — before, the queue made 4 threads WORSE). The remaining 1.3× is the serial pre-pass (mounts / barrels / loadouts over every weapon) — next |
| X-3 | ✅ a phase now: 3.8 → 2.5 ms (1 → 4 threads) |
| X-2 | ⛔ the batch size was NOT the limiter: 6.1 → 5.1 ms (1.2×). Profile next: the serial tail (`SquadSensing.UnderFire`, `Changes.Rounds`) and the per-level phases |
| X-4 | ⏳ this rig run does not set `HROT_EQS_CONFIG_GATE=1`, so the EQS translator's own gate (the 11.6 ms) is not exercised; the gated translators' share shows in the egress total. Measured next with the gate on and `HROT_EGRESS_GATE_STATS=1` |

**Tracker rows:** CE-4516 … CE-4522 live on `coordinator`, not on this branch (cut from `exp/threaded-build` before they were written). ⭐ Decision:
their state is kept in this file's item table for the merging lane to tick, rather than merging `coordinator` here for seven rows.

## Second round — what the profile at 4 threads showed, and the two fixes *(`2026-11-13`)*

| finding (gate on: `HROT_EQS_CONFIG_GATE=1`, `HROT_EGRESS_GATE_STATS=1`; `dotnet-trace` 12 s of the CGF) | fix |
|---|---|
| ⭐ H1 per entity works: begins 30 931, **full 30** (was 5 281), visited 4.15 M, **skipped 4.13 M**; CGF 112.5 ms (17.0 fps). The largest non-full visit reason: **managed input 2.08 M** — a managed input's chunk is 16 384 entities, so one sensor's new `SensorConfigPayload` visited every sensor | **X-4:** a managed input is compared by REFERENCE (an immutable record, replaced whole — CLAUDE.md, CE-4046); the chunk stays the cheap first filter. Rail `AManagedInput_IsComparedByReference_…` 🔴 red-proved |
| ⭐ the squad phase DOES spread (main 378 ms, workers 232–240 ms per 12 s) and its serial tail is tiny (`UnderFire` 7 ms, `Rounds` 5 ms); but **~45 % of the squad jobs' time is `PartIndex.PartsOf`** (via `SquadSenseChanges.Structure` → `EqsChildSensor.Find`) — every read under ONE `lock (index)`, the X-1 shape | **X-2:** a fresh index with no late parts is read LOCK-FREE; its stamps go up only after a build completes (`Volatile`), late parts keep the locked path; `SquadCoordinationSystem` prepares the index serially before its phases (the brains' `PrepareFrame`, N-11). Rail `CE4517_APreparedIndex_IsReadFromManyThreadsAtOnce_AsTheScan_WithoutABuild` (8 threads × 50 rounds vs the scan, no build, 0 B, a late part still found) |

| gate | result |
|---|---|
| `Fdp.Toolkits.Tests` — gate, parts index, squad, frame grid, aim-and-fire, weapon dispatcher, ownership, EQS child sensor | 388 / 0 serial · 388 / 0 `HROT_PARALLEL_SIM=1` |
| red-proof: the managed compare off (the old rule) | ⛔ the new rail fails (1 / 13) — as it should |
| `Hrot.Network.NED.Tests` · `Hrot.Animation.Replication.Tests` (the gated translators) | 198 / 0 · 48 / 0 |
| `Hrot.SimHost.Tests` — the touched classes + EQS | 383 / 1 (`CE454`, pre-existing) |

## Numbers after round two — 1 000 men, VM 4 cores, Release, gate on (`HROT_EQS_CONFIG_GATE=1`) *(`2026-11-13`)*

| CGF, ms / frame | before (4 thr) | round 1 + gate (4 thr) | **round 2 (4 thr)** | round 2 (1 thr) |
|---|---|---|---|---|
| **work** | **140.9** (14.2 fps) | 112.5 | **99.6** (~20 fps) | 135.4 |
| BrainTickSystem | 39.2 | 28.1 | 23.8 | 45.7 |
| CycloneEgressSystem (both passes) | 30.3 | 27.1 | 23.4 | 22.6 |
| WeaponDispatcherSystem | 20.1 | 10.9 | 9.4 | 14.6 |
| SquadCoordinationSystem | 5.2 | 5.0 | 4.1 | 5.4 |
| StaffComposerSystem | 3.6 | 2.8 | 2.3 | 3.9 |

Gate (4 threads): begins 29 686, **full 30**, visited 1.76 M, **skipped 5.27 M (75 %)**; managed-input visits **0** (was 2.08 M). ⚠ The BrainTick drop
(39 → 24 ms) is the parts index too — the brains read `PartsOf` and queued on the same lock. ⚠ Egress is lower at 1 thread than at 4: it is
serial either way and shares the VM's 4 cores with the worker pool, the SimHost and the orchestrator processes.

## X-6 · CE-4521 — it really works

| gate | result |
|---|---|
| ⭐ the stepped gate `HROT_PARALLEL_SIM=1 python3 scripts/stepped-hash.py --threads 1,4,8` (mode 3, `scale-rig-demo-city-1000-2s`, 10 079 entities, sim time 2 s) | ✅ **STATE EQUAL** — 1 vs 4 and 1 vs 8 threads: 0 types differ, 0 `SimTransform` rows differ |
| the threaded 1 000-man session ×2 (mode 3, 4 threads, `HROT_PARALLEL_SIM=1`, `HROT_WORLD_GUARD=1`; `scripts/access-trace-session.py --no-trace`: load · select · panels · perspectives · play · stepping · 3-D · edits · orders · hot reload · Edit-state edit + undo / redo · the N-15 drags while playing · a mid-run load · save) | ✅ A and B: every step **ok**, exit 0, WorldGuard **0** distinct hits — both |
| 0 B per steady frame | ✅ the rails: `CE4518_ASteadyFrame_AllocatesNothing` (staff composer, serial and phase), `CE4516_…` (the lock-free segment walk), `CE4517_APreparedIndex_…` (the lock-free parts read), `AQuietWorld_…` (the gate), `P0_ASteadyRun_AllocatesNothing` (the loop) |

## X-5 · CE-4520 — why the tick plateaus: the breakdown (1 000 men, 4 cores, Release, gate on)

| the 4-thread frame (99.6 ms) | 1 thread | 4 threads | |
|---|---|---|---|
| ⭐ the systems that scale (> 1.3×: BrainTick 1.9×, StaffComposer 1.7×, Stress 1.6×, WeaponDispatcher 1.6×, ThreatEvaluation 1.5×, SquadCoordination 1.3×) | 88.1 | ~51 | 1.7× on 4 cores |
| ⛔ **CycloneEgressSystem — serial** | 22.6 | **23.4** | the largest serial piece |
| ⛔ CalloutSystem · CycloneNetworkIngress · CycloneNetworkCleanup — serial | 10.1 | 9.7 | |
| ⛔ ~40 small serial systems (each ≤ 1.2 ms) | ~15 | ~15 | |
| **the frame's floor with infinite threads** | | **≈ 48 ms** | Amdahl: half the 4-thread frame does not scale |

⭐ **Verdict — a plateau explained, with the levers named:** ① **egress off the frame** (23 ms serial): the replica path already designed and
approved as an experiment — [Egress §7, CE-3427](../../DESIGN_Egress_Change_Detection.md); ② the scaling phases reach only 1.5–1.9× on 4 cores
— the VM's 4 cores also run the SimHost and orchestrator processes, and the phases keep serial pre-passes (the weapon dispatcher's mounts /
barrels / loadouts over every weapon) and per-level waits (squads); ③ the ~40 small systems are each too small to convert one by one — the
lever there is interleaving / quantizing (§8 of the Parallel Simulation design), not more threads. ⛔ No defect found in the scheduler: the
"unaccounted" time (frame − sum of systems) is 0.0 ms at 1 and 4 threads.

## X-7 · CE-4522 — the Windows command (1 … 12 threads)

```bat
rem the threaded editor sweep (the W-1 measurement, now with these conversions), 1 000-man demo-city:
set HROT_PARALLEL_SIM=1
set HROT_EQS_CONFIG_GATE=1
dotnet build Hrot\Runner\Hrot.ClusterRunner\Hrot.ClusterRunner.csproj -c Release
python scripts\threaded-scale-measure.py --out %TEMP%\nc-scale --threads 1,2,4,6,8,10,12 --dll Hrot\Runner\Hrot.ClusterRunner\bin\Release\net8.0\Hrot.ClusterRunner.dll
```
```powershell
# the CGF node per thread count (the numbers in this file come from this rig on the VM)
$env:HROT_PARALLEL_SIM=1; $env:HROT_EQS_CONFIG_GATE=1
$env:HROT_RUNNER_DLL="$PWD\Hrot\Runner\Hrot.ClusterRunner\bin\Release\net8.0\Hrot.ClusterRunner.dll"
foreach ($t in 1,2,4,6,8,10,12) { $env:HROT_SIM_THREADS=$t; python scripts\scale-rig.py --terrain demo-city --men 1000 --json "$env:TEMP\nc-rig-$t.json" }
```

## The read-check survey (`HROT_READ_CHECK=2`, 1 000 men, 4 threads, Release) — X-3's gate, and what it caught

| run | undeclared cross reads in a phase | |
|---|---|---|
| first | 5: `VehicleParams` · `PassengerBuffer` · `CarrierStations` (the staff composer's SPOT → `EquipmentOf`: is the contact a vehicle, can it fire at aircraft) · `PartMetadata` (`PartIndex.TakeLateAdds` inside the WEAPON phase — it never prepared the index) · `Gesture` (the brains' `EnemyStrengthRatio`) | ⛔ my earlier "not re-run, no new cross read" was WRONG — three of the five were X-3's |
| fix | `ComposeJob : IStableReads` declares the three contact types (nothing in the staff phase writes them); `WeaponDispatcherSystem` calls `PartIndex.PrepareFrame` before its phase (as the brains and squads do) | |
| after | **1: `Gesture`** — the brain phase's utility input, not touched by this batch | ✅ clean for X-1 … X-4 |

⚠ One run in between used a build that did not compile (`CarKinem.Core` resolves inside `Fdp.Toolkit` — fixed as `global::`), so its numbers were void
and are not quoted. ⚠ `CE4518_ASteadyFrame_AllocatesNothing` failed 1 run in 5 (24 B): the measured loop also set the `GlobalTime` singleton —
now only the composer is measured, warmed 200 frames: **0 / 20 failed**.
