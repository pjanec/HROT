# BATCH-13 Report — EQS Phase 10: Schema Additions

**Batch Number:** BATCH-13
**Tasks:** TASK-EQS-032, TASK-EQS-033, TASK-EQS-034
**Status:** COMPLETE
**Build:** 0 errors, 0 warnings
**Tests:** 43/43 EQS integration tests passing, 4/4 FDP layout tests passing

---

## Summary

All three schema addition tasks were implemented and verified.  New fields are
correctly propagated through the ECS, DDS serialisation layer and the BTree
lifecycle node.  Ten new tests (across three new test files) were added; all
pass with no regressions in the existing suite.

---

## Task Results

### TASK-EQS-032 — `FlagsMeaningful` field in `EqsResult`

**Status: DONE**

**Files changed:**
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs` — `_pad ushort` renamed
  to `FlagsMeaningful ushort` in `EqsResult`; `EqsPublishPolicy` enum added.
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsDdsTopics.cs` — `FlagsMeaningful ushort`
  added to `EqsResultEntry`.
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/CheapLineOfSightTest.cs` — sets
  `result.FlagsMeaningful |= 1` on the above-threshold path.
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/AccurateLineOfSightTest.cs` — sets
  `result.FlagsMeaningful |= 1` when ray resolves (not pending).
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/NavmeshReachableTest.cs` — sets
  `result.FlagsMeaningful |= (short)(1<<3)` for both reachable and unreachable.
- `Hrot/Network/Hrot.Network.NED/SimHost/EqsResultEventEgressTranslator.cs` —
  `FlagsMeaningful = (ushort)r.FlagsMeaningful` added to the per-result copy loop.
- `Hrot/Subsystems/Hrot.SimHost/Systems/EqsResultUpdateSystem.cs` — Path A copy loop
  now sets `FlagsMeaningful = (short)evt.Results[i].FlagsMeaningful`.
- `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/EqsComponentLayoutTests.cs` — comment
  updated: `_pad` -> `FlagsMeaningful`.

**Tests added:**
- `EqsFlagsMeaningfulTests.cs` (4 tests):
  - T-FM1: struct size unchanged at 24 bytes after rename.
  - T-FM2: below-threshold path keeps `FlagsMeaningful == 0`.
  - T-FM3: above-threshold path sets `FlagsMeaningful` bit 0.
  - T-FM4: `FlagsMeaningful` value survives a full DDS round-trip.

---

### TASK-EQS-033 — `LastUpdateTimeSeconds` in `EqsCognitiveBuffer`

**Status: DONE**

**Files changed:**
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs` — `LastUpdateTimeSeconds
  float` added to `EqsCognitiveBuffer` at offset 8 (between `LastUpdateTick` and the
  result array).
- `Hrot/Subsystems/Hrot.SimHost/Systems/EqsResultUpdateSystem.cs` — both paths now
  stamp `buffer.LastUpdateTimeSeconds = (float)view.Time`.

**Tests added:**
- `EqsLastUpdateTimeTests.cs` (3 tests):
  - T-LUT1: Path B (unmanaged `EqsResultEvent`) stamps the correct simulation time.
  - T-LUT2: Path A (managed `EqsResultUpdateEvent`, empty result list) stamps the
    correct time and buffer becomes `IsReady`.
  - T-LUT3: `GetSpanRW()` still writes and reads back correctly after the layout
    change (regression guard).

---

### TASK-EQS-034 — `ScoreDeltaThreshold` and `EqsPublishPolicy.ScoreDelta`

**Status: DONE**

