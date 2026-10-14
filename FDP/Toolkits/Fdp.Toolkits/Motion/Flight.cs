using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Motion
{
    /// <summary>⭐ M7 (DESIGN_FPV_Drone §2) — <see cref="FlightParamsDto"/> on the entity, every node.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.FlightParams)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct FlightParams
    {
        public float MaxSpeedMps;
        public float ClimbMps;
        public float TurnRateDeg;
        public float EnduranceSeconds;
        public float ContactRadiusM;

        public static FlightParams From(FlightParamsDto d) => new()
        {
            MaxSpeedMps = d.MaxSpeedMps, ClimbMps = d.ClimbMps, TurnRateDeg = d.TurnRateDeg,
            EnduranceSeconds = d.EnduranceSeconds, ContactRadiusM = d.ContactRadiusM,
        };
    }

    /// <summary>⭐ M7 — what the flyer is doing, written by its motion model (<see cref="RotorcraftModel"/>) on the Muscle.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.FlightState)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct FlightState
    {
        /// <summary>1 while it flies; 0 once a dead flyer has fallen to rest.</summary>
        public byte Airborne;
        /// <summary>m/s, + up.</summary>
        public float VerticalSpeed;
    }

    /// <summary>
    /// ⭐ M7 — projects <c>Gen.Flight</c>: <see cref="FlightParams"/> + <see cref="FlightState"/> (airborne), and the navigation
    /// contract a behaviour's <c>MoveTo</c> writes and reads (<see cref="NavigationIntent"/>, <see cref="NavigationStatus"/>), which the
    /// ground's translator gives only a type with ground physics.
    /// </summary>
    public sealed class FlightTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors() { yield return typeof(FlightParamsDto); }

        public IEnumerable<Type> GetProducedComponents()
        {
            yield return typeof(FlightParams);
            yield return typeof(FlightState);
            yield return typeof(NavigationIntent);
            yield return typeof(NavigationStatus);
            yield return typeof(Fdp.Toolkit.Physics.Components.PhysicsCollider);
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            var dto = template.GetDescriptor<FlightParamsDto>();
            if (dto == null) return;
            Ensure(repo, entity, FlightParams.From(dto), overwrite: true);
            Ensure(repo, entity, new FlightState { Airborne = 1 }, overwrite: false);
            Ensure(repo, entity, default(NavigationIntent), overwrite: false);
            Ensure(repo, entity, default(NavigationStatus), overwrite: false);
            // its body: what a round, a fragment or another flyer meets (no ground physics gives it one)
            Ensure(repo, entity, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = dto.BodyRadiusM, Height = dto.BodyHeightM }, overwrite: false);
        }

        private static void Ensure<T>(EntityRepository repo, Entity e, T value, bool overwrite) where T : unmanaged
        {
            if (!repo.IsComponentTypeRegistered<T>()) return;
            if (!repo.HasComponent<T>(e)) repo.AddComponent(e, value);
            else if (overwrite) repo.SetComponent(e, value);
        }
    }
}
