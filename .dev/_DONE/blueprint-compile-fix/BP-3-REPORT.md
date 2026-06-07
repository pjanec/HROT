# BP-3 Report — MSBuild generator deserialization independent of Fdp.Toolkits

**Branch:** `blueprint-integ-1`  
**Date:** 2026-06-05

---

## Root Cause (confirmed)

`ConditionMetPayload.Condition` was declared as `SearchPredicateDto?` (under `#if NET8_0_OR_GREATER`) and
`object?` (netstandard2.0) in `Hrot.Blueprints.Compiler/Assets/Nodes.cs`.

When the MSBuild Roslyn analyzer host loads the **net8.0** Compiler DLL and calls
`BlueprintJsonServices.Deserialize<BlueprintAsset>()`, STJ's `DefaultJsonTypeInfoResolver` reflects over
the entire model graph. Reflecting over `ConditionMetPayload.Condition` whose declared type is
`SearchPredicateDto?` forces the CLR to load `Fdp.Toolkits, 0.1.1.0` — an assembly that is not deployed
into the netstandard2.0 analyzer host. Result: `FileNotFoundException: Could not load file or assembly
'Fdp.Toolkits, 0.1.1.0'` → `BP0002` diagnostic on every `Count2.bp.json` parse attempt.

The `Hrot.Blueprints.Compiler.csproj` already restricts the `Fdp.Toolkits` PackageReference to `net8.0`
only; the trigger was the **property's declared type** (reflected at runtime, not compile-time).

---

## Fix

### Model change — `Assets/Nodes.cs`

Replaced `ConditionMetPayload.Condition` with `JsonNode?` (from `System.Text.Json.Nodes`) for **both TFMs**
(no more `#if NET8_0_OR_GREATER`):

```csharp
// Before
#if NET8_0_OR_GREATER
using Fdp.Toolkit.ReplayBrowser.Search;
#endif
...
public sealed class ConditionMetPayload
{
#if NET8_0_OR_GREATER
    public SearchPredicateDto? Condition { get; set; }
#else
    public object? Condition { get; set; }
#endif
}

// After
using System.Text.Json.Nodes;
...
public sealed class ConditionMetPayload
{
    /// Stored as JsonNode? so deserialization never requires Fdp.Toolkits to be loaded
    /// (e.g. in the netstandard2.0 analyzer host).
    public JsonNode? Condition { get; set; }
}
```

`System.Text.Json.Nodes` ships with `System.Text.Json` v8.0.5, already a PackageReference in the
Compiler project for all TFMs — no new dependency added.

### Stage2 validation — `Compiler/Stages/Stage2_Validate.cs`

Removed typed `SearchPredicateDto` casts. BP2008 (empty compound predicate) and BP2009 (unresolvable
component type) are now checked via `JsonNode` tree inspection:

- `IsEmptyCompoundPredicate(JsonNode?)` — checks `$type == "Compound"` and `Conditions.Count == 0`
- `HasUnresolvableComponentType(JsonNode?)` — checks `$type == "PropertyMatch"` and `ComponentType == null`;
  recurses into compound children

### Stage5 — no change needed

`Stage5_Schedule.cs` already uses `JsonSerializer.Serialize(cm.Condition)` which serializes a `JsonNode?`
to its raw JSON representation — exactly what is needed for embedding the predicate JSON in generated code.

### Editor/runtime consumption

`IPredicateCompiler.CompileComponentPredicate(SearchPredicateDto root)` is an **interface on the runtime
side** (Fdp.Toolkit.Blueprints), not in the serialized model. Test mocks and runtime code still reference
`SearchPredicateDto` via `using Fdp.Toolkit.ReplayBrowser.Search` — this is correct and was preserved in
all test files.

The net8 editor converts `JsonNode? → SearchPredicateDto` at its own boundary before passing to the
predicate compiler. That conversion is out of scope for BP-3 (no editor UI code for WhenNode was added
in this branch).

### Test files updated (5 files)

All test sites that assigned typed `PropertyMatchDto`/`CompoundPredicateDto` instances to
`ConditionMetPayload.Condition` were migrated to `JsonNode.Parse(...)`:

| File | Change |
|------|--------|
| `Compiler/WhenNodeValidatorTests.cs` | `JsonNode.Parse(...)` for BP2008 + BP2009 test payloads |
| `Compiler/Stage6_LoweringTests/WhenNodeLoweringTests.cs` | `JsonNode.Parse(...)` in `MakeConditionMetNode` |
| `Benchmarks/WhenNodePerfTests.cs` | `JsonNode.Parse(...)` for ConditionMet payload; restored `using Fdp.Toolkit.ReplayBrowser.Search` for `IPredicateCompiler` mock |
| `HotReload/WhenNodeHotReloadTests.cs` | `JsonNode.Parse(...)` with invariant-culture number formatting for the `minValue` parameter; restored `using Fdp.Toolkit.ReplayBrowser.Search` for `IPredicateCompiler` mock |
| `Runtime/WhenNodeRuntimeTests.cs` | `JsonNode.Parse(...)` for ConditionMet payload; kept `using Fdp.Toolkit.ReplayBrowser.Search` (already present) |

