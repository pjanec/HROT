using System;
using System.Collections.Generic;
using CarKinem.Core;
using CarKinem.Formation;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Tkb.Domain;
using static CarKinem.Core.GizmoShapeCategory;

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

            // ── BATCH-S2-G2: populate Map2DFootprint for ALL entities carrying VehicleParametersDto ──
            // Mirrors StrideVisualBindingSystem.ResolveShapeDims "0→default" logic:
            //   OrientedBox → GroundVehicle, dims from BoxHalfX/Y (fallback to dto.Length/Width)
            //   Capsule     → Humanoid,      dims from ShapeRadius  (fallback to PhysicsCollider.Radius)
            //   other       → Unknown,        radius-equivalent dims
            if (repo.IsComponentTypeRegistered<Map2DFootprint>() && !repo.HasComponent<Map2DFootprint>(entity))
            {
                Map2DFootprint footprint;
                if (renderDef == null)
                {
                    // No render def available — derive from dto dims, assume vehicle shaped.
                    footprint = new Map2DFootprint
                    {
                        LengthM = dto.Length,
                        WidthM  = dto.Width,
                        Shape   = GroundVehicle,
                    };
                }
                else
                {
                    switch (renderDef.ShapeKind)
                    {
                        case CollisionShapeKind.OrientedBox:
                        {
                            float halfX = renderDef.BoxHalfX != 0f ? renderDef.BoxHalfX : dto.Length / 2f;
                            float halfY = renderDef.BoxHalfY != 0f ? renderDef.BoxHalfY : dto.Width  / 2f;
                            footprint = new Map2DFootprint
                            {
                                LengthM = 2f * halfX,
                                WidthM  = 2f * halfY,
                                Shape   = GroundVehicle,
                            };
                            break;
                        }
                        case CollisionShapeKind.Capsule:
                        case CollisionShapeKind.Cylinder:
                        case CollisionShapeKind.Sphere:
                        {
                            float r = renderDef.ShapeRadius;
                            if (r == 0f && repo.HasComponent<PhysicsCollider>(entity))
                            {
                                ref readonly var col = ref repo.GetComponentRO<PhysicsCollider>(entity);
                                r = col.Radius;
                            }
                            if (r == 0f) r = 0.3f; // hard default matching StrideVisualBindingSystem
                            var cat = renderDef.ShapeKind == CollisionShapeKind.Capsule
                                ? Humanoid
                                : Unknown;
                            footprint = new Map2DFootprint
                            {
                                LengthM = 2f * r,
                                WidthM  = 2f * r,
                                Shape   = cat,
                            };
                            break;
                        }
                        default:
                        {
                            // None / MeshFromModel — use dto dims, Unknown shape.
                            footprint = new Map2DFootprint
                            {
                                LengthM = dto.Length,
                                WidthM  = dto.Width,
                                Shape   = Unknown,
                            };
                            break;
                        }
                    }
                }
                repo.AddComponent(entity, footprint);
            }
        }
    }
}
