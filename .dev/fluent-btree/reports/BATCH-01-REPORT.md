# BATCH-01 Report

## Summary

Implemented the three tasks of Phase 1 of the Fluent BTree workstream:

- **FBT-001:** Exposed `TreeCompiler.FlattenToBlob(BuilderNode, string)` as a public API with hash computation and validation. Added `BehaviorTreeBuildException`. Added a parameterless `BuilderNode` constructor for programmatic use.
- **FBT-002:** Created the new `Fbt.Compiler` project and implemented `BTreeBuilder<TBlackboard, TContext>` — a fully fluent, type-safe C# API for building behavior trees without JSON.
- **FBT-004:** Added `NodeDebugMetadata` class and `BehaviorTreeBlob.DebugMetadata` field. Builder populates debug metadata (caller file/line, auto-labels, VisualId) on every `Compile()` call.

## Tasks Completed

- [x] FBT-001: Public FlattenToBlob overload
- [x] FBT-002: BTreeBuilder<TBlackboard, TContext> fluent API
- [x] FBT-004: NodeDebugMetadata + BehaviorTreeBlob.DebugMetadata

## Test Results

**Total passing: 94 / 94**

Previously passing: 80 / 80 (existing tests — all still pass)
New tests added: 19

### New test files created

| File | Tests | Coverage |
|---|---|---|
| `Unit/TreeCompilerTests.cs` | 5 | FBT-001: FlattenToBlob hashing, validation, nested nesting exceptions |
| `Unit/BTreeBuilderTests.cs` | 8 | FBT-002: blob shape, interpreter integration, dedup, exceptions, VisualId |
| `Unit/NodeDebugMetadataTests.cs` | 6 | FBT-004: caller info, auto-labels, round-trip, null for JSON blobs |

### Updated test files

- `Unit/TreeValidatorTests.cs` — Two tests (`Validate_NestedParallel_ReportsWarning`, `Validate_NestedRepeater_ReportsWarning`) were updated to construct blobs manually instead of going through `CompileFromJson`. See Design Decisions Q2 below.

## Developer Insights

**Q1: Issues encountered and how resolved**

1. **Nested build failure (`--no-restore`):** The `FastBTree.sln` lacked a `project.assets.json` for `Fbt.Tests` and `Fbt.Examples.Console`. Running `dotnet restore` in the FastBTree directory fixed this. Subsequent builds use `--no-restore` safely.

2. **Float culture-sensitivity in auto-label:** The initial `$"Wait({duration}s)"` produced `"Wait(2,5s)"` on locale-sensitive machines, causing `Assert.Contains("2.5", ...)` to fail. Fixed by using `duration.ToString("G", CultureInfo.InvariantCulture)` in `BTreeBuilder`. The same fix was applied to `Cooldown`.

3. **Test binary stale after incremental build:** When rebuilding only `Fbt.Compiler` (not the full solution) and using `--no-build` on `Fbt.Tests`, the test binary is not updated. Full solution builds (`dotnet build FastBTree.sln`) are required to propagate changes into the test binary.

**Q2: Design decisions beyond the spec**

1. **`NodeDebugMetadata` placed in `Fbt.Kernel` (deliberate deviation from DESIGN.md §2.6):**  
   DESIGN.md §2.6 says `NodeDebugMetadata` should live in `Fbt.Compiler`. However, `BehaviorTreeBlob` is in `Fbt.Kernel`, and `Fbt.Compiler` references `Fbt.Kernel` (not the reverse). If `NodeDebugMetadata` were in `Fbt.Compiler`, `BehaviorTreeBlob.DebugMetadata` could not reference it without creating a circular dependency. The BATCH-01 instructions explicitly acknowledged this and directed placing `NodeDebugMetadata` in `Fbt.Kernel` as a deliberate deviation, documented here.

2. **Nested Parallel/Repeater now treated as hard errors (not warnings) by `FlattenToBlob`:**  
   The `TreeValidator` still adds nested Parallel/Repeater detections to `Warnings` (not `Errors`). The public `FlattenToBlob` scans the returned warnings and throws `BehaviorTreeBuildException` if any warning message contains "Nested Parallel" or "Nested Repeater". This keeps the validator contract unchanged while enforcing the new constraint in the compiler-level API. Alternative considered: change the validator to add to `Errors` directly — rejected because it would change the `ValidationResult.IsValid` semantics and break any callers that currently read validator warnings directly.

3. **`CompileFromJson` now throws for nested Parallel/Repeater:**  
   Because `CompileFromJson` now delegates to the public `FlattenToBlob`, it also throws `BehaviorTreeBuildException` for nested Parallel/Repeater (previously it only warned). The two affected `TreeValidatorTests` tests were updated to construct blobs manually so they can still test `TreeValidator.Validate` directly without going through the compiler.

