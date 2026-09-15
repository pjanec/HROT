# AN6 -- Blueprint enum data pins — Report

**Branch:** `blueprint-integ-1`
**Date:** 2026-06-06
**Status:** DONE — 21 new tests green, 0 new failures

---

## Summary

Enum-typed Blueprint data pins now get a combo editor (System B), completing the
end-to-end chain: author in editor → AN2 resolves as unmanaged → AN1 compiles to
`(global::FQN)N`.

---

## 1 — NodePinSchema stamping (the key link AN2 deferred)

**File:**
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/NodePinSchema.cs`

**Change:** `ReflectDataMembers` (the helper that decomposes a ChannelCommand
`ParamsTypeFqn` DTO into data-IN pins) previously returned raw CLR `FullName`
for all field types.  Added `EnumStampedTypeFqn(Type memberType)` which prefixes
the FQN with `"global::"` when `memberType.IsEnum`:

```csharp
private static string EnumStampedTypeFqn(Type memberType)
{
    if (memberType.IsEnum)
    {
        var fqn = memberType.FullName ?? memberType.Name;
        return "global::" + fqn;
    }
    return memberType.FullName ?? memberType.Name;
}
```

Called from both the field-loop and property-loop inside `ReflectDataMembers`.
Non-enum fields are unchanged.  This is the stamping site AN2 deferred ("AN6 will
wire the editor selection").

---

## 2 — `BlueprintEnumValueProvider` implementation

**File (new):**
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintEnumValueProvider.cs`

- Implements `IEnumValueProvider` (from `NodeEditor.Core.Interfaces`).
- `GetValues(TypeKey)`: if the `TypeKey.Id` starts with `"global::"`, strips the
  prefix, resolves the CLR `Type` via `Type.GetType` (fast path) then
  `AppDomain.CurrentDomain.GetAssemblies()` (slow path, scans once).  Uses
  `Enum.GetNames` / `Enum.GetValues` + `Convert.ToInt64` to build
  `EnumValueEntry[]` (Value = underlying integer as `long`, DisplayName = member
  name).  Returns empty for non-`global::` keys or unresolvable / non-enum types.
- Lazy cache: per `TypeKey.Id`, so repeated calls are O(1) after the first scan.
- `GetMaxInlineValues()` returns 8.

---

## 3 — `EnumSentinelPinEditorRegistry` wrapper

**File (new):**
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/EnumSentinelPinEditorRegistry.cs`

Thin `IPinDefaultValueEditorRegistry` wrapper.  `GetEditor(TypeKey)`:

- If `TypeKey.Id.StartsWith("global::")` → returns the shared
  `EnumPinEditor(provider)`.
- Otherwise → delegates to the wrapped inner registry.

`Register` / `RegisterFallback` forward to the inner registry (so FixedString32/64
registration done before wrapping still takes effect).

**Wiring in `BlueprintDocumentFactory.Build`**
(`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs`):

```csharp
var builtinRegistry = PinDefaultValueEditorRegistry.CreateWithBuiltins();
builtinRegistry.Register(new TypeKey(BlueprintTypeSystem.FixedString32), new StringPinEditor());
builtinRegistry.Register(new TypeKey(BlueprintTypeSystem.FixedString64), new StringPinEditor());
// Wrap with the enum-sentinel interceptor (AN6).
var enumProvider = new BlueprintEnumValueProvider();
IPinDefaultValueEditorRegistry editorRegistry =
    new EnumSentinelPinEditorRegistry(builtinRegistry, enumProvider);
```

`CreateWithBuiltins` is NOT edited (framework contract honored).

---

## 4 — `BlueprintPinModel.ParseValue` enum case

**File:**
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintPinModel.cs`

Added an early-exit before the existing `switch`:

```csharp
// Enum sentinel: "global::" prefix (AN2 contract).
if (!string.IsNullOrEmpty(typeId)
    && typeId.StartsWith("global::", StringComparison.Ordinal))
{
    if (string.IsNullOrEmpty(rawValue)) return 0L;
    return long.TryParse(rawValue, out var enumLong) ? enumLong : 0L;
}
```

Returns `long` (not `int`) to match `EnumPinEditor.Draw`'s `value is long` check.
Null/empty raw value → `0L` (type-zero for combo at index 0).
`FormatValue(long)` → `.ToString()` → decimal string stored in `Node.PinDefaults`.
Round-trip: persist `"2"` → parse `2L` → editor selects index 2 → commit `2L` →
`FormatValue` → `"2"`.

---

## 5 — `BlueprintTypeSystem` enum pin color

