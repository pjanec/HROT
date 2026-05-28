# Visual Asset Comparison — Task Details

**Reference design:** [Visual_Asset_Comparison_Detailed_Design.md](./Visual_Asset_Comparison_Detailed_Design.md)
**Tracker:** [TASK-TRACKER.md](./TASK-TRACKER.md)
**Debt:** [DEBT-TRACKER.md](./DEBT-TRACKER.md)

Each task below references the chapter of the design document that contains the full specification. Read both together. Section references like "§3.3" point to chapters of the design.

## Codebase locations referenced repeatedly

| Component | Path |
|---|---|
| Shared comparison code | `Hrot/Editor/Hrot.Editor.AiShared/Comparison/` (new sub-folder) |
| Shared tests | `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/` (new sub-folder) |
| BTree sanitizer | `Hrot/Subsystems/AI/Hrot.BTree.Editor/Comparison/` (new) |
| BTree tests | `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/` (new) |
| HSM sanitizer | `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Comparison/` (new) |
| HSM tests | `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/` (new) |
| Blueprint sanitizer | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Comparison/` (new) |
| Blueprint tests | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Comparison/` (new) |
| Shared asset catalog | `Hrot/Editor/Hrot.Editor.AiShared/Catalog/IAssetCatalog.cs` (existing — sanitizers DI on this) |
| Custom canvas renderer interface | `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/ICustomCanvasRenderer.cs` (existing) |
| Migration adapter (engine) | `Fdp.Core` `ReadOnlyMigrationAdapter` (per `.dev/json-migration/03-interfaces.md` §7.1) — **not yet implemented**; comparison ships its own no-op default until then |

> **Naming note:** the design document calls the Blueprint host `Hrot.Blueprint.Editor` (singular). The actual project on disk is `Hrot.Blueprints.Editor` (plural). All tasks below use the actual name.

---

## Phase 1 — Visual Asset Comparison

### Slice C-1 — Sanitization framework + BTree sanitizer

#### TASK-C-01 — Sanitization framework interfaces and export-builder skeleton

**Design refs:** §3.2, §4.

**Scope.** Create the public surface area for the comparison sanitization pipeline plus a non-functional `ComparisonExportBuilder` skeleton that other slices fill in.

**Deliverables.**
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/IAssetComparisonSanitizer.cs` with the interface and record types from §3.2 (`AssetExportRequest`, `SanitizationResult`, `AssetMetadataBlock`, `SanitizationWarning`).
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/IComparisonMigrationAdapter.cs` (interface declaration only; see TASK-C-08 for no-op implementation).
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/IMetaEnvelopeSanitizer.cs` (interface declaration only; see TASK-C-08 for no-op implementation).
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonExportBuilder.cs` — empty skeleton with method signatures matching §4.1 (assemble export from two `SanitizationResult`s + fixed instruction block). Returns the placeholder string `"<not implemented>"` until TASK-C-14.
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/SanitizerRegistry.cs` — a simple registry keyed by `AssetKind` so the BTree/HSM/Blackboard/Blueprint hosts can register their sanitizers at startup. The asset-selection dialog (TASK-C-10) looks up the right sanitizer here.
- DI extension method to register the registry as a singleton in `Hrot.Editor.AiShared`'s service-collection extensions.

**Success conditions (unit tests in `Hrot.Editor.AiShared.Tests/Comparison/`).**
- `SanitizerRegistryTests` — register a fake sanitizer for `AssetKind.BTree`; `Get(AssetKind.BTree)` returns it; `Get` for unregistered kind throws a descriptive exception naming the kind.
- `ComparisonExportBuilderTests` — given the skeleton, calling Build returns the placeholder constant (regression test; replaced in TASK-C-14).
- Interface types are public and have stable record-style equality semantics (covered by an `Equals` round-trip test).

**Dependencies.** None — entry slice.

---

#### TASK-C-02 — `BTreeComparisonSanitizer` with comment hoist and layout truncation

**Design refs:** §3.3 (especially steps 1–8), §3.3 example.

**Scope.** Implement the per-file sanitizer for BTree assets (`{Name}_BT.cs`). Operates on file text (or in-memory string) via `Hrot.Editor.AiShared.Layout.LayoutDiscovery` (existing) to find layout-method entries.

**Deliverables.**
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Comparison/BTreeComparisonSanitizer.cs` implementing `IAssetComparisonSanitizer` with `TargetKind = AssetKind.BTree`.
- The sanitizer executes §3.3 steps 1–9 in order:
  1. Locate `[BTreeLayout(...)]`.
  2. Parse layout-method body for per-element metadata (visualId, `comment:`, `expressionTarget:`, `SubtreeSyncField` entries).
  3. Walk the `CreateBuilder()` chain; inject `//`-comment lines above the matching `.Node`/`.State`/`.Subtree` call (comment first, then sync bindings with ASCII arrows `<--` `-->` `<-->`).
  4. Humanize cross-asset GUIDs via injected `IAssetCatalog` — append `// -> AssetName (AssetKind)` or `// -> (asset not found in catalog)` on the GUID argument's line.
  5. Truncate from `[BTreeLayout(...)]` onward.
  6. Collapse `[BTreeDefinition]` thunk body to a single-line `CreateBuilder().Compile(name)` form.
  7. Preserve `using` directives, namespace, header marker comment.
- Sanitizer is registered in `BTreeEditorHostServices` (or the existing module wiring) into `SanitizerRegistry` at startup.

