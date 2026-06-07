# BATCH-01 Report: Sanitization Framework Interfaces + BTree Sanitizer

**Batch:** BATCH-01  
**Tasks:** TASK-C-01, TASK-C-02, TASK-C-03, TASK-C-04  
**Status:** COMPLETE  

---

## Success Criteria Verification

- [x] TASK-C-01: All interface files created; `SanitizerRegistry` works; `SanitizerRegistryTests`, `ComparisonExportBuilderTests`, `SanitizationTypesTests` all pass
- [x] TASK-C-02: `BTreeComparisonSanitizer` produces correct output for the §3.3 examples; all `BTreeComparisonSanitizerTests` pass; sanitizer registered in BTree DI
- [x] TASK-C-03: 3 fixture files created; all `BTreeSanitizationDeterminismTests` pass (10-run loop, reorder test)
- [x] TASK-C-04: All `BTreeSelfComparisonTests` pass
- [x] `dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4` — **0 errors** (4 pre-existing file-copy warnings unrelated to this batch)
- [x] `dotnet test "Hrot/Editor/Hrot.Editor.AiShared.Tests/..."` — **Passed: 390, Failed: 0**
- [x] `dotnet test "Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/..."` — **Passed: 291, Failed: 0**

---

## Files Created

### TASK-C-01 — Framework Interfaces

| File | Description |
|------|-------------|
| `Hrot/Editor/Hrot.Editor.AiShared/Comparison/IAssetComparisonSanitizer.cs` | Core interface + record types (`AssetExportRequest`, `SanitizationResult`, `AssetMetadataBlock`, `SanitizationWarning`) |
| `Hrot/Editor/Hrot.Editor.AiShared/Comparison/IComparisonMigrationAdapter.cs` | Interface: `string Adapt(string rawJson, out bool didMigrate)` |
| `Hrot/Editor/Hrot.Editor.AiShared/Comparison/IMetaEnvelopeSanitizer.cs` | Interface: `string Sanitize(string metaEnvelopeJson)` |
| `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonExportBuilder.cs` | Skeleton builder returning `"<not implemented>"` |
| `Hrot/Editor/Hrot.Editor.AiShared/Comparison/SanitizerRegistry.cs` | Registry keyed by `AssetKind`; `Register`, `Get`, `TryGet` |

### TASK-C-01 — Modifications

| File | Change |
|------|--------|
| `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetKind.cs` | Added `Blackboard` value |
| `Hrot/Editor/Hrot.Editor.AiShared/Di/SharedAiEditorServiceCollectionExtensions.cs` | Registered `SanitizerRegistry` and `ComparisonExportBuilder` as singletons |

### TASK-C-01 — Tests

| File | Tests |
|------|-------|
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/SanitizerRegistryTests.cs` | 5 tests: register/get, missing-kind exception, TryGet variants, overwrite |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/ComparisonExportBuilderTests.cs` | 1 test: skeleton returns `"<not implemented>"` |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/SanitizationTypesTests.cs` | 5 tests: record equality for all 4 record types |

### TASK-C-02 — BTree Sanitizer

| File | Description |
|------|-------------|
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Comparison/BTreeComparisonSanitizer.cs` | Full text-based parser and sanitizer |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Comparison/BTreeEditorComparisonServiceCollectionExtensions.cs` | DI extension: `AddBTreeEditorComparison(services)` registers the sanitizer as singleton and wires it into `SanitizerRegistry` |

### TASK-C-02 — Tests (6 tests)

| File | Tests |
|------|-------|
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/BTreeComparisonSanitizerTests.cs` | Round-trip §3.3 before→after; subtree+sync hoist; catalog miss; 10-run determinism; no-layout warning; malformed no-throw |

### TASK-C-03 — Fixtures and Determinism Tests

| File | Description |
|------|-------------|
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/Fixtures/simple_guard.cs` | Minimal BTree: 2 nodes with comments, layout method |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/Fixtures/complex_combat.cs` | Complex BTree: Sequence+Selector nesting, Subtree reference with sync bindings |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/Fixtures/malformed_no_layout.cs` | BTree without `[BTreeLayout]` attribute |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/BTreeSanitizationDeterminismTests.cs` | 3 tests: 10-run loop (2 fixtures), node-order reorder invariant |

### TASK-C-04 — Self-Comparison Tests

| File | Tests |
|------|-------|
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/BTreeSelfComparisonTests.cs` | 4 tests: same file twice (2 fixtures), two independent catalog instances (2 fixtures) |

---

## Developer Insights

### Q1: Parsing challenges and edge case strategies

The main challenge was multi-line builder calls. A single `.Node(...)` or `.Condition(...)` call can span three or four lines (GUID argument, named parameters like `position:`, `comment:`, `expressionTarget:` each on their own line). The sanitizer tracks open-paren depth to collect complete call text, then applies regex patterns to the concatenated single-string.

The hardest edge case was finding the correct `callStart` line to insert a comment above. The algorithm: walk backward from the `visualId:` line and find the first line with **strictly fewer** leading spaces that also starts with `.`. This handles cases where the `visualId:` argument is indented further than the call start (alignment-style indentation like `.Condition(dto => dto.X, Actions.Y,\n           visualId: ...)`). The `<` strict inequality rather than `<=` is critical — it correctly skips sibling `.Condition(...)` lines that have the same indentation.

Another challenge: the header transformation. The line `// HROT_EDITOR_GENERATED — managed by AI editor; manual edits...` must become `// HROT_EDITOR_GENERATED — managed by AI editor.`. The strategy: split on the first `; ` and append `.`. This handles both formats correctly (when there is no `; `, the line is left unchanged).

### Q2: Weak points in the text-based parsing approach

Several fragile scenarios were identified:

