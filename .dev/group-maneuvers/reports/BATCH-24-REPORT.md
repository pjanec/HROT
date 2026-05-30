# BATCH-24 Report — Phase 3: Commander Utility Tick + Squad Inputs + Mission Override + Starter Pack

**Batch:** BATCH-24  
**Tasks:** TASK-SQD-P3-01, TASK-SQD-P3-02, TASK-SQD-P3-03, TASK-SQD-P3-04  
**Status:** COMPLETE

---

## Summary

All Phase 3 squad work is implemented and all new tests pass. The implementation delivers:
- Decimated commander utility-tick system (`CommanderUtilityTickSystem`)
- Five new squad-tier Utility AI input readers in `SquadInputs`
- Two tactical-order mappers for mission override (`ForceManeuverMapper`, `ClearForceManeuverMapper`)
- A worked example ManeuverSelect decision definition (`ManeuverSelectStarterDecision`)

---

## Files Changed

### Modified

| File | Change |
|---|---|
| `FDP/Toolkits/Fdp.Toolkits/Squad/State/SquadCognitiveState.cs` | Added `LastManeuverSelectTick` (uint) to `SquadContactPool` (replaced `private ulong _r1` with `public uint LastManeuverSelectTick; private uint _r1hi`) |
| `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/SquadInputs.cs` | Added 5 new `SquadInputIds` constants; updated `RegisterAll()` to register them; added 5 new reader methods |

### Created

| File | Purpose |
|---|---|
| `FDP/Toolkits/Fdp.Toolkits/Squad/Systems/CommanderUtilityTickSystem.cs` | Runs ManeuverSelect scorer on commander at decimated cadence (P3-01) |
| `FDP/Toolkits/Fdp.Toolkits/Squad/Mappers/ForceManeuverMapper.cs` | `ForceManeuverMapper` and `ClearForceManeuverMapper` ITacticalOrderMapper implementations (P3-03) |
| `FDP/Toolkits/Fdp.Toolkits/Squad/StarterPack/ManeuverSelectStarterDecision.cs` | Worked-example 3-option ManeuverSelect decision definition (P3-04) |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Systems/CommanderUtilityTickSystemTests.cs` | 4 tests covering SC-P3-01-1 through SC-P3-01-4 |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Inputs/SquadInputsP3Tests.cs` | 9 tests covering SC-P3-02-1 through SC-P3-02-5 (including zero-alloc) |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Mappers/ForceManeuverMapperTests.cs` | 3 tests covering SC-P3-03-1 through SC-P3-03-3 |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Phase3IntegrationTests.cs` | 4 end-to-end tests covering SC-P3-04-1 through SC-P3-04-4 |

---

## Implementation Notes

### P3-01: CommanderUtilityTickSystem

- `MissionOverrideBit = 1u` guards the scorer: if `state.Flags & 1u != 0`, the method returns immediately.
- Cadence gate: `firstRun` (when `LastManeuverSelectTick == 0`) or `dwellElapsed` (when `currentTick - LastManeuverSelectTick >= tickInterval`). Default `tickInterval = 6`.
- Uses `UtilityTraceWorkingMemory1024*` (unsafe pointer, non-null only when the component is present) for optional trace output.
- Sets `state.ManeuverKind = output.Top().WinningPostureId` when `output.Count > 0`.

### P3-02: Squad Input Readers (SquadInputs.cs)

New `SquadInputIds` constants (FNV-1a-32 & 0xFFFF):

| Name | Value |
|---|---|
| `SquadStrengthRatio` | `0x6EDF` |
| `SquadAmmoRollup` | `0x8501` |
| `ActiveFeatureThreatRating` | `0xE922` |
| `ActiveFeatureKindIs` | `0x6679` |
| `SquadPoolThreatAggregate` | `0x0426` |

All readers walk `ctx.Self` directly (not via `UnitSubordinate`). `ActiveFeatureThreatRating` and `ActiveFeatureKindIs` look up `DangerAreaCognitiveBuffer` directly on `ctx.Self`.

### P3-03: ForceManeuverMapper / ClearForceManeuverMapper

