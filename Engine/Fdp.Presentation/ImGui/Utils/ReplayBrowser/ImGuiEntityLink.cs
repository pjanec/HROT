using System.Numerics;
using Fdp.Core;
using ImGuiNET;

namespace Fdp.Presentation.Utils.ReplayBrowser;

/// <summary>
/// Utility for rendering and parsing entity-handle links in the replay browser UI.
/// Entity handles are formatted as "[&lt;index&gt;, v&lt;generation&gt;]".
/// </summary>
public static class ImGuiEntityLink
{
    private static readonly Vector4 ExConViolet = new(0.7f, 0.45f, 0.8f, 1f);

    /// <summary>
    /// Renders <paramref name="label"/> as a SmallButton in ExConViolet color.
    /// Returns true if the button was clicked this frame.
    /// </summary>
    public static bool Draw(string label)
    {
        Gui.PushStyleColor(ImGuiCol.Text, ExConViolet);
        bool clicked = Gui.SmallButton(label);
        Gui.PopStyleColor();
        return clicked;
    }

    /// <summary>
    /// Attempts to parse an entity handle string into an <see cref="Entity"/>.
    /// Accepted formats: "[&lt;index&gt;, v&lt;generation&gt;]" or "[&lt;index&gt;, &lt;generation&gt;]".
    /// Internal whitespace is tolerated. The 'v'/'V' prefix on the generation is optional.
    /// Returns false without throwing on any malformed input.
    /// </summary>
    public static bool TryParse(string text, out Entity entity)
    {
        entity = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim();
        // Minimum: "[0,0]" = 5 characters
        if (s.Length < 5 || s[0] != '[' || s[s.Length - 1] != ']')
            return false;

        var inner = s.Substring(1, s.Length - 2);
        var commaIdx = inner.IndexOf(',');
        if (commaIdx < 0)
            return false;

        var indexPart = inner.Substring(0, commaIdx).Trim();
        var genPart = inner.Substring(commaIdx + 1).Trim();

        if (!int.TryParse(indexPart, out int index) || index < 0)
            return false;

        // Strip optional 'v' or 'V' prefix from generation
        if (genPart.Length > 0 && (genPart[0] == 'v' || genPart[0] == 'V'))
            genPart = genPart.Substring(1);

        if (!ushort.TryParse(genPart, out ushort generation))
            return false;

        entity = new Entity(index, generation);
        return true;
    }
}
