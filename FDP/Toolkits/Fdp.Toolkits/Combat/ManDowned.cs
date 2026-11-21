using Fdp.Core;
using Fdp.Toolkit.Combat.Events;

namespace Fdp.Toolkit.Combat
{
    /// <summary>⭐ <c>CE-3364</c> — the ONE place a <see cref="ManDownedEvent"/> is raised; every Health writer that a HIT can take a man out of the fight through calls it (⛔ no copy).</summary>
    public static class ManDowned
    {
        /// <summary>Call BEFORE the write: was he in the fight?</summary>
        public static bool Before(EntityRepository repo, Entity victim) => Injury.IsInTheFight(repo, victim);

        /// <summary>After the hit's writes: when <paramref name="before"/> and he is out of the fight now, publishes ONE <see cref="ManDownedEvent"/> with the hit's attribution.</summary>
        public static void PublishIfDowned(EntityRepository repo, Entity victim, bool before, in DamageAssessedEvent evt)
            => PublishIfDowned(repo, victim, before, evt.Source, evt.OriginalSource, evt.HasBodyPart, evt.BodyPart);

        public static void PublishIfDowned(EntityRepository repo, Entity victim, bool before, Entity source, Entity originalSource, byte hasBodyPart = 0, byte bodyPart = 0)
        {
            if (!before || Injury.IsInTheFight(repo, victim) || !repo.Bus.IsRegistered<ManDownedEvent>()) return;   // an unregistered event: a host that does not read it
            repo.Bus.Publish(new ManDownedEvent
            {
                Victim = victim, Source = source, OriginalSource = originalSource,
                State = Injury.StateOf(repo, victim),
                HasBodyPart = hasBodyPart, BodyPart = bodyPart,
                VictimSide = repo.IsComponentTypeRegistered<EntityInfo>() && repo.HasComponent<EntityInfo>(victim) ? repo.GetComponentRO<EntityInfo>(victim).ForceId : ForceId.Neutral,
            });
        }
    }
}