**Success conditions (unit tests in `Hrot.BTree.Editor.Tests/Comparison/BTreeComparisonSanitizerTests.cs`).**
- **Round-trip example:** Feed the §3.3 "before" example; output equals the §3.3 "after" example byte-for-byte (line endings normalized to `\n`).
- **Subtree + sync hoist:** Feed the §3.3 "subtree with sync" example; output contains the three `//` comment lines in the order: node comment, `sync (in)`, `sync (out)` — and the asset-GUID humanization comment `// -> Shoot_BT (BTree)` (using a fake `IAssetCatalog` that maps the GUID to `Shoot_BT`).
- **Catalog miss:** GUID not in catalog produces `// -> (asset not found in catalog)`.
- **Determinism:** Running sanitize twice on identical input yields byte-identical output (run 10 times in a loop; all must match).
- **No layout method:** A file without `[BTreeLayout(...)]` returns the input verbatim (minus the absent strip) plus a `SanitizationWarning` "Layout method not found; comments/sync may be missing."
- **Malformed file:** Input that fails to parse (e.g., unbalanced braces) returns a `SanitizationResult` with a warning; never throws.

**Dependencies.** TASK-C-01.

---

#### TASK-C-03 — BTree sanitization determinism property test

**Design refs:** §4.6, §10.3.

**Scope.** Add the dedicated property-style determinism test for BTree sanitization across the BTree fixture corpus. Distinct from TASK-C-02's targeted determinism check.

**Deliverables.**
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/BTreeSanitizationDeterminismTests.cs`.
- A fixture folder under the test project holding ≥3 real-shaped BTree `.cs` files (use existing test fixtures from `Hrot.BTree.Editor.Tests` if any; otherwise hand-craft synthetic ones).

**Success conditions.**
- For each fixture, run the sanitizer 10 times; assert all outputs are byte-identical.
- For each fixture, run the sanitizer on a copy with reordered (semantically-equivalent) layout-method calls; assert the sanitized output is byte-identical to the original (the sort/scan must produce stable output regardless of source-file ordering of `.Node` entries).

**Dependencies.** TASK-C-02.

---

#### TASK-C-04 — Self-comparison round-trip integration test

**Design refs:** §4.6, §10.3, "no-change comparison" property.

**Scope.** Validate that comparing an asset against itself produces an export where Version-A content equals Version-B content byte-for-byte. This is the precondition for LLMs producing empty change lists on no-edit comparisons.

**Deliverables.**
- `Hrot.BTree.Editor.Tests/Comparison/BTreeSelfComparisonTests.cs` (covers BTree only in this slice; HSM/Blackboard added in C-2; Blueprint in C-3).
- Test uses the stub `ComparisonExportBuilder` from TASK-C-01 (will be a real builder after TASK-C-14; until then the test asserts only that the two `SanitizationResult.SanitizedText` outputs are byte-identical, not the assembled export).

**Success conditions.**
- For each BTree fixture, sanitize twice with the same `IAssetCatalog` mock; both sanitized texts byte-identical.
- For each fixture, swap the catalog instance for another with identical content; output still byte-identical (catalog mocks must not introduce iteration-order non-determinism).

**Dependencies.** TASK-C-02.

---

### Slice C-2 — HSM and Blackboard sanitizers

#### TASK-C-05 — `HsmComparisonSanitizer` with comment hoist and layout truncation

**Design refs:** §3.3 (HSM-specific notes), §3.5 transition.

**Scope.** Same shape as TASK-C-02 but for HSM assets. Structural identifiers are `stableId` for states/regions and `visualId` for transitions. Layout method is `[HsmLayout(...)]`. The HSM may host sub-BTrees via an orchestrator and carries the same `SubtreeSyncField`-style sync bindings (architect Q1 confirmed for HSM as well).

**Deliverables.**
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Comparison/HsmComparisonSanitizer.cs` implementing `IAssetComparisonSanitizer` with `TargetKind = AssetKind.Hsm`.
- Registered in `HsmEditorHostServices` (mirror of BTree host wiring).

**Success conditions (unit tests in `Hrot.Hsm.Editor.Tests/Comparison/HsmComparisonSanitizerTests.cs`).**
- Simple state machine fixture: comments hoisted to `//` above `.State`/`.Transition` calls.
- Machine with parallel regions: region-level comments hoisted with the region's owning call.
- Machine with global transitions: transition comments hoisted; transition `visualId` preserved.
- Machine hosting a sub-BTree via orchestrator with sync bindings: same hoist verification as BTree (ASCII arrows).
- Determinism: 10-run byte-identical loop.
- Malformed file: warning, never throws.

**Dependencies.** TASK-C-01.

---

#### TASK-C-06 — `BlackboardComparisonSanitizer` (inline + heavy concatenation)

**Design refs:** §3.4.

**Scope.** Read the inline `{Name}.Blackboard.cs` and optional `{Name}.HeavyBlackboard.cs` files; emit a labeled concatenation. No hoist needed — XML `///` comments are already canonical.

**Deliverables.**
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Comparison/BlackboardComparisonSanitizer.cs` (or in a Blackboard-specific folder — choose existing home of blackboard authoring code; place beside `Hrot.Editor.AiShared/Blackboard/SubtreeSyncBinding.cs`'s consumer if a dedicated host exists, else add to `Hrot.Editor.AiShared/Comparison/`).
  - Implements `IAssetComparisonSanitizer` with `TargetKind = AssetKind.Blackboard`.
- Output format per §3.4 step 5:
  ```
  // === Inline blackboard ===
  <inline file content verbatim>

  // === Heavy blackboard (overflow) ===
  <heavy file content verbatim or absent>
  ```
- Phase 1 keeps `[StructLayout]` attributes (per §3.4 step 4).

**Success conditions (`Hrot.Editor.AiShared.Tests/Comparison/BlackboardComparisonSanitizerTests.cs`).**
- Inline-only blackboard: output contains the inline section, no heavy section.
- Inline + heavy: output contains both labeled sections in order.
- XML `///` comments preserved verbatim.
- Determinism: 10-run byte-identical loop.

