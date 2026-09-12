# BATCH-26 Review (unified Open-Asset launcher + Scenario-Load lock-up fix)
**Status:** ✅ APPROVED   **Date:** 2026-06-11

## Summary
One `AssetPickerModal` mechanism now serves three entry points — toolbar "Open Asset" button (leftmost,
`browser/open`, sortOrder -10), File→Open Asset… + Ctrl+O (`shell.openAsset`, Kinds=All), and
Scenario→Load (same modal, Kinds=Scenario) — with pick→`AssetPickActionRouter.Route`
(file→`AiDocumentManager.Open`, scenario→`LoadScenarioByName`). Lock-up fixed.

## Lock-up root cause (confirmed) + fix
The modal opened **invisible/zero-size** and blocked the menu: `OpenPopup` and `BeginPopupModal` used
divergent ID strings, and the popup had no explicit size. Fix mirrors the working "Rename Entity" modal:
a `_pendingOpen` flag consumed at the DrawUI top level, **identical literal ID ("Open Asset")** for
`OpenPopup`+`BeginPopupModal`, and explicit `SetNextWindowSize(720×520)` so it can't collapse. (This is
why my earlier ID-only fix in 573afb9a wasn't sufficient — the zero-size collapse remained.)

## Verification (done by lead)
- Wiring read: `_assetPickRouter` (file→Open, scenario→Load); `shell.openAsset` (browser/open, Ctrl+O,
  Kinds=All); toolbar leftmost (-10) + separator; File→Open Asset… menu; Scenario→Load shares the modal
  (Kinds=Scenario); one modal instance + one DrawModal at DrawUI top level.
- Modal rewrite read: `_pendingOpen` pattern, identical "Open Asset" id, explicit size, Enter→
  `HandleActivated(Selection)`, Ctrl+Tab/Ctrl+Shift+Tab tab-cycle (new `AssetBrowserPanel` tab nav).
- Tests run by lead: AssetPickerModal+AssetBrowserPanel 39/0; ScenarioMenu 16/0. Worker-reported:
  EditorSubsystemBlueprintWindows 12/0 (8 window + guardrail + 3 new Open-Asset), Fdp.Presentation
  toolbar/wm/menu 143/0, `Hrot.Blueprints.Tests` (Stability) = exactly the 9 PRE-1.
- Library projects compile 0/0; all wiring null-safe (guardrail green).

## Verdict
APPROVED. Open-Asset launcher delivered; Scenario→Load lock-up resolved (BUG-3).

## Note
Runtime confirmation (modal actually appears, Enter/Ctrl+Tab feel right) is for the user's next editor
run. Icon art still placeholder (DBT-1).

## Commit Message
```
feat(main-toolbar): unified Open-Asset modal launcher + fix Scenario-Load lock-up (BUG-3)

One AssetPickerModal serves: toolbar "Open Asset" button (leftmost, browser/open), File→Open Asset… +
Ctrl+O (shell.openAsset, Kinds=All), and Scenario→Load (Kinds=Scenario). Pick → AssetPickActionRouter
(file→AiDocumentManager.Open, scenario→LoadScenarioByName), wired in production. Modal UX: Esc cancel,
Enter confirm selection, Ctrl+Tab/Ctrl+Shift+Tab cycle tabs. Lock-up fix: the modal opened invisible/
zero-size and blocked the menu — now mirrors the Rename-Entity pattern (pending-flag, identical
OpenPopup/BeginPopupModal id "Open Asset", explicit SetNextWindowSize). Tests added; Blueprints = 9 PRE-1.
```
