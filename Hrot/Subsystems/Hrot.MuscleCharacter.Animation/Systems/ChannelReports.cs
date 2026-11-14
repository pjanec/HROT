using Fdp.Core;
using Hrot.MuscleCharacter.Animation.Components;

namespace Hrot.MuscleCharacter.Animation.Systems
{
    /// <summary>
    /// ⭐ <c>CE-513</c> / <c>R-180</c> — the Muscle's report components, added on first use. A Muscle replica can be
    /// built from the Brain's request alone (the intent ingress), so the report may not exist yet; the Muscle is the
    /// only writer of these components, never of the request.
    /// </summary>
    internal static class ChannelReports
    {
        public static ref AnimationChannelStatus Animation(EntityRepository repo, Entity entity)
        {
            if (!repo.HasComponent<AnimationChannelStatus>(entity))
                repo.AddComponent(entity, default(AnimationChannelStatus));
            return ref repo.GetComponentRW<AnimationChannelStatus>(entity);
        }

        public static ref LookAtChannelStatus LookAt(EntityRepository repo, Entity entity)
        {
            if (!repo.HasComponent<LookAtChannelStatus>(entity))
                repo.AddComponent(entity, default(LookAtChannelStatus));
            return ref repo.GetComponentRW<LookAtChannelStatus>(entity);
        }
    }
}
