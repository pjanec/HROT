# AN2 -- StaticTypeRegistry enum-FQN acceptance — Report

**Branch:** `blueprint-integ-1`
**Date:** 2026-06-06
**Status:** DONE — 0 new test failures, 4 new tests green (3 resolution + 1 emit round-trip)

---

## STEP 1 — Investigation findings (decisive)

### Is the stamped TypeRef honored or is it TypeId-lookup?

**The pipeline is pure TypeId-string lookup.** `BlueprintTypeRef` (in
`Hrot.Blueprints.Compiler/Assets/Declarations.cs`) carries only:
- `TypeId` (string)
- `IsArray` (bool)
- `GenericArgs` (List<BlueprintTypeRef>)

There are **no `IsUnmanaged` or `SizeBytes` fields** on `BlueprintTypeRef`. The task description's
phrasing "stamp IsUnmanaged/SizeBytes into the TypeRef" was aspirational — those fields live on
`IrTypeRef` (the compiler-internal resolved form), not on the asset-level `BlueprintTypeRef`.

`StaticTypeRegistry.TryResolve` (line 89 before this patch) does exactly:
```csharp
return TypeTable.TryGetValue(typeRef.TypeId, out irType!);
```
Only the `TypeId` string is passed to the registry; any hypothetical `IsUnmanaged`/`SizeBytes`
fields on the asset-level TypeRef would be silently ignored.

### What does asset JSON actually persist for a pin/var type?

A single TypeId string. Example from `VariableDecl.Type`:
```json
{ "TypeId": "System.Single" }
```
The `BlueprintTypeRef` JSON shape has no `IsUnmanaged`/`SizeBytes` properties. `IsArray` and
`GenericArgs` are the only extra fields.

### Where in the editor is a pin/var TypeRef constructed for enum-typed things?

Two sites, both constructing only `new BlueprintTypeRef { TypeId = <string> }`:

1. `BlueprintDocumentFactory.CreateVariable` (line 309): sets `TypeId = finalType` (the string
   from the UI selector — currently only known system types are selectable).
2. `BlueprintVariablesWindow.AddVariable` (line 104): sets `TypeId = entry.FieldType.FullName`,
   which for an enum type already returns the dotted FQN (no `global::` prefix) from reflection.

Neither site can stamp `IsUnmanaged`/`SizeBytes` because the fields do not exist on the type.

### Conclusion

The correct mechanism is: **encode "this is an enum" in the TypeId string itself** using a
convention the compiler can detect without reflection. The ENUM-DESIGN.md §RESOLVED documents
the chosen convention: `TypeId = "global::Ns.MyEnum"` (the `global::` C# alias qualifier).
The compiler detects this prefix and synthesizes an unmanaged IrTypeRef with default size 4.
Editor stamping of `IsUnmanaged`/`SizeBytes` is **unnecessary** (the `global::` TypeId string
is the sole signal) and structurally impossible without schema additions.

---

## Mechanism chosen

**"Trust the JSON FQN + emit a cast"** (ENUM-DESIGN.md §RESOLVED, Q3 answer: lightweight
acceptance, no source-generated catalog).

In `StaticTypeRegistry.TryResolve`, after the `TypeTable` miss:
- If `typeRef.TypeId` starts with `"global::"` (the editor-stamped enum FQN convention), synthesize:
  ```
  IrTypeRef { FullName = typeRef.TypeId.Substring("global::".Length), IsUnmanaged = true, SizeBytes = 4 }
  ```
  The `"global::"` prefix is **stripped** so that `IrTypeRef.FullName` is the UNPREFIXED FQN
  (`"Ns.MyEnum"`), consistent with every other `IrTypeRef.FullName`. See "EMIT bug fix" below.
- SizeBytes=4 is the overwhelmingly common underlying type (System.Int32). Correctness of the
  actual type + value is delegated to the downstream C# compiler (a bad enum FQN → ordinary CS
  error caught gracefully by hot-reload, as specified in ENUM-DESIGN.md §RESOLVED).
- The generator (AN1, separate task) emits `(global::FQN)N` — a direct integer cast.

**Why `global::` as the sentinel (not an `IsEnum` field or other mechanism):**
- `BlueprintTypeRef` schema is not extended — zero JSON migration cost.
- Unambiguous: no system FQN in `TypeTable` starts with `global::`. All system types use plain
  dotted names (`System.Int32`, `Fdp.Core.Entity`).
- The `global::` prefix is precisely the emitted form in generated C#, so the TypeId doubles as
  the C# emit token.

**`CheckUnmanagedConstraint` (BP1503):** No change required. It calls `TryResolve` and checks
`resolved.IsUnmanaged`. With the synthesized IrTypeRef having `IsUnmanaged = true`, enum variables
in Instance state / AiPrimitive WorkingState pass the constraint automatically.

**Editor stamping site:** No changes made. The site that builds a `BlueprintTypeRef` for an enum
just needs to use a `global::` TypeId string (e.g., `"global::Ns.MyEnum"`). No reflection-derived
metadata needs to be stored separately. The AN6 `EnumPinEditor`/`IEnumValueProvider` work will
wire the editor selection, but the TypeId convention is already fully defined.

---

## EMIT bug fix (latent — not caught by resolution-only tests)

