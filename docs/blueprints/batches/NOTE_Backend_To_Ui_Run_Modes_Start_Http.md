<!--STATUS
state: LIVE — backend → ui: CE-4094 + CE-4088 built (what CE-4096 builds on)
updated: 2026-10-28
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §5 M7 as-built note; D16 the start perspective
  - ../PLAN_Run_Modes_And_Content_Build.md — §2 rows CE-4094, CE-4096
-->
# NOTE — backend → ui: HTTP while `Start` shows is built (CE-4094, CE-4088)

⭐ For **CE-4096** (the start gate): use these — do not build another remember-and-restore.

| what | where |
|---|---|
| `WindowManager.StartPerspectiveName` (`"Start"`), `RememberedPerspective`, `IsStartShowing` | `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs` |
| entering `Start` = an ordinary `SwitchPerspective("Start")` (a window must claim `Start`); it remembers the outgoing perspective | same |
| while `Start` shows, `TrySwitchPerspective` refuses with a reason; the gate leaves ONLY through `LeaveStartPerspective(out reason)`, which restores the remembered one | same |
| `SaveSettings` never persists `Start` (saves the remembered one) | same |
| the dispatcher routes reads to the remembered perspective (`RoutedTo`); `/status` reports `perspective: Start` + `routedTo`; `POST /perspective` answers 409 with the reason | `PerspectiveScopedDispatcher`, `DebugApiService*` |

⚠ **When your gate lands:** `ClusterConformanceRails` and `TheUiBaselineIsPinnedPerHostRails` now ASSERT the perspective switch (they used to `continue` silently) but do not load first — a fresh process that boots into `Start` will fail them. They need a load first, or a golden update; `PerspectiveReachTests` already loads first.

_backend lane, 2026-10-28_
