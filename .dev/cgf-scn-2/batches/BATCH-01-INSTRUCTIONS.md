# BATCH-01: DataPolicy Cleanup and Execution-State Exclusion

**Batch Number:** BATCH-01
**Tasks:** TASK-S101, TASK-S102, TASK-S103, TASK-S104, TASK-S105
**Phase:** Phase 1 — DataPolicy Cleanup and Execution-State Exclusion
**Estimated Effort:** 4-6 hours
**Priority:** HIGH
**Dependencies:** None (first batch)

---

## Onboarding & Workflow

### Developer Instructions

This batch implements Phase 1 of the cgf-scn-2 workstream. The goal is to prevent
runtime execution buffers from polluting the scenario JSON DOM and to remove a now-
redundant custom translator. All changes are additive attribute additions plus one
file deletion.

### Required Reading (IN ORDER)

1. **Design Document:** `.dev/cgf-scn-2/DESIGN.md` — Read "Phase 1: DataPolicy Cleanup
   and Execution-State Exclusion" in full. Understand the two-persistence-path model
   (Scenarios vs Checkpoints) before touching any file.
2. **Task Definitions:** `.dev/cgf-scn-2/TASK-DETAIL.md` — Read tasks TASK-S101 through
   TASK-S105 for success conditions and constraints.
3. **Onboarding:** `.dev/cgf-scn-2/ONBOARDING.md` — Folder layout and build commands.

### Source Code Locations

| File | Change |
|---|---|
| `FDP/Engine/Fdp.Core/DataPolicyAttribute.cs` | Fix XML comments on `NoSave` and `NoRecord` |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/ChannelComponents.cs` | Add `[DataPolicy(DataPolicy.NoSave)]` to 3 structs |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BrainComponents.cs` | Add `[DataPolicy(DataPolicy.NoSave)]` to 3 structs |
| `FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs` | Add `[DataPolicy(DataPolicy.NoSave)]` to 2 structs |
| `Hrot/Subsystems/Hrot.SimHost/Serializers/WeaponChannelTranslator.cs` | DELETE the file |
| `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` | Remove `.RegisterTranslator(new Hrot.SimHost.Serializers.WeaponChannelTranslator())` |

### Test Projects

- `FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj` — new tests for S102, S103, S104, S105
- Build and test with:
  ```powershell
  cd d:\Work\IOS-IG-SimHost-FDP-2
  dotnet build IOS-IG-SimHost.sln --no-restore
  dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build
  dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build
  ```

### Report Submission

When done, submit your report to:
`.dev/cgf-scn-2/reports/BATCH-01-REPORT.md`

If you have questions, create:
`.dev/cgf-scn-2/questions/BATCH-01-QUESTIONS.md`

---

## Context

This is the first batch of the cgf-scn-2 workstream. Runtime execution components
(`WeaponChannel`, `BrainBTreeState`, etc.) currently pollute scenario JSON with volatile
mid-tick state that must never persist. The fix is to decorate them with
`[DataPolicy(DataPolicy.NoSave)]` so `ScenarioSerializer` / `FdpAutoSerializer` excludes
them via `ComponentTypeRegistry.GetSaveableTypeIds()`.

