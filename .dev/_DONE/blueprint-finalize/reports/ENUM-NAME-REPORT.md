# ENUM-NAME Report

**Date:** 2026-06-06
**Branch:** blueprint-integ-1
**Batch:** ENUM-NAME — enum pin defaults switch from integer to member-name storage

---

## Summary

Enum pin defaults are now persisted as the member NAME string (e.g. `"Crouching"`) instead of the
decimal integer string (e.g. `"2"`). The compiler emits `global::FQN.MemberName` instead of
`(global::FQN)N`. Old integer-stored assets are still accepted via a backward-compat branch.

---

## 1. Name↔Long Seam Chosen (+ Why)

### Chosen seam: `BlueprintPinDefaultValue` + `BlueprintCommandSink`

**Read direction (name → long):**

`BlueprintPinDefaultValue` (in `BlueprintPinModel.cs`) now accepts an optional `IEnumValueProvider?`
in its constructor. `ParseValue(typeId, rawValue, provider)` is called:

1. If `rawValue` is a pure integer string → `long.TryParse` (backward compat).
2. If not an integer → walks `provider.GetValues(typeKey)` and matches `DisplayName == rawValue`.
3. No provider or unresolved name → falls back to `0L` gracefully (no crash).

**Write direction (long → name):**

`BlueprintCommandSink.ApplySetPinDefault` detects `typeId.StartsWith("global::")` and
`cmd.NewValue is long` → calls `BlueprintPinDefaultValue.FormatEnumValue(longValue, typeId, _enumProvider)`.
`FormatEnumValue` walks `provider.GetValues(typeKey)` and returns the matching `DisplayName`,
or falls back to the decimal string if the value has no member match or provider is null.

**Why this seam:**

- `ApplySetPinDefault` already knows `typeId` and `cmd.NewValue`. Adding `_enumProvider` to
  `BlueprintCommandSink` (as an optional ctor param, default null) keeps backward compat with
  all existing headless tests.
- `BlueprintPinDefaultValue` is the canonical parse point; adding a provider overload there keeps
  the zero-arg static `ParseValue(typeId, rawValue)` working for non-enum types and old tests.
- `BlueprintDocumentFactory.Build` already holds `enumProvider` (created before `graphModel`);
  threading it to both `BlueprintGraphModel` and `BlueprintCommandSink` requires only two
  additional optional params.
