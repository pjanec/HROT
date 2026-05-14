# FDP Replay Browser — Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| RB01-P3-001 | BATCH-01 (RB-1.2) | `JsonExportOptions` round-trip test does not exercise `List<Entity>` with actual entities. `Entity` lacks `[JsonConstructor]`. Add a converter or constructor attribute and re-test with non-empty entity list. | P3 | BATCH-02 or BATCH-03 | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)
