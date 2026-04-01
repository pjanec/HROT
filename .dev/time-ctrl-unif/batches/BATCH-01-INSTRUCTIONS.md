# BATCH-01: CQRS Message Layer — Foundation

**Batch Number:** BATCH-01  
**Tasks:** TCU-M001, TCU-M002, TCU-T005  
**Phase:** Phase 1 — CQRS Message Layer  
**Estimated Effort:** 4–6 hours  
**Priority:** HIGH  
**Dependencies:** None (this is the first batch)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch establishes the message layer that all higher-level controllers will build on. You are fixing the network wire DTOs and introducing the local domain message types as described in the design. No controller logic is touched in this batch.

### Required Reading (IN ORDER)

1. **Design Document:** `.dev/time-ctrl-unif/docs/DESIGN.md` — read §1 Background, §2 Current Problems, §4.1 CQRS Message Contracts
2. **Task Definitions:** `.dev/time-ctrl-unif/docs/TASK-DETAIL.md` — read TCU-M001, TCU-M002, TCU-T005 in full
3. **Onboarding:** `.dev/time-ctrl-unif/ONBOARDING.md`

### Source Code Location

- **Wire DTOs (to modify):** `FDP/Toolkits/FDP.Toolkit.Time/Messages/TimeMessages.cs`
- **Domain messages (new file):** `FDP/Toolkits/FDP.Toolkit.Time/Domain/TimeLocalEvents.cs`
- **Time test project:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/`
- **New test file:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/TimeMessagesTests.cs`
- **FDP solution:** `FDP/FDP.sln`
- **Time toolkit csproj:** `FDP/Toolkits/FDP.Toolkit.Time/FDP.Toolkit.Time.csproj`
- **Time tests csproj:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj`

### Report Submission

**When done, submit your report to:**  
`.dev/time-ctrl-unif/reports/BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev/time-ctrl-unif/questions/BATCH-01-QUESTIONS.md`

---

## Context

The current codebase uses C# properties on DDS wire DTOs (causing IL overhead and blitting issues) and is missing several fields needed by the new unified controllers. This batch converts the DTOs to plain fields and adds the missing fields. It also creates two new local domain message structs (`AdvanceFrameIntent`, `FrameStepCompletedEvent`) in a dedicated `Domain` namespace — these are pure in-process types with no serialisation attributes whatsoever.

**Related Tasks:**
- [TCU-M001](../docs/TASK-DETAIL.md#tcu-m001--fix-network-wire-dtos) — Fix Network Wire DTOs
- [TCU-M002](../docs/TASK-DETAIL.md#tcu-m002--introduce-local-domain-message-types) — Introduce Local Domain Message Types
- [TCU-T005](../docs/TASK-DETAIL.md#tcu-t005--unit-tests-dto-round-trip-and-domain-events) — Unit Tests: DTO Round-Trip and Domain Events

---

## 🎯 Batch Objectives

1. All network wire DTOs in `TimeMessages.cs` use plain public fields (no properties).
2. Missing fields (`TargetSimTime` on `FrameOrderDescriptor`; `SimTimeSnapshot`, `TimeScale` on `SwitchTimeModeWireDto` and `SwitchTimeModeEvent`) are added.
3. `ToWire()` / `ToEvent()` helpers updated to map the new fields.
4. New file `TimeLocalEvents.cs` in `FDP.Toolkit.Time.Domain` namespace with `AdvanceFrameIntent` and `FrameStepCompletedEvent` exact structs per spec (no DDS/MessagePack attributes).
5. New test file `TimeMessagesTests.cs` with all required tests passing.

---

## ✅ Tasks

### Task 1: Fix Network Wire DTOs (TCU-M001)

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Messages/TimeMessages.cs` (UPDATE)  
**Task Definition:** See [TCU-M001 in TASK-DETAIL.md](../docs/TASK-DETAIL.md#tcu-m001--fix-network-wire-dtos) — read it in full.

**Summary of changes needed (confirmed against spec):**

| Struct | Change |
|---|---|
| `FrameOrderDescriptor` | Properties → plain fields; add `double TargetSimTime` at `[Key(3)]` / `[DdsId(3)]` |
| `FrameAckDescriptor` | Properties → plain fields |
| `TimePulseDescriptor` | Properties → plain fields |
| `SwitchTimeModeWireDto` | Properties → plain fields; add `double SimTimeSnapshot` at `[DdsId(3)]`, `float TimeScale` at `[DdsId(4)]`; update `ToWire()`/`ToEvent()` |
| `SwitchTimeModeEvent` | Properties → plain fields; add `double SimTimeSnapshot`, `float TimeScale` |

**Critical constraints (READ CAREFULLY):**
- `[DdsId(N)]` ordinals must be **preserved exactly** — do NOT renumber existing fields.
- New fields get the next ordinal in sequence.
- `[DdsTopic]` must remain on `TimePulseDescriptor` and `SwitchTimeModeWireDto`.
- `[MessagePackObject]` / `[Key(N)]` must remain on local structs (`FrameOrderDescriptor`, `FrameAckDescriptor`, `SwitchTimeModeEvent`).
- After changes: run `dotnet build FDP/FDP.sln` and fix any build errors before writing tests.

---

### Task 2: Introduce Local Domain Message Types (TCU-M002)

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Domain/TimeLocalEvents.cs` (NEW FILE)  
**Task Definition:** See [TCU-M002 in TASK-DETAIL.md](../docs/TASK-DETAIL.md#tcu-m002--introduce-local-domain-message-types) — read it in full.

Create the exact structs as specified:

```csharp
namespace FDP.Toolkit.Time.Domain
{
    public struct AdvanceFrameIntent
    {
        public long   FrameID;
        public float  FixedDelta;
        public double TargetSimTime;
    }

    public struct FrameStepCompletedEvent
    {
        public long FrameID;
        public int  NodeID;
    }
}
```

**Constraints:**
- **NO** `[DdsTopic]`, `[EventId]`, `[MessagePackObject]`, or any serialisation attributes.
- Plain fields only — no properties, no methods.
- Create the `Domain/` subfolder inside `FDP/Toolkits/FDP.Toolkit.Time/` if it does not exist.

---

### Task 3: Unit Tests for DTOs and Domain Events (TCU-T005)

**File:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/TimeMessagesTests.cs` (NEW FILE)  
**Task Definition:** See [TCU-T005 in TASK-DETAIL.md](../docs/TASK-DETAIL.md#tcu-t005--unit-tests-dto-round-trip-and-domain-events) — read it in full.

**Required tests (minimum — all must assert behaviour, not just compilation):**

From TCU-M001 success conditions:
- `SwitchTimeModeWireDto_RoundTrip` — create `SwitchTimeModeEvent` with all fields set to non-zero values; call `ToWire().ToEvent()`; assert **every field** equals the original (including new `SimTimeSnapshot` and `TimeScale`).
- `FrameOrderDescriptor_HasTargetSimTime` — construct with `TargetSimTime = 42.5`; assert the field is readable and equals `42.5`.
- `SwitchTimeModeWireDto_ToWire_PreservesAllFields` — verify `SimTimeSnapshot` and `TimeScale` survive the round-trip.
- `FrameOrderDescriptor_PlainFields_NoCsharpProperties` — use reflection to assert that public members of `FrameOrderDescriptor` are **fields** (not properties). E.g.: `typeof(FrameOrderDescriptor).GetProperties(BindingFlags.Public | BindingFlags.Instance)` should be empty.

From TCU-M002 success conditions:
- `AdvanceFrameIntent_CanBePublishedAndConsumed` — create a `FdpEventBus`; register and publish an `AdvanceFrameIntent`; swap buffers; consume and assert `FrameID`, `FixedDelta`, and `TargetSimTime` match the published values.
- `FrameStepCompletedEvent_CanBePublishedAndConsumed` — same pattern; assert `FrameID` and `NodeID` match.

**Quality bar:**  
Tests must assert **specific values** — not just "no exception thrown". All assertions must validate correctness of field data.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests before moving on:**

1. **Task 1 (TCU-M001):** Modify DTOs → `dotnet build FDP/FDP.sln` — zero errors ✅  
2. **Task 2 (TCU-M002):** Create domain types → `dotnet build FDP/FDP.sln` — zero errors ✅  
3. **Task 3 (TCU-T005):** Write all tests → `dotnet test FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj` — all pass ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Build succeeds with zero errors
- ✅ **ALL tests passing** (including pre-existing tests)

Do NOT ask permission to run tests, fix errors, or rebuild. Do all of that autonomously. Write the report only after everything is green.

---

## 🧪 Testing Requirements

- **Minimum:** 6 tests (the required set above)
- **Additional:** Add any edge-case tests you discover (e.g. all FrameAckDescriptor fields are plain fields check)
- **Quality:** Every test must assert a specific value or behaviour — "no exception" only is not acceptable
- **Pre-existing tests must keep passing:** Run `dotnet test FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj` and fix any regressions

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `dotnet build FDP/FDP.sln` — zero errors, zero warnings on changed types
- [ ] All wire DTO structs use plain fields (no C# properties on DDS types)
- [ ] `FrameOrderDescriptor` has `TargetSimTime` field at `[Key(3)]`/`[DdsId(3)]`
- [ ] `SwitchTimeModeWireDto` and `SwitchTimeModeEvent` have `SimTimeSnapshot` and `TimeScale`
- [ ] `ToWire()`/`ToEvent()` helpers map the new fields correctly
- [ ] `TimeLocalEvents.cs` exists with `AdvanceFrameIntent` and `FrameStepCompletedEvent` (no attributes)
- [ ] `TimeMessagesTests.cs` exists with 6+ passing tests
- [ ] `dotnet test FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj` — all tests pass
- [ ] `BATCH-01-REPORT.md` submitted

---

## 📊 Report Requirements

Submit your report to `.dev/time-ctrl-unif/reports/BATCH-01-REPORT.md`.

Use this structure:

```markdown
# BATCH-01 Report

## Completion Status
[Completed / Partially Completed — list what was done and what was skipped with reasons]

## Test Results
[Paste final test run output showing pass count]

## Developer Insights

**Q1: Issues Encountered**
What problems did you run into during implementation? How did you resolve them?

**Q2: Weak Points Spotted**
What fragile or confusing areas did you notice in the existing codebase?

**Q3: Design Decisions Made Beyond the Spec**
What choices did you make that weren't explicitly specified? Why?

**Q4: Edge Cases Discovered**
What scenarios weren't covered in the instructions that you encountered?

**Q5: Suggested commit message**
What single-line commit message best captures this batch?
```

---

## ⚠️ Common Pitfalls

- Do NOT renumber existing `[DdsId(N)]` ordinals — this breaks backwards compatibility with recordings.
- Do NOT add `[MessagePackObject]` or DDS attributes to the new `Domain/` types.
- The `obj/Generated/` files are auto-regenerated on build — do not manually edit them.
- Make sure `ToWire()` and `ToEvent()` are updated symmetrically — both directions must carry the new fields.
- Find `FdpEventBus` test helpers by searching existing test files in `FDP.Toolkit.Time.Tests` — check how `FdpEventBus` is constructed and used in existing tests.

---

## 📚 Reference Materials

- **Task Definitions:** `.dev/time-ctrl-unif/docs/TASK-DETAIL.md` — §TCU-M001, §TCU-M002, §TCU-T005
- **Design:** `.dev/time-ctrl-unif/docs/DESIGN.md` — §4.1 CQRS Message Contracts
- **Developer Skill Guide:** `.github/skills/developer/SKILL.md`
