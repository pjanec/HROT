using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.IG.Components;

/// <summary>
/// ECS component caching the culling and level-of-detail state for a renderable entity.
///
/// Written each frame by <see cref="Hrot.IG.Systems.MapCullingSystem"/> by comparing
/// the entity's world-space XY position against the active camera viewport rectangle.
///
/// Read by the <c>DebugGizmoLayer</c> and the <c>StandardInteractionTool</c> position
/// delegate to gate rendering: off-screen entities are skipped, eliminating wasted draw calls.
///
/// LOD levels are named constants in <see cref="CullingStateConstants"/> (§CODE-STANDARDS §1).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[ComponentId(GlobalComponentIds.CullingState)]
[DataPolicy(DataPolicy.NoSave)]
public struct CullingState
{
    /// <summary>
    /// <c>true</c> when the entity's XY position falls within the active camera
    /// viewport bounds this frame.
    ///
    /// <c>false</c> causes the <c>DebugGizmoLayer</c> to skip icon, label, damage-bar,
    /// and selection-ring draw calls for this entity.
    /// </summary>
    [MarshalAs(UnmanagedType.I1)]
    public bool IsVisible;

    /// <summary>
    /// Level-of-detail assigned this frame, derived from the camera's current zoom.
    ///
    /// <list type="table">
    ///   <item>
    ///     <term><see cref="CullingStateConstants.LodFull"/> (0)</term>
    ///     <description>Full detail — icon, label, damage bar, sensor overlays.</description>
    ///   </item>
    ///   <item>
    ///     <term><see cref="CullingStateConstants.LodSimplified"/> (1)</term>
    ///     <description>Simplified — icon and label only.</description>
    ///   </item>
    ///   <item>
    ///     <term><see cref="CullingStateConstants.LodIconOnly"/> (2)</term>
    ///     <description>Icon only, drawn at 50 % scale. Used when the camera is very zoomed out.</description>
    ///   </item>
    /// </list>
    /// </summary>
    public byte LodLevel;
}
