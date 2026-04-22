# BATCH-03: Phase 3 - Network Interface Segregation

**Batch Number:** BATCH-03  
**Tasks:** MPM-P3-T01, MPM-P3-T02, MPM-P3-T03, MPM-P3-T04  
**Phase:** Phase 3 - Network Interface Segregation  
**Estimated Effort:** 7-9 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 and BATCH-02 completed

---

## 📋 Onboarding & Workflow

### Developer Instructions
This batch introduces a proper interface hierarchy for network translators. The goal is to split the monolithic `IDescriptorTranslator` contract into a base `INetworkTranslator` interface, keeping descriptor-specific members in `IDescriptorTranslator` and giving event translators a clean `INetworkEventTranslator` contract. Additionally, a common abstract base `CycloneBaseTranslator` eliminates code duplication. Finally, the diagnostic panel's brittle string-matching hack for translator direction is replaced with the interface property.

**This batch has strict task ordering.** Complete tasks in sequence - each task's output is the foundation for the next.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md` - How to work with batches
2. **Task Details:** `.dev/module-phase-manual/TASK-DETAIL.md` - See MPM-P3-T01, MPM-P3-T02, MPM-P3-T03, MPM-P3-T04
3. **Design Document:** `.dev/module-phase-manual/DESIGN.md` - Sections 3.0 through 3.6 (read all of Phase 3)
4. **Previous Reviews:** `.dev/module-phase-manual/reviews/BATCH-01-REVIEW.md`, `.dev/module-phase-manual/reviews/BATCH-02-REVIEW.md`

### Source Code Locations
- `FDP/Engine/Fdp.Core/Abstractions/` - IDescriptorTranslator, new INetworkTranslator, new INetworkEventTranslator
- `FDP/Network/Fdp.Network.Cyclone/Translators/` - CycloneTranslator, CycloneNativeEventTranslator, CycloneManagedEventTranslator, MultiInstanceCycloneTranslator, new CycloneBaseTranslator
- `FDP/Network/Fdp.Network.Cyclone/Systems/` - CycloneNetworkIngressSystem, CycloneEgressSystem
- `FDP/Engine/Fdp.Presentation/ImGui/Panels/ArchitectureDiagnosticsPanel.cs`

### Test Projects
- `FDP/Network/Fdp.Network.Cyclone.Tests/` - Run after each task
- `FDP/Engine/Fdp.Presentation.Tests/` (if it exists)

### Report Submission
**When done, submit your report to:**  
`.dev/module-phase-manual/reports/BATCH-03-REPORT.md`

**If you have questions, create:**  
`.dev/module-phase-manual/questions/BATCH-03-QUESTIONS.md`

---

## Context

After BATCH-01 and BATCH-02, the dead code is gone and ordinals are clean. Now the interface hierarchy needs to be fixed. Currently:
- `CycloneNativeEventTranslator` and `CycloneManagedEventTranslator` both implement `IDescriptorTranslator` even though they are transient event handlers with no `DescriptorOrdinal`, `ApplyToEntity`, or `Dispose` semantics.
- `IDescriptorTranslator` redundantly declares properties (`TopicName`, `Direction`, etc.) that logically belong at a base network-translator level.
- `ArchitectureDiagnosticsPanel` infers translator direction from system class name strings (fragile hack).

After this batch, the hierarchy will be:
```
INetworkTranslator (base - TopicName, Direction, Counts, PollIngress, ScanAndPublish)
  ├── IDescriptorTranslator (+ DescriptorOrdinal, TargetComponentIds, ApplyToEntity, Dispose)
  └── INetworkEventTranslator (marker - no additional members)
```

And the implementation hierarchy:
```
CycloneBaseTranslator : INetworkTranslator (shared impl for all Cyclone translators)
  ├── CycloneTranslator<TDds,TView> : IDescriptorTranslator (also implements CycloneBaseTranslator)
  ├── CycloneNativeEventTranslator<TEcs,TDds> : INetworkEventTranslator
  └── CycloneManagedEventTranslator<TEcs,TDds> : INetworkEventTranslator
```

