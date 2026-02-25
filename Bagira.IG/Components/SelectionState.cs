using System.Runtime.InteropServices;

namespace Bagira.IG.Components;

/// <summary>
/// ECS component tracking interactive selection state for a renderable entity.
///
/// Written by <see cref="Bagira.IG.Tools.StandardInteractionTool"/> in response to
/// operator clicks and box-select gestures, and read by
/// <see cref="Bagira.IG.Systems.SelectionRenderSystem"/> to draw per-entity
/// selection overlays.
///
/// Rendering convention:
/// <list type="bullet">
///   <item>
///     <see cref="IsPrimarySelection"/> <c>true</c> → green filled circle with outline
///     (the first entity in the active selection set).
///   </item>
///   <item>
///     <see cref="IsSelected"/> <c>true</c>, <see cref="IsPrimarySelection"/> <c>false</c>
///     → yellow outline only (secondary multi-select entities).
///   </item>
/// </list>
///
/// All sizes / thresholds used when rendering the ring come from
/// <see cref="Bagira.IG.Adapters.SstVisualizerAdapterConstants"/> (§CODE-STANDARDS §1).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SelectionState
{
    /// <summary>
    /// <c>true</c> when the entity is part of the current operator selection.
    /// Cleared to <c>false</c> by a new click without Shift/Ctrl modifiers.
    /// </summary>
    public bool IsSelected;

    /// <summary>
    /// <c>true</c> when this entity is the primary (first) entity in the selection.
    /// Only one entity holds <c>IsPrimarySelection = true</c> at a time.
    /// Drives the green fill / green outline ring colour in
    /// <see cref="Bagira.IG.Systems.SelectionRenderSystem"/>.
    /// </summary>
    public bool IsPrimarySelection;
}
