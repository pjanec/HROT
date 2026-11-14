using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// JSON serialization DTO for the <c>MoveToLocation</c> behavior parameter block — the
    /// <b>authored contract</b>: what a scenario, the mission panel, or an agent over the debug API
    /// writes. <c>CE-235</c> makes this the type <c>GET /behaviors</c> publishes as <c>paramSchema</c>,
    /// via <c>[Behavior(Params = …)]</c> → <c>BehaviorSchemaDiscovery</c> →
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
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    [Fdp.Toolkit.Replication.RetiredJsonKeys("targetLat", "targetLon", "x", "y")]   // CE-4153 S10: S0 item 5 folded them into `target`
    public struct MoveToLocationParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.MoveToLocation;

        /// <summary>
        /// ⭐ <c>S0</c> item 5 (<c>DESIGN_Scenario_Rules</c> §9.8.0, SR-135) — WHERE to go: ONE authored place, local metres
        /// (<c>[x, y, z]</c>) or an entity's position (<c>"@Key"</c> / a GUID). It replaces the lat/lon pair, the local <c>x</c> / <c>y</c>
        /// fallback and the computed <c>PickableLocation</c>: the TYPE makes the field map-pickable and relocatable, and no latitude or
        /// longitude exists inside the engine (the outer edge converts). ⛔ Migrating authored content is S0 item 9.
        /// </summary>
        [JsonPropertyName("target")]
        public Fdp.Toolkit.Replication.PointLink Location;   // ⚠ the C# member is `Location`, the JSON key `target`: a blueprint SendIntent node names its pins by member, and its own entity pin is already called "Target"

        /// <summary>Travel speed in meters per second.</summary>
        [JsonPropertyName("speed")]
        public double Speed;

        /// <summary>Radius in meters within which arrival is declared.</summary>
        [JsonPropertyName("arrivalRadius")]
        public double ArrivalRadius;

        /// <summary>
        /// ⭐ <c>CE-3130</c> (R-230) — how the mover treats the road network: <c>Never</c> (sneak, keep off it), <c>Neutral</c>,
        /// <c>Prefer</c>, <c>StronglyPrefer</c> (a convoy). <c>Unspecified</c> (omitted) leaves it to the unit's class: a vehicle
        /// prefers roads, infantry is neutral. 📄 docs/designs/navig-2/Navigation_Design_v2_0.md §5.2a D1.
        /// </summary>
        [JsonPropertyName("roadUse")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public Fdp.Toolkit.Navigation.RoadUse RoadUse;
    }
}
