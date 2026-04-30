using Fdp.Presentation.Renderers;
using Hrot.Common;

namespace Hrot.Presentation.Renderers;

// ── ActivePerspective ─────────────────────────────────────────────────────────

/// <summary>
/// Renderer for the <see cref="ActivePerspective"/> managed singleton.
/// Returns <c>false</c> from <see cref="RenderValue"/> so the default
/// <c>ImGuiPropertyTree</c> renders the <c>Name</c> property as an editable leaf.
/// Modifying the perspective name via the inspector is a safe debug operation.
/// </summary>
[ImGuiRenderer(typeof(ActivePerspective))]
public sealed class ActivePerspectiveRenderer : IImGuiRenderer
{
    public string? GetSummary(object value)
    {
        var ap = (ActivePerspective)value;
        return $"'{ap.Name}'";
    }

    /// <summary>
    /// Returning <c>false</c> lets <see cref="Fdp.Presentation.Utils.ImGuiPropertyTree"/>
    /// render the <c>Name</c> property as a normal editable leaf, allowing the user to
    /// double-click and change the active perspective via <c>ComponentEditWindow</c>.
    /// </summary>
    public bool RenderValue(object value) => false;
}
