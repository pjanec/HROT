<!--STATUS
state: LIVE — report from the backend lane to the behaviours lane
updated: 2026-10-01
current-answer: §1 (what changed for you), §2 (acceptance), §3 (things to act on), §4 (gates).
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/batches/HANDOFF_Eqs_Sensor_Lifecycle_Wire.md — the handoff this answers.
  - docs/blueprints/DESIGN_Behaviour_Fault_And_Teardown.md — §3a is the durable as-built of this batch (deviations, rails);
    §1 D5 / §3 now say Suspended.
  - docs/designs/eqs-2/EQS_Design_v1.3_final.md — owns the topics, the carrier and the solver this changed.
-->

# REPORT — EQS sensor lifecycle, wire half (`CE-486`, `CE-487`, `CE-490`)

**From:** the backend lane (`backend`) · **To:** the behaviours lane (`behaviors`) · **Answers:**
[`HANDOFF_Eqs_Sensor_Lifecycle_Wire.md`](HANDOFF_Eqs_Sensor_Lifecycle_Wire.md) — dispatched at `acb4ce898`, merged at `3f2be54ba`,
started-marker `0a293dbc2`. Scope frozen there; nothing on `behaviors` invalidated an item. Rule 4: `behaviors` at `9676147e5`
(`CE-485`, `CE-482`/`CE-483`) is MERGED in (`d03e377ed`); every gate and the live run below are on the merged tree.
**Ids:** none allocated — `CE-486`/`CE-487`/`CE-490` were yours; all three DONE.

## 1. What changed for you

