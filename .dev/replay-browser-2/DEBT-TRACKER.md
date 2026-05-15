# FDP Replay Browser — Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| RB01-P3-001 | BATCH-01 (RB-1.2) | `JsonExportOptions` round-trip test does not exercise `List<Entity>` with actual entities. `Entity` lacks `[JsonConstructor]`. Add a converter or constructor attribute and re-test with non-empty entity list. | P3 | BATCH-03 | OPEN |
| RB02-P2-001 | BATCH-02 (EX-T22) | `EX_T22_NullSerializer_FallsBackToAutoSerializer` tests null-serializer fallback instead of custom `IEntityScenarioTranslator` injection as specified by DESIGN.md §3.8. Must be replaced with `EX_T22_CustomTranslator_IsHonored_PayloadReflectsStubDto` using a `FooHarnessBlackboardTranslator` stub. | P2 | BATCH-02C | OPEN |
| RB02-P3-002 | BATCH-02 (EX-T13) | `EX_T13_ByTime_WindowsCorrectly` assertion `frames.Count >= 1 && frames.Count <= 3` should be `Assert.Equal(2, frames.Count)` given deterministic 1-second frame spacing. | P3 | BATCH-02C | OPEN |
| RB02-P3-003 | BATCH-02 (EX-T20) | `EX_T20_NumericArrayPayloads_AreFlattenedToSingleLine` uses `HarnessPosition` (individual float fields) which serializes as a JSON object, not an array. `FlattenNumericArrays` is never exercised. Need a component with a `Vector3`-shaped array field. | P3 | BATCH-03 | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)
