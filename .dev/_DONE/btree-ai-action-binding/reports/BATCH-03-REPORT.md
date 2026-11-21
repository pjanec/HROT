# BATCH-03 Report

## Implementation Summary

### Task S1-2b: Struct-DTO size resolution

**New file: `StructSizeResolver.cs`** (`Hrot.AiEditor.Generators`)
Resolves the managed C# sequential byte size of any type given its CLR FQN string and a Roslyn `Compilation`. Lookup order: (1) `KnownSizes` table mirroring `BTreeBlackboardPackHelper.KnownSizes`; (2) `compilation.GetTypeByMetadataName(typeId)` to look up struct symbols; (3) `ComputeStructSize` on the symbol. Returns `null` for unresolvable types. Header comment: `// Mirrors Fdp.Toolkits.Analyzers.BehaviorParameterSizeAnalyzer.ComputeStructSize — keep in sync.`

**`BTreeBlackboardPackHelper` (Persistence, netstandard2.0)**
Added two overloads:
- `Pack(vars, Func<string,int?>? extraSizeResolver, out int total)` — lookup order: `KnownSizes` → `extraSizeResolver`. Throws `NotSupportedException` for anything unresolved.
- `WouldOverflow(vars, Func<string,int?>? extraSizeResolver, out string? unknownTypeId)` — same lookup, returns `false + unknownTypeId` for unresolvable.
Both single-param overloads delegate to the new forms with `null` resolver (BATCH-02 tests stay green).

**`BTreeJsonGenerator.GenerateOneAsset`**
Added a per-asset resolver build step (for `Managed == true` assets): calls `StructSizeResolver.MakeDelegate(compilation)`. Before the overflow check, iterates all managed variables; any not in `KnownSizes` is resolved via the Roslyn resolver — if null → report `BTREE0002` and return (no partial emit). The overflow check now passes the resolver. All three emit calls (`EmitTopologyCore`, `EmitBlackboardStructSource`, `EmitBridge`) receive the resolver.

**`BTreeEmitCore`**
- `EmitBlackboardStructSource`: new overload accepting `SizeResolverDelegate? sizeResolver`. Passes it to `Pack`. For struct-DTO fields (non-primitive/vector types), `ToCsTypeName` now returns `global:: + typeId.Replace('+', '.')` so nested-type separators are correctly converted and the field declaration is valid C#.
- `EmitTopologyCore`: new overload accepting `SizeResolverDelegate?`, threads it to `BuildVariableOffsets` → `Pack`.
- `BuildVariableOffsets`: accepts optional resolver, passes to `Pack`.

**`BTreeBridgeEmitCore`**
- `EmitBridge`: new overload accepting `SizeResolverDelegate?`, threads to `EmitBTreeRegisterMethod`.
- `EmitManagedActionThunks` / `EmitManagedConditionThunks`: accept optional resolver, pass to `Pack`.
- `DtoTypeToGlobal`: added `Replace('+', '.')` on the `global::` fallback so nested struct DTO types emit valid C#.

