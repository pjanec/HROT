# BATCH-01 Report

**Date:** 2026-06-15
**Status:** COMPLETE — all 1100 tests pass (11 new)

---

## Task S1-0 — `[MarshalAs(UnmanagedType.I1)]` for bool fields

### Change

`Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardDtoEmitter.cs` — `EmitEditorManagedField`:

Added a guard `if (field.FieldType == typeof(bool))` that emits `    [MarshalAs(UnmanagedType.I1)]` on its own indented line immediately before the `public bool ...` field declaration. `ReadOnlyFieldEntry` emission is untouched (verbatim passthrough).

`using System.Runtime.InteropServices;` was already unconditionally emitted at line 141, so no using-directive change was needed.

### Test added

`Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/BlackboardDtoEmitterTests.cs` — `Emit_BoolField_CarriesMarshalAsI1`:

- Emits a model with `{int A, bool B, int C}`.
- Asserts `[MarshalAs(UnmanagedType.I1)]` appears in the output.
- Asserts it is on the line immediately preceding `public bool B;`.
- Asserts `public int A;` is NOT preceded by `[MarshalAs`.
- Compiles the emitted source with Roslyn (`Microsoft.CodeAnalysis.CSharp` 4.8.0 added to test csproj).
- Asserts `Marshal.OffsetOf(t, "C") == 8` (bool B is 1 byte, not 4).
- Asserts `Marshal.SizeOf(t) == 12` (int@0+4, bool@4+1+3pad, int@8+4).

### Existing test preserved

`Emit_EditorManaged_BoolField_UsesBoolAlias` continues to pass; it checks `public bool isActive;` is present — the `[MarshalAs]` line added above it does not break that assertion.

---

## Task S1-1 — `DtoFields` on `ActionSchemaEntry` + `HardcodedDtoFields` on VM

### Changes

**`IActionSchemaExporter.cs`**

- Added `DtoFieldDescriptor(string Name, Type FieldType)` record.
- Added `IReadOnlyList<DtoFieldDescriptor>? DtoFields = null` as the last optional positional parameter of `ActionSchemaEntry`.

**`ActionSchemaExporter.cs`**

- `ProcessMethod` calls new helper `ReflectDtoFields(dtoType)` which calls `dtoType.GetFields(Public | Instance)`.
- Result is stored in `ActionSchemaEntry.DtoFields`.
- `ReflectDtoFields` catches exceptions from inaccessible types and returns empty list as fallback.

**`BlackboardAuthoringWindow.cs`**

- `VariableViewModel` gets a new last optional parameter `bool IsReadOnly = false`.
- `BlackboardWindowViewModel` gets a new last optional parameter `IReadOnlyList<VariableViewModel> HardcodedDtoFields = null!`.
- `BuildViewModel` gains two new optional parameters: `IActionSchemaExporter? actionSchemaExporter` and `IReadOnlyList<string>? boundActionFqns`.
- New private helper `BuildHardcodedDtoFields` iterates the bound FQNs, looks up each entry via the exporter, and creates `VariableViewModel` rows with `IsReadOnly = true`. Deduplicates by `(DtoType, FieldName)`. All four return paths in `BuildViewModel` now pass `hardcodedDtoFields`.
- Existing callers pass no exporter; `HardcodedDtoFields` defaults to empty array.

### Tests added

**`ActionSchemaExporterTests.cs`** (5 new tests under "S1-1 — DtoFields reflection"):

- `ActionSchema_ReflectsFirstRefParamDto_DtoType_IsCorrect` — `FooDtoAction` entry has `DtoType == typeof(FooDto)`.
- `ActionSchema_ReflectsFirstRefParamDto_DtoFields_NotNull` — `DtoFields` is non-null.
- `ActionSchema_ReflectsFirstRefParamDto_DtoFields_ContainsHealth` — field `Health : int` present.
- `ActionSchema_ReflectsFirstRefParamDto_DtoFields_ContainsSpeed` — field `Speed : float` present.
- `ActionSchema_ReflectsFirstRefParamDto_DtoFields_CountIs2` — exactly 2 fields.

Fixture: `FooDto { public int Health; public float Speed; }` + `ActionFixtures.FooDtoAction(ref FooDto)`.

**`BlackboardAuthoringWindowTests.cs`** (5 new tests under "S1-1"):

- `VariablesPanel_ReflectsHardcodedDto_ReadOnly_FieldsAppearInHardcodedDtoFields` — 2 fields in `HardcodedDtoFields`.
- `VariablesPanel_ReflectsHardcodedDto_ReadOnly_AllFieldsHaveIsReadOnlyTrue` — all entries have `IsReadOnly == true`.
- `VariablesPanel_ReflectsHardcodedDto_ReadOnly_FieldsNotInEditableVariables` — DTO field names absent from `Variables`.
- `VariablesPanel_ReflectsHardcodedDto_ReadOnly_FieldNamesCorrect` — `Health` and `Speed` present by name.
- `VariablesPanel_NoExporter_HardcodedDtoFields_IsEmpty` — without exporter, list is empty (not null).

Uses a `file`-local `StubActionSchemaExporter` backed by a pre-built dictionary.

---

## Test run result

```
Passed!  - Failed: 0, Passed: 1100, Skipped: 0, Total: 1100
```