1. **String literals containing parentheses** — the paren-depth tracker can be confused by `comment: "call foo(bar)"` since it counts `(` inside string literals. This would break `CollectCallText`. A proper fix would require a C# lexer. For the current real-world BTree format this doesn't arise (emitter-generated comments don't contain parentheses).

2. **Escaped double quotes in comments** — the regex `comment:\s*"((?:[^"\\]|\\.)*)"` handles `\"` escapes, but the round-trip test revealed that C# raw string literals (used for test fixtures) do NOT include a trailing `\n` before the closing `"""`. This distinction had to be handled explicitly in tests.

3. **Layout method not last in file** — the sanitizer assumes `[BTreeLayout(...)]` is the last method before the closing `}`. If a developer adds a method below the layout method (unusual but possible), the closing `}` appended by the sanitizer would produce invalid output with two `}` characters.

4. **Multiple classes in one file** — the sanitizer assumes a single class per file. If a BTree source file had multiple nested types, the heuristic for finding the class-closing `}` would not be reliable.

5. **GUID format variations** — the regex `[0-9a-fA-F\-]{36}` is permissive and would match GUIDs with incorrect hyphen placement. The regex relies on the emitter generating canonical 36-character GUIDs.

### Q3: Design decisions beyond the spec

**Double-registration policy:** The spec said "document your choice." `SanitizerRegistry.Register` silently overwrites when the same `AssetKind` is registered twice. This allows re-registration during test setup and hot-reload scenarios without throwing. The test `Register_DoubleRegistration_SecondOverwritesFirst` documents this contract.

**Layout method boundary:** The spec says to truncate "from the `[BTreeLayout(...)]` onward." I truncate by scanning lines from 0 to `lastNonBlank` (last non-blank line before the layout attribute), then appending `}\n`. This strips any blank lines between the last meaningful line and the layout attribute, matching the design's "after" output which has no blank line before `}`.

**DI extension for BTree sanitizer:** Since no pre-existing BTree DI extension existed, I created `BTreeEditorComparisonServiceCollectionExtensions` with `AddBTreeEditorComparison()`. This extension uses a factory-based singleton registration so the sanitizer is wired into `SanitizerRegistry` on first resolution (not at registration time), consistent with how Microsoft.Extensions.DependencyInjection handles lazy initialization.

**Line ending normalization:** All output uses `\n` (LF) regardless of platform. The sanitizer normalizes the input before processing and uses `Append('\n')` for all output lines. This makes the determinism guarantee platform-independent.

**`FindCallStartLine` backward scan skips blank lines:** If a blank line exists between the `visualId:` argument and the call start (unusual but possible due to formatting), the algorithm skips it and continues backward. This prevents false negatives.

### Q4: Design §3.3 examples vs. implementation

The design's §3.3 "subtree with sync" example uses a conceptual format for `SubtreeSyncField`:
```csharp
.SubtreeSyncField("guid", subDtoField: "FieldName", masterPath: "Master", direction: SyncDirection.In)
```

But the actual emitter (verified by reading `BTreeEditorLayoutBuilder.cs`) uses:
```csharp
.SubtreeSyncField("guid", "FieldName", masterVar: "Master", syncIn: true, syncOut: false)
```

These are different. The sanitizer was implemented to parse the **actual emitter format**, not the design doc's conceptual format. The test fixture for the subtree+sync test (`BTreeComparisonSanitizerTests.Sanitize_SubtreeWithSyncAndCatalog_HoistsCommentSyncAndHumanizesGuid`) uses the actual emitter format.

The §3.3 "OrcGuard before/after" example was used as an exact byte-for-byte round-trip test. One ambiguity discovered: C# raw string literals (`"""..."""`) do NOT include the trailing newline before the closing delimiter, but the sanitizer always terminates output with `\n`. The test was adjusted to append `"\n"` to the expected string.

### Q5: Performance concerns with text-based approach

The current implementation is `O(n)` where `n` is the number of lines, which is acceptable for typical BTree files (50–500 lines). However, two potential bottlenecks exist:

1. **Regex compilation** — all regex patterns are compiled as static fields (`RegexOptions.Compiled`). This amortizes compilation across multiple calls. If `BTreeComparisonSanitizer` is instantiated once per session (singleton via DI), this is fine.

2. **StringBuilder allocation** — `RebuildPreLayout` appends each line individually. For very large files (1000+ nodes), this could allocate significantly. A pre-sized `StringBuilder` using the file length as a capacity hint would be a low-risk improvement: `new StringBuilder(normalizedText.Length + 1024)`.

3. **File I/O** — each `Sanitize` call reads the file from disk. If called repeatedly for the same file (e.g., diffing version A against itself 10 times), adding a content cache keyed by `(filePath, lastWriteTime)` would eliminate redundant disk reads. However, since the sanitizer is designed for LLM-triggered comparisons (not hot-path code), raw file I/O is acceptable for now.

---

## Test Summary

| Test class | Count | Result |
|---|---|---|
| `SanitizerRegistryTests` | 5 | PASS |
| `ComparisonExportBuilderTests` | 1 | PASS |
| `SanitizationTypesTests` | 5 | PASS |
| **AiShared subtotal** | **11** | **PASS** |
| `BTreeComparisonSanitizerTests` | 6 | PASS |
| `BTreeSanitizationDeterminismTests` | 3 | PASS |
| `BTreeSelfComparisonTests` | 4 | PASS |
| **BTree subtotal** | **13** | **PASS** |
| **Total new tests** | **24** | **PASS** |

Full suite counts after batch (no regressions introduced):
- `Hrot.Editor.AiShared.Tests`: 390 passed, 0 failed
- `Hrot.BTree.Editor.Tests`: 291 passed, 0 failed