**File:**
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintTypeSystem.cs`

`GetPinColor` now returns a distinct lavender `(0.65, 0.55, 0.85, 1)` for any
`TypeKey.Id` starting with `"global::"`, instead of falling through to the generic
grey.  `TryGetTypeInfo` returns `false` for enum types (no entry in `_types` dict),
so the grey fallback in `GetPinColor` would have applied; now lavender renders.

---

## Files changed

### New
1. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintEnumValueProvider.cs`
2. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/EnumSentinelPinEditorRegistry.cs`
3. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/EnumPinTests.cs`

### Modified
4. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/NodePinSchema.cs`
   — `ReflectDataMembers` + new `EnumStampedTypeFqn` helper.
5. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintPinModel.cs`
   — `ParseValue` enum case (early-exit before the switch).
6. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs`
   — wrap `editorRegistry` with `EnumSentinelPinEditorRegistry`.
7. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintTypeSystem.cs`
   — lavender color for `global::` TypeKeys.

---

## Verification

### Build

```
dotnet build Hrot.Blueprints.Editor.csproj    →  0 errors, 0 warnings
dotnet build Hrot.Blueprints.Tests.csproj     →  0 errors, 8 pre-existing warnings (unchanged)
```

### New tests: 21/21 pass

```
EnumValueProvider_ReturnsMembers_ForGlobalPrefixedTypeKey      PASS  (GraphKind: 3 members)
EnumValueProvider_ReturnsEmpty_ForNonGlobalKey                 PASS
EnumValueProvider_ReturnsEmpty_ForUnresolvableFqn              PASS
EnumValueProvider_GetMaxInlineValues_IsAtLeastEight            PASS
EnumValueProvider_CachedCall_SameResult                        PASS
SentinelRegistry_ReturnsEnumPinEditor_ForGlobalKey             PASS
SentinelRegistry_DelegatesToInner_ForIntKey                    PASS  (IntPinEditor)
SentinelRegistry_DelegatesToInner_ForBoolKey                   PASS  (BoolPinEditor)
SentinelRegistry_ReturnsNull_ForUnknownNonGlobalKey            PASS
SentinelRegistry_Register_ForwardsToInner                      PASS  (FixedString32 → StringPinEditor)
ParseValue_EnumGlobalPrefix_ReturnsLong (×3)                   PASS  ("0"→0L, "2"→2L, "42"→42L)
ParseValue_EnumGlobalPrefix_NullOrEmpty_ReturnsZeroLong (×2)   PASS  (null→0L, ""→0L)
ParseValue_EnumGlobalPrefix_BadString_ReturnsZeroLong          PASS
ParseValue_NonGlobalPrefix_Unchanged                           PASS
FormatValue_Long_ReturnsDecimalString                          PASS  (2L → "2")
PinModel_Default_IsNonNull_ForEnumPin_WithRegistry             PASS  (Value = 0L)
PinModel_Default_ParsesLong_ForPersistedEnumDefault            PASS  ("1" → 1L)
NodePinSchema_ChannelCommandPins_StampsEnumFieldWithGlobalPrefix PASS
```

### Full Hrot.Blueprints suite

```
dotnet test Hrot.Blueprints.Tests.csproj
→  Passed: 1491, Failed: 4, Skipped: 8
```

**Failed (all 4 pre-existing, unchanged):**
- `Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` — pre-existing
- `Library_EmitMatchesGoldenSource` — CRLF snapshot flake (pre-existing)
- `TickFrame_1000Frames_AllocatesZeroBytes` — pre-existing
- `LibraryMath_GeneratedSource_Snapshot` — CRLF snapshot flake (pre-existing)

**0 new failures.**

### Hrot.Editor.AiShared.Tests

```
→  Passed: 832, Failed: 0
```

---

## REVIEW-V1 visual gate (not headless-verifiable)

The in-node combo render (`EnumPinEditor.Draw`) requires a running editor with an
ImGui context.  Confirmed NOT exercised in these headless tests.  REVIEW-V1
"enum pin shows combo" is the outstanding manual gate.

---

## Deviations / notes

- **Enum color:** chose lavender `(0.65, 0.55, 0.85, 1)` as a distinct color rather
  than leaving the grey fallback.  Trivial; easily changed at REVIEW-V1.
- **`TryGetTypeInfo` for enum pins:** returns `false` (no entry in the static `_types`
  dict).  The canvas renders the pin name without a type-label, same as FixedString
  types.  A future refinement could extract a short enum name from the FQN.
- **No `BlueprintTypeRef` schema extension.** Confirmed via AN2 — the fields do not
  exist; the `global::` TypeId string is the sole signal.  No JSON migration cost.
- **`IChannelCommandCatalog` has no `Changed` event.** The fake test catalog was
  simplified accordingly.
