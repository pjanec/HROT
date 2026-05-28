# BATCH-01 Review

**Batch:** BATCH-01
**Tasks:** TASK-C-01, TASK-C-02, TASK-C-03, TASK-C-04
**Status:** APPROVED (with P2/P3 debt items recorded)

---

## Verification Results

| Check | Result |
|-------|--------|
| `Hrot.Editor.AiShared.Tests` | 390 passed, 0 failed |
| `Hrot.BTree.Editor.Tests` | 291 passed, 0 failed |
| Solution build (`IOS-IG-SimHost.sln`) | 0 errors |
| TASK-C-01 deliverables | All present and correct |
| TASK-C-02 deliverables | All present and correct |
| TASK-C-03 deliverables | All present and correct |
| TASK-C-04 deliverables | All present and correct |

---

## Implementation Assessment

### TASK-C-01 — Framework Interfaces

**APPROVED.** All required types are present with correct record semantics:
- `IAssetComparisonSanitizer` interface clean and well-documented.
- `AssetExportRequest`, `SanitizationResult`, `AssetMetadataBlock`, `SanitizationWarning` as sealed records with proper XML docs.
- `AssetMetadataBlock.MigrationNotice` correctly added (needed by TASK-C-09).
- `SanitizerRegistry` with `Register`/`Get`/`TryGet`; Get exception message includes the kind name as required.
- Double-registration policy (silently overwrites) is documented via test `Register_DoubleRegistration_SecondOverwritesFirst` — acceptable choice.
- `AssetKind.Blackboard` added to the enum proactively — correct.
- DI wiring: `SanitizerRegistry` and `ComparisonExportBuilder` registered as singletons.

### TASK-C-02 — BTreeComparisonSanitizer

**APPROVED.** The text-based parser is well-structured and correct:
- The §3.3 "before" → "after" round-trip test passes byte-for-byte — this is the critical correctness proof.
- Comment/sync hoist works correctly: comment line appears before the builder call; sync lines appear after the comment and before the call.
- GUID humanization via `IAssetCatalog` works for both found and not-found cases.
- Header stripping (removing `; manual edits...`) works correctly.
- Never throws: catches `Exception` at the top level and returns a warning result.
- Line ending normalization to `\n` is correct.
- `FindCallStartLine` backward scan (strict `<` comparison) correctly handles indentation.
- DI wiring via `BTreeEditorComparisonServiceCollectionExtensions` is clean.

**Notable design decision:** The `SubtreeSyncField` parser uses the actual emitter format (`syncIn: bool, syncOut: bool`) rather than the design doc's conceptual `direction: SyncDirection.X` format. This is correct — the real files use the actual emitter format. The test uses the correct format too.

### TASK-C-03 — Determinism Tests

**APPROVED.** Three fixtures created covering the required scenarios:
- `simple_guard.cs`: minimal BTree with comments and layout.
- `complex_combat.cs`: complex with Sequence/Selector nesting, Subtree with sync bindings and a cross-asset GUID reference.
- `malformed_no_layout.cs`: no `[BTreeLayout]` attribute.
- 10-run determinism loop: correctly runs 9 additional iterations against the first reference output.
- Layout node reorder invariant: correctly swaps `.Node()` entries and asserts byte-identical output.

### TASK-C-04 — Self-Comparison Tests

**APPROVED.** Correctly tests both:
- Same sanitizer instance twice → byte-identical output.
- Two independent catalog instances with identical content → byte-identical output (proves iteration order of the fake catalog's `Dictionary<>` doesn't affect output).

---

## Test Quality Assessment

**Strong points:**
- The §3.3 round-trip test is a genuine byte-for-byte comparison against the design's "After" example — the gold standard test.
- Determinism tests use a proper 10-iteration loop, not just "run twice".
- Self-comparison tests cover both the single-instance and dual-instance catalog scenarios.
- FakeCatalog and FakeAsset implementations correctly implement all interface members.

