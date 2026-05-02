using Fdp.Core;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common;

namespace Hrot.AI.Behaviors.Mappers
{
    /// <summary>
    /// Mapper for the "DefendArea" tactical intent.
    /// Translates <see cref="AssignTacticalIntentEvent.IntentId"/> == "DefendArea"
    /// into a unit-type-specific <see cref="AssignBehaviorEvent"/>:
    /// <list type="bullet">
    ///   <item><see cref="TkbEntityTypes.MilitaryApc"/> → "ConvoyEscort" behavior</item>
    ///   <item><see cref="TkbEntityTypes.InfantrySoldier"/> → "InfantryCombat" behavior</item>
    ///   <item>All other unit types → returns <c>false</c> (pass-through fallback)</item>
    /// </list>
    /// <para>
    /// The JsonParams from the original intent (centre lat/lon, radius) are forwarded
    /// unchanged to the target behavior for further interpretation.
    /// </para>
    /// </summary>
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

            if (!repo.HasComponent<TkbIdentity>(self))
                return false;

            var tkbType = repo.GetComponent<TkbIdentity>(self).TkbType;

            string behaviorName = tkbType switch
            {
                TkbEntityTypes.MilitaryApc     => "ConvoyEscort",
                TkbEntityTypes.InfantrySoldier => "InfantryCombat",
                _                              => string.Empty
            };

            if (string.IsNullOrEmpty(behaviorName))
                return false;

            assignment = new AssignBehaviorEvent
            {
                Entity       = self,
                BehaviorName = behaviorName,
                JsonParams   = jsonParams
            };
            return true;
        }
    }
}
