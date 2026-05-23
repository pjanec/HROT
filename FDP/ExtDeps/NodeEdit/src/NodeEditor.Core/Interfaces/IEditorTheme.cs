using System.Numerics;

namespace NodeEditor.Core.Interfaces;

/// <summary>Theme — colors, fonts, sizes used by the editor.</summary>
public interface IEditorTheme
{
    Vector4 BackgroundColor { get; }
    Vector4 GridMinorColor { get; }
    Vector4 GridMajorColor { get; }
    Vector4 SelectionAccent { get; }
    Vector4 PrimarySelectionAccent { get; }
    Vector4 ErrorColor { get; }
    Vector4 WarningColor { get; }
    Vector4 TextDefault { get; }
    Vector4 TextMuted { get; }

    Vector4 GetCategoryHeaderColor(Primitives.NodeCategory category);

    float NodeCornerRadius { get; }
    float NodeBorderThickness { get; }
    float NodeHeaderHeight { get; }
    float PinGlyphSize { get; }
    float WireThicknessExec { get; }
    float WireThicknessData { get; }

    /// <summary>
    /// Retrieve an opaque pointer to a host-managed ImGui font optimised for the
    /// target pixel size.  Returns <see cref="IntPtr.Zero"/> to fall back to the
    /// default ImGui font.
    /// </summary>
    nint GetFontForSize(float targetPixelSize);

    // ---- Attachment pill colors (default values match spec section 5.3) ----

    /// <summary>Background color for Decorator category pills. Default: #8E44AD.</summary>
    Vector4 AttachmentDecoratorColor => new(0x8E / 255f, 0x44 / 255f, 0xAD / 255f, 1f);

    /// <summary>Background color for Flag category pills. Default: #16A085.</summary>
    Vector4 AttachmentFlagColor      => new(0x16 / 255f, 0xA0 / 255f, 0x85 / 255f, 1f);

    /// <summary>Background color for Pure category pills. Default: #27AE60.</summary>
    Vector4 AttachmentPureColor      => new(0x27 / 255f, 0xAE / 255f, 0x60 / 255f, 1f);

    /// <summary>Background color for Custom category pills. Default: #7F8C8D.</summary>
    Vector4 AttachmentCustomColor    => new(0x7F / 255f, 0x8C / 255f, 0x8D / 255f, 1f);

    // ---- Attachment pill geometry ----

    /// <summary>Pill height in canvas units at zoom 1.0. Default: 20.</summary>
    float AttachmentHeight        => 20f;

    /// <summary>Pill corner radius at zoom 1.0. Default: 8.</summary>
    float AttachmentCornerRadius  => 8f;

    /// <summary>Vertical gap between pill bottom and host header. Default: 6.</summary>
    float AttachmentGapAboveHost  => 6f;

    /// <summary>Horizontal gap between adjacent pills. Default: 4.</summary>
    float AttachmentInterGap      => 4f;
}
