# BATCH-01 Report

**Batch:** BATCH-01  
**Developer:** GitHub Copilot (Claude Sonnet 4.6)  
**Date:** 2025-07-17  
**Status:** Complete

---

## Summary

Implemented the complete Phase 1 deactivator infrastructure in `Fbt.Kernel`: the
`NodeDeactivatorDelegate` type, `BTreeDeactivatorAttribute`, `ActionRegistry` deactivator
support, and the `Interpreter` delta-tracking sweep with Parallel subtree handling and
hot-reload ordering. All 10 `HybridLifecycleTests` success conditions pass; the 181
pre-existing tests are unaffected.

---

## Tasks Completed

- [x] TASK-EQL-001 — NodeDeactivatorDelegate and BTreeDeactivatorAttribute
- [x] TASK-EQL-002 — ActionRegistry deactivator support
- [x] TASK-EQL-003 — Interpreter deactivator array and delta tracking

---

## Test Results

**Command used** (solution-level test is broken due to missing `Fbt.SourceGen.csproj` — see
Issues Encountered):

```
dotnet test FDP\ExtDeps\FastBTree\tests\Fbt.Tests\Fbt.Tests.csproj --no-build
```

**Full suite (all tests):**

```
Failed!  - Failed: 11, Passed: 191, Skipped: 0, Total: 202
```

The 11 failures are pre-existing; all belong to `DefinitionGeneratorTests`,
`GeneratorOutputTests`, and `AutoDiscoveryTests` which require the missing
`Fbt.SourceGen` source-generator project. These failures were present before this
batch started and are unrelated to Phase 1.

**New `HybridLifecycleTests` only:**

```
Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10, Duration: 83 ms
```

All 10 success conditions (T1–T10) pass.

---

## Files Changed

| File | Change |
|------|--------|
| `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/NodeDeactivatorDelegate.cs` | **Created** — delegate type `NodeDeactivatorDelegate<TBlackboard, TContext>`, void-returning, mirroring `NodeLogicDelegate` signature. |
| `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Attributes/BTreeDeactivatorAttribute.cs` | **Created** — sealed attribute with single `string targetAction` constructor; `AllowMultiple=false`, `Inherited=false`. |
| `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/ActionRegistry.cs` | **Modified** — added `_deactivators` dictionary, `RegisterDeactivator`, `TryGetDeactivator` methods. |
| `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs` | **Modified** — added `_deactivatorDelegates` field, `BindDeactivators`, `InvokeDeactivatorIfRegistered`, `SweepParallelChildren`, updated `Tick` with full 4-step delta-tracking flow. |
| `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/HybridLifecycleTests.cs` | **Created** — 10 test methods covering T1–T10. |

---

## Developer Insights

### Issues Encountered

**1. Solution-level `dotnet test FastBTree.sln` is broken.**  
`Fbt.SourceGen.csproj` does not exist on disk but is referenced from both example projects
and the test project. A solution-level `dotnet restore` or `dotnet test` treats the missing
project as a hard error. The fix: use
`dotnet test FDP\ExtDeps\FastBTree\tests\Fbt.Tests\Fbt.Tests.csproj` directly, which
skips the missing project (as noted in `ONBOARDING.md`). The batch instructions reference
`FastBTree.sln` for the test command — this is misleading and will fail for any new developer
following the instructions literally.

**2. `BehaviorTreeState.NodeIndexStack` requires `unsafe` context.**  
The `fixed ushort NodeIndexStack[8]` field can only be read/written inside an `unsafe {}`
block. In test T10, this meant wrapping the manual state setup with `unsafe { state.NodeIndexStack[0] = 1; }`. The `Interpreter.Tick` already does this for the pre-tick snapshot; there is no API to read the stack without going unsafe.

**3. ObserverSelector uses standard selector resume semantics.**  
`NodeType.ObserverSelector` maps to `ExecuteSelector` in the interpreter (comment says
"uses standard selector semantics"). This means higher-priority conditions are NOT
re-evaluated every tick once a lower-priority branch is running. Initial designs for T2
and T8 assumed the condition would be re-observed on Tick 1, which it is not. Tests were
redesigned to work within the actual semantics: T2 uses a Sequence handoff (A completes
via Success, B takes over), and T8 uses a leaf action that returns Failure to cause the
Selector to switch branches.

**4. T10 hot-reload deactivator timing.**  
The deactivator for a node in `NodeIndexStack[0]` fires inside `SweepExitedNodes`
BEFORE `state.RunningNodeIndex` is reset to zero. This ordering is intentional per the
spec (`pathWasReset` flag, step 2 before step reset). The test captures
`st.RunningNodeIndex` inside the deactivator lambda and asserts it equals the OOB value
(the pre-reset state), verifying the ordering.

