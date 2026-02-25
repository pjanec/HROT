# IG-BATCH-07: IG Application UI & Polish

**Batch Number:** IG-BATCH-07  
**Tasks:** IG.5.1, IG.5.2, IG.5.3, IG.5.4  
**Phase:** IG5 (UI & Polish)  
**Estimated Effort:** ~12 hours (1.5 days)  
**Priority:** MEDIUM
**Dependencies:** IG-BATCH-06 completed (Phase IG4 finished)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to the seventh and final batch for the Image Generator (IG Mock) component! 
We've completed the rendering pipelines and interaction tools. Now, you need to provide real-time situational awareness regarding the map state, the system's performance metrics, and debugging controls to help developers interact with the IG while SimHost integration occurs later.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Tracker:** `docs/design/TASK-TRACKER.md` - Subphase IG5.
3. **Task Definitions:** `docs/design/TASK-DETAILS-IG.md` - See IG.5.x section.
4. **Previous Review:** `.dev-workstream/reviews/IG-BATCH-06-REVIEW.md` 

### Source Code Location
- **Primary Work Area:** `Bagira.IG/` (Specifically targeting ImGui panels and System monitoring)

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/IG-BATCH-07-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/IG-BATCH-07-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Debug & Inspector Panels (IG.5.1, IG.5.2) → Write tests → **ALL tests pass** ✅
2. **Task 2:** Mini-IOS Panel (IG.5.3) → Write tests → **ALL tests pass** ✅  
3. **Task 3:** Performance Overlay (IG.5.4) → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until tests are passing.

---

## Context

Phase IG5 centers around application overlays utilizing **ImGui** (via `rlImGui-cs`). You will create debugging toggles checking systems like collision boundaries or Culling logic, an Inspector interrogating selected entities natively, a standalone spawner UI (Mini-IOS), and overlay metrics displaying frames-per-second, rendering entity counts, and ECS bounds. 

**Related Tasks:**
- [IG.5.1] Create Debug Panel
- [IG.5.2] Add Entity Inspector Panel
- [IG.5.3] Add Mini-IOS Panel
- [IG.5.4] Add Performance Metrics Overlay

---

## 🎯 Batch Objectives
- Supply an immediate-mode UI interface wrapping native Raylib/ECS components.
- Establish an Entity Inspector mapping data from `SelectionState`.
- Provide an experimental component-injection tool via a Mini-IOS widget.
- Output FPS and ECS bounds directly onto the screen.

---

## ✅ Tasks

### Task 1: IG.5.1 & IG.5.2 Debug & Inspector Panels

**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.5.1, IG.5.2)

**Requirements:**
- Hook `rlImGui` rendering bounds into `MapCanvas` post-drawing pipelines.
- ImGui handles must display basic toggles modifying system constants (such as switching `MapUserConfig.ForceHostile` on/off which we built in IG2).
- The Inspector panel must identify currently selected entities (`SelectionState`) and read their components natively (rendering their `EntityId`, `SimTransform` coords, and `ResolvedStyle` logic out as text).

**Tests Required:**
- ✅ State modifications applied to local structures register appropriately against system calls.

---

### Task 2: IG.5.3 Mini-IOS Panel

**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.5.3)

**Requirements:**
- Build a generic spawner UI simulating what a full IOS operator console provides.
- Form inputs for TKB mappings (e.g. Type ID, Affiliation, Coord X, Y boundaries).
- Pushing the trigger injects `SpawnEntityCommand` onto the event bus similarly to the `CreationTool`.

**Tests Required:**
- ✅ Form submission accurately maps variable data loops generating and asserting DDS Events.

---

### Task 3: IG.5.4 Performance Metrics Overlay

**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.5.4)

**Requirements:**
- Build a non-obtrusive UI widget sitting on screen displaying:
  1. Raylib standard FPS.
  2. Number of total entities existing currently.
  3. Number of entities actively rendered (based on `CullingState`).

**Tests Required:**
- ✅ Ensure query extraction methods identifying active vs culled entities match existing unit capacities accurately natively.

---

## 🧪 Testing Requirements

**❗ TEST QUALITY EXPECTATIONS**
- Do NOT test `ImGui` draw calls. UI visual states are inherently brittle to automated testing. Instead, separate the logic states driving the panels into classes/structures and test those structures directly for accurate behavior!

---

## 📊 Report Requirements

## Developer Insights

**Q1:** What strategies were required to prevent `rlImGui` inputs from bleeding through to the `MapCanvas` Raylib mouse inputs unintentionally?

**Q2:** When calculating "visible rendered" entities for the Performance overlay, did you encounter any timing mismatch issues against the Culling logic bounding calculations?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Task IG.5.1 & IG.5.2 completed.
- [ ] Task IG.5.3 completed.
- [ ] Task IG.5.4 completed.
- [ ] This marks the total completion of the IG Mock Subsystem.
- [ ] Developer Report submitted capturing insights.

---

## 📚 Reference Materials
- **Task Defs:** `docs/design/TASK-DETAILS-IG.md`
