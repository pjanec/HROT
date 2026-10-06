using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Sensors;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Squad.DangerArea;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐ <c>CE-3072</c> B5 — <c>GET /entities/{networkId}/sensors</c>: every sensor child of a unit, its kind and result
    /// FAMILY, and its last answer in that family's own shape (a ranked list, or areas along a route). 📄
    /// <c>docs/DESIGN_Utility_AI_Demo_Scenarios.md</c> §10.3 (the check reads it), §10.6.
    /// <para>Read it on the BRAIN perspective (<c>Scenario</c> on a cluster) — the answers land there.</para>
    /// </summary>
    public sealed partial class DebugApiService
    {
        /// <summary>How many entries of a ranked answer are listed.</summary>
        private const int SensorRankedListed = 5;

        /// <summary><c>GET /entities/{networkId}/sensors</c>.</summary>
        public JsonNode GetEntitySensors(long networkId)
        {
            if (!_entityMap.TryGetEntity(networkId, out var unit) || !_world.IsAlive(unit))
                return new JsonObject { ["error"] = $"Entity {networkId} not found." };

            var sensors = new JsonArray();
            if (_world.IsComponentTypeRegistered<EqsSensor>() && _world.IsComponentTypeRegistered<PartMetadata>())
            {
                foreach (var child in _world.Query().With<EqsSensor>().With<PartMetadata>().Build())
                {
                    ref readonly var meta = ref _world.GetComponentRO<PartMetadata>(child);
                    if (!meta.ParentEntity.Equals(unit)) continue;
                    sensors.Add(DescribeSensor(child, meta.InstanceId));
                }
            }

            return new JsonObject
            {
                ["networkId"] = networkId,
                ["count"]     = sensors.Count,
                ["sensors"]   = sensors,
            };
        }

        private JsonObject DescribeSensor(Entity child, int partId)
        {
            ref readonly var s = ref _world.GetComponentRO<EqsSensor>(child);
            bool tagged = _world.IsComponentTypeRegistered<SensorTag>() && _world.HasComponent<SensorTag>(child);
            var kind = tagged ? _world.GetComponentRO<SensorTag>(child).Kind : SensorKindRegistry.EqsQuery;
            var family = SensorKindRegistry.FamilyOf(kind);
            var node = new JsonObject
            {
                ["partId"]     = partId,
                ["kind"]       = tagged ? kind.ToString() : "EqsQuery",
                ["family"]     = family.ToString(),
                ["templateId"] = $"0x{s.BlueprintId:X8}",
                ["epoch"]      = s.Epoch,
                ["suspended"]  = s.Suspended,
                ["origin"]     = tagged && _world.GetComponentRO<SensorTag>(child).FromTkb == 1 ? "tkb"
                               : _world.IsComponentTypeRegistered<BehaviorOwnedPart>() && _world.HasComponent<BehaviorOwnedPart>(child) ? "behaviour"
                               : "other",
            };

            if (_world.IsComponentTypeRegistered<DangerAreaCognitiveBuffer>() && _world.HasComponent<DangerAreaCognitiveBuffer>(child))
            {
                ref readonly var b = ref _world.GetComponentRO<DangerAreaCognitiveBuffer>(child);
                var areas = new JsonArray();
                var span = b.GetSpanRO();
                for (int i = 0; i < b.Count && i < span.Length; i++)
                {
                    var d = span[i];
                    areas.Add(new JsonObject
                    {
                        ["featureId"]          = d.FeatureId,
                        ["kind"]               = d.Kind.ToString(),
                        ["threat"]             = Round(d.ThreatRating),
                        ["distanceAlongRoute"] = Round(d.DistanceAlongRoute),
                        ["center"]             = Vec(d.Center),
                        ["nearHandle"]         = Vec(d.NearSideHandle),
                        ["farHandle"]          = Vec(d.FarSideHandle),
                    });
                }
                node["answer"] = new JsonObject
                {
                    ["ready"] = b.IsReady, ["count"] = b.Count, ["lastUpdateTick"] = b.LastUpdateTick, ["areas"] = areas,
                };
                if (_world.IsComponentTypeRegistered<DangerAreaSensor>() && _world.HasComponent<DangerAreaSensor>(child))
                {
                    var st = _world.GetComponentRO<DangerAreaSensor>(child).Settings;
                    node["settings"] = new JsonObject
                    {
                        ["routeSource"]       = st.RouteSource.ToString(),
                        ["routePoint"]        = Vec(st.RoutePoint),
                        ["routeHandle"]       = st.RouteHandle,
                        ["corridorHalfWidth"] = Round(st.CorridorHalfWidth),
                        ["maxAreas"]          = st.MaxAreas,
                        ["refreshSeconds"]    = Round(st.RefreshSeconds),
                    };
                }
            }
            else if (_world.IsComponentTypeRegistered<EqsCognitiveBuffer>() && _world.HasComponent<EqsCognitiveBuffer>(child))
            {
                ref readonly var b = ref _world.GetComponentRO<EqsCognitiveBuffer>(child);
                var top = new JsonArray();
                var ranked = b.GetSpanRO();
                for (int i = 0; i < b.Count && i < SensorRankedListed && i < ranked.Length; i++)
                {
                    var r = ranked[i];
                    top.Add(new JsonObject
                    {
                        ["entityId"] = r.EntityId,
                        ["score"]    = Round(r.Score),
                        ["position"] = new JsonArray(Round(r.PositionX), Round(r.PositionY), Round(r.PositionZ)),
                    });
                }
                node["answer"] = new JsonObject
                {
                    ["ready"] = b.IsReady, ["count"] = b.Count, ["lastUpdateTick"] = b.LastUpdateTick, ["top"] = top,
                };
            }
            else
            {
                node["answer"] = null;
            }
            return node;
        }

        private static JsonArray Vec(System.Numerics.Vector3 v) => new(Round(v.X), Round(v.Y), Round(v.Z));
    }
}
