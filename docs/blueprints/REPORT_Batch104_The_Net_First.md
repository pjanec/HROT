<!--STATUS
state: LIVE
updated: 2026-08-21
current-answer: this whole file — what Batch 104 measured, fixed and found.
stale-below: nothing.
known-rot: none.
known-conflict: ⚠ `DESIGN_Time_Architecture.md` §13 is now STALE IN TWO PLACES and the coordinator
  owns it, not this session — §7 names both edits.
-->
# ⭐⭐⭐ REPORT — Batch 104 · **the net works, and the first thing it caught was a node that never answered**

> **Scope frozen at** `34deca154` · **branch** `claude/time-system-refactor-batch-104-gp617x` ·
> **started marker** `chore: started batch 104 at 34deca154` *(rule 1b, pushed before any code)*
> ⭐ **Branched by `--ff-only` from the coordinator head `91b53840`** *(rule 7)*, which is `34deca154`
> + 5 commits. ⚠ **Stated rather than assumed:** those five are **docs only** — `CLAUDE.md`, the
> `DESIGN_Time_*` merge, `PLAN_Time_System_Refactor.md`, `RULINGS.md` and the dispatch stamp
> *(`git diff --stat 34deca154 91b53840` — 6 files, all under `docs/` or `.claude/`)*. ⇒ **no
> production file moved after the freeze sha**, so the frozen scope is intact.
> ⭐ This is the **TIME lane's first batch**, approved `2026-08-21`. ⭐ ids **`TM-`**, tracker
> **Area H only**.

| item | verdict | one line |
|---|---|---|
| **`104a`** | ✅ **done** | ⛔⛔ **hypothesis ②, and worse than stated: the CGF node could not ACK — it had no time translators at all.** Both halves fixed |
| **`104b`** | ✅ **done** *(measurement)* | ⛔ **`BP-378` has NOT rotted for the FULL run** — it still aborts, twice, differently. ⭐ **A class-at-a-time gate is real**: see §4 |
| **`104c`** | ✅ **done** | gate row established, **run twice**, no flake |
| **`104d`** | ⚠ **partial** | 2 of 3 gaps closed *(`SetTimeScale`, CGF participation)*; ⛔ **editor-composition and breakpoint-pause NOT added — reasons in §5**, not silence |

⭐⭐ **IDs I allocated:** `TM-001` · `TM-002` · `TM-003` · `TM-004` · `TM-005` — ⭐ **all in the
tracker's new `Area H — Time & clock`**, none anywhere else in that file.

---

## 1. ⛔⛔⛔ `104a` — **root-caused BEFORE it was fixed, and the answer changes the picture**

### ⭐ The handoff put two hypotheses on the table. **It is ②** — and ② was understated.

📐 **The probe** *(a throwaway rail, since removed — §5)*: sample `_pendingAcks`, `_expectedSlaves`
and each node's controller mode around every step.

```
after PAUSE   : mode=Stepping pendingAcks=[]    expectedSlaves=[1,400] simHost=Deterministic cgf=SlaveSyncController/Continuous
after STEP 1  : mode=Stepping pendingAcks=[400] expectedSlaves=[1,400] simHost=Deterministic cgf=SlaveSyncController/Continuous
  STEP 1: waited 5002 ms for ACKs; remaining=1
after STEP 2  : mode=Stepping pendingAcks=[400] …  masterSimTime=4.928   ⛔ unchanged
after STEP 3  : mode=Stepping pendingAcks=[400] …  masterSimTime=4.928   ⛔ unchanged
```

| ⭐ what the numbers say | |
|---|---|
| **node 1 (SimHost) ACKs** | it leaves `_pendingAcks` on the first `Update()` |
| ⛔⛔ **node 400 (CGF) NEVER ACKs** | 📐 **5 000 ms and thousands of pumped frames, three times** |
| ⛔⛔⛔ **and CGF never even left `Continuous`** | ⇒ **it never heard the pause.** ⚠ **This is not "the settle is too short" — there is nothing to wait for** |
| ⇒ | ⭐⭐⭐ **only the FIRST step of any session ever worked**, in every session, always |

⛔ **The handoff said "if it is ②, the defect is in the harness or the ACK wiring."** ⭐ **It is the
ACK wiring, and it is PRODUCTION** — 📌 not a harness artefact, see below.

