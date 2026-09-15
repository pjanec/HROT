# BATCH-08 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-11

## Summary
MTB-P3-T3/T4: relocated perspective switching out of the menu bar into a top-level checkable
"Perspective" menu, and added a `PerspectiveToolbarSection` radio-group driven by per-perspective
`ManagedWindow.IconKey`.

## Issues Found
No issues found.

## Verification (done by lead)
- `dotnet build IOS-IG-SimHost.sln` → 0 errors, 0 new warnings (touched `Fdp.Presentation` has TWAE).
- New tests run by lead: PerspectiveMenuTests(10) + PerspectiveToolbarTests(9) + existing
  WindowManagerTests(29) → **48 passed, 0 failed**.
- Removal verified: `RenderPerspectiveSwitcher` method + its `BeginMainMenuBar` call are gone
  (only doc/test comments reference the name); compile-enforced. `TitleBarColor` preserved. This
  removal is the §8.1-mandated relocation, not a forbidden legacy deletion.
- Seams read: `GetPerspectives()` (distinct/sorted), `BuildPerspectiveMenuModel()` (perspective +
  isChecked), `SelectPerspective()`→`SwitchPerspective`, `GetPerspectiveIconKey()` (first non-null
  among the perspective's windows), `PerspectiveToolbarSection.BuildRadioModel()` (Perspective,
  IsToggled, HasIcon = key!=null && provider.TryGet) + `OnSelect()` + text fallback when !HasIcon.
- `ManagedWindow.IconKey` is the only public-API addition (additive, mirrors `TitleBarColor`).
  No scope creep.

## Test Quality
Good. Menu tests assert distinct+sorted enumeration, checked==current, select switches. Radio tests
assert exactly-one-toggled==current, click-non-active switches (active is no-op), and
missing/unresolved IconKey → HasIcon false (text fallback). The "no-longer-in-menu-bar" test is
indirect (removal is compile-enforced; asserts the new model + Render NoThrow) — acceptable, as it's
the strongest available without reflecting over private render internals.

## Verdict
APPROVED. MTB-P3-T3, MTB-P3-T4 → `[x]`. Phase 3 continues (T5 remains).

## Commit Message
```
feat(main-toolbar): relocate perspective menu + perspective toolbar radio-group (MTB-P3-T3, T4)

Remove the in-menu-bar perspective buttons (RenderPerspectiveSwitcher) per §8.1; add a top-level
checkable "Perspective" menu built from WindowManager.GetPerspectives()/BuildPerspectiveMenuModel/
SelectPerspective. Add optional ManagedWindow.IconKey + WindowManager.GetPerspectiveIconKey, and a
PerspectiveToolbarSection radio-group (exactly-one-toggled, click-to-switch, text fallback on
missing icon) with headless BuildRadioModel/OnSelect seams. Tests: 19 new, all pass.
```