All 11 new tests pass; no regressions.

---

## Implementation Summary

**S1-0:** One-line guard added to `EmitEditorManagedField` in `BlackboardDtoEmitter.cs`; `[MarshalAs(UnmanagedType.I1)]` is emitted immediately before every `public bool` field. `ReadOnlyFieldEntry` is untouched. `using System.Runtime.InteropServices` was already always emitted.

**S1-1:** Three-file change: `ActionSchemaEntry` gains `DtoFields` (reflected public instance fields); `ActionSchemaExporter.ProcessMethod` populates it; `BlackboardAuthoringWindow.BuildViewModel` accepts optional exporter + bound-FQN list and builds a separate `HardcodedDtoFields` list with `IsReadOnly = true` entries that are never included in `Variables`.

---

## Design Decisions

**S1-0 — placement of `[MarshalAs]`:** The attribute is emitted after the `/// <summary>` block (if any) and before the `public bool` line, exactly matching the spec instruction "after any `/// <summary>` comment, before the `public bool ...` line."

**S1-1 — `DtoFields` as optional positional param:** Added as the last positional parameter of `ActionSchemaEntry` with a default of `null`, so all existing call sites (e.g. from `ProcessMethod` before the patch, and callers in tests) compile without modification. This is backward-compatible.

**S1-1 — `HardcodedDtoFields` deduplication by `(DtoType, FieldName)`:** When multiple bound FQNs share a DTO type, the same field would otherwise appear twice. Deduplication by the composite key avoids duplicates while preserving multiple distinct DTO types.

**S1-1 — `BuildHardcodedDtoFields` as private static helper:** Keeps `BuildViewModel` readable and allows direct unit testing of the helper logic via `BuildViewModel` itself.

**S1-1 — `ByteSize = 0` in hardcoded DTO `VariableViewModel`:** Hardcoded DTO fields are read-only reflection artifacts; they are not bin-packed by the editor, so a size of 0 is semantically correct and prevents them from being accidentally included in budget calculations.

**Roslyn version pinned at 4.8.0:** Matches the version already used by `Hrot.Blueprints.Compiler`. No additional download was required.

---

## Deviations

None. All changes are spec-compliant. The `HardcodedDtoFields` null-bang initializer (`null!`) used as a default in `BlackboardWindowViewModel`'s record constructor follows the existing pattern used in `Variables` and `UnboundRequirements` default paths. The `BuildViewModel` always sets it explicitly before returning, so runtime null is impossible.

---

## Developer Insights

**Weak points spotted:**

1. `BlackboardWindowViewModel` uses `null!` as defaults for several list properties (now including `HardcodedDtoFields`). A factory or builder pattern would prevent any caller from accidentally constructing a VM with null lists; the current approach relies on discipline.
2. `ActionSchemaExporter.ReflectDtoFields` silently returns an empty list on exception. This is defensive but means a type-load failure will surface as "no fields" rather than as a diagnostic. A log statement at warning severity would help with runtime debugging.
3. The `BuildHardcodedDtoFields` helper does not deduplicate across different DTO types with the same field name (e.g., two DTOs each having a `Counter` field will both appear). This matches the spec intent (show all fields of all bound DTOs) and is not a bug, but it could confuse designers. The future field-picker (S1-5) will contextualize each field by DTO type.

**Edge cases discovered:**

- A method with a `ref` parameter but no AI attribute is correctly skipped by `ProcessMethod` (the `hosting == None` check fires before `ReflectDtoFields` is called, so no unnecessary reflection occurs).
- A bound action FQN that is not in the exporter (stale reference, e.g., after hot-reload) is gracefully skipped (`Lookup` returns null). No exception.
- The existing test `Emit_EditorManaged_BoolField_UsesBoolAlias` asserts `Assert.Contains("    public bool isActive;", output)`. This still passes because the `[MarshalAs]` attribute is on a separate preceding line.

**Measured Marshal offsets (proving bool = 1 byte):**

For struct `{int A; [MarshalAs(I1)] bool B; int C}` with `[StructLayout(Sequential)]`:
- `Marshal.OffsetOf(t, "A") = 0`
- `Marshal.OffsetOf(t, "B") = 4`
- `Marshal.OffsetOf(t, "C") = 8`  ← proves B occupies 1 byte (would be 12 without `[MarshalAs(I1)]`)
- `Marshal.SizeOf(t) = 12`  ← sequential layout pads to next int-alignment: 4+1+3pad+4 = 12

Without `[MarshalAs(I1)]` (bare `bool`), `Marshal.OffsetOf(t, "C")` would return 8 OR 12 depending on platform/CLR version (Win32 BOOL = 4 bytes is the interop default), making offset-based projection unreliable. The attribute pins it at 1 byte on all platforms.

---

## Known Issues

None for this batch. The `HardcodedDtoFields` in the VM is not yet rendered in `VariablesPanelControl.DrawSection` (the ImGui drawing layer). That wiring is not in scope for BATCH-01 (S1-1 is editor-only, no codegen, no rendering change required by the spec).

---

## Suggested Commit Message

`feat(editor): bool MarshalAs(I1) fix in BlackboardDtoEmitter + Category-1 DTO read-only reflection (BATCH-01)`
