using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Aggregate;
using Hrot.Core.Tkb;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Orders
{
    /// <summary>
    /// ⭐⭐ <c>X-2</c> (agg-behaviors; R-8353 … R-8363; 📄 docs/designs/aggregate-units/DESIGN_X2_Order_Reresolution.md §3) — the PURE half of the
    /// order re-resolution when a unit switches resolution: which play its order resolves to at a resolution (the ONE routing table, the same
    /// one a commander's <c>Order.&lt;K&gt;</c> goes through), the params the re-issued play gets (the split: as they are, + the planned posts for a
    /// <c>SquadDefend</c> — DP3-D ②; the merge: PROJECTED onto the stochastic play's DTO — R-8300 guarantees physical ⊇ neutral, never the
    /// reverse), and why an automatic switch must wait (X2-D, the manager's <c>SwitchBlocked</c> seam). ⭐ The reconciler that acts on it is
    /// <c>Hrot.CGF.Systems.UnitResolutionFitSystem</c>. Nothing here runs per frame in the steady state.
    /// </summary>
    public static class OrderReissue
    {
        /// <summary>⭐ X2-A — the play order kind <paramref name="kind"/> resolves to for <paramref name="unit"/> at <paramref name="resolution"/>
        /// (<see cref="ResolutionRoutingTable.Default"/> over the unit's classes; the physical fallback rows included — X2-C2); null = none.</summary>
        public static string? PlayFor(EntityRepository repo, Entity unit, AggregateResolution resolution, OrderKind kind, Func<string, bool> isBehaviour)
        {
            if (kind == 0) return null;
            var r = ResolutionRoutingTable.Default.Resolve(EntityClassTable.Default, repo.GetDisType(unit), resolution, kind.ToString(), isBehaviour);
            return r.Outcome == OrderResolution.Resolved ? r.Behaviour : null;
        }

        /// <summary>
        /// ⭐ X2-D (R-8357) — why an AUTOMATIC switch of <paramref name="unit"/> to <paramref name="target"/> must wait: its current order (running or
        /// paused, <see cref="CurrentOrders.Of"/>) has no kind (SV-10: an untyped order cannot switch), or its kind has no play at the target.
        /// Null = it may switch (no order, or the order has a play there). ⭐ Plugged into <c>ResolutionManagerSystem.SwitchBlocked</c> by the host.
        /// </summary>
        public static string? BlockReason(EntityRepository repo, Entity unit, AggregateResolution target, Func<string, bool> isBehaviour)
        {
            // X2-I (R-8362) — the scope the reconciler re-resolves: a squad. A platoon / company keeps today's behaviour (its split flattens to men, F-X2-1)
            if (!UnitSplits.RostersItsMen(repo, unit)) return null;   // ⭐ R-8372 — the echelon alone misreads an authored table (agg-merge-back)
            var order = CurrentOrders.Of(repo, unit);
            if (!order.Exists) return null;
            if (order.Kind == 0) return "its order has no kind (SV-10: an untyped order cannot switch)";
            return PlayFor(repo, unit, target, order.Kind, isBehaviour) == null ? "its " + order.Kind + " order has no " + target + " play" : null;
        }

        /// <summary>⭐ X2-C (R-8355) — the params the re-issued <paramref name="targetPlay"/> gets from the order's <paramref name="json"/>: to a
        /// stochastic play ⇒ <see cref="Project"/>ed; to a physical one ⇒ as they are (R-8300: every physical play of a kind reads its neutral DTO).</summary>
        public static string ParamsFor(string targetPlay, string json, bool toStochastic)
            => toStochastic ? Project(targetPlay, json) : (string.IsNullOrEmpty(json) ? "{}" : json);

        /// <summary>⭐ X2-C — <paramref name="json"/> read RELAXED into <paramref name="targetPlay"/>'s params DTO and written back: the fields the
        /// play does not know (a physical play's <c>Posts</c>, <c>MinRange</c> …) are dropped, so its strict read accepts it. A play with no
        /// declared DTO, or text that does not parse, gives <c>"{}"</c> (the play's authored defaults).</summary>
        public static string Project(string targetPlay, string json)
        {
            if (!ParamsDtoRegistry.Default.TryGetBehavior(targetPlay, out var decl) || decl.ParamsType == null) return "{}";
            try
            {
                var dto = JsonSerializer.Deserialize(string.IsNullOrEmpty(json) ? "{}" : json, decl.ParamsType, FdpJsonOptionsRegistry.DefaultRelaxed);
                return dto == null ? "{}" : JsonSerializer.Serialize(dto, decl.ParamsType, FdpJsonOptionsRegistry.DefaultRelaxed);
            }
            catch (JsonException) { return "{}"; }
        }

        /// <summary>
        /// ⭐⭐ X2-F (R-8359; DP3-D ②) — a <c>SquadDefend</c>'s params with the unit's PLANNED POSTS as D-2f <c>Posts</c>: one per posted entry of
        /// <paramref name="plan"/> (its point, its watch point — zero ⇒ toward the sector, its window bit), at most
        /// <see cref="SquadDefendPostJsonDto.MaxPosts"/>. The men take the nearest post and stand on it ⇒ Set at once, no ask. A plan with no
        /// posted entry leaves <paramref name="json"/> as it is.
        /// </summary>
        public static string WithPlannedPosts(string json, in PlannedPosts plan)
        {
            int n = Math.Min((int)plan.Count, PlannedPosts.Capacity), posted = 0;
            var posts = new SquadDefendPostJsonDto[Math.Min(n, SquadDefendPostJsonDto.MaxPosts)];
            for (int k = 0; k < n && posted < posts.Length; k++)
                if (plan.TryGet(k, out var p))
                    posts[posted++] = new SquadDefendPostJsonDto { Point = p.Point, WatchTowards = p.Watch, Window = p.Window };
            if (posted == 0) return string.IsNullOrEmpty(json) ? "{}" : json;
            Array.Resize(ref posts, posted);
            JsonObject obj;
            try { obj = JsonNode.Parse(string.IsNullOrEmpty(json) ? "{}" : json) as JsonObject ?? new JsonObject(); }
            catch (JsonException) { obj = new JsonObject(); }
            obj.Remove("Posts");
            obj["Posts"] = JsonSerializer.SerializeToNode(posts, FdpJsonOptionsRegistry.DefaultRelaxed);
            return obj.ToJsonString();
        }
    }
}
