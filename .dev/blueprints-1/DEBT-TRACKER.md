# Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| DEBT-001 | BATCH-01 | Generators cannot reference Hrot.Blueprints.Core (netstandard2.0 cannot ref net8.0). Generator only checks `{` start. Full schema validation needs a shared netstandard2.0 contracts assembly. | P3 | BATCH-07+ | OPEN |
| DEBT-002 | BATCH-01 | WorldResetTests.WorldResetEvent_IsPlainClass calls Assert.NotNull on a struct (xUnit2002). Suppressed with NoWarn. Test is logically pointless; fix in housekeeping pass. | P3 | BATCH-07+ | OPEN |
| DEBT-003 | BATCH-03 | `BreakpointKey(string NodeId)` record specified in TASK-TH-008 but not implemented. Breakpoints use raw strings. No functional impact in Slice 1. | P3 | BATCH-07+ | OPEN |
| DEBT-004 | BATCH-03 | `IBlueprintDebugSession` event named `OnPinValueChangedEvent` instead of `OnPinValueChanged` (design name) due to C# conflict with generic method of same base name. Deliberate deviation; add comment in source. | P3 | BATCH-07+ | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)
