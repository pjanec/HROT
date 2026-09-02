# BATCH-30 Report

**Batch:** BATCH-30 (MTB2-T1)  
**Developer:** Claude (pjanec)  
**Date:** 2026-06-12  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| MTB2-T1 (Item 4) | [x] | ComputeIconRect + iconScale + hover/toggle fills |

---

## 🧪 Testing Results

**Filtered Tests Passed:** 45 / 45  
**Failed:** 0  
**Skipped:** 0

**Final `dotnet test` summary line:**
```
Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45, Duration: 164 ms - Fdp.Presentation.Tests.dll (net8.0)
```

**New tests added (3):**

| Test | Assertions |
|------|-----------|
| `ComputeIconRect_CentersAtNinetyPercent` | Box (0,0)-(20,20) at 0.9 → Min≈(1,1), Max≈(19,19), size≈(18,18), equal margins both axes |
| `ComputeIconRect_NeverExceedsBox` | Scale 1.0 → rect == box; scale 0.5 → rect strictly inside, centered, equal margins |
| `ComputeIconRect_DefaultScaleIsNinety` | `DefaultIconScale` const == 0.9f; explicit 0.9 yields same result as the default |

**Existing tests still pass:** All 42 pre-existing `IconWidgetsTests` and `ToolbarCommandAdapterTests` — no regressions.

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

No significant issues. The existing `IconHandle` overloads were clean and easy to extend. The main design trade-off was choosing appropriate colors for the hover/toggle fills that are visually distinct yet theme-compatible.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

The existing toggle fill used a hardcoded dark gray `(0.3, 0.3, 0.3, 1.0)` that didn't adapt to the active theme. The new implementation uses `ImGuiCol.Header` from the style, which is theme-aware — an improvement, but the alpha values (0.25 for hover, 0.45 for toggle) are still hardcoded. A future enhancement could expose these as themeable parameters.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

1. **Fill area:** The hover/toggle fills span the **full button area** (size), not just the icon rect. This produces a traditional toolbar-button highlight. The alternative (filling only the 90% icon rect) would leave unfilled margins and look odd.

2. **Hover + toggle mutual exclusion:** When the icon is toggled AND hovered, only the toggle fill is shown (not both). This matches how most toolbars work — the "active/pressed" state takes visual priority over hover.

3. **Color source:** Used `ImGuiCol.Header` (the theme's selection/header color) rather than a hardcoded RGB. This adapts to the active theme. Alternatives considered: hardcoded blue `(0.26, 0.59, 0.98)`, using `ImGuiCol.ButtonHovered` — rejected because `Header` is the closest equivalent to "SelectionAccent" mentioned in the spec.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- `iconScale` could theoretically be passed as 0 or negative — `ComputeIconRect` handles 0 gracefully (zero-size rect at box center) and negative values produce a rect larger than the box (consistent math, no crash).
- The pressed shift (1px offset) was previously applied to the full-size image; it is now applied within the already-inset icon rect — the visual effect is proportionally identical.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

No new allocations. `ComputeIconRect` returns a value tuple (stack-only). The two `GetStyle().Colors` reads are array lookups that are trivially cheap. The `AddRectFilled` calls replace the previous `AddRect` border calls — same GPU draw-list cost.

---

## 🎨 Hover / Toggle Visual Choices (for lead runtime check)

| State | Color source | Alpha | Visual intent |
|-------|-------------|-------|---------------|
| **Hover** | `ImGuiCol.Header` (theme selection accent) | 0.25 | Subtle filled highlight behind the full button — clearly shows the icon is interactive without being distracting. |
| **Toggled** | `ImGuiCol.Header` (theme selection accent) | 0.45 | More opaque "active/pressed" fill, visually distinct from hover. The higher alpha makes it unambiguous which toolbar button is active. |
| **Disabled** | n/a | — | No fill drawn. Icon image dimmed to 28% alpha (unchanged behavior). |

The two states are clearly distinguishable: hover is a faint wash (~25% of the theme accent), toggle is nearly twice as opaque (~45%). The previous implementation had a 1px white border for hover (barely visible) and a dark gray `(0.3,0.3,0.3)` fill for toggle — the new approach gives both states more visual weight.

---

## 📁 Files Changed

| File | Change |
|------|--------|
| `FDP/Engine/Fdp.Presentation/ImGui/Icons/IconWidgets.cs` | Added `DefaultIconScale` const, `ComputeIconRect()` static helper. Added `float iconScale` parameter (default `DefaultIconScale`) to `IconButton(in IconHandle,…)` and `ToggleIcon(in IconHandle,…)`. Icon image draws into computed inset rect; full button hit/spacing box unchanged. Added theme-aware hover fill (`ImGuiCol.Header` @ 0.25α) and toggle fill (`ImGuiCol.Header` @ 0.45α); removed old thin hover border. |
| `FDP/Engine/Fdp.Presentation.Tests/ImGui/Icons/IconWidgetsTests.cs` | Added 3 tests: `ComputeIconRect_CentersAtNinetyPercent`, `ComputeIconRect_NeverExceedsBox`, `ComputeIconRect_DefaultScaleIsNinety`. |

**No other files touched.** No debug output, no temp files, no `#if false`, no `[Skip]`.

---

## ⚠️ Outstanding Issues / Next Steps

- Lead to runtime-verify hover/toggle visuals in the live editor (perspective buttons are the primary toggle consumers).
- Batch BATCH-31 (MTB2-T2: Save icon) can proceed.
