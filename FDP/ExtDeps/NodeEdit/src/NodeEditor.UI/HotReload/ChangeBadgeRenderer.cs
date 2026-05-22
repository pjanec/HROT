using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using System.Numerics;

namespace NodeEditor.UI.HotReload;

/// <summary>
/// Canvas overlay renderer that draws fading change-kind badges on affected nodes.
/// Call during the canvas overlay phase after nodes/wires.
/// </summary>
internal static class ChangeBadgeRenderer
{
    private const float BadgeRadius = 8f;

    public static void Render(
        RecentChanges  changes,
        IGraphModel    model,
        ViewportState  viewport,
        IEditorTheme   theme,
        TimeSpan       now)
    {
        foreach (var node in model.Nodes)
        {
            float alpha = changes.GetBadgeOpacity(node.Id, now);
            if (alpha <= 0f) continue;
            var kind = changes.GetBadgeKind(node.Id, now);
            if (kind is null) continue;
            DrawBadge(node, kind.Value, alpha, viewport);
        }
    }

    private static void DrawBadge(INodeModel node, ChangeBadgeKind kind, float alpha, ViewportState vp)
    {
        var nodePos   = vp.GraphToScreen(node.Position);
        var badgePos  = nodePos + new Vector2(80f, -BadgeRadius); // top-right approx
        var dl        = ImGui.GetWindowDrawList();

        uint bg = kind switch
        {
            ChangeBadgeKind.Added    => ToU32(0.15f, 0.80f, 0.25f, alpha * 0.9f),
            ChangeBadgeKind.Removed  => ToU32(0.90f, 0.15f, 0.15f, alpha * 0.9f),
            ChangeBadgeKind.Modified => ToU32(0.95f, 0.75f, 0.00f, alpha * 0.9f),
            _                        => ToU32(0.5f, 0.5f, 0.5f, alpha * 0.9f),
        };
        string icon = kind switch
        {
            ChangeBadgeKind.Added    => "+",
            ChangeBadgeKind.Removed  => "×",
            ChangeBadgeKind.Modified => "Δ",
            _                        => "?",
        };

        dl.AddCircleFilled(badgePos, BadgeRadius, bg);
        var textSize = ImGui.CalcTextSize(icon);
        var textPos  = badgePos - textSize * 0.5f;
        dl.AddText(textPos, ToU32(1f, 1f, 1f, alpha), icon);
    }

    private static uint ToU32(float r, float g, float b, float a)
        => ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, a));
}
