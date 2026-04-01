# BATCH-04 Review

**Batch:** BATCH-04  
**Reviewed by:** Dev Lead  
**Date:** 2026-04-01  
**Decision:** ✅ APPROVED

---

## Scope Check

| Task | Delivered | Notes |
|------|-----------|-------|
| WM-S401 — Settings handler (`IsOpen`/`IsPinned` persistence) | ✅ | JSON fallback; P2 debt recorded |
| WM-S402 — ImGui docking integration | ✅ | Fullscreen dockspace, PassthruCentralNode |
| WM-S501 — Expose `WindowManager` to subsystems | ✅ | SubsystemConfig.WindowManager added |
| WM-S502 — Composition root `OnPerspectiveChanged` bridge | ✅ | TogglePerspectiveEvent record + stub handler |
| WM-S503 — Dockspace height reserve for status bar | ✅ | Null-safe shrink, stub 0f height until WM-S602 |

---

## Test Quality

- **143/143 pass** (8 new serialization tests for WM-S401, plus 2 new TogglePerspectiveEvent record tests).
- WM-S401 JSON round-trip is fully unit-tested with internal `SerializeToIniSection` / `DeserializeFromIniSection` methods. Excellent testable design.
- TogglePerspectiveEvent value equality and immutability verified.
- All builds pass: FDP.Toolkit.ImGui, FDP.Framework.Runner, Hrot.ClusterRunner.

---

## Design Alignment

- Headless mode guard correct: `_windowManager` is `null` in headless, all null-safe accesses compile.
- `DrawMainMenuBar()` removed — correct; `WindowManager.Render()` replaces it.
- JSON fallback for settings handler is pragmatically correct given ImGui.NET binding limitations.
- `StatusBarHeight => 0f` stub well-commented with `// TODO: WM-S602`.
- `TogglePerspectiveEvent` is a proper immutable record in `Hrot.Common`.
- `OnPerspectiveChanged` subscription in Program.cs is null-guarded and correct.

---

## Issues Found

None structural. The following debt item is pre-recorded by the developer:

| ID | Priority | Description | Target |
|----|----------|-------------|--------|
| DEBT-003 | P2 | `ImGui.AddSettingsHandler` not accessible via ImGui.NET 1.91.0.1 managed bindings. JSON fallback implemented. Pure imgui.ini integration deferred. | Future |

---

## Suggested Git Commit Message

```
feat(framework): wire WindowManager into SubsystemOrchestrator (WM-S401-S503)

WM-S401: WindowManager persistence via JSON fallback (imgui.ini handler not
  available in ImGui.NET managed bindings — DEBT-003 P2).
WM-S402: Fullscreen dockspace with PassthruCentralNode before subsystem UI.
  DockingEnable flag set at rlImGui.Setup time.
WM-S501: SubsystemOrchestrator creates WindowManager (dummy atlas, non-headless
  only); SubsystemConfig.WindowManager set before Initialize(); DrawMainMenuBar
  replaced by _windowManager.Render().
WM-S502: TogglePerspectiveEvent record in Hrot.Common; OnPerspectiveChanged
  subscription in Program.cs (FdpEventBus.Publish deferred to WM-S703).
WM-S503: Dockspace height shrinks by StatusBarHeight (stub=0 until WM-S602).

Tests: 8 new (143/143 total passing). Framework.Runner + ClusterRunner build clean.
```
