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
/// <see cref="Hrot.ScenarioEditor.Rendering.SelectionRenderConstants"/> (§CODE-STANDARDS §1).
///
/// Defined in <c>Hrot.Map.Common</c> so that both the IG and ScenarioEditor
/// projects can reference it without introducing circular project dependencies.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[ComponentId(GlobalComponentIds.SelectionState)]
// ⭐ CE-4049 — UI state, never recorded: a seek must not overwrite what the operator selected. The truth is SelectionStore (a world singleton
//   keyed by network id); this per-entity copy is rewritten from it every frame (EcsSelectionState.Sync). Was NoScenario only — recorded by
//   default, never by decision (the coordinator's replay census, CE-4045 §2).
[DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]
public struct SelectionState
{
    /// <summary>
    /// <c>true</c> when the entity is part of the current operator selection.
    /// Cleared to <c>false</c> by a new click without Shift/Ctrl modifiers.
    /// </summary>
    [MarshalAs(UnmanagedType.I1)]
    public bool IsSelected;

    /// <summary>
    /// <c>true</c> when this entity is the primary (first) entity in the selection.
    /// Only one entity holds <c>IsPrimarySelection = true</c> at a time.
    /// Drives the green fill / green outline ring colour in
    /// <see cref="Hrot.IG.Systems.SelectionRenderSystem"/>.
    /// </summary>
    [MarshalAs(UnmanagedType.I1)]
    public bool IsPrimarySelection;
}
