# IG-BATCH-04: Advanced Base Rendering & Culling

**Batch Number:** IG-BATCH-04  
**Tasks:** IG.2.3, IG.2.4, IG.2.5  
**Phase:** IG2 (Basic Rendering)  
**Estimated Effort:** ~12 hours (1.5 days)  
**Priority:** HIGH  
**Dependencies:** IG-BATCH-03 completed

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to the fourth batch. In the previous batch, we defined the core presentation structures in `ResolvedStyle`. Now we need to act upon them visually with `SstVisualizerAdapter` and optimize canvas calls by introducing a `MapCullingSystem`, culminating in a stress test combining all pieces mapped onto 100 entities securely.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Tracker:** `docs/design/TASK-TRACKER.md` - Subphase IG2 finalization.
3. **Task Definitions:** `docs/design/TASK-DETAILS-IG.md` - See IG.2.3, IG.2.4, IG.2.5 details.
4. **Previous Review:** `.dev-workstream/reviews/IG-BATCH-03-REVIEW.md` 
5. **Code Standards:** `.dev-workstream/guides/CODE-STANDARDS.md` - Remember zero allocation hot paths!

### Source Code Location
- **Primary Work Area:** `Hrot.IG/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/IG-BATCH-04-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/IG-BATCH-04-QUESTIONS.md`

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

We are completing the Basic Rendering phase. Entities can be placed into the domain with `ResolvedStyle` data via translators. `SstVisualizerAdapter` extracts this into our MapCanvas using Raylib. Because standard IG operations host thousands of entities, you must integrate an unmanaged `MapCullingSystem` identifying exactly which entities overlap the active camera bounds, marking invisible bounds out so `SstVisualizerAdapter` ignores them efficiently. Finally, you will prove it all works in the Integration test suite validating the performance loops natively.

**Related Tasks:**
- [IG.2.3] Create SstVisualizerAdapter
- [IG.2.4] Add MapCullingSystem
- [IG.2.5] Integration Test: Render 100 Entities

---

## 🎯 Batch Objectives
- Swap out the core entity circle representations with standard SST visualization primitives responding directly to `ResolvedStyle`.
- Write high-performance unmanaged bounds culling.
- Validate end-to-end entity resolution natively.

---

## ✅ Tasks

### Task 1: IG.2.3 Create SstVisualizerAdapter

**File:** `Hrot.IG/Adapters/SstVisualizerAdapter.cs`  
**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.2.3)

**Description:** Render entity visualization frames using `ResolvedStyle` inputs.
**Requirements:**
- Implement `IVisualizerAdapter` resolving `ResolvedStyle` attributes over the previous basic `StubVisualizerAdapter`.
- Affiliations define colors; Damage modifies transparency/overlay patterns natively.
- Fetch proper textures (symbols) matching UTF8 strings if possible inside Raylib integrations. (If textures are unavailable, draw basic colored geometric proxies natively as long as affiliations mirror correctly.)
- Handle null safety appropriately.

**Tests Required:**
- ✅ Validated unit tests confirming positional coordinates pass correctly to boundaries mapping UI elements properly over Raylib logic wrappers.

---

### Task 2: IG.2.4 Add MapCullingSystem

**File:** `Hrot.IG/Systems/MapCullingSystem.cs`  
**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.2.4)

**Description:** Prevent rendering computations on off-screen entities.
**Requirements:**
- Introduce an unmanaged ECS `MapCullingSystem` inside `SystemPhase.PreRender`.
- Read `MapCamera.WorldBounds` comparing it against every `SimTransform`.
- Tag entities inside boundaries with `InViewCullingTag` and remove it if leaving boundary viewports.
- Optimize this logic for speed (zero allocations, unmanaged structs strictly). Ensure `SstVisualizerAdapter` requires `InViewCullingTag` before performing drawing calculations natively.

**Tests Required:**
- ✅ Coordinate boundary tests confirming entities perfectly inside, intersecting, or entirely outside camera viewports tag accordingly.
- ✅ Removal testing confirming the tag drops properly off entities departing visual bounds dynamically.

---

### Task 3: IG.2.5 Integration Test: Render 100 Entities

**File:** `Hrot.IG.Tests/LayerRenderingIntegrationTests.cs`  
**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.2.5)

**Description:** End-to-end mapping simulation limits check.
**Requirements:**
- Write a pure test constructing 100 mapped entities carrying diverse configurations (TKB/Network symbols matching friend/hostile mapping data).
- Fast-forward the ECS timeline bridging the `StyleResolutionSystem` → `MapCullingSystem` operations explicitly simulating camera coordinate overlays panning.
- Verify `InViewCullingTag` correctly maps to ~50 entities specifically captured within an assumed camera block effectively proving zero ECS crashes.

**Tests Required:**
- ✅ The full test runs cleanly completing Phase IG2 rendering targets natively.

---

## 🧪 Testing Requirements

**❗ TEST QUALITY EXPECTATIONS**
- Culling tests are mathematical bounds tests. Edge tests are mandatory here.
- Your integration tests must reflect correct pipeline progression cleanly verifying the data loop without depending on `Raylib.WindowShouldClose`.

---

## 📊 Report Requirements

## Developer Insights

**Q1:** What hurdles occurred building tests predicting the correct interaction overlap with the native Raylib bounds checking during the visualizer operations?

**Q2:** How much time does the Culling loop realistically take processing 100 entities during the integration test step? Are there immediate refactoring targets needed to hit 10k entities?

**Q3:** During `SstVisualizerAdapter` implementation, did you rely on shapes or actual textures to accommodate the missing raw TKB textures? 

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Task IG.2.3 completed (Visualizer leverages `ResolvedStyle` appropriately simulating logic states).
- [ ] Task IG.2.4 completed (Precise Unmanaged culling boundary tagging prevents render leakage natively).
- [ ] Task IG.2.5 completed (Integration Test securely identifies loop processing).
- [ ] All code conforms to `CODE-STANDARDS.md`.
- [ ] Developer Report submitted.

---

## 📚 Reference Materials
- **Task Defs:** `docs/design/TASK-DETAILS-IG.md`
- **Standards:** `.dev-workstream/guides/CODE-STANDARDS.md`
- **Tests Quality Validation:** `.dev-workstream/guides/DEV-LEAD-GUIDE.md`
