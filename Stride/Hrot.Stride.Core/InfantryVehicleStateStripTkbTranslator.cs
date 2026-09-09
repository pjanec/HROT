#nullable enable
using System;
using System.Collections.Generic;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Stride.Core;

/// <summary>
/// Stride-muscle translator that runs AFTER <c>VehicleKinematicsTkbTranslator</c> and strips
/// <c>VehicleState</c> and <c>VehicleParams</c> from capsule-shaped (infantry) entities.
///
/// <para>
/// <b>Why this exists here and not in shared Fdp.Toolkits:</b>
/// The shared <c>VehicleKinematicsTkbTranslator</c> unconditionally adds <c>VehicleState</c>
/// to every entity that carries a <c>VehicleParametersDto</c>. On the Stride muscle, infantry
/// entities are capsule-shaped and must NOT carry <c>VehicleState</c> because the crowd bridge
/// (<c>NavigationIntentBridgeSystem</c>) uses <c>!HasComponent&lt;VehicleState&gt;()</c> as its
/// crowd-eligibility guard. Previously this decision leaked into the shared translator (gated on
/// shape); we relocate it here so shared code stays clean. The shared translator is kept == main.
/// </para>
///
/// <para>
/// <b>Execution order:</b> Must be placed immediately after <c>VehicleKinematicsTkbTranslator</c>
/// in the <c>BuildTranslators()</c> list — the translator runner iterates the list in order
/// (<c>foreach (var t in _translators) t.Inject(...)</c> in
/// <c>NetworkSpawningSystem.ProcessSpawn</c>), so position in the list is the guarantee.
/// </para>
/// </summary>
public sealed class InfantryVehicleStateStripTkbTranslator : ITkbEntityTranslator
{
    /// <inheritdoc/>
    /// <remarks>
    /// Mirrors <c>VehicleKinematicsTkbTranslator.GetConsumedDescriptors()</c> so this translator
    /// is considered for the same entities.
    /// </remarks>
    public IEnumerable<Type> GetConsumedDescriptors()
    {
        yield return typeof(VehicleParametersDto);
    }

    /// <inheritdoc/>
    public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
    {
        // Only act when this is a VehicleParametersDto entity.
        var dto = template.GetDescriptor<VehicleParametersDto>();
        if (dto == null) return;

        // Only capsule-shaped entities are infantry; everything else keeps VehicleState.
        var renderDef = template.GetDescriptor<StrideRenderModelDefDto>();
        if (renderDef == null || renderDef.ShapeKind != CollisionShapeKind.Capsule)
            return;

        // Strip VehicleState from infantry so NavigationIntentBridgeSystem (crowd bridge)
        // can use !HasComponent<VehicleState>() as its crowd-eligibility guard.
        if (repo.IsComponentTypeRegistered<VehicleState>() && repo.HasComponent<VehicleState>(entity))
            repo.RemoveComponent<VehicleState>(entity);

        // Strip VehicleParams as well — infantry entities do not use vehicle kinematics.
        if (repo.IsComponentTypeRegistered<VehicleParams>() && repo.HasComponent<VehicleParams>(entity))
            repo.RemoveComponent<VehicleParams>(entity);
    }
}
