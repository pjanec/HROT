using System;
using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Editor.AiShared.Adapters;

/// <summary>
/// Production <see cref="IEditorTheme"/> for the engine editor.
/// Uses the demo's Unreal-inspired dark color/geometry scheme (mirroring
/// <c>FakeEditorTheme</c> from the NodeEdit Demo specimen) so the Blueprint,
/// BTree, and HSM canvases all share the same look.
/// <para>
/// <see cref="GetFontForSize"/> queries the engine's currently loaded ImGui
/// fonts. All other members are literal constants that match the demo values.
/// </para>
/// </summary>
public sealed class EngineEditorTheme : IEditorTheme
{
    // ── IEditorTheme surface — colors (demo literal values) ──────────────────

    /// <inheritdoc/>
    public Vector4 BackgroundColor        { get; } = new(0.10f, 0.10f, 0.10f, 1f);

    /// <inheritdoc/>
    public Vector4 GridMinorColor         { get; } = new(0.20f, 0.20f, 0.20f, 1f);

    /// <inheritdoc/>
    public Vector4 GridMajorColor         { get; } = new(0.25f, 0.25f, 0.25f, 1f);

    /// <inheritdoc/>
    public Vector4 SelectionAccent        { get; } = new(0.21f, 0.52f, 0.89f, 1f);

    /// <inheritdoc/>
    public Vector4 PrimarySelectionAccent { get; } = new(0.26f, 0.65f, 0.99f, 1f);

    /// <inheritdoc/>
    public Vector4 ErrorColor             { get; } = new(0.90f, 0.10f, 0.10f, 1f);

    /// <inheritdoc/>
    public Vector4 WarningColor           { get; } = new(0.95f, 0.70f, 0.10f, 1f);

    /// <inheritdoc/>
    public Vector4 TextDefault            { get; } = new(1.00f, 1.00f, 1.00f, 1f);

    /// <inheritdoc/>
    public Vector4 TextMuted              { get; } = new(0.60f, 0.60f, 0.60f, 1f);

    // ── Per-category header colors (demo per-category map) ────────────────────

    /// <inheritdoc/>
    public Vector4 GetCategoryHeaderColor(NodeCategory category) => category switch
    {
        NodeCategory.Event       => new Vector4(0.65f, 0.07f, 0.07f, 1f),
        NodeCategory.Function    => new Vector4(0.07f, 0.30f, 0.60f, 1f),
        // UE-faithful: pure functions/operators are green (impure functions stay blue). A brighter,
        // more-saturated green than VariableGet so the two greens stay visually distinct.
        NodeCategory.Pure        => new Vector4(0.16f, 0.55f, 0.24f, 1f),
        NodeCategory.Macro       => new Vector4(0.25f, 0.15f, 0.50f, 1f),
        NodeCategory.VariableGet => new Vector4(0.07f, 0.40f, 0.20f, 1f),
        NodeCategory.VariableSet => new Vector4(0.05f, 0.35f, 0.15f, 1f),
        NodeCategory.FlowControl => new Vector4(0.85f, 0.45f, 0.12f, 1f),
        _                        => new Vector4(0.15f, 0.15f, 0.15f, 1f),
    };

    // ── IEditorTheme surface — geometry (demo literal values) ────────────────

    /// <inheritdoc/>
    public float NodeCornerRadius    { get; } = 4f;

    /// <inheritdoc/>
    public float NodeBorderThickness { get; } = 1.5f;

    /// <inheritdoc/>
    public float NodeHeaderHeight    { get; } = 28f;

    /// <inheritdoc/>
    public float PinGlyphSize        { get; } = 10f;

    /// <inheritdoc/>
    public float WireThicknessExec   { get; } = 3f;

    /// <inheritdoc/>
    public float WireThicknessData   { get; } = 2f;

    // ── Font resolution ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Resolves against the canvas font ladder baked by
    /// <see cref="Fdp.Presentation.Fonts.EditorFontService"/> and published to
    /// <see cref="Fdp.Presentation.Fonts.EditorFontRegistry"/>, using the
    /// "smallest baked size &gt;= target, else largest" policy (avoids upscaling blur).
    /// Returns <see cref="IntPtr.Zero"/> when no ladder has been baked (e.g. headless
    /// tests, or before the shell sets up fonts) so callers fall back to the ImGui
    /// default font. Does not touch the live ImGui context and never throws.
    /// </remarks>
    public nint GetFontForSize(float targetPixelSize)
        => Fdp.Presentation.Fonts.EditorFontRegistry.ResolveCanvasFont(targetPixelSize);

    // ── Attachment pill colors — use IEditorTheme interface defaults ──────────
    // These match DefaultTheme; no override needed. IEditorTheme provides
    // default implementations so all attachment members resolve correctly.
}
