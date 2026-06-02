using System;
using System.Collections.Generic;
using CarKinem.Core;
using CarKinem.Formation;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace CarKinem.Tkb
{
    /// <summary>
    /// Reference ITkbEntityTranslator implementation.
    /// Projects VehicleParametersDto into four ECS components:
    ///   VehicleParams, VehicleState, NavState, PhysicsCollider.
    /// Also stamps navigation contract components required by MoveToExecutor:
    ///   NavigationIntent, NavigationStatus, FrustrationTicks, FormationController.
    /// Each AddComponent call is guarded by IsComponentTypeRegistered.
    ///
    /// <para>
    /// <b>BATCH-26 / STR-D20 fix:</b> <see cref="VehicleState"/> and <see cref="VehicleParams"/>
    /// are only injected when the template carries a <see cref="StrideRenderModelDefDto"/> with
    /// <c>ShapeKind == CollisionShapeKind.OrientedBox</c> (true vehicles).  Capsule-shaped
    /// entities (infantry, civilians) carry a <see cref="VehicleParametersDto"/> to configure
    /// their walk-speed parameters, but they must NOT receive <see cref="VehicleState"/> — the
    /// <see cref="Fdp.Toolkit.Navigation.Systems.NavigationIntentBridgeSystem"/> uses
    /// <c>!HasComponent&lt;VehicleState&gt;</c> as the crowd-eligibility guard, so injecting it
    /// unconditionally on infantry prevented crowd registration (F6 GPU failure).
    /// </para>
    ///
    /// <para>
    /// The navigation contract components (NavigationIntent, NavigationStatus, FrustrationTicks,
    /// FormationController) continue to be stamped on ALL VehicleParametersDto-carrying entities,
    /// since both infantry and vehicles need them for the BehaviorTree MoveToExecutor front door.
    /// </para>
    /// </summary>
    public sealed class VehicleKinematicsTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors()
        {
            yield return typeof(VehicleParametersDto);
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            var dto = template.GetDescriptor<VehicleParametersDto>();
            if (dto == null) return;

            // ── STR-D20 fix (BATCH-26): only inject VehicleParams/VehicleState for vehicle-shaped
            // entities (OrientedBox).  Infantry/civilian templates carry VehicleParametersDto for
            // walk-speed configuration but have ShapeKind=Capsule — they must NOT receive
            // VehicleState or the NavigationIntentBridgeSystem will exclude them from crowd nav.
            var renderDef = template.GetDescriptor<StrideRenderModelDefDto>();
            bool isVehicleShaped = renderDef == null
                || renderDef.ShapeKind == CollisionShapeKind.OrientedBox;

            if (isVehicleShaped)
            {
                if (repo.IsComponentTypeRegistered<VehicleParams>() && !repo.HasComponent<VehicleParams>(entity))
                    repo.AddComponent(entity, new VehicleParams
                    {
                        Length      = dto.Length,
                        Width       = dto.Width,
                        WheelBase   = dto.Length * 0.6f,
                        MaxSpeedFwd = dto.MaxSpeedFwd,
                        MaxAccel    = dto.MaxAccel
                    });

                if (repo.IsComponentTypeRegistered<VehicleState>() && !repo.HasComponent<VehicleState>(entity))
                    repo.AddComponent(entity, new VehicleState { Speed = 0, SteerAngle = 0 });
            }

            if (repo.IsComponentTypeRegistered<NavState>() && !repo.HasComponent<NavState>(entity))
                repo.AddComponent(entity, new NavState { Mode = KinematicsMode.None });

            if (repo.IsComponentTypeRegistered<PhysicsCollider>())
                repo.AddComponent(entity, new PhysicsCollider
                {
                    Radius         = System.Math.Max(dto.Length, dto.Width) / 2f,
                    CollisionLayer = 1
                });

            if (repo.IsComponentTypeRegistered<NavigationIntent>() && !repo.HasComponent<NavigationIntent>(entity))
                repo.AddComponent(entity, new NavigationIntent());

            if (repo.IsComponentTypeRegistered<NavigationStatus>() && !repo.HasComponent<NavigationStatus>(entity))
                repo.AddComponent(entity, new NavigationStatus());

            if (repo.IsComponentTypeRegistered<FrustrationTicks>() && !repo.HasComponent<FrustrationTicks>(entity))
                repo.AddComponent(entity, new FrustrationTicks());

            if (repo.IsComponentTypeRegistered<FormationController>() && !repo.HasComponent<FormationController>(entity))
                repo.AddComponent(entity, new FormationController());
        }
    }
}
