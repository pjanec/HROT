# Blackboard Authoring — Task Tracker

> One line per task. Check the box when done. Each task links to its detailed description in [`TASK-DETAIL.md`](./TASK-DETAIL.md).
> **Design:** [`Blackboard_Authoring_Detailed_Design.md`](./Blackboard_Authoring_Detailed_Design.md) · **Detail:** [`TASK-DETAIL.md`](./TASK-DETAIL.md) · **Debt:** [`DEBT-TRACKER.md`](./DEBT-TRACKER.md)

## Phase 0 — Kernel / attribute prerequisites
- [ ] **TASK-BB-K-01** — `BlackboardManaged` flag on `[BTreeDefinition]` / `[HsmDefinition]` → [details](./TASK-DETAIL.md#task-bb-k-01--blackboardmanaged-flag-on-btreedefinition--hsmdefinition)
- [ ] **TASK-BB-K-02** — `HeavyDtoType` argument on `[BTreeDefinition]` / `[HsmDefinition]` → [details](./TASK-DETAIL.md#task-bb-k-02--heavydtotype-argument-on-btreedefinition--hsmdefinition)
- [ ] **TASK-BB-K-03** — `[BlackboardDtoStruct]` marker attribute → [details](./TASK-DETAIL.md#task-bb-k-03--blackboarddtostruct-marker-attribute)
- [ ] **TASK-BB-K-04** — `[BlackboardReadOnly]` / `[BlackboardReadWrite]` parameter attributes → [details](./TASK-DETAIL.md#task-bb-k-04--blackboardreadonly--blackboardreadwrite-parameter-attributes)

## Phase 1.5a — Action schema and read-only Variables panel
- [ ] **TASK-BB-1a-01** — `IActionSchemaExporter` reflection-based population → [details](./TASK-DETAIL.md#task-bb-1a-01--iactionschemaexporter-with-reflection-based-population)
- [ ] **TASK-BB-1a-02** — Schema rebuild on `IAssetCatalog.Changed` → [details](./TASK-DETAIL.md#task-bb-1a-02--schema-rebuild-on-iassetcatalogchanged)
- [ ] **TASK-BB-1a-03** — `BlackboardAuthoringWindow` shell (read-only mode) → [details](./TASK-DETAIL.md#task-bb-1a-03--blackboardauthoringwindow-shell-read-only-mode)
- [ ] **TASK-BB-1a-04** — `BlackboardSourceTextParser` (verbatim span capture) → [details](./TASK-DETAIL.md#task-bb-1a-04--blackboardsourcetextparser-verbatim-span-capture)
- [ ] **TASK-BB-1a-05** — Per-field classification → [details](./TASK-DETAIL.md#task-bb-1a-05--per-field-classification-editor-managed-vs-read-only-passthrough)
- [ ] **TASK-BB-1a-06** — Picker filtering by action `DtoType` → [details](./TASK-DETAIL.md#task-bb-1a-06--picker-filtering-by-action-dtotype)

## Phase 1.5b — Editor-managed DTO emit + add/remove
- [ ] **TASK-BB-1b-01** — `BlackboardDtoEmitter` (HROT_EDITOR_GENERATED file) → [details](./TASK-DETAIL.md#task-bb-1b-01--blackboarddtoemitter-hrot_editor_generated-file)
- [ ] **TASK-BB-1b-02** — Add Variable + Remove Variable workflows → [details](./TASK-DETAIL.md#task-bb-1b-02--add-variable--remove-variable-workflows)
- [ ] **TASK-BB-1b-03** — Variable rename via the refactor service → [details](./TASK-DETAIL.md#task-bb-1b-03--variable-rename-via-the-refactor-service)
- [ ] **TASK-BB-1b-04** — `BlackboardBinPacker` (inline-only) → [details](./TASK-DETAIL.md#task-bb-1b-04--blackboardbinpacker-inline-only)
- [ ] **TASK-BB-1b-05** — `BlackboardManaged` wiring + `BlackboardVariable` reference kind → [details](./TASK-DETAIL.md#task-bb-1b-05--blackboardmanaged-asset-wiring--blackboardvariable-reference-kind)
- [ ] **TASK-BB-1b-06** — Round-trip determinism property tests (RT-1, RT-2) → [details](./TASK-DETAIL.md#task-bb-1b-06--round-trip-determinism-property-tests-rt-1-rt-2)

## Phase 1.5c — Recursive aggregation + heavy tier
- [ ] **TASK-BB-1c-01** — `IBlackboardAggregator` for BTree → [details](./TASK-DETAIL.md#task-bb-1c-01--iblackboardaggregator-for-btree)
- [ ] **TASK-BB-1c-02** — `IBlackboardAggregator` for HSM → [details](./TASK-DETAIL.md#task-bb-1c-02--iblackboardaggregator-for-hsm)
- [ ] **TASK-BB-1c-03** — Unbound Sub-Tree Requirements panel section → [details](./TASK-DETAIL.md#task-bb-1c-03--unbound-sub-tree-requirements-panel-section)
- [ ] **TASK-BB-1c-04** — Heavy-tier bin-packing + `Blackboard1024` companion emit → [details](./TASK-DETAIL.md#task-bb-1c-04--heavy-tier-bin-packing--blackboard1024-companion-emit)
- [ ] **TASK-BB-1c-05** — Memory budget indicator with tier breakdown → [details](./TASK-DETAIL.md#task-bb-1c-05--memory-budget-indicator-with-tier-breakdown)

## Phase 1.5d — Approach A whole-DTO aliasing
- [ ] **TASK-BB-1d-01** — Drag-onto-variable aliasing UX → [details](./TASK-DETAIL.md#task-bb-1d-01--drag-onto-variable-aliasing-ux)
- [ ] **TASK-BB-1d-02** — Type-match validation on drop → [details](./TASK-DETAIL.md#task-bb-1d-02--type-match-validation-on-drop)
- [ ] **TASK-BB-1d-03** — Orchestrator emit for aliased sub-trees (BTree) → [details](./TASK-DETAIL.md#task-bb-1d-03--orchestrator-emit-for-aliased-sub-trees-btree)
- [ ] **TASK-BB-1d-04** — Orchestrator emit for state-hosted sub-BTrees (HSM) → [details](./TASK-DETAIL.md#task-bb-1d-04--orchestrator-emit-for-state-hosted-sub-btrees-hsm)
- [ ] **TASK-BB-1d-05** — "Aliased by" badge rendering → [details](./TASK-DETAIL.md#task-bb-1d-05--aliased-by-badge-rendering)

## Phase 1.5e — Approach B field-level synchronization
- [ ] **TASK-BB-1e-01** — Inspector Parameter Synchronization sub-panel → [details](./TASK-DETAIL.md#task-bb-1e-01--inspector-parameter-synchronization-sub-panel)
- [ ] **TASK-BB-1e-02** — "Bound to" dropdown with type filtering → [details](./TASK-DETAIL.md#task-bb-1e-02--bound-to-dropdown-with-type-filtering)
- [ ] **TASK-BB-1e-03** — Sync In / Sync Out checkboxes per field → [details](./TASK-DETAIL.md#task-bb-1e-03--sync-in--sync-out-checkboxes-per-field)
- [ ] **TASK-BB-1e-04** — Orchestrator emit with sync copies → [details](./TASK-DETAIL.md#task-bb-1e-04--orchestrator-emit-with-sync-copies)
- [ ] **TASK-BB-1e-05** — Per-Subtree DTO allocation when no aliasing → [details](./TASK-DETAIL.md#task-bb-1e-05--per-subtree-dto-allocation-when-no-aliasing)

## Phase 1.5f — Validation, diagnostics, recovery
- [ ] **TASK-BB-1f-01** — Cross-region blackboard conflict validator → [details](./TASK-DETAIL.md#task-bb-1f-01--cross-region-blackboard-conflict-validator)
- [ ] **TASK-BB-1f-02** — Drop-target validator (refuse unsafe cross-region alias) → [details](./TASK-DETAIL.md#task-bb-1f-02--drop-target-validator-refuse-unsafe-cross-region-alias)
- [ ] **TASK-BB-1f-03** — Unused-variable diagnostic + glyph → [details](./TASK-DETAIL.md#task-bb-1f-03--unused-variable-diagnostic--glyph)
- [ ] **TASK-BB-1f-04** — "Remove unused" toolbar action → [details](./TASK-DETAIL.md#task-bb-1f-04--remove-unused-toolbar-action)
- [ ] **TASK-BB-1f-05** — Suppression metadata persistence → [details](./TASK-DETAIL.md#task-bb-1f-05--suppression-metadata-persistence)
- [ ] **TASK-BB-1f-06** — `[BlackboardReadOnly]` / `[BlackboardReadWrite]` handling → [details](./TASK-DETAIL.md#task-bb-1f-06--blackboardreadonly--blackboardreadwrite-handling)
- [ ] **TASK-BB-1f-07** — Failure-state handling (States A/B/C/D) → [details](./TASK-DETAIL.md#task-bb-1f-07--failure-state-handling-states-abcd)

## Phase 1.5g — Blueprint UX parity (Tier A)
- [ ] **TASK-BB-1g-01** — Extract `VariablesPanelControl` → [details](./TASK-DETAIL.md#task-bb-1g-01--extract-variablespanelcontrol)
- [ ] **TASK-BB-1g-02** — Migrate BTree/HSM panel to `VariablesPanelControl` → [details](./TASK-DETAIL.md#task-bb-1g-02--migrate-btreehsm-panel-to-variablespanelcontrol)
- [ ] **TASK-BB-1g-03** — Migrate Blueprint variable panel to `VariablesPanelControl` → [details](./TASK-DETAIL.md#task-bb-1g-03--migrate-blueprint-variable-panel-to-variablespanelcontrol)
- [ ] **TASK-BB-1g-04** — Blueprint JSON `Comment` field + emit → [details](./TASK-DETAIL.md#task-bb-1g-04--blueprint-json-comment-field--emit)
- [ ] **TASK-BB-1g-05** — Blueprint JSON `VariableOrder` + emit → [details](./TASK-DETAIL.md#task-bb-1g-05--blueprint-json-variableorder--emit)
- [ ] **TASK-BB-1g-06** — Blueprint Params rename → `BlackboardField` refactor → [details](./TASK-DETAIL.md#task-bb-1g-06--blueprint-params-rename--blackboardfield-refactor)

---

**Totals:** 4 (Phase 0) + 6 + 6 + 5 + 5 + 5 + 7 + 6 = **44 tasks**.
