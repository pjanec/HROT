# BATCH-04 Report

## Summary

Implemented the Roslyn source generator (`BTreeActionGenerator`) and runtime auto-discovery
(`FbtAutoDiscovery.ScanAndRegister`). The generator runs on `Fbt.Tests` at compile time and
emits `FbtActionRegistrar.g.cs` containing registrations for all `[BTreeAction]` /
`[BTreeCondition]` methods. `FbtAutoDiscovery` finds and invokes the registrar at runtime via
reflection. All 128 tests pass (123 existing + 5 new).

## Tasks Completed

- [x] FBT-011: BTreeActionGenerator (Fbt.SourceGen project + IIncrementalGenerator)
- [x] FBT-013: FbtAutoDiscovery.ScanAndRegister

## New Files Created

| File | Purpose |
|------|---------|
| `src/Fbt.SourceGen/Fbt.SourceGen.csproj` | Source generator project (netstandard2.0, IsRoslynComponent) |
| `src/Fbt.SourceGen/BTreeActionGenerator.cs` | IIncrementalGenerator implementation |
| `src/Fbt.Compiler/FbtAutoDiscovery.cs` | Runtime assembly scanner for [FbtRegistrar] types |
| `tests/Fbt.Tests/TestFixtures/AnnotatedTestActions.cs` | Test fixture with [BTreeAction]/[BTreeCondition] methods |
| `tests/Fbt.Tests/Unit/AutoDiscoveryTests.cs` | 5 new tests for FBT-011 + FBT-013 |

## Modified Files

| File | Change |
|------|--------|
| `tests/Fbt.Tests/Fbt.Tests.csproj` | Added Fbt.SourceGen as Analyzer reference |
| `FastBTree.sln` | Added Fbt.SourceGen project |

## Test Results

Total passing: **128 / 128** (0 failed, 0 skipped)

New tests in `AutoDiscoveryTests.cs`:
1. `ScanAndRegister_FindsGeneratedRegistrar_InTestAssembly` — verifies `AlwaysSuccessAction` is registered
2. `ScanAndRegister_FindsBothActionAndCondition` — verifies both `[BTreeAction]` and `[BTreeCondition]` annotated methods are found
3. `ScanAndRegister_RegisteredAction_IsCallable` — actually invokes the registered action and checks return value
4. `ScanAndRegister_SkipsNonReflectableAssemblies_Safely` — sanity test, no exception propagated
5. `FbtRegistrarAttribute_IsAppliedToGeneratedClass` — reflection check: `FbtActionRegistrar` type in test assembly has `[FbtRegistrar]`

## Generator Output Verification

The generator successfully emits `FbtActionRegistrar.g.cs` (compiled in-memory; `EmitCompilerGeneratedFiles`
is false by default so no disk copy, but the type IS compiled into `Fbt.Tests.dll`). The emitted class:
- Is in namespace `Fbt.Tests.Generated`
- Has `[Fbt.FbtRegistrar]` attribute
- Has `RegisterAll(ActionRegistry<TestBlackboard, MockContext> registry)` method
- Registers `AlwaysSuccessAction` and `AlwaysSuccessCondition`

## Known Gaps / Technical Debt

- **FBT-011: 3-param (reusable) delegates** — not handled by generator. The generator emits `BTree001`
  (DiagnosticSeverity.Warning) and skips them. These must be registered via `BTreeBuilder` expression
  binding or a manually written `RegisterAll` override. A future enhancement could add
  `[BTreeAction(BlackboardType=typeof(MyBB), FieldName="AmmoCount")]` to supply missing information.

- **Generated registrar is non-generic** — the spec's pseudocode showed a generic `RegisterAll<TBB, TCT>`,
  but that cannot compile when the registered delegates are typed to specific concrete types. The actual
  implementation emits typed (non-generic) overloads, one per `(TBlackboard, TContext)` group found in
  the annotated methods. `FbtAutoDiscovery` uses `GetMethods` (not `GetMethod`) to iterate all overloads
  and try each; type mismatches are caught silently.

## Developer Insights

**Q1: Issues encountered?**

Two compile errors on first pass:
1. `GetDeclaredSymbol` returns `ISymbol`, not `IMethodSymbol` — fixed with `as IMethodSymbol` cast.
2. `FbtAutoDiscovery.ScanAndRegister` constraint `where TContext : struct` was missing `, IAIContext`
   required by `ActionRegistry<TBlackboard, TContext>` — added the constraint.
Also suppressed `RS2008` (analyzer release tracking) in `Fbt.SourceGen.csproj` `NoWarn`.

**Q2: Design decisions beyond the spec?**

- Generated `RegisterAll` is non-generic (typed to concrete TBlackboard/TContext) — unavoidable for
  the code to compile when delegates have specific types.
- `FbtAutoDiscovery` iterates `GetMethods` instead of `GetMethod` to safely handle multiple `RegisterAll`
  overloads without `AmbiguousMatchException`.
- `IAIContext` constraint added to `ScanAndRegister<TBlackboard, TContext>` to match `ActionRegistry`.

**Q3: Weak points?**

- If an assembly has methods annotated with different (TBlackboard, TContext) pairs, multiple `RegisterAll`
  overloads are emitted. Only the matching one is successfully invoked at runtime; others fail silently.
- `Location.None` is used for `BTree001` diagnostics — no source file/line clickthrough in IDE.
- Generator always re-runs (no incremental caching optimization) because `BTreeMethodInfo` is a mutable
  class, not a value-equatable record. Acceptable for this batch.

**Suggested commit message:**
```
feat(fluent-btree): BATCH-04 -- BTreeActionGenerator + FbtAutoDiscovery (FBT-011, FBT-013)
```
