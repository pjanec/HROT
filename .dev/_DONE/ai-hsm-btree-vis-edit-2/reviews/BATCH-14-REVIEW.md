# BATCH-14 Review — TASK-BT-14 Emit cycle guard (CRITICAL)

**Reviewer:** Dev Lead · **Date:** 2026-06-12 · **Status:** ✅ APPROVED

## Verification (independent)
- `BTreeEmitCore.CheckNoCycles`/`DfsCheckNoCycles`: path-visited DFS — `pathSet.Add` returns false on a back-edge → `InvalidOperationException`; `pathSet.Remove` on leave → DAGs/shared children correctly allowed (only true back-edges throw). Called at both entry sites in `EmitCreateBuilder` **before** the recursive walk. Algorithm is textbook-correct.
- Generator already catches the throw → BTREE0002 Warning → asset skipped → build survives (no stack overflow). A `StackOverflowException` is uncatchable, so the pre-pass guard (never recurse into the overflow) is the right approach.
- Tests: cyclic → throws (not overflow), self-child → throws, acyclic → ok, **diamond DAG → ok** (proves no false positive); generator cyclic → BTREE0002 + skip + zero Error + valid sibling still emits.
- Independent re-run: Persistence.Tests **123/0**, BTree.Editor.Tests **493/0**, Generators.Tests **46/2** (2 = pre-existing MigrationEquivalence, verified pre-existing in BATCH-09).

## Issues
None.

## Verdict
APPROVED. Cyclic topology is now a diagnostic, not an uncatchable crash. BATCH-15 (single-parent enforcement) stops cycles being created in the first place.

## Commit message
```
fix(btree-editor)!: cycle guard in codegen — cyclic tree → diagnostic, not StackOverflow (BATCH-14 / TASK-BT-14)

A cyclic BTree node graph made BTreeEmitCore recurse infinitely
(EmitComposite↔EmitChildNode) → uncatchable StackOverflowException → crashed
the Roslyn/MSBuild/VS process. Add a path-visited DFS pre-pass (CheckNoCycles)
that throws InvalidOperationException before the recursive emit; the generator
catches it → BTREE0002 warning → asset skipped → build survives. DAGs are
correctly allowed. +emit-core + generator tests.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
