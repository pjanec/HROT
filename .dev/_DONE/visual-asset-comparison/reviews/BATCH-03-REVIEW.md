# BATCH-03 Review

**Batch:** BATCH-03
**Reviewer:** Dev Lead
**Date:** 2026-05-28
**Verdict:** APPROVED

---

## Summary

All tasks completed. Tests pass: AiShared 407, BTree 291, HSM 250, Blueprint comparison 16. 16 new tests added (13 Blueprint + 3 NoOp). D-08 refactor clean.

---

## Task-by-Task Assessment

### D-08 Debt Fix — APPROVED

The FakeCatalog/FakeAsset consolidation is clean and complete:
- BTree project: `FakeCatalogHelper.cs` created; private nested classes removed from all 3 BTree test files.
- HSM project: `FakeCatalogHelper.cs` created; private nested classes removed from all 3 HSM test files (the report correctly noted the instructions only mentioned `HsmComparisonSanitizerTests.cs`, but the developer proactively cleaned all 3 HSM test files — good initiative).
- All 291 BTree and 250 HSM tests still pass.

### TASK-C-08 (NoOp adapters) — APPROVED

**Implementation quality: Correct.**

- `NoOpComparisonMigrationAdapter.Adapt()` returns input unchanged, sets `didMigrate=false`. Matches the interface contract exactly.
- `NoOpMetaEnvelopeSanitizer.Sanitize()` returns input unchanged. Correct.
- `TryAddSingleton` used for registration — production adapters registered earlier will not be overwritten. This is the correct pattern.

**Test quality: Good.**

Three tests cover the contract precisely:
- `NoOpAdapter_Adapt_ReturnsSameJson_DidMigrateFalse`: checks reference equality AND `didMigrate=false`.
- `NoOpMetaSanitizer_Sanitize_ReturnsSameEnvelope`: checks reference equality.
- `DI_DefaultContainer_ResolvesNoOpAdapter`: actually builds a DI container and resolves the type — genuine integration check, not just "not null".

### TASK-C-09 (BlueprintComparisonSanitizer) — APPROVED

**Implementation quality: Good.**

- DOM manipulation is clean: `ProcessRootEditorMetadata` strips entirely; `ProcessGraphEditorMetadata` hoists CanvasComments (text-only) and removes the block; `ProcessNodeEditorMetadata` hoists Comment and removes the block.
- The alias `using AiCatalog = Hrot.Editor.AiShared.Catalog.IAssetCatalog` cleanly resolves the namespace ambiguity with the Blueprint editor's own `IAssetCatalog`.
- `PeerBlueprintId` is the correct JSON field name (confirmed against `Nodes.cs`).
- `SortPropertiesRecursive` correctly sorts object keys alphabetically while preserving array order.
- Never-throws contract verified (try/catch in `Sanitize`, `File.Exists` guard in core pipeline).

**Fixtures:** All three `.bp.json` fixtures correctly include the `<Content>` entry in the csproj for output directory copying. The `with_peer_call.bp.json` fixture uses the real `PeerBlueprintId` field, not the design doc's `TargetBlueprint`.

**Test quality: Good.**

Tests are behavior-driven, using `JsonNode.Parse` to assert on specific DOM paths:
- `Sanitize_NodeComment_IsHoistedToTopLevelNodeProperty`: checks `node["Comment"]` value AND `node["EditorMetadata"]` is absent.
- `Sanitize_CanvasComments_AreHoistedToGraphLevelWithTextOnly`: checks `graph["_canvasComments"]` is present, contains only `Text` (no `X`/`Y`), and `EditorMetadata` is absent.
- `Sanitize_NodePositionXY_IsStripped`: uses both string `DoesNotContain` and DOM-level `node["EditorMetadata"]` null check.
- `Sanitize_CallPeerBlueprint_AddsTargetName_*`: checks `node["_targetName"]` value for both catalog-hit and miss cases.
- `Sanitize_OutputIsAlphabeticallySorted`: parses the output JSON and verifies key ordering by comparing `allKeys` to `allKeys.OrderBy(...)`.
- `Sanitize_ShuffledInput_SameOutputAsCanonicalInput`: deserializes and re-serializes in reverse-alpha key order, verifies byte-identical sanitized output.
- `Sanitize_WithFakeMigrationAdapter_MigrationNoticePopulated`: verifies `Metadata.MigrationNotice` is non-null and contains "migrated".

**Gap noted:** No test for a Blueprint where a `CallPeerBlueprint` node is present but `PeerBlueprintId` is absent or empty string — the sanitizer handles this via `?.GetValue<string>()` and GUID parse, but it's untested. Low risk since the real emitter always writes this field when the node type is used. Not tracking as debt (P4 scenario).

---

## No New Debt Registered

No implementation shortcuts or fragile code paths found that warrant debt tracking. The Blueprint sanitizer is appropriately simple: strip by key name, hoist by key name, sort. The design's "strip-unknown-by-default at node level" policy means future EditorMetadata keys are naturally handled without code changes.

---

## Approved

Clean implementation, thorough tests, no new debt. All four asset kind sanitizers are now complete and registered. The batch is approved for commit.
