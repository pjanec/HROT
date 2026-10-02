# BATCH-08: Integration Gate + Demo Fixture (BSA-401 + BSA-402)

**Batch Number:** BATCH-08  
**Tasks:** BSA-401 (E2E scenario round-trip + dynamic swap gate), BSA-402 (demo scenario fixture)  
**Phase:** Phase 4-5 — Editor UI completion + Integration gate  
**Estimated Effort:** 4-6 hours  
**Priority:** CRITICAL  
**Dependencies:** All previous batches (BSA-101 through BSA-205)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Wire the final integration points, write end-to-end tests proving the whole pipeline, and create a committed demo scenario fixture. This batch closes the blueprint-scenario workstream.

### Required Reading (IN ORDER)
1. **Design Document:** `.dev/_DONE/blueprint-scenario/BLUEPRINT-SCENARIO-DESIGN.md` — §4.1 (Persist/Load flow E2E), §7 (dynamic assignment), §12 (editor UI)
2. **Task Details:** `.dev/_DONE/blueprint-scenario/TASK-DETAIL.md` — BSA-401 and BSA-402 sections
3. **Task Tracker:** `.dev/_DONE/blueprint-scenario/TASK-TRACKER.md`

### Source Code Location
- **Integration tests (NEW):** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/BlueprintScenarioIntegrationTests.cs`
- **Demo scenario (NEW):** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Fixtures/BlueprintDemo.scenario` (or similar path)
- **Panel registration fix:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (wire EntityBlueprintsPanel via WindowManager)
- **Entity selection bridge:** Wire the selected map entity into `EntityBlueprintsEditModel`
- **Existing integration test pattern:** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/` — study existing tests

### Report Submission
**When done, submit your report to:**  
`.dev/_DONE/blueprint-scenario/reports/BATCH-08-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW

1. **Task 1:** Fix panel registration + entity selection → verify panel opens with selected entity ✅
2. **Task 2:** Write integration tests (BSA-401) → ALL pass ✅
3. **Task 3:** Create demo scenario fixture (BSA-402) → committed fixture passes ✅

---

## Context

All individual pieces exist. Now:
1. The Entity Blueprints panel needs to actually appear in the editor and work with the selected entity
2. End-to-end integration tests must prove: save→load round-trip, dynamic swap, resilience, backward-compat
3. A committed demo scenario acts as a test fixture and onboarding reference

---

## ✅ Tasks

### Task 1: Fix panel registration + entity selection

**Problem:** `BlueprintWindowRegistrar` is retired (AIE-015). The Entity Blueprints registration in that class is dead code. The panel must be registered through the active window infrastructure.

**Solution approach:** 

A) **Simplest viable path** — wire the panel through `IWindowRegistrar.RegisterToolbarEntry` or by registering directly with `WindowManager` in `EditorSubsystem.RegisterWindows()`. Look at how "Run Blueprint on Selected Entity" button is registered (line ~2070 in `EditorSubsystem.cs`).

B) **Entity selection bridge** — the panel needs the currently selected entity from the IG/map. Check if there's a way to get the selected entity from `ISimulationView` or `IInspectableSession`. The Entity Inspector (BSA-204 renderers) uses `IInspectableSession` to get the entity — but that's during rendering, not window creation.

C) **Alternative**: Instead of a dedicated window, add an "Edit Blueprints..." button to the Entity Inspector that opens the panel. This button would know the entity from the inspector's context.

**Required changes:**
1. Remove the dead registration from `BlueprintWindowRegistrar.cs`
2. In `EditorSubsystem.RegisterWindows()`, register the panel directly with `WindowManager`
3. The panel should resolve the selected entity at render time (not construction time) by accessing the shared entity selection mechanism
4. Add a selection entity accessor — e.g., a static `Entity? SelectedEntity` on the panel or a shared service

**Study:** How does the Entity Inspector get the entity being inspected? Look at `InspectorWindow` in `Hrot/Subsystems/Hrot.IG/` (not the blueprint Inspector — the entity inspector that shows ECS components). Follow how it gets its entity.

