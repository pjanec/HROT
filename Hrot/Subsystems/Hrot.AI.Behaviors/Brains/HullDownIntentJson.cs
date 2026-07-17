using System.Text.Json;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Curated JSON-payload builder for <c>AssignTacticalIntentEvent{ IntentId="HullDownAttack" }</c>
    /// (architect Q#6-C / Q#8-E) — the wave-dispatch sibling of <see cref="MoveIntentJson"/>. Serializes a
    /// <see cref="HullDownAttackParams"/> to the event's opaque <c>JsonParams</c> string, keeping the
    /// serialization in reviewable C# so the visual graph just publishes the string. Mirrors the C# oracle
    /// <c>HillAttackCommanderNodes.Action_DispatchWaveWithTargets</c>'s
    /// <c>JsonSerializer.Serialize(new HullDownAttackParams{…}, FdpJsonOptionsRegistry.DefaultRelaxed)</c>.
    /// </summary>
    public static class HullDownIntentJson
    {
        /// <summary>
        /// Serializes a <see cref="HullDownAttackParams"/> from the per-tank firing/baseline positions,
        /// attack direction, and resolved target NetworkId. The oracle's fixed kinematics/quota constants
        /// (<c>ApproachSpeed=15</c>, <c>CreepSpeed=5</c>, <c>MaxRounds=1</c>, <c>RoundsFired=0</c>,
        /// <c>LastObservedAmmo=-1</c>) are baked here, byte-for-byte as the oracle does.
        /// </summary>
        public static string Build(
            float slotX, float slotY, float baselineX, float baselineY,
            float attackDirX, float attackDirY, long targetNetworkId)
        {
            var dto = new HullDownAttackParams
            {
                SlotX            = slotX,
                SlotY            = slotY,
                BaselineX        = baselineX,
                BaselineY        = baselineY,
                AttackDirX       = attackDirX,
                AttackDirY       = attackDirY,
                TargetNetworkId  = targetNetworkId,
                ApproachSpeed    = 15f,
                CreepSpeed       = 5f,
                MaxRounds        = 1,
                RoundsFired      = 0,
                LastObservedAmmo = -1,
            };
            return JsonSerializer.Serialize(
                dto,
                Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
        }
    }
}
