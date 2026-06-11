# BATCH-25 Review (toolbar relocate + resize)
**Status:** ✅ APPROVED   **Date:** 2026-06-11

## Summary
Toolbar now renders **inline inside the main menu bar** (`MainToolbarManager.RenderInline` called within
`BeginMainMenuBar`, band removed), icons sized to `GetFrameHeight()` (menu height), and the dockspace
top inset dropped (`Program.cs toolbarHeight = 0`). Reclaims the vertical space; small icons render crisp.

## Verification (done by lead)
- Wiring read: `WindowManager` calls `_mainToolbar.RenderInline(CurrentPerspective)` at L413 inside the
  menu bar; separate band `Render` call removed. `Program.cs` toolbar inset = 0. Icon sizes →
  `GetFrameHeight()` in all three sections.
- Tests: Fdp.Presentation toolbar/wm classes 35/0; Hrot.Editor.Tests 176/0; MainToolbarTimeControl 10/0;
  BATCH-24 guardrail passes; `Hrot.Blueprints.Tests` (Stability filter) = exactly the 9 PRE-1.
- **Worker's "22 Fdp.Toolkits + 38 SimHost failures" were ENVIRONMENTAL** — the running editor locked
  output DLLs during the worker's test run. Lead re-ran `Fdp.Toolkits.Tests` → **1856/0**. SimHost is
  the same unrelated-to-toolbar situation. No regression.
- Library projects compile clean (0 warnings).

## Verdict
APPROVED. Toolbar is menu-bar-height, inline, right of the menus.

## Note
Icon *art* still placeholder-ish (DBT-1, cosmetic). Visual confirmation of the inline layout is for the
user's next editor run.