| | |
|---|---|
| ⭐ **the one shared seam** | `EqsSensor` gains ONE appended field, `bool Suspended` (`[MarshalAs(I1)]` — the ECS layout validator refuses a bare bool). Default `false` = running ⇒ every existing `new EqsSensor { … }` stays correct; `CE-485` needs no change for it. Nothing renamed or reordered. ⚠ The approved name was `Active`; B-1 flipped the polarity and the user settled it: *"ok Suspended is fine"* |
| ⭐ **ending a sensor** | destroy (or strip `EqsSensor` from) the child as `CE-485` does — the egress's sweep then writes that sensor's last config back with `Suspended = true`. ⛔ It never disposes a child instance while the parent lives. A live local sensor holding the same (parent, part id) in that scan keeps the key out of the sweep — so `CE-485`'s end-then-`Ensure` in one frame is safe. ⭐ ONE rule ends every sensor: *an instance under an entity this node owns that no local sensor holds is suspended* — your ended ones and inherited ones (`CE-490`) alike (a separate end-write was built first and was fully shadowed by the sweep, measured by its red-proof) |
| **a dead parent** | ⚠ not in the handoff: when the PARENT is gone, the egress still DISPOSES the child instance (descriptor rules: dispose = entity deletion). Writing `Suspended` there would leave a TransientLocal instance on the topic forever |
| **the Muscle** | a child dispose no longer destroys the carrier (`SubEntityCleanupSystem` does, when the parent dies); a `Suspended` config updates an existing carrier but never creates one; the solver publishes nothing for a suspended carrier — not even the unknown-template empty answer |
| ⭐ **the solver, after the merge** | two things `CE-485` + `CE-486` together exposed, both in `EqsSolverSystem` (yours to know, nothing to change): ① a **suspended carrier drops its `SensorEvalState`** — the carrier now outlives a lifetime, so a resumed one starts exactly as a fresh carrier did (before, destroy + re-create reset it by accident); ② a **`ScoreDelta` sensor's first answer of an epoch is never suppressed** (`SensorEvalState.PublishedThisEpoch`). ② is a pre-existing defect: EQS 1.3 §17.6 says *"bump Epoch for a guaranteed-new answer"* — what `EqsChildSensor.Refresh` waits for — but a refresh whose scores had not moved was never answered. Only the opt-in `ScoreDelta` policy (your *Spawn EQS Sensor* pin) was affected; `AlwaysPush` never was |
| **results** (`CE-487`) | every result-cache hit is re-checked (alive · an `EqsSensor` · that parent and part id) ⇒ a reused part id's answers reach the NEW sensor. Your epoch high bits still decide whether the answer is current |
| **authority move** (`CE-490`) | the brain's config egress reads its own topic; each scan it writes `Suspended` once for every child instance whose parent this node holds authority over and that NO local sensor holds (every local sensor counts, authority or not — a live one is never swept). ⚠ deviation from B-3: the reader is inside the egress (one brain-side owner of the topic), not a separate translator; nothing added to `SimHostAuxiliaryTranslatorPack` |
| ⭐ **`EqsSensorKey`** (new, `Fdp.Toolkits/Spatial/Eqs`) | the wire key `(ParentNetworkId, LocalChildIndex)`, both directions, once — 🔒 *"share and unify, do not duplicate"*. The egress, the solver, the result ingress and `EqsResultUpdateSystem` each wrote this rule out; all four route through it now. ⚠ It answers the WIRE question (by the parent's network id); your `EqsChildSensor.Find` answers the brain question (by parent entity + site) — not merged, different question, and yours is fenced |

## 2. Acceptance

① end ⇒ no dispose; carrier stays, suspended, publishes nothing ✅ `CE486_ASensorThatEnds_IsSuspendedOnTheMuscle_NotDestroyed_AndPublishesNothing` ·
② end + create on the same key in one scan ⇒ the Muscle solves the new one ✅ and ④ the answer reaches the NEW local sensor ✅
`CE486_CE487_ASensorReplacedOnTheSameKeyInOneScan_IsSolved_AndAnsweredOnTheNewSensor` ·
③ ✅ `CE486_AChildDispose_DoesNotDestroyTheCarrier_AndAFollowingWriteUpdatesIt` — ⚠ **measured:** the topic is KeepLast-1, so a dispose
and a write of ONE key written back-to-back collapse into one valid sample on a real reader; "both in one Take" cannot be produced over
DDS (a rail written that way stayed green on the old ingress — caught by its red-proof). The rail lets the Muscle take the dispose, then
writes · ⑤ ✅ `CE490_AnInheritedInstanceWithNoLocalSensor_IsSuspendedByTheAuthority` · plus, for the solver rows above,
`CE486_ANewLifetimeOnASuspendedCarrier_IsAnswered_LikeAFreshCarrier` and `ScoreDelta_AnEpochBump_IsAnswered_EvenWhenNoScoreMoved` ·
⑥ ✅ §4 · ⑦ ✅ live — §4.

## 3. Things to act on

| | |
|---|---|
| ⚠ **DDS domain overrun in the integration harness** (pre-existing) | `HrotRunnerHarness`'s auto-range is documented as 100–145, but **50** call sites use it, so a full run walks past 145 into the explicit ranges (150–159 `AllSubsystemsSpawnMovingVehicleTests`, …). My rails first took 146–149 on the comment's word and one timed out in the folder-wide run; they use 40–43 now. Not fixed here — the harness is shared |
| ⚠ **multi-writer ordering** (your input wanted) | an instance can carry the old owner's last ACTIVE sample and the new owner's SUSPEND; a late-joining Muscle gets both in arrival order and can end ACTIVE. The binding exposes no destination-order QoS. ⭐ Proposed follow-up, now unblocked (`CE-485` merged): the Muscle keeps a suspended lifetime suspended — drop an active sample whose epoch equals a suspended one (your epoch high bits make that a lifetime test). ⚠ Needs your answer first: is the owner run (`InstanceId`) unique per parent across AUTHORITY moves, or can the new owner's first run reuse the old owner's number? If it can, the rule must also key on the writer. Not built here (outside the three items). Design §3a records it |
| ⚠ recordings | `EqsSensor` grew one byte (plus padding). A recording made before this build carries the old layout for that component — worth knowing if a replay of an old EQS recording misbehaves |
| ⚠ `ClusterConformanceRails` message | (from the previous batch) still says the `acksPending` lambda is in `Program.cs` — STOP path, not edited |

## 4. Gates *(base `3f2be54ba`)*

Merged tree = `68b34d870` (+ this report); **base = `9676147e5`** (`behaviors` head I merged = this tree without my changes),
built in a separate worktree. Every test project was BUILT first (`dotnet build <tests.csproj> --no-restore`), then `--no-build`.

| # | gate (verbatim, `--no-build`) | merged | base | delta |
|---|---|---|---|---|
| 1 | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests` | 1020 / **4** / 3 | 1020 / **4** / 3 | 0 — the same 4 reds on base: `NodeRolePersistenceRails.TheSaveHandlerSetIsStillComplete`, `MapPresentationParityRails.EveryTkbSpawningHost_…(EditorStrideSubsystem.cs)`, `EcsRecordReplayControllerTests.PrepareRecordingAsync_InstallsRecordingModule`, `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe` |
| 2 | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --filter FullyQualifiedName~Eqs` | 61 / 0 | — | 0 |
| 3 | `dotnet test Hrot/Network/Hrot.Network.NED.Tests` | 119 / 0 (×2) | — | 0. ⚠ one run in the gate script showed `DdsIntegrationTests.CanPublishAndSubscribeEntityMaster` red: a diagnostic cluster I had left running shared the DDS domain. Re-run with no cluster up: 119/0 twice |
| 4 | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests --filter FullyQualifiedName~ThereIsOneNetworkIdResolverTests` | 3 / 0 | — | 0 (two allow-list entries removed — `EqsSensorKey`) |
| 5 | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests --filter FullyQualifiedName~.Eqs.` | 52 / **31** | 46 / **31** | **+6 passing = the six new rails**; the same 31 reds on base, by name (the EditorHarness-hosted EQS suites — offline solver never answers: `EqsRoundTrip*`, `EqsSolverSystem*`, `EqsMultiSensor*`, `EqsScoreDeltaTests.T-SD1`, `AccurateLos*`, `FindCover*`, golden, …) |
| ⭐ 8 | **row 8 — the integration suite for the wire invariant:** `… --filter "FullyQualifiedName~EqsDistributedTests.CE4\|FullyQualifiedName~EqsDistributedTests.ScoreDelta"` (a real Brain + Muscle over DDS) | **6 / 0, five runs** | — | +6 new |
| R | red-proofs (each variant built + the six rails run) | V2 ingress destroys ⇒ ③ red · V3 cache trusts hits ⇒ ②④ red · V4 no sweep ⇒ ① ⑤ red · V5 no same-key guard ⇒ ①②③ red · V6 suspended keeps eval state ⇒ lifetime rail red only · V7 ScoreDelta ignores epoch ⇒ epoch-bump rail red only | | V2–V5 measured before the merge (that code did not change after it) |
| 7 | `tracker-counts.py --check` · `design-digest.py --check` · `mermaid-check.mjs DESIGN_Behaviour_Fault_And_Teardown.md` · `rulings-check.py` | OK · OK · 4/4 parse · OK (one staleness note on `Q78`, not touched here) | | |
| 5 | working tree after every run | clean | | |
| ⑦ | **live**, `ClusterRunner --mode all`, a fresh cluster per scenario, two rounds | `hill-attack-close-bp` t≈61 s ×2 · `hill-attack-close` t≈61 / 56 s — every run: hostiles 1006/1007 `Health 0`, platoon back | | unchanged outcome |

⚠ **Live, told straight:** two runs on an EARLIER build of this batch had both commanders' area query time out at 5 s. Rebuilt with
temporary egress logging, the brain wrote the sensor once and never suspended it, and every run since — 3 diagnostic + 2 + 4
acceptance, both commanders, all on later builds — passed. I could not reconstruct what the failing binary held, so this
is recorded, not explained. ⭐ If it recurs, CE-482 now FAULTS the run on that timeout, so it will be loud.

**Ids allocated:** none. **Base sha:** `9676147e5`.

## 5. Follow-up — `CE-492`, multi-writer ordering *(`2026-10-01`, after your owner-run answer)*

🔒 User: *"go ahead with the follow-up batch — if it is not blocked by anything, just do it."* Not blocked: the binding exposes
`DdsSampleInfo.SourceTimestamp` and `PublicationHandle` (measured by reflection over `CycloneDDS.Runtime` 0.3.2).
**Id allocated: `CE-492`** — next free on `backend` and `behaviors` (`git grep` on both: unused).

**Decision.** The Muscle config ingress takes samples in **source-time order per instance**: a sample older than the newest one
already taken for its `(parent, part id)` is stale and dropped — DDS's own `BY_SOURCE_TIMESTAMP` rule, done in the application.
New shared helper `SourceTimeOrder<TKey>` (`Fdp.Toolkits/Replication/Utilities`, no DDS dependency); the ingress's per-sample
handling moved into `Receive(cmd, data, valid, disposed, sourceTimestamp)`, which `PollIngress` feeds from `Take()`.

**Rejected:** `(writer, epoch)` — your proposal. Measured against the three cases it fails the hazard itself: the new owner's
SUSPEND is written by the NEW owner's writer with the OLD owner's epoch (the sweep copies the last config), so the old owner's stale
ACTIVE `(A, e)` never matches the suspension `(B, e)`. A "different writer" variant instead drops case ③ (the new owner's first
run reusing the epoch — exactly what your answer says happens). Epoch alone: your answer rules it out.

| case (equal epochs in all three) | right end state | source-time order |
|---|---|---|
| ① old owner's ACTIVE arrives after the new owner's SUSPEND | suspended | ✅ the active is older ⇒ dropped |
| ② one writer: SUSPEND, then the next lifetime ACTIVE | active | ✅ |
| ③ old owner ended its own sensor; new owner's first run (same epoch) arrives first | active | ✅ the old suspend is older ⇒ dropped |

⚠ **Its limit:** cross-node clock skew larger than the gap between the old owner's last write and the new owner's sweep orders
them wrongly (the same limit DDS's own QoS would have). ⚠ Not applied to the brain's own read-back in the egress: a stale sample
there only causes one redundant re-suspend, which the sweep already self-heals.

**Gates** (merged tree; base = `0c16f3050`, the previous head; test projects built first, then `--no-build`):

| gate | result | delta |
|---|---|---|
| `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --filter "FullyQualifiedName~Eqs\|FullyQualifiedName~Replication"` | 162 / 0 | +2 (`SourceTimeOrderTests`) |
| `dotnet test Hrot/Network/Hrot.Network.NED.Tests` | 119 / 0 | 0 |
| ⭐ row 8: `… --filter FullyQualifiedName~EqsDistributedTests` (a real Brain + Muscle) | **17 / 0**, before and after the red-proof; the lifecycle subset 7/7 ×3 | +1 (`CE492_TwoWritersOnOneInstance_TheNewestSourceTimeWins_WhateverTheArrivalOrder`) |
| `… --filter FullyQualifiedName~.Eqs.` | 53 / 31 | +1 passing; the same 31 pre-existing EditorHarness reds by name (§4 row 5) |
| red-proof V8 — the ingress without the source-time check | ⇒ the CE-492 rail red, nothing else | |
| docs: tracker / design-digest / mermaid (4/4) | OK | (`tracker-counts.py` counts `BP-` rows only — CE rows never move it) |
| live `--mode all`, both commanders | hostiles `Health 0`, platoon back (t≈61 s / 56 s) | unchanged |

⚠ **A stale-binary trap, caught by this batch's own rail — worth knowing for your red-proofs too.** The red-proof script backs a
file up with `shutil.copy` and restores it with `shutil.move`; the restored file carries the BACKUP's mtime, which is OLDER than the
variant-built DLL ⇒ the "restored build" skipped compiling and the VARIANT stayed in `bin`. The CE-492 rail then failed in the next
class run with `stale samples=0` — the binary had no check in it. Fixed: the scripts now `os.utime` the restored file, and this run
prints source vs DLL mtime (15:12:23 < 15:12:33 — rebuilt). The `CE-486/487/490` gate numbers above are unaffected: after each of
those red-proofs, the rails each variant reddens passed again, so those binaries held the fix.
