# BATCH-02 Report

**Batch:** BATCH-02
**Date:** 2026-05-28
**Status:** Complete

---

## Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| D-02 Debt Fix | Complete | Line-order assertions added to subtree+sync BTree test |
| TASK-C-05 | Complete | HsmComparisonSanitizer + DI extension + 7 unit tests |
| TASK-C-06 | Complete | BlackboardComparisonSanitizer + DI wiring + 7 unit tests |
| TASK-C-07 | Complete | HSM determinism (4 tests) + self-comparison (4 tests) + Blackboard determinism (4 tests) + self-comparison (3 tests) |

---

## Testing Results

| Test Project | Before | After | Delta |
|---|---|---|---|
| `Hrot.BTree.Editor.Tests` | 291 passed | 291 passed | +0 (D-02 upgraded existing test) |
| `Hrot.Hsm.Editor.Tests` | 235 passed | 250 passed | +15 new comparison tests |
| `Hrot.Editor.AiShared.Tests` | 390 passed | 404 passed | +14 new comparison tests |
| Solution build | 0 errors | 0 errors | Clean |

**Total new tests: 29** (15 HSM + 14 AiShared)

### Detailed Test Output

```
Hrot.BTree.Editor.Tests:
  Passed!  - Failed: 0, Passed: 291, Total: 291

Hrot.Hsm.Editor.Tests:
  Passed!  - Failed: 0, Passed: 250, Total: 250

Hrot.Editor.AiShared.Tests:
  Passed!  - Failed: 0, Passed: 404, Total: 404

Solution Build:
  Build succeeded.
```

---

## Implementation Summary

### D-02 Debt Fix

Updated `Sanitize_SubtreeWithSyncAndCatalog_HoistsCommentSyncAndHumanizesGuid` in
`BTreeComparisonSanitizerTests.cs` to add line-index ordering assertions after all the existing
`Contains` assertions. The additions:
- Split output by `\n`
- Find indices of comment line, sync-in line, sync-out line, `.Subtree(` call
- Assert `commentIdx < syncInIdx < syncOutIdx < subtreeIdx`

All existing assertions retained per instructions.

### TASK-C-05: HsmComparisonSanitizer

