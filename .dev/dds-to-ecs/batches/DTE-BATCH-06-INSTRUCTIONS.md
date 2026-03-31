# DTE-BATCH-06: IG Dead Reckoning + Time Sync

**Batch Number:** DTE-BATCH-06  
**Tasks:** DDS2ECS-S10T1, DDS2ECS-S10T2, DDS2ECS-S10T3, DDS2ECS-S10T4, DDS2ECS-S11T1, DDS2ECS-S11T2, DDS2ECS-S11T3  
**Phase:** Phase 10 + Phase 11  
**Estimated Effort:** 14�18 hours  
**Priority:** HIGH  
**Dependencies:** DTE-BATCH-05 approved

---

## ?? Onboarding & Workflow

### Developer Instructions
This batch implements IG dead-reckoning and time synchronization so ghost entities move smoothly and clocks are consistent across nodes.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md` (DDS2ECS-S10T1 ? DDS2ECS-S11T3)
3. **Design Document:** `docs/dds-to-ecs/DESIGN.md` (�6.3, �6.4)
4. **Previous Review:** `.dev-workstream/reviews/DTE-BATCH-05-REVIEW.md`

### Source Code Location
- **Primary Work Area:** `Hrot.IG/`, `Hrot.SimHost/`, `Hrot.ClusterRunner/`
- **Test Projects:** `Hrot.IG.Tests/`, `Hrot.SimHost.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/DTE-BATCH-06-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/DTE-BATCH-06-QUESTIONS.md`

---

## ?? MANDATORY WORKFLOW: Finish the Batch Completely

**CRITICAL:** Do the work end-to-end without stopping to ask for permission. Implement, write tests, run tests, fix root causes, and repeat until **all tests pass**. No partial handoffs, no asking to run tests, no asking whether to fix obvious failures.

---

## ? Tasks

### Task 1: DDS2ECS-S10T1 � Fix `WorldPosTranslator.Decode`
**File:** `Hrot.IG/Translators/WorldPosTranslator.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s10t1--fix-geospatialtranslatordecode-ig-write-networkposition`

---

### Task 2: DDS2ECS-S10T2 � Create `WorldPosTranslator`
**File:** `Hrot.IG/Translators/WorldPosTranslator.cs` (NEW)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s10t2--create-geospatialdrtranlator-ig`

---

### Task 3: DDS2ECS-S10T3 � Create `DeadReckoningSyncSystem`
**File:** `Hrot.IG/Systems/DeadReckoningSyncSystem.cs` (NEW)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s10t3--create-deadreckoningsyncsystem-ig`

---

### Task 4: DDS2ECS-S10T4 � Register DR translator + system
**File:** `Hrot.IG/IgApplication.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s10t4--igapplication-register-new-dr-translator-and-system`

---

### Task 5: DDS2ECS-S11T1 � Verify `TimePulseDescriptor` DDS topic registration
**File:** FDP toolkit file that defines `TimePulseDescriptor` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s11t1--verify-timepulsedescriptor-dds-topic-registration`

---

### Task 6: DDS2ECS-S11T2 � `IgApplication`: enable `TimePulseTranslator`
**File:** `Hrot.IG/IgApplication.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s11t2--igapplication-enable-timepulsetranslator`

---

### Task 7: DDS2ECS-S11T3 � SimHost time-pulse egress
**Files:** `Hrot.SimHost/SimHostApp.cs`, `Hrot.ClusterRunner/Services/SimHostSubsystem.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s11t3--simhostapp--simhostsubsystem-register-time-pulse-egress`

---

## ?? Testing Requirements
- **Framework:** xUnit only. Do not add MSTest or NUnit tests.
- Run:
  - `dotnet test Hrot.IG.Tests/Hrot.IG.Tests.csproj`
  - `dotnet test Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj`

---

## ?? Quality Standards

**? TEST QUALITY EXPECTATIONS**
- Tests must verify behavior (no string-only checks).
- Include all scenarios specified in `TASK-DETAIL.md` for these tasks.

**? REPORT QUALITY EXPECTATIONS**
- Include test output summary.
- List any design deviations and why they were required.

---

## ?? Report Requirements

## Developer Insights

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Are there any performance concerns or optimization opportunities you noticed?

---

## ?? Success Criteria

This batch is DONE when:
- [ ] DDS2ECS-S10T1�S10T4 complete with xUnit tests
- [ ] DDS2ECS-S11T1�S11T3 complete with xUnit tests
- [ ] All tests pass
- [ ] Report submitted to `.dev-workstream/reports/DTE-BATCH-06-REPORT.md`

---

## ?? Common Pitfalls to Avoid
- Do not leave `TransformSyncSystem` and `DeadReckoningSyncSystem` double-applying motion.
- Do not ignore DDS topic registration for `TimePulseDescriptor`.
- Do not leave tests failing; fix the root cause.

---

## ?? Reference Materials
- **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md`
- **Design:** `docs/dds-to-ecs/DESIGN.md` (�6.3, �6.4)
