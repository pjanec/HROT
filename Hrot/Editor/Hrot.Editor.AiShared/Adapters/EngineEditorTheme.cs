using System;
using System.Numerics;
using ImGuiNET;
using NodeEditor.Core;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Editor.AiShared.Adapters;

/// <summary>
/// Production <see cref="IEditorTheme"/> for the engine editor.
/// Delegates geometry, colors, and attachment/container defaults to
/// <see cref="DefaultTheme"/>; overrides <see cref="GetFontForSize"/> to
/// query the engine's currently loaded ImGui fonts.
/// <para>
/// The palette is aligned to the engine's dark-mode UI so the NodeEdit canvas
/// feels native in the ClusterRunner editor shell.
/// </para>
/// </summary>
public sealed class EngineEditorTheme : IEditorTheme
{
    // ── Backing DefaultTheme supplies all property values ────────────────────

    private static readonly DefaultTheme _base = new DefaultTheme();

    // ── IEditorTheme surface — colors ────────────────────────────────────────

    /// <inheritdoc/>
    public Vector4 BackgroundColor        => _base.BackgroundColor;

    /// <inheritdoc/>
    public Vector4 GridMinorColor         => _base.GridMinorColor;

    /// <inheritdoc/>
    public Vector4 GridMajorColor         => _base.GridMajorColor;

    /// <inheritdoc/>
    public Vector4 SelectionAccent        => _base.SelectionAccent;

    /// <inheritdoc/>
    public Vector4 PrimarySelectionAccent => _base.PrimarySelectionAccent;

    /// <inheritdoc/>
    public Vector4 ErrorColor             => _base.ErrorColor;

    /// <inheritdoc/>
    public Vector4 WarningColor           => _base.WarningColor;

    /// <inheritdoc/>
    public Vector4 TextDefault            => _base.TextDefault;

    /// <inheritdoc/>
    public Vector4 TextMuted              => _base.TextMuted;

    // ── Per-category header colors ────────────────────────────────────────────

    /// <inheritdoc/>
    public Vector4 GetCategoryHeaderColor(NodeCategory category)
        => _base.GetCategoryHeaderColor(category);

    // ── IEditorTheme surface — geometry ──────────────────────────────────────

    /// <inheritdoc/>
    public float NodeCornerRadius    => _base.NodeCornerRadius;

    /// <inheritdoc/>
    public float NodeBorderThickness => _base.NodeBorderThickness;

    /// <inheritdoc/>
    public float NodeHeaderHeight    => _base.NodeHeaderHeight;

    /// <inheritdoc/>
    public float PinGlyphSize        => _base.PinGlyphSize;

    /// <inheritdoc/>
    public float WireThicknessExec   => _base.WireThicknessExec;

    /// <inheritdoc/>
    public float WireThicknessData   => _base.WireThicknessData;

    // ── Font resolution ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Searches the ImGui font atlas for the closest font to
    /// <paramref name="targetPixelSize"/> by comparing each font's pixel size.
    /// Returns <see cref="IntPtr.Zero"/> when no ImGui context is active or
    /// no fonts are loaded, so callers fall back to the ImGui default font.
    /// Never throws.
    /// </remarks>
    public unsafe nint GetFontForSize(float targetPixelSize)
    {
        // Guard against missing context BEFORE any ImGui dereference.
        // AccessViolationException is a corrupted-state exception that managed
        // try/catch cannot handle, so we must prevent the native call entirely.
        if (ImGui.GetCurrentContext() == IntPtr.Zero)
            return IntPtr.Zero;

        try
        {
            var io = ImGui.GetIO();
            if (io.Fonts.Fonts.Size == 0)
                return IntPtr.Zero;

            ImFontPtr best     = io.Fonts.Fonts[0];
            float     bestDiff = Math.Abs(best.FontSize - targetPixelSize);

            for (int i = 1; i < io.Fonts.Fonts.Size; i++)
            {
                var   font = io.Fonts.Fonts[i];
                float diff = Math.Abs(font.FontSize - targetPixelSize);
                if (diff < bestDiff)
                {
                    best     = font;
                    bestDiff = diff;
                }
            }

            // Return the native pointer; zero means "use default".
            nint ptr = best.NativePtr == null ? IntPtr.Zero : (nint)best.NativePtr;
            return ptr;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    // ── Attachment pill colors (engine-aligned overrides) ────────────────────
    // Matching DefaultTheme values; override here if engine palette diverges.

    /// <inheritdoc/>
    public Vector4 AttachmentDecoratorColor => _base.AttachmentDecoratorColor;

    /// <inheritdoc/>
    public Vector4 AttachmentFlagColor      => _base.AttachmentFlagColor;

    /// <inheritdoc/>
    public Vector4 AttachmentPureColor      => _base.AttachmentPureColor;

    /// <inheritdoc/>
    public Vector4 AttachmentCustomColor    => _base.AttachmentCustomColor;

    // ── Attachment geometry ───────────────────────────────────────────────────

    /// <inheritdoc/>
    public float AttachmentHeight       => _base.AttachmentHeight;

    /// <inheritdoc/>
    public float AttachmentCornerRadius => _base.AttachmentCornerRadius;

    /// <inheritdoc/>
    public float AttachmentGapAboveHost => _base.AttachmentGapAboveHost;

    /// <inheritdoc/>
    public float AttachmentInterGap     => _base.AttachmentInterGap;
}
