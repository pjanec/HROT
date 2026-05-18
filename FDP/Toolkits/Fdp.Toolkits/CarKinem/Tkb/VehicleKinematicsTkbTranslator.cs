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
