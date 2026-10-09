using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Behavior.Params;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// JSON serialization DTO for the <c>MoveToLocation</c> behavior parameter block — the
    /// <b>authored contract</b>: what a scenario, the mission panel, or an agent over the debug API
    /// writes. <c>CE-235</c> makes this the type <c>GET /behaviors</c> publishes as <c>paramSchema</c>,
    /// via <c>[BehaviorContract]</c> → <c>BehaviorSchemaDiscovery</c> →
    /// <c>BehaviorDefinition.JsonParamsDtoType</c>.
    ///
    /// <para>
    /// ⛔ Not to be confused with <c>CgfNodes.MoveToLocationParams</c>, the blittable blackboard struct
    /// the resolver writes into. That one is engine-internal (<c>BehaviorDefinition.BlackboardLayoutType</c>)
    /// and must never appear in a public description.
    /// 📄 <c>Behavior_Parameter_Resolver_Detailed_Design.md</c> §3.2 — this behaviour is one of the
    /// <i>"two shapes on divergence"</i> cases the design names, precisely because a designer clicks a
    /// geo point while the tree reads Cartesian metres.
    /// </para>
    /// </summary>
    /// <remarks>⭐ <c>CE-2023</c> ③ (<c>DESIGN_Unified_Behaviour_Run.md</c> "S8n"): a STRUCT, so a host can bind it — a
    /// blueprint's Behaviour Task passes its bytes and the typed resolver's from-bytes arm converts them. ⛔ It was a class,
    /// which the from-bytes arm cannot take. Same properties and JSON keys.</remarks>
    [BehaviorContract(BehaviorId, BehaviorCategory.AllMilitary | BehaviorCategory.Civilian)]
    public struct MoveToLocationParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.MoveToLocation;

        /// <summary>Target latitude in degrees (flat JSON wire format).</summary>
        [JsonPropertyName("targetLat")]
        public double TargetLat { get; set; }

        /// <summary>Target longitude in degrees (flat JSON wire format).</summary>
        [JsonPropertyName("targetLon")]
        public double TargetLon { get; set; }

        /// <summary>Travel speed in meters per second.</summary>
        [JsonPropertyName("speed")]
        public double Speed { get; set; }

        /// <summary>Radius in meters within which arrival is declared.</summary>
        [JsonPropertyName("arrivalRadius")]
        public double ArrivalRadius { get; set; }

        /// <summary>
        /// Local ENU easting in metres, relative to <b>this node's geographic origin</b>
        /// (<c>GET /world/info</c> reports it).
        ///
        /// <para>
        /// ⚠ <b>Origin-dependent, therefore NOT portable between nodes</b> — <c>TargetLat</c>/
        /// <c>TargetLon</c> is the canonical form and the one to use for anything that travels.
        /// 🔒 User ruling, <c>2026-09-08</c>: <i>"cartesian depends on geoconverter, different nodes
        /// might use different origin, generic parameter for generic use should be geo always; for
        /// purpose of ai driven development cartesian is much easier and matches the internal
        /// components so it is still very useful so i would keep it."</i>
        /// </para>
        ///
        /// <para>
        /// ⭐ Advertised deliberately: the resolver has accepted it since <c>CE-224</c> proved it end to
        /// end on the live host, and it is the only form that works on a node whose world is missing
        /// the <c>IGeographicTransform</c> singleton (<c>CE-151</c>). An accepted-but-undocumented key
        /// helps nobody.
        /// </para>
        ///
        /// <para>
        /// ⚠ Supplied only when <c>TargetLat</c> and <c>TargetLon</c> are both zero — the geo pair wins
        /// when present. See <c>CgfNodes.ParseMoveToParams</c>.
        /// </para>
        /// </summary>
        [JsonPropertyName("x")]
        public float X { get; set; }

        /// <summary>
        /// Local ENU northing in metres. Same origin-dependence and same precedence as <see cref="X"/>.
        /// </summary>
        [JsonPropertyName("y")]
        public float Y { get; set; }

        /// <summary>
        /// ⭐ <c>CE-3130</c> (R-230) — how the mover treats the road network: <c>Never</c> (sneak, keep off it), <c>Neutral</c>,
        /// <c>Prefer</c>, <c>StronglyPrefer</c> (a convoy). <c>Unspecified</c> (omitted) leaves it to the unit's class: a vehicle
        /// prefers roads, infantry is neutral. 📄 docs/designs/navig-2/Navigation_Design_v2_0.md §5.2a D1.
        /// </summary>
        [JsonPropertyName("roadUse")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public Fdp.Toolkit.Navigation.RoadUse RoadUse { get; set; }

        /// <summary>
        /// Composite facade exposing the target position as a single pickable value.
        /// Excluded from JSON serialization; <see cref="TargetLat"/> and
        /// <see cref="TargetLon"/> carry the wire representation.
        /// </summary>
        [JsonIgnore]
        public PickableGeoPoint PickableLocation
        {
            get => new PickableGeoPoint(TargetLat, TargetLon);
            set
            {
                TargetLat = value.Latitude;
                TargetLon = value.Longitude;
            }
        }
    }
}