**`BTreeMethodCompatibilityValidator.CheckThreeParamReusable`**
Normalizes both `param0TypeFqn` (from Roslyn symbol display, uses `.`) and `varTypeId` (from asset JSON, uses `+` for nested types) to `.` before comparison. Both sides are normalized; the "direction" is toward `.` (C# source notation).

## Design Decisions

1. **Resolver in Generators, not Persistence**: `StructSizeResolver` lives in `Hrot.AiEditor.Generators` (Roslyn-aware) and is injected via `Func<string,int?>`. The Persistence assembly stays netstandard2.0 / Roslyn-free. This matches the design mandate.

2. **Managed size, not Marshal size**: `GetTypeSize` returns 1 for `System.Boolean` (C# sequential layout). `Marshal.SizeOf` returns 4 for bool (Win32 BOOL). The Unsafe.As projection and struct layout both use managed sizes — using Marshal sizes would cause read/write to corrupt adjacent fields.

3. **Single offset source preserved**: The resolver delegate is built once in `GenerateOneAsset` and passed to all three emit helpers. All three call `BTreeBlackboardPackHelper.Pack` with the same resolver, ensuring struct fields, blob keys, and registrar thunk offsets are all derived from the same pack result.

4. **Unresolvable → fail early, no partial emit**: The new loop in `GenerateOneAsset` checks every non-primitive variable _before_ the overflow guard and before any emit. If any variable is unresolvable, BTREE0002 + return (no topology, no struct, no bridge). This is stricter than the old path which would silently fall back to empty offsets.

5. **`InternalsVisibleTo`**: Added `<InternalsVisibleTo Include="Hrot.AiEditor.Generators.Tests" />` to the Generators `.csproj` so tests can call `StructSizeResolver.Resolve` directly. The resolver stays `internal` (generator-internal detail).

## Deviations

### T09_BlackboardManaged.btree.json now skips with BTREE0002

**WHAT:** `T09_BlackboardManaged.btree.json` stores variable types as short aliases (`float`, `Vector3`, `int`, `bool`) instead of FQNs (`System.Single`, `System.Numerics.Vector3`, etc.). The new unresolvable-check loop finds `float` is not in `KnownSizes` and not a valid Roslyn metadata name, so it reports BTREE0002 and skips the asset.

**BEFORE (pre-BATCH-03):** The asset silently fell back to "unmanaged mode" — topology emitted without offsets and no blackboard struct. This was already broken (the asset declared `Managed=true` but emitted as if `Managed=false`).

**WHY:** The design spec says unresolvable types → BTREE0002 skip (no silent partial emit). Correct diagnosis is now surfaced. The asset needs its variable TypeIds updated to FQNs in the editor.

**BENEFIT:** The asset no longer silently emits incorrect code. The BTREE0002 warning is actionable.

**RISK:** T09 is a demo asset (not part of any test assertions). The build warning is harmless (`TreatWarningsAsErrors` not set for `Hrot.AI.Behaviors`). No test references T09's generated output.

## Test Results

### `Hrot.AiEditor.Generators.Tests` (full suite, stability filter applied)
```
Failed:  2, Passed: 69, Skipped: 0, Total: 71
```
The 2 failures are the known pre-existing `MigrationEquivalenceTests` (DEBT-AIB-007):
- `BTree_SampleScout_MigrationJson_RoundTrips_And_CarriesLayout`
- `Hsm_SampleGuard_MigrationJson_RoundTrips_And_CarriesLayout`

These are excluded per the batch instructions. All 6 new S1-2b tests pass; all 63 existing BATCH-02 tests pass.

**New tests (all 6 passed):**
- `StructDtoVariable_ResolvesManagedSize` — resolver returns 8 (TwoIntParams), 12 (ThreeFieldParams), 8 (NestedDto), 24 (VecParams). Verifies bool=1, unknown→null.
- `StructDtoVariable_PacksAtResolvedOffsets` — TwoIntParams@0(8), ThreeFieldParams@8(12), total=20. Struct source uses `global::` names with `.` separators.
- `StructDtoVariable_TopologyAndRegistrar_CarryResolvedOffsets` — blob key `@0` for Params1, `@8` for Params2; registrar thunk `(nint)0` and `(nint)8`. Both sides agree.
- `NestedStructDto_TypeMatch_Validates` — `Stub.ContainerParams+NestedDto` TypeId validates against the same nested struct method param; 3 files emitted.
- `StructDtoVariable_AggregateOver100Bytes_SkipsWithBtree0002` — 6×VecParams (=5 exceed 100B); generator emits BTREE0002, zero files.
- `UnresolvableStructDto_SkipsWithBtree0002` — unknown TypeId `Stub.DoesNotExistAtAll`; BTREE0002, zero files.

### `Hrot.AiEditor.Persistence.Tests` (full suite, stability filter applied)
```
Failed: 0, Passed: 129, Skipped: 0, Total: 129
```
Byte-identity gate green. `Managed==false` CombatShowcase/SampleScout tests unchanged.

### Clean rebuild
```
dotnet build-server shutdown + dotnet build Hrot.AI.Behaviors -t:Rebuild
→ Build succeeded. 0 Errors. 1 Warning (BTREE0002 on T09, expected deviation above).
```

## Developer Insights

1. **VecParams managed size = 24, not 20.** `{int(4), Vector3(12)}` raw end = 20, but `AlignUp(20, maxAlign=8) = 24`. The struct is padded to 24 so arrays of it have naturally aligned elements. This is correct managed sequential behavior and matches the reference analyzer.

2. **T09 alias type names.** T09 predates the FQN convention. The old silent-fallback behavior masked the bug. Needs an editor-side fix to write FQN TypeIds. This is now tracked as the actionable BTREE0002 warning.

3. **`GetTypeByMetadataName` handles `+` natively.** The Roslyn API accepts the CLR metadata `+` form for nested types, so `StructSizeResolver.Resolve("Outer+Inner", compilation)` works without preprocessing.

4. **Validator normalization direction.** The Roslyn symbol display format `NameAndContainingTypesAndNamespaces` always uses `.` for nested types. The asset JSON uses `+`. Normalizing both to `.` before comparison is the correct and safe approach — it handles both cases without guessing which side is "canonical".

5. **DEBT-AIB-012 (suggested).** The `StructSizeResolver` logic is a third copy of `ComputeStructSize` (alongside `BTreeActionGenerator` and `BehaviorParameterSizeAnalyzer`). All three are kept in sync by the "keep in sync" comment. A shared utility would be better but is architecturally non-trivial (cross-assembly, different namespaces). Consider extracting to a shared Roslyn utilities package in a future batch.

## Known Issues

- ~~T09_BlackboardManaged.btree.json uses short type aliases and now skips with BTREE0002~~ — **Fixed in corrective round below.**
- Three copies of `ComputeStructSize` in the codebase (DEBT-AIB-012, see above).

---

## Corrective round (alias acceptance)

### Problem

The `KnownSizes` dictionaries in both `StructSizeResolver` and `BTreeBlackboardPackHelper` keyed
only on CLR FQNs (`System.Single`, `System.Numerics.Vector3`, …). Hand-authored assets — including
the demo asset `T09_BlackboardManaged.btree.json` — use C# alias TypeIds (`float`, `Vector3`, `int`,
`bool`). With only FQN keys, the alias TypeIds fell through to the Roslyn lookup path; because
`"float"` is not a valid metadata name, `GetTypeByMetadataName` returned `null`, the pre-emit
unresolvable check fired BTREE0002, and the asset was skipped. This was a coverage regression
introduced by the BATCH-03 strict-fail-early policy.

### Root cause of the emit-side gap

Beyond the size table, `BTreeEmitCore.ToCsTypeName` and `BTreeBridgeEmitCore.DtoTypeToGlobal`
also lacked alias arms. For alias TypeIds not in their switch statements, both fell through to
`_ => "global::" + typeId.Replace('+', '.')`, which emits `global::float`, `global::int`, etc. —
invalid C#. Similarly, the `[MarshalAs(UnmanagedType.I1)]` guard in `EmitBlackboardStructSource`
only checked `"System.Boolean"`, so a `"bool"` TypeId would not get the attribute.

### Changes

**`StructSizeResolver.cs` — alias entries added to `KnownSizes`:**
```
bool→1, byte→1, sbyte→1, char→2, short→2, ushort→2,
int→4, uint→4, float→4, long→8, ulong→8, double→8,
Vector2→8, Vector3→12, Vector4→16, Quaternion→16
```
Comment: `// C# alias forms — mirror of BlackboardTypeHelper`

**`BTreeBlackboardPackHelper.cs` — identical alias entries added to its `KnownSizes`:**
Same 16 alias entries, same comment. Both tables are now symmetric.

**`BTreeEmitCore.cs` — two fixes:**
1. `ToCsTypeName`: each existing FQN arm extended with `or "<alias>"` so aliases produce valid
   C# keywords/short names. Vector alias arms (`"Vector2"`, `"Vector3"`, `"Vector4"`,
   `"Quaternion"`) map to `global::System.Numerics.*` (same as their FQN arms).
