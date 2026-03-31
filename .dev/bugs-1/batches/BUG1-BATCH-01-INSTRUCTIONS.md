# BUG1-BATCH-01: Infrastructure and Network Correctness

**Batch Number:** BUG1-BATCH-01  
**Tasks:** BUG1-F001, BUG1-F002, BUG1-F003, BUG1-N001, BUG1-N002  
**Phase:** Phase 1 (Infrastructure & Configuration) & Phase 2 (Network Correctness)  
**Estimated Effort:** ~10-12 hours  
**Priority:** HIGH  
**Dependencies:** None

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to BUG1-BATCH-01. This batch focuses on foundational infrastructure fixes (DDS domains, CLI arguments, scripts) and network correctness (silent bystander rule, descriptor disposal).

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Task Definitions:** `docs/bugs-1/TASK-DETAIL.md` - See BUG1-* specifics
3. **Design Document:** `docs/bugs-1/DESIGN.md` - Architectural context
4. [No previous reviews yet, this is the first batch in the BUG1 stream]

### Source Code Location
- **Primary Work Area:** `Hrot.ClusterRunner/`, `FDP/Framework/FDP.Framework.Runner/`, `Hrot.Map.Common/`, `FDP/ModuleHost/ModuleHost.Network.Cyclone/`
- **Batch Scripts:** `run_all_standalone.bat`, `run_SimHost.bat`, `run_IG.bat`, `run_IOS.bat`
- **Test Project:** Respective `.Tests` packages in these subsystems

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/BUG1-BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/BUG1-BATCH-01-QUESTIONS.md`

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

**Why:** Ensures each component is solid before building on top of it. Prevents cascading failures.

---

## Context

This batch serves as the first set of fixes under the BUG1 effort. We need to ensure basic configurations like node-id and DDS domains are correctly parsed and passed to the simulation host, batch scripts have the right working directory, and the network correctly follows the silent bystander rule and gracefully cleans up resources. 

**Related Tasks:**
- [BUG1-F001](docs/bugs-1/TASK-DETAIL.md#bug1-f001-fix-simhost-dds-domain-zero-guard) - Fix SimHost DDS Domain Zero Guard
- [BUG1-F002](docs/bugs-1/TASK-DETAIL.md#bug1-f002-add---node-id-cli-option-to-runner) - Add `--node-id` CLI Option to Runner
- [BUG1-F003](docs/bugs-1/TASK-DETAIL.md#bug1-f003-fix-batch-script-working-directory) - Fix Batch Script Working Directory
- [BUG1-N001](docs/bugs-1/TASK-DETAIL.md#bug1-n001-enforce-silent-bystander-rule-in-updateentitydescriptorrequestsystem) - Enforce Silent Bystander Rule
- [BUG1-N002](docs/bugs-1/TASK-DETAIL.md#bug1-n002-fan-out-entity-descriptor-disposal) - Fan-Out Entity Descriptor Disposal

---

## 🎯 Batch Objectives
Ensure reliable multi-node and multi-domain launch capabilities, and ensure network entities cleanly adhere to authoritative restrictions and life-cycle cleanups.

---

## ✅ Tasks

### Task 1: Fix SimHost DDS Domain Zero Guard (BUG1-F001)

**File:** `Hrot.ClusterRunner/Services/SimHostSubsystem.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md §BUG1-F001](docs/bugs-1/TASK-DETAIL.md#bug1-f001-fix-simhost-dds-domain-zero-guard)

**Description:**
Replace the `> 0` guard in `SimHostSubsystem.Initialize()` with a direct pass-through for `config.DomainId`.

**Requirements:**
- Pass `config.DomainId` down to `SimHostApp` (use nullable cast if needed).
- Must preserve existing behaviour when `--node-id` is not supplied.
- Do not change `SimHostApp` constructor signature unnecessarily.

**Design Reference:** [BUG1 DESIGN.md - §1.1](docs/bugs-1/DESIGN.md#11-fix-simhost-dds-domain-zero-guard)

**Tests Required:**
- ✅ Verify happy path (domain 0 accepted).
- ✅ Verify non-zero domain preserved.
- ✅ Ensure regressions in `Hrot.SimHost.Tests` do not occur.

---

### Task 2: Add `--node-id` CLI Option to Runner (BUG1-F002)

**File:** `FDP/Framework/FDP.Framework.Runner/RunnerConfiguration.cs` and others (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md §BUG1-F002](docs/bugs-1/TASK-DETAIL.md#bug1-f002-add---node-id-cli-option-to-runner)

**Description:**
Plumb a `--node-id` (alias `-n`) CLI option down to subsystems and Network init, enabling deterministic node offsetting per-subsystem.

**Requirements:**
- Add `NodeId` to `RunnerConfiguration`, `RunnerOptions`, `SubsystemConfig`.
- Resolve node-id dynamically per-subsystem (SimHost: `+0`, IG: `+100`, IOS: `+200`). 
- If `--node-id` is not supplied (0), fall back to legacy constants like `SimHostNetworkConstants.LocalNodeId`.

**Design Reference:** [BUG1 DESIGN.md - §1.2](docs/bugs-1/DESIGN.md#12-add---node-id-cli-option-to-runner)

**Tests Required:**
- ✅ Verify legacy fallback when no flag supplied.
- ✅ Verify explicit `--node-id` applied.
- ✅ Verify deterministic offset behaviour for IG subsystem (`+100`).
- ✅ Verify `-n` short alias behaviour.

---

### Task 3: Fix Batch Script Working Directory (BUG1-F003)

**File:** `run_all_standalone.bat`, `run_SimHost.bat`, `run_IG.bat`, `run_IOS.bat` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md §BUG1-F003](docs/bugs-1/TASK-DETAIL.md#bug1-f003-fix-batch-script-working-directory)

**Description:**
Fix working directory issues to allow assets to be found regardless of where the script is executed.

**Requirements:**
- Add `cd /d %~dp0` logic targeting the appropriate inner directory so the script computes its own directory.
- Use explicit executable name for `RUNNER`.
- Keep the domain flag `-d %DOMAIN%`.

**Design Reference:** [BUG1 DESIGN.md - §1.3](docs/bugs-1/DESIGN.md#13-fix-batch-script-working-directory)

**Tests Required:**
- ✅ Inspect `cd` usage is robust.
- ✅ Explicit `-d %DOMAIN%` passed to the executable via `start`.

---

### Task 4: Enforce Silent Bystander Rule (BUG1-N001)

**File:** `Hrot.Map.Common/Systems/UpdateEntityDescriptorRequestSystem.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md §BUG1-N001](docs/bugs-1/TASK-DETAIL.md#bug1-n001-enforce-silent-bystander-rule-in-updateentitydescriptorrequestsystem)

**Description:**
Remove anti-pattern `WriteAck` calls generating negative responses on non-authoritative logic paths, replacing them with `Debug`-level log statements to silently discard.

**Requirements:**
- Remove negative ACKs (`EntityNotFound`, `NotSupported`, `NotOwner`).
- Do not modify the `Success` paths.
- Log message must contain `EntityId` and discard reason.

**Design Reference:** [BUG1 DESIGN.md - §2.1](docs/bugs-1/DESIGN.md#21-enforce-silent-bystander-rule-in-updateentitydescriptorrequestsystem)

**Tests Required:**
- ✅ Non-authoritative node emits no ACK, just silent debug log.
- ✅ Unfound entity emits debug log, no ACK.
- ✅ Unsupported descriptor type emits debug log, no ACK.
- ✅ Authoritative path correctly issues Success ACK.

---

### Task 5: Fan-Out Entity Descriptor Disposal (BUG1-N002)

**File:** `FDP/ModuleHost/ModuleHost.Network.Cyclone/Systems/CycloneNetworkCleanupSystem.cs` and others (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md §BUG1-N002](docs/bugs-1/TASK-DETAIL.md#bug1-n002-fan-out-entity-descriptor-disposal-in-cyclenetworkdiscardcleanuplsystem)

**Description:**
Refactor the network cleanup logic to fan-out the dispose call to a collection of `IDescriptorTranslator` using resilience (try/catch).

**Requirements:**
- Accept `IEnumerable<IDescriptorTranslator>`.
- Iterate through each translator, invoke `Dispose(long)` wrapped in `try..catch`.
- On error, gracefully log translator type name and entity ID, without interrupting remaining translations.

**Design Reference:** [BUG1 DESIGN.md - §2.2](docs/bugs-1/DESIGN.md#22-fan-out-entity-descriptor-disposal-in-cyclenetworkdiscardcleanuplsystem)

**Tests Required:**
- ✅ Verify all registered translators receive `Dispose` upon dead authoritative entity.
- ✅ Verify exception in one translator doesn't short-circuit disposal loop for remainder.
- ✅ Verify non-authoritative entities skip translator disposition entirely.

---

## 🧪 Testing Requirements
- **Quality Over Quantity:** Tests must verify behavior and correct paths. Do not just assert something exists.
- **Edge Cases:** All negative/discard paths in Task 4 and Task 5 must have strict assertions confirming they don't produce unintended side effects (e.g. no ACKs issued in non-auth scenarios).

---

## ⚠️ Quality Standards

**❗ TEST QUALITY EXPECTATIONS**
- **NOT ACCEPTABLE:** Tests that only verify "can I set this value" or use `Assert.Contains` loosely.
- **REQUIRED:** Tests that verify actual behavior and edge cases (e.g. `UpdateEntityDescriptorAck` is truly absent when it shouldn't be).

**❗ REPORT QUALITY EXPECTATIONS**
- **REQUIRED:** Document issues encountered and how you resolved them
- **REQUIRED:** Document design decisions YOU made beyond the spec
- **REQUIRED:** Share insights on code quality and improvement opportunities
- **REQUIRED:** Note any edge cases or scenarios discovered during implementation

---

## 📊 Report Requirements

**Focus on Developer Insights, Not Understanding Checks**

Please capture your valuable insights and experience:

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Are there any performance concerns or optimization opportunities you noticed?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] BUG1-F001 completed
- [ ] BUG1-F002 completed
- [ ] BUG1-F003 completed
- [ ] BUG1-N001 completed
- [ ] BUG1-N002 completed
- [ ] All tests passing
- [ ] Report submitted answering the Developer Insight questions

---

## ⚠️ Common Pitfalls to Avoid
- Blindly trusting test names. Ensure assertions actually map to requirements.
- Missing edge case exception handling inside the loop in Task 5.

---

## 📚 Reference Materials
- **Task Defs:** `docs/bugs-1/TASK-DETAIL.md` (# BUG1-F001 through BUG1-N002)
- **Design:** `docs/bugs-1/DESIGN.md`
