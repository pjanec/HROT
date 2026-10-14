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
        /// <summary>⭐ Air Ops slice 2 — the route height where a waypoint states none, and the height a landing starts from.</summary>
        public float HoverHeightM;
        /// <summary>⭐ Air Ops slice 2 — the radius a route's corners are rounded to (0 ⇒ <see cref="TurnRadius"/> derives it).</summary>
        public float TurnRadiusM;
        /// <summary>⭐ Air Ops slice 4 — a plane's floor speed, route speed and steepest bank.</summary>
        public float StallSpeedMps;
        public float CruiseSpeedMps;
        public float MaxBankDeg;

        public static FlightParams From(FlightParamsDto d) => new()
        {
            MaxSpeedMps = d.MaxSpeedMps, ClimbMps = d.ClimbMps, TurnRateDeg = d.TurnRateDeg,
            EnduranceSeconds = d.EnduranceSeconds, ContactRadiusM = d.ContactRadiusM,
            HoverHeightM = d.HoverHeightM, TurnRadiusM = d.TurnRadiusM,
            StallSpeedMps = d.StallSpeedMps, CruiseSpeedMps = d.CruiseSpeedMps, MaxBankDeg = d.MaxBankDeg,
        };

        /// <summary>The radius a route is rounded to: <see cref="TurnRadiusM"/>; else a plane's cruise² / (g·tan max bank) (DESIGN_Air_Operations
        /// §6: 150 m/s at 60° ≈ 1.3 km); else top speed / turn rate (the tightest circle it flies at full speed).</summary>
        public readonly float TurnRadius
            => TurnRadiusM > 0f ? TurnRadiusM
             : MaxBankDeg > 0f && CruiseSpeedMps > 0f ? CruiseSpeedMps * CruiseSpeedMps / (9.81f * MathF.Tan(MaxBankDeg * MathF.PI / 180f))
             : TurnRateDeg > 0f ? MaxSpeedMps / (TurnRateDeg * MathF.PI / 180f) : 0f;
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
