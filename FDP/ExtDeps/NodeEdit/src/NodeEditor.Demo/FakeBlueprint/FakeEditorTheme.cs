using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>Default Unreal-inspired dark theme.</summary>
public sealed class FakeEditorTheme : IEditorTheme
{
    public Vector4 BackgroundColor        { get; } = new(0.10f, 0.10f, 0.10f, 1f);
    public Vector4 GridMinorColor         { get; } = new(0.20f, 0.20f, 0.20f, 1f);
    public Vector4 GridMajorColor         { get; } = new(0.25f, 0.25f, 0.25f, 1f);
    public Vector4 SelectionAccent        { get; } = new(0.21f, 0.52f, 0.89f, 1f);
    public Vector4 PrimarySelectionAccent { get; } = new(0.26f, 0.65f, 0.99f, 1f);
    public Vector4 ErrorColor             { get; } = new(0.90f, 0.10f, 0.10f, 1f);
    public Vector4 WarningColor           { get; } = new(0.95f, 0.70f, 0.10f, 1f);
    public Vector4 TextDefault            { get; } = new(1.00f, 1.00f, 1.00f, 1f);
    public Vector4 TextMuted              { get; } = new(0.60f, 0.60f, 0.60f, 1f);

    public float NodeCornerRadius    { get; } = 4f;
    public float NodeBorderThickness { get; } = 1.5f;
    public float NodeHeaderHeight    { get; } = 28f;
    public float PinGlyphSize        { get; } = 10f;
    public float WireThicknessExec   { get; } = 3f;
    public float WireThicknessData   { get; } = 2f;

    public Vector4 GetCategoryHeaderColor(NodeCategory category) => category switch
    {
        NodeCategory.Event    => new Vector4(0.65f, 0.07f, 0.07f, 1f),
        NodeCategory.Function => new Vector4(0.07f, 0.30f, 0.60f, 1f),
        NodeCategory.Macro    => new Vector4(0.25f, 0.15f, 0.50f, 1f),
        NodeCategory.VariableGet => new Vector4(0.07f, 0.40f, 0.20f, 1f),
        NodeCategory.VariableSet => new Vector4(0.05f, 0.35f, 0.15f, 1f),
        NodeCategory.FlowControl => new Vector4(0.20f, 0.20f, 0.20f, 1f),
        _                     => new Vector4(0.15f, 0.15f, 0.15f, 1f),
    };
}
