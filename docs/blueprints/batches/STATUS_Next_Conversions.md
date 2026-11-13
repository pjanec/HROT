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
| X-1 | CE-4516 | 🔨 | the weapon phase's shooters queued on ONE lock in `LineOfFire` — fixed (lock-free walk) |
| X-2 | CE-4517 | ⏳ | |
| X-3 | CE-4518 | ⏳ | |
| X-4 | CE-4519 | 🔨 | the egress gate skipped nothing: any authority write anywhere ⇒ a full scan — made per entity; repeat ownership writes dropped |
| X-5 | CE-4520 | ⏳ | |
| X-6 | CE-4521 | ⏳ | |
| X-7 | CE-4522 | ⏳ | |

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