**Dependencies.** TASK-C-01.

---

#### TASK-C-07 — HSM + Blackboard sanitizer round-trip and determinism tests

**Design refs:** §10.3.

**Scope.** Same as TASK-C-03 + TASK-C-04 but extended to HSM and Blackboard.

**Deliverables.**
- `Hrot.Hsm.Editor.Tests/Comparison/HsmSanitizationDeterminismTests.cs`.
- `Hrot.Hsm.Editor.Tests/Comparison/HsmSelfComparisonTests.cs`.
- `Hrot.Editor.AiShared.Tests/Comparison/BlackboardSanitizationDeterminismTests.cs`.
- `Hrot.Editor.AiShared.Tests/Comparison/BlackboardSelfComparisonTests.cs`.

**Success conditions.**
- All four test classes verify the 10-run determinism property and the self-comparison byte-identical property across ≥3 fixtures each.

**Dependencies.** TASK-C-05, TASK-C-06.

---

### Slice C-3 — Blueprint sanitizer

#### TASK-C-08 — No-op `IComparisonMigrationAdapter` and `IMetaEnvelopeSanitizer` implementations

**Design refs:** §3.5 step 0, §3.5 step 1, §8.1.

**Scope.** Provide the default no-op implementations so the Blueprint sanitizer can land before the JSON Migration System exists. When the Migration System ships, production implementations swap in via DI and the comparison code does not change.

**Deliverables.**
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/NoOpComparisonMigrationAdapter.cs` — `Migrate(docType, JsonObject)` returns `(node, didMigrate: false, fromVersion: null, toVersion: null)`.
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/NoOpMetaEnvelopeSanitizer.cs` — `Sanitize($metaJsonObject)` returns the input unchanged.
- Both registered as default singletons in `Hrot.Editor.AiShared`'s DI extension; production code can override.

**Success conditions (`Hrot.Editor.AiShared.Tests/Comparison/NoOpAdapterTests.cs`).**
- No-op adapter returns the input DOM reference-equal to the input.
- No-op meta sanitizer returns the input reference-equal.
- DI test: resolving `IComparisonMigrationAdapter` from a default container returns the no-op.

**Dependencies.** TASK-C-01.

---

#### TASK-C-09 — `BlueprintComparisonSanitizer` (JSON DOM walk, strip EditorMetadata, sort, re-serialize)

**Design refs:** §3.5 (steps 0–8), §3.5 `$meta` classification table, §3.5 `EditorMetadata` classification table.

**Scope.** Per-asset sanitizer for `.bp.json` files.

**Deliverables.**
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Comparison/BlueprintComparisonSanitizer.cs` implementing `IAssetComparisonSanitizer` with `TargetKind = AssetKind.Blueprint`.
- Constructor injects `IComparisonMigrationAdapter`, `IMetaEnvelopeSanitizer`, `IAssetCatalog` (the shared `Hrot.Editor.AiShared.Catalog.IAssetCatalog`).
- Implements §3.5 steps 0–8 in order:
  0. Up-migrate via adapter (no-op default).
  1. Walk `$meta`; preserve `docType`, `schemaVersion`; strip `engineVersion`, `createdBy`, `createdUtc` via injected `IMetaEnvelopeSanitizer`.
  2. Walk every `EditorMetadata` block. Hoist `Comment` → `node.Comment`; hoist `CanvasComments` (Text only) → `graph._canvasComments`. Strip `X`, `Y`, `Viewport`, `DockState`, `NodeViewStates`.
  3. Remove now-empty `EditorMetadata` blocks.
  4. Humanize cross-asset GUIDs (`CallPeerBlueprint`, future kinds): inject `_targetName` from `IAssetCatalog`.
  5. Preserve `kind` discriminator.
  6. Preserve all `Id`, `FromNodeId`, `ToPinId`, `LinkedToIds`.
  7. Preserve variable/parameter/working-state declarations + comments.
  8. Re-serialize with stable property ordering: alphabetical within objects, source order within arrays.
- Sanitizer registered in `BlueprintEditorBootstrap` (or equivalent module wiring) into `SanitizerRegistry`.
- Result carries `Metadata.MigrationNotice` (nullable string) populated when the adapter reports `didMigrate=true`, e.g. `"Version A migrated from schema v3 to v4 to match Version B before comparison."`. This is what `ComparisonSummaryPanel` (TASK-C-23) displays.

**Success conditions (`Hrot.Blueprints.Tests/Comparison/BlueprintComparisonSanitizerTests.cs`).**
- §3.5 example fixture: input matches the "Before" JSON; output matches the "After" JSON byte-for-byte after both pass through `JsonNode.ToJsonString()` with identical writer options.
- `CallPeerBlueprint` node: `_targetName` matches the fake `IAssetCatalog` lookup; catalog miss → `_targetName = "(asset not found in catalog)"`.
- `$meta` envelope: `docType`, `schemaVersion` preserved; `engineVersion`/`createdBy`/`createdUtc` stripped.
- Per-node `Comment` hoisted to top-level node property; node's `X`, `Y` stripped.
- `CanvasComments` array hoisted to `_canvasComments`; positions stripped.
- All node `Id`, `FromNodeId`, `ToPinId`, `LinkedToIds` preserved.
- **Cross-schema migration:** Inject a fake `IComparisonMigrationAdapter` that promotes a v3-shaped DOM to v4; sanitizer's output exits with `$meta.schemaVersion=4` and `MigrationNotice` populated.
- **Default no-op adapter:** With `NoOpComparisonMigrationAdapter`, output is identical to the canonical-format-equivalent input (no migration, no notice).
- Determinism: 10-run byte-identical loop on the §3.5 fixture + ≥2 additional fixtures.
- Shuffled-input determinism: input with reversed key ordering inside objects produces byte-identical output to the canonical-ordered input.

**Dependencies.** TASK-C-01, TASK-C-08.

---

### Slice C-4 — Export workflow

#### TASK-C-10 — `AssetSelectionDialog` UI

**Design refs:** §7.1, §7.2.

**Scope.** Modal dialog for picking Version A. Reuses the editor's existing file-picker and folder-picker primitives.

**Deliverables.**
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/AssetSelectionDialog.cs`.
- Layout per §7.2 ASCII mock (active asset chip, single-file/folder radio, Browse buttons, Selected path display, Reverse A↔B, Cancel, Build).
- Dialog returns a `AssetSelectionResult` record with `VersionA: AssetExportRequest`, `VersionB: AssetExportRequest`, `Reversed: bool`.

