# BATCH-01: Phase 1 - Dead Code Purge

**Batch Number:** BATCH-01  
**Tasks:** MPM-P1-T01, MPM-P1-T02, MPM-P1-T03  
**Phase:** Phase 1 - Dead Code Purge  
**Estimated Effort:** 5-7 hours  
**Priority:** HIGH  
**Dependencies:** None (first batch)

---

## 📋 Onboarding & Workflow

### Developer Instructions
This batch is pure subtraction. You will delete dead code that creates confusion, violates ACL constraints, or pollutes the diagnostic UI. No new logic. No feature additions. Delete the dead files, strip dead interface implementations, update solution files, and make the build pass.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md` - How to work with batches
2. **Task Details:** `.dev/module-phase-manual/TASK-DETAIL.md` - See MPM-P1-T01, MPM-P1-T02, MPM-P1-T03
3. **Design Document:** `.dev/module-phase-manual/DESIGN.md` - Sections 1.1, 1.2, 1.3

### Source Code Locations
- `Hrot/Subsystems/Hrot.SimHost/` - SimHost subsystem
- `FDP/Network/Fdp.Network.Cyclone/` - Cyclone network layer
- `FDP/Examples/Fdp.Examples.NetworkDemo/` - NetworkDemo to be deleted entirely
- `FDP/Examples/Fdp.Examples.NetworkDemo.Tests/` - NetworkDemo tests to be deleted entirely
- `FDP/Engine/Fdp.Core/Abstractions/` - FDP core abstractions

### Test Projects
- `FDP/Network/Fdp.Network.Cyclone.Tests/` - Run after Task 2 and Task 3 changes

### Report Submission
**When done, submit your report to:**  
`.dev/module-phase-manual/reports/BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev/module-phase-manual/questions/BATCH-01-QUESTIONS.md`

---

## Context

This is the first batch of the MPM (Module Phase Manual) project. The codebase contains dead code left behind by superseded subsystems:
- Two perception systems no longer registered anywhere
- A legacy network-replay interface used only by a dead demo project
- Auto-translator infrastructure that violates the Anti-Corruption Layer

All three tasks in this batch are pure deletion. No new code. The work will reduce confusion and compiler noise for all future batches.

**Related Tasks:**
- [MPM-P1-T01](./../TASK-DETAIL.md#mpm-p1-t01---delete-legacy-perception-systems) - Delete two unused perception system files
- [MPM-P1-T02](./../TASK-DETAIL.md#mpm-p1-t02---delete-inetworkreplaytarget-and-strip-from-translators) - Delete INetworkReplayTarget interface and strip from 4 translators
- [MPM-P1-T03](./../TASK-DETAIL.md#mpm-p1-t03---delete-autocyclonetranslators-replicationbootstrap-and-networkdemo) - Delete auto-translators, ReplicationBootstrap, FdpDescriptorAttribute, and entire NetworkDemo project

---

## 🎯 Batch Objectives

Remove all dead code that:
1. Creates confusion (unused systems with explanatory comments about why they are not registered)
2. Violates ACL constraints (auto-translators coupling ECS memory layout directly to DDS wire format)
3. Pollutes the diagnostic UI (dead interface implementations)

After this batch, the solution builds cleanly with none of the deleted artifacts referenced anywhere.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing build/tests:**

1. **Task 1:** Delete files → Build → **Build passes** ✅
2. **Task 2:** Delete/strip → Build → Run tests → **All tests pass** ✅
3. **Task 3:** Delete files + update SLN files → Build → Run tests → **All tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ **Build passes** (run `dotnet build IOS-IG-SimHost.sln` from repo root)
- ✅ **Relevant tests pass** (run `dotnet test` on affected test projects)

**No stopping to ask for permission. Fix any compilation errors you encounter before moving on. Work autonomously until all success criteria are met, then write the report.**

---

## ✅ Tasks

### Task 1: Delete Legacy Perception Systems (MPM-P1-T01)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#mpm-p1-t01---delete-legacy-perception-systems)  
**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 1.1

**Files to delete:**
- `Hrot/Subsystems/Hrot.SimHost/Systems/PerceptionBroadphaseSystem.cs`
- `Hrot/Subsystems/Hrot.SimHost/Systems/ThreatEvaluationAdapterSystem.cs`

**File to modify:**
- `Hrot/Subsystems/Hrot.SimHost/Modules/CombatModule.cs`
  - Remove the comment block that says why `PerceptionBroadphaseSystem` and `ThreatEvaluationAdapterSystem` are not registered here.
  - Remove any `using` directives that only referenced the namespaces of the deleted files.

**Verify:**
- Run `dotnet build IOS-IG-SimHost.sln` from repo root. Must succeed with no errors.
- Run `grep -r "PerceptionBroadphaseSystem\|ThreatEvaluationAdapterSystem" Hrot/` - must return zero results.

---

### Task 2: Delete INetworkReplayTarget and Strip from Translators (MPM-P1-T02)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#mpm-p1-t02---delete-inetworkreplaytarget-and-strip-from-translators)  
**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 1.2

**File to delete:**
- `FDP/Network/Fdp.Network.Cyclone/Abstractions/INetworkReplayTarget.cs`

**Files to modify** (remove `: INetworkReplayTarget` from class declarations, delete the `InjectReplayData` method body):
- `FDP/Network/Fdp.Network.Cyclone/Translators/CycloneTranslator.cs`
- `FDP/Network/Fdp.Network.Cyclone/Translators/CycloneNativeEventTranslator.cs`
- `FDP/Network/Fdp.Network.Cyclone/Translators/CycloneManagedEventTranslator.cs`
- `FDP/Network/Fdp.Network.Cyclone/Translators/MultiInstanceCycloneTranslator.cs`

**Additional change in `CycloneNativeEventTranslator.cs`:**
- Remove the line `DescriptorOrdinal = topicName.GetHashCode();` from the constructor (the hack that used hash as a fake routing key for the deleted replay system). The `DescriptorOrdinal` property itself stays.

**Note on AutoCycloneTranslator / ManagedAutoCycloneTranslator:** These also implement `INetworkReplayTarget` but will be deleted entirely in Task 3 below. Do NOT touch them now.

**Verify:**
- Run `dotnet build IOS-IG-SimHost.sln` from repo root. Must succeed.
- Run `grep -r "INetworkReplayTarget\|InjectReplayData" FDP/Network/Fdp.Network.Cyclone/` - must return zero results (excluding the NetworkDemo files which come next in Task 3).
- Run `dotnet test FDP/Network/Fdp.Network.Cyclone.Tests/Fdp.Network.Cyclone.Tests.csproj` - all existing tests must pass.

---

### Task 3: Delete AutoCycloneTranslators, ReplicationBootstrap, and NetworkDemo (MPM-P1-T03)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#mpm-p1-t03---delete-autocyclonetranslators-replicationbootstrap-and-networkdemo)  
**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 1.3

**Files to delete:**
- `FDP/Network/Fdp.Network.Cyclone/Translators/AutoCycloneTranslator.cs`
- `FDP/Network/Fdp.Network.Cyclone/Translators/ManagedAutoCycloneTranslator.cs`
- `FDP/Network/Fdp.Network.Cyclone/ReplicationBootstrap.cs`
- `FDP/Engine/Fdp.Core/Abstractions/FdpDescriptorAttribute.cs`
- `FDP/Network/Fdp.Network.Cyclone.Tests/Translators/AutoCycloneTranslatorTests.cs`
- Entire directory `FDP/Examples/Fdp.Examples.NetworkDemo/` (all files and subdirectories)
- Entire directory `FDP/Examples/Fdp.Examples.NetworkDemo.Tests/` (all files and subdirectories)

**Solution files to update:**
Remove the project entries for `Fdp.Examples.NetworkDemo` and `Fdp.Examples.NetworkDemo.Tests` from both:
- `FDP/FDP.sln`
- `IOS-IG-SimHost.sln`

When removing from .sln files, you must remove:
1. The `Project(...)...EndProject` block for each deleted project
2. All entries in the `GlobalSection(ProjectConfigurationPlatforms)` block that reference the deleted project GUIDs
3. Any entries in `GlobalSection(NestedProjects)` referencing those GUIDs

**Verify:**
- Run `dotnet build IOS-IG-SimHost.sln` from repo root. Must succeed with no errors.
- Run `dotnet build FDP/FDP.sln` from repo root. Must succeed with no errors.
- Run `grep -r "AutoCycloneTranslator\|ManagedAutoCycloneTranslator\|ReplicationBootstrap\|FdpDescriptorAttribute\|\[FdpDescriptor" . --include="*.cs"` - must return zero results.
- Run `dotnet test FDP/Network/Fdp.Network.Cyclone.Tests/Fdp.Network.Cyclone.Tests.csproj` - all remaining tests must pass.

---

## 🧪 Testing Requirements

This batch is primarily deletion work. Testing focus:

1. **Build verification** after every task: `dotnet build IOS-IG-SimHost.sln` from `d:\Work\IOS-IG-SimHost-FDP-2\`
2. **Unit tests** for Cyclone translators after Task 2 and Task 3:
   ```
   dotnet test FDP/Network/Fdp.Network.Cyclone.Tests/Fdp.Network.Cyclone.Tests.csproj
   ```
3. **Full solution test run** after Task 3:
   ```
   dotnet test IOS-IG-SimHost.sln --no-build
   ```

**No new tests required** for this batch (you are deleting, not adding).

---

## 📊 Report Requirements

Submit your report to `.dev/module-phase-manual/reports/BATCH-01-REPORT.md`.

Structure:
```markdown
# BATCH-01 Report

