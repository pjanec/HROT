using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;

namespace Hrot.Editor.DebugApi
{
    public sealed partial class DebugApiService
    {
        /// <summary>
        /// ⭐ <c>CE-2298</c> (LD-5; DA-26) — <c>POST /entities/{networkId}/sector</c>: the operator sets a man's or a commander's SECTOR — the
        /// ROE-twin <see cref="SetEngagementAreaEvent"/>, origin Operator, applied through <see cref="EngagementAreaSystem.Apply"/> (the one
        /// writer). Body (all optional): <c>left</c> / <c>right</c> (compass bearings of the arc, 0 = north, clockwise), <c>minRange</c>,
        /// <c>maxRange</c>, <c>watch</c> (where he looks, compass), <c>allowArea</c> (an area's network id), <c>noFire</c> (up to four
        /// area network ids). The arc's origin is where he stands now. An empty body withdraws the sector. A unit whose brain runs on
        /// another node gets it there (backend's tactical-intent kind 4, <c>CE-3261</c>).
        /// </summary>
        public (JsonNode? result, int status, string? error) PostEntitySector(long networkId, JsonNode? body)
        {
            if (!_entityMap.TryGetEntity(networkId, out var unit) || !_world.IsAlive(unit))
                return (null, 404, $"Entity {networkId} not found.");
            if (!_world.IsComponentTypeRegistered<EngagementArea>())
                return (null, 409, "This node has no sectors (EngagementArea is not registered — not a brain host).");
            var evt = new SetEngagementAreaEvent { Entity = unit, Origin = BehaviorOrigin.Operator };
            if (body?["left"] != null || body?["right"] != null)
            {
                evt.ArcSet = 1;
                evt.ArcOrigin = _world.HasComponent<SimTransform>(unit) ? _world.GetComponentRO<SimTransform>(unit).Position : Vector3.Zero;
                evt.LeftBearing = (float)Num(body?["left"], 0);
                evt.RightBearing = (float)Num(body?["right"], 360);
                evt.MinRange = (float)Num(body?["minRange"], 0);
                evt.MaxRange = (float)Num(body?["maxRange"], 0);
            }
            if (body?["watch"] != null)
            {
                evt.WatchSet = 1;
                evt.WatchBearing = (float)Num(body?["watch"], 0);
            }
            evt.AllowArea = (long)Num(body?["allowArea"], 0);
            if (body?["noFire"] is JsonArray noFire)
                foreach (var id in noFire)
                    if (evt.NoFire.Count < EngagementArea.MaxNoFire && id is JsonValue v && v.TryGetValue<long>(out long areaId))
                        evt.NoFire.Items[evt.NoFire.Count++] = areaId;

            // ⭐ CE-3261 (backend, kind 4) — a unit whose brain runs on another node: the event goes there and its EngagementAreaSystem
            //   applies it (the replicated EngagementArea reads it back here a few frames later)
            if (_world.IsComponentTypeRegistered<BehaviorState>() && _world.HasComponent<BehaviorState>(unit) && !_world.HasAuthority<BehaviorState>(unit))
            {
                if (!_world.Bus.IsRegistered<SetEngagementAreaEvent>())
                    return (null, 409, "The unit's brain runs on another node and this node cannot forward a sector.");
                _world.Bus.Publish(evt);
                return (new JsonObject { ["networkId"] = networkId, ["forwarded"] = true, ["note"] = "applied by the brain's owner; read it back with GET /entities/{id}" }, 200, null);
            }
            if (!EngagementAreaSystem.Apply(_world, evt))
                return (null, 409, "Refused: the sector was set by a higher origin.");
            var a = _world.GetComponentRO<EngagementArea>(unit);
            return (new JsonObject
            {
                ["networkId"] = networkId,
                ["arc"] = a.ArcSet != 0 ? new JsonObject { ["left"] = a.LeftBearing, ["right"] = a.RightBearing, ["minRange"] = a.MinRange, ["maxRange"] = a.MaxRange } : null,
                ["watch"] = a.WatchSet != 0 ? a.WatchBearing : null,
                ["allowArea"] = a.AllowArea != 0 ? a.AllowArea : null,
                ["noFire"] = a.NoFire.Count,
                ["setBy"] = a.SetBy.ToString(),
            }, 200, null);
        }
    }
}
