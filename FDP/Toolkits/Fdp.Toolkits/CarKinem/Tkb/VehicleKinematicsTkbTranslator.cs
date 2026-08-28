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
                repo.AddComponent(entity, BuildVehicleParams(dto));

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

        /// <summary>
        /// Builds <see cref="VehicleParams"/> from the TKB descriptor: a
        /// <see cref="VehiclePresets"/> baseline chosen by
        /// <see cref="VehicleParametersDto.VehicleClass"/>, then overridden field by field
        /// with whatever the template authored.
        /// </summary>
        /// <remarks>
        /// A preset baseline is what makes the result driveable.  The kinematics fields
        /// that no TKB descriptor carries -- <c>MaxSteerAngle</c>, <c>AccelGain</c>,
        /// <c>MaxDecel</c>, <c>MaxLatAccel</c>, <c>AvoidanceRadius</c>, the lookahead
        /// window -- come from the preset, and a vehicle with <c>AccelGain = 0</c> and
        /// <c>MaxSteerAngle = 0</c> cannot accelerate or steer at all.  Writing only the
        /// authored fields left every one of those at zero.
        /// <para>
        /// Every override is guarded by <c>&gt; 0f</c>, so an unauthored (or, for a TKB
        /// zip predating a field, absent) value keeps the preset's -- see
        /// <see cref="VehicleParametersDto"/> on the two-producer hazard.
        /// </para>
        /// </remarks>
        internal static VehicleParams BuildVehicleParams(VehicleParametersDto dto)
        {
            var vehicleClass = dto.VehicleClass ?? VehicleClass.PersonalCar;

            var p = VehiclePresets.GetPreset(vehicleClass);
            p.Class = vehicleClass;

            if (dto.Length > 0f)
            {
                p.Length    = dto.Length;
                p.WheelBase = dto.Length * 0.6f;
            }
            if (dto.Width > 0f)
                p.Width = dto.Width;
            if (dto.MaxSpeedFwd > 0f)
                p.MaxSpeedFwd = dto.MaxSpeedFwd;
            if (dto.MaxSpeedRev > 0f)
                p.MaxSpeedRev = dto.MaxSpeedRev;
            if (dto.MaxAccel > 0f)
                p.MaxAccel = dto.MaxAccel;
            if (dto.TurnRate > 0f)
                p.MaxSteerRate = dto.TurnRate * (MathF.PI / 180f);

            return p;
        }
    }
}
