using System.Numerics;
using ImGuiNET;

namespace Hrot.SimHost.UI;

/// <summary>
/// Centralised SimHost title-bar colour theme (red).
/// Call <see cref="Push"/> immediately before <c>ImGui.Begin</c> and
/// <see cref="Pop"/> immediately after so the colours are restored before
/// the panel body is drawn.
/// </summary>
internal static class SimHostPanelColors
{
    /// <summary>Dark red — used when the title bar is not focused.</summary>
    internal static readonly Vector4 TitleBg       = new(0.40f, 0.08f, 0.08f, 1f);

    /// <summary>Bright red — used when the title bar is focused / active.</summary>
    internal static readonly Vector4 TitleBgActive = new(0.56f, 0.12f, 0.12f, 1f);

    /// <summary>Pushes the two SimHost title-bar colours onto the ImGui colour stack.</summary>
    internal static void Push()
    {
        ImGui.PushStyleColor(ImGuiCol.TitleBg,       TitleBg);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, TitleBgActive);
    }

    /// <summary>Pops the two colours pushed by <see cref="Push"/>.</summary>
    internal static void Pop() => ImGui.PopStyleColor(2);
}
