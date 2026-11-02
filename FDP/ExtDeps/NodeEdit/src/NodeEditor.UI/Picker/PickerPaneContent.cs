using ImGuiNET;
using NodeEditor.Core.Interfaces;
using System.Numerics;

namespace NodeEditor.UI.Picker;

/// <summary>
/// ⭐ CE-4168 (AQ108 D26 P1) — the DEFAULT content of the side pane: the entry's image (<see cref="PickerEntry.ImageKey"/> through the
/// shared cache, scaled to the pane width keeping its aspect; absent ⇒ no placeholder), else its atlas icon, then name, category and the
/// wrapped description. ⛔ Text goes through <c>TextUnformatted</c> — ImGui's printf-style <c>Text</c> would choke on a <c>%</c>.
/// </summary>
internal static class PickerPaneContent
{
    private static readonly Vector2 IconSize = new(64f, 64f);

    public static void DrawMuted(IEditorTheme theme, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, theme.TextMuted);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    public static void DrawDefault(PickerEntry entry, PickerRenderContext ctx, PickerImageCache images, float width)
    {
        if (entry.ImageKey is { Length: > 0 } key && images.TryGet(key, out var img))
        {
            var size = PickerImageCache.FitToWidth(img.Width, img.Height, width - ImGui.GetStyle().WindowPadding.X);
            ImGui.Image(img.Texture, size);
        }
        else if (entry.IconKey is { Length: > 0 } iconKey && ctx.Icons.TryGet(iconKey, out var icon))
        {
            ImGui.Image(icon.TextureId, IconSize, icon.Uv0, icon.Uv1);
        }

        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextDefault);
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(entry.Name);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();

        if (entry.Category is { Length: > 0 } cat)
            DrawMuted(ctx.Theme, cat.Replace("/", PickerTreeBuilder.FoldSeparator));

        if (entry.Description is { Length: > 0 } desc)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextUnformatted(desc);
            ImGui.PopTextWrapPos();
        }
    }
}
