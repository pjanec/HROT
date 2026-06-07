# BATCH-05 REVIEW

**Reviewer:** Dev Lead
**Batch:** BATCH-05
**Tasks Reviewed:** TASK-C-19, TASK-C-10, TASK-C-13, TASK-C-18, TASK-C-15
**Decision:** APPROVED

---

## Test Quality Assessment

### TASK-C-19 — ResponseAssetMatcher (6 tests)

**PASS.** Tests verify the exact score values and mismatch boolean, not just "is non-zero":
- The score=0.5 test explicitly asserts the boundary condition: 0.5 is NOT < 0.5, so `IsLikelyMismatch = false`. This is the most important edge case (50% match is exactly at the threshold and should NOT trigger a dialog).
- The score < 0.5 test uses 1 of 3 matches (≈0.333) and asserts both the score comparison AND `IsLikelyMismatch = true`.
- The all-null ElementIds test verifies the "0 resolvable IDs → score=1.0 → no dialog" rule (important for pure `intent_shift` responses).
- The empty changes test covers the degenerate case.

**Implementation:** Clean and minimal. Uses LINQ to filter null ElementIds before scoring. Correct handling of the 1.0 default for the "no scorable IDs" case.

### TASK-C-10 — AssetSelectionDialog (5 tests)

**PASS — minor observation.** The separation of state (`AssetSelectionDialogState`) from rendering (`AssetSelectionDialog`) is clean and follows the existing codebase pattern. Tests verify:
- `Reverse()` swaps paths correctly and double-reverse restores them with `Reversed=false`.
- `Validate()` calls through to `AssetSelectionValidator` correctly with real temp files.
- `BuildResult()` propagates the reversed flag.

**Minor observation:** The batch spec requested 6 tests; the developer delivered 5 (merged the "BuildResult Reversed flag" scenario into the same test that checks path values). The delivered 5 tests cover all required behaviors. No action needed.

**Note on C-10 Q4:** The developer raised a valid design question in the Insights — the `PasteResponseModal.Apply` policy of treating "0 changes + warnings" as an error would incorrectly reject a valid LLM response that genuinely found no changes. This is added as D-12.

### TASK-C-13 — ExportDeliveryModal (7 tests)

**PASS — strong.** All 7 required tests implemented:
- `SaveToFile` success test writes to a real temp file and asserts exact byte content.
- `SaveToFile` failure test uses an invalid path (`Z:\DoesNotExist\...`) — returns a non-null error string.
- Clipboard threshold tests verify both the under-limit (returns text) and over-limit (returns null) cases. The over-limit test constructs a string of exactly `MaxClipboardBytes + 1` bytes.
- Preview line count test asserts exactly 31 elements (30 content lines + 1 marker line) using `Split('\n')`. This is precise.
- Default filename test uses a regex `\d{8}_\d{6}` to verify the timestamp format without hardcoding a specific timestamp — correct approach.

### TASK-C-18 — PasteResponseModal (5 tests)

**PASS.** Tests verify state transitions, not surface behaviors:
- The "apply twice" test asserts the second session's `TopLevelSummary` contains the second payload's text — not just that `GetSession()` is non-null.
- The failure test checks both `Apply()` returning false AND `ParseError` being non-null.
- The "ParseError null after success" test is a guard against state leaking between calls.
- Reset test verifies all three state fields return to defaults.

**Note on policy question:** The developer correctly notes that "0 changes, no warnings" (a valid LLM response with nothing to report) would also fail the `Apply` gate since it produces no warnings but also no changes. This is a design flaw: the gate should check for unrecoverable truncation warnings specifically, not "0 changes OR warnings". Added as D-12.

### TASK-C-15 — ComparisonToolbarAction + Editor Wiring (3 tests)

**PASS.** The 3 integration tests verify the most important pipeline correctness properties:
- The main pipeline test uses real temp files + a `FakeBTreeSanitizer` + real `ComparisonExportBuilder`. It asserts `exportText` is non-empty and contains both "VERSION A" and "VERSION B" — verifying structural correctness of the builder's output in a pipeline context.
- The instruction block test asserts `StartsWith("You are comparing")` on the real builder output — not just `Contains`.
- The preview test constructs a 40-line string, runs `GetPreviewText()`, and asserts exactly 31 elements (30 + marker) — same precise line count check as C-13.

**Positive:** `ComparisonToolbarAction` correctly uses `TryGet` rather than `Get` so it does not crash when no sanitizer is registered for the current kind. The modals are stored as fields (not re-created each frame) — correct for ImGui popup state.

**Note on BTree/HSM wiring:** The developer created `BTreeComparisonToolbar` and `HsmComparisonToolbar` thin wrappers in the BTree/HSM editor `Comparison/` folders. These are not wired into specific canvas window classes (since neither editor has a single `DrawUI()` window). This is acceptable for Phase 1 — the toolbar wrappers exist and are ready to be called. Added as D-13 (wire into actual host windows when they exist).

---

## Code Quality

**ResponseAssetMatcher:** Minimal and correct. The LINQ `.Where(c => c.ElementId != null)` filter correctly excludes null IDs before scoring.

**AssetSelectionDialogState:** The `Validate()` method correctly calls `AssetSelectionValidator.Validate()` with the discovered asset objects. The state exposes `ValidationError` and `ValidationWarning` as properties the modal can read each frame.

**ExportDeliveryModalState:** Well-structured. The `MaxClipboardBytes = 8 * 1024 * 1024` constant is a named constant (not a magic number). `GetPreviewText` uses `string.Join('\n', lines.Take(30))` — correct for normalized line endings from the builder.

**PasteResponseModalState:** Clean separation. The `Apply()` method accesses `response.Warnings.Count > 0 && response.Changes.Count == 0` as the error gate. This is slightly wrong for the "no changes found" valid case — see D-12.

**ComparisonToolbarAction:** The `TryGet` guard before calling `Build()` is correct. `Path.GetFileNameWithoutExtension` used for asset name in delivery modal — appropriate.

---

## Debt Items Added

| ID | Description | Priority | Target |
|----|-------------|----------|--------|
| D-12 | `PasteResponseModalState.Apply` rejects 0-change+no-warning responses (valid "nothing changed" result). Should check for TruncationWarning text specifically, not count-based heuristic. | P2 | BATCH-07 |
| D-13 | BTree and HSM comparison toolbar wrappers created but not integrated into actual host canvas windows (no `DrawUI()` call site exists yet for those editors). Wire when host windows are created. | P3 | BATCH-05 deferred |

---

## Decision

**APPROVED.** 478 tests pass. Build is clean. The logic-layer test pattern (state model tested without ImGui) is followed consistently. The `ComparisonToolbarAction` integration tests are the most important in the batch and they correctly exercise the full export pipeline.
