# BATCH-S2-AD — Toast warning when issuing a nav move while time is PAUSED

## Goal
When the operator RMB-clicks a move target while sim time is PAUSED, the unit doesn't move and there's
no feedback (operator gets confused). Show a short auto-expiring on-screen toast:
"Sim is PAUSED — move queued; the unit will move when you start time." The marker still appears and the
order is still issued (no behavior change) — this is purely an explanatory toast.

There is NO existing auto-expiring toast system in the Stride editor; build a minimal one mirroring the
existing `_moveMarkerSecondsRemaining` countdown pattern in EditorStrideSubsystem.

## Scope — THREE FILES

### File 1: `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs` — toast state + countdown
Mirror the move-marker fields (`_moveMarkerFdp` / `_moveMarkerSecondsRemaining` / `MoveMarkerTotalSeconds`,
~line 1360 + 1473 + 1509).
1. Add fields + a public API near the move-marker members:
```csharp
// BATCH-S2-AD: transient on-screen toast (auto-expiring), driven by the same dt countdown as the move marker.
private string _toastMessage = string.Empty;
private float  _toastSecondsRemaining;
private const float ToastTotalSeconds = 4.0f;

/// <summary>Currently-visible toast text (empty when none). Read by the editor-window overlay.</summary>
public string ToastMessage => _toastMessage;
/// <summary>Seconds the toast remains visible; &gt; 0 means draw it. Read by the editor-window overlay.</summary>
public float ToastSecondsRemaining => _toastSecondsRemaining;

/// <summary>Show a short auto-expiring toast (BATCH-S2-AD).</summary>
public void ShowToast(string message, float seconds = ToastTotalSeconds)
{
    _toastMessage = message ?? string.Empty;
    _toastSecondsRemaining = seconds;
}
```
2. Decrement it where the move marker is decremented — in `EmitMoveMarker(float dt)` (~line 1509), add at
   the top or bottom of that method:
```csharp
if (_toastSecondsRemaining > 0f) _toastSecondsRemaining -= dt;
```
   (EmitMoveMarker is already called each frame from both Tick() and TickHosted() with dt — confirm and
   reuse it; do NOT add a new per-frame hook.)

### File 2: `Stride/HrotStrideApp.Game/StrideHrotGame.cs` — fire the toast when paused
In the RMB-release move block, immediately AFTER the existing
`IssueMoveOrder(world, sel.SelectedEntity, hit.PointFdp);` + `_editorSubsystem.ShowMoveMarker(hit.PointFdp);`
lines, add:
```csharp
// BATCH-S2-AD: if sim time is paused, the unit won't move yet — tell the operator (they kept hitting this).
if (_editorSubsystem.TimeController.GetMode() == TimeMode.Deterministic)
    _editorSubsystem.ShowToast("Sim is PAUSED — move queued; the unit will move when you start time.");
```
- `TimeMode` is in namespace `Fdp.ModuleHost.Time` — add `using Fdp.ModuleHost.Time;` if not already present.
- `_editorSubsystem.TimeController.GetMode() == TimeMode.Deterministic` is the confirmed paused-check idiom
  (used in EditorStrideSubsystem.PreKernelUpdateHook). `TimeController` is a public property on
  EditorStrideSubsystem (MasterSyncController). Verify both before relying on them.

### File 3: `Stride/HrotStrideApp.Game/StrideInspectorWindow.cs` — draw the toast overlay
The editor ImGui frame is rendered in `PumpFrame()` between `rlImGui.Begin()` (~line 646) and
`rlImGui.End()` (~line 700). After the dockspace is ended and before/after `wm?.Render()` / `editor?.DrawUI()`,
draw an auto-resizing, non-interactive overlay near the top-center when a toast is active:
```csharp
// BATCH-S2-AD: transient paused-nav toast overlay.
if (_subsystem != null && _subsystem.ToastSecondsRemaining > 0f)
{
    var vp = ImGui.GetMainViewport();
    ImGui.SetNextWindowPos(
        new System.Numerics.Vector2(vp.WorkPos.X + vp.WorkSize.X * 0.5f, vp.WorkPos.Y + 48f),
        ImGuiCond.Always, new System.Numerics.Vector2(0.5f, 0f));
    ImGui.SetNextWindowBgAlpha(0.85f);
    ImGui.Begin("##PausedNavToast",
        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing);
    ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1f, 0.85f, 0.2f, 1f)); // amber
    ImGui.TextUnformatted(_subsystem.ToastMessage);
    ImGui.PopStyleColor();
    ImGui.End();
}
```
- `using ImGuiNET;` is already present (line 14). `ImGui.GetMainViewport()` already used (~line 651).
- IMPORTANT: VERIFY the field name by which `StrideInspectorWindow` reaches the `EditorStrideSubsystem`
  (the batch calls it `_subsystem`). Read the class — it already calls something like `editor?.DrawUI()`.
  Determine the actual reference it holds to `EditorStrideSubsystem` and use THAT. If StrideInspectorWindow
  does NOT already hold an EditorStrideSubsystem reference, add a minimal one: a settable
  `public EditorStrideSubsystem? ToastSource { get; set; }` on StrideInspectorWindow, set by whoever
  constructs/owns it (the same place that owns both the window and the subsystem — likely StrideHrotGame
  or EditorStrideSubsystem itself), and read it here. Do the LEAST-plumbing option that compiles and works;
  REPORT exactly what you wired.
  - `NoDocking` flag may not exist in this ImGuiNET version — if it doesn't compile, drop it (I omitted it above).

## Constraints
- THREE files. Do NOT change IssueMoveOrder behavior, the marker, selection, or the move itself — the
  order is still issued and the marker still shows while paused; the toast is additive.
- The toast must auto-expire (no lingering). Reuse the existing dt countdown (EmitMoveMarker); do not add
  Date.now / new Date (restricted) — drive expiry from dt only.
- Don't show the toast when time is running.

## Acceptance
- Builds clean (`Stride/HrotStrideApp.sln`).
- (User) With time PAUSED, select a unit and RMB-click a destination → an amber toast appears near
  top-center for ~4s explaining the unit will move when time starts; marker still shows. With time
  RUNNING, the same action shows NO toast and the unit moves as before.
