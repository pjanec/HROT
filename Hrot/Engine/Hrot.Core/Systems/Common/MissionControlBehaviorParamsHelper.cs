using System.Text.Json;
using Fdp.Kernel;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common.Components;

namespace Hrot.Common.Systems
{
    /// <summary>
    /// JSON-dependent helpers for mission execution.
    /// </summary>
    internal static class MissionControlBehaviorParamsHelper
    {
        internal static bool TryTranslateFollowRouteBehaviorParams(
            EntityRepository repo,
            string? behaviorParams,
            out string translatedParams)
        {
            translatedParams = behaviorParams ?? string.Empty;

            if (string.IsNullOrWhiteSpace(behaviorParams))
                return true;

            long routeEntityId;
            double speed = 0.0;
            bool loop = false;

            try
            {
                using var doc = JsonDocument.Parse(behaviorParams);
                var root = doc.RootElement;

                if (!root.TryGetProperty("routeEntityId", out var routeEl))
                    return true;

                routeEntityId = routeEl.GetInt64();

                if (root.TryGetProperty("Speed", out var speedEl))
                    speedEl.TryGetDouble(out speed);

                if (root.TryGetProperty("Loop", out var loopEl))
                    loop = loopEl.GetBoolean();
            }
            catch
            {
                return true;
            }

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
                return false;

            var cache = repo.GetComponent<RouteTrajectoryCache>(found);
            if (cache.TrajectoryId == 0)
                return false;

            translatedParams =
                $"{{\"trajectoryId\":{cache.TrajectoryId}" +
                $",\"Speed\":{speed.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                $",\"Loop\":{(loop ? "true" : "false")}}}";
            return true;
        }
    }
}
