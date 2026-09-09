using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Action;

namespace Hrot.Blueprints.Editor;

/// <summary>
/// BP-223 — draws the toasts a document's <see cref="IEditorIndicators"/> queues.
///
/// <para>
/// ⚠ <b>Why this had to be written rather than reused.</b> <c>IEditorIndicators.Notify</c> →
/// <c>ToastQueue.Enqueue</c> has existed since NodeEdit's action layer landed, and
/// <c>BookmarkCommands</c> has been calling it since BP-24 — but the <b>only</b> <c>TryDequeue</c>
/// in the repository was <c>NodeEditor.Demo.DemoShell.DrawToasts</c>. In the Hrot editor every
/// notification was enqueued into a queue nothing drained and silently discarded. BP-74 needs a
/// refusal to reach the designer ("refuse on invoke, and say why" — Q26-B2), so the missing half is
/// supplied here, and bookmarks get their notifications back as a side effect.
/// </para>
///
/// <para>
/// Mirrors <c>DemoShell.DrawToasts</c>'s convention deliberately — bottom-right, auto-sized,
/// input-transparent, severity-coloured title over a wrapped body — so the two do not drift into
/// two different notions of what a toast looks like.
/// </para>
///
/// <para>
/// ⭐ Instance state, not static: dismissal countdowns belong to the document being drawn. Call
/// <see cref="Draw"/> once per frame after the canvas (e.g. from <c>AiGraphCanvasWindow.AfterDraw</c>).
/// </para>
/// </summary>
public sealed class NotificationOverlay
{
    private readonly List<(EditorNotification Notification, float TimeRemaining)> _active = new();

    /// <summary>Default lifetime for a notification that did not ask for one.</summary>
    private const float DefaultSeconds = 4f;

    /// <summary>Pending notifications currently on screen. Exposed for tests.</summary>
    public int ActiveCount => _active.Count;

    /// <summary>
    /// Drains <paramref name="indicators"/>' queue and draws whatever is still alive.
    /// </summary>
    /// <param name="indicators">
    /// The document's indicators. Only <see cref="EditorIndicatorsImpl"/> exposes its queue; any
    /// other implementation is left alone, which is the correct behaviour for a host that already
    /// has its own surface.
    /// </param>
    /// <param name="deltaSeconds">Frame time; drives auto-dismiss.</param>
    public void Draw(IEditorIndicators? indicators, float deltaSeconds)
    {
        if (indicators is EditorIndicatorsImpl impl)
        {
            while (impl.Toasts.TryDequeue(out var queued))
                _active.Add((queued, (float)(queued.AutoDismiss?.TotalSeconds ?? DefaultSeconds)));
        }

        if (_active.Count == 0) return;

        var viewport = ImGui.GetMainViewport();
        var anchor = new Vector2(
            viewport.Pos.X + viewport.Size.X - 20f,
            viewport.Pos.Y + viewport.Size.Y - 50f);

        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var (notification, remaining) = _active[i];
            remaining -= deltaSeconds;
            if (remaining <= 0f)
            {
                _active.RemoveAt(i);
                continue;
            }
            _active[i] = (notification, remaining);

            ImGui.SetNextWindowPos(anchor, ImGuiCond.Always, new Vector2(1f, 1f));
            ImGui.SetNextWindowBgAlpha(0.85f);
            ImGui.SetNextWindowSizeConstraints(new Vector2(240f, 0f), new Vector2(460f, 400f));

            var flags = ImGuiWindowFlags.NoDecoration
                      | ImGuiWindowFlags.AlwaysAutoResize
                      | ImGuiWindowFlags.NoSavedSettings
                      | ImGuiWindowFlags.NoFocusOnAppearing
                      | ImGuiWindowFlags.NoNav
                      | ImGuiWindowFlags.NoMove
                      | ImGuiWindowFlags.NoInputs;

            if (ImGui.Begin($"##bp_toast_{i}", flags))
            {
                ImGui.TextColored(SeverityColor(notification.Severity), notification.Title);
                if (!string.IsNullOrEmpty(notification.Body))
                {
                    ImGui.Separator();
                    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 420f);
                    ImGui.TextUnformatted(notification.Body);
                    ImGui.PopTextWrapPos();
                }
            }
            ImGui.End();

            anchor.Y -= ImGui.GetFrameHeightWithSpacing() * 2.4f;
        }
    }

    private static Vector4 SeverityColor(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Info    => new Vector4(0.40f, 0.70f, 1.00f, 1f),
        NotificationSeverity.Success => new Vector4(0.40f, 0.90f, 0.40f, 1f),
        NotificationSeverity.Warning => new Vector4(1.00f, 0.80f, 0.20f, 1f),
        NotificationSeverity.Error   => new Vector4(1.00f, 0.30f, 0.30f, 1f),
        _                            => new Vector4(1f, 1f, 1f, 1f),
    };
}
