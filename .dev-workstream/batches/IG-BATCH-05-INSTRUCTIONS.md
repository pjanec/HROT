# IG-BATCH-05: Interaction Tools & Selections

**Batch Number:** IG-BATCH-05  
**Tasks:** IG.3.1, IG.3.2, IG.3.3, IG.3.4, IG.3.5  
**Phase:** IG3 (Interaction Tools)  
**Estimated Effort:** ~16 hours (2 days)  
**Priority:** HIGH  
**Dependencies:** IG-BATCH-04 completed (Phase IG2 finished)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to the fifth batch. You will now give the operator the ability to interact with the map canvas. This involves building out a suite of tools that plug into the FDP input system, enabling entity selection, measurement, and importantly: pushing new network entity spawn commands back out to the SimHost via the `CreationTool`.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Tracker:** `docs/design/TASK-TRACKER.md` - Subphase IG3.
3. **Task Definitions:** `docs/design/TASK-DETAILS-IG.md` - See IG.3.x section.
4. **Previous Review:** `.dev-workstream/reviews/IG-BATCH-04-REVIEW.md` 
5. **Code Standards:** `.dev-workstream/guides/CODE-STANDARDS.md` - Pay close attention to mathematical constraints and ECS logic vs Input events.

### Source Code Location
- **Primary Work Area:** `Bagira.IG/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/IG-BATCH-05-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/IG-BATCH-05-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement Selection (IG.3.1, IG.3.2) → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement Creation (IG.3.3) → Write tests → **ALL tests pass** ✅  
3. **Task 3:** Implement Measure (IG.3.4) → Write tests → **ALL tests pass** ✅
4. **Task 4:** Integration Test (IG.3.5) → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until tests are passing.

---

## Context

Operators need multiple specific modes (Tools) to operate the canvas. We will implement `StandardInteractionTool` for basic hover/select mechanics, `CreationTool` which delegates DDS `SpawnEntityCommand` onto the event bus, and `MeasureTool` solving Cartesian distances between points.

**Related Tasks:**
- [IG.3.1] Integrate StandardInteractionTool
- [IG.3.2] Add Selection Highlighting
- [IG.3.3] Implement CreationTool (Entity Placement)
- [IG.3.4] Implement MeasureTool (Distance)
- [IG.3.5] Integration Test: Create Entity

---

## 🎯 Batch Objectives
- Build out `StandardInteractionTool` picking up objects.
- Feed selection data back to the `SstVisualizerAdapter` (we touched on selection rings in the prior batch; now hook them up).
- Build the `CreationTool` mapping cursor inputs to real DDS commands.
- Build the `MeasureTool` computing distance constraints.

---

## ✅ Tasks

### Task 1: IG.3.1 & IG.3.2 Standard Interaction & Selection

**File:** `Bagira.IG/Tools/StandardInteractionTool.cs`  
**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.3.1, IG.3.2)

**Description:** Provide hover states and active selection logic.
**Requirements:**
- Implement the primary tool utilizing the MapCanvas tool system.
- Hook Raylib mouse inputs translating Screen coordinates to World coordinates.
- Find entities within hit radiuses using standard spatial checking (AABB/Distance logic interacting with `SstVisualizerAdapter.GetHitRadius`).
- Support highlighting `isHovered` and persisting `isSelected` structures.
- Ensure the `SstVisualizerAdapter` uses these flags correctly during its render loop.

**Tests Required:**
- ✅ Coordinate overlap logic confirms entity picking resolves the closest entity correctly based on world bounds.

---

### Task 2: IG.3.3 Implement CreationTool

**File:** `Bagira.IG/Tools/CreationTool.cs`  
**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.3.3)

**Description:** Push map clicks into network entity creation requests.
**Requirements:**
- Tool activates in a "Create Mode".
- On Left-Click, construct a `SpawnEntityCommand` loaded with `SimTransform` mapping to the World coordinate clicked.
- Inject the creation onto the `FdpEventBus` targeted at Node 300 (or the SimHost designated node limits).
- Provide a visual ghosting or feedback mechanism natively.

**Tests Required:**
- ✅ Tool execution tests proving `SpawnEntityCommand` is fired precisely over the event bus loaded with proper geometric coordinates matching simulated mouse inputs.

---

### Task 3: IG.3.4 Implement MeasureTool

**File:** `Bagira.IG/Tools/MeasureTool.cs`  
**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.3.4)

**Description:** Compute distance lines between two operators.
**Requirements:**
- Implement stateful measurement tool (Point A -> Point B).
- Calculate distance correctly using `SimMath` limits.
- Render the line via Raylib between coordinates actively while tracking the second point.

**Tests Required:**
- ✅ Validated unit tests confirming distance bounds map perfectly to mathematical outputs natively without requiring rendering calls.

---

### Task 4: IG.3.5 Integration Test: Create Entity

**File:** `Bagira.IG.Tests/ToolInteractionIntegrationTests.cs`  
**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.3.5)

**Description:** Integration flow for canvas interaction.
**Requirements:**
- Establish test where `CreationTool` drops an object onto the map.
- Trigger `StandardInteractionTool` to confirm the bounds of the newly dropped entity are picked up correctly.
- This ensures ECS registration loops update fast enough for spatial queries.

**Tests Required:**
- ✅ Successful end-to-end execution without memory collisions.

---

## 🧪 Testing Requirements

**❗ TEST QUALITY EXPECTATIONS**
- Same as last batch — bypass Raylib context loops where needed by testing mathematical logic and ECS state representations natively.

---

## 📊 Report Requirements

## Developer Insights

**Q1:** The picking logic for `StandardInteractionTool` requires looping entities; did you implement a spatial hashing approach or a simple linear scan? Why?

**Q2:** When calculating distances in the `MeasureTool`, did you discover any issues regarding geographic mappings vs cartesian local canvas spaces?

**Q3:** What edge cases surfaced when switching between interaction tools (e.g. from Standard to Measure)?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Task IG.3.1 & IG.3.2 completed (Selection logic tracks actively).
- [ ] Task IG.3.3 completed (Canvas creates entity commands correctly).
- [ ] Task IG.3.4 completed (Measurements execute math logic correctly).
- [ ] Task IG.3.5 completed (Integration loop functions seamlessly).
- [ ] Developer Report submitted capturing insights.

---

## 📚 Reference Materials
- **Task Defs:** `docs/design/TASK-DETAILS-IG.md`
- **Fdp Vis Toolkit:** Read `Fdp.Toolkit.Vis2D` for any Interaction Tool baseline interfaces if available.
