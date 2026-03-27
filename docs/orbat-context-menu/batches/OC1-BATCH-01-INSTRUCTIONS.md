# OC1-BATCH-01: Phase 0 Bug Fixes & Shared Contracts

**Batch Number:** OC1-BATCH-01
**Tasks:** OC1-B001, OC1-B002, OC1-B003, OC1-B004, OC1-C001
**Phase:** Phase 0 & Phase 1
**Estimated Effort:** 10-12 hours
**Priority:** HIGH
**Dependencies:** None

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to OC1-BATCH-01. This batch focuses on resolving blocking bugs in the route and shape authoring pipelines, ensuring that the existing workflows function correctly before we build the new ORBAT Context Menu on top of them. Additionally, it introduces the core shared contract for personal route drawing.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Task Definitions:** `docs/orbat-context-menu/OC1-TASK-DETAIL.md` - See task details for OC1-B001 through OC1-B004, and OC1-C001
3. **Design Document:** `docs/orbat-context-menu/OC1-DESIGN.md` - Sections 0.1 to 1.1

### Source Code Location
- **Primary Work Areas:** `Bagira.IG`, `Bagira.IOS`, `Bagira.SimHost`, `Bagira.DDS.DataModel`
- **Test Projects:** `Bagira.IG.Tests`, `Bagira.IOS.Tests`, `Bagira.SimHost.Tests`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/OC1-BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/OC1-BATCH-01-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task 3:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## Context

These bug fixes are priority 0 because they block the verification of the new features. We must fix broken authoring and deletion lifecycle flows first.

