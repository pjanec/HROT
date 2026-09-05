# BATCH-14 — Emit cycle guard: cyclic tree → diagnostic, not StackOverflow (CRITICAL)

**Task:** TASK-BT-14 (Fix-A2 #1). **One objective.**

## 🔒 Working agreement (MANDATORY)
One task; **NO cheating** (no excluding files / suppressing diagnostics / weakening tests); **finish without asking** until build clean + `Failed: 0`; tests assert real values; litter-free; report = diffs.

## 📋 Onboarding
- Report → `.dev/_DONE/ai-hsm-btree-vis-edit-2/reports/BATCH-14-REPORT.md`.
- Context: `BTreeEmitCore` walks `node.ChildVisualIds` recursively (`EmitCreateBuilder` → `EmitNode`/`EmitComposite` → `EmitChildNode` → …) with **no cycle/visited guard** ([BTreeEmitCore.cs:296-300](../../Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeEmitCore.cs#L296)). A cyclic node graph (A→B→A — which the canvas can currently create) → infinite recursion → **`StackOverflowException`**. A stack overflow is **uncatchable** in .NET, so it crashes the whole Roslyn/MSBuild/VS process — the generator's existing try/catch and BT-12 **cannot** intercept it. This must become a normal, catchable exception so the generator skips the asset + emits BTREE0002 (build survives).

## 🎯 Objective
Detect a cycle **before** the recursive emit walk and **throw a normal `InvalidOperationException`** (which `BTreeJsonGenerator` already catches → BTREE0002 Warning → asset skipped → build survives). No stack overflow ever.

## File (exact)
`Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeEmitCore.cs`:
1. Add a private static `CheckNoCycles(BehaviorTreeAssetDto dto, Dictionary<Guid,BTreeNodeDto> nodeById, BTreeNodeDto entry)` that does an iterative or recursive **DFS over `ChildVisualIds` starting at `entry`, with a path-visited `HashSet<Guid>`**. If a node is encountered that is already on the current DFS path (a back-edge) → `throw new InvalidOperationException($"Cycle detected in BTree topology at node {id:D} — a node cannot be its own ancestor. Fix the wiring in the editor.");`. (Use a per-path set: add on enter, remove on leave — OR pass the current ancestor set down recursively. Missing child ids in `nodeById` are simply skipped, as the emit walk already does.)
2. Call `CheckNoCycles(...)` inside `EmitCreateBuilder` **immediately after the entry node is determined** (the `root != null` branch → check from the entry child; the no-root branch → check from `dto.Nodes[0]`), **before** the `EmitNode(...)` walk. Use the SAME entry-node selection that `EmitCreateBuilder` already uses (lines ~197-210) so the check covers exactly the subgraph that gets emitted.
3. Do NOT change the emit walk's output for valid (acyclic) trees — only add the pre-pass guard.

## 🧪 Tests
**Emit-core unit tests** (`Hrot.AiEditor.Persistence.Tests`, e.g. extend `BTreeEmitCoreValidationTests`):
- `EmitTopologyCore_CyclicTree_ThrowsInvalidOperationException_NotStackOverflow`: build a DTO with Root→A(Sequence)→B(Sequence)→A (A's ChildVisualIds contains B, B's contains A) → `BTreeEmitCore.EmitTopologyCore(dto)` throws `InvalidOperationException` (assert the throw; it must return/throw quickly, NOT hang/overflow).
- `EmitTopologyCore_SelfChild_Throws`: a composite whose `ChildVisualIds` contains its own VisualId → throws.
- `EmitTopologyCore_AcyclicTree_DoesNotThrow`: a normal Root→Sequence→(Wait, Action-bound) tree → no throw, normal output.
- `EmitTopologyCore_DiamondNotACycle`: (only if your model allows a node id appearing under two parents in the same DTO) — a DAG where a node is referenced twice but NO back-edge on any single path → does NOT throw. (If single-parent makes this impossible, skip this case and note it.)

**Generator test** (`Hrot.AiEditor.Generators.Tests`, mirror BATCH-12's unbound tests):
- `Generator_CyclicAsset_DoesNotEmitSource_AndReportsWarning_NoErrors`: a cyclic `.btree.json` → generator emits no source for it, reports **BTREE0002 Warning**, and the run has **zero Error diagnostics** (build survives). Also assert a valid sibling asset still emits (fault isolation).

## ✅ Success criteria
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 new warnings (committed assets are acyclic → no BTREE0002 fires).
- [ ] `Failed: 0` in `Hrot.AiEditor.Persistence.Tests`, `Hrot.AiEditor.Generators.Tests`, `Hrot.BTree.Editor.Tests` (pre-existing Generators.Tests MigrationEquivalence ×2 may remain — list them).
- [ ] A cyclic tree → `InvalidOperationException` (NOT stack overflow); generator → BTREE0002 + skip + no Error.
- [ ] Acyclic trees unchanged.
- [ ] Report written.

## Notes
- The guard must run BEFORE the recursive emit (a pre-pass), so the overflow never starts.
- Throw a NORMAL exception (`InvalidOperationException`) — do NOT try to catch StackOverflowException (it's uncatchable; the point is to never recurse into one).
- This is defense-in-depth; BATCH-15 (single-parent enforcement) will stop cycles being created in the first place.
