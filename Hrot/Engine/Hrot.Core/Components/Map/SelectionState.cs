using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.IG.Components;

/// <summary>
/// ECS component tracking interactive selection state for a renderable entity.
///
/// Written by <see cref="Hrot.IG.Tools.StandardInteractionTool"/> in response to
/// operator clicks and box-select gestures, and read by
/// <see cref="Hrot.IG.Systems.SelectionRenderSystem"/> to draw per-entity
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
/// <see cref="Hrot.IG.Adapters.NedVisualizerAdapterConstants"/> (§CODE-STANDARDS §1).
///
/// Defined in <c>Hrot.Map.Common</c> so that both the IG and ScenarioEditor
/// projects can reference it without introducing circular project dependencies.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[ComponentId(GlobalComponentIds.SelectionState)]
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
    /// <see cref="Hrot.IG.Systems.SelectionRenderSystem"/>.
    /// </summary>
    public bool IsPrimarySelection;
}
