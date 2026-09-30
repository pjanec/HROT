using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Services;
using Hrot.AI.Behaviors.Brains;
using Hrot.Core.Mission;
using Hrot.Editor.AiShared;

namespace Hrot.AI.Behaviors.StandardLibrary
{
    /// <summary>
    /// ⭐ <c>CE-469</c> — the built-in blueprint functions: generic engine access, surfaced in the palette, used by any
    /// doctrine. 🔒 User <c>2026-09-30</c> (<c>R-156</c>): <i>"Non-generic c# helpers are a band aid and last resort"</i> —
    /// ⇒ everything here is domain-neutral; doctrine logic belongs in nodes and blueprint functions.
    /// </summary>
    /// <remarks>
    /// <para>Every function ends with the P7 trailing context (<c>ISimulationView view</c>, and <c>Entity self</c> where it
    /// needs one) — Stage5 appends it, so it never shows as a pin. It works in every dispatch kind: an AiPrimitive passes its
    /// <c>EntityRepository</c>, a Behavior/Instance its <c>ISimulationView</c> (the same object at runtime).</para>
    /// <para>⚠ Singletons (geo transform, network map) are not on <c>ISimulationView</c>; those two downcast to
    /// <see cref="EntityRepository"/> and return a neutral value otherwise — the design of <c>DESIGN_Resolver_World_Reach</c> §6.</para>
    /// 📄 <c>docs/blueprints/Architect_Question_78_Hill_Attack_The_Blueprint_Node_Way.md</c> §6.2, §7 row 4.
    /// </remarks>
    public static class BlueprintWorldLibrary
    {
        // ── World ──────────────────────────────────────────────────────────────────────────────

        /// <summary>True while <paramref name="entity"/> exists in the ECS (its handle is valid). ⚠ A knocked-out unit still
        /// exists — use <see cref="IsAliveInCombat"/> for "still in the fight".</summary>
        [BlueprintCallable("World", DisplayName = "Entity Exists")]
        public static bool EntityExists(Entity entity, ISimulationView view) => view.IsAlive(entity);

        /// <summary>True while <paramref name="entity"/> exists and, if it has <c>Health</c>, its HP is above zero
        /// (<see cref="CombatLife.IsAlive"/> — the engine's combat-death rule since the <c>CE-267</c> revert).</summary>
        [BlueprintCallable("World", DisplayName = "Is Alive In Combat")]
        public static bool IsAliveInCombat(Entity entity, ISimulationView view) => CombatLife.IsAlive(view, entity);

        /// <summary>The entity a network id refers to on this node, or <see cref="Entity.Null"/> when it is not (yet) known.</summary>
        [BlueprintCallable("World", DisplayName = "Entity From Network Id")]
        public static Entity EntityFromNetworkId(long networkId, ISimulationView view)
        {
            if (view is not EntityRepository world || !world.HasSingletonManaged<NetworkEntityMap>())
                return Entity.Null;
            var map = world.GetSingletonManaged<NetworkEntityMap>();
            return map != null && map.TryGetEntity(networkId, out var e) ? e : Entity.Null;
        }

        // ── Random (deterministic) ──────────────────────────────────────────────────────────────

        /// <summary>
        /// A deterministic pseudo-random integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>),
        /// seeded from <paramref name="self"/>, <paramref name="salt"/> and sim time (<see cref="SimRng.FromSim"/>) — the
        /// same run replays the same numbers. ⚠ Stateless: two calls with the same salt in one tick return the same value;
        /// the salt pin is how an author tells them apart.
        /// </summary>
        [BlueprintCallable("Random", DisplayName = "Random Int")]
        public static int RandomInt(int minInclusive, int maxExclusive, int salt, Entity self, ISimulationView view)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            var rng = SimRng.FromSim(self.Index, salt, view.Time);
            return rng.NextInt(minInclusive, maxExclusive);
        }

