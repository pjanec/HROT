using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;

namespace Hrot.ScenarioEditor.Tools
{
    /// <summary>⭐ B-17 P4 — a click the pending pick refused, and the time of it (seconds on the host's clock): flashed at the cursor for <see cref="PickerToolHost.FlashSeconds"/>.</summary>
    public readonly record struct PickRejection(string Reason, double At);

    /// <summary>
    /// ⭐ B-17 P4 (📄 docs/UX/DESIGN_Map_Interaction.md §2.3) — the pick that is pending on a <see cref="PickerToolHost"/>: what it asks, what it
    /// last refused, what it replaced, and how to cancel it. ONE per host (a second request replaces the first).
    /// </summary>
    internal sealed class PendingPick
    {
        public string ToolId = "";
        /// <summary>The words of the banner (<c>"Pick a point for Move to"</c>); null ⇒ a drawing tool that is not a pick (no banner, never replaced).</summary>
        public string? Prompt;
        public string? Notice;
        public double NoticeAt;
        public string? RejectedReason;
        public double RejectedAt;
        public Vector2 CursorWorld;
        public IEntityStatefulGizmo? Inner;
        public Action Remove = () => { };
        /// <summary>Completes the caller's await as cancelled — a backstop for a gizmo whose Esc does not.</summary>
        public Action? CancelCaller;

        /// <summary>The pick's own Esc (every picker gizmo ends itself and completes its caller as cancelled), then the unregister and the caller's cancel, all idempotent.</summary>
        public void Cancel()
        {
            Inner?.OnKeyEvent(MapKeyboardKey.Escape, true);
            Remove();
            CancelCaller?.Invoke();
        }
    }

    /// <summary>
    /// ⭐ B-17 P4 — wraps a picker gizmo and draws its banner as GIZMO PRIMITIVES (so every terminal, a remote viewer too, shows it — §2.4):
    /// <c>[Cancel]  Pick a point for Move to</c> at the top of the map (screen-space text), <c>replaced: …</c> under it for a few seconds,
    /// and the refusal of a click flashed at the cursor. A left click on <c>[Cancel]</c> cancels the pick; every other event goes to the picker.
    /// ⭐ B-17 P7 ④ — the gap is CLOSED: the prompt, the notice and the refusal are <see cref="GizmoTextStyle.Backdrop"/> text, <c>Cancel</c> is a
    /// <see cref="GizmoTextStyle.Button"/>, and a crosshair of two pixel bars follows the cursor while the pick is pending (gizmo primitives, not the OS cursor —
    /// the gizmo runs where the edit runs, so a remote terminal would otherwise not show it).
    /// </summary>
    internal sealed class PickBannerGizmo : IEntityStatefulGizmo
    {
        private static readonly Rgba32 PromptColour = new(255, 255, 255, 255);
        private static readonly Rgba32 CancelColour = new(255, 255, 255, 255);
        private static readonly Rgba32 CrossFill = new(255, 255, 255, 255);
        private static readonly Rgba32 CrossOutline = new(20, 20, 24, 255);
        private static readonly Rgba32 NoticeColour = new(180, 190, 205, 255);
        private static readonly Rgba32 RejectColour = new(255, 110, 100, 255);

        private readonly IEntityStatefulGizmo _inner;
        private readonly PendingPick _pick;
        private readonly PickerToolHost _host;

        public PickBannerGizmo(IEntityStatefulGizmo inner, PendingPick pick, PickerToolHost host)
        {
            _inner = inner; _pick = pick; _host = host;
        }

        public bool RequiresExclusiveFocus => _inner.RequiresExclusiveFocus;
        public bool WantsRawInput => _inner.WantsRawInput;
        public bool IsFocused => _inner.IsFocused;
        public void SetFocus(bool isFocused) => _inner.SetFocus(isFocused);
        public string? ToolMenuJson => _inner.ToolMenuJson;
        public Fdp.Toolkit.Diagnostics.Gizmos.UndoRedo.IGizmoUndoRecord? CreateUndoRecord(Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoInteractionCommitEvent commit) => _inner.CreateUndoRecord(commit);

        public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
        {
            _inner.UpdateAndDraw(view, deltaTime, draw);
            if (_pick.Prompt is not { } prompt) return;
            draw.DrawTextLong(PickerToolHost.CancelLeftPx, PickerToolHost.BannerTopPx, "Cancel", CancelColour, CoordinateSpace.Screen, style: GizmoTextStyle.Button);
            draw.DrawTextLong(PickerToolHost.PromptLeftPx, PickerToolHost.BannerTopPx, prompt, PromptColour, CoordinateSpace.Screen, style: GizmoTextStyle.Backdrop);
            if (_host.NoticeOf(_pick) is { } notice)
                draw.DrawTextLong(PickerToolHost.PromptLeftPx, PickerToolHost.BannerTopPx + 22f, notice, NoticeColour, CoordinateSpace.Screen, style: GizmoTextStyle.Backdrop);
            if (_host.Rejected is { } rejected)
                draw.DrawTextLong(_pick.CursorWorld.X, _pick.CursorWorld.Y, rejected.Reason, RejectColour, lineOffsetPx: 22f, style: GizmoTextStyle.Backdrop);
            DrawCrosshair(draw, _pick.CursorWorld);
        }

