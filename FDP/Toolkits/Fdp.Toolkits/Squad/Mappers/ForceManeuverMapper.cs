using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Squad;

namespace Fdp.Toolkit.Squad.Mappers
{
    /// <summary>
    /// Maps a "ForceManeuver" tactical intent to a direct write of
    /// <see cref="SquadCognitiveState.ManeuverKind"/> + MissionOverride flag.
    /// JSON payload: <c>{"maneuverKind":&lt;ushort&gt;,"featureId":&lt;uint?&gt;}</c>
    /// </summary>
    public sealed class ForceManeuverMapper : ITacticalOrderMapper
    {
        public string TargetIntentId => "ForceManeuver";

        public bool TryMap(Entity self, EntityRepository repo, string jsonParams,
                           out AssignBehaviorEvent assignment)
        {
            assignment = null!;
            if (!repo.HasComponent<Blackboard1024>(self)) return false;

            // Parse JSON.
            ForceManeuverParams p;
            try
            {
                p = JsonSerializer.Deserialize<ForceManeuverParams>(jsonParams,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            }
            catch { return false; }

            ref var state = ref SquadCognitiveState.Project(
                ref repo.GetComponentRW<Blackboard1024>(self));

            state.ManeuverKind   = p.ManeuverKind;
            state.Flags         |= MissionOverrideBit;
            if (p.FeatureId.HasValue)
                state.ActiveFeatureId = p.FeatureId.Value;

            assignment = new AssignBehaviorEvent { Entity = self, BehaviorName = string.Empty };
            return true;
        }

        private const uint MissionOverrideBit = 1u;
    }

    /// <summary>
    /// Clears the MissionOverride flag so the commander's Utility scorer resumes.
    /// No JSON parameters required.
    /// </summary>
    public sealed class ClearForceManeuverMapper : ITacticalOrderMapper
    {
        public string TargetIntentId => "ClearForceManeuver";

        public bool TryMap(Entity self, EntityRepository repo, string jsonParams,
                           out AssignBehaviorEvent assignment)
        {
            assignment = null!;
            if (!repo.HasComponent<Blackboard1024>(self)) return false;

            ref var state = ref SquadCognitiveState.Project(
                ref repo.GetComponentRW<Blackboard1024>(self));

            state.Flags &= ~MissionOverrideBit;

            assignment = new AssignBehaviorEvent { Entity = self, BehaviorName = string.Empty };
            return true;
        }

        private const uint MissionOverrideBit = 1u;
    }

    internal sealed class ForceManeuverParams
    {
        public ushort ManeuverKind { get; set; }
        public uint?  FeatureId   { get; set; }
    }
}
