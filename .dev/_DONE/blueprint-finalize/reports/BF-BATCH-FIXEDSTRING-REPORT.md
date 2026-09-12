# BF-BATCH-FIXEDSTRING Report

**Branch:** `blueprint-integ-1`
**Date:** 2026-06-06
**Scope:** Add `Fdp.Core.FixedString32` / `FixedString64` as blueprint string pin types (recognized + editable).

---

## Summary

All five production tasks completed. 11 new headless tests added, all passing. Zero new test failures.

---

## Files Changed

### 1. StaticTypeRegistry.cs
**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Catalogs/StaticTypeRegistry.cs`
**Lines added:** after the FDP.Eqs entry (~line 69), before the "Common aliases" block.
```csharp
// Fdp.Core fixed-length string value types (unmanaged, blittable; preferred over System.String in state)
["Fdp.Core.FixedString32"] = Unmanaged("Fdp.Core.FixedString32", 32),
["Fdp.Core.FixedString64"] = Unmanaged("Fdp.Core.FixedString64", 64),
```
Both are `IsUnmanaged=true`, `SizeBytes=32/64` respectively.

### 2. BlueprintTypeSystem.cs
**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintTypeSystem.cs`

Three hunks:

a) **Well-known constants** (after `Entity`):
```csharp
public const string FixedString32 = "Fdp.Core.FixedString32";
public const string FixedString64 = "Fdp.Core.FixedString64";
```

b) **`_types` color/name entries** (after EqsSensorHandle):
```csharp
[FixedString32] = (new Vector4(0.25f, 0.75f, 0.55f, 1f), "FixedString32"),
[FixedString64] = (new Vector4(0.25f, 0.65f, 0.50f, 1f), "FixedString64"),
```
Color: teal-green, distinct from String (orange), Entity (cyan-green).

c) **`SelectableTypeIds`** — appended both constants at the end:
```csharp
FixedString32, FixedString64,
```

### 3. BlueprintDocumentFactory.cs
**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs`

- Added `using NodeEditor.Primitives;` (required for `TypeKey`).
- After `PinDefaultValueEditorRegistry.CreateWithBuiltins()` call (~line 123), added host-side registration:
```csharp
editorRegistry.Register(new TypeKey(BlueprintTypeSystem.FixedString32), new StringPinEditor());
editorRegistry.Register(new TypeKey(BlueprintTypeSystem.FixedString64), new StringPinEditor());
```
`StringPinEditor` is already in `NodeEditor.UI.MiniEditors` (using already present). Reuses existing editor; no new editor class created. `CreateWithBuiltins()` in the NodeEdit framework was NOT modified.

### 4. BlueprintPinModel.cs
**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintPinModel.cs`

In `ParseValue`, null/empty branch added:
```csharp
"Fdp.Core.FixedString32" => (object)"",
"Fdp.Core.FixedString64" => (object)"",
```
Non-null rawValue falls through to `_ => rawValue` (string/raw pass-through) — mirrors `System.String` behavior exactly.

### 5. Demo Recipe — EditorTypesDemo.bp.json (both copies)
**Files:**
- `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/EditorTypesDemo.bp.json`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/EditorTypesDemo.bp.json`

Added a new `FunctionCall` node (node `f7000007-0090-bb90-0090-000000000001`) with one unconnected In-data pin of type `Fdp.Core.FixedString32` at canvas position `(800, 400)`. This is the same pattern as the existing String/Vector/Quaternion demo nodes (IsPure=true, no TargetTypeId/MethodName, no links). Node and pin GUIDs follow the existing `f70000007-00NN` / `e7dd0007-00NN` stable-ID convention. Also updated `ConceptsTaught` to mention FixedString32/64 and the host-side StringPinEditor wiring.

### 6. Test file (new)
**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/FixedStringPinTests.cs`

11 new headless tests:
- `StaticTypeRegistry_Resolves_FixedStringTypes` (×2, theory) — IsUnmanaged=true, SizeBytes=32/64
- `EditorRegistry_ReturnsNonNullEditor_ForFixedStringTypes` (×2, theory) — StringPinEditor returned after host-side registration
- `ParseValue_NullOrEmpty_ReturnsEmptyString` (×2, theory) — null/empty → ""
- `ParseValue_NonEmpty_ReturnsRawString` (×2, theory) — raw string round-trip
- `EditorTypesDemo_DeserializesAndContainsFixedString32Pin` — recipe has the new pin
- `EditorTypesDemo_FixedStringPin_DefaultIsNonNull_WhenRegistrySupplied` — pinModel.Default non-null, Value=""
- `EditorTypesDemo_CompilesWithNoErrors` — BlueprintCompiler.Compile → 0 error diagnostics

---

## Build Gate

```
dotnet build IOS-IG-SimHost.sln -c Debug --no-incremental
```

**Result:** Zero `error CS`. 96 MSB3027/MSB3021 copy-lock errors only — all from `Hrot.ClusterRunner` (process 59280) + `Microsoft Visual Studio 2022` (59284) locking output DLLs. These are running-editor copy conflicts, not compile errors, as expected per batch instructions.

Blueprints test project standalone build: **0 warnings, 0 errors**.

---

## Test Results (no BLUEPRINT_REGENERATE_SNAPSHOTS)

```
Total tests: 1473
     Passed: 1461
     Failed: 4
    Skipped: 8
```

**Failing tests (4 — all pre-existing, none attributable to this batch):**

| Test | Category |
|------|----------|
| `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` | Known: "ConditionSummary ScoreCrossed" (pre-existing) |
| `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes` | Known: "AllocationFree AllocatesZeroBytes" (pre-existing) |
| `LibraryEmitGoldenTests.Library_EmitMatchesGoldenSource` | Known: Library demo snapshot bin-copy/line-ending quirk (pre-existing) |
| `LibraryMathDemoTests.LibraryMath_GeneratedSource_Snapshot` | Known: LibraryMath demo snapshot bin-copy/line-ending quirk (pre-existing) |

**0 NEW failures attributable to this batch.**

**New FixedString tests:** 11/11 passed.

---

## Out of Scope (noted per instructions)

**Stage3 default-literal materialization:** `Stage3_Normalize.MaterializeDefaultPinLiterals` is confirmed a no-op stub. Inline pin default values written via `Node.PinDefaults` are persisted in JSON and round-trip through the editor, but are NOT consumed by the compiler today for ANY type. Making inline defaults actually compile is a separate cross-cutting Stage3 feature — out of scope for this batch.

---

## Constraints Verified

- Branch `blueprint-integ-1`: confirmed.
- `CreateWithBuiltins()` NOT modified (framework untouched).
- `StringPinEditor` reused; no new editor class created.
- `System.String` handling NOT removed.
- `EditorSubsystem` / `RecipeCreateModal` / `AssetBrowserWindow` NOT touched.
- `Count*` / `Loco1` / `InlineEd1` `.bp.json` files NOT touched.
- Stage3 materialization NOT implemented.
- No commit made (lead commits).
- No snapshot regeneration.
