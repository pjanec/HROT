using System.Numerics;
using Fdp.Presentation.Icons;
using Fdp.Presentation.Windows;

namespace Fdp.Presentation.WindowManager;

/// <summary>
/// Status-bar section that renders a notification icon indicating whether
/// the <see cref="MessageLogWindow"/> contains unseen Warning/Error/Critical
/// messages that pass the current per-tab filters.
///
/// <para>Register once in each composition root (ClusterRunner and Editor):</para>
/// <code>
/// var section = new MessageLogStatusBarSection(msgLogWindow, windowManager);
/// windowManager.StatusBar.RegisterSection("msg_log_notify", sortOrder: 90, section.Render);
/// </code>
///
/// <para>The icon is drawn grayed out when all messages have been seen, and
/// tinted red when new attention-worthy messages are waiting. Clicking it
/// raises the Message Log window and switches to the first tab that has
/// unseen messages.</para>
///
/// <para>Icon coordinate "i31" in the Silk atlas corresponds to the yellow
/// exclamation icon. Adjust the constant if a different atlas cell is preferred.</para>
/// </summary>
public sealed class MessageLogStatusBarSection
{
    // famfamfam-silk atlas: "exclamation" icon (yellow warning triangle).
    // Row 'i' = index 8 (0-based), column 31 (1-based).
    private const string IconCoordinate = "i31";

    private static readonly Vector4 s_tintAlert = new(1.0f, 0.3f, 0.3f, 1.0f); // red
    private static readonly Vector4 s_tintIdle  = new(0.4f, 0.4f, 0.4f, 1.0f); // gray

    private readonly MessageLogWindow _window;
    private readonly WindowManager    _wm;

    /// <param name="window">The shared Message Log window.</param>
    /// <param name="wm">Window manager used to focus the window on icon click.</param>
    public MessageLogStatusBarSection(MessageLogWindow window, WindowManager wm)
    {
        _window = window;
        _wm     = wm;
    }

    /// <summary>
    /// Called each frame by <see cref="StatusBarManager"/>.
    /// Must be invoked inside an active ImGui frame and inside the status-bar window.
    /// </summary>
    public void Render()
    {
        bool hasUnseen = _window.HasUnobservedAttention;
        Vector4 tint = hasUnseen ? s_tintAlert : s_tintIdle;

        if (IconWidgets.IconButton(_wm.Atlas, "msg_log_status_btn", IconCoordinate, tint))
        {
            _wm.FocusWindow(_window.Id);
            if (hasUnseen)
                _window.FocusFirstAttentionTab();
        }

        if (Gui.IsItemHovered())
            Gui.SetTooltip(hasUnseen ? "Message Log (New Messages!)" : "Message Log");
    }
}