**Related Tasks:**
- [OC1-B001](../../docs/orbat-context-menu/OC1-TASK-DETAIL.md#oc1-b001--ios-draw-route-no-entity-created) - IOS Draw Route — no entity created
- [OC1-B002](../../docs/orbat-context-menu/OC1-TASK-DETAIL.md#oc1-b002--no-map-layer-for-routes-in-ios) - No map layer for routes in IOS
- [OC1-B003](../../docs/orbat-context-menu/OC1-TASK-DETAIL.md#oc1-b003--tactical-shape-authoring-shape-position-wrong) - Tactical shape authoring — shape position wrong
- [OC1-B004](../../docs/orbat-context-menu/OC1-TASK-DETAIL.md#oc1-b004--entity-deletion-not-reflected-in-ios-inspector) - Entity deletion not reflected in IOS inspector
- [OC1-C001](../../docs/orbat-context-menu/OC1-TASK-DETAIL.md#oc1-c001--add-cmd_draw_personal_route-to-commandtype) - Add `CMD_DRAW_PERSONAL_ROUTE` to `CommandType`

---

## 🎯 Batch Objectives

Fix critical bugs blocking the ORBAT context menu feature, and establish the new `CMD_DRAW_PERSONAL_ROUTE` command type.

---

## ✅ Tasks

### Task 1: Fix Route Creation Pipeline (OC1-B001)

**Files:** Varies (likely `Bagira.IG/Controllers/MapCommandController.cs` or runner wireup)
**Task Definition:** [OC1-B001](../../docs/orbat-context-menu/OC1-TASK-DETAIL.md#oc1-b001--ios-draw-route-no-entity-created)

**Description:**
Investigate and fix why drawing a route in IOS does not result in a created entity. 

**Requirements:**
- See task definition for detailed investigation steps (Hypothesis A vs B).
- Ensure the fix works in full multi-process mode (`Bagira.Runner -m all`).

**Tests Required:**
- You may need to write or update unit/integration tests confirming the pipeline flow (e.g., Ack received).
- Follow success conditions outlined in the task definition.

### Task 2: Fix Map Layer for Routes in IOS (OC1-B002)

**Files:** `Bagira.Map/Definitions/MapLayerRegistry.cs`, `Bagira.IOS/Panels/ConfigPanel.cs` (or similar)
**Task Definition:** [OC1-B002](../../docs/orbat-context-menu/OC1-TASK-DETAIL.md#oc1-b002--no-map-layer-for-routes-in-ios)

**Description:**
Fix the invisibility of route entities in IOS by either clarifying the existing layer mapping or adding a dedicated `routes` layer.

**Requirements:**
- Must ensure newly authored routes are visible without operator configuration (layer enabled by default).
- Follow success conditions outlined in the task definition.

**Tests Required:**
- ✅ Verify layer default state toggle.

### Task 3: Fix Tactical Shape Position (OC1-B003)

**Files:** `Bagira.IG/Controllers/MapCommandController.cs`, `Bagira.IOS/Renderers/...` (Renderer for tactical shapes)
**Task Definition:** [OC1-B003](../../docs/orbat-context-menu/OC1-TASK-DETAIL.md#oc1-b003--tactical-shape-authoring-shape-position-wrong)

**Description:**
Fix the position offset of drawn area shapes.

**Requirements:**
- Evaluate whether centroid or vertex anchoring is applied consistently.
- Ensure the fix preserves ability to modify the shape's location.

**Tests Required:**
- ✅ Verify correct geodetic positions based on vertex or centroid.

### Task 4: Fix Entity Deletion Not Reflected in IOS Inspector (OC1-B004)

**Files:** `Bagira.IOS/IosLogic.cs` (or `EntityInspectorState.cs`)
**Task Definition:** [OC1-B004](../../docs/orbat-context-menu/OC1-TASK-DETAIL.md#oc1-b004--entity-deletion-not-reflected-in-ios-inspector)

**Description:**
Ensure the IOS entity inspector correctly clears out stale state when an entity is deleted.

**Requirements:**
- Do not clear the inspector when an unrelated entity is deleted.
- Handle edge case of an empty selection gracefully.

**Tests Required:**
- ✅ Inspector clears on selected entity deletion. `Bagira.IOS.Tests/EntityInspectorStateTests.cs` (or similar)
- ✅ Inspector unaffected for other entity.
- ✅ No crash on empty selection.

### Task 5: Add CMD_DRAW_PERSONAL_ROUTE to CommandType (OC1-C001)

**File:** `Bagira.DDS.DataModel/MapMessages.cs`
**Task Definition:** [OC1-C001](../../docs/orbat-context-menu/OC1-TASK-DETAIL.md#oc1-c001--add-cmd_draw_personal_route-to-commandtype)

**Description:**
Add `CMD_DRAW_PERSONAL_ROUTE` to `CommandType` enum.

**Requirements:**
- Append after existing enum values.
- Document the JSON shape: `{ "contextId": "<guid>", "entityId": 12345 }`.

**Tests Required:**
- ✅ Compile only, ensure tests pass.

---

## 🧪 Testing Requirements

- Tests must verify **ACTUAL BEHAVIOR** (e.g., offsets, sizes, component states), not just compilation or presence of instances.
- Include explicit tests for all edge cases mentioned in the task constraints and success conditions.

---

## ⚠️ Quality Standards

**❗ TEST QUALITY EXPECTATIONS**
- **NOT ACCEPTABLE:** Tests that only verify "can I set this value"
- **REQUIRED:** Tests that verify actual behavior and edge cases as per task constraints.

**❗ REPORT QUALITY EXPECTATIONS**
- **REQUIRED:** Document issues encountered and how you resolved them
- **REQUIRED:** Document design decisions YOU made beyond the spec
- **REQUIRED:** Share insights on code quality and improvement opportunities
- **REQUIRED:** Note any edge cases or scenarios discovered during implementation

---

## 📊 Report Requirements

**Focus on Developer Insights, Not Understanding Checks**

Please capture your valuable insights and experience in `.dev-workstream/reports/OC1-BATCH-01-REPORT.md` answering the following:

## Developer Insights

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Are there any performance concerns or optimization opportunities you noticed?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] OC1-B001 completed (DDS topic issue or visibility fixed)
- [ ] OC1-B002 completed (Routes layer correctly enabled)
- [ ] OC1-B003 completed (Shapes drawn correctly at cursor)
- [ ] OC1-B004 completed (Inspector clears on delete)
- [ ] OC1-C001 completed (Enum compiled and added)
- [ ] All tests passing
- [ ] Report submitted answering the 5 Developer Insights questions

---

## ⚠️ Common Pitfalls to Avoid

- Forgetting to properly isolate test state (static variables creeping between runs).
- Leaving the layer keys unaligned between layers setup and config JSON check.
- Omitting doc-comments in shared DDS messages which could cause serialization desync.

---

## 📚 Reference Materials

- **Task Defs:** `docs/orbat-context-menu/OC1-TASK-DETAIL.md`
- **Design:** `docs/orbat-context-menu/OC1-DESIGN.md`