Once `WeaponChannel` is excluded from serialization, the custom `WeaponChannelTranslator`
(which existed to work around the auto-serializer's truncation of its `fixed byte` buffers)
is no longer visited by the serializer pipeline and must be deleted to avoid dead code.

The `DataPolicy.NoSave` XML comment currently reads "Exclude from Save Game / Checkpoints"
which is wrong — it applies only to scenario JSON, not binary checkpoints. Fix this first.

**Key concept:** `NoSave` = exclude from scenario JSON. `NoRecord` = exclude from binary
checkpoints. `Transient` = both. These are entirely separate paths. See DESIGN.md Phase 1
Background section.

---

## Batch Objectives

- Correct misleading `DataPolicy` XML documentation
- Tag all runtime execution components with `[DataPolicy(DataPolicy.NoSave)]`
- Delete the now-dead `WeaponChannelTranslator`
- Verify with unit tests that tagged components are absent from `GetSaveableTypeIds()`
  but remain in `GetRecordableTypeIds()`

---

## Tasks

### Task 1: Fix DataPolicy XML Comments (TASK-S101)

**File:** `FDP/Engine/Fdp.Core/DataPolicyAttribute.cs` (UPDATE)
**Task Definition:** See [TASK-DETAIL.md — TASK-S101](../TASK-DETAIL.md#task-s101-fix-datapolicynosave-xml-comment)

Replace the XML `<summary>` on `DataPolicy.NoSave`:

**CURRENT (wrong):**
```
/// <summary>
/// Exclude from Save Game / Checkpoints.
/// Use for runtime-only data that doesn't persist across sessions.
/// </summary>
NoSave = 1 << 3,
```

**REQUIRED:**
```
/// <summary>
/// Exclude from Scenario JSON serialization. Use for runtime execution state
/// (e.g., BTree pointers, active weapon channels) that should be preserved in
/// binary checkpoints but omitted from declarative authoring templates.
/// </summary>
NoSave = 1 << 3,
```

Replace the XML `<summary>` on `DataPolicy.NoRecord`:

**CURRENT (wrong):**
```
/// <summary>
/// Exclude from Flight Recorder (.fdp replay files).
/// Use for debug-only data that shouldn't be in recordings.
/// </summary>
NoRecord = 1 << 2,
```

**REQUIRED:**
```
/// <summary>
/// Exclude from Flight Recorder and Binary Checkpoints. Use for debug-only data
/// or metrics that should not pollute binary state snapshots.
/// </summary>
NoRecord = 1 << 2,
```

**Do NOT change** any enum values, flag bits, `DataPolicy.Transient`, `NoSnapshot`,
or `SnapshotViaClone`.

**Success:** No unit test required for a comment change. Build must succeed without errors.

---

### Task 2: Add DataPolicy.NoSave to Execution Channel Components (TASK-S102)

**File:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/ChannelComponents.cs` (UPDATE)
**Task Definition:** See [TASK-DETAIL.md — TASK-S102](../TASK-DETAIL.md#task-s102-add-datapolicynosave-to-execution-channel-components)

Add `[DataPolicy(DataPolicy.NoSave)]` to `LocomotionChannel`, `WeaponChannel`, and
`InteractionChannel`. Place the attribute immediately after the existing
`[ComponentId(...)]` line (or after `[StructLayout(...)]` — consistent ordering is
`[StructLayout] [ComponentId] [DataPolicy]`).

Example for `LocomotionChannel`:
```csharp
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.LocomotionChannel)]
[DataPolicy(DataPolicy.NoSave)]
public unsafe struct LocomotionChannel
{
    // ... unchanged ...
}
```

Apply the same pattern to `WeaponChannel` and `InteractionChannel`. Do NOT change any
field definitions.

**Tests Required (add to `FDP/Toolkits/Fdp.Toolkits.Tests/Scenario/` — new file
`DataPolicyNoSaveTests.cs` or add to an existing scenario test file):**

1. Register `LocomotionChannel`, `WeaponChannel`, `InteractionChannel` in a fresh
   `EntityRepository`; call `ComponentTypeRegistry.GetSaveableTypeIds()`; assert none
   of the three type IDs appears in the returned set.
2. Assert all three type IDs DO appear in `ComponentTypeRegistry.GetRecordableTypeIds()`
   (they must still reach binary checkpoints).

Look at `FDP/Toolkits/Fdp.Toolkits.Tests/Replication/DataPolicyTests.cs` for the
existing pattern of testing `GetSaveableTypeIds()` / `GetRecordableTypeIds()`.
Look at `FDP/Toolkits/Fdp.Toolkits.Tests/Scenario/ScenarioSerializerTests.cs` for
the `ComponentTypeRegistry.Clear()` / `repo.RegisterComponent<T>()` pattern.

---

### Task 3: Add DataPolicy.NoSave to Brain Execution Components (TASK-S103)

**File:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BrainComponents.cs` (UPDATE)
**Task Definition:** See [TASK-DETAIL.md — TASK-S103](../TASK-DETAIL.md#task-s103-add-datapolicynosave-to-brain-execution-components)

Add `[DataPolicy(DataPolicy.NoSave)]` to `BrainBTreeState`, `BrainHsm64`, and
`BrainHsm128`. Same placement as Task 2 — after existing `[ComponentId(...)]`.

**Critical constraint: Do NOT add `NoRecord`.** Brain execution state must still appear
in binary checkpoints (`GetRecordableTypeIds()`).

**Tests Required:**

1. Register `BrainBTreeState`, `BrainHsm64`, `BrainHsm128`; assert none appears in
   `GetSaveableTypeIds()`.
2. Assert all three appear in `GetRecordableTypeIds()`.

---

### Task 4: Add DataPolicy.NoSave to Transient Perception Components (TASK-S104)

**File:** `FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs` (UPDATE)
**Task Definition:** See [TASK-DETAIL.md — TASK-S104](../TASK-DETAIL.md#task-s104-add-datapolicynosave-to-transient-perception-components)

The structs `SensorContactList` (line ~217) and `ActiveSensorTracks` (line ~266) need
`[DataPolicy(DataPolicy.NoSave)]`. Do NOT touch `TargetMemory`, `PerceptionReceptor`,
or any other type in this file.

**Tests Required:**

1. Assert `SensorContactList` and `ActiveSensorTracks` are absent from `GetSaveableTypeIds()`.
2. Assert both appear in `GetRecordableTypeIds()`.

---

### Task 5: Delete WeaponChannelTranslator and Unregister It (TASK-S105)

**File to DELETE:** `Hrot/Subsystems/Hrot.SimHost/Serializers/WeaponChannelTranslator.cs`
**File to UPDATE:** `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`
**Task Definition:** See [TASK-DETAIL.md — TASK-S105](../TASK-DETAIL.md#task-s105-delete-weaponchanneltranslator-and-unregister-it)

**TASK-S102 must be complete first** (WeaponChannel must carry `[DataPolicy(DataPolicy.NoSave)]`
before you remove the translator that was working around the serializer limitation).

Step 1: Delete the file.
```powershell
Remove-Item "Hrot/Subsystems/Hrot.SimHost/Serializers/WeaponChannelTranslator.cs"
```

Step 2: In `SimHostApp.cs` line ~346, remove the line:
```csharp
.RegisterTranslator(new Hrot.SimHost.Serializers.WeaponChannelTranslator())
```
Also remove any now-dangling `using` statement that referenced only
`Hrot.SimHost.Serializers.WeaponChannelTranslator` (if the namespace is still used
by other translators, leave the `using`).

**Test Required:** Build the solution. If the solution builds without errors, the
translator is correctly removed. No `WeaponChannelTranslator` string may appear
anywhere in the repository after deletion.

Verify:
```powershell
Select-String -Path "Hrot/**/*.cs" -Pattern "WeaponChannelTranslator" -Recurse
```
This must return zero results.

---

## Mandatory Workflow: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1 (S101):** Edit XML comments -> Build -> Build succeeds. **Then proceed.**
2. **Task 2 (S102):** Add `[DataPolicy(DataPolicy.NoSave)]` to channels -> Write tests ->
   **ALL tests pass.** Then proceed.
3. **Task 3 (S103):** Brain components -> Write tests -> **ALL tests pass.** Then proceed.
4. **Task 4 (S104):** Perception components -> Write tests -> **ALL tests pass.** Then proceed.
5. **Task 5 (S105):** Delete file + remove registration -> Build -> **ALL tests pass.**

**DO NOT** move to the next task until all current tests pass.
**DO NOT** stop to ask permission to run tests, fix compiler errors, or fix failing tests.
Work autonomously until all 5 tasks are done and all tests pass, then write your report.

---

## Testing Requirements

- Minimum **6 new unit tests** covering the NoSave/NoRecord assertions for all 8 tagged
  components (channels x3, brain x3, perception x2). Tests may be grouped by task.
- All existing tests in `Fdp.Toolkits.Tests` and `Hrot.SimHost.Tests` must continue
  to pass.
- Tests must verify actual membership in `GetSaveableTypeIds()` / `GetRecordableTypeIds()`,
  not just that the attribute is applied to the type.

### Test Quality Standard

**REQUIRED:** Tests that call `GetSaveableTypeIds()` and assert the type ID is absent.
**NOT ACCEPTABLE:** Tests that only verify the attribute is present on the type via reflection
without invoking the registry query.

---

## Success Criteria

- [ ] `DataPolicy.NoSave` and `NoRecord` XML comments corrected (TASK-S101)
- [ ] `LocomotionChannel`, `WeaponChannel`, `InteractionChannel` carry `[DataPolicy(DataPolicy.NoSave)]` (TASK-S102)
- [ ] `BrainBTreeState`, `BrainHsm64`, `BrainHsm128` carry `[DataPolicy(DataPolicy.NoSave)]` (TASK-S103)
- [ ] `SensorContactList`, `ActiveSensorTracks` carry `[DataPolicy(DataPolicy.NoSave)]` (TASK-S104)
- [ ] `WeaponChannelTranslator.cs` deleted; no references remain (TASK-S105)
- [ ] All new tests pass
- [ ] All existing tests pass
- [ ] Solution builds without errors

---

## Common Pitfalls

- Do NOT add `[DataPolicy(DataPolicy.NoRecord)]` to brain or channel components — they
  must remain in binary checkpoints.
- Do NOT add `[DataPolicy(DataPolicy.NoSave)]` to `TargetMemory`, `PerceptionReceptor`,
  or `BrainBlackboard` — those are out of scope for this batch.
- When removing the `WeaponChannelTranslator` registration in `SimHostApp.cs`, ensure you
  preserve all other translator registrations on the same `ScenarioSerializerBuilder` chain.
- `ComponentTypeRegistry` is a shared static in tests — always call `ComponentTypeRegistry.Clear()`
  in test setup (see existing `ScenarioSerializerTests` pattern).

---

## Developer Insights

When writing your report, answer these questions:

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any other execution-state components that should have `[DataPolicy(DataPolicy.NoSave)]`
but currently don't?

**Q3:** Were there any unexpected dependencies on `WeaponChannelTranslator` that required
additional cleanup beyond `SimHostApp.cs`?

**Q4:** What design decisions did you make beyond the instructions? (e.g., test file naming,
whether to add tests to existing file vs. new file)

**Q5:** Suggest a git commit message for this batch.
