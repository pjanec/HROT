# BATCH-01: Kernel Prerequisites

**Batch Number:** BATCH-01  
**Tasks:** TASK-K-01, TASK-K-02, TASK-K-03, TASK-K-04, TASK-K-05, TASK-K-06  
**Phase:** Phase 0 — Kernel-side prerequisites  
**Estimated Effort:** 12-16 hours  
**Priority:** HIGH  
**Dependencies:** None (first batch)

---

## Mandatory Workflow

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **TASK-K-01:** Implement → Write tests → **ALL tests pass** ✅
2. **TASK-K-02:** Implement → Write tests → **ALL tests pass** ✅
3. **TASK-K-03:** Implement → Write tests → **ALL tests pass** ✅
4. **TASK-K-04:** Implement → Write tests → **ALL tests pass** ✅
5. **TASK-K-05:** Implement → Write tests → **ALL tests pass** ✅
6. **TASK-K-06:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- Current task implementation complete
- Current task tests written
- **ALL tests passing** (including all previous tests)

Do NOT stop and ask for permission to proceed with obvious next steps (running tests, fixing failures). Complete the entire batch and submit the report.

---

## Onboarding & Workflow

### Developer Instructions

This batch adds small, additive changes to the FastHSM and FastBTree kernel and compiler libraries so the AI editor can later faithfully round-trip asset source code. All changes are backwards-compatible: new parameters have default values, new flags default to clear.

The three outputs are:
- FastHSM attribute/builder additions (`TASK-K-01`, `K-02`, `K-03`, `K-04`)
- FastBTree pause flag (`TASK-K-05`)
- FastBTree builder completeness check (`TASK-K-06`)

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Task Definitions:** `.dev/blueprints-2/TASK-DETAIL.md` — Phase 0 section, TASK-K-01 through TASK-K-06
3. **HSM Design Spec (partial):** `.dev/blueprints-2/HSM_Editor_NodeEditor_Host_Design.md` — §1.4, §4.1 (stableId/visualId emit examples), §10.3–10.4 (Lane usage), §13.2 (pause semantics)
4. **BTree Design Spec (partial):** `.dev/blueprints-2/BTree_Editor_NodeEditor_Host_Design.md` — §4.1 (visualId emit), §12.2 (pause semantics), §17 open question #1
5. **Code Standards:** `.dev/.guides/CODE-STANDARDS.md`

### Source Code Locations

| What | Path |
|------|------|
| HSM attribute | `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Attributes/HsmActionAttribute.cs` |
| HSM InstanceFlags enum | `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/Enums.cs` |
| HSM InstanceHeader struct | `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/InstanceHeader.cs` |
| HSM RTC loop (Paused check goes here) | `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmKernelCore.cs` |
| HSM builder (State + transitions) | `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmBuilder.cs` |
| HSM graph nodes | `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/Graph/` |
| HSM tests | `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/` |
| BTree builder | `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeBuilder.cs` |
| BTree kernel runtime state | `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/BehaviorTreeState.cs` |
| BTree tests | `FDP/ExtDeps/FastBTree/tests/` |

### Build Commands

```powershell
# Build FastHSM
dotnet build FDP/ExtDeps/FastHSM/FastHSM.sln

# Test FastHSM
dotnet test FDP/ExtDeps/FastHSM/FastHSM.sln

# Build FastBTree
dotnet build FDP/ExtDeps/FastBTree/FastBTree.sln

# Test FastBTree
dotnet test FDP/ExtDeps/FastBTree/FastBTree.sln
```

### Report Submission

**When done, submit report to:**  
`.dev/blueprints-2/reports/BATCH-01-REPORT.md`

**If you have questions:**  
`.dev/blueprints-2/questions/BATCH-01-QUESTIONS.md`

---

## Context

Phase 0 is the prerequisite that unlocks faithful source-code round-tripping for both the HSM and BTree editors. Without stable Guid identities in the builder API, the editor cannot emit source that survives a compile-edit-compile cycle. Without the `Paused` flags the debug step-control UI (later in Phase 8–10) can't pause execution.

These are all additive changes with default values — zero risk of breaking existing code.

---

## Tasks

### Task 1: TASK-K-01 — Add `Lane` property to `[HsmAction]`