### ⛔⛔ `TM-002` — **why CGF cannot answer: it composes past the code that wires it**

| ⭐ | |
|---|---|
| **the three translators** | `SwitchTimeModeDescriptorTranslator` *(hears the pause)* · `SlaveLockstepTranslator` *(`FrameOrder`→`AdvanceFrameIntent` in, `FrameStepCompletedEvent`→`FrameAck` out)* · `SlaveTimeSyncTranslator` |
| **who wires them** | ⭐ `SharedApplicationBootstrapper` **phase 6c** — ⇒ SimHost · IG · StrideMock |
| ⛔⛔ **who does NOT** | **`CgfSubsystem`**, which builds through `HrotNodeBuilder` **directly** and never runs that bootstrapper ⇒ the node holds a `SlaveSyncController` **with nothing connected to it** |
| ⛔⛔⛔ **and yet it is in the roster** | `OrchestratorSubsystem:303` **and** `ClusterMaster:327`, both `SubsystemName is "SimHost" or "IG" or "CGF"` ⇒ ⭐ **the master blocks every step on a node that is structurally unable to reply** |
| ⚠ **the cruel detail** | 📌 **`CgfApplication` DID wire them** *(`:118-119`)* — ⛔ it has **exactly one caller, a unit test.** ⇒ **the working copy is the dead one, and the live one is the broken one** |

⇒ ⭐⭐ **Fix: phase 6c extracted to `SlaveTimeTranslatorRegistration.RegisterOn(...)` and called from
BOTH sites.** ⛔ **Not copied into CGF** — 📌 the standing ruling is *"no keeping two implementations
for the same concept"*, and a second copy is precisely how the first one rotted.

### ⭐⭐⭐ `TM-001` — **and the silent discard is STILL a defect, so it was fixed too**

⚠⚠ **Fixing only `TM-002` would have turned the suite green and left the trap armed.** 📌 The plan says
so itself: *"`AS-14` gets WORSE under `T4`: intents can be published faster than ACKs return."*

| ⭐ the choice the handoff asked me to make and justify | |
|---|---|
| ⭐⭐⭐ **QUEUE, bounded — and REFUSE audibly past the bound.** ⛔ **Not one or the other** | |
| **why not refuse-only** | ⭐ the operator clicking Step three times means three steps; a refusal that is merely *logged* still loses the motion they asked for |
| **why not queue-only** | ⛔ **`TM-002` is exactly the case that breaks it** — a node that has stopped ACKing **forever** would accumulate an unbounded queue and then fire the whole burst if it ever returned. ⚠ **Unbounded queueing would have HIDDEN `TM-002`, not surfaced it** |
| **why the ACK guard stays** | 📌 the handoff's own constraint, and it is right: **removing it trades a lost step for a cluster desync** |
| ⭐ **the bound** | `TimeConfig.MaxQueuedSteps`, default **8** — a config knob, not a magic number in the controller |

| ⭐ the resulting contract | |
|---|---|
| ACKs outstanding, room in the queue | **deferred**, released by `UpdateStepping` the moment the ACK set clears — ⭐ **one per frame**, because the next one waits for this one's ACKs |
| queue full | **refused**, `Warn` naming the nodes that have not ACKed, `RefusedStepCount++` |
| not in `Stepping` mode | **refused**, `Warn`, `RefusedStepCount++` — ⛔ **this used to be a silent no-op too** |
| **Resume** | **drops** what was deferred, and the `RESUME` log line **says how many** — 📌 a step queued during one pause must not fire into the next |

⭐ **Observability without inventing vocabulary:** `QueuedStepCount` / `RefusedStepCount` are public
properties. ⛔ **No new event type** — 📌 a `StepDeferredEvent` for a toolbar affordance is **`W4`**,
explicitly not this batch.

---

## 2. ⭐⭐ WHAT MOVED — **6 production files, and 2 of them are the fix**

