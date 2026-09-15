# BATCH-03 REPORT — Pill Glyph + Param Label (TASK-BT-03)

**Date:** 2026-06-12
**Status:** ✅ COMPLETE

## Summary

Implemented per-type glyphs and parameter-including labels in `BTreePillAttachmentModel`, replacing the bare `DecoratorType.ToString()` label and `null` glyph. Decorator pills now read like "Repeater x3 / Cooldown 2s" rather than "Repeater / Cooldown".

## Files Changed

| File | Change |
|------|--------|
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BTreeGraphModel.cs` | Modified `BTreePillAttachmentModel.Glyph` and `Label` properties (+26/-2 lines) |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Model/BTreePillLabelTests.cs` | **NEW** — 5 test methods (11 test cases) |

## Implementation Details

### `Glyph` — per-type ASCII-safe symbols

| DecoratorType | Glyph |
|---------------|-------|
| Inverter | `!` |
| Repeater | `R` |
| Cooldown | `C` |
| ForceSuccess | `S` |
| ForceFailure | `F` |
| UntilSuccess | `U+` |
| UntilFailure | `U-` |
| (default/unknown) | `?` |

### `Label` — parameter-including labels

- **Repeater** → `"x{IntParam ?? 1}"` (e.g. `"x3"`, `"x1"` when null)
- **Cooldown** → `"{FloatParam ?? 0f}s"` (e.g. `"2s"`, `"2.5s"`)
- **Inverter, ForceSuccess, ForceFailure, UntilSuccess, UntilFailure** → uses `nameof(NodeType.*)` for short human-readable label
- **Other types** → falls back to `DecoratorType.ToString()`

### Locale safety

Cooldown label uses `FormattableString.Invariant(...)` to guarantee decimal-dot formatting regardless of current culture — the same fix class as documented for prior locale bugs.

## Tests (headless, `Model/BTreePillLabelTests.cs`)

| Test | What it asserts |
|------|-----------------|
| `Repeater_LabelIncludesCount` | IntParam=3 → Label contains "3", Glyph non-null |
| `Cooldown_LabelIncludesDuration` | FloatParam=2f → Label contains "2" and "s", Glyph non-null |
| `Cooldown_LabelIsInvariant` | FloatParam=2.5f under `de-DE` culture → Label contains "2.5" (dot), does NOT contain "2,5" |
| `Inverter_HasGlyphAndLabel` | Inverter pill → Glyph & Label both non-null/non-empty |
| `AllDecoratorTypes_HaveNonNullGlyph` | `[Theory]` over all 7 decorator types — each Glyph non-null/non-empty |

## Build & Test Results

```
dotnet build Hrot.BTree.Editor.csproj           → 0 warnings, 0 errors
dotnet build IOS-IG-SimHost.sln                  → 0 errors (only pre-existing warnings in other projects)
dotnet test Hrot.BTree.Editor.Tests              → 469 passed, 0 failed, 0 skipped
```

No new warnings introduced. Only `Glyph` and `Label` properties were changed — all other properties (`Id`, `HostNodeId`, `Category`, `Tooltip`, `State`, `StackIndex`) untouched.

## Conformance to Working Agreement

- [x] One task only (TASK-BT-03)
- [x] No excluding files / suppressing diagnostics / weakening tests
- [x] Finished without asking — build clean, Failed: 0
- [x] Tests assert real values (not null checks only)
- [x] Litter-free (only the two intended files changed/added)
- [x] Report written
