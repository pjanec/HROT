using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;

namespace Fdp.Toolkit.Squad
{
    /// <summary>
    /// ⭐ <c>CE-2152</c> C-c (R-d, Squad Tactics §7.5.4) — the squad's casualties as its commander sees them: a member is a casualty
    /// "to care for" while he is DOWN (incapacitated), nobody carries him, and the care drill has not yet brought him to cover
    /// (<see cref="SquadLeadership.CaredFor"/>). ⭐ One home for the rule the care play and the rescue inputs both read.
    /// </summary>
    public static class SquadCasualties
    {
        /// <summary>Seconds left to bleed out reported for a casualty who does not bleed.</summary>
        public const float NoBleedSeconds = 1e6f;

        /// <summary>Is the man in roster slot <paramref name="slot"/> a casualty still to care for?</summary>
        public static bool ToCareFor(EntityRepository repo, Entity commander, int slot, Entity man)
        {
            if (slot < 0 || slot >= 16 || man.IsNull || !Injury.IsIncapacitated(repo, man)) return false;
            if (repo.IsComponentTypeRegistered<IsEmbarkedTag>() && repo.HasComponent<IsEmbarkedTag>(man)) return false;   // someone has him
            return (CaredFor(repo, commander) & (1 << slot)) == 0;
        }

        /// <summary>The roster bits already brought to cover.</summary>
        public static ushort CaredFor(EntityRepository repo, Entity commander)
            => repo.IsComponentTypeRegistered<SquadLeadership>() && repo.HasComponent<SquadLeadership>(commander)
                ? repo.GetComponentRO<SquadLeadership>(commander).CaredFor : (ushort)0;

        /// <summary>The casualty to care for who will bleed out FIRST (his slot, or −1), and his seconds left.</summary>
        public static int Worst(EntityRepository repo, Entity commander, out Entity man, out float secondsLeft)
        {
            man = Entity.Null; secondsLeft = NoBleedSeconds;
            if (!repo.HasComponent<UnitRoster>(commander)) return -1;
            ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(commander);
            int worst = -1;
            for (int i = 0; i < roster.Count && i < 16; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (!ToCareFor(repo, commander, i, m)) continue;
                float left = SecondsLeft(repo, m);
                if (worst < 0 || left < secondsLeft) { worst = i; man = m; secondsLeft = left; }
            }
            return worst;
        }

        /// <summary>How long until <paramref name="man"/> bleeds out: (current / max) ÷ his type's bleed rate.</summary>
        public static float SecondsLeft(EntityRepository repo, Entity man)
        {
            if (!repo.HasComponent<Health>(man) || !repo.HasComponent<InjuryThresholds>(man)) return NoBleedSeconds;
            ref readonly var h = ref repo.GetComponentRO<Health>(man);
            float rate = repo.GetComponentRO<InjuryThresholds>(man).BleedPerSecond;
            if (h.Max <= 0f || rate <= 0f) return NoBleedSeconds;
            return (h.Current / h.Max) / rate;
        }

        /// <summary>How many casualties are still to care for.</summary>
        public static int Count(EntityRepository repo, Entity commander)
        {
            if (!repo.HasComponent<UnitRoster>(commander)) return 0;
            ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(commander);
            int n = 0;
            for (int i = 0; i < roster.Count && i < 16; i++)
                if (ToCareFor(repo, commander, i, roster.SubordinateEntities[i])) n++;
            return n;
        }
    }
}