**Success conditions.**
- Unit tests with a mock file-picker verify: single-file mode populates `AssetMainFilePath`; folder mode populates `CompanionDirectoryPath` with main file resolved by AssetId.
- Reverse toggle swaps A↔B in the returned record.

**Dependencies.** TASK-C-01.

---

#### TASK-C-11 — Companion-file discovery (single-file + folder modes)

**Design refs:** §3.6.

**Scope.** Implement the naming-convention scan plus the AssetId-match folder scan. Skip dot-prefixed directories (`.migration-snapshots/`, `.git/`, etc.).

**Deliverables.**
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/CompanionFileDiscovery.cs` with two entry points:
  - `DiscoverFromMainFile(mainFilePath, expectedKind)` returns the main path + companions found in the same directory by naming convention from §3.6 table.
  - `DiscoverFromFolder(folderPath, targetAssetId, expectedKind)` recursively scans (skipping dot-prefixed dirs) for the main file whose AssetId matches; then resolves companions from that file's directory.

**Success conditions (`Hrot.Editor.AiShared.Tests/Comparison/CompanionFileDiscoveryTests.cs`).**
- Single-file BTree: discovers `_BT.cs` + `.Blackboard.cs` + `.HeavyBlackboard.cs` + `.Orchestrators.g.cs` when all present.
- Missing companion: returns `notPresent=true` markers for the absent files (used in the metadata block).
- Folder mode: a folder containing a matching main file by AssetId is found; companions pulled from the same folder.
- **`.migration-snapshots/` excluded:** Folder containing `.migration-snapshots/Foo_BT.cs` (matching AssetId) is NOT picked up; the test asserts the file outside the dot-dir is chosen instead.
- Dot-prefixed dir exclusion is general (not hardcoded to `.migration-snapshots/`) — verified with a `.git/` fixture.

**Dependencies.** TASK-C-01.

---

#### TASK-C-12 — Asset-kind and AssetId validation at selection

**Design refs:** §3.7, §7.3.

**Scope.** Validation gate before the "Build Comparison Export" button enables.

**Deliverables.**
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/AssetSelectionValidator.cs`.
- Validates: files exist and are readable; asset kinds match; AssetIds match-or-warn; main file parseable enough to extract AssetId.

**Success conditions.**
- BTree vs Blueprint selection produces refusal with the exact message from §3.7 / §7.3.
- Same-kind mismatched AssetIds produces a `Warning` (allowed to proceed) with the §3.7 message text.
- Missing file: refusal "File not found".
- Permission denied: refusal "Cannot read file: permission denied".
- Unparseable file: refusal "Cannot parse Version A's metadata...".

**Dependencies.** TASK-C-11.

---

#### TASK-C-13 — Export delivery modal (Save / Copy / preview)

**Design refs:** §4.5.

**Scope.** Modal that appears after `ComparisonExportBuilder` builds the export text.

**Deliverables.**
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/ExportDeliveryModal.cs`.
- "Save to file…" opens system save dialog; default filename `{AssetName}_comparison_{timestamp:yyyyMMdd_HHmmss}.txt`.
- "Copy to clipboard" places the text on the OS clipboard.
- 8 MB threshold check disables Copy when over (TASK-C-29 covers this in polish but the hook should exist now as a constant).
- Read-only preview shows first 30 lines plus a "Show full" expander.

**Success conditions.**
- Unit tests with mock clipboard/file-picker: Save writes the byte-identical export text to the picked path; Copy posts the exact string to the mock clipboard.
- Preview helper returns first 30 lines for normal inputs; "Show full" toggles to full content.

**Dependencies.** TASK-C-14 (the modal consumes the builder output).

---

#### TASK-C-14 — `ComparisonExportBuilder` integration (instruction block + metadata + content)

**Design refs:** §4.1, §4.2, §4.3, §4.4, §4.6.

**Scope.** Replace the placeholder skeleton from TASK-C-01 with the full builder.

**Deliverables.**
- Full implementation of `ComparisonExportBuilder.Build(IAssetComparisonSanitizer, AssetExportRequest versionA, AssetExportRequest versionB)`:
  - Emits the §4.2 instruction block (fixed text — stored as a compiled-in resource string).
  - Emits two version sections, each with the §4.3 metadata block + `--- COMPANION FILES ---` marker + one `// === FILE: ... ===` header per file + sanitized content per §4.4.
  - 80-char `====` separator lines per §4.1.
  - Line endings normalized to `\n` per §4.6.
