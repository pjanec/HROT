using Fdp.Core;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Behavior;
using Hrot.Map.Definitions.Behavior.Intents;

namespace Hrot.AI.Behaviors.Mappers
{
    /// <summary>
    /// Mapper for the "DefendArea" tactical intent.
    /// Translates <see cref="AssignTacticalIntentEvent.IntentId"/> == "DefendArea"
    /// into a unit-type-specific <see cref="AssignBehaviorEvent"/>:
    /// <list type="bullet">
    ///   <item><see cref="TkbEntityTypes.MilitaryApc"/> → "ConvoyEscort" behavior</item>
    ///   <item>⭐ <see cref="TkbEntityTypes.InfantrySoldier"/> / <see cref="TkbEntityTypes.Insurgent"/> → the library's
    ///         <c>CombatPosture</c>, advancing to the area's centre and holding within its radius (<c>CE-3160</c>, L4, R-266 — ⛔ it
    ///         was <c>InfantryCombat</c>, the UrbanCombat example's tree, registered on the editor only, so CGF dropped the order)</item>
    ///   <item>All other unit types → returns <c>false</c> (pass-through fallback)</item>
    /// </list>
    /// <para>
    /// The APC's JsonParams (centre lat/lon, radius) are forwarded unchanged; the infantry's are converted to the posture's
    /// contract with the node's geographic transform.
    /// </para>
    /// </summary>
    [Behavior(BehaviorNames.DefendArea, Params = typeof(global::Hrot.Map.Definitions.Behavior.Intents.DefendAreaIntentDto), Categories = BehaviorCategory.AllMilitary)]   // ⭐ CE-2134 — an intent id is fulfilled by its mapper
    public sealed class DefendAreaMapper : ITacticalOrderMapper
    {
        public string TargetIntentId => "DefendArea";

        public bool TryMap(
            Entity self,
            EntityRepository repo,
            string jsonParams,
            out AssignBehaviorEvent assignment)
        {
            assignment = null!;

            // ⭐ CE-2275 D-1 (DA-39) — a unit with men under it defends with its squad: SquadDefend at the area's centre, its radius the
            //   posts' search radius. 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8c.
            if (repo.IsComponentTypeRegistered<Fdp.Core.CommandHierarchy.UnitRoster>() && repo.HasComponent<Fdp.Core.CommandHierarchy.UnitRoster>(self)
                && repo.GetComponentRO<Fdp.Core.CommandHierarchy.UnitRoster>(self).Count > 0)
            {
                assignment = new AssignBehaviorEvent { Entity = self, BehaviorName = BehaviorNames.SquadDefend, JsonParams = SquadDefendAt(repo, jsonParams) };
                return true;
            }

            if (!repo.HasComponent<TkbIdentity>(self))
                return false;

            var tkbType = repo.GetComponent<TkbIdentity>(self).TkbType;

            string behaviorName = tkbType switch
            {
                TkbEntityTypes.MilitaryApc     => "ConvoyEscort",
                TkbEntityTypes.InfantrySoldier => BehaviorNames.CombatPosture,
                TkbEntityTypes.Insurgent       => BehaviorNames.CombatPosture,
                _                              => string.Empty
            };

            if (string.IsNullOrEmpty(behaviorName))
                return false;

            if (behaviorName == BehaviorNames.CombatPosture)
                jsonParams = PostureAt(repo, jsonParams);

            assignment = new AssignBehaviorEvent
            {
                Entity       = self,
                BehaviorName = behaviorName,
                JsonParams   = jsonParams
            };
            return true;
        }
    
        /// <summary>⭐ CE-2275 — the area as SquadDefend's contract: the centre (this node's sim frame) and the radius.</summary>
        private static string SquadDefendAt(EntityRepository repo, string jsonParams)
        {
            var options = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
            var area = string.IsNullOrWhiteSpace(jsonParams) ? null
                : System.Text.Json.JsonSerializer.Deserialize<DefendAreaIntentDto>(jsonParams, options);
            return System.Text.Json.JsonSerializer.Serialize(new SquadDefendParamsJsonDto
            {
                Position = CentreOf(repo, area), SearchRadius = (float)(area?.RadiusMeters ?? 0f),
            }, options);
        }

        private static System.Numerics.Vector3 CentreOf(EntityRepository repo, DefendAreaIntentDto? area)
        {
            var geo = repo.HasSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>()
                ? repo.GetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>() : null;
            if (area == null || (area.CenterLat == 0 && area.CenterLon == 0) || geo == null) return System.Numerics.Vector3.Zero;
            var c = geo.ToCartesian(area.CenterLat, area.CenterLon, 0.0);
            return new System.Numerics.Vector3(c.X, c.Y, 0f);
        }

        /// <summary>The area as CombatPosture's contract: advance to the centre (lat/lon → this node's sim frame), arrive within the
        /// radius. Without a geographic transform the centre stays at the origin — the posture then holds where it is.</summary>
        private static string PostureAt(EntityRepository repo, string jsonParams)
        {
            var options = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
            var area = string.IsNullOrWhiteSpace(jsonParams) ? null
                : System.Text.Json.JsonSerializer.Deserialize<DefendAreaIntentDto>(jsonParams, options);
            var centre = System.Numerics.Vector3.Zero;
            var geo = repo.HasSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>()
                ? repo.GetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>() : null;
            if (area != null && (area.CenterLat != 0 || area.CenterLon != 0) && geo != null)
            {
                var c = geo.ToCartesian(area.CenterLat, area.CenterLon, 0.0);
                centre = new System.Numerics.Vector3(c.X, c.Y, 0f);
            }
            var posture = new CombatPostureParamsJsonDto
            {
                Advance = new CombatPostureAdvanceJsonDto { Objective = centre, ArrivalRadius = area?.RadiusMeters ?? 0f },
            };
            return System.Text.Json.JsonSerializer.Serialize(posture, options);
        }
}
}
