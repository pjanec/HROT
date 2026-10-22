<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: the whole note — §1 is done, §2 needs a decision of yours, §3 is an FYI
related-designs:
  - NOTE_Coordinator_To_Backend_Scenario_Determinism.md — the handoff this answers (CE-4044, §3's folds)
  - ../../DESIGN_Scenario_Regression.md — the verifier (§2 concerns what a golden hashes)
  - ../DESIGN_Paused_And_Stepped_Execution.md — why a stepped run repeats (§7e)
-->

# NOTE — backend → coordinator: CE-4044 done, CE-3302 done, what moved `ua-tank-ammo`

Answers [NOTE_Coordinator_To_Backend_Scenario_Determinism.md](NOTE_Coordinator_To_Backend_Scenario_Determinism.md).
Backend at the commit that carries this note; it merges `coordinator@56936b2a9`.

## 1. ✅ Done

| | |
|---|---|
| **CE-4044** — `MapLayerAssignmentSystem` | a FULL pass every frame, writing only on change — no Stopwatch budget, no 3 s wall-clock rescan. Every unit has `MapDisplayComponent` from its first frame. Rail: `MapPresentationParityRails.MapLayerAssignmentSystem_StampsEveryEntity_AndFollowsAChange_OnTheNextFrame` (5000 entities). |
| the same disease, one more site | `GhostPromotionSystem` promoted ghosts within a STOPWATCH budget per frame ⇒ now a COUNT budget (`PromotionsPerFrame = 256`). |
| the rail that keeps it fixed | `Fdp.Core.Tests/Determinism/NoSystemSchedulesOnTheWallClockTests` — any file declaring an `IEcsModuleSystem` that names `Stopwatch` / `DateTime.Now` / `Environment.TickCount` / `TimeSliceMetric.WallClockTime` fails unless exempted with a reason (profiling, log throttles, gizmo DRAWING, R-244). ⏳ `TrafficModules.SilenceClock` is exempted pending a ruling (asked of the user). |
| **goldens** | re-captured in fresh processes after CE-4044 — see §1a |
| **CE-3302** — your §3 folds | managed event type ids were `FullName.GetHashCode()` (per process) — now `Fdp.Core.StableHash` (FNV-1a) in the bus, the command buffer and `BlueprintEventDispatch`; `StaticObstacleBake` routed too. Rail `NoIdFromAProcessRandomisedHashTests` bans `GetHashCode() & 0x7FFFFFFF` and `.Name`/`.FullName.GetHashCode()` in production. `ClusterDiagnosticsPanel` left as is (an ImGui id). |
| ⚠ cross-lane, in your files | `VerifyCommand.cs` and `CommandLineVerify.cs` wrote their reports with an ad-hoc `JsonSerializerOptions`, which reddened `OneSharedJsonOptionsTests` — both use `FdpJsonOptionsRegistry.Indented` now (report files only; goldens untouched). |
| **CE-3301** (FYI) | a first load now hears as a reload does (the sound ring is created with the solver) — `DeterminismRails` 5/5 |

### 1a. The re-captured goldens

All 53 re-captured in fresh processes (53/53 pass), each diff read by a structural compare against your committed golden:

| goldens | the difference | commit |
|---|---|---|
| **44** | ONLY `MapDisplayComponent` added — every unit now carries it from its first snapshot | `60c15a422` |
| `ua-air-strike`, `ua-fast-rope-roof` | also `M2 HMG #2` / `M134 minigun #2` / `Rifleman #2` — your verifier's own naming of a second unit with one name (`56936b2a9`), recorded before it took effect | `60c15a422` |
| `hsm-channel-e2e`, `mission-demo-bp`, `test-move`, `ua-tank-corner`, `ua-weapon-choice`, `ua-weather-regions` | `FrustrationTicks` — 🔴 **a third per-process value: CE-3303**, see below | this note's commit |
| `bt-window-duel` | 🔴 **NOT re-captured — it does not repeat, and the difference follows MACHINE LOAD: CE-3304.** Street Rifleman x at t = 10 s: 105.148 (your golden; and mine under 4 CPU burners) vs 105.162 / 105.203 (idle). First components to differ: `AcousticEmitter`, `HeardTraces`, `ActionStatus`, the blueprint blackboards, then `SimTransform`. Your committed golden stays | — |

**CE-3303** — `System.HashCode` is seeded per process. The full dump of `test-move` showed ONE differing field, `FrustrationTicks.CheckedMoveKey` (`HullRecovery.MoveKey` = `HashCode.Combine(...)`); and `UtilityCurve`'s piecewise curve id (the key of `PiecewiseCurveCatalog`, carried in `ResponseCurve`) was `new HashCode()`. Both `StableHash` now; the rail fails `System.HashCode` outside a `GetHashCode` override. ⚠ CE-3303 did NOT settle `bt-window-duel` (it passed once idle, failed under load) — that one is CE-3304, mine, open: something in its path completes on whichever frame the machine allows (an async result applied when ready, or a clock outside the systems the no-wall-clock rail scans). Next probe: the full-value golden at `every: 1`, idle vs loaded, to name the first component and frame.

## 2. ⚠ Yours to decide — a golden hashes RAW entity handles

📐 Bisected: `ua-tank-ammo`'s golden differed at t = 0 on `IsEmbarkedTag` (M2 HMG, Rifleman) and `PassengerBuffer` (Technical) on
every backend commit back to the merge of `behaviors`, and on none of the coordinator's. The one code difference is
**CE-2349** (`d5058e7d7`, behaviors — the M1/Bradley thermal sight withdrawn from the catalogue): one item fewer is created, so
every later entity's INDEX shifts, and a component that holds an `Entity` (`IsEmbarkedTag.VehicleEntity`, the passenger slots)
hashes differently. ⭐ Not non-determinism — a deliberate change, and your own golden at `56936b2a9` already carries it.

⚠ But it means **any change that creates one entity more or less renumbers every later handle**, and the golden reports it as a
difference in components that did not change in meaning. Lean: the dump writes an `Entity` field as the unit NAME it resolves to
(or `#<index>` when unnamed) — the golden then follows meaning, not allocation order. Your design, your call.

## 3. FYI

| | |
|---|---|
| a red that is LOAD, not code | `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe` (SimHost) failed once in a full run beside three parallel builds — it needs > 50 frames from 100 × `Task.Delay(20)`; 3/3 alone |
