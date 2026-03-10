using System;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;

namespace Bagira.IG.Systems;

/// <summary>
/// Defines a single named map layer with its rendering bit-mask and the predicate
/// used by <see cref="MapLayerAssignmentSystem"/> to decide whether an entity
/// belongs to that layer.
/// </summary>
/// <param name="Name">
/// Standardised JSON string key for this layer (e.g. <c>"units_ground"</c>).
/// Used both in <c>IGCapabilitiesAnnounce.LayerTreeJson</c> and in the JSON Merge
/// Patch sent by the IOS over the <c>MapInteractionConfig</c> DDS topic.
/// </param>
/// <param name="BitMask">
/// Single power-of-two bit allocated to this layer within
/// <c>MapCanvas.ActiveLayerMask</c> and <c>MapDisplayComponent.LayerMask</c>.
/// </param>
/// <param name="IsMember">
/// Evaluated once per entity per rescan pass by <see cref="MapLayerAssignmentSystem"/>.
/// Parameters:
/// <list type="bullet">
///   <item>The ECS entity handle.</item>
///   <item>
///     The entity's <see cref="DISEntityType"/> resolved from the entity header
///     (zero if none has been assigned).
///   </item>
///   <item>
///     An <see cref="ISimulationView"/> allowing component-presence checks via
///     <c>HasComponent&lt;T&gt;</c> and read-only component access via
///     <c>GetComponentRO&lt;T&gt;</c>.
///   </item>
/// </list>
/// Returns <c>true</c> when the entity should belong to this layer.
/// </param>
public record MapLayerDefinition(
    string Name,
    uint   BitMask,
    Func<Entity, DISEntityType, ISimulationView, bool> IsMember);