| file | what |
|---|---|
| ⭐⭐⭐ `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/MasterSyncController.cs` | `Step` splits into `Step` *(decide)* + `ExecuteStep` *(issue)*; queue + refusal counters; drain in `UpdateStepping`; clear on Resume/`SnapAndPause` |
| ⭐ `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/TimeConfig.cs` | `MaxQueuedSteps` |
| ⭐⭐ `Hrot/Engine/Hrot.Common/Infrastructure/SlaveTimeTranslatorRegistration.cs` | **new** — phase 6c, extracted |
| ⭐ `Hrot/Engine/Hrot.Common/Infrastructure/SharedApplicationBootstrapper.cs` | phase 6c now **calls** it *(−18 lines, behaviour identical)* |
| ⭐⭐⭐ `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | **calls it too** — the fix; plus `TestHook_TimeControllerType`/`Mode` |
| ⭐ `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs` | `TestHook_TimeScale` |
| — | ⛔ **no `GlobalTime` change · no new `SystemPhase` · no drain system · no refusal deletion · no bus move** *(handoff §5)* |

### ⚠⚠ SCOPE NOTE — **`Hrot.CGF` and `Hrot.Common` are NOT in the time lane's file list. Flagged, not hidden.**

📌 The lane table lists `Fdp.Toolkits/Time/` · `Hrot.Orchestrator` · `ModuleHostKernel` ·
`Hrot.ClusterRunner.Integration.Tests`. ⭐ **That list was derived as a MEASUREMENT of what the
refactor touches, before anyone knew where `AS-14` lived.**

| ⭐ why I proceeded rather than stopping | |
|---|---|
| **① the handoff anticipated it** | *"if this is it, the defect is in the harness or **the ACK wiring**"* ⇒ **this IS `104a`'s fix**, not scope creep |
| **② the cross-lane rule is not triggered** | ⛔ the rule protects the **UI/variable lane** — `AiShared` · `Blueprints/BTree/Hsm.Editor` · variables · working state. 📐 `Hrot.CGF` and `Hrot.Common.Infrastructure` are **neither lane's**, and are **different assemblies** from everything the UI lane holds ⇒ **no shared production file, no merge collision** |
| **③ `T0` is a blocker for the whole programme** | 📌 a STOP here stops `T1`…`W5` as well, and `R-106` says a blocked item stops **that item** — ⛔ but this item was not blocked, only *outside a list* |
| ⚠ **what would have made me stop** | a file the UI lane owns. ⭐ **There was none** |

⇒ ⭐ **The coordinator should widen the TIME lane's row to name `Hrot.CGF` + `Hrot.Common.Infrastructure`**, or say the edit should have been a stop. **Either is fine; the silence is not.**

---

## 3. ⭐⭐ `104c` — **the gate row, and it is not a flake**

```
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/... --no-build \
            --filter "FullyQualifiedName~TimeControlIntegrationTests"
