# INTS-BATCH-04: Resolving End-to-End Validation

**Batch Number:** INTS-BATCH-04  
**Tasks:** CORRECTIVE-0 (Rewrite INTS-P3-014)  
**Phase:** Phase 3 - Debug Instrumentation & End-to-End Validation  
**Estimated Effort:** ~2 hours  
**Priority:** CRITICAL  
**Dependencies:** INTS-BATCH-03 must be completed and merged.

---

## 📋 Onboarding & Workflow

### Developer Instructions
In the previous batch, you failed to implement a true End-to-End (E2E) test for INTS-P3-014. Instead of setting up two headless instances communicating over real DDS on an isolated domain, you wrote an `EntityLifecycleIntegrationTests.cs` that mocks the network boundary by copying ECS components between local `EntityRepository` instances. 

This violates the primary goal of Phase 3 tracing and integration validation. Quick and dirty solutions that mock system boundaries do not fulfill integration testing requirements.

**⚠️ CRITICAL DIRECTIVE: NO MORE FAKE TESTS ⚠️**
In this batch, you will throw away the component-copying mock test and write a real, cross-process (or multi-run-loop within process) DDS integration test using the fully composed runtime hosts.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Task Definitions:** `docs\design\TASK-DETAILS-Integration-Troubleshooting.md` - See detailed task specifications
3. **Previous Review:** `.dev-workstream/reviews/INTS-BATCH-03-REVIEW.md` - Read why your previous attempt was rejected.

### Source Code Location
- **Primary Work Areas:** 
  - `Bagira.SimHost.Integration.Tests` / `Bagira.Integration.Tests`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/INTS-BATCH-04-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Task 0:** Implement → Write test → **Test must pass on Domain 10 without failing.** ✅

---

## ✅ Tasks

### Task 0: Corrective Action - Real E2E DDS Integration Test (CORRECTIVE-0)
**Files:** `Bagira.SimHost.Integration.Tests/EntityLifecycleIntegrationTests.cs`
**Description:** 
Rewrite `INTS-P3-014`. The integration test must:
1. Initialize a `SimHostApp` instance (headless) configured to use DDS domain 10.
2. Initialize an `IgApplication` instance (headless) configured to use DDS domain 10.
3. Establish an asynchronous or polled mechanism to handle both application update loops simultaneously for a short duration (e.g., 60-120 ticks).
4. Send a `CreateEntityRequest` (or `SpawnEntityCommand` on the SimHost bus) into the SimHost logic.
5. Tick both systems until SimHost generates the entity.
6. Tick both systems until DDS pushes the data over loopback.
7. Tick both systems until IG ingest translates the incoming DDS messages into an IG-world entity.
8. Validate via assertion that the IG repository has the `ResolvedStyle` component present for that new network ID.

Your implementation must not bypass DDS via memory sharing or component copying.

---

## 🧪 Testing and Technical Requirements

**Guidelines Override:**
- **xUnit Framework:** All tests MUST use xUnit.
- **Domain Isolation:** You must run the `DdsParticipant` for both the IG app and the SimHost app on `Domain Id: 10` so that local desktop CycloneDDS traffic (Domain 0) does not interfere with validations.

---

## 📊 Report Requirements

Provide a copy of this layout in your `.dev-workstream/reports/INTS-BATCH-04-REPORT.md` report, filling in details:

**Developer Insights**

**Q1:** What complexities did you discover when running two full Application instances in the same process bounds communicating over DDS?

**Q2:** How large was the latency between SimHost processing the spawn and IG fully resolving the style component? How many ticks did you determine to be safe for synchronization?

**Q3:** Does this test cleanly teardown and dispose both apps and the CycloneDDS participants correctly such that it can run repeatedly without error?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Task 0: The unit test spawns two real `App` instances talking over Domain 10.
- [ ] Task 0: The unit test successfully queries IG's ECS world to prove successful network transaction.
- [ ] Test boundaries and setups are respected. 
- [ ] Report submitted addressing developer feedback explicitly.
