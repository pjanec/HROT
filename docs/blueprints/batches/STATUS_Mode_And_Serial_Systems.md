<!--STATUS
state: DONE — M-1 … M-9 (CE-4480 … CE-4488) built, gated, measured, folded; the residual (Entity padding) → CE-4507
updated: 2026-11-13
current-answer: the whole file (NEXT is the resume point)
related-designs:
  - HANDOFF_Coordinator_To_Backend_Mode_And_Serial_Systems.md — the work M-1 … M-9 (CE-4480 … CE-4488), scope frozen at ffc802ab2
  - NOTE_Backend_To_Coordinator_Parallel_Switches_And_Throughput.md — the findings it answers (D1–D5)
  - STATUS_Overnight_Threaded_Build.md — the overnight build this continues (D-1 … D-43, the gate commands)
  - ../../DESIGN_Parallel_Simulation.md — §10–§11 the pre-score, §18 the per-entity rule
-->

# STATUS — the mode set by what is running, the pre-score retired, the serial systems converted

Branch `exp/threaded-build`. Rulings R-4218 / R-4219 (R-4207, R-4215).

## 1. DONE

| item | what | commit |
|---|---|---|
| M-1 `CE-4480` | ⭐ the batch size does NOT change the result: the stepped 1 000-man run with the adaptive batch `max(32, n/(threads×4))` gives the same world at 1 / 2 / 4 / 8 threads AND the same world as the fixed 32 batch (every type equal but `EntityGuid`, minted at random per run). By construction: contiguous batches merged in batch order = entity order | (measurement) |
| M-2 `CE-4481` | ⭐ the mode is set by what is running: every kernel VOTES at its tick boundary (its clock stepped ⇒ Deterministic — stepping, Run-to, filming and the regression all step it), a harness may PIN Deterministic (`SimulationModeSelector.RequireDeterministic`, held by `HeadlessTestExecutor`); the process value = Deterministic when any vote or pin, else Throughput — ONE value for the process, `--mode all` included. `HROT_SIM_MODE` removed (code + `parallel-sim-measure.py`); `ParallelEntities.Mode`'s comment true | (this commit) |
| M-4 `CE-4483` | ⭐ measured + the serial work outside the systems cut: the ranked tables (§6), the profile (M-4b), the registry lock read lock-free (M-D6), the module snapshots recycled and synced incrementally (M-D7), the callouts' no-echo check from a grid (M-D8) | `f92849a9a` … (this commit) |
| M-3 `CE-4482` | `UtilityPrescore` retired: the class, its two `UtilityScorer` hooks and helper, the `BrainTickSystem` call, `HROT_BRAIN_PRESCORE`, its two rails (Q4 / T3 — "pre-score == serial score", now covered by the brain phase's own determinism rails); Parallel Simulation §14 / §15 marked SUPERSEDED | (this commit) |

## 2. GATES

| gate | command | result |
|---|---|---|
| ⭐ M-7 the stepped gate | 1 000-man demo-city, mode 3 + `HROT_PARALLEL_SIM=1` (every phase: brains, squads, stress, callouts, threat, the weapon dispatcher, movement), Release, 120 steps from paused with two orders, `GET /world/hash?byType=1` (field bytes only) at 1 / 2 / 4 / 8 threads | ⭐ **STATE EQUAL** (`EntityGuid` excluded — minted at random) after M-D9 (the hash counted padding), M-D10 (the weapon phase's race + the declaration epoch), M-D11. Switch off, two identical runs: STATE EQUAL. Then seven more 4-thread runs: `WeaponChannel` differed in 3 — 📐 the row bytes: ONLY offsets 63–64 = `State[14..15]` = bytes 6–7 of `AimState.Target`, an `Entity` (int Index · ushort Generation · **2 bytes of padding**) — padding carried INTO an opaque byte buffer, which no field mask can see (M-D12) |
| ⭐ M-7 the threaded session ×2 (`scratchpad/run-session.sh`: mode 3, `HROT_SIM_THREADS=4`, `HROT_PARALLEL_SIM=1`, world guard on, 1 000 men: load · select · play · pause · step · 3-D · spawn / edit / delete · orders · Edit-state edit + undo / redo · the N-15 drags while playing · a mid-run load · save) | A and B | every step **ok**, WorldGuard **0** hits, **0** faults — both |
| ⭐ M-7 `--mode all` (`scratchpad/mode-all-gate.sh`, windowed under xvfb, 1 000 men live, 2 min PLAYING, `HROT_PARALLEL_SIM=1`) | 12 × 10 s samples | alive, **0** faults, simTime 0 → 126.7 s, entities 4 042 → 7 546 |
| ⭐ M-7 Throughput continuous play, 5 min (`threaded-scale-measure.py --threads 4 --play-seconds 300`, Release) | | **0** faults; tick 203 ms, UI 53 ms playing |
| ⭐ M-7 switch-OFF sweep (whole suites, `--no-build`) | `Fdp.Core.Tests` · `Fdp.ModuleHost.Tests` · `Fdp.Toolkits.Tests` | 1 365 / 3 ⚠ pre-existing (the source-policy scanners — files this batch never touched) · 266 / 0 · 4 964 / 4 ⇒ S0 ×2 pre-existing (recorded overnight); `FdpOrchestrationCqrsStructTests.TakeCheckpointIntent_…` a STALE rail (CE-1467 in the base added the `Note` field, the rail was not updated) ⇒ updated to the as-built shape; `MissionDirectorSystemTests…DoesNotAdvance…` order-dependent, green alone and with its class (35 / 0). The 0 B rails are in these suites (green) |
| M-6 rails | Core `PerEntityRule|EntityCensus` · Toolkits `AimAndFire|WeaponInteractionDispatcher|Weapon|Combat` serial AND `HROT_PARALLEL_SIM=1` · SimHost `ActionDispatchModule|AirAttackNodes|CgfLogicPack|WeaponSystem|Station` serial AND parallel | 7 / 0 (+ M6 rail red-proved: the owner test dropped ⇒ red) · 574 / 0 both · 68 / 1 both (`CE454`, pre-existing) |
| M-5 FriendNear | SimHost `Stress|UnderFire|Callout|ParallelBrains|FrameEntityGrid` serial · parallel · Toolkits `FrameEntityGrid` | 49 / 0 · 48 / 1 (`CE2261_BC5`, pre-existing — §5) · 1 / 0 |
| M-4 rails | `Fdp.ModuleHost.Tests` (whole) · `Fdp.Core.Tests` (whole) · SimHost `Callout|Eqs|ParallelBrains|Sensor` · Toolkits `Replay|Keyframe|Snapshot` | 266 / 0 · 1 360 / 3 ⚠ pre-existing (source-policy scanners naming files this batch never touched: `BridgeRoads.cs`, `DeployProcessorSystem.cs`, `TerrainContentHash.cs` …) · 69 / 0 · 401 / 0. ⭐ `SnapshotRecycleTests` 6 — red-proved: the force dropped, the epoch check dropped, the invalidation dropped, the lock-free path dropped ⇒ each its rail red. `CalloutsTests.M4_TheNoEcho…` green |
| M-1 stepped gate, adaptive vs fixed batch | `SAVE=… HROT_SIM_MODE=throughput python3 scratchpad/stepped-hash.py 1,2,4,8` (the old switch, on the binary before M-2) vs `stepped-hash.py 1` (fixed 32) | STATE EQUAL across all five runs, type by type |
| M-2 rails | `dotnet test FDP/Engine/Fdp.ModuleHost.Tests --no-build --filter "…SimulationModeFromWhatRuns|…PausedAndStepped|…ModuleHostKernelTime"` | 24 / 0; red-proved (the kernel's `Vote` dropped ⇒ both R4218 rails red) |
| M-3 suites | Toolkits `Utility|Behavior|Squad` · SimHost `ParallelBrains|Utility|Brain|Sop` | 974 / 0 · 114 / 0 |

## 3. DECISIONS

| # | question | decision | where |
|---|---|---|---|
| M-D1 | the handoff points the pre-score's record at Parallel Simulation §10–§11 | ⭐ it is §14 (POC 2) and §15 (POC 3) — §10–§11 never mention it; those are the sections marked superseded | design |
| M-D3 | how does the kernel know "stepped / Run-to / filming / regression"? | ⭐ all four STEP the clock: Run-to is the ACK-gated stepped run (`RunToController`), the director and the verify run advance by `StepBy` (`DirectorRunner.cs:730/986`) ⇒ the clock's `TimeMode.Deterministic` (`_steppedFrame`, already computed for CE-3292) is the one signal — no per-feature flag | `ModuleHostKernel` |
| M-D4 | "one value for the process" when kernels disagree for a tick (`--mode all`, slaves switching on the master's event) | ⭐ any Deterministic vote wins — the safe direction; a harness pin likewise | `SimulationModeSelector.Vote` |
| M-D5 | "the tests ⇒ Deterministic" | ⭐ an explicit counted pin (`RequireDeterministic`), held by the headless test harness; rails that run `ParallelEntities` without a kernel set the mode directly (unchanged) — ⚠ a test kernel in continuous play votes Throughput, which M-1 shows is the same result | `HeadlessTestExecutor` |
| M-D6 | `NeedsClone` under the registry lock — make the lookup lock-free how? | ⭐ a `bool[MAX_COMPONENT_TYPES]` written ONLY under the lock (registration, `SetNeedsClone`, `Clear`), read with `Volatile.Read`; the dictionary stays the authority for an out-of-range id — the CE-2389 pattern the per-T id already uses | `ComponentType.cs` |
| M-D7 | stop the snapshots' clear-and-copy-everything — how? | ⭐ the replica pattern (`DoubleBufferProvider`, the staging buffers): a recycled snapshot keeps its content and the next sync copies only the chunks the source changed. ⚠ Two traps, each a rail red-proved: ① a background module's write into its view carries the version the view synced at — the same an unchanged live chunk may carry ⇒ the recycle marks every chunk stamped since that sync never-equal; ② a source REPLACED rather than advanced (a load's clear, a playback frame, a rewind into it) repeats version numbers with other content ⇒ a new `ContentEpoch` (bumped by `Clear`, `SetGlobalVersion`, `ResetGlobalVersion`, a sync INTO the world) and a full copy when it, the source or a non-decreasing version differ. 📌 Measured on the way: the first two forms (force every chunk; force native, clear managed) saved little — the cost was copying every chunk, not the decommit. A new `RecycleForSync`, ⛔ not `SoftClear` (that one EMPTIES a world: the scenario file service, playback); used only by the two pools whose next use is always a sync. ⚠ The price, measured: the pool now KEEPS its memory — 221 MB across 15 pooled worlds (3 of them used) at 1 000 men, where a cleared one held ≈ 0 | `EntityRepository.Sync.cs`, `SnapshotPool`, `OnDemandProvider` |
| M-D8 | the callouts' serial say-pass — parallelise it? | ⭐ no: it is order-dependent BY DESIGN (AQ105 E — who spoke first in the 10 m / 2 s window; N-11's serial follow-up). Its cost was the algorithm: the no-echo check walked every speaker per call ⇒ the CE-2411 per-frame grid (`FrameEntityGrid`, as `UnderFire.FriendNear`), radius + `MovedSlackMetres`, each candidate re-read live with the exact 3-D rule | `Callouts.cs` |
| M-D9 | M-7's stepped gate (1 000 men, 1 / 2 / 4 / 8 threads) differs — is it the thread count? | ⭐ NO, and NOT state at all: TWO IDENTICAL 1-thread runs differed in the same types (`NavigationApplied`, `NavigationIntent`, `ScenarioClock`, `Utterance`; positions equal), every switch OFF, already at `38f1d1191`. 📐 The row bytes (`GET /world/hash?rows=<T>&bytes=1`) showed ONLY PADDING differing — `ScenarioClock` bytes 10–12 (after its long and short), `Utterance` 1–3 and 10–12 — holding leftovers of pointers: a padding byte is never written by a field assignment. ⇒ the gate hashed history, not state. ⭐ Fixed AT THE GATE: `ContentHash` counts field bytes only (the managed layout, found by writing each leaf field of a zeroed box). Then the same two runs: **only `EntityGuid` differs (random by design) ⇒ STATE EQUAL.** 📌 The earlier "equal" runs (N-15, M-1) were the 300-man rig — the script hard-coded it. ⇒ the Windows F-2 set (`Utterance`, `NavigationApplied`, `NavigationIntent`, `SquadDrillState`) is very likely the same artefact — confirm there (F-3) | `EntityRepository.ContentHash.cs`, `WorldContentHashTests` |
| M-D10 | the gate's 4-thread run differed once in `WeaponChannel` — where from? | ⭐ the read-check survey (`HROT_READ_CHECK=2`, 1 000 men, 4 threads) named the weapon phase's undeclared cross reads: the TARGET's `WeaponPosture` (`ForceLevels.AimsAt`, written by the target's own job — a race on the engagement mount) and `StanceIntent` ⇒ tick-start view; who controls a weapon (`WeaponSystemParams`, `IsEmbarkedTag`, `PassengerBuffer`, `CarrierStations`), the target's `PhysicsCollider` / `VehicleParams` ⇒ stable reads. ⭐ And a phase that declares lazily read live on its FIRST frame (movement's `SimVelocity`): a declaration EPOCH retakes a view taken before the type was declared. `PartMetadata` (callouts → `PartIndex`) is locked and not changed in a phase — benign | `WeaponDispatcherSystem`, `ParallelReadCheck`, `EntityRepository` |
| M-D11 | 13 `SquadCoordinationSystemTests` red with the parallel switch — "event 4006 not registered … cannot playback" | ⭐ a deferred publish replays by type id and needs the stream; the direct publish MAKES the stream ⇒ one rule for both: the deferred path makes the stream at the publish (thread-safe), strict mode still demands registration | `FdpEventBus` |
| M-D12 | a struct's padding written into an OPAQUE byte buffer (`WeaponChannel.State` / `Params`, 32 B each) is hashed as data — fix? | ⭐ every field equal; only `Entity`'s 2 padding bytes inside `AimState.Target` differ (a worker stack's leftover). ⚠ Not fixed silently — the root fix is in the engine's most basic type: **lean: name `Entity`'s two padding bytes (an always-zero field), so an Entity's 8 bytes are canonical everywhere — the hash, the recordings (delta compression sees no false change), the wire**; blast radius: every reflection-based serializer of `Entity`. ⇒ a question for the coordinator (in the handoff report) | — |
| M-D2 | does any host keep SERIAL brains and want parallel scoring (M-3's precondition)? | ⭐ no — measured in code: the pre-score needs `HROT_PARALLEL_SIM=1`, which itself makes the brains a parallel phase (`BrainTickSystem.Parallel`), so it ran only under the measuring switch `HROT_SERIAL_BRAINS=1`; by design R-4214 ① puts the per-entity brain rule in both modes | `BrainTickSystem.cs`, R-4214 |

## 4. BUGS FIXED

| bug | rail |
|---|---|

## 5. OPEN

| item | why | what it needs |
|---|---|---|
| watch: `PerEntityRuleTests.N10_ASteadyRun_…AllocatesNothing` red ONCE (24 B) in 1 of 6 runs, under a concurrent `EventBus` class; green alone ×3, with its class, and the same combination ×2 | 24 B is one bare object: ⚠ hypothesis — `FdpWorkerPool.Run`'s `ManualResetEventSlim` makes its lock object the FIRST time the caller really blocks (a loaded machine) — pre-existing pool code | if it recurs: confirm, then warm the event's lock at pool construction |
| ⚠⚠ `SquadManeuverTests` (228) with the brains a PARALLEL phase (`HROT_PARALLEL_SIM=1`): **78 red** (serial: 228 / 0) | 📐 measured: the BC5 room drills among them are red at `38f1d1191` (before this batch) and green at `ffc802ab2` (before the overnight brain phase); green with `HROT_SERIAL_BRAINS=1`; the SAME at 1 and 4 threads (deterministic); the survey (`HROT_READ_CHECK=2`) finds no undeclared access. ⚠ The harness drives `BrainTickSystem` ALONE — no kernel, no `Tick()`, no Simulation dispatch — so these are serial-order rails; 📌 tried: taking the tick-start view before each brain run (as the kernel does) ⇒ 94 red, WORSE ⇒ reverted. ⇒ open question: are the squad plays per-entity-safe, or does the per-entity rule's one-tick lag on cross-entity effects (R-4212) change what a squad DOES? The rails cannot answer it in this harness | a squad harness that runs the production order (kernel phases, ticks) in both modes, and the drills compared on it; the live gates (M-7: the stepped gate, continuous play, `--mode all`) in the meantime |

## 6. NUMBERS (before / after)

| what | before | after | how |
|---|---|---|---|
| ⭐ M-4 the tick, 1 000-man demo-city, mode 3 + `HROT_PARALLEL_SIM=1`, PLAYING (Throughput by M-2), 4-core VM | **Debug** 1 thread / 4 threads: tick 2 247 / 1 452 ms · **Release**: 192 / **222** ms ⚠ (worse at 4) | Release 1 / 2 / 4: **182 / 205 / 214** ms; BrainTick 44 / 38 / 26; the sim group 116 / 111 / 106; ⚠ the module-snapshot pool keeps 222 MB | `threaded-scale-measure.py --threads 1,2,4` (`HROT_PARALLEL_SIM=1`) |

| ⭐ **M-8 after** — the same rig and switches, every phase on (`HROT_PARALLEL_SIM=1`), after M-4 … M-7 | Release 1 / 4: 192 / 222 ms (M-4) · Debug 1 / 4: 2 247 / 1 452 ms | **Release 1 / 2 / 4: 177 / 190 / 186 ms** (BrainTick 52 / 35 / 26; the sim group 118 / 105 / 96; the weapon dispatcher 13–15) · **Debug 1 / 4: 2 189 / 1 290 ms** · ⭐ the tick no longer gets WORSE with threads; ⚠ a 4-core VM shared with the UI and EQS cannot show the real-core scaling — that is the Windows run's | `threaded-scale-measure.py --threads 1,2,4` |

⭐ **The Windows command (real cores, Release):** `dotnet build Hrot/Runner/Hrot.ClusterRunner -c Release` then
`set HROT_PARALLEL_SIM=1` and `python scripts\threaded-scale-measure.py --out <dir> --threads 1,2,4,6,8,12` — and the determinism gate
`python scripts\stepped-hash.py --threads 1,2,4,8` (exit 0 = state equal).

### M-4 — what is serial in the tick (ms per call, recent average; 4 threads)

| system | Debug 1 → 4 | Release 1 → 4 | per-entity? | writes on OTHER entities |
|---|---|---|---|---|
| `EqsSolverSystem` (background module, its own window on the pool) | 700 → 522 | 342 → 167 | its window (`105705de9`) | — (sensor results on its own carriers) |
| `TogglableSimulationGroup` (73 members) | 1 067 → 917 | 141 → 101 | — | — |
| · `BrainTickSystem` | 461 → 186 | 56 → 26 | ✅ phase | by command |
| · `WeaponDispatcherSystem` | 220 → **276** | 9.9 → 11.1 | ⛔ serial | an operated station's mount `WeaponState`, a turret part's `WeaponTask` (exclusive per seat) |
| · `StressSystem` | 64 → 61 | 17.1 → 11.2 | ✅ phase (gains little) | edges / breaks deferred |
| · `CalloutSystem` | 52 → 50 | 12.6 → 12.9 | ✅ phase (no gain) | follow-ups |
| · `SquadCoordinationSystem` | 47 → 36 | 9.7 → 11.0 | ✅ per level | members (owned) |
| · `CgfThreatEvaluationSystem` | 21 → 14 | 7.6 → 6.1 | ✅ phase | — |
| · the rest (≈ 66 small) | ≈ 120 | ≈ 18 | mostly serial | — |
| `TogglableInputGroup` (31: ray casts, fire processing, ingress) | 171 → 179 | 5.3 → 5.4 | ray casts on the pool | `FireProcessingSystem` writes the struck |
| `TogglablePostSimulationGroup` (movement, ballistics) | 39 → 42 | 8.6 → 8.1 | movement ✅ phase | — |

### M-4b — the sim thread by `dotnet-trace` (Release, 4 threads, playing, 20 s ≈ 90 ticks; ms in the 20 s)

| where | ms | per tick | what it is | ⭐ done |
|---|---|---|---|---|
| `ComponentTypeRegistry.NeedsClone` (under every managed table's `SyncDirtyChunks`) | **2 258** (+ 813 on the UI thread) | ≈ 25 | the global registry LOCK, taken per table per sync — the sim's module-snapshot syncs and staging publish against the UI's pull; ⚠ grows with the threads that contend | ✅ read lock-free (M-D6) |
| `OnDemandProvider.AcquireView` / `HarvestEntry → Clear` (module snapshots) | 2 977 / 778 → **1 786 / 58** | ≈ 33 / 9 → 20 / 1 | every release CLEARED the snapshot and every acquire copied every chunk of every table again (decommit, recommit, every managed reference chunk) | ✅ recycled + incremental (M-D7) |
| `StagingWorlds.Publish` | 1 708 → **1 499** | ≈ 19 → 17 | the UI's copy (the sampler put part of its copy under the lock frame) | ✅ by M-D6 |
| `HrotNodeContext.StepTimeline` (the live timeline's lanes) | 767 | ≈ 8 | serial on the sim thread | ranked (next) |
| brains · callouts · weapon dispatcher · stress · squads | 2 852 · 1 344 · 1 297 · 1 182 · 1 041 | | callouts: 986 of 1 344 is the SERIAL say-pass after the phase, of which `SaidNearby` walked every speaker per call (548) | ✅ `SaidNearby` from a grid (M-D8) |
| `EqsSolverSystem` | 9 221 — on .NET pool threads, NOT the sim thread | | background module ≈ ½ core continuous + its window on the pool | — |

⭐⭐ **Why 4 threads were slower than 1 on the 4-core VM:** ① the registry lock above — more threads syncing, more contention; ② the machine is
oversubscribed: sim thread + 3 pool workers + the UI thread + EQS on the .NET pool = 6 busy threads on 4 cores (the pool's default budget is
cores − 2 for this reason; `HROT_SIM_THREADS=4` overrides it).

⭐⭐ **M-4 verdict.** The serial work OUTSIDE the systems is what the profile named, and the fixes cut it ≈ 2.3 s in 20 s (≈ 25 ms a tick):
the snapshots now sync incrementally, the registry lock is gone. ⚠ **The tick still gets slower with threads on this VM while every system gets
faster** — the sim thread is CPU-bound (it waits on the pool only ≈ 1.3 s in 20 s), and on 4 cores the threads that help the systems (the pool
workers, EQS's window on them, the UI thread) take cores from the serial sim thread. ⇒ the next serial wins are the systems themselves (M-5) and
the live timeline's scan (≈ 9 ms a tick); the thread-count question on a real machine is Windows F-4 (`HANDOFF_…_Windows_Findings`, CE-4504).

⭐⭐ **The Release reading changes the plan:** every kernel system together is ~120 ms of a **222 ms** Release tick at 4 threads, and the tick got
WORSE with threads (192 → 222) while the systems shrank (group 141 → 101). ⇒ the largest serial cost in Release is OUTSIDE the systems — being
profiled (`dotnet-trace`, M-4b). The Debug ranking (weapon dispatcher 276 ms) is Debug overhead: 11 ms in Release.

## NEXT (the resume point)
⭐ DONE. Follow-ups: the Windows findings (`STATUS_Windows_Findings.md`, CE-4501…CE-4506), then Entity padding + squad drills (CE-4507…CE-4510).
Open here: §5 (the squad rails under parallel brains → CE-4508 / CE-4509; the N10 watch item).
