# FIX1-BATCH-01 — Phase 0: Kernel Prerequisites

## Tasks Covered
- **TASK-K-01** — Add `Lane` property to `[HsmAction]` attribute; ensure source generator preserves it.
- **TASK-K-02, TASK-K-03** — Add `stableId`/`visualId` parameters to HSM fluent builder methods; stamp into emitted definition blob.
- **TASK-K-05** — Add `Paused` flag check in `BTreeTickSystem.Execute` to halt progression on breakpoints.
- **TASK-K-06** — Add `Guid visualId = default` parameter to all BTree fluent builder composite/decorator methods.

## Onboarding

You are working on the `IOS-IG-SimHost-FDP-2` project. This batch fixes Phase 0 kernel-side
prerequisites required by the BTree and HSM AI editors. These fixes ensure the editors can
faithfully round-trip visual data.

**Mandatory read before coding:**
- `.dev/blueprints-2/FIX1-TASK-DETAIL.md` — "ACTION PACKET 1: Phase 0" and ACTION PACKET for K-05 & K-06.
- `.dev/blueprints-2/FIX1-TASK-EXTRA-DETAILS.md` — Step-by-step detailed fix instructions for K-01, K-02/K-03, K-05/K-06.
- `.dev/blueprints-2/ACCEPTANCE-CRITERIA.md` — Criteria F0-01 through F0-08.
- `.dev/blueprints-2/AI_Editor_Shared_Infrastructure.md` — Section on kernel prerequisites.
- `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/` — Existing HSM kernel code.
- `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/` — Existing BTree compiler code.

## Developer Insights Section

After implementing, answer the following in your report:
1. What issues were encountered during implementation (unexpected dependencies, missing types, etc.)?
2. What weak points did you spot in the existing codebase (e.g., missing null checks, unclear APIs)?
3. What design decisions did you make beyond the spec (e.g., where the spec was ambiguous)?

---

## Tasks

### TASK-K-01: `[HsmAction]` Lane Property & Source Generator

**Target files:**
- `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Attributes/HsmActionAttribute.cs`
- `FDP/Toolkits/Fdp.Toolkits.Analyzers/HsmActionGenerator.cs`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmOutputLaneMaskInferrer.cs`

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "ACTION PACKET: TASK-K-01 Detailed Fix Instructions" (Steps 1–4).

**Summary:**
1. Verify `HsmActionAttribute.cs` has `public CommandLane Lane { get; set; } = CommandLane.None;`
2. In `HsmActionGenerator.cs` → `GetMethodInfo`: extract the `Lane` named argument from the attribute and capture it in the local `MethodInfo` class.
3. In `EmitSharedAiActionThunk`: prepend `[global::Fhsm.Kernel.Attributes.HsmAction(Name = "...", Lane = ...)]` to every emitted thunk method.
4. Verify `HsmOutputLaneMaskInferrer.BuildLaneDictionary` reads `attr.Lane` and skips `CommandLane.None`.

**Acceptance criteria:** F0-01, F0-02.

**Tests required:**
- Add or update a test in `FDP/Toolkits/Fdp.Toolkits.Analyzers.Tests/` (or the existing HSM analyzer test project) that runs the source generator on a class with `[HsmAction(Lane = CommandLane.Fast)]` and asserts that the emitted thunk has `[HsmAction(..., Lane = (global::Fhsm.Kernel.Data.CommandLane)1)]` in its source text.
- Add a test for `HsmOutputLaneMaskInferrer` (or its test class if exists) that verifies a method with `Lane = CommandLane.Fast` appears in the output dictionary and `Lane = CommandLane.None` does not.

---

### TASK-K-02 & TASK-K-03: HSM Fluent Builder `stableId` / `visualId` Round-Trip

**Target files:**
- `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/MachineMetadata.cs`
- `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/HsmDefinitionBlob.cs`
- `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmEmitter.cs`
- `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmBuilder.cs` (and `StateBuilder.cs`, `TransitionBuilder.cs` if separate)
- `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/Graph/StateMachineGraph.cs`

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "ACTION PACKET: TASK-K-02 & TASK-K-03 Detailed Fix Instructions" (Steps 1–4).

**Summary:**
1. Add `Dictionary<ushort, Guid> StateStableIds` and `Dictionary<ushort, Guid> TransitionVisualIds` to `MachineMetadata`.
2. Add `MachineMetadata? Metadata { get; set; }` to `HsmDefinitionBlob`.
3. Update `HsmEmitter.BuildMachineMetadata` to populate both dictionaries following the exact same flattening order as `HsmFlattener`.
4. In `StateMachineGraph.Compile()`, call `blob.Metadata = HsmEmitter.BuildMachineMetadata(this)` before returning.
5. Ensure `HsmBuilder.State(name, Guid stableId = default)` and `StateBuilder.AddChild(name, Guid stableId = default)` pass the `stableId` down to the `StateNode` constructor. If `stableId == default`, assign `Guid.NewGuid()`.
6. Ensure `TransitionBuilder.GoTo(target, Guid visualId = default)` and `HsmBuilder.GlobalTransition(..., Guid visualId = default)` capture `visualId`. If `default`, assign `Guid.NewGuid()`.

**Acceptance criteria:** F0-03, F0-04, F0-05.

**Tests required:**
- Add a test in `FDP/ExtDeps/FastHSM/` (or existing test project) that:
  1. Builds a 2-state machine using the fluent API with explicit `stableId` Guids.
  2. Compiles it to a blob.
  3. Asserts `blob.Metadata.StateStableIds` contains the two expected Guids at the correct flat indices.
  4. Adds a transition with an explicit `visualId` and asserts it appears in `blob.Metadata.TransitionVisualIds`.
  5. Verifies that calling `State(name)` without a `stableId` still produces a non-default Guid in the metadata (auto-generated).

---

### TASK-K-05: BTree `Paused` Flag Check in `BTreeTickSystem`

**Target files:**
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BTreeTickSystem.cs`
- `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/BrainBTreeState.cs` (or wherever `BehaviorInstanceFlags` is checked)

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "ACTION PACKET: TASK-K-05 & TASK-K-06" → "Step 1: Enforce `Paused` Flag in `BTreeTickSystem`".