- When the sanitizer of either version reports a `Metadata.MigrationNotice`, the builder surfaces it: include a one-line `// MIGRATION NOTICE: ...` at the top of that version's section so the LLM sees the same fact the summary panel will display.

**Success conditions (`Hrot.Editor.AiShared.Tests/Comparison/ComparisonExportBuilderTests.cs`).**
- Two mock sanitized results produce the expected assembled text matching the §4.1 structure character-for-character (golden file in `Tests/Comparison/Fixtures/expected_export.txt`).
- Instruction block matches the §4.2 text exactly.
- Metadata blocks have stable key order per §4.3.
- File ordering within a version is main file first, companions sorted alphabetically by filename.
- Line endings in output are `\n` only.
- Migration notice fixture: when `Metadata.MigrationNotice` populated, the output contains the `// MIGRATION NOTICE: ...` line in the right version's section.
- Self-comparison: feeding identical mock `SanitizationResult`s for A and B produces an export where everything after each separator is byte-identical between the two version sections (modulo metadata path/timestamp).

**Dependencies.** TASK-C-01, TASK-C-02 (or any sanitizer for end-to-end smoke).

---

#### TASK-C-15 — "Compare with…" toolbar action wired in all four editors

**Design refs:** §7.1.

**Scope.** Add the toolbar action that opens the `AssetSelectionDialog`, runs the pipeline, and shows the export modal.

**Deliverables.**
- BTree canvas: toolbar button in `Hrot.BTree.Editor` (use the editor's existing toolbar registration).
- HSM canvas: same in `Hrot.Hsm.Editor`.
- Blueprint canvas: same in `Hrot.Blueprints.Editor`.
- Blackboard Variables panel: same in whichever project hosts the Variables panel (verify location — current grepping suggests it lives under the editor host module).
- Dropdown alongside main button offers "Compare with… (as A)" per §7.1.

**Success conditions.**
- Manual smoke test in the demo harness: clicking the action in each editor opens `AssetSelectionDialog`; after selection the export modal appears with non-placeholder content.
- Integration test (one per editor) loads a fixture asset, simulates dialog confirm, asserts a non-empty export text is produced.

**Dependencies.** TASK-C-02, TASK-C-05, TASK-C-06, TASK-C-09, TASK-C-10, TASK-C-12, TASK-C-13, TASK-C-14.

---

### Slice C-5 — Response parsing

#### TASK-C-16 — `LlmResponseParser` with robustness rules

**Design refs:** §5.1, §5.2, §5.3.

**Scope.** Parse the LLM response into a strongly-typed `ComparisonResponse` model.

**Deliverables.**
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/LlmResponseParser.cs`.
- `ComparisonResponse` record carrying: `Summary`, `Changes[]`, `Warnings[]`.
- `Change` record matching the §5.2 schema (`Kind`, `ElementId`, `ElementDescription`, `Field`, `OldValue`, `NewValue`, `Severity`, `Description`).
- Robustness rules per §5.3:
  - Strip ` ```json … ``` ` fences.
  - Strip leading/trailing whitespace.
  - Default missing optional fields to `null`.
  - Unknown `kind`/`severity` accepted with a warning, mapped to `node_modified` / `tuning` respectively.
  - Recover truncated JSON by finding the last complete `}` before truncation.
  - Never throw on a missing `elementId` — the change is preserved with `ElementId=null`.

**Success conditions (`Hrot.Editor.AiShared.Tests/Comparison/LlmResponseParserTests.cs`).**
- Well-formed response (§5.4 example): all fields populated correctly.
- Markdown-fenced JSON: unwrapped before parsing.
- Leading prose before `----- HUMAN SUMMARY -----` marker: tolerated; summary captured.
- Marker absent: parser falls back to "first JSON object is structured section" rule.
- Truncated JSON (recoverable): returns parsed `Change[]` up to the last complete object plus a warning.
- Truncated JSON (unrecoverable): returns an error `ComparisonResponse.Error` with the §5.3 user message.
- Unknown `kind`: parsed; warning collected.
- Unknown `severity`: parsed; warning collected.
- Missing required `Description`: filled with empty string; warning collected.
- `elementId` not present in the active asset: change preserved, no exception.

**Dependencies.** TASK-C-01.

---

#### TASK-C-17 — `ComparisonSessionState` model

**Design refs:** §6.2, §6.3, §6.9, §8.1.

**Scope.** Per-asset, in-memory holder for the active comparison. Drives the summary panel and sidebar.

**Deliverables.**
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonSessionState.cs`.
- Properties: `AssetId`, `Response` (`ComparisonResponse`), `MigrationNotice` (string?), `IsStale` (bool), `EnabledSeverities` (set).
- Methods: `ToggleSeverity(severity)`, `MarkStale()`, `Clear()`.
- A `ComparisonSessionRegistry` (singleton) holding one `ComparisonSessionState?` per asset, scoped per editor session, not persisted.

