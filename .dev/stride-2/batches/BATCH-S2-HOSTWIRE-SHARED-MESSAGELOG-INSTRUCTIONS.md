# BATCH-S2-HOSTWIRE — Extract shared message-log host wiring (de-duplicate the two hosts)

## Goal
Both hosts wire the message log identically: ClusterRunner's `LocalWindowController` and the Stride
`StrideInspectorWindow` (the latter via BATCH-S2-ML, which duplicated `LocalWindowController`). Extract the
duplicated wiring into ONE shared helper that both call, so there's a single source of truth. Keep each
host's existing ORDER/structure otherwise (perspective/settings differ between them — leave those alone).

## Part 1 — new shared helper
New file in `Fdp.Presentation` (referenced by BOTH ClusterRunner and the Stride app). Place under
`FDP/Engine/Fdp.Presentation/ImGui/WindowManager/` next to `WindowManager.cs`, namespace
`Fdp.Presentation.WindowManager`:

```csharp
#nullable enable
using Fdp.Core.Logging;
using Fdp.Presentation.Windows;

namespace Fdp.Presentation.WindowManager
{
    /// <summary>
    /// Shared host wiring for the editor Message Log (BATCH-S2-HOSTWIRE). Both the ClusterRunner host
    /// (LocalWindowController) and the Stride host (StrideInspectorWindow) need an identical
    /// MessageLogRegistry + MessageLogWindow created on the WindowManager BEFORE subsystem RegisterWindows
    /// (so editor RegisterSource calls land), plus a status-bar notifier. Extracted here to avoid
    /// duplicating it per host.
    /// </summary>
    public static class MessageLogHostWiring
    {
        /// <summary>
        /// Creates the MessageLogRegistry (seeded with the shared NLog target), the MessageLogWindow,
        /// registers the window, and sets <c>wm.MessageLogRegistry</c>. Call BEFORE subsystem
        /// RegisterWindows. Returns the window so the caller can add the status-bar notifier.
        /// </summary>
        public static MessageLogWindow CreateAndRegister(WindowManager wm)
        {
            var registry = new MessageLogRegistry();
            registry.RegisterSource(NLogMessageLogTarget.SharedInstance);
            var window = new MessageLogWindow(registry);
            wm.RegisterWindow(window);
            wm.MessageLogRegistry = registry;
            return window;
        }

        /// <summary>Registers the status-bar message-log notifier section (click to open the log).</summary>
        public static void AddStatusBarNotifier(WindowManager wm, MessageLogWindow window)
        {
            var section = new MessageLogStatusBarSection(window, wm);
            wm.StatusBar.RegisterSection("msg_log_notify", sortOrder: 90, section.Render);
        }
    }
}
```
VERIFY all namespaces/types and signatures against the current code in `LocalWindowController.cs:44-72`
(`MessageLogRegistry`, `NLogMessageLogTarget` = Fdp.Core.Logging; `MessageLogWindow` = Fdp.Presentation.Windows;
`MessageLogStatusBarSection` = Fdp.Presentation.WindowManager; `wm.RegisterWindow`, `wm.MessageLogRegistry`,
`wm.StatusBar.RegisterSection`). Match exactly. `MessageLogStatusBarSection` is in the same namespace, so no
extra using needed there.

## Part 2 — use it in BOTH hosts (remove the duplication)

### `Hrot/Runner/Hrot.ClusterRunner/Presentation/LocalWindowController.cs` (~lines 47-72)
Replace the inline message-log block (lines 47-51):
```csharp
var messageLogRegistry = new MessageLogRegistry();
messageLogRegistry.RegisterSource(NLogMessageLogTarget.SharedInstance);
var msgLogWindow = new MessageLogWindow(messageLogRegistry);
wm.RegisterWindow(msgLogWindow);
wm.MessageLogRegistry = messageLogRegistry;
```
with:
```csharp
var msgLogWindow = MessageLogHostWiring.CreateAndRegister(wm);
```
And replace the status-section block (lines 71-72):
```csharp
var msgLogSection = new MessageLogStatusBarSection(msgLogWindow, wm);
wm.StatusBar.RegisterSection("msg_log_notify", sortOrder: 90, msgLogSection.Render);
```
with:
```csharp
MessageLogHostWiring.AddStatusBarNotifier(wm, msgLogWindow);
```
(Keep everything else — the subsystem RegisterWindows loop, the perspective bridge, system_health section,
LoadSettings/SwitchPerspective — UNCHANGED. Add a `using Fdp.Presentation.WindowManager;` only if not present.)

### `Stride/HrotStrideApp.Game/StrideInspectorWindow.cs` (the BATCH-S2-ML block)
Replace the registry/window block I added before RegisterWindows:
```csharp
var messageLogRegistry = new Fdp.Core.Logging.MessageLogRegistry();
messageLogRegistry.RegisterSource(Fdp.Core.Logging.NLogMessageLogTarget.SharedInstance);
var msgLogWindow = new Fdp.Presentation.Windows.MessageLogWindow(messageLogRegistry);
_windowManager.RegisterWindow(msgLogWindow);
_windowManager.MessageLogRegistry = messageLogRegistry;
```
with:
```csharp
var msgLogWindow = Fdp.Presentation.WindowManager.MessageLogHostWiring.CreateAndRegister(_windowManager);
```
And replace the status-section block (after RegisterWindows):
```csharp
var msgLogSection = new Fdp.Presentation.WindowManager.MessageLogStatusBarSection(msgLogWindow, _windowManager);
_windowManager.StatusBar.RegisterSection("msg_log_notify", sortOrder: 90, msgLogSection.Render);
```
with:
```csharp
Fdp.Presentation.WindowManager.MessageLogHostWiring.AddStatusBarNotifier(_windowManager, msgLogWindow);
```
(Keep placement: CreateAndRegister BEFORE RegisterWindows; AddStatusBarNotifier after.)

## Constraints
- New helper + the two host edits ONLY. Behavior must be IDENTICAL to before (pure de-duplication).
- Do NOT touch perspective/settings wiring in either host (that's a separate concern).
- Build BOTH the ClusterRunner (or the solution that contains it) AND the Stride solution — this touches
  the cluster host, so it must still compile/run. Report both builds.

## Acceptance
- Both builds clean (Stride solution + the ClusterRunner/FDP solution covering LocalWindowController).
- Message log still works in BOTH the cluster runner and the Stride editor (no behavior change), now from
  one shared helper. The S2-ML duplication is gone.