**Summary:**
Inside `BTreeTickSystem.Execute`, right before calling `def.BTreeInterpreter!.Tick(...)`, add:
```csharp
if ((btState.State.InstanceFlags & Fbt.BehaviorInstanceFlags.Paused) != 0)
    continue; // Entity is held by the debugger. Skip ticking.
```

**Acceptance criteria:** F0-08.

**Tests required:**
- Add a unit test that:
  1. Creates a minimal BTree definition and entity state with `InstanceFlags |= BehaviorInstanceFlags.Paused`.
  2. Calls `BTreeTickSystem.Execute` (or the relevant tick entry point).
  3. Asserts the `RunningNodeIndex` and `StackPointer` in `BrainBTreeState` are unchanged after the tick (the tree was not advanced).
  4. Clears the flag and ticks again; asserts the running node index advances (tree executes normally).

---

### TASK-K-06: BTree Fluent Builder `visualId` for All Composite/Decorator Nodes

**Target files:**
- `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeBuilder.cs`

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "ACTION PACKET: TASK-K-05 & TASK-K-06" → "Step 2 & 3".

**Summary:**
1. Add `Guid visualId = default` parameter (just before `[CallerFilePath]`) to the following methods: `Sequence`, `Selector`, `ObserverSelector`, `Parallel`, `Inverter`, `Repeater`, `ForceSuccess`, `ForceFailure`, `UntilSuccess`, `UntilFailure`.
2. Pass `visualId` into `BuildMeta(...)` in each of those methods.
3. Ensure `BuildMeta` stamps the Guid into `NodeDebugMetadata.VisualId` as `visualId.ToString("D")`. If `visualId == default`, generate a new `Guid.NewGuid()` and stamp that.

**Acceptance criteria:** F0-06.

**Tests required:**
- Add a test that:
  1. Builds a BTree with a `Sequence` node and passes an explicit `visualId` Guid.
  2. Compiles to a blob.
  3. Retrieves the `NodeDebugMetadata` for that composite node.
  4. Asserts `NodeDebugMetadata.VisualId` equals the originally passed Guid string.
  5. Builds another tree with `Sequence` and no explicit `visualId`; asserts the `VisualId` is a non-empty, non-zero Guid string (auto-generated).

---

## Mandatory Workflow: Test-Driven Task Progression

For every task:
1. **Read** the spec and acceptance criteria first.
2. **Write or update the test** before or alongside the implementation.
3. **Implement** the feature/fix.
4. **Run the tests** and confirm they pass (use `dotnet test` in the relevant project).
5. **Do not mark a task complete** unless its tests pass.

If a test fails and you cannot resolve it, document it clearly in the report. Do not remove or disable tests to make them pass.

Do not swallow exceptions silently. Let failures surface loudly.

---

## Build & Test Commands

```
# Build the kernel projects
cd FDP/ExtDeps/FastHSM
dotnet build

cd FDP/ExtDeps/FastBTree
dotnet build

# Run all FDP tests
cd FDP
dotnet test

# Or target specific test project
dotnet test FDP/Toolkits/Fdp.Toolkits.Analyzers.Tests/
```

---

## Report Format

Write your report to: `.dev/blueprints-2/reports/FIX1-BATCH-01-REPORT.md`

**Required sections:**
1. **Summary** — What was implemented.
2. **Task Status** — Per-task: Implemented / Partial / Blocked.
3. **Tests** — List of test methods added, and their pass/fail status.
4. **Developer Insights**:
   - Issues encountered
   - Weak points spotted in the codebase
   - Design decisions made beyond the spec
5. **Build Output** — Paste relevant `dotnet build` / `dotnet test` output (last 30 lines minimum).