        /// <summary>A deterministic pseudo-random float in [<paramref name="min"/>, <paramref name="max"/>) — see <see cref="RandomInt"/>.</summary>
        [BlueprintCallable("Random", DisplayName = "Random Float")]
        public static float RandomFloat(float min, float max, int salt, Entity self, ISimulationView view)
        {
            var rng = SimRng.FromSim(self.Index, salt, view.Time);
            return min + (max - min) * rng.NextSingle();
        }

        /// <summary>
        /// ⭐ <c>CE-464</c> — a deterministic pseudo-random integer seeded by an explicit <paramref name="seed"/> (e.g. another
        /// entity's index) rather than by self — the C# hill attack draws a slot per SUBORDINATE, so the graph must too.
        /// Same <c>SimRng.FromSim(seed, salt, time)</c> stream as <see cref="RandomInt"/>.
        /// </summary>
        [BlueprintCallable("Random", DisplayName = "Random Int (Seeded)")]
        public static int RandomIntSeeded(int seed, int salt, int minInclusive, int maxExclusive, ISimulationView view)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            var rng = SimRng.FromSim(seed, salt, view.Time);
            return rng.NextInt(minInclusive, maxExclusive);
        }

        /// <summary>⭐ <c>CE-464</c> — an entity's index (stable while it lives): a seed, a parity key.</summary>
        [BlueprintCallable("World", DisplayName = "Entity Index")]
        public static int EntityIndex(Entity entity) => entity.Index;

        /// <summary>⭐ <c>CE-464</c> — the id a behaviour name registers under (<c>BehaviorState.ActiveBehaviorHash</c>).</summary>
        [BlueprintCallable("Behavior", DisplayName = "Behavior Hash")]
        public static int BehaviorHashOf(string behaviorName) => Fdp.Toolkit.Behavior.BehaviorHash.FromName(behaviorName);

        // ── Geo ──────────────────────────────────────────────────────────────────────────────────

        /// <summary>A geodetic point (lat/lon degrees, altitude metres) to local Cartesian metres, through the world's
        /// <see cref="IGeographicTransform"/>. Without one, returns <see cref="Vector3.Zero"/>.</summary>
        [BlueprintCallable("Geo", DisplayName = "Geo To Cartesian")]
        public static Vector3 GeoToCartesian(GeoPoint point, ISimulationView view)
        {
            var geo = Transform(view);
            return geo == null ? Vector3.Zero : geo.ToCartesian(point.Latitude, point.Longitude, point.Altitude);
        }

        /// <summary>Local Cartesian metres to a geodetic point — the inverse of <see cref="GeoToCartesian"/>. Without a
        /// transform, returns the default point.</summary>
        [BlueprintCallable("Geo", DisplayName = "Cartesian To Geo")]
        public static GeoPoint CartesianToGeo(Vector3 position, ISimulationView view)
        {
            var geo = Transform(view);
            if (geo == null) return default;
            var (lat, lon, alt) = geo.ToGeodetic(position);
            return new GeoPoint(lat, lon, alt);
        }

        /// <summary>⭐ <c>CE-464</c> — true when this world has an <see cref="IGeographicTransform"/> (offline/test worlds may not).</summary>
        [BlueprintCallable("Geo", DisplayName = "Has Geographic Transform")]
        public static bool HasGeographicTransform(ISimulationView view) => Transform(view) != null;

        /// <summary>⭐ <c>CE-464</c> — latitude/longitude (degrees) at altitude 0 to local Cartesian metres; zero without a transform.</summary>
        [BlueprintCallable("Geo", DisplayName = "Lat/Lon To Cartesian")]
        public static Vector3 LatLonToCartesian(double latitude, double longitude, ISimulationView view)
        {
            var geo = Transform(view);
            return geo == null ? Vector3.Zero : geo.ToCartesian(latitude, longitude, 0.0);
        }

        private static IGeographicTransform? Transform(ISimulationView view)
            => view is EntityRepository world && world.HasSingletonManaged<IGeographicTransform>()
                ? world.GetSingletonManaged<IGeographicTransform>()
                : null;
    }
}
