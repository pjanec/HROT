# BATCH-01: Sanitization Framework Interfaces + BTree Sanitizer

**Batch Number:** BATCH-01
**Tasks:** TASK-C-01, TASK-C-02, TASK-C-03, TASK-C-04
**Slice:** C-1 — Sanitization framework + BTree sanitizer
**Estimated Effort:** 12-16 hours
**Priority:** HIGH (foundational — all other slices depend on these interfaces)
**Dependencies:** None (entry batch)

---

## Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Developer Skill:** `.github\skills\developer\SKILL.md` — understand the batch workflow
2. **Design Document:** `.dev\visual-asset-comparison\Visual_Asset_Comparison_Detailed_Design.md` — read the **entire** document before touching code; pay special attention to §1.5, §3.1, §3.2, §3.3, §4.6, §10.3
3. **Task Details:** `.dev\visual-asset-comparison\TASK-DETAILS.md` — read TASK-C-01 through TASK-C-04 sections in full
4. **Debt Tracker:** `.dev\visual-asset-comparison\DEBT-TRACKER.md` — no open items yet

### Source Code Locations

| What | Path |
|------|------|
| Shared framework (new `Comparison/` sub-folder) | `Hrot/Editor/Hrot.Editor.AiShared/Comparison/` |
| Shared tests (new `Comparison/` sub-folder) | `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/` |
| BTree sanitizer (new `Comparison/` sub-folder) | `Hrot/Subsystems/AI/Hrot.BTree.Editor/Comparison/` |
| BTree tests (new `Comparison/` sub-folder) | `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/` |
| Shared DI extension (existing file to modify) | `Hrot/Editor/Hrot.Editor.AiShared/Di/SharedAiEditorServiceCollectionExtensions.cs` |
| BTree host services (existing file to modify) | `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeEditorHostServices.cs` |
| AssetKind enum (existing, may need to extend) | `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetKind.cs` |
| IAssetCatalog interface | `Hrot/Editor/Hrot.Editor.AiShared/Catalog/IAssetCatalog.cs` |
| IEditableAsset interface | `Hrot/Editor/Hrot.Editor.AiShared/Identity/IEditableAsset.cs` |
| Layout types (BTreeEditorLayout, NodeLayoutEntry, SubtreeSyncBinding) | `Hrot/Editor/Hrot.Editor.AiShared/Layout/` |

### Test Execution

```powershell
# Run shared AiShared tests (TASK-C-01 tests)
dotnet test "Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj" -c Debug

# Run BTree editor tests (TASK-C-02, C-03, C-04 tests)
dotnet test "Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj" -c Debug

# Build everything to catch compilation errors
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4
```

### Report Submission

Submit completed report to: `.dev\visual-asset-comparison\reports\BATCH-01-REPORT.md`

---

## Context

This is the foundational batch for the Visual Asset Comparison feature (design §1–§3). It establishes:
1. The shared sanitization pipeline interfaces and registry (used by ALL subsequent slices)
2. The first concrete sanitizer: BTree (used as the reference implementation)
3. The determinism and self-comparison test harnesses (the correctness contract for the whole feature)

The design principle is that same input → same sanitized output, byte-identical. This is what allows comparing an asset against itself to produce zero changes.

The BTree sanitizer works on the **text** of C# source files — not via runtime reflection on compiled assemblies. This is critical: the asset being compared may be a historical file version that has never been compiled in the current session. `LayoutDiscovery` (which uses reflection) is NOT used here; instead the sanitizer implements text-based parsing of the C# file.

---

## Tasks

### TASK-C-01 — Sanitization Framework Interfaces and Export-Builder Skeleton

