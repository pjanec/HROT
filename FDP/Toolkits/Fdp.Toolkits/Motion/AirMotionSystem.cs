using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Motion
{
    /// <summary>
    /// ⭐ CE-2168 (Motion Domains MD-B, MD-C, MD-D) — moves every AIR body this node owns the position of: its <see cref="Locomotion"/>
    /// class picks the model (<see cref="MotionModelTable"/>), the body's <see cref="NavigationIntent"/> drives it — <c>Mode = Manual</c>
    /// with a <see cref="ManualDrive"/> through <see cref="IMotionModel.Manual"/>, anything else through
    /// <see cref="IMotionModel.Autonomous"/>. A ground body is not touched (no air class), and <c>LinearKinematicsSystem</c> skips
    /// every body with a <see cref="Locomotion"/>, so nothing is integrated twice.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class AirMotionSystem : IEcsModuleSystem
    {
        /// <summary>⭐ Air Ops slice 2 — the routes a <c>FollowRoute</c> intent names (<see cref="NavigationIntent.RouteEntityId"/>); the
        /// host supplies it (Hrot.Core's route source). Null ⇒ a route order hovers, awaiting a path.</summary>
        public IRouteSource? RouteSource { get; init; }

        private readonly System.Collections.Generic.List<NavRouteWaypoint> _waypoints = new();

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<Locomotion>()) return;
            bool intents = repo.IsComponentTypeRegistered<NavigationIntent>();
            bool drives = repo.IsComponentTypeRegistered<ManualDrive>();
            var terrain = Fdp.Toolkit.World.WorldQuery.Of(repo);
            var query = repo.Query().With<Locomotion>().With<SimTransform>().WithOwned<SimTransform>().With<SimVelocity>().Build();
            foreach (var e in query) Step(repo, e, intents, drives, terrain, deltaTime);
            // ⭐ airborne insertion B-1/B-2 (D-1, D-2) — a man coming down a rope or under a canopy: his Descent makes his class Rope /
            //   Parachute (Locomotions.Of reads it first); a man whose type states no Gen.Locomotion is moved here too
            if (repo.IsComponentTypeRegistered<Descent>())
                foreach (var e in repo.Query().With<Descent>().With<SimTransform>().WithOwned<SimTransform>().With<SimVelocity>().Without<Locomotion>().Build())
                    Step(repo, e, intents, drives, terrain, deltaTime);
        }

        /// <summary>
        /// ⭐ CE-3293 S5 — compiles every owned air body's route path WITHOUT moving it: the paused-time half of this system
        /// (<see cref="AirRouteCompileSystem"/>), so a route edited while time stands still is flown as drawn. The same compile the
        /// step runs (<see cref="EnsurePath"/>) — one implementation.
        /// </summary>
        internal void CompilePaths(EntityRepository repo)
        {
            if (!repo.IsComponentTypeRegistered<Locomotion>() || !repo.IsComponentTypeRegistered<NavigationIntent>()) return;
            var terrain = Fdp.Toolkit.World.WorldQuery.Of(repo);
            foreach (var e in repo.Query().With<Locomotion>().With<SimTransform>().WithOwned<SimTransform>().With<NavigationIntent>().Build())
            {
                var cls = Locomotions.Of(repo, e);
                if (Locomotions.DomainOf(cls) != MotionDomain.Air) continue;
                var intent = repo.GetComponentRO<NavigationIntent>(e);
                if (intent.Mode == NavigationMode.FollowRoute) EnsurePath(repo, e, in intent, terrain, cls);
            }
        }

        private void Step(EntityRepository repo, Entity e, bool intents, bool drives, Fdp.Toolkit.World.IWorldQuery? terrain, float deltaTime)
        {
            var cls = Locomotions.Of(repo, e);
            if (Locomotions.DomainOf(cls) != MotionDomain.Air) return;
            var model = MotionModelTable.Model(cls);
            if (model == null) return;

            var intent = intents && repo.HasComponent<NavigationIntent>(e) ? repo.GetComponentRO<NavigationIntent>(e) : default;
            if (intent.Mode == NavigationMode.FollowRoute) EnsurePath(repo, e, in intent, terrain, cls);
            var frame = new MotionFrame(repo, e, ref repo.GetComponentRW<SimTransform>(e), ref repo.GetComponentRW<SimVelocity>(e)) { Terrain = terrain };
            if (intent.Mode == NavigationMode.Manual && drives && repo.HasComponent<ManualDrive>(e))
                model.Manual(ref frame, in repo.GetComponentRO<ManualDrive>(e), deltaTime);
            else
                model.Autonomous(ref frame, in intent, deltaTime);
        }

        /// <summary>⭐ Air Ops slice 2 — (re)compiles the body's <see cref="AirPath"/> when the order, the route or its version changed
        /// (<see cref="AirRoute.Compile"/>, from where the body is); keeps it while they hold.</summary>
        private void EnsurePath(EntityRepository repo, Entity e, in NavigationIntent intent, Fdp.Toolkit.World.IWorldQuery? terrain,
                                LocomotionClass cls)
        {
            if (RouteSource == null || intent.RouteEntityId == 0) return;
            if (!RouteSource.TryGetRoute(repo, intent.RouteEntityId, _waypoints, out bool loop, out int version)) return;
            bool table = repo.TryGetTable(typeof(AirPath), out _);
            if (table && repo.HasManagedComponent<AirPath>(e))
            {
                var have = ((ISimulationView)repo).GetManagedComponentRO<AirPath>(e);
                if (have.IntentId == intent.IntentId && have.RouteEntityId == intent.RouteEntityId && have.RouteVersion == version) return;
            }
            if (!table) repo.RegisterManagedComponent<AirPath>();
            // ⭐ Air Ops slice 4 (B9) — planes do not land or hover in v1: a Ground waypoint is flown over, a hold is ignored (warned)
            if (cls == LocomotionClass.FixedWing)
                for (int i = 0; i < _waypoints.Count; i++)
                {
                    var w = _waypoints[i];
                    if (!AirRoute.IsStop(in w)) continue;
                    Fdp.Core.Logging.FdpLog<AirMotionSystem>.Warn(
                        "[AIR] route {0} waypoint {1}: a plane does not land or hold (v1) — flown over", intent.RouteEntityId, i);
                    _waypoints[i] = w with { Kind = RouteWaypointKind.Pass, Hold = RouteHold.None };
                }
            var p = repo.IsComponentTypeRegistered<FlightParams>() && repo.HasComponent<FlightParams>(e)
                ? repo.GetComponentRO<FlightParams>(e) : FlightParams.From(new FlightParamsDto());
            if (cls == LocomotionClass.Rotorcraft) AirborneHolds(repo, e, in intent, terrain, p.HoverHeightM);
            var path = AirRoute.Compile(repo.GetComponentRO<SimTransform>(e).Position, _waypoints, loop, p.TurnRadius, p.HoverHeightM,
                terrain == null ? null : terrain.GroundHeightAt);
            path.IntentId = intent.IntentId;
            path.Dropping = false;
            path.RouteEntityId = intent.RouteEntityId;
            path.RouteVersion = version;
            repo.SetManagedComponent(e, path);
        }

        /// <summary>The height (m) a helicopter may hover at and still let men step down — CE-1084's drawn hop (D-8).</summary>
        public const float StepDownMaxM = 3f;
        /// <summary>A <see cref="RouteHold.Rope"/> waypoint that states no <c>HeightAgl</c> hovers this high over the roof (design §2: 10–20 m).</summary>
        public const float DefaultRopeHoverM = 12f;

        /// <summary>
        /// ⭐ Airborne insertion B-3 (D-7, D-8) — a helicopter route's airborne holds, settled before the path is compiled:
        /// <list type="bullet">
        ///   <item>a <see cref="RouteHold.Rope"/> hover is put at its height above the HIGHEST surface under the waypoint (a roof —
        ///     <c>AirRoute</c> measures <c>HeightAgl</c> from the ground, so a 12 m hover over a 12 m building would sit ON it), and is
        ///     REFUSED (warned, flown through) when that height is more than the carrier's rope;</item>
        ///   <item>an <see cref="RouteHold.Unload"/> / <see cref="RouteHold.Pickup"/> hover more than <see cref="StepDownMaxM"/> above the
        ///     ground is REFUSED as a hover (warned) and LANDS instead — men no longer teleport down (or up) from a 30 m hover.</item>
        /// </list>
        /// </summary>
        private void AirborneHolds(EntityRepository repo, Entity e, in NavigationIntent intent, Fdp.Toolkit.World.IWorldQuery? terrain, float hoverAgl)
        {
            for (int i = 0; i < _waypoints.Count; i++)
            {
                var w = _waypoints[i];
                if (w.Kind == RouteWaypointKind.Ground) continue;
                float ground = terrain?.GroundHeightAt(w.Position.X, w.Position.Y) ?? 0f;
                if (w.Hold == RouteHold.Rope)
                {
                    float roof = terrain != null ? DescentRules.HighestSurface(terrain, w.Position.X, w.Position.Y) : ground;
                    float h = w.HeightAgl > 0f ? w.HeightAgl : DefaultRopeHoverM;
                    float rope = DescentRules.RopeLength(repo, e);
                    if (h > rope)
                    {
                        Fdp.Core.Logging.FdpLog<AirMotionSystem>.Warn(
                            "[AIR] route {0} waypoint {1}: a {2:0.#} m rope hover on a {3:0.#} m rope — refused, flown through", intent.RouteEntityId, i, h, rope);
                        _waypoints[i] = w with { Hold = RouteHold.None };
                        continue;
                    }
                    _waypoints[i] = w with { HeightAgl = roof - ground + h };   // HeightAgl counts from the ground: put the hover over the roof
                }
                else if (w.Hold is RouteHold.Unload or RouteHold.Pickup)
                {
                    float agl = w.HeightAgl > 0f ? w.HeightAgl : MathF.Max(w.Position.Z - ground, hoverAgl);
                    if (agl <= StepDownMaxM) continue;
                    Fdp.Core.Logging.FdpLog<AirMotionSystem>.Warn(
                        "[AIR] route {0} waypoint {1}: a {2} hold hovering {3:0.#} m up — nobody steps down that far; it lands instead",
                        intent.RouteEntityId, i, w.Hold, agl);
                    _waypoints[i] = w with { Kind = RouteWaypointKind.Ground };
                }
            }
        }
    }

    /// <summary>
    /// ⭐ CE-3293 S5 (R-3036 §1b; docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md §8, approved "split") — the paused-time half
    /// of <see cref="AirMotionSystem"/>: on a frame that does not advance, an air body's route path is (re)compiled when its order or
    /// its route changed, so the path the map draws is the one it will fly. On a frame that advances the motion system compiles it.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class AirRouteCompileSystem : IEcsModuleSystem
    {
        private readonly AirMotionSystem _motion;

        public AirRouteCompileSystem(AirMotionSystem motion) => _motion = motion ?? throw new ArgumentNullException(nameof(motion));

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (deltaTime > 0f || view is not EntityRepository repo) return;   // advancing: the motion system compiles
            _motion.CompilePaths(repo);
        }
    }

    /// <summary>
    /// ⭐ CE-2168 (MD-B) — the AIR domain's module: the systems that move air bodies (<see cref="AirMotionSystem"/>) and then what happens
    /// when one meets something (the drone's impact, M7). Held beside <c>GroundKinematicsModule</c> by the SimHost and the editor's
    /// Muscle (<c>SimHostCoreLogicPack</c>). ⚠ The per-domain OWNERSHIP role (<c>MuscleAir</c>, MD-B) is not built: the ownership table
    /// allows a component in one group only (CE-3199) — until that is decided, an air body's position stays with its creator.
    /// </summary>
    public sealed class AirMotionModule
    {
        public AirMotionModule(params IEcsModuleSystem[] after) : this(null, after) { }

        /// <summary>⭐ Air Ops slice 2 — with the routes a <c>FollowRoute</c> names (<see cref="AirMotionSystem.RouteSource"/>).</summary>
        public AirMotionModule(IRouteSource? routes, params IEcsModuleSystem[] after)
        {
            var motion = new AirMotionSystem { RouteSource = routes };
            var list = new System.Collections.Generic.List<IEcsModuleSystem>
            {
                new AirRouteCompileSystem(motion),   // ⭐ CE-3293 S5 — a route edited while paused is compiled at once
                motion,
                new DescentLandingSystem(),
            };
            if (after != null) list.AddRange(after);
            PostSimulationSystems = list;
        }

        /// <summary>Systems that run in the PostSimulation phase, in order.</summary>
        public System.Collections.Generic.IReadOnlyList<IEcsModuleSystem> PostSimulationSystems { get; }
    }
}