**New files:**
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Comparison/HsmComparisonSanitizer.cs`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Comparison/HsmEditorComparisonServiceCollectionExtensions.cs`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/HsmComparisonSanitizerTests.cs`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/Fixtures/simple_machine.cs`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/Fixtures/parallel_machine.cs`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/Fixtures/malformed_no_layout.cs`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj` (modified: added fixture handling)

The sanitizer:
1. Finds `[HsmLayout(` attribute line
2. Parses layout body via regex for `.State(`, `.Transition(`, `.Region(` calls — extracts GUID key (normalized to `guid.ToString("D")`) and `comment:` argument
3. Walks pre-layout lines for `stableId: new Guid("...")` and `visualId: new Guid("...")`
4. For `stableId:`: if the line starts with `}` (multi-line Child block closure), performs backward brace-depth scan to find the `.Child(` call opener; otherwise injects before the current line
5. For `visualId:`: always injects before current line (both `.On().GoTo()` and `builder.GlobalTransition()` are single-line in the emitter)
6. Truncates at `[HsmLayout(`, closes class with `}`
7. Strips `; manual edits...` header suffix
8. Returns warning + verbatim text if no layout found; never throws

### TASK-C-06: BlackboardComparisonSanitizer

**New files:**
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/BlackboardComparisonSanitizer.cs`
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/BlackboardComparisonSanitizerTests.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Di/SharedAiEditorServiceCollectionExtensions.cs` (modified: DI wiring)

The sanitizer:
1. Checks main file exists; returns warning if not
2. Reads inline file content (normalized to `\n`)
3. Derives companion path: strips `.Blackboard.cs` suffix → appends `.HeavyBlackboard.cs`
4. Assembles output: `// === Inline blackboard ===\n{inline}` and optionally `\n// === Heavy blackboard (overflow) ===\n{heavy}`
5. Parses `// OwningAssetId:` and `// OwningAssetName:` from inline file headers

### TASK-C-07: Determinism and Self-Comparison Tests

**New files (HSM):**
- `HsmSanitizationDeterminismTests.cs`: 10-run loop on simple_machine and parallel_machine, layout `.State()` reorder invariant, malformed fixture check
- `HsmSelfComparisonTests.cs`: same-file-twice and two-independent-catalog-instances for 2 fixtures

**New files (Blackboard):**
- `BlackboardSanitizationDeterminismTests.cs`: 3 inline-only shapes + 1 inline+heavy shape, all with 10-run loops
- `BlackboardSelfComparisonTests.cs`: inline-only, inline+heavy, two-independent-sanitizer-instances

---

## Design Decisions

### HSM Call-Start Detection

The key design challenge was locating the "call start" line for the stableId injection. Two cases arise in the emitter's output:

1. **Single-line declarations** (`builder.State(...)` or no-children `Child(...)`): the `stableId:` is on the same line as the call → inject before the current line.

2. **Multi-line Child blocks**: the `stableId:` appears on the closing `}, stableId: new Guid("...")` line. The emitter always produces the structure:
   ```
   parentVar.Child("Name", sb2 =>   <- call start
   {                                 <- opening brace
       ...                           <- body
   }, stableId: new Guid("..."));   <- stableId line
   ```
   Solution: detect `trimmedLine.StartsWith("}")` → backward brace-depth scan. Starting at the stableId line, count `}` as +1 and `{` as -1 across each line going backward. When depth reaches 0, the current line contains the opening `{`; return `i - 1` as the `.Child(` call.

This correctly handles nested Child blocks because the depth counter will cross intermediate `{}`  pairs before reaching the outermost opener.

### Region Handling

Region stableIds in the layout (`.Region("guid", ...)`) map to the same `stableId: new Guid("...")` in the Child calls for children of parallel states. Regions and States are merged into a single comment dictionary — no separate lookup needed. This works because stableIds are globally unique within an HSM asset.

### Blackboard Header Discrepancy

The batch instructions describe the header as `// AssetName:` and `// AssetId:`. The actual emitter (`BlackboardDtoEmitter.cs`) writes `// OwningAssetName:` and `// OwningAssetId:`. The sanitizer handles both variants (checking `OwningAssetId` first) to be forward-compatible and avoid fragility.

### Blackboard DI Wiring

The instructions say to register `BlackboardComparisonSanitizer` in `SharedAiEditorServiceCollectionExtensions.AddSharedAiEditor()`. This was done using the same DI factory pattern as BTree: a singleton factory that also wires into `SanitizerRegistry`. Since `BlackboardComparisonSanitizer` has no constructor parameters (unlike BTree/HSM which need `IAssetCatalog`), the factory is simpler: `new BlackboardComparisonSanitizer()`.

---

## Deviations from Instructions

| Area | Instruction | Actual | Reason |
|------|-------------|--------|--------|
| Blackboard header parsing | Instructions say `// AssetName:`, `// AssetId:` | Emitter uses `// OwningAssetName:`, `// OwningAssetId:` | Adapted to real emitter format; handles both for robustness |
| HSM SubtreeSyncField | Instructions say "ignore gracefully" | Not parsed at all — layout parser only processes `.State`, `.Transition`, `.Region` entries | Correct per note: "HsmEditorLayoutBuilder does NOT have .SubtreeSyncField()" |
| HSM visualId injection | Instructions say "inject above .On(...) line" | On(...).GoTo(...) is all on one line; injection before current line IS above .On | No deviation, the single-line format is what the emitter produces |

---

## Developer Insights

### Q1: HSM vs BTree builder chain format differences

The most significant structural difference between BTree and HSM:

**BTree:** The `visualId:` parameter appears on a continuation line of fluent chain, consistently indented deeper than the call start (e.g., `.Sequence(s => s.Action(..., visualId: new Guid("...")))`). This made the BTree backward scan clean: find the first line with STRICTLY FEWER leading spaces starting with `.`.

**HSM:** The `visualId:` for transitions appears on the SAME line as `.On("Event").GoTo("Target", visualId: ...)` — everything is on one line. The `stableId:` for states also appears on the same line for top-level State calls. Only for multi-line Child blocks does `stableId:` end up on a closing `}` line separate from the call.

The BTree's backward-scan-by-indentation approach was not reused for HSM. Instead, HSM uses:
- Direct inspection of whether the stableId line starts with `}` to choose between "inject here" and "brace-depth scan"

This is more explicit and correct for the imperative-style builder chain HSM uses.

### Q2: Handling both stableId and visualId in a single pass

The parser maintains a single merged `Dictionary<string, string>` for all element comments — States, Transitions, and Regions all share one dict keyed by normalized GUID string (`guid.ToString("D")`). In the pre-layout scan:
- `stableId:` lookups hit State+Region entries
- `visualId:` lookups hit Transition entries

Since the GUID space is shared (a state and a transition cannot have the same GUID in practice), there are no conflicts. If a conflict did arise (designer bug in asset authoring), the last-write-wins policy in the dict would silently resolve it — one comment would "win". This is acceptable; the real fix is at the authoring level.

Duplicate detection: within the pre-layout scan, `AddCommentInsertion` only adds the first comment for any given line (if `list.Count == 0`). This prevents duplicate injection if somehow the same call line is matched via both stableId and visualId patterns — a practically impossible scenario with valid HSM assets.

### Q3: Blackboard simplicity concerns

The Blackboard sanitizer's verbatim pass-through is intentionally minimal. Potential problem scenarios:

1. **BOM markers**: If the file has a UTF-8 BOM (`\xEF\xBB\xBF`), `File.ReadAllText` preserves it in the string. The output would then have a BOM embedded in the `// === Inline blackboard ===` section header but not at the file start. This would be subtle. `File.ReadAllText` with no explicit encoding uses the OS default, which handles BOM correctly on Windows. Low risk but worth noting.

2. **Non-LF line endings in heavy file**: Both inline and heavy file endings are normalized to `\n`. This ensures the concatenated output is deterministic regardless of how the files were written.

3. **Missing `.Blackboard.cs` suffix**: If a file with a non-standard name is passed, `DiscoverHeavyCompanion` returns null and no heavy file is sought. The sanitizer gracefully handles this.

### Q4: Backward-scan reliability for multi-line Child blocks

The brace-depth scan works correctly because the HSM emitter always produces well-structured C# with matching braces. The known fragility (same as BTree's D-01): if a comment or string literal contains `{` or `}`, the depth count would be wrong. The HSM emitter never puts braces in comments or the builder-chain string arguments (event names, state names, action FQNs), so this is a theoretical concern only.

One edge case I verified: nested Child calls (a region's children having their own Child calls). The brace-depth scan correctly identifies the outermost matching `{` by counting all inner `{}`  pairs. The test `Sanitize_ParallelRegions_HoistsRegionComments` covers a two-level nesting (Running → MotionTrack/AnimTrack).

### Q5: Test scenarios that would add value but were not specified

These are logged as P3 debt candidates:

- **D-05 (P3):** HSM sanitizer test for a state with both `stableId:` comment AND a `visualId:` transition originating from the same state — verify neither injection is confused with the other.
- **D-06 (P3):** Blackboard sanitizer test for a file with `AssetId:` header (design doc form) rather than `OwningAssetId:` — currently handled by the dual-check in `ExtractAssetId` but not explicitly tested.
- **D-07 (P3):** HSM test for deeply nested Child calls (3 levels) where `stableId:` on a level-3 `}` must correctly scan past level-2 `{}` pairs to find the level-3 `.Child(` opener.
- **D-08 (P3):** Blackboard self-comparison test where the inline file changes between runs (simulate a file-write race) — currently not tested since the sanitizer reads from disk each call.

---

## Known Issues / Limitations

1. **No cross-asset GUID humanization in HSM**: The `IAssetCatalog` is injected but not used (no cross-asset GUIDs in HSM Phase 1). This is correct per design; the constructor signature is kept consistent with BTree for DI uniformity.

2. **Brace-depth parsing susceptible to braces in string literals**: Inherited from BTree (D-01). Same fragility applies to HSM's multi-line Child scan. The emitter never produces such content.

3. **DI for BlackboardComparisonSanitizer is eager-singleton via factory**: The first time `SanitizerRegistry` is resolved, the Blackboard sanitizer registers itself. This matches the BTree pattern. If `BlackboardComparisonSanitizer` is never resolved from DI, it won't be registered. In practice all sanitizers must be resolved at startup.

---

## Outstanding Items for Next Batch

None — all tasks are complete and all tests pass.
