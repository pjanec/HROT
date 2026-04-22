# IG-BATCH-06: Advanced Rendering & Subsystems

**Batch Number:** IG-BATCH-06  
**Tasks:** IG.4.1, IG.4.2, IG.4.3, IG.4.4, IG.4.5  
**Phase:** IG4 (Advanced Features)  
**Estimated Effort:** ~16 hours (2 days)  
**Priority:** MEDIUM
**Dependencies:** IG-BATCH-05 completed (Phase IG3 finished)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to the sixth batch! We are entering Phase IG4. With rendering and interaction tools online, we need the map to support advanced visual contexts, such as showing historical trails, producing kinetic effects (like explosions or flashes based on network data), interacting via context menus, and modifying geometries like route waypoints with a specialized tool.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Tracker:** `docs/design/TASK-TRACKER.md` - Subphase IG4.
3. **Task Definitions:** `docs/design/TASK-DETAILS-IG.md` - See IG.4.x section.
4. **Previous Review:** `.dev-workstream/reviews/IG-BATCH-05-REVIEW.md` 
5. **Code Standards:** `.dev-workstream/guides/CODE-STANDARDS.md`

### Source Code Location
- **Primary Work Area:** `Hrot.IG/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/IG-BATCH-06-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/IG-BATCH-06-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** System Implementations (IG.4.1, IG.4.2) → Write tests → **ALL tests pass** ✅
2. **Task 2:** Context Menus (IG.4.3) → Write tests → **ALL tests pass** ✅  
3. **Task 3:** EditTool implementation (IG.4.4) → Write tests → **ALL tests pass** ✅
4. **Task 4:** Integration Tests (IG.4.5) → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until tests are passing.

---

## Context

Phase IG4 introduces depth to the canvas rendering. You will aggregate positional snapshots into histories (`HistoryRecordingSystem`), map runtime interactions/emissions to visual states (`EventToEffectSystem`), empower right-clicks (`Context Menu System`), and allow users to actively manipulate polygonal components (`EditTool`).

**Related Tasks:**
- [IG.4.1] Implement HistoryRecordingSystem
- [IG.4.2] Implement EventToEffectSystem
- [IG.4.3] Add Context Menu System
- [IG.4.4] Implement EditTool (Vertex Manipulation)
- [IG.4.5] Integration Test: Advanced Features

---

## 🎯 Batch Objectives
- Track where entities have been and render trailing history components natively.
- Catch specific simulation bursts/events and inject effect entities temporarily.
- Give power-user features via Context Menus mapped against standard ECS selections.
- Build the `EditTool` capable of picking apart vertices/multipoints on compatible entities.

---

## ✅ Tasks

### Task 1: IG.4.1 HistoryRecordingSystem

**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.4.1)

**Requirements:**
- Build system gathering `SimTransform` deltas. Store past points (limit bounds to prevent infinite buffer bloat).
- Hook rendering layer to draw connecting poly-lines simulating trailing history vectors based on the `ResolvedStyle` data (respect affiliations).

**Tests Required:**
- ✅ Validated bounds flushing correctly when exceeding track depth limit.

---

### Task 2: IG.4.2 EventToEffectSystem

**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.4.2)

**Requirements:**
- Trap network event signals (i.e., weapon detonations, comms emissions).
- Spawn unmanaged temporary ECS entities (Ephemeral visual effects) tagged with a Decay component.
- Visualizer should read effect structs and draw fading rings or icons natively before they are culled.

**Tests Required:**
- ✅ Event trigger testing spawning effects and validating decay timers successfully.

---

### Task 3: IG.4.3 Context Menu System

**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.4.3)

**Requirements:**
- Bind right-click on the `MapCanvas` executing logic targeting currently hovered items. 
- Use an unmanaged state array exposing specific actions based on the target (e.g. `SimHost` Delete entity commands).
- Handle the visual wrapper inside our Immediate Mode Gui (ImGui later, draw basics natively now).

**Tests Required:**
- ✅ Coordinate overlap resolving to context popup triggers appropriately without locking interactions.

---

### Task 4: IG.4.4 Implement EditTool

**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.4.4)

**Requirements:**
- Specialized Interaction replacing standard pointer tool. Allows dragging vertices of poly-lines (like Routes or Areas).
- Intercept coordinate overrides pushing bounding alterations securely over ECS commands.

**Tests Required:**
- ✅ Tests proving clicking and dragging node-indexes correctly update underlying `SimTransform` or equivalent components without bleeding interactions.

---

### Task 5: IG.4.5 Integration Test: Advanced Features

**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.4.5)

**Requirements:**
- End-to-end integration constructing an entity with trailing history emitting an event, pausing, selecting it with Context Menus, and using the EditTool seamlessly.

**Tests Required:**
- ✅ Flawless integration execution passing natively.

---

## 🧪 Testing Requirements

**❗ TEST QUALITY EXPECTATIONS**
- Same boundaries apply regarding Raylib dependencies. Build logic cleanly against the `EntityRepository` loops testing states, geometry positions and component existences directly natively.

---

## 📊 Report Requirements

## Developer Insights

**Q1:** What limitations arose capturing history intervals? Did memory layout restrictions clash with managing dynamically sized buffers inside fixed ECS structs? 

**Q2:** Explain the lifecycle mapping strategies used to dispose of effects automatically. Were custom lifecycles preferred over simple timed death ticks?

**Q3:** The `EditTool` modifies bounds. How complex was isolating multi-point logic inside flat struct layers targeting Raylib inputs?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Task IG.4.1 completed.
- [ ] Task IG.4.2 completed.
- [ ] Task IG.4.3 completed.
- [ ] Task IG.4.4 completed.
- [ ] Task IG.4.5 completed.
- [ ] Developer Report submitted capturing insights.

---

## 📚 Reference Materials
- **Task Defs:** `docs/design/TASK-DETAILS-IG.md`