**File:** `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Attributes/HsmActionAttribute.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-k-01--add-lane-property-to-hsmaction)

**What to add:**
- A `CommandLane Lane { get; set; }` property on `HsmActionAttribute`
- Default value must be a sentinel meaning "no lane" (e.g., `CommandLane.None`, or whichever value is the "not set" value in the existing `CommandLane` enum)

**Requirements:**
- Find the `CommandLane` enum in the Hrot/FDP codebase (likely in Hrot.Common or Fdp.Core). The enum is used for AI output lane masking.
- The default must be the sentinel/unset value so existing `[HsmAction]` usages (without `Lane =`) compile unchanged and behave identically.
- Do NOT add any `CommandLane` enum here — it already exists; you're only adding the property to the attribute.

**Backwards compatibility:** ALL existing `[HsmAction]` usages must compile unchanged.

**Tests Required:**
- Verify that `[HsmAction]` without `Lane =` compiles and the default value is the sentinel
- Verify that `[HsmAction(Lane = CommandLane.SomeValue)]` compiles and the property is read correctly via reflection
- Verify that HSM attribute existing test suite still passes

---

### Task 2: TASK-K-02 — Add `stableId` parameter to `HsmBuilder.State()` and `StateBuilder.AddChild()`

**Files to update:**
- `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmBuilder.cs`
- `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/Graph/StateNode.cs` (if needed, but `StableId` is already there)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-k-02--add-stableid-parameter-to-hsmbuildersstate-and-statebuilderaddchild)

**What to add:**
- `HsmBuilder.State(string name, Guid stableId = default)` — when `stableId == default`, auto-generate via `Guid.NewGuid()`.
- `StateBuilder.Child(string childName, Action<StateBuilder> configure, Guid stableId = default)` — same default behavior.
- The `stableId` value must be passed down into the `StateNode.StableId` property (which already exists in `StateNode.cs`).
- Note: `StateNode` constructor already accepts `Guid? stableId` — wire it up from the builder.

**Requirements:**
- Existing calls like `builder.State("Idle")` must compile and run unchanged.
- Existing calls like `childBuilder.Child("Running", c => c.OnEntry(...))` must compile unchanged.
- When `stableId` is explicitly provided, the `StateNode.StableId` must equal that value after building.

**Tests Required:**
- `HsmBuilder_State_WithDefaultStableId_GeneratesRandomGuid`: Build a machine, get two states, verify their StableIds differ and are not `Guid.Empty`.
- `HsmBuilder_State_WithExplicitStableId_UsesProvidedValue`: Provide a specific `Guid`, verify the compiled `StateNode.StableId` equals it.
- `HsmBuilder_Child_WithExplicitStableId_UsesProvidedValue`: Same for a nested child state.
- Round-trip: Build a machine, emit to builder source (if HsmEmitter already supports stableId), re-build, verify same StableIds.

---

### Task 3: TASK-K-03 — Add `visualId` parameter to `TransitionBuilder.GoTo()` and `HsmBuilder.GlobalTransition()`

**Files to update:**
- `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmBuilder.cs`
- `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/Graph/TransitionNode.cs` (add `VisualId` property)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-k-03--add-visualid-parameter-to-transitionbuildergoto-and-hsmbuilderglobaltransition)

**What to add:**
- Add `Guid VisualId { get; set; }` to `TransitionNode`.
- `TransitionBuilder.GoTo(string targetStateName, Guid visualId = default)` — when default, auto-generate via `Guid.NewGuid()`.
- `TransitionBuilder.GoTo(StateBuilder target, Guid visualId = default)` — same.
- If `HsmBuilder` has a `GlobalTransition()` method (check if it exists), add `Guid visualId = default` there too. If it doesn't exist, create a minimal stub that adds a global transition to the graph (check if `StateMachineGraph` has a `GlobalTransitions` collection).

**Requirements:**
- Existing `TransitionBuilder.GoTo(...)` calls compile unchanged.
- When `visualId` is provided, the `TransitionNode.VisualId` must equal the provided value.

**Tests Required:**
- Same pattern as TASK-K-02 tests but for transitions.
- `TransitionBuilder_GoTo_WithDefaultVisualId_GeneratesRandomGuid`
- `TransitionBuilder_GoTo_WithExplicitVisualId_UsesProvidedValue`
- `HsmBuilder_GlobalTransition_WithExplicitVisualId` (if GlobalTransition method is added/exists)

---

### Task 4: TASK-K-04 — Add `Paused` flag to HSM `InstanceFlags`

**Files to update:**
- `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/Enums.cs` (add `Paused` to `InstanceFlags`)
- `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmKernelCore.cs` (respect `Paused` in RTC loop)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-k-04--add-paused-flag-to-hsm-instanceflags)

**What to add:**
- `Paused = 1 << 7` in `InstanceFlags` enum (currently `Reserved7` — replace it).
- In `HsmKernelCore` — in `ValidateInstance()` or at the start of `ProcessInstancePhase()` — add a check: if the `Paused` flag is set, skip processing for that instance (do not advance microsteps, do not process events, do not execute transitions). The instance should remain frozen until the flag is cleared.

**Requirements:**
- An instance with `Paused` set must not process events, not advance to next phase, not execute any transitions or actions.
- Clearing `Paused` must allow normal processing to resume on the very next tick.
- This is a **runtime flag**, not a persistent state — it's controlled by the debug session externally.

**Tests Required (write in `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/Kernel/`):**
- `PausedFlag_InstanceDoesNotAdvance_WhenPaused`: Set up an instance with a pending event; set `Paused` flag; tick; verify the state has NOT changed and no commands were output.
- `PausedFlag_InstanceResumes_WhenFlagCleared`: After the above, clear `Paused`; tick; verify the transition fires and state advances normally.
- `PausedFlag_DoesNotInterferWithDebugTrace`: An instance can have both `Paused` and `DebugTrace` set simultaneously without conflict.

---

### Task 5: TASK-K-05 — Add `Paused` flag to BTree instance state

**Files to update:**
- `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/BehaviorTreeState.cs` (or relevant file — inspect carefully)
- BTree tick/execution entry point (find where the tree is ticked per-instance)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-k-05--add-paused-flag-to-btree-instanceflags-or-equivalent)

**What to add:**
- The BTree kernel today does not have an `InstanceFlags` enum. Add either:
  - A `[Flags] enum BehaviorInstanceFlags : byte` with `Paused = 1 << 0` and store it in `BehaviorTreeState`, or  
  - Add a `bool IsPaused` bit flag in an available field of `BehaviorTreeState` (it has reserved bytes at offset 56–63 per the struct layout comment).
- In the BTree tick/execution path, check this flag early and skip execution if `Paused` is set.

**IMPORTANT:** `BehaviorTreeState` is a fixed-size struct laid out with `[StructLayout(LayoutKind.Explicit, Size = 64)]`. Any field you add must fit into the reserved space (offsets 56–63). Verify the layout before adding.

**Requirements:**
- Paused instance must not execute (tree returns immediately without advancing `RunningNodeIndex`).
- Clearing pause resumes from exactly where it stopped (no state loss).

**Tests Required:**
- `BTreePaused_TickDoesNotExecute_WhenPaused`
- `BTreePaused_ResumesFromSameState_WhenUnpaused`

---

### Task 6: TASK-K-06 — Verify `visualId` parameter on all BTree fluent methods

**File:** `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeBuilder.cs` (UPDATE if missing)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-k-06--visualid-parameter-on-btree-fluent-builder)

**What to verify/add:**

The following fluent methods MUST have `Guid visualId = default` as a parameter (with `Guid.NewGuid()` used when default):

- `.Sequence(...)` ✓ (present per current source)
- `.Selector(...)` ✓ (present per current source)
- `.ObserverSelector(...)` — check if it exists at all; if not, add it (Observer Selector is a special composite type used by the BTree editor)
- `.Parallel(...)` ✓ (present)
- `.Action(...)` ✓ (present)
- `.Condition(...)` ✓ (present)
- `.Wait(...)` ✓ (present)
- `.Subtree(...)` — check if it exists; add `visualId` if missing
- `.Inverter(...)` ✓ (present)
- `.Repeater(...)` ✓ (present)
- `.Cooldown(...)` ✓ (present)
- `.ForceSuccess(...)` — check if exists; add if missing with `visualId`
- `.ForceFailure(...)` — check if exists; add if missing with `visualId`
- `.UntilSuccess(...)` — check if exists; add if missing with `visualId`
- `.UntilFailure(...)` — check if exists; add if missing with `visualId`

For any missing method: add it as a decorator/composite following the same pattern as `Inverter`. For `ObserverSelector`: it is a composite type similar to Selector but uses the `NodeType.ObserverSelector` node type. Check if `NodeType` has this value; add it if not. For `Subtree`: it should call into a named subtree by reference.

**Requirements:**
- ALL methods must set `VisualId` on the produced debug metadata: `NodeDebugMetadata.VisualId` (check the metadata struct).
- When `visualId == default (Guid.Empty)`, generate a new `Guid.NewGuid()`.
- When explicit, use the provided value.

**Tests Required:**
- `BTreeBuilder_AllCompositeMethods_HaveVisualId`: Build a tree with all composite types; verify no VisualId in metadata is `Guid.Empty`.
- `BTreeBuilder_ExplicitVisualId_IsPreserved`: Set an explicit visualId on a Sequence, verify it round-trips through build.
- `BTreeBuilder_ObserverSelector_Exists`: Verify `ObserverSelector` is callable and produces a node.

---

## Testing Requirements

- All **new** tests in `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/` and `FDP/ExtDeps/FastBTree/tests/`
- Minimum: **15 new tests** across both projects
- ALL existing tests must still pass (zero regressions)
- Tests must verify ACTUAL behavior, not just "does it compile"

## Quality Standards

**NOT ACCEPTABLE:**
- Tests that only verify object construction
- Tests that just check property exists without verifying behavior (Paused not advancing, visualId round-trip, etc.)

**REQUIRED:**
- Tests that verify the Paused flag actually prevents execution
- Tests that verify explicit Guid values are preserved through the builder pipeline
- Tests that verify backwards compatibility (existing code works unchanged)

---

## Success Criteria

This batch is DONE when:

- [ ] TASK-K-01: `HsmActionAttribute.Lane` property added, defaults to sentinel, existing tests pass
- [ ] TASK-K-02: `HsmBuilder.State()` and `StateBuilder.Child()` accept optional `stableId`, wired to `StateNode.StableId`
- [ ] TASK-K-03: `TransitionBuilder.GoTo()` accepts optional `visualId`, wired to `TransitionNode.VisualId`
- [ ] TASK-K-04: `InstanceFlags.Paused` flag added; RTC loop skips paused instances; 3 tests pass
- [ ] TASK-K-05: BTree pause mechanism added to `BehaviorTreeState`; tick skips paused trees; 2 tests pass
- [ ] TASK-K-06: All 15 BTree builder methods have `visualId`; missing ones added; `ObserverSelector` exists
- [ ] ALL FastHSM tests pass: `dotnet test FDP/ExtDeps/FastHSM/FastHSM.sln`
- [ ] ALL FastBTree tests pass: `dotnet test FDP/ExtDeps/FastBTree/FastBTree.sln`
- [ ] Minimum 15 new tests written
- [ ] Report submitted at `.dev/blueprints-2/reports/BATCH-01-REPORT.md`

---

## Common Pitfalls

- `BehaviorTreeState` uses `[StructLayout(LayoutKind.Explicit, Size = 64)]` — any new field must be at an explicit `[FieldOffset(N)]` within the reserved bytes. Do NOT change the struct size.
- `InstanceFlags` is a `byte` enum — only 8 bits total. `Reserved7` is currently the last bit. Replace it with `Paused`.
- When adding missing BTree builder methods, use the same `[CallerFilePath]` / `[CallerLineNumber]` pattern as existing methods for debug metadata.
- `NodeDebugMetadata` — verify it has a `VisualId` field; if not, add one of type `Guid`.

---

## Developer Insights Report Template

Submit your report using the template at `.dev/.guides/BATCH-REPORT-TEMPLATE.md`.

**Questions to answer in your report:**

1. What was the trickiest part of adding the `Paused` flag to the HSM kernel? Were there any race conditions or ordering issues in `ValidateInstance`?
2. What missing BTree builder methods did you find in TASK-K-06? Were any of them more involved than a simple stub?
3. Did you find any weak points in the existing test coverage for the kernel code that you'd flag for improvement?
4. Were there any design decisions you made that weren't explicitly specified (e.g., exactly where in the RTC loop to check `Paused`)?
5. Any edge cases discovered during implementation?

---

## Reference Materials

- **Task Defs:** `.dev/blueprints-2/TASK-DETAIL.md` — Phase 0 tasks
- **HSM Design:** `.dev/blueprints-2/HSM_Editor_NodeEditor_Host_Design.md` — §1.4, §4.1, §10.3, §10.4, §13.2
- **BTree Design:** `.dev/blueprints-2/BTree_Editor_NodeEditor_Host_Design.md` — §4.1, §12.2, §17
