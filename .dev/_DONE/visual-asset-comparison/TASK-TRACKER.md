# Visual Asset Comparison — Task Tracker

**Reference:** See [TASK-DETAILS.md](./TASK-DETAILS.md) for detailed task descriptions and success conditions.
**Design:** [Visual_Asset_Comparison_Detailed_Design.md](./Visual_Asset_Comparison_Detailed_Design.md)
**Debt:** [DEBT-TRACKER.md](./DEBT-TRACKER.md)

---

## Slice C-1 — Sanitization framework + BTree sanitizer

**Goal:** Land the shared interfaces, registry, and the first concrete sanitizer (BTree) with full determinism guarantees.

- [x] **TASK-C-01** Sanitization framework interfaces + export-builder skeleton [details](./TASK-DETAILS.md#task-c-01--sanitization-framework-interfaces-and-export-builder-skeleton) _(BATCH-01)_
- [x] **TASK-C-02** `BTreeComparisonSanitizer` with comment hoist + layout truncation [details](./TASK-DETAILS.md#task-c-02--btreecomparisonsanitizer-with-comment-hoist-and-layout-truncation) _(BATCH-01)_
- [x] **TASK-C-03** BTree sanitization determinism property test [details](./TASK-DETAILS.md#task-c-03--btree-sanitization-determinism-property-test) _(BATCH-01)_
- [x] **TASK-C-04** Self-comparison round-trip integration test (BTree) [details](./TASK-DETAILS.md#task-c-04--self-comparison-round-trip-integration-test) _(BATCH-01)_

---

## Slice C-2 — HSM and Blackboard sanitizers

**Goal:** Cover the two remaining C#-emitted asset kinds, reusing the framework from C-1.

- [x] **TASK-C-05** `HsmComparisonSanitizer` with comment hoist + layout truncation [details](./TASK-DETAILS.md#task-c-05--hsmcomparisonsanitizer-with-comment-hoist-and-layout-truncation) _(BATCH-02)_
- [x] **TASK-C-06** `BlackboardComparisonSanitizer` (inline + heavy concatenation) [details](./TASK-DETAILS.md#task-c-06--blackboardcomparisonsanitizer-inline--heavy-concatenation) _(BATCH-02)_
- [x] **TASK-C-07** HSM + Blackboard determinism + self-comparison tests [details](./TASK-DETAILS.md#task-c-07--hsm--blackboard-sanitizer-round-trip-and-determinism-tests) _(BATCH-02)_

---

## Slice C-3 — Blueprint sanitizer

**Goal:** Cover the JSON-based asset kind plus the optional no-op migration / meta-envelope dependencies.

- [x] **TASK-C-08** No-op `IComparisonMigrationAdapter` + `IMetaEnvelopeSanitizer` [details](./TASK-DETAILS.md#task-c-08--no-op-icomparisonmigrationadapter-and-imetaenvelopesanitizer-implementations) _(BATCH-03)_
- [x] **TASK-C-09** `BlueprintComparisonSanitizer` (DOM walk, strip, sort, re-serialize) [details](./TASK-DETAILS.md#task-c-09--blueprintcomparisonsanitizer-json-dom-walk-strip-editormetadata-sort-re-serialize) _(BATCH-03)_

---

## Slice C-4 — Export workflow

**Goal:** End-to-end from "Compare with…" toolbar action to a ready-to-paste `.txt` export.

- [x] **TASK-C-10** `AssetSelectionDialog` UI [details](./TASK-DETAILS.md#task-c-10--assetselectiondialog-ui) _(BATCH-05)_
- [x] **TASK-C-11** Companion-file discovery (single-file + folder modes; dot-prefix exclusion) [details](./TASK-DETAILS.md#task-c-11--companion-file-discovery-single-file--folder-modes) _(BATCH-04)_
- [x] **TASK-C-12** Asset-kind and AssetId validation at selection [details](./TASK-DETAILS.md#task-c-12--asset-kind-and-assetid-validation-at-selection) _(BATCH-04)_
- [x] **TASK-C-13** Export delivery modal (Save / Copy / preview) [details](./TASK-DETAILS.md#task-c-13--export-delivery-modal-save--copy--preview) _(BATCH-05)_
- [x] **TASK-C-14** `ComparisonExportBuilder` integration [details](./TASK-DETAILS.md#task-c-14--comparisonexportbuilder-integration-instruction-block--metadata--content) _(BATCH-04)_
- [x] **TASK-C-15** "Compare with..." toolbar action wired in all four editors [details](./TASK-DETAILS.md#task-c-15--compare-with-toolbar-action-wired-in-all-four-editors) _(BATCH-05)_

---

## Slice C-5 — Response parsing

**Goal:** Turn a pasted LLM response into a structured session.

- [x] **TASK-C-16** `LlmResponseParser` with robustness rules [details](./TASK-DETAILS.md#task-c-16--llmresponseparser-with-robustness-rules) _(BATCH-04)_
- [x] **TASK-C-17** `ComparisonSessionState` model [details](./TASK-DETAILS.md#task-c-17--comparisonsessionstate-model) _(BATCH-04)_
- [x] **TASK-C-18** "Paste LLM Response..." UI [details](./TASK-DETAILS.md#task-c-18--paste-llm-response-ui) _(BATCH-05)_
- [x] **TASK-C-19** Response/asset mismatch detection [details](./TASK-DETAILS.md#task-c-19--responseasset-mismatch-detection) _(BATCH-05)_
- [x] **TASK-C-20** LLM response parsing fixture suite [details](./TASK-DETAILS.md#task-c-20--llm-response-parsing-fixture-suite) _(BATCH-04)_

---

## Slice C-6 — Visualization

**Goal:** Surface the parsed response as canvas annotations, summary panel, and sidebar.

- [x] **TASK-C-21** `ComparisonAnnotationRenderer` (custom canvas renderer) [details](./TASK-DETAILS.md#task-c-21--comparisonannotationrenderer-custom-canvas-renderer) _(BATCH-06)_
- [x] **TASK-C-22** Severity -> color + kind -> badge mapping [details](./TASK-DETAILS.md#task-c-22--severity--color-and-kind--badge-mapping) _(BATCH-06)_
- [x] **TASK-C-23** `ComparisonSummaryPanel` docked window [details](./TASK-DETAILS.md#task-c-23--comparisonsummarypanel-docked-window) _(BATCH-06)_
- [x] **TASK-C-24** `ComparisonSidebar` docked window with click-to-focus [details](./TASK-DETAILS.md#task-c-24--comparisonsidebar-docked-window-with-click-to-focus) _(BATCH-06)_
- [x] **TASK-C-25** Variable-binding badges on nodes affected by `variable_renamed` [details](./TASK-DETAILS.md#task-c-25--variable-binding-badges--on-nodes-affected-by-variable_renamed) _(BATCH-06)_
- [x] **TASK-C-26** Blackboard Variables panel integration [details](./TASK-DETAILS.md#task-c-26--blackboard-variables-panel-integration) _(BATCH-06)_
- [x] **TASK-C-27** Exit Comparison Mode toolbar action [details](./TASK-DETAILS.md#task-c-27--exit-comparison-mode-toolbar-action) _(BATCH-06)_
- [x] **TASK-C-28** "Stale comparison" badge when asset saved during comparison [details](./TASK-DETAILS.md#task-c-28--stale-comparison-badge-when-asset-is-saved-while-comparison-is-active) _(BATCH-06)_

---

## Slice C-7 — Polish and robustness

**Goal:** Production-ready edges: large-export handling, preview, reverse-direction, comprehensive fixtures, error messages, user docs.

- [x] **TASK-C-29** 8 MB clipboard threshold check [details](./TASK-DETAILS.md#task-c-29--8mb-clipboard-threshold-check) _(BATCH-05)_
- [x] **TASK-C-30** Export modal preview polish (first 30 lines + "Show full") [details](./TASK-DETAILS.md#task-c-30--export-modal-preview-polish-first-30-lines--show-full) _(BATCH-05)_
- [x] **TASK-C-31** Reverse A<->B button [details](./TASK-DETAILS.md#task-c-31--reverse-ab-button) _(BATCH-05)_
- [x] **TASK-C-32** Comprehensive sanitization fixture corpus [details](./TASK-DETAILS.md#task-c-32--comprehensive-sanitization-fixture-corpus) _(BATCH-07)_
- [x] **TASK-C-33** Error handling polish [details](./TASK-DETAILS.md#task-c-33--error-handling-polish) _(BATCH-07)_
- [x] **TASK-C-34** User-facing documentation [details](./TASK-DETAILS.md#task-c-34--user-facing-documentation) _(BATCH-07)_

---

## Slice C-8 — Optional ghost rendering (DEFERRED)

**Goal:** Phase 1.5 polish only — implement if user feedback after Phase 1 indicates removed-node visibility is needed. See §6.5 of the design.

- [ ] **TASK-C-35** Read sanitized Version A to enumerate removed nodes (deferred)
- [ ] **TASK-C-36** Render ghost nodes at approximate positions (deferred)
- [ ] **TASK-C-37** Ghost click handling routing to sidebar entry (deferred)
