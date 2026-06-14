# BATCH-S2-ML — Wire the Message Log window in the Stride editor host

## Problem
The editor's **Message Log** panel is absent in the Stride editor. Root cause: the `MessageLogWindow`
+ `MessageLogRegistry` are created by the HOST (ClusterRunner's `LocalWindowController`), NOT by the
editor's `RegisterWindows`. The editor only registers log *sources* into `windowManager.MessageLogRegistry`
(EditorSubsystem.cs:3605/3607) and looks up a window it assumes the host already created (comment at
EditorSubsystem.cs:3645 "registered globally by Program.cs"). The Stride host `StrideInspectorWindow`
builds `new WindowManager(atlas)` but never creates the registry/window — so `MessageLogRegistry` is null,
the editor's `RegisterSource` calls no-op, and no Message Log panel exists.

## Fix — mirror LocalWindowController's message-log host setup in StrideInspectorWindow
Reference (the canonical host): `Hrot/Runner/Hrot.ClusterRunner/Presentation/LocalWindowController.cs:44-72`:
```csharp
var wm = new WindowManager(atlas);
var messageLogRegistry = new MessageLogRegistry();
messageLogRegistry.RegisterSource(NLogMessageLogTarget.SharedInstance);
var msgLogWindow = new MessageLogWindow(messageLogRegistry);
wm.RegisterWindow(msgLogWindow);
wm.MessageLogRegistry = messageLogRegistry;
... // subsystem RegisterWindows happen AFTER this
var msgLogSection = new MessageLogStatusBarSection(msgLogWindow, wm);
wm.StatusBar.RegisterSection("msg_log_notify", sortOrder: 90, msgLogSection.Render);
```

File: `Stride/HrotStrideApp.Game/StrideInspectorWindow.cs`, in `Open()` — immediately AFTER
`_windowManager = new Fdp.Presentation.WindowManager.WindowManager(atlas);` (~line 587) and BEFORE the
`_subsystem.HostedEditor.RegisterWindows(_windowManager)` call (~line 591) [the registry MUST exist before
RegisterWindows so the editor's RegisterSource calls land], add:
```csharp
// BATCH-S2-ML: message-log host wiring (mirrors LocalWindowController). The editor registers log
// SOURCES into wm.MessageLogRegistry and looks up a MessageLogWindow it assumes the host created —
// so the host (this window) must create the registry + window, exactly like the ClusterRunner host.
var messageLogRegistry = new Fdp.Core.Logging.MessageLogRegistry();
messageLogRegistry.RegisterSource(Fdp.Core.Logging.NLogMessageLogTarget.SharedInstance);
var msgLogWindow = new Fdp.Presentation.Windows.MessageLogWindow(messageLogRegistry);
_windowManager.RegisterWindow(msgLogWindow);
_windowManager.MessageLogRegistry = messageLogRegistry;
```
And AFTER `RegisterWindows` (so the status bar exists), add the status-bar notify section (the bell/notify
that surfaces the log), mirroring LocalWindowController 71-72:
```csharp
// BATCH-S2-ML: status-bar message-log notifier (click to open the log).
var msgLogSection = new Fdp.Presentation.WindowManager.MessageLogStatusBarSection(msgLogWindow, _windowManager);
_windowManager.StatusBar.RegisterSection("msg_log_notify", sortOrder: 90, msgLogSection.Render);
```

VERIFY:
- Exact namespaces/types: `MessageLogRegistry` (Fdp.Core.Logging — confirm), `NLogMessageLogTarget`
  (Fdp.Core.Logging), `MessageLogWindow` (Fdp.Presentation.Windows — confirm via the grep hits), the
  `WindowManager.RegisterWindow(...)` + `MessageLogRegistry` property + `StatusBar.RegisterSection(...)`
  signatures match LocalWindowController's usage. Add usings or use fully-qualified names.
- Place the registry/window creation BEFORE RegisterWindows; the status section AFTER (StatusBar must exist).
- Do NOT change EditorSubsystem (the source registration there already works once the registry exists).

## Constraints
- ONE file (StrideInspectorWindow.cs). Mirror LocalWindowController exactly; don't invent APIs.
- Build the Stride solution; kill HrotStrideApp + rebuild on file lock.

## Acceptance
- Builds clean.
- (User) The Message Log panel is now available in the Stride editor (via the window menu / the status-bar
  notifier), and editor/AI/NLog messages appear in it (the editor's source registrations now have a
  registry to attach to).