**Weakness (P2 — logged to debt tracker):**
- The subtree+sync test uses `Assert.Contains` for all assertions rather than verifying the **ordering** of injected lines (comment first, then sync-in, then sync-out, then the builder call). The design §3.3 is explicit about order. A more thorough test would split the output by lines and assert relative positions.
- Duplicate `FakeCatalog`/`FakeAsset` infrastructure across three test classes. Minor code duplication but increases maintenance surface.

---

## Debt Items Recorded

| ID | Priority | Description | Source |
|----|----------|-------------|--------|
| D-01 | P2 | `CollectCallText` counts `(` inside string literals, breaking for comments containing parentheses. In practice the emitter never produces such comments but this is a fragility. | Developer report Q2 |
| D-02 | P2 | Subtree+sync test uses `Contains` assertions only — does not verify ordering of comment/sync lines relative to the builder call. Upgrade to line-order assertions in a future batch. | Review observation |
| D-03 | P3 | `BTreeComparisonSanitizer` does not strip block-bodied `[BTreeDefinition]` thunks (design §3.3 step 6). Emitter always generates expression-bodied methods so this is a no-op in practice. | Developer report Q3 |
| D-04 | P3 | `FakeCatalog`/`FakeAsset` duplicated across 3 test classes. Consolidate to a shared `TestHelpers/` class in a later refactor. | Review observation |

---

## Developer Insights Extracted

1. **Parsing fragility:** String literals containing parentheses would confuse the paren-depth tracker — worth noting for future sanitizer extensions.
2. **Real vs. conceptual emitter format:** The design doc's `SubtreeSyncField` uses a conceptual API (`direction: SyncDirection.In`); the actual emitter uses `syncIn: bool, syncOut: bool`. The sanitizer correctly targets the real format.
3. **Line ending normalization:** All output uses `\n` regardless of platform — critical for the determinism guarantee.
4. **Performance:** Text-based approach is O(n) in lines; no performance concerns for typical BTree files. Regex patterns are compiled (`RegexOptions.Compiled`) for amortized cost.

---

## Suggested Commit Message

```
feat(comparison): BATCH-01 – sanitization framework + BTree sanitizer (C-01..C-04)

TASK-C-01: Core comparison interfaces
- IAssetComparisonSanitizer, AssetExportRequest, SanitizationResult,
  AssetMetadataBlock (with MigrationNotice), SanitizationWarning records
- IComparisonMigrationAdapter, IMetaEnvelopeSanitizer interface declarations
- ComparisonExportBuilder skeleton (returns "<not implemented>")
- SanitizerRegistry with Register/Get/TryGet; descriptive exception on miss
- AssetKind.Blackboard added to enum
- SanitizerRegistry + ComparisonExportBuilder registered in shared DI

TASK-C-02: BTreeComparisonSanitizer (text-based, no reflection)
- Locates [BTreeLayout(...)], parses layout body for comments/sync/expressionTarget
- Hoists comments as // lines above matching builder calls
- Hoists SubtreeSyncField bindings as // sync (in/out/both): ... lines
- Humanizes cross-asset GUIDs via IAssetCatalog (found: "-> Name (Kind)", miss: "(asset not found)")
- Strips layout method; normalizes header; LF-only line endings
- BTreeEditorComparisonServiceCollectionExtensions for DI wiring
- 6 tests: §3.3 round-trip, subtree+sync, catalog-miss, 10-run determinism, no-layout warning, malformed no-throw

TASK-C-03: 3 fixture files + BTreeSanitizationDeterminismTests
- simple_guard.cs, complex_combat.cs (with Subtree+sync), malformed_no_layout.cs
- 10-run byte-identical loop (2 fixtures), layout-node-reorder invariant

TASK-C-04: BTreeSelfComparisonTests
- Same file sanitized twice: byte-identical (2 fixtures)
- Two independent catalog instances: byte-identical (2 fixtures)

Tests: 390 AiShared + 291 BTree, all passing. Build: 0 errors.
```
