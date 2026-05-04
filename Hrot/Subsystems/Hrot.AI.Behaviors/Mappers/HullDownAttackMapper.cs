using Fdp.Core;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common;

namespace Hrot.AI.Behaviors.Mappers
{
    /// <summary>
    /// Mapper for the "HullDownAttack" tactical intent.
    /// Translates <see cref="AssignTacticalIntentEvent.IntentId"/> == "HullDownAttack"
    /// into an <see cref="AssignBehaviorEvent"/> targeting the "HullDownAttackRun"
    /// behavior for tank-type entities.
    ///
    /// <para>Non-tank entities (including infantry and APCs) return <c>false</c>,
    /// leaving the intent unhandled and falling through to the pass-through path
    /// in <c>TacticalIntentResolutionSystem</c>.</para>
    ///
    /// <para>The <c>JsonParams</c> from the original intent are forwarded unchanged
    /// to <c>BehaviorIngressSystem</c> for parsing into <c>HullDownAttackParams</c>.</para>
    /// </summary>
    public sealed class HullDownAttackMapper : ITacticalOrderMapper
    {
        /// <inheritdoc/>
        public string TargetIntentId => "HullDownAttack";

        /// <inheritdoc/>
        public bool TryMap(
            Entity self,
            EntityRepository repo,
            string jsonParams,
            out AssignBehaviorEvent assignment)
        {
            assignment = null!;

            if (!repo.HasComponent<TkbIdentity>(self))
                return false;

            long tkbType = repo.GetComponent<TkbIdentity>(self).TkbType;

            // Accept all tank entity types.
            bool isTank = tkbType == TkbEntityTypes.Tank_M1Abrams
                       || tkbType == TkbEntityTypes.IFV_Bradley
                       || tkbType == TkbEntityTypes.Tank_T72;

            if (!isTank)
                return false;

            assignment = new AssignBehaviorEvent
            {
                Entity       = self,
                BehaviorName = "HullDownAttackRun",
                JsonParams   = jsonParams
            };
            return true;
        }
    }
}