**Bug:** The original AN2 patch synthesized `IrTypeRef.FullName = typeRef.TypeId`, keeping the
`"global::"` prefix. But `StatementEmitter.TypeRefToCSharp` (StatementEmitter.cs line 866) emits
the fallback as `$"global::{t.FullName}"`, and EVERY other `IrTypeRef.FullName` in the compiler is
UNPREFIXED (`"System.Single"`, `"Fdp.Core.FixedString32"`, …) — emit adds the `global::`. So an
enum-typed Instance State field / WorkingState field / param / local emitted
`global::global::Ns.MyEnum` → CS0234 / parse error. The resolution-only tests asserted
`IsUnmanaged`/`SizeBytes`/no-BP1500/no-BP1503 but never exercised emit, so they passed despite the
broken generated source.

**Fix:** In `StaticTypeRegistry.TryResolve`, strip the sentinel when building the resolved
`IrTypeRef`:
```csharp
FullName = typeRef.TypeId.Substring("global::".Length),  // "Ns.MyEnum", not "global::Ns.MyEnum"
IsUnmanaged = true,
SizeBytes   = 4,
```
`TypeRefToCSharp` then re-adds `global::` exactly once → `global::Ns.MyEnum`.

### Contract (documented in a code comment in `StaticTypeRegistry.TryResolve` and here)

| Layer | Value for an enum | Example |
|-------|-------------------|---------|
| ASSET-level `BlueprintTypeRef.TypeId` | `"global::" + FQN` (explicit enum sentinel, per ENUM-DESIGN §RESOLVED / architect Q2) | `"global::Ns.MyEnum"` |
| Compiler-internal `IrTypeRef.FullName` | UNPREFIXED FQN (consistent with all other IrTypeRefs) | `"Ns.MyEnum"` |
| Emitted C# (`TypeRefToCSharp`) | `global::` re-added exactly once | `global::Ns.MyEnum` |

**AN6 (editor)** will emit enum pin/variable TypeIds with the `"global::"` prefix to match this
asset-level contract.

---

## Files changed

### Modified

1. **`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Catalogs/StaticTypeRegistry.cs`**
   - Lines 89–111 (before), 89–128 (after)
   - Split `TryResolve` final return into: TypeTable hit (fast path) + `global::` prefix fallback
     (AN2 enum path) + not-found return.
   - Synthesizes `IrTypeRef { FullName = typeRef.TypeId.Substring("global::".Length),
     IsUnmanaged = true, SizeBytes = 4 }`. The `global::` sentinel is STRIPPED so `FullName` is the
     unprefixed FQN (see "EMIT bug fix"). Doc comment documents the three-layer contract.

2. **`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage4_TypeResolveTests.cs`**
   - Added 3 new test methods in the AN2 section (before BP1502 block):
     - `TypeResolve_EnumTypeRef_GlobalPrefix_Resolves` — enum variable resolves in FieldTypes map,
       no BP1500.
     - `TypeResolve_EnumTypeRef_GlobalPrefix_IsUnmanagedSize4` — resolved IrTypeRef is unmanaged,
       SizeBytes=4.
     - `TypeResolve_EnumVariable_DoesNotEmitBP1503` — enum Instance variable does NOT emit BP1503
       (the critical gate).

3. **`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage7_EmitTests/InstanceEmitGoldenTests.cs`**
   - Added EMIT round-trip test `Instance_EnumVariable_EmitsSingleGlobalPrefix` (the gap the
     resolution-only tests missed): builds a minimal Instance asset with an enum-typed Variable
     (`TypeId = "global::SomeNamespace.SomeEnum"`), runs the Stage2..7 emit path (same as the emit
     golden tests), and asserts the generated source contains `global::SomeNamespace.SomeEnum Mode;`
     and does NOT contain `global::global::`.

---

## Verification

### Build
```
dotnet build Hrot.Blueprints.Compiler.csproj   →  0 errors, 0 warnings
```

### Tests
```
dotnet test --filter (TypeResolve_Enum|Instance_EnumVariable_EmitsSingleGlobalPrefix)
                                   →  4/4 passed (3 resolution + 1 emit round-trip)
dotnet test (full suite)           →  1465 passed / 4 failed / 8 skipped
```

**Pre-existing failures (unchanged, same as baseline):**
- `Library_EmitMatchesGoldenSource` (CRLF/bin-copy flake)
- `Synthesize_EqsResult_ScoreCrossed_IncludesThreshold`
- `TickFrame_1000Frames_AllocatesZeroBytes`
- `LibraryMath_GeneratedSource_Snapshot` (CRLF flake)

**0 new failures.**

---

## Deviations / notes

- **`BlueprintTypeRef` schema NOT extended.** The task's "stamp IsUnmanaged/SizeBytes into the
  persisted TypeRef" was structurally impossible (the fields do not exist) and architecturally
  unnecessary (the `global::` TypeId string is sufficient). This is the correct implementation
  per ENUM-DESIGN.md §RESOLVED Q3.
- **Editor stamping site not modified.** The `BlueprintDocumentFactory.CreateVariable` and
  `BlueprintVariablesWindow.AddVariable` sites already emit `TypeId` from `Type.FullName`. To
  make them emit the `global::` prefix for enum types, the caller needs to be aware the type is an
  enum and prepend `"global::"` — this is the AN6 work item (EnumPinEditor / IEnumValueProvider
  wiring), not AN2. AN2's contract (compiler accepts enum TypeRef as unmanaged) is fully met.
- **SizeBytes=4 hardcoded.** Enums with `byte`/`short`/`long` underlying types are rare and the
  size error would be caught by the downstream C# compiler (wrong-sized struct field). A future
  refinement could accept the actual underlying size via the TypeId convention (e.g.,
  `"global::Ns.MyEnum:1"` for byte-backed), but this is not needed for the current gates.
