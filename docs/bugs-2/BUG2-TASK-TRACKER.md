# BUG2 — Task Tracker

**Reference:** See [BUG2-TASK-DETAIL.md](./BUG2-TASK-DETAIL.md) for detailed task descriptions.  
**Design:** See [BUG2-DESIGN.md](./BUG2-DESIGN.md) for architecture context.

---

## Phase 1 — Network Correctness

**Goal:** Stop duplicate ACKs on descriptor update requests, ensure DDS sender identity is
broadcast by all participant types, and tombstone the GeoSpatialDR descriptor on entity deletion.

- [x] **BUG2-N001** Fix Duplicate UpdateEntityDescriptorRequestSystem Registration [details](./BUG2-TASK-DETAIL.md#bug2-n001-fix-duplicate-updateentitydescriptorrequestsystem-registration)
- [x] **BUG2-N002** Add EnableSenderTracking to All DDS Participant Initializations [details](./BUG2-TASK-DETAIL.md#bug2-n002-add-enablesendertracking-to-all-dds-participant-initializations)
- [x] **BUG2-N003** Fix GeoSpatialDR Descriptor Disposal Leak [details](./BUG2-TASK-DETAIL.md#bug2-n003-fix-geospatialdr-descriptor-disposal-leak)

---

## Phase 2 — Mission System

**Goal:** Fix the trigger translation bug that prevents vehicles from moving; expose trigger
editing in the task editor UI; fix unreadable task action buttons; add inline conflict resolution.

- [x] **BUG2-M001** Fix Missing ResolveTrigger Cases [details](./BUG2-TASK-DETAIL.md#bug2-m001-fix-missing-resolvetrigger-cases)
- [x] **BUG2-M002** Add Trigger Selection UI to MissionPanel [details](./BUG2-TASK-DETAIL.md#bug2-m002-add-trigger-selection-ui-to-missionpanel)
- [x] **BUG2-M003** Fix Unreadable Mission Task Action Buttons [details](./BUG2-TASK-DETAIL.md#bug2-m003-fix-unreadable-mission-task-action-buttons)
- [x] **BUG2-M004** Add Inline Version-Conflict Resolution to MissionPanel [details](./BUG2-TASK-DETAIL.md#bug2-m004-add-inline-version-conflict-resolution-to-missionpanel)

---

## Phase 3 — IOS UI Clean-up

**Goal:** Remove dead-weight legacy code from ConfigPanel and fix ORBAT tree indentation.

- [x] **BUG2-U001** Remove Legacy Tool Combo from ConfigPanel [details](./BUG2-TASK-DETAIL.md#bug2-u001-remove-legacy-tool-combo-from-configpanel)
- [x] **BUG2-U002** Fix ORBAT Tree Indentation [details](./BUG2-TASK-DETAIL.md#bug2-u002-fix-orbat-tree-indentation)

---

## Phase 4 — IG Interaction

**Goal:** Enable per-frame drag updates when the operator holds SHIFT for real-time testing.

- [ ] **BUG2-I001** Add Shift-Key Immediate Drag Mode [details](./BUG2-TASK-DETAIL.md#bug2-i001-add-shift-key-immediate-drag-mode)

---

## Phase 5 — Layer Visibility Enforcement

**Goal:** Make all selection and rendering subsystems honour the map layer visibility mask so
hidden entities cannot be selected or shown with selection rings.

- [ ] **BUG2-V001** Enforce Layer Visibility in Selection and Rendering [details](./BUG2-TASK-DETAIL.md#bug2-v001-enforce-layer-visibility-in-selection-and-rendering)

---

## Phase 6 — Tool Cursors

**Goal:** Provide clear visual feedback that the Measure and EntityPicker tools are active and
waiting for user input.

- [ ] **BUG2-T001** Add Crosshair Cursor to MeasureTool [details](./BUG2-TASK-DETAIL.md#bug2-t001-add-crosshair-cursor-to-measuretool)
- [ ] **BUG2-T002** Add Crosshair Cursor to EntityPickerTool [details](./BUG2-TASK-DETAIL.md#bug2-t002-add-crosshair-cursor-to-entitypickertool)

---

## Phase 7 — Entity Deletion

**Goal:** Expose networked entity deletion through all relevant UI entry points (inspector context
menus and IOS map context menu).

- [ ] **BUG2-E001** Add Delete to Inspector Context Menus [details](./BUG2-TASK-DETAIL.md#bug2-e001-add-delete-to-inspector-context-menus)
- [ ] **BUG2-E002** Wire IOS DELETE Context Action to IG-Side ELM Deletion [details](./BUG2-TASK-DETAIL.md#bug2-e002-wire-ios-delete-context-action-to-ig-side-elm-deletion)

---

## Phase 8 — Road Graph

**Goal:** Fix the two independent bugs that prevent the static road network from appearing on the
SimHost visualization.

- [ ] **BUG2-R001** Fix SimHost Road Graph Rendering [details](./BUG2-TASK-DETAIL.md#bug2-r001-fix-simhost-road-graph-rendering)

---

## Phase 9 — Architecture

**Goal:** Replace the DEBT-033 HealthData mirror hack with a clean single-component design rooted
in the Combat.Contracts shared assembly.

- [ ] **BUG2-A001** Consolidate Health into FDP.Toolkit.Combat.Contracts [details](./BUG2-TASK-DETAIL.md#bug2-a001-consolidate-health-into-fdptoolkitcombatcontracts)
