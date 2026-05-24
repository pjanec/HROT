# Fluent BTree — Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| DT-001 | BATCH-01 | `TreeCompiler.CalculateStructureHash` missing `writer.Flush()` before `ComputeHash`. No behavior impact but fragile. | P3 | TBD | OPEN |
| DT-002 | BATCH-01 | `MethodNames` deduplication uses `List.IndexOf` (O(n)). Use `Dictionary<string, int>` for large trees. | P3 | TBD | OPEN |
| DT-003 | BATCH-01 | `GetDelegateKey` in BTreeBuilder does not null-guard `DeclaringType`. Affects lambda delegates. | P3 | TBD | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)
