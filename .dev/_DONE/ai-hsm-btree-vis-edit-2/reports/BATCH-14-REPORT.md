# BATCH-14 REPORT — Emit cycle guard: cyclic tree → diagnostic, not StackOverflow

**Date:** 2026-06-12
**Task:** TASK-BT-14 (Fix-A2 #1)
**Branch:** blueprint-integ-1

## Summary

Added a DFS pre-pass cycle detector (`CheckNoCycles`) to `BTreeEmitCore` that throws a normal `InvalidOperationException` BEFORE the recursive emit walk, preventing an uncatchable `StackOverflowException` when the DTO contains a cyclic node graph. The generator's existing catch → BTREE0002 Warning path now handles cycles safely — asset skipped, build survives.

## Changes

### 1. `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeEmitCore.cs`

- Added `CheckNoCycles(BehaviorTreeAssetDto dto, Dictionary<Guid,BTreeNodeDto> nodeById, BTreeNodeDto entry)` — entry point that creates a path-visited `HashSet<Guid>` and delegates to the recursive DFS.
- Added `DfsCheckNoCycles(BTreeNodeDto node, Dictionary<Guid,BTreeNodeDto> nodeById, HashSet<Guid> pathSet)` — recursive DFS over `ChildVisualIds`. Adds node on enter, removes on leave (path-visited set). If a node is encountered that is already on the current DFS path (back-edge) → throws `InvalidOperationException` with a descriptive message. Missing child IDs in `nodeById` are silently skipped (matching the emit walk's behavior).
- Two call sites in `EmitCreateBuilder`, both BEFORE the corresponding `EmitNode` calls:
  - `root != null` branch: `CheckNoCycles(dto, nodeById, entryChild)` after `entryChild` is determined via `TryGetValue`.
  - No-root branch: `CheckNoCycles(dto, nodeById, dto.Nodes[0])` before `EmitNode`.

### 2. `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence.Tests/Emit/BTreeEmitCoreValidationTests.cs`

- `EmitTopologyCore_CyclicTree_ThrowsInvalidOperationException_NotStackOverflow` — Root→A(Sequence)→B(Sequence)→A. Asserts `InvalidOperationException` with "Cycle detected", verifies it's catchable (not StackOverflow).
- `EmitTopologyCore_SelfChild_Throws` — Sequence whose `ChildVisualIds` contains its own ID. Asserts throw.
- `EmitTopologyCore_CyclicTree_NoRoot_ThrowsInvalidOperationException` — No Root node; entry = `dto.Nodes[0]`. A→B→A. Asserts throw.
- `EmitTopologyCore_AcyclicTree_DoesNotThrow` — Normal Root→Sequence→(Wait, Action) tree. Asserts no throw and output contains CreateBuilder().
- `EmitTopologyCore_DiamondNotACycle_DoesNotThrow` — Root→A→(B, C)→D (D shared from two parents, DAG, no back-edges). Asserts no throw and output produced.

### 3. `Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Generator/BTreeJsonGeneratorTests.cs`

- `Generator_CyclicAsset_DoesNotEmitSource_AndReportsWarning_NoErrors` — Cyclic .btree.json → no sources emitted, single BTREE0002 Warning, zero Error diagnostics.
- `Generator_CyclicAsset_DoesNotSuppressSiblingValidAsset` — Valid + cyclic assets together → valid still emits 2 files, cyclic reports single BTREE0002, no BTREE0001 errors.
- `BuildCyclicTreeJson()` helper — constructs a cyclic DTO (Root→A→B→A) and serializes to JSON.

## Test results

| Project | Passed | Failed | Notes |
|---------|--------|--------|-------|
| Hrot.AiEditor.Persistence.Tests | 123 | 0 | Includes 5 new cycle-detection tests |
| Hrot.AiEditor.Generators.Tests | 46 | 2 | 2 new cyclic generator tests pass; 2 pre-existing |
| Hrot.BTree.Editor.Tests | 493 | 0 | Unchanged |

### Pre-existing failures (allowed per instructions)

| Test | Status |
|------|--------|
| `MigrationEquivalenceTests.Hsm_SampleGuard_MigrationJson_RoundTrips_And_CarriesLayout` | FAIL (pre-existing) |
| `MigrationEquivalenceTests.BTree_SampleScout_MigrationJson_RoundTrips_And_CarriesLayout` | FAIL (pre-existing) |

## Build

- `dotnet build IOS-IG-SimHost.sln` — **0 errors, 0 new warnings** (only pre-existing warnings: NU1903, CS0618, xUnit2013, CS8601/CS8602).
- Committed assets are acyclic → no BTREE0002 fires.

## Design notes

- **Path-visited DFS** (add on enter, remove on leave) correctly distinguishes cycles (back-edges) from DAGs (shared children via different paths). The `EmitTopologyCore_DiamondNotACycle_DoesNotThrow` test proves this.
- The check runs **before** the recursive emit — the overflow never starts.
- Uses the **same entry-node selection** as `EmitCreateBuilder` (Root child or `dto.Nodes[0]`), covering exactly the emitted subgraph.
- Throws `InvalidOperationException` (normal, catchable) — `BTreeJsonGenerator.GenerateOneAsset` already has a `catch (Exception ex)` around `EmitTopologyCore` that converts it to a BTREE0002 Warning.
- This is defense-in-depth; BATCH-15 (single-parent enforcement) will prevent cycles from being created in the canvas editor.
