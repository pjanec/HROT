using System;
using System.Text.Json;
using Fdp.Kernel;
using Hrot.Map.Common.Components;
using FDP.Toolkit.Replication.Components;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// Static helpers for <c>MissionControlExecutionSystem</c> that require
    /// <c>System.Text.Json</c>. Keeping JSON deserialization in this separate file
    /// ensures <c>MissionControlExecutionSystem.cs</c> itself has zero
    /// <c>System.Text.Json</c> references (PACK-P001 constraint).
    /// </summary>
    internal static class MissionControlBehaviorParamsHelper
    {
        /// <summary>
        /// Attempts to rewrite <c>BehaviorParams</c> for a <c>FollowRoute</c> task by
        /// resolving the <c>routeEntityId</c> (network ID) to a compiled
        /// <see cref="RouteTrajectoryCache.TrajectoryId"/> (local ECS ID).
        ///
        /// Returns <c>true</c> when the rewrite succeeded or when the params do not contain
        /// a <c>routeEntityId</c> key (pass-through). Returns <c>false</c> when the route
        /// entity is not yet present or its trajectory has not been compiled.
        /// </summary>
        internal static bool TryTranslateFollowRouteBehaviorParams(
            EntityRepository repo,
            string? behaviorParams,
            out string translatedParams)
        {
            translatedParams = behaviorParams ?? string.Empty;

            if (string.IsNullOrWhiteSpace(behaviorParams))
                return true; // nothing to translate

            long routeEntityId;
            double speed = 0.0;
            bool loop   = false;

            try
            {
                using var doc  = JsonDocument.Parse(behaviorParams);
                var       root = doc.RootElement;

                if (!root.TryGetProperty("routeEntityId", out var routeEl))
                    return true; // not a network-ID-based FollowRoute task; pass through

                routeEntityId = routeEl.GetInt64();

                if (root.TryGetProperty("Speed", out var speedEl))
                    speedEl.TryGetDouble(out speed);

                if (root.TryGetProperty("Loop", out var loopEl))
                    loop = loopEl.GetBoolean();
            }
            catch
            {
                return true; // malformed JSON — let downstream handle it
            }

            // Find the route entity in ECS by NetworkIdentity.Value.
            var routeQuery = repo.Query()
                .With<NetworkIdentity>()
                .With<RouteTrajectoryCache>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            Entity found = Entity.Null;
            foreach (var e in routeQuery)
            {
                if (repo.GetComponent<NetworkIdentity>(e).Value == routeEntityId)
                {
                    found = e;
                    break;
                }
            }

            if (found == Entity.Null)
                return false; // entity not yet registered; retry

            var cache = repo.GetComponent<RouteTrajectoryCache>(found);
            if (cache.TrajectoryId == 0)
                return false; // route compiled but trajectory not yet ready; retry

            // Rewrite params with the resolved local trajectory ID.
            translatedParams =
                $"{{\"trajectoryId\":{cache.TrajectoryId}" +
                $",\"Speed\":{speed.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                $",\"Loop\":{(loop ? "true" : "false")}}}";
            return true;
        }
    }
}
