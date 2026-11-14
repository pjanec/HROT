using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.Picker;

/// <summary>
/// ⭐ CE-4168 (AQ108 D26 P1/P2) — what a request's <see cref="PickerRequest.DrawPane"/> receives to draw the side pane:
/// the pane's inner width, the theme/icons, the shared <see cref="Images"/> cache, and <see cref="DrawDefault"/> to render the
/// standard content (image, name, category, description) before or after its own widgets.
/// </summary>
public sealed class PickerPaneContext
{
    /// <summary>Inner width of the pane in pixels.</summary>
    public float Width { get; set; }

    /// <summary>Editor theme.</summary>
    public required IEditorTheme Theme { get; init; }

    /// <summary>Icon provider.</summary>
    public required IIconProvider Icons { get; init; }

    /// <summary>The shared lazy image cache (<see cref="PickerEntry.ImageKey"/> → texture).</summary>
    public required PickerImageCache Images { get; init; }

    /// <summary>Draws the default pane content for the entry (image scaled to the width, name, category, description).</summary>
    public required System.Action DrawDefault { get; init; }
}
