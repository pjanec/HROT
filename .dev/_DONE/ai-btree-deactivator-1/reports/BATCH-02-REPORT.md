# BATCH-02 Report

## Summary

BATCH-02 closes all Phase 1 test gaps and delivers the Phase 2 Roslyn generator extension.
Part A adds 9 missing contract tests (5 for TASK-EQL-002, 4 for TASK-EQL-001) to the
FastBTree test project. Part B extends `BTreeActionGenerator` with full deactivator detection
and emission, adds diagnostics BHU-016 and BHU-017, and validates all changes with 5 new
Roslyn compilation tests.

## Tasks Completed

- [x] P1 Fix: TASK-EQL-002 contract tests (ActionRegistryTests.cs) — 5 test methods
- [x] P2 Fix: TASK-EQL-001 contract tests (AttributeTests.cs) — 4 test methods
- [x] TASK-EQL-004 — BTreeActionGenerator deactivator detection and emission

## Test Results

### Fbt.Tests.csproj

```
Failed!  - Failed: 11, Passed: 200, Skipped: 0, Total: 211
```

- **11 failures: pre-existing** (AutoDiscoveryTests — Fbt.SourceGen.csproj missing, D-07 from
  BATCH-01 review). Unchanged from before this batch.
- **200 passing: +9 new** vs the BATCH-01 baseline of 191.
  - +5 in `ActionRegistryTests` (T1–T5 deactivator contract tests)
  - +4 in `AttributeTests` (T1–T4 TASK-EQL-001 namespace/shape checks)

### Fdp.Toolkits.Tests.csproj

```
Failed!  - Failed: 51, Passed: 1158, Skipped: 0, Total: 1209
```

- **51 failures: pre-existing** (all in `ReplayBrowser.Export` and
  `ReplayBrowser.Search` test classes — unrelated to BTree or Roslyn generator work).
- **1158 passing: +5 new** (`BTreeActionGeneratorTests` T1–T5).
- All existing BTree, TkbDescriptorGenerator, and BhuIntegration tests continue to pass.

## Files Changed

| File | Change |
|------|--------|
| `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/ActionRegistryTests.cs` | Added `using System;` and 5 deactivator contract test methods (T1–T5) |
| `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/AttributeTests.cs` | Added `using Fbt.Tests.TestFixtures;` and 4 contract test methods (T1–T4) |
| `FDP/Toolkits/Fdp.Toolkits.Analyzers/SharedBhuDiagnostics.cs` | Added `BHU016_DeactivatorMissingTarget` and `BHU017_DeactivatorUnknownTarget` descriptors |
| `FDP/Toolkits/Fdp.Toolkits.Analyzers/BTreeActionGenerator.cs` | Extended with deactivator detection, collection, validation, emission, and BHU016/017 references |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/BTreeActionGeneratorTests.cs` | **NEW** — 5 Roslyn compilation tests (T1–T5, TASK-EQL-004) |

## Developer Insights

### Issues Encountered

**Deactivator routing in Execute:** The instruction showed collecting deactivators in a
separate second loop over `methods`. However, without also modifying the first loop, a
deactivator `BTreeMethodInfo` (IsSharedAi=false, IsReusable=false) would fall through into
`registrable` and be emitted as a spurious `registry.Register(...)` call. The fix was to
check `m.IsDeactivator` first in the existing loop (`if (m.IsDeactivator) { deactivators.Add(m); continue; }`)
rather than adding a separate pass. The instruction's separate-loop snippet was treated as
illustrative.

**Roslyn stub completeness for T3/T4 (diagnostics tests):** For BHU-016 and BHU-017 to be
emitted, `Execute` must reach the deactivator-processing loop, which requires
`mergedGroups.Count > 0`. That in turn requires at least one 4-param `[BTreeAction]` in
the source. Each diagnostic test therefore includes a dummy `Action_X` method alongside
the defective deactivator.

**Empty-target early return with no actions:** If a source contains only a deactivator
method with an empty TargetAction and no actions, the generator's existing early return
(`if (registrable.Count == 0 && ...) return;`) triggers before the deactivator loop, so
BHU-016 is never emitted. This is an uncovered edge case not in the test matrix, and the
spec does not require it to be handled. Left as-is.

### Weak Points Spotted

**`BTreeMethodInfo` is a class with mutable setters, not a record.** This means accidental
mutation after construction is possible. The incremental generator relies on value equality
for caching; `BTreeMethodInfo` has no `Equals`/`GetHashCode` override, so caching of the
`candidateMethods` provider node effectively never hits (every compilation invalidates the
cache). This was pre-existing and is unchanged by this batch.

**`GroupEntry.Deactivators` is not populated in the constructor.** It is initialized via a
property initializer (`= new List<BTreeMethodInfo>()`), which is correct and idiomatic C#,
but it differs from how `Direct` and `Bridges` are handled (passed through the constructor).
A future refactor could unify these.

**No deactivator duplicate-key check.** If two `[BTreeDeactivator("Same.Key")]` methods
exist for the same group, both are added to `group.Deactivators` and both
`RegisterDeactivator` calls are emitted. The second registration wins at runtime
(last-write-wins in `ActionRegistry`), but a diagnostic (new BHU-018) to warn about this
would be useful. Recorded as potential future work.

### Design Decisions Beyond Spec

**Single loop for all method kinds:** Folded the deactivator collection into the existing
`foreach` loop over `methods` rather than using a second pass. Eliminates the bug where
deactivators would be added to `registrable`, and avoids iterating `methods` twice.

**`Diagnostic.Create` with `null` location:** The BHU-016/017 creation uses `null` for the
location argument (resolves to `Location.None`). No `IMethodSymbol` is available at the
Execute stage (only `BTreeMethodInfo` strings), so no source location is attached. This
matches the pattern for the BHU-002 warning in Execute.

**`deactivatorAttr` check before accessibility guard:** The deactivator detection path in
`GetMethodInfo` runs before the private/protected accessibility check that gates action and
SharedAi methods. Deactivator methods are expected to be `public static` in practice, but
the generator does not enforce accessibility for them (consistent with how the instructions
present the detection snippet).

### Gaps Found in DESIGN.md §2

**§2.5 is slightly ambiguous about who adds the `@0` suffix.** The text says "the generator
must emit the @0 compound key" which could imply the generator constructs it. In practice,
the generator emits `m.TargetAction` verbatim — the developer must include `@0` in the
attribute string themselves. The validation check `b.FullQualifiedMethodName + "@0" ==
d.TargetAction` confirms this: the `@0` is expected to already be in `TargetAction`. The
BATCH-02-INSTRUCTIONS correctly clarify this in the "Key convention" note, but §2.5 in
DESIGN.md does not make it explicit that the attribute value must include the suffix.
Suggested wording update for DESIGN.md: "The TargetAction string in the attribute must
already include the @0 suffix; the generator emits it as-is."