2. `[MarshalAs(UnmanagedType.I1)]` guard: changed from `f.TypeId == "System.Boolean"` to
   `f.TypeId == "System.Boolean" || f.TypeId == "bool"`.

**`BTreeBridgeEmitCore.cs` — `DtoTypeToGlobal`:**
Each existing FQN arm extended with `or "<alias>"`. Bare `"Vector2"/"Vector3"/"Vector4"/"Quaternion"`
aliases added (no FQN equivalent in original switch) mapping to `global::System.Numerics.*`.

### Tests added (2 new, next to `StructDtoVariable_ResolvesManagedSize`)

**`StructSizeResolver_AcceptsCSharpAliases`**
Calls `StructSizeResolver.Resolve` with alias strings and asserts:
- `"float"` == 4 == `"System.Single"` result
- `"int"` == 4 == `"System.Int32"` result
- `"bool"` == 1 == `"System.Boolean"` result
- `"Vector3"` == 12 == `"System.Numerics.Vector3"` result

**`T09Managed_AliasTypes_EmitsStructNoWarning`**
Builds a managed DTO matching T09's exact variable set (`AttackRange:float`, `HomePosition:Vector3`,
`PatrolLoops:int`, `IsAlerted:bool`; Wait-only tree; `Managed=true`). Runs the full generator and
asserts:
- (a) A `*.Blackboard.g.cs` IS emitted containing all four field names.
- (b) Exactly 3 files emitted (topology + struct + bridge).
- (c) Zero diagnostics — no BTREE0002 warning.

