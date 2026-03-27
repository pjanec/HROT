# SIM-BATCH-01: Geographic Module Integration (Phase S3)

**Batch Number:** SIM-BATCH-01  
**Tasks:** TASK-S3.1  
**Phase:** S3  
**Estimated Effort:** 16 hours (2 days)  
**Priority:** HIGH  
**Dependencies:** S2 (CreateEntityRequestHandler, DescriptorMapper)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to SimHost development! In this batch, you will integrate the Geographic module into the SimHost console application, enabling position translation from local Euclidean simulation space (`SimTransform` / `SimVelocity`) to geodetic network space (`GeoSpatial` DDS topic).

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Code Standards:** `.dev-workstream/guides/CODE-STANDARDS.md` - **CRITICAL:** Note §1 (No Magic Numbers), §2 (Coordinate System), and §4 (Zero Allocation).
3. **Design Document:** `docs/design/DESIGN-SIMHOST.md` - Geographic Module integration section.
4. **Task Definitions:** `docs/design/TASK-DETAILS-SIMHOST.md#phase-s3-geographic-module-integration-2-days`
5. **Edge Cases:** `docs/design/EDGE-CASES-AND-MITIGATIONS.md` - Review for networking and position gotchas.
6. **Debt Tracker:** `.dev-workstream/DEBT-TRACKER.md` - Review any ongoing open P2/P3 items.

### Source Code Location
- **Primary Work Area:** `Bagira.SimHost/`
- **Geographic Toolkit:** `FDP/Toolkits/Fdp.Toolkit.Geographic/`
- **Test Project:** `Bagira.SimHost.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/SIM-BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/SIM-BATCH-01-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including prior tests)

---

## Context

Phase S2 established the core DDS ingress for entity creation (`CreateEntityRequest`). The created entities are loaded into the ECS. However, their local simulation representations (`SimTransform`, `SimVelocity`) currently do not get egressed correctly back over DDS as geodetic positions. 

We need to wire the `GeographicModule` which registers `SimTransformBridgeSystem`. This system safely updates the outbound geodetic components, which are then published by the already present `GeoSpatialEgressTranslator`.

*Note: With recent `CarKinem` refactoring, `VehicleState` is strictly reserved for wheeled kinematics parameters (speed, steering). `SimTransform` is the authoritative source for ALL entity rendering, positioning, and orientation.*

---

## 🎯 Batch Objectives
- Connect `GeographicModule` from the FDP core toolkit to the running `Bagira.SimHost` kernel.
- Ensure egress DDS translators publish accurate positional data.
- Validate via smoke test that entities spawned via DDS accurately egress their positions back.

---

## ✅ Tasks

### Task 1: Register GeographicModule and Verify Egress (TASK-S3.1)

**Files:**
- Update `Bagira.SimHost/Program.cs`
- Check `Bagira.SimHost/Modules/SimHostModule.cs`
- Check `Bagira.SimHost/Translators/GeoSpatialEgressTranslator.cs`

**Task Definition:** See [TASK-DETAILS-SIMHOST.md](../../docs/design/TASK-DETAILS-SIMHOST.md#phase-s3-geographic-module-integration-2-days)

**Description:**
Wire the Geographic toolkit into the SimHost node.

**Requirements:**
- Follow the steps exactly as written in `TASK-DETAILS-SIMHOST.md` (Phase S3 Task S3.1).
- **Setup Initialization**: You will need to build the ECS kernel, network modules, and pass the configuration through `Program.cs`. Use a dummy `WGS84Transform` origin parameter (make sure to avoid magic numbers in the code; use named constants).
- **Verify Translator Registration**: `SimHostModule` exposes `GeoEgressTranslator`. Ensure this is linked dynamically via the `CycloneNetworkModule` translator list.
- **DO NOT INLINE BRIDGE CODE**: You must use the toolkit's existing `SimTransformBridgeSystem` located inside `GeographicModule`.

**Design Reference:** 
- `docs/design/TASK-DETAILS-SIMHOST.md#task-s31-register-geographicmodule-and-verify-egress`

**Tests Required:**
- ✅ Integration smoke test simulating `Program.cs` startup.
- ✅ Validation that `GeoSpatialEgressTranslator` correctly converts and publishes `GeoSpatial` from `SimTransform` correctly.
- ✅ *Warning*: Do not test `VehicleState` for positional asserts! Test `SimTransform`.

---

## 🧪 Testing Requirements
- Provide an overarching integration test that initializes `GeographicModule` alongside `SimHostModule`.
- 1-2 integration tests verifying full traversal: from `SimTransform` modification to DDS output queue verification via an active DomainParticipant pub/sub capture.
- **CODE-STANDARDS Reminder**: Assert exact value accuracy (with reasonable tolerances) and layout correctness, do not just check `Assert.NotNull()`.

---

## 📊 Report Requirements

**Focus on Developer Insights, Not Understanding Checks**

- **Q1 Issues Encountered:** What issues did you encounter configuring the ECS Kernel or CycloneNetworkModule initialization in Program.cs? 
- **Q2 Architectural Constraints:** Did the split between `SimTransform` and `VehicleState` complicate the integration step? How?
- **Q3 Improvement Opportunities:** Did you notice any weak points or hard-to-wire points in `GeographicModule` or `SimHostModule`?
- **Q4 Edge Cases:** Were there edge cases for `WGS84Transform` coordinates or invalid data states you stumbled upon?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] TASK-S3.1 is completed.
- [ ] `Program.cs` successfully runs up a dummy kernel with `GeographicModule` and DCS Egress attached.
- [ ] `GeoSpatialEgressTranslator` properly triggers changes without regressions.
- [ ] All tests pass (with detailed verifications!).
- [ ] Report submitted.

---

## ⚠️ Common Pitfalls to Avoid
- Re-writing bridge logic. Use `GeographicModule` as-is.
- Hardcoded coordinates. If configuring test coordinates, make them named constants.
- Mixing `SimMath` coordinates with raw System.Numerics (as highlighted in `CODE-STANDARDS.md`).

---

## 📚 Reference Materials
- **Task Defs:** [TASK-DETAILS-SIMHOST.md](../../docs/design/TASK-DETAILS-SIMHOST.md) - See TASK-S3.1
- **Code Standards:** `.dev-workstream/guides/CODE-STANDARDS.md`
- **Debt:** `.dev-workstream/DEBT-TRACKER.md`
