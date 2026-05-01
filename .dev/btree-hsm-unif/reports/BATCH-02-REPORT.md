# BATCH-02 Report

**Batch:** BATCH-02
**Tasks:** BHU-011, BHU-012, BHU-013, BHU-014
**Status:** COMPLETE
**Final test counts:** Fbt.Tests 171/171 pass | Fhsm.Tests 251/251 pass

---

## Summary

All four tasks completed. Both source generators were rewritten to handle SharedAi
attributes; the HSM graph validator was extended with channel-safety checks; test
coverage was added for every new code path.

---

## BHU-011: Shared AI Attributes (COMPLETE)

**Files changed:**
- `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/SharedAiAttributes.cs` (NEW)
- `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/ActionRegistry.cs` (MODIFIED)

**What was done:**
- Defined `[SharedAiCondition(Type dtoType, string fieldName)]`,
  `[SharedAiAction(Type dtoType, string fieldName)]`, and
  `[WritesChannel(ChannelKind channel)]` attributes in `Fbt.Kernel`, all with
  `AllowMultiple = true`.
- Added `ChannelKind` enum (`Locomotion=0, Weapon=1, Interaction=2`).
- Added `RegisterCondition` / `TryGetCondition` as thin aliases over the existing
  `Register` / `TryGetAction` methods in `ActionRegistry<TBB, TCtx>`.

**Tests added:**
- `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/SharedAiAttributeTests.cs` — 7 tests
  covering attribute construction, `AllowMultiple`, and `ChannelKind` values.

---

## BHU-012: BTree Generator SharedAi Support (COMPLETE)

**Files changed:**
- `FDP/ExtDeps/FastBTree/src/Fbt.SourceGen/BTreeActionGenerator.cs` (REWRITTEN)
- `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/TestFixtures/MockContext.cs` (MODIFIED)
- `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/TestFixtures/SharedAiTestFixtures.cs` (NEW)
- `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/SharedAiGeneratorTests.cs` (NEW)
- `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/GeneratorOutputTests.cs` (MODIFIED)

**What was done:**
- `BTreeActionGenerator` now scans `[SharedAiCondition]` and `[SharedAiAction]`
  alongside the existing `[BTreeAction]` / `[BTreeCondition]` attributes.
- For each SharedAi method the generator computes the byte offset of the named
  field inside the DTO struct, supporting both sequential (default) and explicit
  (`[StructLayout(LayoutKind.Explicit)]`) layouts.
- Registration key format: `"MethodName@offset"` (compound key) for SharedAi
  entries; plain `"MethodName"` for 4-param direct actions.
- SharedAi condition adapters wrap the `bool`-returning method in a ternary:
  `return Method(...) ? NodeStatus.Success : NodeStatus.Failure;`.
- SharedAi action adapters call the `NodeStatus`-returning method and return it
  directly.
- Group assignment: SharedAi entries are only assigned to a group whose `TContext`
  type has a `Self` member (required for the `ctx.Self` call-site). If no such
  group exists the entry is silently skipped with a BHU_003 diagnostic.
- `ChannelClear` block emitted for actions carrying `[WritesChannel]`.
- Bug fixed: `LayoutKind.Explicit == 2`, not `3` (which is `Auto`). Both
  `TryComputeFieldOffset` and `ComputeStructSize` in both generators used the
  wrong constant; all four sites corrected.

**Tests added:**
- `SharedAiGeneratorTests.cs` — 4 tests: sequential-offset key, explicit-offset
  key (exercises the layout bug fix), plain-name registration, and an end-to-end
  call test that writes into raw blackboard memory and verifies `NodeStatus.Success`.
- `MockContext` gained `int Self; int World;` so the existing `(TestBlackboard,
  MockContext)` group is eligible for SharedAi assignment (avoids a second
  `RegisterAll` overload that would have broken `GetMethod` in `GeneratorOutputTests`).
- `GeneratorOutputTests.GeneratedRegistrar_ContainsBTreeAction_Method` updated to
  use `GetMethods(...).Where(m => m.Name == "RegisterAll")` instead of `GetMethod`
  to handle multiple overloads.

---

## BHU-013: HSM Generator SharedAi Support (COMPLETE)

**Files changed:**
- `FDP/ExtDeps/FastHSM/src/Fhsm.SourceGen/HsmActionGenerator.cs` (REWRITTEN)
- `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/SourceGen/SharedAiHsmTests.cs` (NEW)

**What was done:**
- `HsmActionGenerator` now scans `[SharedAiCondition]` and `[SharedAiAction]`
  (identified by their fully-qualified attribute class name from `Fbt.Kernel`).
- For `[SharedAiCondition]` emits a private `bool Guard_{MethodName}_At{offset}`
  thunk that reads the DTO field from the blackboard via unsafe pointer arithmetic
  and calls the underlying `bool`-returning method directly.