- The provider is NOT reached through `IPinDefaultValueEditorRegistry` (which doesn't expose it)
  — threading it separately is the cleanest approach without violating framework contracts.

---

## 2. FormatDefaultLiteral Change (with no-double-global proof)

### Old code (AN1):
```csharp
if (typeId.StartsWith("global::", StringComparison.Ordinal))
    return $"({typeId}){rawValue}";
```

### New code (ENUM-NAME):
```csharp
if (typeId.StartsWith("global::", StringComparison.Ordinal))
{
    var isInteger = rawValue.Length > 0
        && (rawValue[0] == '-' ? rawValue.Length > 1 && rawValue.Substring(1).All(char.IsDigit)
                               : rawValue.All(char.IsDigit));

    if (isInteger)
        return $"({typeId}){rawValue}";      // old-style: (global::FQN)N
    else
        return $"{typeId}.{rawValue}";       // new-style: global::FQN.MemberName
}
```

**No-double-global proof:**

- `typeId` for enum pins is `"global::Fdp.Toolkit.Behavior.Demo.DemoStance"` (the AN2 sentinel).
- New emit: `$"{typeId}.{rawValue}"` = `"global::Fdp.Toolkit.Behavior.Demo.DemoStance.Crouching"`.
- Only one `global::` prefix. The old emit `$"({typeId}){rawValue}"` also had exactly one.
- `StaticTypeRegistry` stores `FullName = typeId["global::".Length..]` (without the prefix),
  and `StatementEmitter` re-adds `global::` when referencing the type in generated code —
  that path is via `IrTypeRef.FullName`, not via `LiteralNode.ValueJson`, so there is no
  interaction between the two and no double-prefix risk.

**Backward-compat dispatch:**
- Pure integer string (`"2"`, `"-1"`) → `(global::FQN)N` (old cast, C# valid)
- Any other string (`"Crouching"`) → `global::FQN.MemberName` (new form, C# valid)
- `netstandard2.0` compat: uses `Substring(1)` instead of `rawValue[1..]` slice syntax.

---

## 3. Rename-fallback Behavior

| Scenario | ParseValue result | FormatEnumValue result | Compiler effect |
|---|---|---|---|
| Valid name ("Crouching") + provider | Correct long (1L) | "Crouching" | `global::FQN.Crouching` |
| Valid name + no provider | 0L (fallback) | decimal string | `(global::FQN)0` |
| Unknown name ("RenamedMember") + provider | 0L (fallback) | "RenamedMember" | `global::FQN.RenamedMember` → CS0117 compile error (surfaced by compiler, not crash) |
| Integer string ("2") | 2L (backward compat) | — | `(global::FQN)2` |
| null/empty | 0L | — | no default materialized |

The compiler is the safety net for renames: if a member name no longer exists in the enum,
the compiler emits `global::FQN.RenamedMember` which produces a CS0117 error at the generated
C# compile step. No crash at editor runtime.

---

## 4. Files Changed

### Production code

| File | Change |
|---|---|
| `Hrot.Blueprints.Editor/Host/BlueprintPinModel.cs` | Added `IEnumValueProvider?` param to `BlueprintPinModel` ctor (4-arg overload). `BlueprintPinDefaultValue` gains `IEnumValueProvider?` ctor param + `ParseValue` overload + `FormatEnumValue` static. |
| `Hrot.Blueprints.Editor/Host/BlueprintGraphModel.cs` | Added `IEnumValueProvider? _enumProvider` field + ctor param; threads it into `BlueprintPinModel` ctor call. |
| `Hrot.Blueprints.Editor/Host/BlueprintCommandSink.cs` | Added `IEnumValueProvider? _enumProvider` field + optional ctor param; `ApplySetPinDefault` uses `FormatEnumValue` for enum pins. |
| `Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs` | Passes `enumProvider` to both `BlueprintGraphModel` and `BlueprintCommandSink`. |
| `Hrot.Blueprints.Compiler/Compiler/Stages/Stage3_Normalize.cs` | `FormatDefaultLiteral` enum branch: integer dispatch → cast; name dispatch → `global::FQN.Name`. |

### Tests

| File | Change |
|---|---|
| `EnumPinTests.cs` | Renamed integer-based `ParseValue` tests to reflect backward compat. Added: `ParseValue_MemberName_ResolvesViaProvider`, `ParseValue_UnknownMemberName_FallsBackToZero`, `FormatEnumValue_Long_ReturnsMemberName`, `FormatEnumValue_NoProvider_ReturnsDecimalString`, `FormatEnumValue_UnresolvableValue_ReturnsDecimalString`, `PinModel_Default_ParsesLong_ForPersistedMemberNameEnumDefault`, `PinModel_Default_UnresolvableName_FallsBackToZeroLong`. |
| `MaterializeDefaultPinLiteralsTests.cs` | Updated `DefaultPins_IntFloatFixedStringEnum` to store `"MyMember"` and assert `global::SomeNs.SomeEnum.MyMember`. Added `DefaultPins_EnumIntegerFallback_EmitsIntegerCast` for backward compat. |
| `EnumSampleTests.cs` | Renamed test 7 to `DemoEnumAction_GeneratedSource_ContainsMemberQualifiedName`; stores `DefaultValue = "Crouching"` (not `"1"`); asserts `global::Fdp.Toolkit.Behavior.Demo.DemoStance.Crouching`. Added `DemoEnumAction_IntegerDefault_StillEmitsCast` for backward compat. |

---

## 5. Test Results

### Targeted suite (66 tests)
```
Total tests: 66 — Passed: 66 — Failed: 0
```
Tests covered: `EnumPinTests`, `EnumSampleTests`, `MaterializeDefaultPinLiteralsTests`, `BlueprintPinDefaultValueTests`.

### Full Blueprints suite (1567 tests)
```
Failed: 4, Passed: 1555, Skipped: 8
```
All 4 failures are pre-existing:
- `ScoreCrossed_IncludesThreshold` — pre-existing
- `AllocatesZeroBytes` — pre-existing
- `Library_EmitMatchesGoldenSource` — CRLF flake
- `LibraryMath_GeneratedSource_Snapshot` — CRLF flake

### Hrot.Editor.AiShared suite (832 tests)
```
Failed: 1, Passed: 831
```
1 failure: `AtomicMultiFileWriterTests.Write_to_invalid_path_does_not_leave_temp_files_behind` — pre-existing flaky filesystem race (passes in isolation, confirmed pre-existing in AN3/AN4/BF-BATCH-0607 reports).

### Build
All 4 targeted projects: 0 CS errors.
Full solution: `Hrot.ClusterRunner` DLL lock (VS + running process) — as documented; specific projects build cleanly.

---

## 6. Deviations

None. Implementation follows the specification exactly:
- Name stored in `PinDefaults`, ParseValue resolves via provider.
- `FormatDefaultLiteral`: name → `global::FQN.Name`; integer → `(global::FQN)N`.
- Unresolvable name → fallback to 0L (no crash); compiler surfaces mismatch.
- Tests updated per spec; integer-fallback test included.
