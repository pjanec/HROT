# BATCH-18: Phase M11 — UI Registration (Editor Wiring)

**Batch Number:** BATCH-18  
**Phase:** M11 — Corrective: Production wiring  
**Tasks:** WHEN-M11-T1, WHEN-M11-T2, WHEN-M11-T3  
**Estimated Effort:** 12-16 hours  
**Dependencies:** Phases M0–M10 all completed and committed to main  

---

## Overview

Phase M11 wires the three new node kinds (`WhenNode`, `ReadEqsResultNode`,
`SpawnEqsSensorNode`) and their editor surfaces into the **running Blueprint editor** so
designers can actually use them.

This batch covers the **editor-side bootstrap wiring** — registering drawers, palette
entries, and visual attachment providers so the inspector, palette menu, and canvas all
recognize and render the new nodes.

**Critical invariant:** Currently every editor-side class added by this iteration has
**zero inbound callers from production code**. After this batch, each class listed below
must have at least one production bootstrap caller.

---

## Detailed Task Scope

Refer to [TASK-DETAIL.md](../TASK-DETAIL.md) for full success conditions and design
references.

### WHEN-M11-T1 — Register the three drawers with the editor's `DrawerRegistry`

**Scope:**
- Locate the Blueprint editor's bootstrap code path that constructs the `DrawerRegistry`.
- If no central site exists, create one (this is a gap the tests confirm exists).
- Construct three drawers with proper DI dependencies:
  - `WhenNodeDrawer` needs: `IChannelCommandCatalog`, `IEngineEventCatalog`,
    `IEditService`, `IPredicateCompiler`
  - `ReadEqsResultNodeDrawer` and `SpawnEqsSensorNodeDrawer` — check their ctors for DI
    surfaces
- Call:
  ```csharp
  registry.Register(typeof(WhenNode), new WhenNodeDrawer(...));
  registry.Register(typeof(ReadEqsResultNode), new ReadEqsResultNodeDrawer(...));
  registry.Register(typeof(SpawnEqsSensorNode), new SpawnEqsSensorNodeDrawer(...));
  ```

**Investigation hints:**
- Look for `Hrot.Editor.EditorSubsystem` or similar bootstrap entry points.
- Search for existing `registry.Register(typeof(SomeNode), ...)` calls to find the
  pattern.
- The tests in `Hrot.Blueprints.Tests/Inspector/` construct `DrawerRegistry` directly;
  those show the expected DI interface.

**Success indicator:** After implementation, selecting a `WhenNode` in the inspector
renders `WhenNodeDrawer` instead of the generic fallback.

---

### WHEN-M11-T2 — Register `WhenNodePaletteEntries` in the palette host

**Scope:**
- Find the editor's palette / context-menu system (the `+ New Node` right-click on
  canvas).
- Integrate `WhenNodePaletteEntries` into the palette host's startup so the three new
  entries appear:
  - Reactive Guards → When
  - EQS → ReadEqsResult
  - EQS → SpawnEqsSensor

**Design reference:** [When_Reactivity_Iteration_Design_v2_2.md §14.2](../When_Reactivity_Iteration_Design_v2_2.md)
describes palette categories and entry structure.

**Investigation hints:**
- Palette entries are typically registered via a central host or service.
- Look for existing entries (e.g., other reactive guards, other EQS nodes) to find the
  registration pattern.
- The `WhenNodePaletteEntries` class (WHEN-M5-T4) exports a method to enumerate
  entries.

**Success indicator:** Right-click an empty canvas spot in an Instance Blueprint; the
context menu contains the three new entries under their correct categories.

---

### WHEN-M11-T3 — Register the three visual attachment providers with the canvas

**Scope:**
- Locate the canvas's `NodeAttachmentProvider` or `CustomCanvasRenderer` registration
  list.