**Success conditions (`Hrot.Editor.AiShared.Tests/Comparison/ComparisonSessionStateTests.cs`).**
- Default `EnabledSeverities` = `{behavior, feature, removal, tuning}` (cosmetic disabled by default per §6.3).
- `ToggleSeverity` flips inclusion.
- `MarkStale()` sets `IsStale=true`.
- `Clear()` empties all changes.
- Registry keyed by AssetId; setting a new session for an existing AssetId replaces.

**Dependencies.** TASK-C-16.

---

#### TASK-C-18 — "Paste LLM Response…" UI

**Design refs:** §6.1.

**Scope.** A multi-line text input pane + Apply button + "Load from file…" alternative.

**Deliverables.**
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/PasteResponseModal.cs`.
- Two ingestion paths per §6.1.
- On Apply: invoke `LlmResponseParser`; on success, populate `ComparisonSessionRegistry` for the active asset; on failure, keep modal open with the error message visible.

**Success conditions.**
- Unit test: paste the §5.4 example; session is populated.
- Paste malformed JSON: error surfaces in the modal; session unchanged.
- Load-from-file: same parsing path.

**Dependencies.** TASK-C-16, TASK-C-17.

---

#### TASK-C-19 — Response/asset mismatch detection

**Design refs:** §7.6.

**Scope.** When the user pastes a response whose `elementId`s mostly don't resolve against the active asset's node table, surface a confirmation dialog.

**Deliverables.**
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ResponseAssetMatcher.cs` with `MatchScore(response, activeAssetNodeIds)` returning a fraction.
- Threshold: if <50% of non-null `elementId`s resolve, show the confirmation dialog "This LLM response references VisualIds not present in {AssetName}. Apply anyway?".

**Success conditions.**
- Score = 1.0 when all IDs resolve.
- Score = 0.0 when none resolve.
- Score = 0.5 when half resolve.
- Dialog shown only when score < 0.5; bypassed otherwise.
- Response with all `elementId=null` (rare; intent_shift only) does not trigger the dialog (no resolvable IDs to score).

**Dependencies.** TASK-C-16, TASK-C-17, TASK-C-18.

---

#### TASK-C-20 — LLM response parsing fixture suite

**Design refs:** §10.1 LlmResponseParserTests bullets.

**Scope.** Add a curated fixture suite covering the response shapes from §10.1.

**Deliverables.**
- `Hrot.Editor.AiShared.Tests/Comparison/Fixtures/Responses/` containing:
  - `well_formed.txt`
  - `markdown_fenced.txt`
  - `extra_leading_prose.txt`
  - `truncated_recoverable.txt`
  - `truncated_unrecoverable.txt`
  - `unknown_kind.txt`
  - `unknown_severity.txt`
  - `missing_required_field.txt`
  - `unresolvable_element_ids.txt`
- Each fixture has a paired golden expected-result JSON.

**Success conditions.**
- One test per fixture asserts the parser produces the expected `ComparisonResponse`.

**Dependencies.** TASK-C-16.

---

### Slice C-6 — Visualization

#### TASK-C-21 — `ComparisonAnnotationRenderer` (custom canvas renderer)

**Design refs:** §6.4 (outline style, dashed stroke spec, fallback rule).

**Scope.** Register `ICustomCanvasRenderer` with `Id = "comparison.annotations"`, `Pass = CanvasRenderPass.AfterNodes` (verify exact enum name in `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/ICustomCanvasRenderer.cs`).

**Deliverables.**
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/Rendering/ComparisonAnnotationRenderer.cs`.
- Per active `ComparisonSessionState`:
  - Dashed 2px stroke offset 3px outward from the node bounding box (dash 6px, gap 4px; stable across zoom).
  - Color by severity per §6.4 (cosmetic gray 60%, tuning blue, feature green, removal red strikethrough, behavior/intent_shift orange).
  - Badge on upper-right per §6.4 kind→glyph table.
- `connection_changed` fallback rule per §6.4:
  1. Both endpoints exist → badge at edge midpoint.
  2. One endpoint missing → badge on surviving endpoint.
  3. Neither exists → sidebar only (renderer skips).
- Renderer never throws on missing IDs.

**Success conditions (`Hrot.Editor.AiShared.Tests/Comparison/ComparisonAnnotationRendererTests.cs`, mock-canvas tests).**
- Outline is drawn 3px outside the node bounds with dashed stroke (verify draw commands on mock `ICanvasRenderContext`).
- Outline coexists with selection / Error / Warning outlines (both render to mock draw list without overlap on the same z-layer).
- `connection_changed` badge placed at edge midpoint when both endpoints exist.
- Same change with one missing endpoint: badge moves to surviving endpoint.
- Same change with neither endpoint: renderer issues no draw calls.
- Severity-filter applied: changes whose severity is disabled produce no draw calls.

**Dependencies.** TASK-C-17.

---

#### TASK-C-22 — Severity → color and kind → badge mapping

**Design refs:** §6.4.

**Scope.** Encapsulate the small mapping tables so other components (sidebar, summary panel) reuse the same constants.

**Deliverables.**
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/Rendering/ComparisonStyleMap.cs` — static class returning colors per severity and glyph strings per kind.

**Success conditions.**
- Unit tests assert each (severity, color) and (kind, glyph) pair matches the §6.4 tables exactly.

**Dependencies.** TASK-C-21.

---

#### TASK-C-23 — `ComparisonSummaryPanel` docked window

**Design refs:** §6.3.

**Scope.** Docked panel registered with id `ai_comparison_summary`, opened automatically when a session activates.