**Related Tasks:**
- [MPM-P3-T01](./../TASK-DETAIL.md#mpm-p3-t01---create-inetworktranslator-base-interface) - Create INetworkTranslator
- [MPM-P3-T02](./../TASK-DETAIL.md#mpm-p3-t02---refactor-idescriptortranslator-to-extend-inetworktranslator) - Refactor IDescriptorTranslator
- [MPM-P3-T03](./../TASK-DETAIL.md#mpm-p3-t03---create-inetworkeventtranslator-and-update-event-translator-base-classes) - Extract CycloneBaseTranslator + INetworkEventTranslator + update event translators
- [MPM-P3-T04](./../TASK-DETAIL.md#mpm-p3-t04---update-ingressegress-systems-and-diagnostic-panel) - Update systems + remove GetDirectionLabel hack

---

## 🎯 Batch Objectives

1. Introduce `INetworkTranslator` as the root interface for all network translators.
2. Make `IDescriptorTranslator` extend `INetworkTranslator` without duplicating members.
3. Give event translators (`CycloneNativeEventTranslator`, `CycloneManagedEventTranslator`) a clean `INetworkEventTranslator` contract, removing the false obligation to implement descriptor-specific methods.
4. Extract `CycloneBaseTranslator` to prevent code duplication across the three translator families.
5. Update ingress/egress systems to accept `INetworkTranslator[]`.
6. Replace the `GetDirectionLabel` string-matching hack with `translator.Direction.ToString()`.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing build/tests:**

1. **Task 1:** Create `INetworkTranslator` → Build → **Build passes** ✅
2. **Task 2:** Refactor `IDescriptorTranslator` → Build → Run Cyclone tests → **All pass** ✅
3. **Task 3:** Create `CycloneBaseTranslator` + `INetworkEventTranslator` + update event translators → Build → Run Cyclone tests → **All pass** ✅
4. **Task 4:** Update systems + remove GetDirectionLabel → Build → Run all tests → **All pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ **Build passes** (`dotnet build IOS-IG-SimHost.sln` from `d:\Work\IOS-IG-SimHost-FDP-2`)
- ✅ **Relevant tests pass** (run Cyclone tests after each step)

**No stopping to ask for permission. Fix any compilation errors before moving on. Work autonomously until all success criteria are met.**

---

## ✅ Tasks

### Task 1: Create INetworkTranslator Base Interface (MPM-P3-T01)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#mpm-p3-t01---create-inetworktranslator-base-interface)  
**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 3.1

**New file to create:** `FDP/Engine/Fdp.Core/Abstractions/INetworkTranslator.cs`

Content (exactly as in DESIGN.md § 3.1):
- Namespace: `Fdp.Interfaces`
- Members: `string TopicName { get; }`, `TranslatorDirection Direction { get; }`, `long ReceivedSampleCount { get; }`, `long SentSampleCount { get; }`, `void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)`, `void ScanAndPublish(ISimulationView view)`

`TranslatorDirection` enum is already defined in the codebase (in `IDescriptorTranslator.cs` or nearby). Keep it where it is.

This is a **pure addition** - no existing code is changed in this task.

**Verify:**
- File exists at `FDP/Engine/Fdp.Core/Abstractions/INetworkTranslator.cs`
- `dotnet build IOS-IG-SimHost.sln` passes.
- No existing code is changed.

---

### Task 2: Refactor IDescriptorTranslator to Extend INetworkTranslator (MPM-P3-T02)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#mpm-p3-t02---refactor-idescriptortranslator-to-extend-inetworktranslator)  
**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 3.2

**File to modify:** `FDP/Engine/Fdp.Core/Abstractions/IDescriptorTranslator.cs`

Changes:
1. Add `: INetworkTranslator` to the interface declaration.
2. Remove from `IDescriptorTranslator` the members that are now inherited from `INetworkTranslator`: `TopicName`, `Direction`, `ReceivedSampleCount`, `SentSampleCount`, `PollIngress`, `ScanAndPublish`.
3. Keep: `DescriptorOrdinal`, `TargetComponentIds`, `ApplyToEntity`, `Dispose`.

The resulting interface should match DESIGN.md § 3.2 exactly (5 members total including the default implementation for `TargetComponentIds`).

Because `CycloneTranslator<>` already implements all these methods, NO changes to any concrete translator class should be needed.

**Verify:**
- `IDescriptorTranslator` extends `INetworkTranslator`.
- `dotnet build IOS-IG-SimHost.sln` passes.
- `dotnet test FDP/Network/Fdp.Network.Cyclone.Tests/Fdp.Network.Cyclone.Tests.csproj --no-build` - all 40 pass.

---

### Task 3: Extract CycloneBaseTranslator + Create INetworkEventTranslator + Update Event Translators (MPM-P3-T03)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#mpm-p3-t03---extract-cyclonebasetranslator-and-switch-event-translator-interfaces)  
**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 3.0, 3.3, 3.4

This task has two sequential steps:

#### Step A: Extract CycloneBaseTranslator

**New file:** `FDP/Network/Fdp.Network.Cyclone/Translators/CycloneBaseTranslator.cs`

Read the current implementations of `CycloneTranslator<TDds,TView>`, `CycloneNativeEventTranslator<TEcs,TDds>`, and `CycloneManagedEventTranslator<TEcs,TDds>` to identify their shared `INetworkTranslator` member implementations (TopicName, ReceivedSampleCount, SentSampleCount, and any shared fields/constructor logic).

Create `CycloneBaseTranslator` that:
- Implements `INetworkTranslator`
- Carries the non-generic shared members: `TopicName` (property), `ReceivedSampleCount`, `SentSampleCount`, `Direction` (abstract)
- Constructor takes the common parameters shared across all three translator constructors

**Important design constraint from DESIGN.md § 3.0:** Because each translator is generic with its own `TDds`, the `Reader`/`Writer` fields cannot literally live in the non-generic base. The base carries only non-generic `INetworkTranslator` members. `CycloneTranslator`, `CycloneNativeEventTranslator`, and `CycloneManagedEventTranslator` change their inheritance from `(none)` to `CycloneBaseTranslator` and remove the now-duplicated implementations.

After Step A:
- `CycloneTranslator<TDds,TView>` extends `CycloneBaseTranslator` and still implements `IDescriptorTranslator`
- `CycloneNativeEventTranslator<TEcs,TDds>` extends `CycloneBaseTranslator`
- `CycloneManagedEventTranslator<TEcs,TDds>` extends `CycloneBaseTranslator`
- Build passes, Cyclone tests pass

#### Step B: Create INetworkEventTranslator + Update Event Translators

**New file:** `FDP/Engine/Fdp.Core/Abstractions/INetworkEventTranslator.cs`

Content (from DESIGN.md § 3.3 - XML doc included):
```csharp
namespace Fdp.Interfaces
{
    /// <summary>
    /// Marker interface for transient network event translators.
    /// Event translators do not manage persistent entity state and have no
    /// DescriptorOrdinal, TargetComponentIds, ApplyToEntity, or Dispose contract.
    /// </summary>
    public interface INetworkEventTranslator : INetworkTranslator
    {
    }
}
```

**Files to modify:**
- `FDP/Network/Fdp.Network.Cyclone/Translators/CycloneNativeEventTranslator.cs`:
  - Add `: INetworkEventTranslator` to the class/base declaration (alongside `CycloneBaseTranslator`)
  - Remove `DescriptorOrdinal` property (already removed `GetHashCode` assignment in BATCH-01; remove the property itself)
  - Remove `TargetComponentIds` property (if present)
  - Remove `ApplyToEntity` method (if present)
  - Remove `Dispose` method (if present - these have no meaning for transient events)
- `FDP/Network/Fdp.Network.Cyclone/Translators/CycloneManagedEventTranslator.cs`:
  - Apply the same changes

**Note:** `FireInteractionEventTranslator` inherits from `CycloneNativeEventTranslator`. After this change it automatically satisfies `INetworkEventTranslator`. Its file should NOT need changes.

**Verify after Task 3:**
- `CycloneBaseTranslator.cs` and `INetworkEventTranslator.cs` exist.
- `CycloneTranslator` extends `CycloneBaseTranslator` and implements `IDescriptorTranslator`.
- Neither event translator class exposes `DescriptorOrdinal`, `ApplyToEntity`, or `Dispose`.
- `dotnet build IOS-IG-SimHost.sln` passes.
- `dotnet test FDP/Network/Fdp.Network.Cyclone.Tests/Fdp.Network.Cyclone.Tests.csproj --no-build` - all tests pass (especially `CycloneManagedEventTranslatorTests`).

---

### Task 4: Update Ingress/Egress Systems and Remove GetDirectionLabel (MPM-P3-T04)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#mpm-p3-t04---update-ingressegress-systems-and-diagnostic-panel)  
**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 3.5, 3.6

**Files to modify:**

1. **`FDP/Network/Fdp.Network.Cyclone/Systems/CycloneNetworkIngressSystem.cs`**
   - Change constructor/field type from `IDescriptorTranslator[]` to `INetworkTranslator[]`
   - The system only calls `PollIngress` on each translator - both interfaces provide this method
   - Update the field declaration and constructor parameter

2. **`FDP/Network/Fdp.Network.Cyclone/Systems/CycloneEgressSystem.cs`** (or equivalent egress system)
   - Same change as above: `IDescriptorTranslator[]` → `INetworkTranslator[]`
   - The system only calls `ScanAndPublish` - available on `INetworkTranslator`

3. **`FDP/Engine/Fdp.Presentation/ImGui/Panels/ArchitectureDiagnosticsPanel.cs`**
   - Find and delete the `GetDirectionLabel(string systemName)` method (around line 274 per DESIGN.md context)
   - In `EnumerateTranslatorRows` (or whichever method called `GetDirectionLabel`), replace the call `GetDirectionLabel(system.GetType().Name)` with `translator.Direction.ToString()`
   - Ensure `EnumerateTranslatorRows` iterates over `INetworkTranslator` so it includes both descriptor and event translators

**Important:** `CycloneNetworkCleanupSystem` calls `Dispose(networkEntityId)` on its translators - it must keep `IDescriptorTranslator[]`. Do NOT change it.

**Verify:**
- `GetDirectionLabel` method no longer exists in `ArchitectureDiagnosticsPanel.cs`.
- `CycloneNetworkIngressSystem` constructor accepts `INetworkTranslator[]`.
- `CycloneEgressSystem` constructor accepts `INetworkTranslator[]`.
- `CycloneNetworkCleanupSystem` still uses `IDescriptorTranslator[]`.
- `dotnet build IOS-IG-SimHost.sln` passes.
- `dotnet test IOS-IG-SimHost.sln --no-build` - all Cyclone system tests pass.

---

## 🧪 Testing Requirements

1. **After every task:** `dotnet build IOS-IG-SimHost.sln` from `d:\Work\IOS-IG-SimHost-FDP-2`
2. **After Task 2 and Task 3:** `dotnet test FDP/Network/Fdp.Network.Cyclone.Tests/Fdp.Network.Cyclone.Tests.csproj --no-build`
3. **Final sweep after Task 4:** `dotnet test IOS-IG-SimHost.sln --no-build`

No new tests required by this batch (the refactoring is structural, not behavioral). Existing `CycloneManagedEventTranslatorTests` and `CycloneTranslatorTests` must continue to pass.

---

## 📊 Report Requirements

Submit your report to `.dev/module-phase-manual/reports/BATCH-03-REPORT.md`.

```markdown
# BATCH-03 Report

## Completion Status
- [ ] MPM-P3-T01: Create INetworkTranslator
- [ ] MPM-P3-T02: Refactor IDescriptorTranslator
- [ ] MPM-P3-T03: Extract CycloneBaseTranslator + INetworkEventTranslator + update event translators
- [ ] MPM-P3-T04: Update systems + remove GetDirectionLabel

## Build Status
[Result of: dotnet build IOS-IG-SimHost.sln]

## Test Status
[Result of Cyclone tests + full suite]

## Developer Insights

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did extracting CycloneBaseTranslator require any changes beyond what the design specified?

**Q3:** Were there any call sites (beyond those specified) that needed updating when CycloneNetworkIngressSystem changed its parameter type?

**Q4:** Did you find any other places where GetDirectionLabel-style hacks exist?

**Q5:** Any architecture concerns or simplification opportunities you spotted?

## Suggested Commit Message
[Your suggested git commit message]
```

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `INetworkTranslator.cs` exists in `FDP/Engine/Fdp.Core/Abstractions/`
- [ ] `IDescriptorTranslator` extends `INetworkTranslator` without duplicating members
- [ ] `INetworkEventTranslator.cs` exists in `FDP/Engine/Fdp.Core/Abstractions/`
- [ ] `CycloneBaseTranslator.cs` exists in `FDP/Network/Fdp.Network.Cyclone/Translators/`
- [ ] Event translators implement `INetworkEventTranslator` and no longer expose `DescriptorOrdinal`, `ApplyToEntity`, `Dispose`
- [ ] `CycloneNetworkIngressSystem` and `CycloneEgressSystem` accept `INetworkTranslator[]`
- [ ] `GetDirectionLabel` method deleted from `ArchitectureDiagnosticsPanel`
- [ ] `dotnet build IOS-IG-SimHost.sln` passes with zero errors
- [ ] `CycloneManagedEventTranslatorTests` and `CycloneTranslatorTests` pass
- [ ] Report submitted

---

## ⚠️ Common Pitfalls to Avoid

- **`CycloneNetworkCleanupSystem` stays with `IDescriptorTranslator[]`** - it calls `Dispose()` which is not on `INetworkTranslator`. Do NOT change it.
- **Task ordering matters:** Do Task 1 (create INetworkTranslator) before Task 2 (modify IDescriptorTranslator). Do both before Task 3 (which references both). Task 4 depends on all prior.
- **CycloneBaseTranslator complexity:** The three translator classes are generic but the base need not be. Only non-generic `INetworkTranslator` members live in the base; generic `TDds`-specific Reader/Writer construction stays in each derived class.
- **Event translators have no `DescriptorOrdinal` property after this task.** Any code that casts to `IDescriptorTranslator` and accesses `DescriptorOrdinal` on an event translator would have been wrong before and must be fixed.
- **FireInteractionEventTranslator inherits from `CycloneNativeEventTranslator`** - it should NOT need changes if the base class changes are done correctly.
- **Don't stop and ask for permission.** Fix all compilation errors autonomously. Build must be green before writing the report.

---

## 📚 Reference Materials
- **Task Details:** `.dev/module-phase-manual/TASK-DETAIL.md` - MPM-P3-T01 through MPM-P3-T04
- **Design:** `.dev/module-phase-manual/DESIGN.md` - Sections 3.0 through 3.6 (read all!)
- **Previous Reviews:** `.dev/module-phase-manual/reviews/BATCH-01-REVIEW.md` (for context on what was removed)
