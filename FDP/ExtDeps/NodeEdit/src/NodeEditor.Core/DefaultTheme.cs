using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.Core;

/// <summary>
/// Default implementation of <see cref="IEditorTheme"/>. A host can use
/// this directly or implement its own.
/// </summary>
public sealed class DefaultTheme : IEditorTheme
{
    public Vector4 BackgroundColor          { get; init; } = Rgb(0x1E, 0x1E, 0x1E);
    public Vector4 GridMinorColor           { get; init; } = Rgb(0x2A, 0x2A, 0x2A);
    public Vector4 GridMajorColor           { get; init; } = Rgb(0x3A, 0x3A, 0x3A);
    public Vector4 SelectionAccent          { get; init; } = Rgb(0xFF, 0xD7, 0x00);
    public Vector4 PrimarySelectionAccent   { get; init; } = Rgb(0xFF, 0xE6, 0x4D);
    public Vector4 ErrorColor               { get; init; } = Rgb(0xFF, 0x44, 0x44);
    public Vector4 WarningColor             { get; init; } = Rgb(0xFF, 0xAA, 0x00);
    public Vector4 TextDefault              { get; init; } = Rgb(0xE0, 0xE0, 0xE0);
    public Vector4 TextMuted                { get; init; } = Rgb(0x80, 0x80, 0x80);

    public float NodeCornerRadius      { get; init; } = 4f;
    public float NodeBorderThickness   { get; init; } = 2f;
    public float NodeHeaderHeight      { get; init; } = 24f;
    public float PinGlyphSize          { get; init; } = 10f;
    public float WireThicknessExec     { get; init; } = 3f;
    public float WireThicknessData     { get; init; } = 2f;

    public Vector4 GetCategoryHeaderColor(NodeCategory category) => category switch
    {
        NodeCategory.Function     => Rgb(0x2E, 0x5C, 0x8A),
        NodeCategory.Event        => Rgb(0xA9, 0x32, 0x26),
        NodeCategory.Pure         => Rgb(0x27, 0xAE, 0x60),
        NodeCategory.VariableGet  => Rgb(0x56, 0x65, 0x73),
        NodeCategory.VariableSet  => Rgb(0x56, 0x65, 0x73),
        NodeCategory.FlowControl  => Rgb(0xD3, 0x54, 0x00),
        NodeCategory.Macro        => Rgb(0x8E, 0x44, 0xAD),
        NodeCategory.Comment      => Rgb(0x7F, 0x8C, 0x8D),
        _                         => Rgb(0x7F, 0x8C, 0x8D),
    };

    private static Vector4 Rgb(byte r, byte g, byte b) =>
        new(r / 255f, g / 255f, b / 255f, 1f);

    /// <inheritdoc/>
    /// <remarks>
    /// The default theme has no font pipeline. Returns <see cref="IntPtr.Zero"/>
    /// so callers fall back to the active ImGui default font.
    /// </remarks>
    public nint GetFontForSize(float targetPixelSize) => 0;

    // ---- Attachment pill colors ----
    public Vector4 AttachmentDecoratorColor { get; init; } = new(0x8E / 255f, 0x44 / 255f, 0xAD / 255f, 1f);
    public Vector4 AttachmentFlagColor      { get; init; } = new(0x16 / 255f, 0xA0 / 255f, 0x85 / 255f, 1f);
    public Vector4 AttachmentPureColor      { get; init; } = new(0x27 / 255f, 0xAE / 255f, 0x60 / 255f, 1f);
    public Vector4 AttachmentCustomColor    { get; init; } = new(0x7F / 255f, 0x8C / 255f, 0x8D / 255f, 1f);

    // ---- Attachment pill geometry ----
    public float AttachmentHeight       { get; init; } = 20f;
    public float AttachmentCornerRadius { get; init; } = 8f;
    public float AttachmentGapAboveHost { get; init; } = 6f;
    public float AttachmentInterGap     { get; init; } = 4f;
}
