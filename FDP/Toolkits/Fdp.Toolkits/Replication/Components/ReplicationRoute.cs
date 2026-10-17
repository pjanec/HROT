using Fdp.Core;

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>
    /// ⭐ AQ93 EM-4 (R-4045) — an entity that is OWNED here but published by a mechanism OTHER than the node's default network
    /// replication (NED). Ownership, the network id, the entity map, hits, lifecycle and saving stay the ordinary ones; only
    /// the PUBLISHING differs, so every default egress skips it.
    /// <para>⭐ Absent ⇒ the default route. A normal entity carries nothing, and the default egress pays nothing: it excludes
    /// routed entities in its QUERY (<c>Without&lt;ReplicationRoute&gt;</c>), a bitmask test in the same step as its include
    /// test (<c>EntityQuery.cs:140-141</c>).</para>
    /// <para>The first user: SUMO traffic, whose cars travel on SumoSharp's own topics (R-4020). 📄
    /// docs/blueprints/Architect_Question_93_Sumo_Traffic_Node.md §10 EM-4.</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.ReplicationRoute)]
    // NoScenario: a route is re-stamped by whoever creates the entity. ⛔ NOT NoReplay: a replayed copy must still be skipped by
    // the default egress.
    [DataPolicy(DataPolicy.NoScenario)]
    public struct ReplicationRoute
    {
        /// <summary>The SumoSharp traffic transport (AQ93 R-4020).</summary>
        public const byte SumoSharp = 1;

        /// <summary>Which publishing mechanism carries this entity (never 0 — absent is the default route).</summary>
        public byte RouteId;

        public ReplicationRoute(byte routeId) { RouteId = routeId; }
    }
}

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>⭐ AQ93 EM-4 (R-4045) — the ONE place the default egress's "who do I publish" rule lives.</summary>
    public static class ReplicationRouteQueries
    {
        /// <summary>
        /// Entities the DEFAULT network replication publishes: they have a <see cref="NetworkIdentity"/> and carry no
        /// <see cref="ReplicationRoute"/>. ⚠ Authority is still the caller's per-entity check (<c>HasAuthority</c>); this only
        /// removes the entities another mechanism publishes. Every default (NED) egress query starts here.
        /// </summary>
        public static QueryBuilder DefaultRoutePublishable(this QueryBuilder query)
            => query.With<NetworkIdentity>().Without<ReplicationRoute>();
    }
}