        /// <summary>⭐ B-17 P7 ④ — the pick cursor: two thin bars (18 × 2 px) in SCREEN pixels at the cursor's world point, white with a dark outline, so the
        /// crosshair keeps its size at every zoom (a ScreenPixels Box2D is scaled by 1/zoom by the 2-D renderer and draped at constant size by the 3-D one).</summary>
        internal const float CrossHalfLongPx = 9f, CrossHalfShortPx = 1f;
        private static void DrawCrosshair(IDebugDrawBuilder draw, Vector2 at)
        {
            draw.DrawBox2D(at, new Vector2(CrossHalfLongPx, CrossHalfShortPx), CrossOutline, 0f, 1f, SizeMode.ScreenPixels, fillColor: CrossFill);
            draw.DrawBox2D(at, new Vector2(CrossHalfShortPx, CrossHalfLongPx), CrossOutline, 0f, 1f, SizeMode.ScreenPixels, fillColor: CrossFill);
        }

        public void OnDragUpdate(Vector3 worldPos)
        {
            _pick.CursorWorld = new Vector2(worldPos.X, worldPos.Y);
            _inner.OnDragUpdate(worldPos);
        }

        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
        {
            _pick.CursorWorld = new Vector2(worldPos.X, worldPos.Y);   // a click carries the cursor too — the crosshair never lags a hover
            if (button == MapMouseButton.Left && _pick.Prompt != null && _host.OverCancel())
            {
                if (!isPressed) _pick.Cancel();   // the release is the click, as every picker takes it
                return;
            }
            _inner.OnMouseEvent(button, isPressed, worldPos);
        }

        public void OnKeyEvent(MapKeyboardKey key, bool isPressed) => _inner.OnKeyEvent(key, isPressed);
        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) => _inner.OnInteractionStarted(token, worldPos);
        public void OnCommit(Vector3 worldPos) => _inner.OnCommit(worldPos);
        public void OnCancel() => _inner.OnCancel();
        public void OnMenuAction(int actionId) => _inner.OnMenuAction(actionId);
        public void Dispose() => _inner.Dispose();
    }

    /// <summary>⭐ B-17 P4 — the words of a pick's banner: the field the form is asking for, or the tool's own default.</summary>
    public static class PickPrompts
    {
        /// <summary>"Pick a point for Move to" — <paramref name="what"/> is "a point" / "a unit"; the field is the last segment of the form's JSON path, split into words.</summary>
        public static string For(string what, string? jsonPath)
        {
            var field = LastWord(jsonPath);
            return field.Length == 0 ? $"Pick {what}" : $"Pick {what} for {field}";
        }

        /// <summary>The default for a picker tool with no asking field; null for a tool that is not a pick.</summary>
        public static string? DefaultFor(string toolId)
            => toolId == ScenarioToolIds.PickLocation ? "Pick a point on the map"
             : toolId == ScenarioToolIds.PickEntity   ? "Pick a unit on the map"
             : toolId == ScenarioToolIds.PickArea     ? "Drag a box on the map"
             : toolId == ScenarioToolIds.PickBounds   ? "Drag a box on the map"
             : null;

        private static string LastWord(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            var seg = path!;
            int cut = Math.Max(seg.LastIndexOf('.'), Math.Max(seg.LastIndexOf('/'), seg.LastIndexOf(']')));
            if (cut == seg.Length - 1 && seg.Length > 0) { seg = seg.TrimEnd(']'); cut = Math.Max(seg.LastIndexOf('.'), seg.LastIndexOf('/')); }
            seg = seg.Substring(cut + 1).Trim('[', ']', '$');
            if (seg.Length == 0 || char.IsDigit(seg[0])) return "";
            var sb = new System.Text.StringBuilder(seg.Length + 4);
            for (int i = 0; i < seg.Length; i++)
            {
                char c = seg[i];
                if (c == '_') { sb.Append(' '); continue; }
                bool newWord = i > 0 && char.IsUpper(c) && char.IsLower(seg[i - 1]);
                if (newWord) sb.Append(' ');
                sb.Append(i == 0 || !newWord ? c : char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }
    }
}
