using System.Numerics;
using Hrot.Hsm.Editor.Host;
using NodeEditor.Core;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Theme;

/// <summary>
/// HSM-specific editor theme. Delegates all standard properties to
/// <see cref="DefaultTheme"/> and overrides the header color for
/// <see cref="NodeCategory.Custom"/> nodes (pseudo-states) to transparent,
/// since pseudo-states are rendered exclusively by HsmHistoryGlyphsRenderer.
/// </summary>
public sealed class HsmEditorTheme : IEditorTheme
{
    private static readonly DefaultTheme _default = new();

    public Vector4 BackgroundColor        => _default.BackgroundColor;
    public Vector4 GridMinorColor         => _default.GridMinorColor;
    public Vector4 GridMajorColor         => _default.GridMajorColor;
    public Vector4 SelectionAccent        => _default.SelectionAccent;
    public Vector4 PrimarySelectionAccent => _default.PrimarySelectionAccent;
    public Vector4 ErrorColor             => _default.ErrorColor;
    public Vector4 WarningColor           => _default.WarningColor;
    public Vector4 TextDefault            => _default.TextDefault;
    public Vector4 TextMuted              => _default.TextMuted;

    public float NodeCornerRadius    => _default.NodeCornerRadius;
    public float NodeBorderThickness => _default.NodeBorderThickness;
    public float NodeHeaderHeight    => _default.NodeHeaderHeight;
    public float PinGlyphSize        => _default.PinGlyphSize;
    public float WireThicknessExec   => _default.WireThicknessExec;
    public float WireThicknessData   => _default.WireThicknessData;

    public nint GetFontForSize(float targetPixelSize) => _default.GetFontForSize(targetPixelSize);

    /// <summary>
    /// Returns transparent (alpha=0) for <see cref="NodeCategory.Custom"/> so that
    /// pseudo-state node headers are invisible, leaving only the glyph renderer visible.
    /// All other categories delegate to <see cref="DefaultTheme"/>.
    /// </summary>
    public Vector4 GetCategoryHeaderColor(NodeCategory category) =>
        category == NodeCategory.Custom
            ? Vector4.Zero
            : _default.GetCategoryHeaderColor(category);

    /// <summary>
    /// Returns true if the given kind key represents an HSM pseudo-state.
    /// Pseudo-states (History, Deep-History, Final) have transparent node bodies.
    /// </summary>
    public static bool IsPseudostateKind(NodeKindKey kind) =>
        kind.Id == HsmKinds.Final       ||
        kind.Id == HsmKinds.History     ||
        kind.Id == HsmKinds.DeepHistory;
}
