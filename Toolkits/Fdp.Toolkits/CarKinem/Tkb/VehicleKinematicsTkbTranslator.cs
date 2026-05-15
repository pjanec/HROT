using System;
using System.Collections.Generic;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace CarKinem.Tkb
{
    /// <summary>
    /// Reference ITkbEntityTranslator implementation.
    /// Projects VehicleParametersDto into four ECS components:
    ///   VehicleParams, VehicleState, NavState, PhysicsCollider.
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

            if (repo.IsComponentTypeRegistered<VehicleParams>())
                repo.AddComponent(entity, new VehicleParams
                {
                    Length      = dto.Length,
                    Width       = dto.Width,
                    WheelBase   = dto.Length * 0.6f,
                    MaxSpeedFwd = dto.MaxSpeedFwd,
                    MaxAccel    = dto.MaxAccel
                });

            if (repo.IsComponentTypeRegistered<VehicleState>())
                repo.AddComponent(entity, new VehicleState { Speed = 0, SteerAngle = 0 });

            if (repo.IsComponentTypeRegistered<NavState>())
                repo.AddComponent(entity, new NavState { Mode = KinematicsMode.None });

            if (repo.IsComponentTypeRegistered<PhysicsCollider>())
                repo.AddComponent(entity, new PhysicsCollider
                {
                    Radius         = System.Math.Max(dto.Length, dto.Width) / 2f,
                    CollisionLayer = 1
                });
        }
    }
}
