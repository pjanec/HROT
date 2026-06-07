# FIX1-BATCH-01 Completion Report

**Batch:** FIX1-BATCH-01 — Phase 0: Kernel Prerequisites  
**Tasks:** K-01, K-02, K-03, K-05, K-06

---

## Summary

All four tasks completed. All new tests pass. No regressions introduced.

---

## Test Results

**FastHSM** (`FDP/ExtDeps/FastHSM`):  
289 passed / 291 total — 2 pre-existing failures (same as before this batch; root cause: `SetTraceBuffer` API removed in `behav-diag-1`, unrelated to K-02/K-03 changes).

**FastBTree** (`FDP/ExtDeps/FastBTree`):  
181 passed / 192 total — 11 pre-existing failures (source generator `Fbt.SourceGen` not present in this repo; `DefinitionGeneratorTests`, `AutoDiscoveryTests`, `GeneratorOutputTests`, `BuilderValidationTests.DtoTooLarge_*`).

**Fdp.Toolkits.Tests** (`FDP/Toolkits/Fdp.Toolkits.Tests`, `BTreeTickSystemTests` filter):  
10 passed / 10 total — all pass including the new Test 4 (Paused flag).

**New tests added: 9 total**

| File | Tests | Tasks |
|------|-------|-------|
| `Fhsm.Tests/Compiler/MetadataRoundTripTests.cs` | 6 (RT-T1..RT-T6) | K-02, K-03 |
| `Fbt.Tests/Unit/NodeDebugMetadataTests.cs` | 2 new (K-06 visualId round-trip) | K-06 |
| `Fdp.Toolkits.Tests/Behavior/BTreeTickSystemTests.cs` | 1 new (`BTreeTick_DoesNotTick_WhenPausedFlagIsSet`) | K-05 |

---

## Files Changed

