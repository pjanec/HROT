# BATCH-15 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-11

## Summary
MTB-P5-T3/T4: `AssetPickerModal` (close + `callback(asset|null)`, no side effects) and
`AssetBrowserDockedWindow` (`ManagedWindow`, stays open, invokes registrant callback) — both
generic hosts over `AssetBrowserPanel`, in `Hrot.Editor.AiShared/Browser`.

## Issues Found
No issues found.

## Verification (done by lead)
- `dotnet build IOS-IG-SimHost.sln` → 0 errors, 0 new warnings.
- New tests run by lead: `AssetPickerModalTests` (14) + `AssetBrowserDockedWindowTests` (8) →
  **22 passed, 0 failed**. Suites green: AiShared 947, Fdp.Toolkits 1856, SimHost 585.
- `AssetPickerModal`: `Open(options, callback)` with `_callbackInvoked` guard (one callback per open);
  `HandleActivated`→callback(asset)+close; `HandleCancel`→callback(null)+close; programmatic `Close`
  discards pending callback; no document-manager/load dependency at all (structurally side-effect-free).
- `AssetBrowserDockedWindow : ManagedWindow`, `Id="AssetBrowser"` (`ExpectedId`), `Scope=Global`;
  `OnPanelAssetActivated`→`_onAssetActivated(asset)` with the window left open; `DrawClientArea`→
  `panel.DrawContent()`. No caller wiring (correctly deferred to MTB-P5-T6); no scenario nested-name
  (MTB-P5-T5).
- Scope: 4 new files. No legacy deletions, no scope creep.

## Test Quality
Strong. Modal tests assert IsOpen true→false across activate and cancel, callback receives the exact
asset / null, single-invocation guard, and `NeverCalls_DocumentManager_Or_Load` (recording fake never
called). Docked tests assert registration Id/Scope and that activation invokes the callback while
`IsOpen` stays true. No tautological/skipped tests.

## Verdict
APPROVED. MTB-P5-T3, MTB-P5-T4 → `[x]`. Phase 5 continues (T5/T6 remain).

## Commit Message
```
feat(main-toolbar): asset picker modal + docked window hosts (MTB-P5-T3, T4)

AssetPickerModal hosts AssetBrowserPanel with an Action<IEditableAsset?> callback (activate →
close + callback(asset); Esc/cancel → close + callback(null); single-invocation guard; zero side
effects). AssetBrowserDockedWindow : ManagedWindow (Id="AssetBrowser", Global) invokes the
registrant's Action<IEditableAsset> on activation and stays open. Both generic, in
Hrot.Editor.AiShared/Browser. Caller wiring deferred to MTB-P5-T6. Tests: 22 new, all pass.
```
