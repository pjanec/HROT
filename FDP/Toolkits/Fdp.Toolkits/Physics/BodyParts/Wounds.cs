using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Physics.BodyParts
{
    /// <summary>
    /// ⭐⭐ <c>CE-3210</c> (docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md HL-7) — a person's limb wounds, DERIVED like
    /// <c>Injury</c>'s states (I1): the replicated <see cref="PartDamage"/> against the type's resolved zones (<see cref="BodyZones"/>), never
    /// stored or sent, so every node agrees. A LEG zone at or over its threshold slows him (the type's wounded speed factor, no running); an
    /// ARM zone widens his aim (<see cref="WoundedAimFactor"/> — Injury States' open "wounded AIM").
    /// </summary>
    public static class Wounds
    {
        /// <summary>A wounded arm's aim spread, × the healthy one. Starter value (HL-11) — tuning starts here.</summary>
        public const float WoundedAimFactor = 1.6f;

        /// <summary>A wounded leg's top speed share when the type declares no wounded speed factor.</summary>
        public const float WoundedLegSpeedFactor = 0.6f;

        /// <summary>Is any zone of <paramref name="entity"/> with <paramref name="effect"/> wounded (a part at or over its threshold)?</summary>
        public static bool Has(ISimulationView view, Entity entity, WoundEffect effect)
        {
            // ⚠ a snapshot view reaches no TKB ⇒ it judges by the engine's person body (the live world uses the type's zones)
            var world = view as EntityRepository;
            if (world != null && !world.IsComponentTypeRegistered<PartDamage>()) return false;
            if (!view.HasComponent<PartDamage>(entity)) return false;
            var damage = view.GetComponentRO<PartDamage>(entity);
            if ((world != null ? BodyZones.Of(world, entity) : BodyZones.PersonFallback.Value) is not { } body) return false;
            foreach (var zone in body.Zones)
            {
                if (zone.Effect != effect) continue;
                for (byte p = 0; p < PartDamage.MaxParts; p++)
                    if (zone.Has(p) && damage.LevelOf(p) >= zone.WoundedAt) return true;
            }
            return false;
        }

        /// <summary>A wounded LEG (HL-7): slower, no running.</summary>
        public static bool LegWounded(ISimulationView view, Entity entity) => Has(view, entity, WoundEffect.Slow);

        /// <summary>A wounded ARM (HL-7): a wider aim.</summary>
        public static bool ArmWounded(ISimulationView view, Entity entity) => Has(view, entity, WoundEffect.Aim);

        /// <summary>The aim-spread factor his wounds give (1 when none).</summary>
        public static float AimFactor(ISimulationView view, Entity entity) => ArmWounded(view, entity) ? WoundedAimFactor : 1f;
    }
}