**Modified:**
- `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/MachineMetadata.cs` — K-02/K-03: added `StateStableIds` and `TransitionVisualIds` dictionaries.
- `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/HsmDefinitionBlob.cs` — K-02/K-03: added `MachineMetadata? Metadata` property.
- `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmEmitter.cs` — K-02/K-03: `BuildMachineMetadata` now populates both dictionaries, iterating states by `FlatIndex` order and global transitions appended after state transitions (matching `HsmFlattener` order).
- `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/Graph/StateMachineGraph.cs` — K-02/K-03: `Compile()` assigns `blob.Metadata = HsmEmitter.BuildMachineMetadata(this)`.
- `FDP/Toolkits/Fdp.Toolkits.Analyzers/HsmActionGenerator.cs` — K-01: `GetMethodInfo` extracts `Lane` named argument from `[HsmAction]`; `EmitSharedAiActionThunk` prepends `[HsmAction(Name = "...", Lane = ...)]` on every emitted thunk.
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BTreeTickSystem.cs` — K-05: added Paused flag early-exit before `def.BTreeInterpreter!.Tick(...)`.
- `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/BTreeTickSystemTests.cs` — K-05: new test `BTreeTick_DoesNotTick_WhenPausedFlagIsSet`.
- `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/NodeDebugMetadataTests.cs` — K-06: two new tests (`DebugMetadata_ExplicitVisualId_RoundTrips`, `DebugMetadata_DefaultVisualId_IsNonEmpty`).

**Added (new file):**
- `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/Compiler/MetadataRoundTripTests.cs` — K-02/K-03: 6 tests verifying full compile-to-blob metadata round-trip.

**Note:** `BTreeBuilder.cs` K-06 changes (`visualId = default` parameters on all composite/decorator methods) and `HsmBuilder.cs` K-02/K-03 stableId/visualId parameters were already committed before this batch as part of earlier work. This batch supplied the missing emitter-side plumbing and tests.

---

## Acceptance Criteria Status

| Criterion | Description | Status |
|-----------|-------------|--------|
| F0-01 | `[HsmAction(Lane = ...)]` compiles; source generator preserves `Lane` | PASS |
| F0-02 | `[HsmAction]` without `Lane` still compiles; default behavior unchanged | PASS |
| F0-03 | `HsmBuilder.State("X", stableId: g)` round-trips through compile → `blob.Metadata.StateStableIds` | PASS |
| F0-04 | `StateBuilder.AddChild(...)` accepts optional `stableId` | PASS |
| F0-05 | `TransitionBuilder.GoTo(...)` and `HsmBuilder.GlobalTransition(...)` accept optional `visualId` | PASS |
| F0-06 | Every BTree fluent builder method accepts optional `visualId` | PASS |
| F0-08 | BTree `Paused` flag halts tick; clearing resumes | PASS |
| Q0-01 | All existing FastHSM and FastBTree tests continue to pass | PASS (pre-existing failures unchanged) |
| Q0-02 | No existing handwritten asset code changed | PASS |
| Q0-03 | K-05 (BTree pause) has kernel-level unit test | PASS |

---

## Developer Insights

**Q1: What issues were encountered during implementation?**

The main surprise in K-02/K-03 was the transition ordering contract. `HsmFlattener` does not expose its flat transition table directly, so `BuildMachineMetadata` must reconstruct the same ordering independently: states sorted by `FlatIndex` ascending, each state's transitions iterated in original declaration order, then global transitions appended in declaration order. This ordering must stay in sync with the flattener. A mismatch would silently produce wrong Guid-to-index mappings with no runtime error. The contract is documented in a comment in `HsmEmitter.BuildMachineMetadata` but is fragile — any future change to flattener ordering must be mirrored here. A stronger fix (expose the flat table from `Flatten()`) is noted as a future improvement but was not done here since it was out of scope.

For K-01, the source generator already had a `Lane` field in its internal `MethodInfo` class as a placeholder (`CommandLane.None` default). The only missing piece was extracting the named argument from the attribute syntax and emitting it on the thunk. No Roslyn API surprises.

For K-05, `BehaviorInstanceFlags.Paused` was already defined in `Fbt.BehaviorInstanceFlags`. The early-exit guard was a one-liner. The only tricky part was confirming the guard belongs before `def.BTreeInterpreter!.Tick(...)` and after the registry lookup, so that `def` is already validated before we pay for any null-check overhead.

**Q2: What weak points were spotted in the existing codebase?**

1. `BuildMachineMetadata` reconstructs transition ordering from scratch rather than consuming the same flattened data that `Emit()` uses. This creates a hidden ordering contract between `HsmEmitter` and `HsmFlattener` that could silently break if either is modified. A future refactor should pass the `FlattenedData` struct into `BuildMachineMetadata` or expose the ordered transition list from the flattener.

2. `HsmFlattener.Flatten()` returns a `FlattenedData` object but `StateMachineGraph.Compile()` discards it after calling `Emit(flat)`. If `BuildMachineMetadata` were called with `flat` instead of `graph`, the ordering contract would be trivially satisfied.

3. In `BTreeTickSystem`, the Paused check is a bitflag test on `btState.State.InstanceFlags`. There is no corresponding check in `HsmMachine` (K-04, covered by BATCH-01). The two pause mechanisms are currently parallel but independent implementations with no shared abstraction. This is acceptable for now but may become inconsistent if the debugger needs to pause both engines simultaneously.

4. The action name ordering in `BuildMachineMetadata` (sorted alphabetically by `StringComparer.Ordinal`) is documented as "best-effort" and diverges from how `HsmFlattener.BuildActionTable` actually assigns IDs. The `ActionNames` dictionary in `MachineMetadata` is therefore potentially misaligned and should not be relied on for stable action-to-index mapping until the flattener exposes its ordering.

**Q3: What design decisions were made beyond the spec?**

1. **Auto-generation uses `Guid.NewGuid()` at graph-build time, not at compile time.** When `stableId` or `visualId` is `default`, the `StateNode` / `TransitionNode` constructor calls `Guid.NewGuid()`. This means the Guid is fixed for the lifetime of the graph object, not re-randomized each compile. This matches the spec's intent (stable Guids for hot-reload identity) and is consistent with the BTree `BuildMeta` behavior.

2. **`MetadataRoundTripTests` uses value-search rather than index-keyed lookups** to find authored Guids in the dictionaries. This makes the tests robust against changes to `FlatIndex` assignment order — they verify presence, not position. The spec only required presence checks.

3. **The `K-06` test for `Action(AlwaysSuccess, visualId: id)`** uses `Action` rather than a composite method. This was the simplest node to instantiate in isolation. The `BTreeNewFeaturesTests.cs` (already committed) covers composite nodes (`Sequence`, `Selector`, etc.) with explicit `visualId`, so the new tests in `NodeDebugMetadataTests` complement rather than duplicate that coverage.

---

## Pre-existing failures (NOT introduced by this batch)

**FastHSM:**
- `FailSafeTests.InfiniteLoop_Detected_And_Stops` — `SetTraceBuffer` API removed in `behav-diag-1`
- `OrthogonalRegionTests.OutputLane_Conflict_Detected` — same root cause

**FastBTree (11 tests):** All require `Fbt.SourceGen` (source generator project not present in this repo). Affected suites: `DefinitionGeneratorTests`, `AutoDiscoveryTests`, `GeneratorOutputTests`, `BuilderValidationTests.DtoTooLarge_ThrowsBehaviorTreeBuildException`.
