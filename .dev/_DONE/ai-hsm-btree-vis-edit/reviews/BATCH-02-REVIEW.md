# BATCH-02 Review

**Batch:** BATCH-02 — Action Schema Exporter, Source Text Parser, Field Classifier
**Tasks:** TASK-BB-1a-01, TASK-BB-1a-02, TASK-BB-1a-04, TASK-BB-1a-05
**Verdict:** APPROVED with P2 debt item

---

## Summary

All 4 tasks implemented. 55 new tests in the Blackboard test folder, all green. Full solution builds clean. No regressions.

---

## Scope Check

- [x] TASK-BB-1a-01: `IActionSchemaExporter` + `ActionSchemaExporter` — correct public surface, reflection over all BTree/HSM/Shared attribute kinds
- [x] TASK-BB-1a-02: `ActionSchemaExporterCatalogWatcher` — disposable, subscribes/unsubscribes correctly, wired tests
- [x] TASK-BB-1a-04: `BlackboardSourceTextParser` — line-by-line, verbatim span capture, `HasAttribute` + `HasInitializer` extensions
- [x] TASK-BB-1a-05: `BlackboardFieldClassifier` — six-condition rule, all conditions independently tested

---

## Design Alignment

- `ActionSchemaEntry` record, `ActionHosting` flags, `BlackboardAccess` enum match the spec exactly.
- Classifier correctly treats leading `///` comment as allowed (condition 3) rather than checking it.
- `FieldParseResult` extension with `HasAttribute` / `HasInitializer` is a good forward-compatible addition — the classifier needs these booleans to avoid re-parsing.
- `TypeLoadException` guard in `ScanAssembly` is the correct approach for the test-runner assembly compatibility problem.

---

## Test Quality Assessment

**ActionSchemaExporterTests:**
- Tests actual FQN discovery, hosting flags, DtoType, `[BlackboardReadOnly]` → ReadOnly, `[BlackboardReadWrite]` → ReadWrite, unannotated → Unknown, HeavyDtoType extraction
- Watcher tests: `Rebuild()` called exactly once per catalog event, `Changed` fires once, watcher dispose prevents further calls
- Fixture class uses the real attribute types — tests are not superficial

**BlackboardSourceTextParserTests:**
- Tests span boundaries via `source.Substring(span.Start, span.Length)` — byte-exact assertions
- Covers: simple field, doc comment, attribute, multi-line, initializer, struct-not-found, empty struct, mixed fields
- Added test for struct keyword inside a line comment not being matched

**BlackboardFieldClassifierTests:**
- Each of the six conditions independently verified
- Tests for: editorManaged primitives/enums/known-struct/schema-DTO, `///` comment does NOT force ReadOnly

All tests assert specific values, not just no-exception. Aligned with design §16.1.

---

## Issues Found

### P2 — HSM actions with `void*` signature not discoverable in schema exporter

The `HsmActionAttribute` (and `HsmGuardAttribute`) target methods with `void*` parameters (unsafe interop), not managed `ref TDto` parameters. The schema exporter's `ExtractFirstRefParamType` returns null for void-pointer methods, so real HSM actions produce zero schema entries.

The test fixtures use `ref TestHsmDto dto` (managed) which correctly tests the reflection logic for the BTree/Shared path, but real HSM-only actions will never be surfaced by the schema exporter without a new convention.

**Impact:** HSM-only actions whose DTO type is not also used by a `[SharedAiAction]` will not appear in the type picker. Designers cannot bind HSM nodes to variables of those types.

**Workaround until fixed:** HSM actions should also carry `[SharedAiAction]` on a companion managed-signature method for the editor to pick up.

**Target batch:** BATCH-03 or dedicated corrective batch — add to DEBT-TRACKER.

---

## Suggested Git Commit Message

```
feat(blackboard): BATCH-02 action schema exporter, source text parser, field classifier

- IActionSchemaExporter + ActionSchemaExporter: reflection over BTree/HSM/Shared attrs
- ActionSchemaExporterCatalogWatcher: IAssetCatalog.Changed -> Rebuild() wiring
- BlackboardSourceTextParser: line-by-line verbatim span capture, HasAttribute/HasInitializer
- BlackboardFieldClassifier: six-condition editor-managed vs read-only rule
- Add Fbt.Kernel + Fhsm.Kernel project references to Hrot.Editor.AiShared
- 55 new tests in Hrot.Editor.AiShared.Tests/Blackboard/; 0 regressions

Closes TASK-BB-1a-01, TASK-BB-1a-02, TASK-BB-1a-04, TASK-BB-1a-05
```
