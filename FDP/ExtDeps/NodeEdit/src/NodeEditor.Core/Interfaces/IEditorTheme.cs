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
}
