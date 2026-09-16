# BATCH-47 Report — BUG-A13: Unify toolbar icon chrome

**Batch:** BATCH-47
**Developer:** Claude (lead)
**Date:** 2026-06-12
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| Part A — Shared chrome helper (`IconButtonChrome.cs`) | ✅ Done | New file in `Fdp.Presentation.Icons` |
| Part B — `IconWidgets.cs`: `ToggleIcon` (IconAtlas, ~L68) | ✅ Done | Replaced gray fill + white rect with shared chrome |
| Part B — `IconWidgets.cs`: `AlternatingFaceToggleIcon` (~L112) | ✅ Done | Replaced white rect with `DrawHoverFrame` |
| Part B — `IconWidgets.cs`: `DropdownFaceIcon` (~L152) | ✅ Done | Replaced white rect with `DrawHoverFrame` |
| Part B — `IconWidgets.cs`: `ToggleIcon` (IconHandle, ~L289) | ✅ Done | Replaced 3 bespoke draw blocks with shared chrome |
| Part C — `TransportIconRenderer.cs`: `DrawButton` (~L52) | ✅ Done | Removed hover fill + frame; uses `DrawHoverFrame` |
| Deviation — `TransportIcons.cs` (Hrot): `DrawTransportButton` | ✅ Done | Same pattern fix; see deviation section |
| Build — 0 warnings | ✅ Done | All 4 projects: 0 Warnings, 0 Errors |
| Tests — named suites `Failed: 0` | ✅ Done | No test assertion changes needed |
| Report | ✅ Done | This file |

---

## 🧪 Testing Results

### Fdp.Presentation.Tests — Icon tests (filtered to `FullyQualifiedName~Icons`)
```
Passed!  - Failed:     0, Passed:    51, Skipped:     0, Total:    51, Duration: 147 ms
```
All 51 icon-related tests pass. The 11 failures in the full suite run are pre-existing `Vis2D/Gizmos` NRE crashes (`DebugPrimitiveRenderer2D`, `DebugGizmoLayer`, etc.) — unrelated to this batch.

### Hrot.Presentation.Tests — Full suite
```
Passed!  - Failed:     0, Passed:    60, Skipped:     0, Total:    60, Duration: 96 ms
```
All 60 tests pass, including:
- `TransportIconsTests` (5 tests: Draw_AllShapes_Headless_NoThrow, FormatRate_*, FormatTime_*, TimeRates_HasExpectedValues)
- `MainToolbarTimeControlTests` (gating/logic tests for toolbar section)
- `ClusterTimeControlStatusBarSectionTests`

### Build
| Project | Warnings | Errors |
|---------|----------|--------|
| `Fdp.Presentation` | 0 | 0 |
| `Hrot.Presentation` | 0 | 0 |
| `Fdp.Presentation.Tests` | 0 | 0 |
| `Hrot.Presentation.Tests` | 0 | 0 |

### Test assertions updated
**None.** No existing test asserted the old hover fill color (white@0.12, white@0.55, gray@0.3) or the old hover rect (white@0.8, white@0.55). All existing tests verify behavior (no-throw, return values, state flipping), not pixel-level rendering. The shared chrome is a pure rendering change that preserves all behavioral semantics.

---

## 📝 Developer Insights

### Q1: What issues did you encounter during implementation? How did you resolve them?

No issues with the code changes themselves — the spec was precise. One architectural discovery: the codebase has **two independent transport-icon rendering paths**:
- `TransportIconRenderer.DrawButton` (FDP) — used by Replay Browser panels
- `TransportIcons.DrawTransportButton` (Hrot) — used by Main Toolbar and Status Bar time-control sections

Both had identical bespoke hover fill+frame code. The spec only named `TransportIconRenderer.cs` for Part C, but fixing only it would not make the toolbar time-control icons match (they go through `TransportIcons.cs`). I fixed both — see Deviation 1 below.

### Q2: Did you spot any weak points in the existing codebase? What would you improve?

Yes — the duplication between `TransportIconRenderer` (FDP) and `TransportIcons` (Hrot) is a maintenance hazard. They have different shape enums (`TransportShape` vs `BtnShape`), different `DrawShape` implementations, but nearly identical `DrawButton`/`DrawTransportButton` logic. Long-term, `TransportIcons` should delegate to `TransportIconRenderer` (or they should merge), but that requires aligning the shape enums first. Out of scope for this batch.

### Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?

