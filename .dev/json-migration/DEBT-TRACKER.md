# JSON Migration System — Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| D-001 | BATCH-01 review | `JsonEnvelope.ReadMetaObject` lets `InvalidOperationException` escape when `schemaVersion` is a non-integer token; T1-010 masks this with `ThrowsAny<Exception>`. Fix: wrap `reader.GetInt32()` in try-catch, re-throw as `MigrationException`. Update T1-010 to `Assert.Throws<MigrationException>`. | P2 | BATCH-02 | RESOLVED |
| D-002 | BATCH-01 review | `IJsonDocumentMigrator.Direction` is redundant (derivable from version delta, registry already validates via delta). Every migrator must supply it unnecessarily. Consider removing from interface. | P3 | BATCH-02 | RESOLVED |
| D-003 | BATCH-01 review | `MigrationReport.AddWarning(string)` is `public` and hardcodes path `"$"`, bypassing scope capture. Migrators using `ctx.Report.AddWarning` instead of `ctx.AddWarning` silently lose path info. Make `internal`. | P3 | BATCH-02 | RESOLVED |
| D-004 | BATCH-02 review | Encoding corruption: `ComponentDiffService.cs` was written back in wrong encoding (W-1252 interpreted as UTF-8), converting `—` to mojibake. Fixed directly by reviewer. | P1 | BATCH-02 | RESOLVED |
| D-005 | BATCH-02 review | T1-123 `MigrateToCurrent_PreservesEngineVersionField` missing — no explicit test that normal migration preserves diagnostic fields. | P2 | BATCH-03 | RESOLVED |
| D-006 | BATCH-02 review | T1-124 `MigrateToCurrent_PreservesCreatedUtcField` missing (same as D-005). | P2 | BATCH-03 | RESOLVED |
| D-007 | BATCH-02 review | T1-125 `MigrateToCurrent_PreservesCreatedByField` missing (same as D-005). | P2 | BATCH-03 | RESOLVED |
| D-008 | BATCH-02 review | T1-129 `MigrateToCurrent_MigratorThrowsAtStep2of3_DoesNotRunStep3` missing — no test that chain halts on first failure. Use StubMigrator.ApplyCallCount to verify. | P2 | BATCH-03 | RESOLVED |
| D-009 | BATCH-02 review | T1-136 `MigrateTo_NoPathExists_Throws` missing — T1-125 (impl) covers unknown docType, not unreachable version. | P2 | BATCH-03 | RESOLVED |
| D-010 | BATCH-02 review | T1-138 duration assertion uses `>= TimeSpan.Zero`; spec says "positive". Change to `> TimeSpan.Zero`. | P3 | BATCH-03 | RESOLVED |
| D-011 | BATCH-03 review | T1-293 does not pin the expected hash for non-ASCII input (`"\u00e9"`). The correct hash is `"4a99557e4033c353"` (not `"2db7e52e4d32d0c5"` as originally stated). Developer corrected in BATCH-04. | P3 | BATCH-04 | RESOLVED |
| D-012 | BATCH-03 review | T1-264 round-trip does not verify `Operations[0].Value` for the Set operation. Should assert the value is preserved through Serialize/Deserialize. | P3 | BATCH-04 | RESOLVED |
| D-013 | BATCH-03 review | DomDiffer treats arrays as monolithic leaf DiffValues; DiffToJournalConverter's array-index path is not exercisable from real DomDiffer output. Limitation should be documented in a comment on `DomDiffer.Diff`. | P3 | BATCH-04 | RESOLVED |
| D-014 | BATCH-04 review | T3-008 parity test covers only 5 of 9 IMigrationStorage methods. Missing: WriteJournal, FindJournal, DeleteJournal, DeleteSidecar coverage in the InMemory vs FileSystem comparison. | P3 | BATCH-05 | RESOLVED |
| D-015 | BATCH-04 review | T3-007 uses early `return` on non-Windows instead of an explicit xUnit Skip. Silently passes on Linux/macOS without executing any code. | P3 | BATCH-05 | RESOLVED |
| D-016 | BATCH-04 review | BATCH-04-INSTRUCTIONS.md states incorrect pre-computed hash `"2db7e52e4d32d0c5"` for U+00E9. Correct value is `"4a99557e4033c353"`. Documentation-only issue; test is correct. | P3 | BATCH-05 | RESOLVED |

Legend:
- **P1** = Critical (never enters tracker; always becomes a corrective task in the next batch)
- **P2** = Should fix (tracked here, assigned a target batch)
- **P3** = Nice to have (tracked here, best-effort)
- **Status:** OPEN / RESOLVED (do not delete resolved rows)