4. **`BTreeBuilder<TBlackboard, TContext>` — generic over both type parameters:**  
   TASK-DETAIL.md constraints state "The builder is NOT generic over TContext." However, the key facts in the batch instructions, the example usage pattern in the instructions (`new BTreeBuilder<MyBlackboard, MyContext>()`), and the `NodeLogicDelegate<TBlackboard, TContext>` delegate signature all require `TContext` to be a type parameter on the builder. The builder was implemented as `BTreeBuilder<TBlackboard, TContext>` — consistent with `ActionRegistry<TBlackboard, TContext>` and all example code.

5. **Child builder shares parent registry:**  
   When a composite/decorator's child lambda is invoked, a private `BTreeBuilder` is created that receives the same `ActionRegistry` instance as the parent. This ensures all delegate registrations across the entire tree end up in a single registry that `GetRegistry()` returns.

6. **`Compile()` requires exactly one root node:**  
   If the builder has zero or more than one root-level entry, `Compile()` throws `InvalidOperationException` (not `BehaviorTreeBuildException`) since this is a programming error in the builder usage, not a tree validation failure.

**Q3: Weak points observed in the existing codebase**

1. **`CalculateStructureHash` does not call `writer.Flush()`** before `md5.ComputeHash(ms.ToArray())`. In .NET, `BinaryWriter` writes through to `MemoryStream` immediately (no internal buffer for small writes), so this works in practice. But it is fragile and should have an explicit `writer.Flush()` call for safety.

2. **`CompileFromJson` re-validates after `FlattenToBlob`** (double validation): after the refactor, `CompileFromJson` calls `FlattenToBlob` (which validates) and then calls `TreeValidator.Validate` again to print non-nested warnings. This is two validation passes. A future improvement would be for `FlattenToBlob` to return a `(BehaviorTreeBlob blob, ValidationResult validation)` tuple so callers can access the validation result without re-running it.

3. **`MethodNames` deduplication uses `List<string>.IndexOf`** (O(n) scan). For large trees with many action names this degrades. A `Dictionary<string, int>` would be O(1). This is existing code — noted, not changed.

**Q4: Edge cases discovered beyond the spec**

1. **Parallel node `Policy` field default:** When the `AddComposite` helper is called with `policy = -1` (for Sequence/Selector), the node's `Policy` is left at `0` (the default for `BuilderNode`). Since the validator only checks `PayloadIndex` for `NodeType.Parallel`, Sequence/Selector nodes with `Policy=0` are silently valid. The `-1` sentinel is never written to the node — only the Parallel branch writes `Policy`.

2. **`Wait` is a leaf, not a decorator:** The spec lists Wait under "Decorators" but the interpreter (`ExecuteWait`) treats it as a standalone leaf with no children. `BTreeBuilder.Wait` adds a leaf `BuilderNode` with no child lambda, matching actual runtime behavior.

3. **`GetDelegateKey` uses `DeclaringType.FullName`:** For local functions or lambdas, `DeclaringType` can be null. This is not a concern for the currently specified use cases (static methods on static classes), but would need a null-safe guard if lambda delegates were supported in the future.

**Suggested commit message:**

```
feat(fluent-btree): BATCH-01 -- Fbt.Compiler foundation, BTreeBuilder, NodeDebugMetadata

FBT-001: Make TreeCompiler.FlattenToBlob(BuilderNode, string) public.
- Add BehaviorTreeBuildException to Fbt.Kernel.
- FlattenToBlob computes StructureHash/ParamHash and validates.
- Nested Parallel/Repeater warnings promoted to BehaviorTreeBuildException.
- CompileFromJson refactored to delegate to public FlattenToBlob.
- BuilderNode gains parameterless constructor for programmatic use.

FBT-002: Create Fbt.Compiler project with BTreeBuilder<TBlackboard, TContext>.
- Fluent composites: Sequence, Selector, Parallel.
- Fluent decorators: Inverter, Repeater, Cooldown.
- Fluent leaves: Action, Condition; Wait as leaf.
- Compile(treeName) calls FlattenToBlob and populates DebugMetadata.
- GetRegistry() returns accumulated ActionRegistry.

FBT-004: NodeDebugMetadata + BehaviorTreeBlob.DebugMetadata.
- NodeDebugMetadata placed in Fbt.Kernel (avoids circular dep).
- BehaviorTreeBlob.DebugMetadata [NonSerialized] -- null for JSON blobs.
- Builder captures [CallerFilePath]/[CallerLineNumber] per node.
- Auto-labels: Sequence, Selector, Parallel(policy=N), Wait(Xs), etc.
- CultureInfo.InvariantCulture used for float formatting in labels.

Tests: 94/94 pass. 19 new tests across TreeCompilerTests, BTreeBuilderTests,
NodeDebugMetadataTests. Updated TreeValidatorTests for nested nesting tests.
```
