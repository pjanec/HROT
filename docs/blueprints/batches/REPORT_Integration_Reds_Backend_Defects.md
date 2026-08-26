<!--STATUS
state: LIVE
updated: 2026-08-26
current-answer: this REPORT is the batch record. The durable root causes live in
  TESTING_Harness_And_Goldens.md §9; the rows live in Blueprint_Issues_Tracker.md Area N.
known-conflict: ⛔ QA-033 — the merged CE-051 change takes down 127 of 267 integration tests and is
  NOT fixed here (UI-lane production file). Every integration number below was measured with its
  three-attribute fix applied LOCALLY and UNCOMMITTED.
-->
# REPORT — **Close the backend-owned refiled integration reds** *(BACKEND lane, `QA-`)*

📄 Frame: [`HANDOFF_Integration_Reds_Backend_Defects.md`](HANDOFF_Integration_Reds_Backend_Defects.md)
· dispatch `42a6ef37c` · started-marker `d3311a3a6` *(rule 1b)*.

## 0. ⭐⭐⭐ HEADLINE

| target | asked | ⭐ delivered |
|---|---|---|
| **`QA-017`** *(7)* | root-cause + fix | ✅ **root-caused, TWO instances of one shape.** **4 of 7 green**, ➕ **2 more script tests outside the 7** went green. **3 moved DOWNSTREAM** ⇒ different defects, refiled **`QA-031`** |
| **`QA-024`** *(3)* | root-cause + fix | ✅ **CLOSED — one line, three layers away.** **12/12 EQS green**; inverse-edit red-proof clean |
| **`QA-022`** *(3)* | ⚠ measure lane FIRST | ✅ **measured CROSS-LANE ⇒ STOPPED and refiled** *(the handoff's own instruction, `R-106`)* |

⭐ **ids allocated *(rule 5)*: `QA-027` `QA-028` `QA-029` `QA-030` `QA-031` `QA-032` `QA-033` `QA-034`.**

## 1. 🔴🔴 READ THIS FIRST — **`QA-033` blocks the whole integration suite, and it is not mine to fix**

📐 **Measured on the dispatch tree `42a6ef37c`, before I changed anything:**

| | |
|---|---:|
| total | **267** |
| passed | **130** |
| **failed** | 🔴 **134** |
| skipped | 3 |
| ⛔ **failed with ONE message** — *"System `ToolActivationDrainSystem` must have `[UpdateInPhase]` attribute"* | 🔴 **127** |

*(The pre-`CE-051` baseline measured last batch was **51** reds.)*

⭐ It throws in `SystemScheduler.GetPhaseAttribute` ← `ScenarioEditorModule.RegisterSystems:90` ←
`ModuleHostKernel.Initialize` ← **`CgfSubsystem.Initialize:948`** ⇒ **every harness that boots CGF dies
in its constructor**, so this is not "some tests fail", it is "the suite cannot boot".

⭐⭐ **Three systems from `CE-051` lack the attribute**, all in
`Hrot/Engine/Hrot.Presentation/ScenarioEditor/Systems/`:
`ToolActivationDrainSystem` · `SelectEntitySystem` · `CenterOnEntitySystem`.

⭐ **The phase is not a judgement call** — `DataDrivenGizmoSystem`, `GlobalGizmoManager` and
`CanvasMenuUpdateSystem` are all `[UpdateInPhase(SystemPhase.PostSimulation)]`, and the drain
**activates gizmos on the first two**. The patch is three lines:

```csharp
[UpdateInPhase(SystemPhase.PostSimulation)]     // ← add above each of the three class declarations
public sealed class ToolActivationDrainSystem : IEcsModuleSystem
```

⛔ **NOT APPLIED — UI-lane production file** *(`claude/reset-working-branch-qd1qpv`)*; CLAUDE.md makes a
cross-lane edit a STOP-and-report, not a judgement call.
⚠⚠ **Every integration number in this report was measured with those three attributes applied LOCALLY
and UNCOMMITTED.** Without them **no** integration number is obtainable on the merged tree.

## 2. ⭐⭐⭐ `QA-017` — **an integer where a strict string enum is required, twice, one field apart**

📄 Full write-up: **`TESTING_Harness_And_Goldens.md` §9.1–9.2**.

| # | payload | rejected by | the rejection went to |
|---|---|---|---|
| **A** `QA-027` | `TargetState` as `(int)ClusterState.OperatingLive` | ⭐ `StrictStringEnumConverter` — **the converter written for exactly this**; `OrchestrationJsonOptions` documents itself as rejecting integer enums *"to avoid silent integer-as-enum bugs"* | `ClusterOpRequestAdapter` throws → `ClusterMaster` catches into **`FdpLog.Warn`** |
| **B** `QA-029` | `ExerciseId` as `"e2e-all-01"` — it is a **non-nullable `Guid`** | `JsonException` | the adapter wraps it into the **same** `InvalidOperationException` ⇒ the **same** `Warn` |

⇒ ⭐⭐ **`ProcessTransitionStateIntent` never runs, `_currentDsmState` is never advanced, and the cluster
sits at its current state silently.** ⛔ *"Cluster did not reach state 31"* is a parse failure two
components upstream.

**Fixed:** 4 call sites → `nameof(ClusterState.X)`; 8 `ExerciseId` values across 4 E2E scripts → real GUIDs.

### 2.1 📐 The 7, measured

| test | before | ⭐ after |
|---|---|---|
| `AllSubsystems_TransitionToOperatingLive_CommitStateIsNotDroppedAsDuplicate` | 🔴 | ✅ |
| `AllSubsystems_FullCycleTwice_LoadOperateUnloadIdle` | 🔴 | ✅ |
| `CgfRecording.BothNodes_OperatingReplay_ClusterReachesReplayState` | 🔴 | ✅ |
| `CgfRecording.BothNodes_SeekDuringReplay_ClusterRemainsInReplayState` | 🔴 | ✅ |
| `CgfRecording.BothNodes_LiveSimulation_BothRecordingFilesCreated` | 🔴 state 0 | ⚠ **moved** — *"SimHost recording not found: …/node_1.fdp"* |
| `DistributedScenarioLoad…RemappedMissionPlan` | 🔴 state 0 | ⚠ **moved** — reaches **31**, then *"CGF world must contain exactly 2 entities. Actual: 0"* |
| `UrbanCombatFileLifecycle…InLiveMode` | 🔴 state 0 | ⚠ **moved** — *"Grand demo timed out. Latches: ambush=False, halt=False, hit=False, killed=False"* |

➕ **Outside the 7**, the same fix turned `ClusterOpE2eScriptTests.OverlappingCheckpoints_Passes` and
`PreviewStateRestore_Passes` green, and gave the other two real messages for the first time
(`StatusCode=13`; *timed out waiting for SysOpStatus*).

⇒ ⭐ **The residue is recording / replay / scenario-load, NOT orchestration** — refiled **`QA-031`**.

### 2.2 ⛔⛔ `QA-028` — **the harness discarded its own diagnostic, and that is why this looked opaque**

`HeadlessTestExecutor.RunAsync` collapses every failure into `1` and routes the reason to the injected
`ILogger`; every script-driven test injects **`NullLogger.Instance`** and asserts `Assert.Equal(0, result)`.
⇒ **the entire red was *"Expected: 0 / Actual: 1"*** while the handler underneath had built a precise
message that nothing ever printed.

⭐ Exposed `AssertionFailures`; routed **all 6 call sites** through a new `ScriptRunAssert.Passed`; added
the **master's** state to the transition handler's message. 📌 **It paid on the very next run** — that
run is what located instance **B**.

## 3. ⭐⭐⭐ `QA-024` — **`EntityRepository.SyncFrom` synced ONE of two version clocks** *(`QA-030`)*

📄 Full write-up: **`TESTING_Harness_And_Goldens.md` §9.3**.

`_globalVersion` was copied; `_simulationTick` was not — and `ISimulationView.Tick` reads the latter.
⇒ **every SoD / background snapshot reported `Tick == 1` for the life of the process.**

📐 **Probe** *(throwaway, `EditorHarness` + EQS)*: live repo **1 → 121 → 241** over 240 pumped frames,
solver re-evaluated **37 times, every one seeing `view.Tick == 1`.**

⇒ ① `RefreshTick = tick + 1` always **2** ⇒ `LastUpdateTick` never moved ⇒ *"a large score delta
re-publishes"* failed **even though the publish happened**; ② `AwaitingSinceTick == currentTick` is the
`_AwaitingRaycasts` skip-guard ⇒ once entered, **never left** — contradicting
`EQS_Design_v1.3_final.md:422`.

⛔⛔ **Why ~8 000 tests missed it:** the invariant is `_globalVersion >= _simulationTick` — advancing one
and not the other keeps it **TRUE**. ⚠ And the comment on that line already claimed to sync *"the
correct tick/version reference"*.

⭐ **Result: 12/12 EQS tests green.** `AccurateLos_MultiTickConvergence` went from a **7 s timeout to
796 ms**.

## 4. ⭐ `QA-022` — **STOPPED, per the handoff's own instruction**

📐 With `QA-033` neutralised locally, all 3 still fail, and the messages are viewport-tool **production**:
*"CreationTool did not become active in time"* · *"SimHost did not attach `EditablePolyline` in time"* ·
*"SimHost entity did not have expected `TkbType`"* ⇒ **UI lane** *(`claude/reset-working-branch-qd1qpv`)*.

⭐ Repro:
```
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests --no-build \
  --filter "FullyQualifiedName~MapPlacementIntegrationTests|FullyQualifiedName~AreaAuthoringIntegrationTests"
```

## 5. ⭐⭐ GATES *(rule 8 contract)*

⭐ **Built ONCE per project, `--no-build` for every run after.** ⛔ No full-solution build in the loop.

| # | gate — verbatim | `--no-build` | result | Δ vs base |
|---|---|:--:|---|---|
| 1 | `dotnet test FDP/Engine/Fdp.Core.Tests/… --no-build` | ✅ | **1210 total · 1192 passed · 9 failed · 9 skipped** | ⭐ **base *(fix reverted)* = 10 failed = 7 pre-existing + my 3 rails** ⇒ **0 regressions**; the fix turns exactly its 3 rails green |
| 2 | `dotnet test FDP/Engine/Fdp.ModuleHost.Tests/… --no-build` | ✅ | **198 total · 192 passed · 6 failed** | ⭐ **base = the SAME 6, identical names** ⇒ **pre-existing** *(all SoD/Convoy/provider — the area `QA-030` touches, so this was proved, not assumed)* |
| 3 | `dotnet test …ClusterRunner.Integration.Tests --no-build --filter "…SyncCarriesTheSimulationTick"` | ✅ | **3/3 green** | new rails |
| 4 | EQS: `--filter "…AccurateLosPhaseTests\|…EqsScoreDeltaTests\|…EqsSolverSystemTests\|…EqsFlagsMeaningfulTests"` | ✅ | ⭐ **12/12 green** | was 3 red |
| 5 | scripts: `--filter "…AllSubsystemsClusterTransitionTests\|…ClusterOpE2eScriptTests"` | ✅ | **6 total · 4 passed · 2 failed** | was 0 passed |
| 6 | 🔴 **inverse-edit red-proof** — comment out `_simulationTick = source._simulationTick;` | ✅ | ⭐⭐ **rails 3/3 RED · exactly the 3 `QA-024` tests RED** | the fix is load-bearing |
| 7 | `python3 scripts/design-digest.py --check` | n/a | ✅ pass | — |
| 8 | `python3 scripts/rulings-check.py` | n/a | ✅ **25/25 verified** *(2 staleness WARNs: `.claude/CLAUDE.md`, `DataBreakpointManager.cs` — both pre-existing)* | — |
| 9 | `python3 scripts/tracker-counts.py --check` | n/a | ✅ **open 102 / done 346** | ⚠ unchanged **by design** — the script matches only `BP-` rows, so `QA-` rows are invisible to it *(reported last batch, not changed here)* |
| 10 | working tree clean after every suite run | — | ✅ no golden moved; **zero golden files touched this batch** | — |
| 11 | 🔴 **full integration suite** — `dotnet test …ClusterRunner.Integration.Tests --no-build` *(with `QA-033` patched locally)* | ✅ | ⛔ **ABORTS — "Test host process crashed"** after ~54 of 267: **41 passed · 16 failed · 2 skipped** | ⭐⭐ **base-proved NOT mine** — with `QA-030` reverted it aborts too *(**54 total · 35 passed · 17 failed · 2 skipped**)*, and the **abort point differs between runs** ⇒ nondeterministic. Filed **`QA-034`** |
| 12 | quarantine / skips | — | ✅ **9 skipped in `Fdp.Core.Tests`, unchanged**; ⛔ **no new skip, no new filter-around** *(`R-131`)* | — |

### 5.1 ⛔⛔ ROW 8 OF THE CONTRACT — **the cross-cutting gate, and its honest limit**

⭐ `QA-030` changes `Fdp.Core`, so its blast radius is **every SoD/background system that reads
`view.Tick`**. Gates 1+2 are the two engine suites that exercise the snapshot providers, and **both were
base-proved by reverting the single line**, not by assertion.

⛔⛔ **What CANNOT gate, and why — TWO findings, not omissions.**

**① `QA-033`.** The **full integration suite**
is the system-level suite for this change, and on the dispatch tree it **cannot boot CGF at all**
(`QA-033`, §1: 127/267 dead in a constructor). Its numbers below were obtainable **only** with
`QA-033`'s three attributes applied locally and uncommitted. ⚠ **State that plainly rather than let the
noise stand in for "verified".**

**② `QA-034` — and it is the more alarming of the two.** With `QA-033` neutralised so CGF actually boots,
the full suite **ABORTS on a host crash** after ~54 tests. ⭐⭐ **Base-proved not mine** *(gate 11)*.
⚠⚠ **The reason it looked fixed is worth stating plainly:** today's pre-edit run at `42a6ef37c`
**completed all 267** — ⛔ **but only because `QA-033` killed 127 of them in `CgfSubsystem`'s constructor,
so they never allocated a world.** ⇒ 📌 **a green "the suite completes" can be manufactured by a crash
EARLIER in the pipeline.** `W1` proved this suite stable 3× at `614cd8a81`, so something merged since
re-introduced it. ⭐ The instrument to close it already exists — `FDP_TRACK_REPO_LEAKS=1` +
`DumpLiveOrigins()` (§7.2).

## 6. ⭐ WHAT I GOT WRONG — **three of my own hypotheses, refuted by measurement**

| hypothesis | ⛔ refuted by |
|---|---|
| *"the bootstrap latch gates the request"* | no `orchestrator-config.json` exists ⇒ `Mandatory` is empty ⇒ the ctor sets the latch. **Refuted twice** — the second time because the first refutation was measured while the payload was still broken, so I correctly re-tested rather than trusting it |
| *"the master transitions, the slave does not follow"* | `QA-028`'s message, once printed, said **`ClusterMaster.CurrentClusterState` is 0 (Idle)** — the master had not transitioned either |
| *"the trajectory planner rejects the transition"* | the planner is fine; the intent never reached it |

⇒ ⭐⭐ **What resolved each one was a cheap INSTRUMENT, not a better theory** — expose the failure list ·
add the master's state to a message · a 40-line probe printing `view.Tick` per solver call. **Build the
instrument before the second hypothesis.** *(Folded into `TESTING_Harness_And_Goldens.md` §9.6.)*

## 7. ⭐ FILES

**Production (2):** `FDP/Engine/Fdp.Core/EntityRepository.Sync.cs` *(the `QA-030` line)* ·
`FDP/Toolkits/Fdp.Toolkits/Runner/Testing/HeadlessTestExecutor.cs` *(`AssertionFailures` accessor)*.
**Tests / scripts / docs:** the 4 payload sites · 4 E2E scripts · `ScriptRunAssert.cs` *(new)* ·
`ClusterOpE2eScriptTests` · `SyncCarriesTheSimulationTickTests` *(new, 3 rails)* ·
`TESTING_Harness_And_Goldens.md` §9 · `Blueprint_Issues_Tracker.md` Area N · this report.

⛔ **Not in the diff, deliberately:** the three `[UpdateInPhase]` attributes (`QA-033`, UI lane) and the
`ClusterMaster` swallow (`QA-032`, TIME lane).