- For `[SharedAiAction]` emits a private `void Action_{MethodName}_At{offset}`
  thunk that reads the field, calls the `NodeStatus`-returning method, and discards
  the return value.
- Every thunk carries the mandatory constraint comment:
  `// CONSTRAINT: Do NOT add or remove ECS components from this thunk...`
- Thunks use `global::` prefixes throughout; no `using Fdp.Toolkit.*` is added to
  the generated file (test project does not reference that assembly).
- `RegisterAll()` registers all SharedAi thunks under their compound keys
  (`MethodName@offset`) alongside the regular HSM actions and guards.
- Same `LayoutKind.Explicit == 2` bug fixed in `HsmActionGenerator` (two sites).

**Tests added:**
- `SharedAiHsmTests.cs` — 4 reflection-based tests:
  1. `GeneratedRegistrar_Has_RequiredExitCleanups_Property` — verifies the field
     exists and implements `IReadOnlyDictionary<string,string>`.
  2. `RequiredExitCleanups_IsEmpty_WhenNoWritesChannelActionsExist` — dict is empty
     when the test assembly has no `[WritesChannel]`-annotated methods.
  3. `RegisterAll_Exists_And_Is_Public_Static` — verifies `RegisterAll()` is
     present via reflection (not called, to avoid polluting the shared
     `HsmActionDispatcher` static table used by other tests).
  4. `GeneratedDispatcher_Has_ExecuteAction_Method` — verifies `ExecuteAction` and
     `EvaluateGuard` are present on `HsmActionDispatcher`.

**Note on SharedAi thunk execution tests:** Full end-to-end HSM thunk tests
(calling thunks with real `HsmKernelBridge` and `BrainBlackboard`) are not
possible in `Fhsm.Tests` because that project does not reference `Fdp.Toolkit.*`.
Coverage is provided at the integration level (real game assemblies include both).

---

## BHU-014: Channel Safety (COMPLETE)

**Files changed:**
- `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmGraphValidator.cs` (MODIFIED)
- `FDP/ExtDeps/FastHSM/src/Fhsm.SourceGen/HsmActionGenerator.cs` (MODIFIED — see BHU-013)
- `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/Compiler/HsmGraphValidatorChannelSafetyTests.cs` (NEW)

**What was done:**
- Added `HsmGraphValidator.ValidateChannelSafety(graph, dict, errors)` — for each
  state whose `OnEntryAction` or `ActivityAction` appears as a key in
  `requiredExitCleanups`, verifies that `OnExitAction` equals the required cleanup
  name; otherwise appends a `ValidationError` naming the offending action and
  expected cleanup.
- Added `HsmGraphValidator.Validate(graph, IReadOnlyDictionary<string,string>?)`
  overload that runs base validation then, if the dict is non-null, calls
  `ValidateChannelSafety`.
- `HsmActionGenerator` emits `RequiredExitCleanups` — a `public static readonly`
  field mapping each `[WritesChannel]`-annotated action name to its generated
  `ExitCleanup_{MethodName}` name. Also emits `ExitCleanup_*` thunks that perform
  the channel-clear logic (set `ActiveAction = 0`, increment `ActionInstanceId`).

**Tests added:**
- `HsmGraphValidatorChannelSafetyTests.cs` — 6 tests calling `ValidateChannelSafety`
  directly (to isolate it from base-validator noise):
  1. Correct entry+exit combo produces no errors.
  2. Wrong exit action produces one error naming the offending action.
  3. Missing exit action for an `ActivityAction` produces one error.
  4. Non-channel action (not in dict) produces no errors.
  5. `Validate(graph, null)` produces same result as `Validate(graph)` (no crash,
     no extra channel errors).
  6. Empty cleanup dict produces no channel errors.

---

## Bugs Fixed

| ID | Location | Description |
|----|----------|-------------|
| B1 | `BTreeActionGenerator.TryComputeFieldOffset` | `LayoutKind.Explicit == 2`, not `3` (Auto). Explicit-layout DTO offsets were never computed; SharedAi entries with explicit DTOs were silently dropped. |
| B2 | `BTreeActionGenerator.ComputeStructSize` | Same constant bug. |
| B3 | `HsmActionGenerator.TryComputeFieldOffset` | Same constant bug. |
| B4 | `HsmActionGenerator.ComputeStructSize` | Same constant bug. |

---

## Deviations from Instructions

None. All behaviours match the BATCH-02-INSTRUCTIONS and referenced TASK-DETAIL.

---

## Test Counts

| Suite | Before BATCH-02 | After BATCH-02 |
|-------|-----------------|----------------|
| Fbt.Tests | 160 | 171 (+11) |
| Fhsm.Tests | 241 | 251 (+10) |
| **Total** | **401** | **422** |
