using System.Numerics;
using ImGuiNET;

namespace Hrot.ExCon.Panels;

/// <summary>
/// Centralised ExCon title-bar colour theme (violet).
/// Call <see cref="Push"/> immediately before <c>ImGui.Begin</c> and
/// <see cref="Pop"/> immediately after so the colours are restored before
/// the panel body is drawn.
/// </summary>
internal static class ExConPanelColors
{
    /// <summary>Dark violet — used when the title bar is not focused.</summary>
    internal static readonly Vector4 TitleBg       = new(0.32f, 0.08f, 0.48f, 1f);

    /// <summary>Bright violet — used when the title bar is focused / active.</summary>
    internal static readonly Vector4 TitleBgActive = new(0.44f, 0.12f, 0.62f, 1f);

    /// <summary>Pushes the two ExCon title-bar colours onto the ImGui colour stack.</summary>
    internal static void Push()
    {
        ImGui.PushStyleColor(ImGuiCol.TitleBg,       TitleBg);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, TitleBgActive);
    }

    /// <summary>Pops the two colours pushed by <see cref="Push"/>.</summary>
    internal static void Pop() => ImGui.PopStyleColor(2);
}