```

| run | result | |
|---|---|---|
| ⭐ **baseline, at `34deca154`** | ⛔ **4 passed / 2 FAILED**, 32 s | 📐 *"advanced ~3s after 3 steps; actual delta=**1.000s**"* · *"expected ~2s advance; got **1.000s**"* — ⭐ **reproduced exactly as the handoff predicted** |
| ✅ **after `TM-001` + `TM-002`** | **6 / 0**, 36 s | the two named reds are **fixed**, not skipped |
| ✅ **run 1, with `TM-005`'s additions** | **9 / 0**, 51 s | |
| ✅ **run 2** *(the handoff's "run it TWICE")* | **9 / 0**, 52 s | ⭐ **no flake observed** |
| ✅ **run 3** *(again, inside the isolation sweep of §4)* | **9 / 0**, 62 s | |

⛔ **No third red at any point.** ⭐ **The two greens are a FIX and are reported as one**, per the rule.

---

## 4. ⛔⛔⛔ `104b` — **`BP-378` HAS NOT ROTTED FOR THE FULL RUN. But the crash has ONE name.**

### ⚠⚠ First, the correction: **`AS-13`'s headline is true of the FILTERED run only**

📐 **Two full single-process runs, same commit, same machine:**

| run | got through | then |
|---|---|---|
| ① | **55 of 250** — 31P / 22F / 2S | ⛔ **host crash**, then the 5-min blame timer elapsed; ⚠ *"Collect dump was enabled but no dump file was generated"* |
| ② | **83 of 250** — 42P / 36F / 5S | ⛔ **`Test host process crashed : CycloneDDS.Runtime.DdsException: dds_take failed: -3 (BadParameter)`** |

⇒ ⭐⭐ **`BP-378`'s signature — *"the count differs every run"* — is intact.** ⛔ **The suite cannot be
gated as one process today.**

### ⭐ The OOM, **named** — 📌 the handoff said *"do not fix `MAX_ENTITIES` blind"*, so it is not fixed

📐 **14 `OutOfMemoryException`s in run ②.** ⭐ The stack is the same every time:

```
Fdp.Core.EntityIndex..ctor()          EntityIndex.cs:38    ← _freeList = new int[FdpConfig.MAX_ENTITIES]   (1 000 000)
Fdp.Core.EntityRepository..ctor()     EntityRepository.cs:114                                    ×13
Hrot.Common.Infrastructure.HrotNodeBuilder.Build()          HrotNodeBuilder.cs:92                ×12
Hrot.SimHost.SimHostNodeBootstrapper.BuildContext(...)      SimHostNodeBootstrapper.cs:156       ×11
```

| ⭐ | |
|---|---|
| **the allocation** | `int[1_000_000]` *(4 MB managed)* **plus** `NativeChunkTable<BitMask512>` and `NativeChunkTable<EntityMetadataCold>`, each reserving `⌈1_000_000 / chunkCapacity⌉ × 64 KB` ≈ **64 MB of address space** |
| **the multiplier** | ⛔ **one set per `EntityRepository`** ⇒ **one per NODE**, and each harness boots **4–5 nodes** — 📌 `HrotRunnerHarness` builds Orchestrator + SimHost + IG + ExCon + CGF |
| **why it accumulates** | ⚠ **nothing releases them between test classes** in a shared host |
| ⛔ **why I did not touch it** | 📌 `MAX_ENTITIES` is a **genuine engine constant**, not a test knob — ⭐ capping it to make a test suite pass is exactly the "blind fix" the handoff forbade |

### ⭐⭐⭐ AND THE FIND THAT IS ACTUALLY ACTIONABLE — **the host crash has a single source**

📐 **Every one of the 72 classes run in its OWN test host:**

| ⭐ | |
|---|---|
| ⛔⛔ **`ClusterOpE2eScriptTests` aborts the host ON ITS OWN** | **in 2–3 s, reproducibly, at BOTH shas** |
| ✅ **every other class COMPLETES** | ⚠ some with pre-existing reds — but **none crashes, none hangs, none OOMs** |
| ⭐ **total wall-clock for all 72** | **15.7 minutes** |

⇒ ⭐⭐⭐ **`BP-378`'s *"a DIFFERENT proximate cause every time"* is what a crashed host looks like from
the outside.** 📌 **Isolation makes it one class.** ⇒ **Quarantining `ClusterOpE2eScriptTests` is the
difference between a suite that finishes and one that does not** — ⚠ **and I did NOT do it**: 📌 the
handoff's rule is *"a new skip is a finding, not a fix"*, and this is a finding. ⭐ `TM-006`.

### ✅ **43 classes FULLY GREEN in isolation** — ⭐ the standing class-at-a-time gate list

```
AclBackdoorEliminationTests                     BlueprintKernelRunTests
BreakpointSubsystemWiringTests                  CgfComponentRegistryTests
ContextMenuIntegrationTests                     DdsIdAllocatorDecouplingTests
EditorAuthoringIntegrationTests                 EditorPreviewAndSaveIntegrationTests
EditorSubsystemBootTests                        Eqs.EqsChildSensorActionTests
Eqs.EqsCombatNodesTests                         Eqs.EqsContextSlotTests
Eqs.EqsDistributedTests                         Eqs.EqsLastUpdateTimeTests
Eqs.EqsLifecycleNodesTests                      Eqs.EqsMultiLevelProofTests
Eqs.EqsMultiSensorTests                         Eqs.EqsMultiTemplateTests
Eqs.EqsResultUpdateSystemTests                  Eqs.EqsRoundTripTests
Eqs.EqsSolverSystemPhase2Tests                  Eqs.EqsSolverSystemTests
Eqs.EqsTranslatorTests                          Eqs.FindCoverFromTargetTests
Eqs.Golden.EqsFlatTerrainGoldenTests            Eqs.HideInCoverV2SmokeTests
Eqs.HotReloadTests                              Eqs.PathCostInversionTests
EyesAndMuscleIntegrationTests                   HarnessSmokeTests
HeadlessGizmoStreamingTests                     HrotRunnerHarnessTests
IdAllocatorDiscoveryTests                       MissionControlIntegrationTests
OfflineEditorIntegrationTests                   PreviewModeVehicleMovementTests
ReplicationPhaseExecutionTests                  SelectionAndMissionIntegrationTests
SimTimeSyncIntegrationTests                     SubEntityCascadeDestroyTests
TimeControlIntegrationTests                     ZombieEntityMapTests
ZoneScenarioLoadIntegrationTests
```

### ⚠ **29 classes NOT green — every one IDENTICAL at the base sha `91b53840`**

| class | base | this branch | |
|---|---|---|---|
| `AllSubsystemsClusterTransitionTests` | 0P / 2F / 0S | 0P / 2F / 0S | ✅ same |
| `AllSubsystemsSpawnMovingVehicleTests` | 0P / 1F / 0S | 0P / 1F / 0S | ✅ same |
| `AreaAuthoringIntegrationTests` | 0P / 1F / 0S | 0P / 1F / 0S | ✅ same |
| `BlueprintObserveTests` | 4P / 1F / 0S | 4P / 1F / 0S | ✅ same |
| `BlueprintScenarioIntegrationTests` | 4P / 1F / 2S | 4P / 1F / 2S | ✅ same |
| `CgfRecordingIntegrationTests` | 0P / 3F / 0S | 0P / 3F / 0S | ✅ same |
| `CgfSubsystemHeadlessTests` | 4P / 5F / 0S | 4P / 5F / 0S | ✅ same |
| `ClusterOpE2eScriptTests` | ⛔ **ABORTS the host** | ⛔ **ABORTS the host** | ✅ same |
| `DistributedBrainMuscleIntegrationTests` | 2P / 1F / 1S | 2P / 1F / 1S | ✅ same |
| `DistributedScenarioLoadTests` | 0P / 1F / 0S | 0P / 1F / 0S | ✅ same |
| `DragDropIntegrationTests` | 0P / 2F / 0S | 0P / 2F / 0S | ✅ same |
| `EditorFileIOIntegrationTests` | 3P / 1F / 0S | 3P / 1F / 0S | ✅ same |
| `EntityDestroyIntegrationTests` | 0P / 2F / 0S | 0P / 2F / 0S | ✅ same |
| `Eqs.AccurateLosPhaseTests` | 1P / 2F / 0S | 1P / 2F / 0S | ✅ same |
| `Eqs.EqsFlagsMeaningfulTests` | 3P / 1F / 0S | 3P / 1F / 0S | ✅ same |
| `Eqs.EqsScoreDeltaTests` | 2P / 1F / 0S | 2P / 1F / 0S | ✅ same |
| `FeatureSwitchRcuIntegrationTests` | 0P / 4F / 0S | 0P / 4F / 0S | ✅ same |
| `GhostPromotionTests` | 0P / 1F / 0S | 0P / 1F / 0S | ✅ same |
| `HsmBehaviorIntegrationTests` | 1P / 1F / 0S | 1P / 1F / 0S | ✅ same |
| `MapPlacementIntegrationTests` | 0P / 2F / 0S | 0P / 2F / 0S | ✅ same |
| `MiniExConIntegrationTests` | 2P / 3F / 0S | 2P / 3F / 0S | ✅ same |
| `NavigationStatusAuthorityTests` | 1P / 1F / 0S | 1P / 1F / 0S | ✅ same |
| `NetworkDemoPatrolAndEngageTests` | 0P / 3F / 0S | 0P / 3F / 0S | ✅ same |
| `NetworkGatewayIntegrationTests` | 0P / 1F / 0S | 0P / 1F / 0S | ✅ same |
| `SensorMechanismIntegrationTests` | 0P / 1F / 0S | 0P / 1F / 0S | ✅ same |
| `SpawnMovingVehicleIntegrationTests` | 0P / 3F / 0S | 0P / 3F / 0S | ✅ same |
| `SpawnMovingVehicleWithGatewayIntegrationTests` | 0P / 1F / 0S | 0P / 1F / 0S | ✅ same |
| `SplitAuthoritySpawnTests` | 2P / 1F / 0S | 2P / 1F / 0S | ✅ same |
| `UrbanCombatFileLifecycleTests` | 0P / 1F / 0S | 0P / 1F / 0S | ✅ same |


⭐⭐⭐ **On "pre-existing": this is a MEASUREMENT.** 📐 Every red above was re-run at the base sha
`91b53840` *(the branch point)* — ⛔ **not asserted from the diff.** ⭐⭐ **Zero classes differ.**
⚠ **This mattered most for the CGF classes**: `TM-002` adds three systems to the CGF kernel's schedule,
so `CgfSubsystemHeadlessTests` *(4P/5F)* and `CgfRecordingIntegrationTests` *(0P/3F)* were the ones that
could plausibly have moved. ⭐ **They did not — identical counts at both shas.**

---

## 5. ⚠ `104d` — **two of three gaps closed, and the third is NAMED, not skipped**

| gap | verdict | |
|---|---|---|
| ⛔ **no `SetTimeScale` test** | ✅ **closed, twice over** | ⭐ `SetTimeScale_HalfSpeed_ReachesTheMasterController` — the op **had never been exercised at all**. ⭐⭐ **And a second rail records what the first one found:** ⛔⛔ **`TimeScale = 0` IS NOT EXPRESSIBLE over the cluster op path** — 📐 `ClusterMaster.cs:359`, `scale = dto != null && dto.TimeScale > 0f ? dto.TimeScale : 1f` ⇒ **"halt via time scale" silently becomes "run at full speed"** *(`TM-004`)* |
| ⛔ **no editor-composition test** | ⛔ **NOT added** | 📌 it needs `EditorHarness` — **a second composition root** — inside a batch whose stated point is *"touches NO time-control production code"*. ⭐ **It belongs to `T3`**, which is the change it would guard: a rail written now guards nothing and would have to be rewritten when the bus moves |
| ⛔ **no breakpoint-pause test** | ⛔ **NOT added** | 📌 it needs the rewind path **`W2`/`W5` turn on**, which does not exist yet ⇒ ⭐ there is nothing to assert against |
| ⭐ **bonus, unasked** | ✅ **added** | `PauseStep_CgfNodeEntersLockstepAndAcksEveryStep` — ⛔⛔ **nothing in the suite observed the CGF side.** ⚠ **That is precisely why `TM-002` survived**: the two reds saw the *missing sim time*, never the *silent node* |

⭐ **The root-cause probe was a throwaway and was REMOVED**, not left behind: it reflected into
`_pendingAcks` / `_mode` and asserted `true`. ⇒ **Replaced by real hooks** —
`CgfSubsystem.TestHook_TimeControllerType`/`Mode` *(mirroring `SimHostApp`)* and
`OrchestratorSubsystem.TestHook_TimeScale` — ⭐ so the rails assert **observed state** instead of private
fields or an inferred timing slope.


---

## 6. ⭐⭐ GATES

⚠ **Environment: Linux cloud container, 4 cores / 16 GB, `dotnet 8.0.424`. ⛔ NO Xvfb** — nothing in
these suites needs a GL context.
⭐ **`--no-build` column:** every row below ran `--no-build` **after** an explicit build of that project
in the same session ⇒ ⛔ **no stale-binary green.** 📌 The out-of-solution projects that report a stale
bin *(`NodeEditor.Core`, `NodeEditor.UI`, `Fhsm.Tests`)* are **not in this batch's blast radius** and
were not run.

| # | gate | built? | before *(`91b53840`)* | after | Δ |
|---|---|---|---|---|---|
| **1** | ⭐⭐⭐ `dotnet build IOS-IG-SimHost.sln` | — | 0 errors | ✅ **0 errors**, 60 warnings, 67 s | **0** |
| **2** | ⭐⭐⭐ **`~TimeControlIntegrationTests`** *(the standing row)* | ✅ | ⛔ **4P / 2F** | ✅ **9P / 0F** | **+2 fixed, +3 added** |
| **3** | ⭐ same, **run 2** | `--no-build` | — | ✅ **9P / 0F**, 52 s | ⭐ **no flake** |
| **4** | ⭐⭐ `~MasterSyncControllerTests` | ✅ | 34P / 0F | ✅ **39P / 0F** | **+5 rails** |
| **5** | ⭐⭐ **`Fdp.Toolkits.Tests` — FULL** | ✅ | — | ✅ **1973P / 0F / 0S**, 33 s | ⚠ **`DEBT-AIB-030` did NOT fire this run** — ⛔ neither a red nor a green here is evidence; row 6 is the one that counts |
| **6** | ⭐⭐⭐ **`~ThePauseFlagOnTheClockIsFalseWhilePausedTests`** *(must stay 4/0)* | `--no-build` | 4 / 0 | ✅ **4P / 0F**, 65 ms | **0** — 📌 `M-42` + `AS-1b` still pinned |
| **7** | ⭐ **`Hrot.ClusterRunner.Tests`** | ✅ | ⛔ 2 pre-existing reds | ⚠ **260P / 2F** — ⭐ **exactly `DataDrivenGizmoPredicateTests.D003_Predicate_True_AllowsUpdateAndDraw` and `…_False_SkipsUpdateAndDraw_ForFilteredEntity`** | **0** — ⛔ **no third red** |
| **8** | ⛔⛔ **`Hrot.ClusterRunner.Integration.Tests` — FULL, one process** | ✅ | *(never measured — `BP-378`)* | ⛔ **ABORTS**: 55/250 then a host crash; 83/250 then `dds_take -3` | 📌 **`TM-006`** |
| **9** | ⭐⭐⭐ **same — 72 classes, EACH in its own host** | `--no-build` | **identical, class by class** | ✅ **43/72 fully green** · 192P / 48F / 6S · 15.7 min | ⭐ **0 differences vs base** — `TM-007` |
| **10** | ⭐ `python3 scripts/tracker-counts.py --check` | — | `open 81 / done 242 (+1 refuted)` | ✅ **unchanged** | **0** — ⚠ **and that is the point: it counts only `BP-` rows, so it does NOT cover Area H.** Stated in the section header, `TM-008` |

| ⭐ contract items | |
|---|---|
| **golden movement** | ⛔ **none.** 📐 No golden, `.bp.json`, `.trx` baseline or generated file is in the diff — **6 production files + 2 test files, all hand-written**, zero regenerated — 📐 `git diff --stat 91b53840..HEAD`: **10 files, +642 / −28**, of which 2 are docs |
| **working tree after every suite run** | ✅ **CLEAN** — `git status --short` empty; `TestResults/` is gitignored |
| **quarantine counts** | ⭐ **unchanged.** ⛔ **I added no skip.** 📌 The 6 skips in the isolated sweep are pre-existing `[Fact(Skip=…)]`, identical at base. ⚠ **`ClusterOpE2eScriptTests` is a candidate for quarantine and I did NOT quarantine it** — *"a new skip is a finding, not a fix"* |
| **ids allocated** | `TM-001` `TM-002` `TM-003` `TM-004` `TM-005` `TM-006` `TM-007` `TM-008` — ⭐ **all in Area H**, ⛔ **no `BP-` id taken** |

---

## 7. ⛔ THE FOUR VERDICTS — `R-106`

| item | verdict | |
|---|---|---|
| **`104a`** | ✅ **done** | root-caused *(hypothesis ②)*, then **both** defects fixed. ⛔ Nothing blocked |
| **`104b`** | ✅ **done** | the measurement **is** the deliverable; ⚠ one part *(edit `BP-378`)* **redirected** to Area H — `TM-008` |
| **`104c`** | ✅ **done** | gate row + before/after, run twice |
| **`104d`** | ⚠ **partial** | 2 of 3 measured gaps closed + 1 unasked; **2 not added, each with a reason** — §5 |

⛔ **No item was blocked.** ⭐ **Nothing cascaded.**


---

## 8. ⛔⛔ FOR THE COORDINATOR — **two design-doc statements are now stale, and that file is yours**

📌 **Rule: the coordinator designs; the implementation session does not rewrite `DESIGN_*`.** ⭐ So
they are **named here rather than edited**:

| 📄 `DESIGN_Time_Architecture.md` | what is now wrong |
|---|---|
| ⛔ **§13 `AS-13` — *"`BP-378` HAS ROTTED — no OOM, no hang"*** | ⭐ **True of the FILTERED run only, and the doc says so** — ⚠ but the headline reads as a verdict on the suite. 📐 **The FULL run aborts**: §4 |
| ⛔⛔ **§13 `AS-14` — *"either the settle is too short or the slave never ACKs in the harness"*** | ⭐ **Neither.** 📐 **CGF is structurally unable to ACK, in PRODUCTION** — `TM-002`. ⚠ *"in the harness"* pointed at the wrong place; the harness is faithful |

⭐ **And one addition worth making:** `AS-14`'s *"it gets WORSE under `T4`"* is now **guarded** —
`TM-001`'s queue is what absorbs the faster intent publication `T3`/`T4` introduce.