**Decision 1: Separate file `IconButtonChrome.cs`** rather than adding static methods to `IconWidgets`. Rationale: keeps the chrome helpers independent — they serve both `IconWidgets` (bitmap) and `TransportIconRenderer` (vector). Attaching them to `IconWidgets` would create a misleading dependency impression. The spec explicitly allowed either.

**Decision 2: `DrawToggleFill`/`DrawHoverFrame` accept raw `bool` params.** The spec-code shows `enabled && isToggled` and `enabled && isHovered` at call sites. The helpers themselves just take `toggled`/`hovered` bools — the `enabled` gating happens at the call site. This keeps the helpers simple (they draw when told; they don't encode business rules about enabled state).

**Decision 3: `DrawHoverFrame` uses `new Vector2(size, size)` for transport (square), `size` for icon widgets (may be rectangular).** Transport icons are always square; icon handle toggle icons use the full `size` parameter (which is the hit/spacing box). The frame is drawn relative to the hit box, consistent with the spec.

### Q4: What edge cases did you discover that weren't mentioned in the spec?

- **Disabled icons:** Both `DrawToggleFill` and `DrawHoverFrame` receive `enabled && …` gating at the call site, so disabled icons never show chrome. This was already the behavior before (the old code also didn't draw hover/toggle on disabled icons). No regression.
- **Toggled + hovered composition:** When both flags are true, `DrawToggleFill` draws the blue fill, then the glyph, then `DrawHoverFrame` draws the inset white frame on top. This naturally composes and the frame remains visible over the blue fill because it's drawn after and uses 0.9 alpha. The old code had a separate white overlay `AddRectFilled` for this case — the new scheme handles it through draw order alone.

### Q5: Are there any performance concerns or optimization opportunities you noticed?

No performance concerns. The shared helpers add no measurable overhead:
- Each helper is a single `if` guard + one `ImDrawList` call, identical to the code it replaces.
- The old code had up to 3 draw calls per hover/toggle state (fill + overlay + border); the new scheme has at most 2 (`DrawToggleFill` + `DrawHoverFrame`).
- The `ImDrawListPtr` is passed by value (a struct wrapper around a pointer), so no allocation.

---

## ⚠️ Deviations

### Deviation 1: Fixed `TransportIcons.DrawTransportButton` (Hrot) — not named in the spec

**What:** Applied the same shared-chrome fix to `Hrot.Presentation/Panels/TransportIcons.cs` `DrawTransportButton`.

**Why:** The spec names `TransportIconRenderer.cs` (FDP) for Part C, but the Main Toolbar and Status Bar time-control icons actually render through `TransportIcons.DrawTransportButton` (Hrot). This file has the identical bespoke hover-fill + hover-frame pattern (white@0.12 fill, white@0.55 frame). If only `TransportIconRenderer` were fixed, the toolbar time-control icons would NOT use the shared chrome and would NOT match — violating the goal "New Asset + perspective-switch + time-control all match."

**Benefit:** All toolbar icons (New Asset, perspective-switch, time-control) now show the SAME hover (inset white frame, visible top edge) and toggle (blue fill).

**Risk:** None. `TransportIcons` is in `Hrot.Presentation`, which already references `Fdp.Presentation` (verified in `.csproj`). The shape geometry, formatting helpers, and behavioral semantics are unchanged.

**Recommendation:** Keep this change. It's necessary for the DoD.

---

## 📁 Files Changed

| File | Change |
|------|--------|
| `FDP/Engine/Fdp.Presentation/ImGui/Icons/IconButtonChrome.cs` | **NEW** — shared `DrawToggleFill` + `DrawHoverFrame` for both renderers |
| `FDP/Engine/Fdp.Presentation/ImGui/Icons/IconWidgets.cs` | 4 overloads routed through shared chrome; XML docs updated |
| `FDP/Engine/Fdp.Presentation/ImGui/Icons/TransportIconRenderer.cs` | `DrawButton`: removed bespoke hover fill+frame; uses `DrawHoverFrame` |
| `Hrot/Engine/Hrot.Presentation/Panels/TransportIcons.cs` | `DrawTransportButton`: same fix; added `using Fdp.Presentation.Icons` |

---

## ⚠️ Outstanding Issues / Next Steps

- [ ] Lead should runtime-verify the visual result per the spec (all toolbar icons match — New Asset, perspective-switch, time-control)
- [ ] Long-term: consider merging `TransportIcons` (Hrot) into `TransportIconRenderer` (FDP) or having the former delegate to the latter, to eliminate the duplicated shape rendering code. Out of scope for BUG-A13.
