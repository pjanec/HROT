# IG-BATCH-03: Resolved Styles and Component Properties

**Batch Number:** IG-BATCH-03  
**Tasks:** IG.2.1, IG.2.2  
**Phase:** IG2 (Basic Rendering)  
**Estimated Effort:** ~10 hours (1.25 days)  
**Priority:** HIGH  
**Dependencies:** IG-BATCH-02 completed

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to the third batch. We are building the foundational visual components for mapping entities precisely onto the canvas. You will create the `ResolvedStyle` structure caching rendering state and inject a `StyleResolutionSystem` evaluating network structures vs TKB layers vs UI overwrites.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Tracker:** `docs/design/TASK-TRACKER.md` - Subphase IG2 validation.
3. **Task Definitions:** `docs/design/TASK-DETAILS-IG.md` - See IG.2.1, IG.2.2 details.
4. **Previous Review:** `.dev-workstream/reviews/IG-BATCH-02-REVIEW.md` 
5. **Code Standards:** `.dev-workstream/guides/CODE-STANDARDS.md` - MANDATORY READING (Cache safety, ECS mutations).

### Source Code Location
- **Primary Work Area:** `Hrot.IG/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/IG-BATCH-03-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/IG-BATCH-03-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## Context

Our network environment natively pulls DDS structures successfully (batch 2). We must evaluate the `EntityMaster` / UI components to construct a unified styling representation mapping colors, damage UI bounds, text, and affiliations natively so our subsequent visual adapter can immediately push render loops onto Raylib without computing logic heavily on the render thread.

**Related Tasks:**
- [IG.2.1] Implement ResolvedStyle Component 
- [IG.2.2] Implement StyleResolutionSystem 

---

## 🎯 Batch Objectives
- Create the ECS component caching style metrics per entity.
- Inject an ECS simulation step to merge base identifiers with live DDS override mapping.

---

## ✅ Tasks

### Task 1: IG.2.1 Implement ResolvedStyle Component

**File:** `Hrot.IG/Components/ResolvedStyle.cs`  
**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.2.1)

**Description:** Establish the runtime visual state cached per entity.
**Requirements:**
- Implement the unmanaged `ResolvedStyle` component inside `Hrot.IG.Components` capturing string references, Tint mapping (RGBA), Display Name labels, and specific flags outlined.
- **Standards Note:** Make sure your component adheres natively to strict unmanaged structs rules utilizing ECS configurations (i.e. avoid unbounded reference types. use flat layouts / fixed buffers if needed for string labels/names).

**Tests Required:**
- ✅ Validated unit tests confirming struct size footprint fits cache safety limits natively (< 64 bytes).
- ✅ Validation mapping defaults precisely to neutral.

---

### Task 2: IG.2.2 Implement StyleResolutionSystem

**File:** `Hrot.IG/Systems/StyleResolutionSystem.cs`  
**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.2.2)

**Description:** Execute an ECS evaluation tier updating `ResolvedStyle`.
**Requirements:**
- Implement `StyleResolutionSystem` inside `SystemPhase.Simulation` tracking `EntityMasterComponent` alongside `SimTransform`.
- Build the 3-layer merge logic natively analyzing mappings across TKB presets, active native overrides via `MapEntitySymbol`, and final native overrides based on UI rulesets.
- Extract affiliation rules mapping hostile components down to exact Tint outputs. (Friend = Blue, Hostile = Red, Neutral = Green, Unknown = White).

**Tests Required:**
- ✅ Unit testing ensuring default TKB parameters build successfully across ECS contexts.
- ✅ Unit testing for logic paths simulating Network overloads (mock `MapEntitySymbol` overrides Tint outputs completely).
- ✅ Validate damage outputs scale linearly.
- ✅ *Coverage Check:* Ensure test files mock dependencies securely without bleeding network limits.

---

## 🧪 Testing Requirements

**❗ TEST QUALITY EXPECTATIONS**
- Do NOT test simple "system executes loop without throwing" metrics.
- Please test `MapUserConfig` overlaps against regular outputs checking expected state overrides strictly against your simulation state outputs.
- Test missing references.

---

## 📊 Report Requirements

**Focus on Developer Insights, Not Understanding Checks**

Please capture your valuable insights in your report:

## Developer Insights

**Q1:** What issues did you face resolving structs around `Hrot.IG.Components.ResolvedStyle` adhering cleanly to the < 64 byte constraint specified organically?

**Q2:** Did you locate any performance constraints blending overlapping entity symbols resolving strings or IDs dynamically inside the Simulation loop context? 

**Q3:** What design decisions did you apply building your unit tests without native TKB configurations initialized?

**Q4:** What edge case constraints required addressing simulating the damage bounds mappings natively?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Task IG.2.1 completed (Valid caching struct, sized correctly).
- [ ] Task IG.2.2 completed (Resolution handles overlap cleanly simulating exact specs without breaking ECS limits).
- [ ] System hooks into Network overrides safely executing mid-simulation natively.
- [ ] All code conforms to `CODE-STANDARDS.md`.
- [ ] Developer Report submitted.

---

## 📚 Reference Materials
- **Task Defs:** `docs/design/TASK-DETAILS-IG.md`
- **Standards:** `.dev-workstream/guides/CODE-STANDARDS.md`
- **Tests Quality Validation:** `.dev-workstream/guides/DEV-LEAD-GUIDE.md`