**Deliverables.**
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/ComparisonSummaryPanel.cs`.
- Renders (top→bottom): asset name, migration notice (if any), one-sentence top summary, full prose summary (scrollable), severity-filter toggles.
- Migration notice is only shown when `ComparisonSessionState.MigrationNotice` is non-null.

**Success conditions.**
- Snapshot tests: given a mock session, the rendered widget tree matches expected (use the editor's existing widget-snapshot test pattern if one exists; otherwise assert on the panel's internal model).
- Toggling a severity calls `ComparisonSessionState.ToggleSeverity` and triggers a renderer refresh (asserted via a fake renderer).

**Dependencies.** TASK-C-17, TASK-C-21.

---

#### TASK-C-24 — `ComparisonSidebar` docked window with click-to-focus

**Design refs:** §6.6.

**Scope.** Docked panel listing changes with the LLM's order; severity-filter aware; click focuses canvas on the affected node.

**Deliverables.**
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/ComparisonSidebar.cs` (id: `ai_comparison_sidebar`).
- Entry layout per §6.6.
- Click handler: pan + zoom the canvas to center the affected node, flash its comparison outline briefly.

**Success conditions.**
- Sidebar populated from a mock session shows all visible-severity entries in the LLM's order.
- Severity toggle hides matching rows in real time.
- Click on an entry with a resolvable elementId triggers the canvas-focus call on a fake canvas controller.
- Click on an entry with a removed-node (elementId not on canvas) does nothing (no exception).

**Dependencies.** TASK-C-17, TASK-C-21.

---

#### TASK-C-25 — Variable-binding badges (`↻`) on nodes affected by `variable_renamed`

**Design refs:** §6.4 variable_renamed bullet, §6.7.

**Scope.** When a change has `kind=variable_renamed`, the renderer must add the `↻` badge to every node whose binding references the renamed variable.

**Deliverables.**
- Extend `ComparisonAnnotationRenderer` with a binding-aware pass that scans the active asset's node table for bindings to `oldValue` / `newValue` of the renamed variable and adds `↻` badges.

**Success conditions.**
- Test fixture: BTree with two nodes binding `AmmoCount`; a `variable_renamed` change `AmmoCount → BurstShotsRemaining`; both nodes get `↻` badges; nodes not binding the variable get no badge.

**Dependencies.** TASK-C-21.

---

#### TASK-C-26 — Blackboard Variables panel integration

**Design refs:** §6.7.

**Scope.** When a session targets a Blackboard asset (or a host asset whose Blackboard has variable-level changes), the Variables panel rows pick up the severity outline + `↻ ➕ ➖` badges. Removed variables appear as ghost rows with strikethrough.

**Deliverables.**
- Hook in the existing Blackboard Variables panel rendering to read `ComparisonSessionRegistry` and decorate rows.
- Ghost-row rendering for `variable_removed`.

**Success conditions.**
- Variables panel test: a session with a `variable_added` row shows the `➕` badge + green outline on the added variable row.
- `variable_removed` produces a strikethrough ghost row at the end of the list.
- `variable_retyped` highlights the type cell in blue (tuning color).

**Dependencies.** TASK-C-21.

---

#### TASK-C-27 — Exit Comparison Mode toolbar action

**Design refs:** §6.8.

**Scope.** Clears the session, summary panel, sidebar, canvas annotations, stale badge — but never modifies the asset.

**Deliverables.**
- "Exit Comparison" action available via the canvas title-bar chip (click-to-close) and a View menu entry.
- Calls `ComparisonSessionRegistry.Clear(assetId)`.

**Success conditions.**
- After Exit, the renderer issues no comparison draw calls.
- Asset content (per a content hash before/after) is unchanged.

**Dependencies.** TASK-C-21, TASK-C-23, TASK-C-24.

---

#### TASK-C-28 — "Stale comparison" badge when asset is saved while comparison is active

**Design refs:** §6.2.

**Scope.** Subscribe to the editor's asset-saved event; when fired with an asset that has an active comparison session, set `IsStale=true` on the session and show the chip.

**Deliverables.**
- Subscriber in `Hrot.Editor.AiShared/Comparison/StaleBadgeWatcher.cs`.
- UI chip in the canvas title bar reads `ComparisonSessionState.IsStale`.