**Files changed:**
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs` — `ScoreDeltaThreshold
  float` added to `EqsSensor`; `EqsPublishPolicy` enum with `ScoreDelta = 3` added.
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsDdsTopics.cs` — `ScoreDeltaThreshold
  float` added to `EqsSensorConfigTopic`.
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsEvalState.cs` — `TopKScoreCache`
  inline-array struct and `LastPublishedTopK` field added to `SensorEvalState`.
- `Hrot/Network/Hrot.Network.NED/SimHost/EqsSensorConfigEgressTranslator.cs` —
  `ScoreDeltaThreshold` field copied to the DDS topic.
- `Hrot/Network/Hrot.Network.NED/SimHost/EqsSensorConfigIngressTranslator.cs` —
  `ScoreDeltaThreshold` field read from the DDS sample.
- `Hrot/Subsystems/Hrot.SimHost/Systems/EqsSolverSystem.cs` —
  `WriteResultsToPoolAndPublish` extended: when `PublishPolicy == ScoreDelta` the
  method compares each candidate score against `LastPublishedTopK`; suppresses the
  publish if all deltas are below threshold, updates the cache otherwise.
- `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/EqsLifecycleNodes.cs` —
  `EqsParams.ScoreDeltaThreshold` added; `Action_MaintainEqsSensor` now sets the
  field in the initial `AddComponent` call **and** increments `Epoch` when the
  threshold changes on subsequent ticks.

**Tests added:**
- `EqsScoreDeltaTests.cs` (3 tests):
  - T-SD1: small score changes (within threshold) keep `LastUpdateTick` unchanged;
    large changes advance it.
  - T-SD2: changing `EqsParams.ScoreDeltaThreshold` in `Action_MaintainEqsSensor`
    increments `EqsSensor.Epoch` exactly once.
  - T-SD3: `ScoreDeltaThreshold` value survives a full DDS round-trip.

---

## Issues Encountered

1. **`FlagsMeaningful` missing from `Action_MaintainEqsSensor` initial sensor creation.**
   The initial `AddComponent(new EqsSensor { ... })` call did not include
   `ScoreDeltaThreshold`. The field was written only on the update path, so the first
   tick always left it at `0f`. Fixed by adding `ScoreDeltaThreshold = p.ScoreDeltaThreshold`
   to the object initialiser. Test T-SD2 caught this on first run.

2. **`BTreeContext` lives in `Fdp.Toolkit.Behavior`, not `Fbt`.**
   I added `using Fbt;` (correct for `BehaviorTreeState`) but forgot
   `using Fdp.Toolkit.Behavior;` for `BTreeContext`. Added the missing directive.

3. **`Assert.Equal(int, int, string)` not available in this xUnit version.**
   xUnit v2 does not have a `string` overload for integer equality. The third argument
   was silently matching a `Func<int,int,bool>` overload instead, producing a compile
   error. Removed the message argument.

4. **Hex literals with alphabetic characters after `0x` are not valid C#.**
   Initial blueprint IDs were written as `0xFM020001u` etc., which are not legal hex
   (M is not a hex digit). Replaced with plain decimal literals (`2110001u`, etc.).

5. **`unsafe` context required for `TargetMemory.AddOrUpdateTarget` calls.**
   `TargetMemory` is declared `public unsafe struct`. Calling any method that
   takes it by ref requires the calling method to also be `unsafe`. Added `unsafe`
   keyword to T-FM2 and T-FM3 test methods.

---

## Weak Points Observed in the Codebase

1. **`Action_MaintainEqsSensor` initial-creation block is fragile.**
   Each time a new field is added to `EqsSensor` it must be copied into two places in
   that method: the initial `AddComponent` call and the update-path comparator/setter.
   The two copies have historically drifted (this batch proved it). A helper method
   that builds the `EqsSensor` value from `EqsParams` would eliminate the duplication.
   Recorded as a tech debt item.

2. **`EqsCognitiveBuffer` layout is tracked only via struct alignment rules.**
   There is no explicit assertion that `Results` starts at offset 16. If a future
   developer adds or reorders fields the silent padding will move the array without any
   test failure until a DDS round-trip test fires at runtime. A `[StructLayout(
   LayoutKind.Sequential)]` annotation with an explicit `[FieldOffset]` set or an
   offset unit test would make this robust.

3. **`EqsPublishPolicy` has a `_Reserved2` gap.**
   The gap was intentional (reserved for future use) but undocumented in the enum.
   A `[Obsolete]` attribute or XML-doc comment would prevent accidental usage.

---

## Design Decisions Beyond the Spec

1. **`FlagsMeaningful` is `short` in `EqsResult` (ECS) but `ushort` in `EqsResultEntry`
   (DDS).** The conversion cast `(ushort)r.FlagsMeaningful` is in the egress translator.
   The asymmetry exists because the ECS struct must match the existing 2-byte slot
   previously occupied by `_pad` (which was `short`) while the wire format uses unsigned.
   This is harmless for current bit-flags usage (values 0/1) but should be documented.

2. **`TopKScoreCache` uses `[InlineArray(16)]` capped to the pool's 16-element TopK
   limit.** The inline array avoids a heap allocation per sensor eval state while
   keeping the comparison loop cache-friendly. The cap is the same constant as
   `EqsResultPool.TopK` but is not derived from it; they must be kept in sync manually.

3. **`WriteResultsToPoolAndPublish` owns ALL `evalState` persistence** (both the
   suppress and the publish branches). The prior implementation had a duplicate persist
   in `EvaluateSensor` that was removed to avoid double-write. This is correct but
   non-obvious; a comment was added to the method explaining the ownership contract.

---

## Build and Test Results

```
dotnet build Hrot.ClusterRunner.Integration.Tests.csproj -c Debug
  Build succeeded. 0 Error(s)

dotnet test --filter "FullyQualifiedName~Eqs"
  Total tests: 43   Passed: 43   Failed: 0   Skipped: 0
  Total time: 44.68 s

dotnet test Fdp.Toolkits.Tests --filter "FullyQualifiedName~EqsComponent"
  Total tests: 4    Passed: 4    Failed: 0   Skipped: 0
  Total time: 5.98 s
```
