# Visual Asset Comparison — Task Tracker

**Reference:** See [TASK-DETAILS.md](./TASK-DETAILS.md) for detailed task descriptions and success conditions.
**Design:** [Visual_Asset_Comparison_Detailed_Design.md](./Visual_Asset_Comparison_Detailed_Design.md)
**Debt:** [DEBT-TRACKER.md](./DEBT-TRACKER.md)

---

## Slice C-1 — Sanitization framework + BTree sanitizer

**Goal:** Land the shared interfaces, registry, and the first concrete sanitizer (BTree) with full determinism guarantees.

- [ ] **TASK-C-01** Sanitization framework interfaces + export-builder skeleton [details](./TASK-DETAILS.md#task-c-01--sanitization-framework-interfaces-and-export-builder-skeleton)
- [ ] **TASK-C-02** `BTreeComparisonSanitizer` with comment hoist + layout truncation [details](./TASK-DETAILS.md#task-c-02--btreecomparisonsanitizer-with-comment-hoist-and-layout-truncation)
- [ ] **TASK-C-03** BTree sanitization determinism property test [details](./TASK-DETAILS.md#task-c-03--btree-sanitization-determinism-property-test)
- [ ] **TASK-C-04** Self-comparison round-trip integration test (BTree) [details](./TASK-DETAILS.md#task-c-04--self-comparison-round-trip-integration-test)

---

## Slice C-2 — HSM and Blackboard sanitizers

**Goal:** Cover the two remaining C#-emitted asset kinds, reusing the framework from C-1.

- [ ] **TASK-C-05** `HsmComparisonSanitizer` with comment hoist + layout truncation [details](./TASK-DETAILS.md#task-c-05--hsmcomparisonsanitizer-with-comment-hoist-and-layout-truncation)
- [ ] **TASK-C-06** `BlackboardComparisonSanitizer` (inline + heavy concatenation) [details](./TASK-DETAILS.md#task-c-06--blackboardcomparisonsanitizer-inline--heavy-concatenation)
- [ ] **TASK-C-07** HSM + Blackboard determinism + self-comparison tests [details](./TASK-DETAILS.md#task-c-07--hsm--blackboard-sanitizer-round-trip-and-determinism-tests)

---

## Slice C-3 — Blueprint sanitizer

**Goal:** Cover the JSON-based asset kind plus the optional no-op migration / meta-envelope dependencies.

- [ ] **TASK-C-08** No-op `IComparisonMigrationAdapter` + `IMetaEnvelopeSanitizer` [details](./TASK-DETAILS.md#task-c-08--no-op-icomparisonmigrationadapter-and-imetaenvelopesanitizer-implementations)
- [ ] **TASK-C-09** `BlueprintComparisonSanitizer` (DOM walk, strip, sort, re-serialize) [details](./TASK-DETAILS.md#task-c-09--blueprintcomparisonsanitizer-json-dom-walk-strip-editormetadata-sort-re-serialize)

---

## Slice C-4 — Export workflow

**Goal:** End-to-end from "Compare with…" toolbar action to a ready-to-paste `.txt` export.

- [ ] **TASK-C-10** `AssetSelectionDialog` UI [details](./TASK-DETAILS.md#task-c-10--assetselectiondialog-ui)
- [ ] **TASK-C-11** Companion-file discovery (single-file + folder modes; dot-prefix exclusion) [details](./TASK-DETAILS.md#task-c-11--companion-file-discovery-single-file--folder-modes)
- [ ] **TASK-C-12** Asset-kind and AssetId validation at selection [details](./TASK-DETAILS.md#task-c-12--asset-kind-and-assetid-validation-at-selection)
- [ ] **TASK-C-13** Export delivery modal (Save / Copy / preview) [details](./TASK-DETAILS.md#task-c-13--export-delivery-modal-save--copy--preview)
- [ ] **TASK-C-14** `ComparisonExportBuilder` integration [details](./TASK-DETAILS.md#task-c-14--comparisonexportbuilder-integration-instruction-block--metadata--content)
- [ ] **TASK-C-15** "Compare with…" toolbar action wired in all four editors [details](./TASK-DETAILS.md#task-c-15--compare-with-toolbar-action-wired-in-all-four-editors)

---

## Slice C-5 — Response parsing

**Goal:** Turn a pasted LLM response into a structured session.

- [ ] **TASK-C-16** `LlmResponseParser` with robustness rules [details](./TASK-DETAILS.md#task-c-16--llmresponseparser-with-robustness-rules)
- [ ] **TASK-C-17** `ComparisonSessionState` model [details](./TASK-DETAILS.md#task-c-17--comparisonsessionstate-model)
- [ ] **TASK-C-18** "Paste LLM Response…" UI [details](./TASK-DETAILS.md#task-c-18--paste-llm-response-ui)
- [ ] **TASK-C-19** Response/asset mismatch detection [details](./TASK-DETAILS.md#task-c-19--responseasset-mismatch-detection)
- [ ] **TASK-C-20** LLM response parsing fixture suite [details](./TASK-DETAILS.md#task-c-20--llm-response-parsing-fixture-suite)

---

## Slice C-6 — Visualization

**Goal:** Surface the parsed response as canvas annotations, summary panel, and sidebar.

- [ ] **TASK-C-21** `ComparisonAnnotationRenderer` (custom canvas renderer) [details](./TASK-DETAILS.md#task-c-21--comparisonannotationrenderer-custom-canvas-renderer)
- [ ] **TASK-C-22** Severity → color + kind → badge mapping [details](./TASK-DETAILS.md#task-c-22--severity--color-and-kind--badge-mapping)
- [ ] **TASK-C-23** `ComparisonSummaryPanel` docked window [details](./TASK-DETAILS.md#task-c-23--comparisonsummarypanel-docked-window)
- [ ] **TASK-C-24** `ComparisonSidebar` docked window with click-to-focus [details](./TASK-DETAILS.md#task-c-24--comparisonsidebar-docked-window-with-click-to-focus)
- [ ] **TASK-C-25** Variable-binding badges (`↻`) on nodes affected by `variable_renamed` [details](./TASK-DETAILS.md#task-c-25--variable-binding-badges--on-nodes-affected-by-variable_renamed)
- [ ] **TASK-C-26** Blackboard Variables panel integration [details](./TASK-DETAILS.md#task-c-26--blackboard-variables-panel-integration)
- [ ] **TASK-C-27** Exit Comparison Mode toolbar action [details](./TASK-DETAILS.md#task-c-27--exit-comparison-mode-toolbar-action)
- [ ] **TASK-C-28** "Stale comparison" badge when asset saved during comparison [details](./TASK-DETAILS.md#task-c-28--stale-comparison-badge-when-asset-is-saved-while-comparison-is-active)

---

## Slice C-7 — Polish and robustness

**Goal:** Production-ready edges: large-export handling, preview, reverse-direction, comprehensive fixtures, error messages, user docs.

- [ ] **TASK-C-29** 8 MB clipboard threshold check [details](./TASK-DETAILS.md#task-c-29--8mb-clipboard-threshold-check)
- [ ] **TASK-C-30** Export modal preview polish (first 30 lines + "Show full") [details](./TASK-DETAILS.md#task-c-30--export-modal-preview-polish-first-30-lines--show-full)
- [ ] **TASK-C-31** Reverse A↔B button [details](./TASK-DETAILS.md#task-c-31--reverse-ab-button)
- [ ] **TASK-C-32** Comprehensive sanitization fixture corpus [details](./TASK-DETAILS.md#task-c-32--comprehensive-sanitization-fixture-corpus)
- [ ] **TASK-C-33** Error handling polish [details](./TASK-DETAILS.md#task-c-33--error-handling-polish)
- [ ] **TASK-C-34** User-facing documentation [details](./TASK-DETAILS.md#task-c-34--user-facing-documentation)

---

## Slice C-8 — Optional ghost rendering (DEFERRED)

**Goal:** Phase 1.5 polish only — implement if user feedback after Phase 1 indicates removed-node visibility is needed. See §6.5 of the design.

- [ ] **TASK-C-35** Read sanitized Version A to enumerate removed nodes (deferred)
- [ ] **TASK-C-36** Render ghost nodes at approximate positions (deferred)
- [ ] **TASK-C-37** Ghost click handling routing to sidebar entry (deferred)