**Success conditions.**
- Load a session, save the asset (via the editor's save command in a test harness), assert `IsStale=true`.
- Re-paste a fresh response: `IsStale` resets to false.

**Dependencies.** TASK-C-17.

---

### Slice C-7 — Polish and robustness

#### TASK-C-29 — 8MB clipboard threshold check

**Design refs:** §4.5.

**Scope.** Disable the Copy-to-clipboard button when the export exceeds 8 MB, with a tooltip recommending Save-to-file. The hook was reserved in TASK-C-13; wire the size check.

**Deliverables.**
- Size check in `ExportDeliveryModal` using `Encoding.UTF8.GetByteCount(text) > 8 * 1024 * 1024`.

**Success conditions.**
- Mock 9 MB text: Copy button is disabled and tooltip is set.
- Mock 1 MB text: Copy enabled.

**Dependencies.** TASK-C-13.

---

#### TASK-C-30 — Export modal preview polish (first 30 lines + "Show full")

**Design refs:** §4.5.

**Scope.** Polish the preview UX from TASK-C-13. Implement the "Show full" toggle.

**Deliverables.**
- Toggle button in `ExportDeliveryModal` swaps between truncated and full content views.

**Success conditions.**
- Default: shows first 30 lines + ellipsis line "(N additional lines hidden — click Show full)".
- After toggle: shows entire content.

**Dependencies.** TASK-C-13.

---

#### TASK-C-31 — Reverse A↔B button

**Design refs:** §7.1, §7.2.

**Scope.** Toolbar dropdown action and dialog button per §7.1 mock. Swap which side gets the active asset.

**Deliverables.**
- "Compare with… (as A)" entry in the toolbar dropdown.
- "Reverse A↔B" button in `AssetSelectionDialog`.

**Success conditions.**
- Reverse action swaps `VersionA`/`VersionB` in the result record.
- Resulting export's `VERSION A` section's metadata block reflects the swap.

**Dependencies.** TASK-C-10, TASK-C-14.

---

#### TASK-C-32 — Comprehensive sanitization fixture corpus

**Design refs:** §10.1 (full bullet list across sanitizer tests).

**Scope.** Extend the fixture corpora introduced in slices C-1/C-2/C-3 to cover the full set called out in §10.1, including:
- Assets with no comments at all.
- Blackboards with only read-only-passthrough fields.
- Blueprints with deeply nested graphs.
- BTree with malformed file (graceful failure).
- HSM with parallel regions, global transitions, sub-BTree with sync bindings.

**Deliverables.**
- Add the missing fixtures to each per-editor `Tests/Comparison/Fixtures/` folder.

**Success conditions.**
- Each fixture is referenced by at least one test that asserts a specific behavior from §10.1.

**Dependencies.** TASK-C-02, TASK-C-05, TASK-C-06, TASK-C-09.

---

#### TASK-C-33 — Error handling polish

**Design refs:** §7.3, §5.3 user-facing messages.

**Scope.** Audit every failure mode for a clear, actionable message.

**Deliverables.**
- Audit + update messages in: `AssetSelectionValidator`, `LlmResponseParser`, `CompanionFileDiscovery`, `ComparisonExportBuilder`, `ExportDeliveryModal`.
- A central `ComparisonErrorMessages.cs` collecting all user-facing strings (for future localization).

**Success conditions.**
- Each failure mode has a test asserting the exact (or substring-matching) message.

**Dependencies.** TASK-C-12, TASK-C-16, TASK-C-11.

---

#### TASK-C-34 — User-facing documentation

**Design refs:** §1.1 use cases, §2 end-to-end workflow.

**Scope.** A short doc covering: what the feature is for, the export → LLM → paste workflow, recommended LLM prompts for the four use cases (PR review, AI-agent edit audit, refactor verification, regression hunt).

**Deliverables.**
- `.dev/visual-asset-comparison/USER-GUIDE.md`.

**Success conditions.**
- Documents covers all four use cases from §1.1 with concrete prompt snippets.
- Reviewed and approved by a non-author reader (manual gate).

**Dependencies.** TASK-C-15, TASK-C-18, TASK-C-27.

---

### Slice C-8 — Optional ghost rendering for removed nodes (deferred)

> Phase 1 ships **option (a) — sidebar only** per §6.5. The tasks below are kept as design-of-record for a Phase 1.5 polish slice; do not implement unless user feedback indicates they're needed.

#### TASK-C-35 — Read sanitized Version A to enumerate removed nodes

Read the LLM input's Version A section to map `node_removed` change `elementId`s back to enough information (action FQN, role) to label a ghost. Output: a `RemovedNodeInfo` record per removed change.

#### TASK-C-36 — Render ghost nodes at approximate positions

Renderer adds faded ghost nodes near each removed node's nearest surviving parent on the current canvas.

#### TASK-C-37 — Ghost click handling

Ghost click routes to the sidebar entry (focuses + flashes it).

---

## Cross-cutting test strategy

Per design §10.

- **Unit:** every task above has its own focused unit-test deliverable.
- **Integration:** TASK-C-15 carries the end-to-end "click toolbar → export → paste mock LLM response → see annotations" path. The mock LLM is a fixture file in `Hrot.Editor.AiShared.Tests/Comparison/Fixtures/Responses/end_to_end.txt`.
- **Round-trip property:** TASK-C-03, TASK-C-04, TASK-C-07, TASK-C-09's determinism block — for each asset kind, sanitize(canonical) must be byte-identical across 10 runs.
- **Manual checklist:** TASK-C-34 includes the manual demo-harness scenario from §10.4.

## Project dependency check

The comparison feature touches these projects. All paths verified via codebase memory; the feature adds new sub-folders only — no new csproj files.

| Project | Already references… | Comparison feature adds reference to… |
|---|---|---|
| `Hrot.Editor.AiShared` | NodeEditor.Core, System.Text.Json | (no new refs needed) |
| `Hrot.BTree.Editor` | Hrot.Editor.AiShared | (no new refs needed — sanitizer lives in BTree.Editor, consumes shared types) |
| `Hrot.Hsm.Editor` | Hrot.Editor.AiShared | (no new refs needed) |
| `Hrot.Blueprints.Editor` | Hrot.Editor.AiShared, System.Text.Json | (no new refs needed — System.Text.Json already in use for `.bp.json`) |
| `Hrot.Editor.AiShared.Tests` | xUnit | (no new refs needed) |

**Migration system dependency:** §3.5 step 0 / step 1 depend on `IComparisonMigrationAdapter` and `IMetaEnvelopeSanitizer`. The JSON Migration System is not yet implemented — TASK-C-08 ships no-op default implementations of both. The production implementations (wrapping `ReadOnlyMigrationAdapter` from `Fdp.Core` per `.dev/json-migration/03-interfaces.md` §7.1) ship with the migration system. Comparison code requires no changes when that swap happens.