**Full spec:** See [TASK-DETAILS.md](../TASK-DETAILS.md#task-c-01--sanitization-framework-interfaces-and-export-builder-skeleton)
**Design refs:** §3.2, §4

**New files to create:**

| File | Description |
|------|-------------|
| `Hrot/Editor/Hrot.Editor.AiShared/Comparison/IAssetComparisonSanitizer.cs` | Core interface + record types |
| `Hrot/Editor/Hrot.Editor.AiShared/Comparison/IComparisonMigrationAdapter.cs` | Interface declaration only |
| `Hrot/Editor/Hrot.Editor.AiShared/Comparison/IMetaEnvelopeSanitizer.cs` | Interface declaration only |
| `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonExportBuilder.cs` | Skeleton returning placeholder |
| `Hrot/Editor/Hrot.Editor.AiShared/Comparison/SanitizerRegistry.cs` | Registry keyed by AssetKind |

**Existing files to modify:**

| File | Change |
|------|--------|
| `Hrot/Editor/Hrot.Editor.AiShared/Di/SharedAiEditorServiceCollectionExtensions.cs` | Register `SanitizerRegistry` as singleton |

**Key design points (read §3.2 for the full record types):**

`IAssetComparisonSanitizer` exposes:
- `AssetKind TargetKind { get; }`
- `SanitizationResult Sanitize(AssetExportRequest request)`

`AssetExportRequest` is a record with `AssetMainFilePath`, `CompanionDirectoryPath?`, `ExpectedKind`.

`SanitizationResult` is a record with `SanitizedText`, `Metadata: AssetMetadataBlock`, `Warnings: IReadOnlyList<SanitizationWarning>`.

`AssetMetadataBlock` carries `AssetName`, `Kind`, `AssetId`, `SourceFilePath`, `CompanionFiles`, `LastModifiedTimestamp?`, and also a `MigrationNotice: string?` (needed by the Blueprint sanitizer in TASK-C-09; leave nullable and default null).

`SanitizationWarning` is a simple record with a `Message: string`.

`SanitizerRegistry`:
- `Register(IAssetComparisonSanitizer sanitizer)` — registers by `sanitizer.TargetKind`
- `Get(AssetKind kind)` — returns the registered sanitizer or throws a descriptive exception: `"No comparison sanitizer registered for AssetKind.{kind}. Register one via SanitizerRegistry.Register()."`
- `TryGet(AssetKind kind, out IAssetComparisonSanitizer?)` — non-throwing variant

`ComparisonExportBuilder.Build(...)` skeleton:
- Signature: `Build(IAssetComparisonSanitizer sanitizer, AssetExportRequest versionA, AssetExportRequest versionB) : string`
- Returns the string constant `"<not implemented>"` for now (replaced in TASK-C-14)

**Tests required (`Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/SanitizerRegistryTests.cs`):**
- Register a fake `IAssetComparisonSanitizer` for `AssetKind.BTree`; `Get(AssetKind.BTree)` returns it
- `Get` for an unregistered kind throws an exception whose message contains the kind name
- `TryGet` returns false and null for unregistered kind
- Double-registration for same kind: second registration overwrites the first (or throws — document your choice)

**Tests required (`Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/ComparisonExportBuilderTests.cs`):**
- Calling `Build` on the skeleton returns the `"<not implemented>"` constant (regression test)

**Tests required (`Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/SanitizationTypesTests.cs`):**
- `AssetExportRequest`, `SanitizationResult`, `AssetMetadataBlock`, `SanitizationWarning` all have record equality (`Equals` round-trip with same values returns `true`)

---

### TASK-C-02 — `BTreeComparisonSanitizer` with Comment Hoist and Layout Truncation

**Full spec:** See [TASK-DETAILS.md](../TASK-DETAILS.md#task-c-02--btreecomparisonsanitizer-with-comment-hoist-and-layout-truncation)
**Design refs:** §3.3 (carefully read all of it, including both examples)

**New files to create:**

| File | Description |
|------|-------------|
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Comparison/BTreeComparisonSanitizer.cs` | Main sanitizer implementation |

**Implementation notes:**

The sanitizer works on the **file text** (reads `AssetExportRequest.AssetMainFilePath` from disk). It does NOT use `LayoutDiscovery` (reflection-based) because the file may be a historical version not compiled in the current process.

The text parsing strategy:
1. Read all lines of the file
2. Find the `[BTreeLayout(` line by scanning for that string prefix
3. Everything before that line is the "keeper" (pre-layout) content
4. Parse the layout method body (all lines after the attribute+method signature until the matching closing brace) to extract per-element metadata:
   - Each `.Node("visualId-string", ...comment: "...", ...expressionTarget: "...", ...)` call: capture the visualId, comment, expressionTarget
   - Each `.SubtreeSyncField("visualId-string", subDtoField: "...", masterPath: "...", direction: SyncDirection.In/Out/Both)` call: capture all four fields
   - Ignore `position:`, `size:`, `panOffset:`, `zoomLevel:`, `waypoints:`, `collapsed:`, `color:`
5. Walk the pre-layout content line by line; find each `.Node(`, `.Condition(`, `.Action(`, `.Sequence(`, `.Selector(`, `.Subtree(`, `.Decorator(` call that has a `visualId: new Guid("...")` argument. For each:
   - Identify the visualId from the `visualId: new Guid("...")` argument
   - Look up extracted metadata for that visualId
   - If there is a comment, insert a `// {comment}` line immediately before this builder call (preserving indentation)
   - If there are sync bindings, insert them as `// sync (in): ...`, `// sync (out): ...`, `// sync (both): ...` lines (using ASCII arrows `<--`, `-->`, `<-->`) after the comment line but before the builder call
   - For `.Subtree(` calls, look up the first string/Guid argument as a cross-asset reference and append `// -> AssetName (AssetKind)` inline comment at the end of that argument's line using the injected `IAssetCatalog`
6. Strip the `[XxxDefinition]` thunk body: find the thunk method (attribute `[BTreeDefinition(...)]`), keep the attribute and the method signature, replace the body with just `CreateBuilder().Compile("name")` where "name" is the string from the attribute
7. The result must preserve `using` directives, `namespace` declaration, and the file header comment

**Critical — determinism:**
- The output must be byte-identical for the same input, regardless of the order of `.Node(...)` entries in the layout method
- Sort the extracted metadata map by visualId (string sort) when looking up or applying — actually, DON'T sort; apply in order they appear in the `CreateBuilder()` chain, not the layout method. The layout method is what gets discarded; the builder chain order is preserved.
- Line endings: normalize all output line endings to `\n`

**Constructor:** `BTreeComparisonSanitizer(IAssetCatalog catalog)` — the catalog is injected.

**AssetKind.Blackboard note:** TASK-C-06 will need `AssetKind.Blackboard`. Add it to the `AssetKind` enum now (in `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetKind.cs`) so downstream tasks don't have to modify it again. Value: `Blackboard`.

**Metadata extraction for `AssetMetadataBlock`:**
- `AssetName`: parse from the `// AssetId: ...` header comment or from the class name / `[BTreeDefinition("Name", ...)]` attribute
- `AssetId`: parse the GUID from `// AssetId: ...` header comment
- `SourceFilePath`: `request.AssetMainFilePath`
- `CompanionFiles`: leave as empty list for now (companion discovery is TASK-C-11)
- `LastModifiedTimestamp`: use `File.GetLastWriteTimeUtc(request.AssetMainFilePath)`, or null on exception

**Warning cases:**
- No `[BTreeLayout(...)]` found → return sanitized text = the file content unchanged + warning "Layout method not found; comments/sync may be missing."
- Parse error (e.g., malformed content) → return result with warning; never throw

**Tests required (`Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/BTreeComparisonSanitizerTests.cs`):**
- Round-trip with the §3.3 "before" example: output equals the §3.3 "after" example (normalize line endings; compare byte-for-byte)
- Subtree + sync hoist: feeds the §3.3 "subtree with sync" example; verifies comment lines, sync lines, and asset-GUID humanization comment `// -> Shoot_BT (BTree)` appear in the correct positions using a fake `IAssetCatalog`
- Catalog miss: GUID not in catalog produces `// -> (asset not found in catalog)`
- Determinism: running sanitize 10 times on identical input yields byte-identical output each time (loop assertion)
- No layout method: returns input verbatim + `SanitizationWarning` containing "Layout method not found"
- Malformed file: returns a `SanitizationResult` with at least one warning; does not throw

**Registration:** Wire the sanitizer into BTree's DI. You can do this in `BTreeEditorHostServices.cs` or in the BTree project's DI extension if one exists. Look at how other services are registered; follow the existing pattern. The sanitizer itself only needs `IAssetCatalog` in its constructor.

---

### TASK-C-03 — BTree Sanitization Determinism Property Test

**Full spec:** See [TASK-DETAILS.md](../TASK-DETAILS.md#task-c-03--btree-sanitization-determinism-property-test)
**Design refs:** §4.6, §10.3

**New files to create:**

| File | Description |
|------|-------------|
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/BTreeSanitizationDeterminismTests.cs` | Determinism property tests |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/Fixtures/` | Folder containing ≥3 BTree `.cs` fixture files |

**Fixtures:** Create at least 3 synthetic BTree `.cs` files in the fixtures folder:
- `simple_guard.cs` — minimal BTree with 2-3 nodes, comments, and layout method
- `complex_combat.cs` — BTree with Sequence/Selector nesting, subtree reference, sync bindings, cross-asset GUID
- `malformed_no_layout.cs` — BTree source without any `[BTreeLayout]` (tests the no-layout-method path)

**Tests required:**
- For each fixture (except the malformed one), run the sanitizer 10 times; assert all outputs are byte-identical (`string.Equals` on all 10 results)
- For `simple_guard.cs` and `complex_combat.cs`: take the fixture, produce a "reordered" copy where the layout method `.Node(...)` entries are reordered (semantically equivalent), run both through the sanitizer; assert the two sanitized outputs are byte-identical (the sanitizer output depends on builder-chain order, not layout-method order, so this should hold)
- Malformed fixture: runs without exception; returns a non-empty warning list

---

### TASK-C-04 — Self-Comparison Round-Trip Integration Test

**Full spec:** See [TASK-DETAILS.md](../TASK-DETAILS.md#task-c-04--self-comparison-round-trip-integration-test)
**Design refs:** §4.6, §10.3

**New files to create:**

| File | Description |
|------|-------------|
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/BTreeSelfComparisonTests.cs` | Self-comparison integration tests |

**Tests required:**
- For each fixture (simple_guard.cs, complex_combat.cs): sanitize the same file twice using the same fake `IAssetCatalog` mock; assert both `SanitizationResult.SanitizedText` outputs are byte-identical
- For each fixture: sanitize using two **separate** catalog mock instances with identical content (same entries, different object references); assert both outputs are still byte-identical (proves the catalog mock's internal iteration order doesn't break determinism)
- Self-comparison assertion: do NOT call `ComparisonExportBuilder.Build` (it returns `"<not implemented>"`); test only that the two `SanitizationResult.SanitizedText` values are byte-identical, as stated in TASK-C-04 Success Conditions

---

## Mandatory Workflow

**CRITICAL: You MUST complete tasks in sequence with passing tests before moving on.**

1. **TASK-C-01:** Implement interfaces + registry → Write tests → ALL tests pass ✅
2. **TASK-C-02:** Implement BTreeComparisonSanitizer → Write tests → ALL tests pass ✅
3. **TASK-C-03:** Create fixtures → Write determinism tests → ALL tests pass ✅
4. **TASK-C-04:** Write self-comparison tests → ALL tests pass ✅

**After each task:** run `dotnet test` for the affected project and fix all failures before proceeding. Do NOT move to the next task with failing tests. Do NOT ask for permission to fix compiler errors or test failures — just fix them. Keep going until all tasks are done and all tests pass.

Build the entire solution at the end to catch any integration issues:
```powershell
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4
```

---

## Quality Standards

### Code Quality
- No compiler warnings (project is configured with `TreatWarningsAsErrors`)
- All new public APIs must have XML `<summary>` comments
- All new files must be in the correct namespace: `Hrot.Editor.AiShared.Comparison.*` or `Hrot.BTree.Editor.Comparison.*`
- Follow existing code style: record types, sealed classes, init-only properties
- Sanitizer must never throw — catch all exceptions and return a warning

### Test Quality
- Tests must verify ACTUAL BEHAVIOR, not just "object is not null"
- The BTree round-trip test (§3.3 before/after examples) is the most important test — implement it as the exact "before" and "after" strings from the design doc
- Determinism tests must use a loop of 10 iterations with `Assert.Equal` on each output vs. the first
- Do NOT write tests that just create an object and check it is not null — those are worthless

### Architecture
- All sanitizer implementations must inject `IAssetCatalog` via constructor (not service locator)
- `SanitizerRegistry` is a singleton; registration happens at startup, not on demand
- The `Blackboard` value must be added to `AssetKind` enum in this batch (needed by TASK-C-06 later)

---

## Developer Insights (Answer in Report)

**Q1:** What parsing challenges did you encounter when implementing the BTree text parser? What strategies did you use to handle edge cases in C# source text?

**Q2:** Did you spot any weak points or fragility in the text-based parsing approach? What input patterns could confuse the sanitizer?

**Q3:** What design decisions did you make beyond the spec (e.g., how to handle multi-line builder calls, how to detect the layout method boundary)?

**Q4:** Did the design's BTree "before" and "after" examples in §3.3 match your implementation, or did you find ambiguities?

**Q5:** Are there any performance concerns with the text-based approach for large BTree files? What would you optimize if this became a bottleneck?

---

## Success Criteria

This batch is DONE when:
- [ ] TASK-C-01: All interface files created; `SanitizerRegistry` works; `SanitizerRegistryTests`, `ComparisonExportBuilderTests`, `SanitizationTypesTests` all pass
- [ ] TASK-C-02: `BTreeComparisonSanitizer` produces correct output for the §3.3 examples; all `BTreeComparisonSanitizerTests` pass; sanitizer registered in BTree DI
- [ ] TASK-C-03: ≥3 fixture files created; all `BTreeSanitizationDeterminismTests` pass (10-run loop, reorder test)
- [ ] TASK-C-04: All `BTreeSelfComparisonTests` pass
- [ ] `dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4` succeeds with 0 errors
- [ ] `dotnet test "Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj"` passes
- [ ] `dotnet test "Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj"` passes
- [ ] Report submitted to `.dev\visual-asset-comparison\reports\BATCH-01-REPORT.md`

---

## Reference Materials

- **Design:** `.dev\visual-asset-comparison\Visual_Asset_Comparison_Detailed_Design.md` — §1.5, §3.1, §3.2, §3.3, §4.6, §10.3
- **Task specs:** `.dev\visual-asset-comparison\TASK-DETAILS.md` — TASK-C-01 through TASK-C-04
- **Existing layout types:** `Hrot/Editor/Hrot.Editor.AiShared/Layout/` — study BTreeEditorLayout, NodeLayoutEntry, SubtreeSyncBinding to understand the data model you're parsing text into
- **Existing BTree layout builder:** `Hrot/Editor/Hrot.Editor.AiShared/Layout/BTreeEditorLayoutBuilder.cs` — shows the exact C# source format the sanitizer must parse
- **Existing DI extension:** `Hrot/Editor/Hrot.Editor.AiShared/Di/SharedAiEditorServiceCollectionExtensions.cs` — follow the same pattern for registering the registry
- **Existing tests to study:** `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BTreeFluentEmitterTests.cs` — understand the emitted C# format
