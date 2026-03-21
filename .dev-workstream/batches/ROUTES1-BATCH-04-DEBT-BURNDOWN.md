# ROUTES1-BATCH-04: Debt Burndown

**Batch Number:** ROUTES1-BATCH-04-DEBT-BURNDOWN
**Tasks:** Debt Tasks Identified Post-Batch-3
**Phase:** Maintenance / Tech-Debt Cleanup
**Estimated Effort:** ~8 hours
**Priority:** HIGH
**Dependencies:** ROUTES1-BATCH-03

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to `ROUTES1-BATCH-04-DEBT-BURNDOWN`. With the core feature implementation of the unified routing system wrapping up, we are pausing feature progression to zero-out the accumulated technical debt identified during code review of Batch 3. 

This batch is exclusively focused on fixing vulnerabilities, query allocation inefficiencies, and ImGui UX hazards so that we lock in the foundational architecture of `ROUTES1` permanently before proceeding to new epics.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Debt Tracker:** `.dev-workstream/DEBT-TRACKER.md`
3. **Previous Review:** `.dev-workstream/reviews/ROUTES1-BATCH-03-REVIEW.md`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/ROUTES1-BATCH-04-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **CT-1 (Safeguards):** Implement → Write tests → **ALL tests pass** ✅
2. **CT-2 (UX Tweaks):** Implement → Write tests → **ALL tests pass** ✅
3. **CT-3 (Query Caching):** Implement → Write tests → **ALL tests pass** ✅

---

## ✅ Feature Tasks (Debt Cleansing)

### Task 1: Safety Guards (Route Edit Mortality & Rendering)
**Target:** `Bagira.IG/Application/IgApplication.cs` and `Bagira.IG/Systems/RouteRenderLayer.cs`
**Descriptions:**
1. The `RouteEditTool` commit handler currently publishes a modification without validating if the underlying entity actually survived to the end of the editing frame.
   **Action:** Add a `World.IsAlive(routeEntity)` check before invoking the update pipeline within the commit callback.
2. `RouteRenderLayer.Draw` may crash if a DDS sample arrives carrying an empty, explicitly `null` waypoints list.
   **Action:** Modify the count accesses to be safe (`plan.Waypoints?.Count ?? 0`).

*Required Tests:* Simulate entity destruction inside the IG bus logic immediately prior to route commit propagation and ensure updates are silently dropped without crashing. 

### Task 2: ImGui Enhancements (Stale State & Buffer Allocations)
**Target:** `Bagira.IG/UI/WaypointEditorPanel.cs`
**Descriptions:**
1. Focus is not properly yielded on `Right-Click` commit operations natively within ImGui.
   **Action:** Conditionally strip keyboard/focus context if a commit runs during traversal.
2. `_jsonBuffer` forces a string copy of `ExtensionJson` unconditionally. 
   **Action:** Define `_lastWpIndex` to cache and intercept updates unless the active selection indices jump natively minimizing garbage allocation per frame.

*Required Tests:* Formulate rendering configurations measuring the `_jsonBuffer` pointer differences validating structural continuity across unaffected layout draws.

### Task 3: Performance Caching (Query Extractions)
**Target:** `Bagira.SimHost/Systems/Routing/RouteContextSystem.cs` and `Bagira.SimHost/Systems/SimHostTrajectoryLayer.cs`
**Descriptions:**
1. Local ECS query objects like `vehicleQuery` and `routeQuery` are being re-allocated via `.Build()` continuously within `OnUpdate` or `Draw()`.
   **Action:** Register them globally as member variables generated strictly within `OnCreated()` or standard Constructors exactly once for the lifetime of the System/Layer.

*Required Tests:* Verify that no new query identities are built across frames. Logic regressions should easily trigger compilation warnings when variables shift contexts natively.

---

## 🧪 Testing Requirements
- **Quantity minimum:** 7-10 targeted tests resolving the edge cases specified.
- **Style:** Precision integration simulating precisely defined edge behaviors across explicit memory boundaries. 

---

## 📊 Report Requirements
1. **Safety Assertions:** What behaviour did you note observing when the editor commit triggered aggressively over deleted elements natively?
2. **Render Allocations:** What memory layout differences occurred post ImGui `_lastWpIndex` caching checks over extended profiling? 

---

## 🎯 Success Criteria
- [ ] Task 1 (Safety Checks) Completed.
- [ ] Task 2 (UI Checks) Completed.
- [ ] Task 3 (Query Checks) Completed.
- [ ] Tests verify behavior correctly.