**If entity selection isn't easily accessible from the Blueprint editor**, implement a pragmatic alternative: add a button to the Entity Inspector (BSA-204 renderers) that opens the Entity Blueprints panel, passing the entity. The renderer already has the entity via `IInspectableSession`.

---

### Task 2: Integration tests (BSA-401)

**File:** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/BlueprintScenarioIntegrationTests.cs` (NEW)

**Test structure:** Use the existing `EditorHarness` pattern from `Hrot.ClusterRunner.Integration.Tests`. These tests boot a minimal cluster with the CGF subsystem to exercise the full pipeline.

**Tests:**

- **Test 1 — Author → Save → Load → Tick:**
  1. Create entity with 2 Instance blueprints attached (via `BlueprintInstanceService`)
  2. Extract via `BlueprintStateTranslator.Extract()` — assert JSON has `"BlueprintAssignments"` (2 AssetIds) and NO `BlueprintBlackboard*` keys
  3. Create intent, load into a new entity
  4. Run `BlueprintMaterializationSystem` — assert entity has the right tier with 2 slots
  5. Tick `BlueprintTickSystem` — assert both blueprints execute

- **Test 2 — Round-trip stability:**
  1. Load a scenario, save it again
  2. Assert the assignment JSON is byte-identical (stable extract)

- **Test 3 — Dynamic swap:**
  1. Attach blueprint A to entity
  2. Publish `ReplaceInstanceBlueprintEvent(A→B)`, tick ingress system
  3. Assert A detached, B attached, B's `InitDefault` ran

- **Test 4 — Resilience (deleted blueprint):**
  1. Create intent referencing an unregistered AssetId
  2. Run materialization — assert scenario loads successfully, unregistered skipped with log, valid blueprints attach

- **Test 5 — Backward-compat (old scenario):**
  1. Construct a scenario DOM fragment with a `"BlueprintBlackboard1024"` key
  2. Deserialize — assert no exception thrown (black-holed), no `BlueprintBlackboard1024` component on entity

**⚠️ These tests may need `[Fact(Skip = "Requires cluster")]` if the full EditorHarness setup is too heavy for CI. At minimum, write Tests 3-5 as unit-level integration (using EntityRepository + translator + materializer manually, no cluster needed).**

---

### Task 3: Demo scenario fixture (BSA-402)

**File:** Create a small, self-contained `.scenario` file with `BlueprintAssignments`

**Process:**
1. Using the Entity Blueprints panel (once Task 1 is working), assign 1-2 Instance blueprints (e.g., `Count4` or similar simple test blueprint) to an entity
2. Save the scenario
3. Verify the JSON contains `"BlueprintAssignments"` array and NO `"BlueprintBlackboard*"` keys
4. Commit the `.scenario` file

**Test:**
- **Test 6 — Demo fixture loads and ticks:**
  ```csharp
  [Fact]
  public void DemoScenario_Loads_BlueprintsAttachAndTick()
  {
      // Load the committed demo scenario
      // Assert entity has blueprint slots
      // Tick N frames
      // Assert blueprints executed (e.g., counter advanced)
  }
  ```

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Entity Blueprints panel opens in editor (via a button or menu item)
- [ ] Panel shows blueprints for the selected entity
- [ ] Integration tests 1-5 pass (or gracefully skipped with documented reason)
- [ ] Demo scenario fixture committed with valid `BlueprintAssignments` JSON
- [ ] Demo fixture test passes
- [ ] 0 net-new failures
- [ ] Build: 0 errors

---

## ⚠️ Common Pitfalls

1. **Don't spend >2h on panel registration** — if the entity selection bridge is architecturally complex, implement a simpler alternative (button on Entity Inspector) and move on to the integration tests.
2. **Integration tests should be pragmatic** — not all need a full cluster boot. Use `EntityRepository` + manual translator/system execution where sufficient.
3. **Demo scenario must be human-readable** — the JSON should clearly show `BlueprintAssignments` with recognizable AssetId GUIDs.

---

## 📊 Report Requirements

- **Q1:** How did you wire the panel to show with a selected entity?
- **Q2:** Which integration tests needed a full cluster boot and which ran on bare EntityRepository?
- **Q3:** What demo scenario did you create? (which blueprints, entity)
- **Q4:** Suggested commit message.