- Register:
  - `WhenNodeAttachmentProvider` (ConditionSummaryAttachment provider for `WhenNode`)
  - EQS template attachment provider for `SpawnEqsSensorNode`
  - Sensor-name pill provider for `ReadEqsResultNode`
  - `CrossAssetDependencyAttachmentProvider` (cross-asset dependency badges)
  - `WhenFiringPulseRenderer` (Debug-mode runtime firing pulses)

**Constraint:** The `WhenFiringPulseRenderer` must **only run in Debug mode**. Guard its
registration with a debug-flag check.

**Design reference:** [When_Reactivity_Iteration_Design_v2_2.md §9](../When_Reactivity_Iteration_Design_v2_2.md)
— attachment provider contract and Debug-mode renderer requirements.

**Investigation hints:**
- Canvas rendering is typically integrated via a list of providers / renderers in the
  canvas subsystem.
- Search for existing attachment provider registrations to find the wiring pattern.
- Look for other `CustomCanvasRenderer` implementations to see how Debug-mode guards
  are applied.

**Success indicator:** Opening a graph with a `WhenNode`, `SpawnEqsSensorNode`, and a
peer-variable `ValueChanged` `WhenNode` shows the condition pill, EQS template pill, and
cross-asset dependency badge on the respective nodes.

---

## Success Conditions (Integration Tests)

Each task requires an **integration test** validating the production bootstrap:

1. **T1 Integration Test:**
   ```csharp
   Boot editor harness (real instance)
   Select a WhenNode in the inspector
   Assert: rendered drawer is WhenNodeDrawer (not generic fallback)
   Repeat for ReadEqsResultNode and SpawnEqsSensorNode
   ```

2. **T2 Integration Test:**
   ```csharp
   Boot editor (headless)
   Open Instance Blueprint in canvas
   Right-click empty canvas spot
   Assert: context menu contains "Reactive Guards → When", "EQS → ReadEqsResult", 
           "EQS → SpawnEqsSensor"
   ```

3. **T3 Integration Test:**
   ```csharp
   Boot editor with a graph containing:
     - WhenNode (Value Changed mode)
     - SpawnEqsSensorNode
     - Another WhenNode (Condition Met, depends on peer variable)
   Visual validation (manual or snapshot):
     - WhenNode shows condition-summary pill
     - SpawnEqsSensorNode shows EQS template pill
     - ReadEqsResultNode shows sensor name
     - Peer-variable node shows dependency badge
   ```

---

## Code Quality Checklist

Before submitting, verify:

- [ ] No new compilation errors or warnings in `Hrot.Editor` and `Hrot.Blueprints.Editor`
  projects.
- [ ] Integration tests pass with editor booting successfully.
- [ ] Debug-mode check for `WhenFiringPulseRenderer` is in place (prevents release-mode
  overhead).
- [ ] DI dependencies for each drawer are correctly wired.
- [ ] No changes to TASK-DETAIL.md, TASK-TRACKER.md, or DESIGN documents.
- [ ] A brief note in the batch report summarizing which bootstrap sites were added or
  modified.

---

## Deliverables

Upon completion, submit:

1. **BATCH-18-REPORT.md** in `reports/` folder containing:
   - Summary of bootstrap changes (files touched, registration sites added/modified)
   - Copy of integration test output (pass/fail counts)
   - Any blockers or unexpected findings
   - Notes on Debug-mode guard for pulse renderer

2. **Updated solution** with all three registrations live and tests green.

---

## References

- [TASK-DETAIL.md § Phase M11](../TASK-DETAIL.md#phase-m11--corrective-production-wiring)
  — full task specs
- [When_Reactivity_Iteration_Design_v2_2.md § 8, 9, 14](../When_Reactivity_Iteration_Design_v2_2.md)
  — drawer hosting, attachment provider contract, palette structure
- Existing drawer/palette/attachment tests under `Hrot.Blueprints.Tests/Inspector/` and
  `Hrot.Blueprints.Tests/Canvas/` (code patterns)