## Completion Status
- [ ] MPM-P1-T01: Delete Legacy Perception Systems
- [ ] MPM-P1-T02: Delete INetworkReplayTarget
- [ ] MPM-P1-T03: Delete AutoCycloneTranslators and NetworkDemo

## Build Status
[Result of: dotnet build IOS-IG-SimHost.sln]

## Test Status
[Result of: dotnet test FDP/Network/Fdp.Network.Cyclone.Tests/...]

## Developer Insights

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points or other dead code in the existing codebase beyond what was specified? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** Were there any surprises in the SLN file editing (unexpected project references, extra GUIDs)?

**Q5:** Are there any remaining references to the deleted artifacts that weren't covered by the task spec?

## Suggested Commit Message
[Your suggested git commit message for this batch]
```

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] MPM-P1-T01: `PerceptionBroadphaseSystem.cs` and `ThreatEvaluationAdapterSystem.cs` deleted; `CombatModule.cs` cleaned up
- [ ] MPM-P1-T02: `INetworkReplayTarget.cs` deleted; all 4 translator classes stripped of the interface and `InjectReplayData`; `DescriptorOrdinal = topicName.GetHashCode()` removed from `CycloneNativeEventTranslator`
- [ ] MPM-P1-T03: All listed files/directories deleted; both SLN files updated; zero references to deleted symbols
- [ ] `dotnet build IOS-IG-SimHost.sln` passes with zero errors
- [ ] `dotnet test FDP/Network/Fdp.Network.Cyclone.Tests/...` passes
- [ ] Report submitted to `.dev/module-phase-manual/reports/BATCH-01-REPORT.md`

---

## ⚠️ Common Pitfalls to Avoid

- **SLN files:** A project in a .sln has entries in 3 places: `Project(...)...EndProject` blocks, `ProjectConfigurationPlatforms`, and sometimes `NestedProjects`. Remove ALL occurrences of each deleted project GUID.
- **Partial deletion:** If you delete a file but miss removing its `using` in another file, the build will fail with "type not found" errors. Fix these compile errors before moving on.
- **Don't touch AutoCycloneTranslator in Task 2:** Task 2 covers the 4 surviving translators only. AutoCyclone and ManagedAutoCyclone are deleted entirely in Task 3.
- **Don't stop and ask:** If you hit a compile error, read it, fix the root cause, rebuild. Keep going until the build is green.

---

## 📚 Reference Materials
- **Task Details:** `.dev/module-phase-manual/TASK-DETAIL.md` - See MPM-P1-T01, MPM-P1-T02, MPM-P1-T03
- **Design:** `.dev/module-phase-manual/DESIGN.md` - Sections 1.1, 1.2, 1.3
- **Codebase Context Table:** `.dev/module-phase-manual/DESIGN.md` § "Codebase Context" table
