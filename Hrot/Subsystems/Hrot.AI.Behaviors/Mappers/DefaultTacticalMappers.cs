using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Squad.Mappers;

namespace Hrot.AI.Behaviors.Mappers
{
    /// <summary>
    /// ⭐⭐ <b><c>CE-454</c> — the ONE list of tactical-order mappers every Brain host registers.</b>
    /// 📄 <c>docs/designs/group-maneuvers/DESIGN_Squad_Wiring.md</c> §3 (slice <c>W3</c>).
    ///
    /// <para>🔴 It used to be built twice, by hand, in <c>CgfSubsystem</c> and <c>EditorSubsystem</c> — so adding a
    /// mapper was a per-host chance to forget (the <c>CE-161</c> shape), and the squad's <c>ForceManeuver</c> pair was
    /// in neither.</para>
    /// </summary>
    public static class DefaultTacticalMappers
    {
        /// <summary>A fresh registry holding every production mapper.</summary>
        public static TacticalIntentMapperRegistry Create()
        {
            var registry = new TacticalIntentMapperRegistry();
            registry.Register(new DefendAreaMapper());
            registry.Register(new HullDownAttackMapper());
            registry.Register(new ForceManeuverMapper());
            registry.Register(new ClearForceManeuverMapper());
            return registry;
        }
    }
}
