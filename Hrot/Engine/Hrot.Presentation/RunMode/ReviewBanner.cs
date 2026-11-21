using System.Numerics;
using Hrot.UI.Common.Facades;
using ImGuiNET;

namespace Hrot.UI.Common.RunMode;

/// <summary>
/// ⭐ CE-4321 (L-5, docs/UX/DESIGN_Scenario_Lifecycle_Review.md LC-16) — the PLAYER's sign that a During Action Review is on: a banner
/// <i>"REVIEW — the exercise is paused"</i> across the top of the screen for the WHOLE DAR (the Player's time bar is suppressed, so nothing else says it).
/// Read from the node's run transport (<see cref="TransportMode.Review"/>), drawn on the foreground list over everything; nothing at all otherwise.
/// </summary>
public static class ReviewBanner
{
    public const string Text = "REVIEW — the exercise is paused";

    /// <summary>Whether the banner shows in <paramref name="mode"/> (null = no run transport: never).</summary>
    public static bool Shows(TransportMode? mode) => mode == TransportMode.Review;

    /// <summary>Draws the banner when <see cref="Shows"/> (call once per frame inside the ImGui frame). 0 B: a const text, no formatting.</summary>
    public static void Draw(TransportMode? mode)
    {
        if (!Shows(mode)) return;
        var vp = ImGui.GetMainViewport();
        var dl = ImGui.GetForegroundDrawList();
        var size = ImGui.CalcTextSize(Text);
        const float padX = 18f, padY = 8f, top = 12f;
        var min = new Vector2(vp.Pos.X + (vp.Size.X - size.X) / 2f - padX, vp.Pos.Y + top);
        var max = new Vector2(min.X + size.X + 2 * padX, min.Y + size.Y + 2 * padY);
        dl.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.10f, 0.08f, 0.02f, 0.88f)), 6f);
        dl.AddRect(min, max, ImGui.GetColorU32(new Vector4(0.96f, 0.70f, 0.18f, 1f)), 6f, ImDrawFlags.None, 2f);   // DAR amber
        dl.AddText(new Vector2(min.X + padX, min.Y + padY), ImGui.GetColorU32(new Vector4(1f, 0.86f, 0.45f, 1f)), Text);
    }
}
