# BATCH-30 — MTB2-T1: Generic toolbar icon UX (90% inset + clear hover/toggle)

**Task:** MTB2-T1 (Item 4) · **Model:** pro · **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`
**Detail:** `.dev/_DONE/main-toolbar-2/TASK-DETAIL.md` (section `MTB2-T1`) · **Design:** `.dev/_DONE/main-toolbar-2/DESIGN.md` (DEC-A3)

## Onboarding (read first; do NOT use codebase-memory tooling)
1. `.dev/.guides/DEV-GUIDE.md` — engineering rules.
2. This file + the `MTB2-T1` section of `.dev/_DONE/main-toolbar-2/TASK-DETAIL.md`.

## ⚙️ RULES (non-negotiable)
1. Do this batch's ONE objective only. Do NOT touch files outside the scope below. No drive-by refactors/renames.
2. NEVER make a build/test pass by hiding the problem: do NOT exclude assets, comment out / `[Skip]` tests,
   delete/weaken assertions, stub with `NotImplementedException`, suppress diagnostics, or `#if false`. If something
   can't be done as specified, STOP and report why.
3. Add the EXACT named tests below; they must assert real values and FAIL if the production code is wrong.
4. DO NOT STOP until the build has 0 warnings AND the test command shows `Failed: 0`. Run tests WITHOUT setting
   `BLUEPRINT_REGENERATE_SNAPSHOTS`. Re-run after each fix until green.
5. Report: exact files changed, exact tests added, paste the final `dotnet test` summary line. Describe what YOU
   changed (don't say "already existed"). Leave no litter (no debug writes, no temp files).

## Objective
Make toolbar icons breathe (~90% inset) and show clear hover + toggled states — **generically**, in the shared
widget, so every toolbar icon benefits. The hit/spacing box (layout) must NOT change.

## Scope — ONLY these files
- `FDP/Engine/Fdp.Presentation/ImGui/Icons/IconWidgets.cs` — the **`IconHandle` overloads**:
  `ToggleIcon(in IconHandle icon, string id, Vector2 size, ref bool isToggled, bool enabled = true, Vector4? tint = null)`
  and `IconButton(in IconHandle, …)` (around lines 211–330). Leave the `IconAtlas`-based overloads' external behavior
  intact (you may share a private helper).
- Tests: `FDP/Engine/Fdp.Presentation.Tests/ImGui/Icons/IconWidgetsTests.cs`.

## Requirements
1. Add a pure, ImGui-free helper:
   `public static (Vector2 Min, Vector2 Max) ComputeIconRect(Vector2 boxPos, Vector2 boxSize, float scale)`
   — returns the centered sub-rect at `scale` of the box (0.9 → 90%, centered with equal margins).
2. In the `IconHandle` `ToggleIcon`/`IconButton` overloads: keep the `InvisibleButton` (hit/spacing box) at the FULL
   `size`; draw the icon **image** into `ComputeIconRect(pos, size, iconScale)`. Add an optional trailing parameter
   `float iconScale = 0.9f` to both overloads (existing callers unchanged → default 0.9).
3. **Hover:** when `IsItemHovered() && enabled`, draw a clear **filled** highlight behind the icon (e.g. theme
   `SelectionAccent` at ~0.25 alpha) — not just the faint 1px border.
4. **Toggled:** when `isToggled && enabled`, draw a clearly-readable "active" fill (accent-tinted, more opaque than
   hover), visually distinct from hover.
5. Disabled stays dimmed; no hover/toggle visuals when `!enabled`.

> Note: this widget is shared beyond the toolbar — changing the **hit/spacing box** size would shift layout. Keep the
> `InvisibleButton` at full `size`; only the drawn image rect + the hover/toggle fills change.

## Tests — add to `IconWidgetsTests.cs` (EXACT names)
- `ComputeIconRect_CentersAtNinetyPercent` — box pos (0,0), size (20,20), scale 0.9 ⇒ Min ≈ (1,1), Max ≈ (19,19),
  size ≈ (18,18), equal margins (assert with a small float tolerance).
- `ComputeIconRect_NeverExceedsBox` — scale 1.0 ⇒ rect == box; scale 0.5 ⇒ rect strictly inside and centered.
- `ComputeIconRect_DefaultScaleIsNinety` — assert the `IconHandle` overload applies 0.9 when `iconScale` is omitted
  (e.g. expose/verify via `ComputeIconRect`'s use, or a const `DefaultIconScale == 0.9f` that the overload uses and
  the test asserts).

Existing `IconWidgetsTests` and `ToolbarCommandAdapterTests` must still pass.

## Build & test commands (run WITHOUT BLUEPRINT_REGENERATE_SNAPSHOTS)
```
dotnet build FDP/Engine/Fdp.Presentation/Fdp.Presentation.csproj
dotnet test  FDP/Engine/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj ^
  --filter "FullyQualifiedName~IconWidgets|FullyQualifiedName~ToolbarCommandAdapter"
```
(The FULL `Fdp.Presentation.Tests` suite is known to deadlock — use the class filter above. Do NOT "fix" that by
disabling tests.)

## Definition of done
- `ComputeIconRect` added + the three named tests pass; `IconHandle` overloads inset to `iconScale` (default 0.9),
  clear hover fill, clear toggled fill; hit/spacing box unchanged.
- Build 0 warnings; the filtered test command shows `Failed: 0` (existing icon/toolbar tests still pass).
- Write `.dev/_DONE/main-toolbar-2/reports/BATCH-30-REPORT.md`: files changed, tests added, final test summary, and a note
  on the hover/toggle visual choices (colors/alpha) for the lead's runtime check.

If something cannot be done as specified, STOP and report why rather than stubbing it.