**5. `childStatesBits = 0` after Parallel exits.**  
`ExecuteParallel` clears `state.LocalRegisters[3] = 0` when it returns Success or Failure.
This means `SweepParallelChildren` always sees `childStatesBits = 0`, making ALL children
appear "not finished." This is intentional: the sweep re-fires all child deactivators
regardless of which children completed. The spec does not explicitly call this out but it
is the natural consequence of the clearing behaviour.

---

### Weak Points Spotted

**1. `NodeIndexStack` depth is hardcoded at 8.**  
`BehaviorTreeState` has `fixed ushort NodeIndexStack[8]`, and the delta-tracking path
array is exactly 9 elements (8 stack slots + RunningNodeIndex). Trees deeper than 8
levels cannot push their full path onto the stack, which means deactivators for nodes
deeper than 8 levels may never fire. This limit is not documented in the spec or
`DESIGN.md`.

**2. `Parallel` overwrites `RunningNodeIndex` with its own node index.**  
`ExecuteParallel` always sets `state.RunningNodeIndex = (ushort)nodeIndex` when running,
erasing whatever the leaf nodes set. This is why `SweepParallelChildren` is needed: the
leaf action indices are never captured in the delta path, only the Parallel node itself
is. Any future node type that similarly overwrites `RunningNodeIndex` would require the
same special-case sweep logic.

**3. `LocalRegisters` layout is implicit.**  
`LocalRegisters[0]` is used by Repeater, `LocalRegisters[3]` by Parallel. This mapping
is documented only in inline comments, not in any constants or enums. A developer
implementing a new composite node type could easily collide with existing register usage.

**4. `BindActions` logs to `Console.WriteLine` for missing actions.**  
Unregistered actions produce a `Console.WriteLine` warning at interpreter construction
time. This is a silent failure in production; calling code has no way to detect that a
tree was built with missing delegates except by observing unexpected Failure returns
at runtime.

**5. Solution-level build is permanently broken without `Fbt.SourceGen`.**  
The missing source-generator project prevents `dotnet test FastBTree.sln` from running.
This affects CI pipelines and new developers. The project reference should either be
removed, or the generator csproj should be committed (or excluded with a condition).

---

### Design Decisions Beyond Spec

**1. `SweepExitedNodes` and `SweepParallelChildren` as private helpers.**  
The spec describes these as steps within `Tick`. Splitting them into named private methods
improves readability and isolatability; each has a single responsibility.

**2. `InvokeDeactivatorIfRegistered` does a redundant bounds check.**  
The method checks `(uint)nodeIndex >= (uint)_blob.Nodes.Length` even though
`SweepExitedNodes` already guards Parallel nodes with the same check. The extra check
is cheap and makes the helper self-defending against future callers — it was kept for
safety.

**3. T6 test uses an always-Success action on fresh state** rather than an empty blob
(which is not constructible via `BTreeBuilder`). The test exercises the idle-sentinel
skip path (zero entries in `oldPath` are skipped) without requiring a fabricated blob.

**4. T7 uses try-catch instead of `Assert.Throws`** because xunit's `Assert.Throws`
requires a `Func<T>` or `Action` delegate, and `ref` struct parameters (or `ref`
variables) cannot be captured in lambdas in C# 8/net8 without boxing. The try-catch
pattern is semantically equivalent.

---

### Gaps Found in DESIGN.md

**1. Parallel sweep when `childStatesBits = 0` is not explained.**  
DESIGN.md §1.4 describes the Parallel sweep but does not explicitly state that
`ExecuteParallel` clears `LocalRegisters[3]` to zero on success/failure. A reader
could infer from the description that finished children would be skipped (finished
bit set), but in practice the clearing means ALL children are swept every time the
Parallel exits the path. The behavior is correct and idempotent, but the spec should
clarify it to prevent confusion.

**2. T10 success condition (c) wording is ambiguous.**  
"RunningNodeIndex == 0 after hot-reload reset" overlaps with "tree evaluates this
frame." The spec asserts both that `RunningNodeIndex` is reset AND that execution
continues — which is guaranteed by the `pathWasReset` design (no early return). The
test verified this by asserting the tree returned `NodeStatus.Success` (proving
`ExecuteNode` ran), which is a stronger check than just inspecting `RunningNodeIndex`.

**3. Zero-sentinel rule for `oldPath` is not stated in §3.5.**  
The spec describes the 9-element path layout but does not explicitly say "entry == 0
means idle, skip it in the sweep." The implementation follows the idle-sentinel
convention (zero = absent from path) which is consistent with `RunningNodeIndex`'s
convention, but the spec should state it for completeness.

---

## Outstanding Issues / Next Steps

- [ ] The solution-level `dotnet test FastBTree.sln` command referenced in the batch
  instructions does not work due to `Fbt.SourceGen.csproj` being missing. Phase 2
  (Roslyn generator) should resolve this, but until then the per-project test command
  must be used.
- [ ] `NodeIndexStack` depth limit (8 levels) should be documented as a known constraint
  of the deactivator system.