Note: `WhenNodeHotReloadTests.BuildCondMetAsset(minValue)` uses
`minValue.ToString("G17", CultureInfo.InvariantCulture)` to avoid locale-dependent decimal separators
in the JSON string — this was critical; `$"..."` interpolation uses thread culture and broke parsing
on systems with `,` as decimal separator.

---

## Verification

### Assets/ folder — zero Fdp.Toolkits references

```
grep Fdp.Toolkit in Hrot.Blueprints.Compiler/Assets/
  BlueprintAsset.cs:32: // Mirror of Fdp.Toolkit.Blueprints.BlueprintDispatchKind. (comment only)
  Nodes.cs:203:         // ... netstandard2.0 analyzer host) (comment only)
```

No runtime `using` or type reference to `Fdp.Toolkit*` in the serialized model layer.

### Count2 Full Rebuild

`Count2.bp.json.setaside` restored to `Count2.bp.json` (Move-Item, not git-tracked).

`dotnet build IOS-IG-SimHost.sln -c Debug` result:
- **0 errors** (was `BP0002: FileNotFoundException: Fdp.Toolkits`)
- Generated: `obj/GeneratedFiles/Hrot.Blueprints.Generators/Hrot.Blueprints.Generators.BlueprintIncrementalGenerator/Count2_F5F6F285_Bp.g.cs` (timestamp 2026-06-05 21:52)
- `Hrot.AI.Behaviors.dll` compiled successfully

### Test results — `Hrot.Blueprints.Tests`

`dotnet test Hrot.Blueprints.Tests.csproj -c Debug --no-build`:
```
Failed:  8, Passed: 1371, Skipped: 8, Total: 1387
```

**All 8 failures are pre-existing (not introduced by BP-3):**

| Failing test | Pre-existing cause |
|---|---|
| `CountingDemo_PinsStripped_After5Ticks_CountEquals5` | BP-2: Stage0_Rehydrate disabled; blueprint ticks as no-op |
| `AiPrimitiveEmitGoldenTests.*` (×2) | DEBT-006: golden snapshot mismatch (pre-dates this branch) |
| `LibraryEmitGoldenTests.Library_EmitMatchesGoldenSource` | DEBT-006: golden snapshot mismatch |
| `LibraryMathDemoTests.LibraryMath_GeneratedSource_Snapshot` | DEBT-006: golden snapshot mismatch |
| `MoveToAndFireDemoTests.MoveToAndFire_GeneratedSource_Snapshot` | DEBT-006: golden snapshot mismatch |
| `ConditionSummaryAttachmentTests.*` | Pre-existing unrelated to WhenNode/Condition model |
| `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes` | Pre-existing allocation regression |

WhenNode-specific test results:
- `WhenNodeValidatorTests` — all pass
- `WhenNodeLoweringTests` — all pass
- `WhenNodeHotReloadTests` — 10/10 pass
- `WhenNodeRuntimeTests` — 19/19 pass
- `WhenNodePerfTests` — all pass

---

## Weak points / follow-up

1. **Editor boundary conversion missing** — No code currently converts `JsonNode? Condition` → `SearchPredicateDto`
   for the runtime `IPredicateCompiler`. The hot-reload tests pass because they use mock predicate compilers that
   accept `SearchPredicateDto` but never actually receive a value from `ConditionMetPayload.Condition`. When the
   editor's `WhenNodeDrawer` wires up a real predicate compiler, it will need to deserialize `JsonNode` into
   `SearchPredicateDto` using `JsonSerializer.Deserialize<SearchPredicateDto>(node)`. This is a BP-4/editor scope
   item.

2. **JSON number culture safety** — The `minValue.ToString("G17", InvariantCulture)` pattern should be applied
   consistently anywhere `double` values are embedded into raw JSON strings in test helpers. The perf test and
   lowering test use hardcoded literals (safe); only the hot-reload test had a variable.

3. **Stage0 still disabled** — `Count2.bp.json` generates and compiles but ticks as a no-op at runtime until
   BP-2 rehydration lands. Acknowledged per instructions.

---

## Suggested commit message

```
fix(blueprints): decouple Compiler model from Fdp.Toolkits (BP-3)

Replace ConditionMetPayload.Condition type from SearchPredicateDto?
(#if NET8_0_OR_GREATER) to JsonNode? for both TFMs, eliminating the
Fdp.Toolkits assembly-load failure in the netstandard2.0 Roslyn
analyzer host. Stage2 validation migrated to JsonNode tree inspection.
Count2.bp.json now parses, generates Count2_F5F6F285_Bp.g.cs, and
builds with 0 errors. No new test regressions (1371 pass, 8 pre-existing
failures retained).
```