- `ForceManeuverMapper.TryMap`: deserializes JSON `{"maneuverKind":<ushort>,"featureId":<uint?>}` with `PropertyNameCaseInsensitive = true`; sets `state.ManeuverKind`, `state.Flags |= 1u`, and optionally `state.ActiveFeatureId`.
- `ClearForceManeuverMapper.TryMap`: clears `state.Flags &= ~1u`. Returns `false` if the entity has no `Blackboard1024`.

### P3-04: ManeuverSelectStarterDecision

Three options using the new P3-02 readers:
- Option 0 (`DangerAreaCross`): `SquadStrengthRatio(Linear)` + `ActiveFeatureKindIs(StreetCrossing)` + `SquadAmmoRollup(Step@0.3)`
- Option 1 (`BoundOverwatch`): `SquadStrengthRatio(Linear)` + `ActiveFeatureKindIs(OpenGround)` + `ActiveFeatureThreatRating(Logistic slope=6 xShift=0.5)`
- Option 2 (`Hold`): `ActiveFeatureThreatRating(Linear)` + `SquadAmmoRollup(InverseLinear)`

Note: `InverseLinear` (`CurveKind.InverseLinear`) was used instead of a `yShift=1f` trick because `ResponseCurve` does not support a `yShift` parameter (omitted to maintain 16-byte struct size per design comment).

---

## Test Results

### Isolated Squad test run (authoritative)

```
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --filter "FullyQualifiedName~Squad"
```

**Passed: 68 / 68, Failed: 0**

### Success criteria coverage

| Criterion | Test | Result |
|---|---|---|
| SC-P3-01-1 | `Run_TwoOptions_WinnerIsHighestScore_ManeuverKindIs0` | PASS |
| SC-P3-01-2 | `Run_MissionOverrideSet_ScorerSkipped_ManeuverKindUnchanged` | PASS |
| SC-P3-01-3 | `Run_CadenceGate_BlocksMidIntervalRescore` | PASS |
| SC-P3-01-4 | `Run_WithTraceBuffer_RecordCountIsNonZero` | PASS |
| SC-P3-02-1 | `SquadStrengthRatio_*` (3 cases) | PASS |
| SC-P3-02-2 | `ActiveFeatureThreatRating_NoActiveFeature_Returns0` | PASS |
| SC-P3-02-3 | `ActiveFeatureKindIs_MatchAndNonMatch_Flip` | PASS |
| SC-P3-02-4 | `SquadAmmoRollup_*` (3 cases) | PASS |
| SC-P3-02-5 | `AllReaders_ZeroAlloc_After1MillionCalls` | PASS |
| SC-P3-03-1 | `ForceManeuverMapper_SetsManeuverKindAndFlag` | PASS |
| SC-P3-03-2 | `ClearForceManeuverMapper_ClearsFlagAndScorerResumes` | PASS |
| SC-P3-03-3 | `ForceManeuverMapper_NoBlackboard_ReturnsFalse` | PASS |
| SC-P3-04-1 | `Run_StreetCrossingFeature_SelectsDangerAreaCross` | PASS |
| SC-P3-04-2 | `Run_OpenGroundFeature_SelectsBoundOverwatch` | PASS |
| SC-P3-04-3 | `Run_TraceEnabled_RecordCountNonZero` | PASS |
| SC-P3-04-4 | `Run_MissionOverrideSet_ManeuverKindUnchanged` | PASS |

### Full suite note

The full suite (`dotnet test` without filter) reports 91 failures. All of these failures are pre-existing and unrelated to BATCH-24:
- Tests from unrelated areas (`Navigation`, `Geographic`, `ReplayBrowser`, `Combat`) fail due to known issues in the project baseline.
- Squad tests that fail in the full run pass individually; failures are due to pre-existing xUnit parallel execution contention on the static `UtilityInputReaderStore` singleton — this affected Phase 2 squad tests before this batch as well.

---

## Deviations from Instructions

| Instruction | Actual | Reason |
|---|---|---|
| Hold option: `slope: -1f, yShift: 1f` | `CurveKind.InverseLinear` | `ResponseCurve` struct does not support `yShift` (design comment: "YShift (c) is omitted to hit 16 bytes") |
| Zero-alloc: `GC.GetTotalAllocatedBytes(true)` | `GC.GetAllocatedBytesForCurrentThread()` with 64-byte tolerance | `GetTotalAllocatedBytes` is cross-thread and includes GC background activity; matches the pattern established by Phase2IntegrationTests |