### Test results

**`Hrot.AiEditor.Generators.Tests`**
```
Failed: 2, Passed: 71, Skipped: 0, Total: 73
```
+2 new tests pass; all 71 prior passing tests remain passing.
2 failures remain the pre-existing `MigrationEquivalenceTests` (DEBT-AIB-007, unchanged).

**`Hrot.AiEditor.Persistence.Tests`**
```
Failed: 0, Passed: 129, Skipped: 0, Total: 129
```
`Managed==false` byte-identity gate unchanged.

---

## Suggested Commit Message

```
feat(btree-ai-binding): struct-DTO size resolution + nested-type validator fix (BATCH-03)

Implements S1-2b: resolve managed struct sizes from Roslyn Compilation and thread them
through all three emit helpers (struct, topology, registrar) as a single offset source.

- StructSizeResolver: Roslyn-backed managed size resolver in Hrot.AiEditor.Generators
  (mirrors BehaviorParameterSizeAnalyzer.ComputeStructSize; bool=1 not Marshal's 4).
- BTreeBlackboardPackHelper: Pack/WouldOverflow overloads with injected Func<string,int?>
  size resolver; existing primitive-only overloads unchanged (BATCH-02 tests stay green).
- BTreeJsonGenerator: builds resolver per Compilation; checks all managed variables for
  unresolvable types before any emit; threads resolver to EmitTopologyCore/
  EmitBlackboardStructSource/EmitBridge.
- BTreeEmitCore: struct-DTO fields use global::-qualified names (+ -> . separator);
  resolver threaded to BuildVariableOffsets and Pack.
- BTreeBridgeEmitCore: resolver threaded to Pack in thunk emitters; DtoTypeToGlobal
  handles + -> . for nested types.
- BTreeMethodCompatibilityValidator: normalize both param0TypeFqn and varTypeId to '.'
  before comparison so nested-struct DTO bindings (TypeId uses '+') validate correctly.
Tests: 6 new S1-2b tests (sizes, offsets, topology+registrar agreement, nested validation,
overflow skip, unresolvable skip); Generators.Tests 69/2 (2 = pre-existing debt);
Persistence.Tests 129/0; clean rebuild 0 errors.
```
