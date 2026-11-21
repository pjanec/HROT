using System;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;

namespace Fdp.Toolkit.Replication.Edits
{
    /// <summary>
    /// ⭐ Edit framework F4 (📄 docs/UX/DESIGN_Edit_Framework.md §3, Q2) — the applying node's answer to an interaction a user asked for (embark,
    /// disembark, possess, release): the ONE <see cref="EditOutcome"/> under the request's id, where the executor accepts or refuses today.
    /// A no-op when nobody asked (<see cref="Guid.Empty"/>) — so the AI's own commands cost nothing. ⛔ Not a per-interaction reply topic
    /// (<c>DESIGN_Entity_Interactions.md:136</c>).
    /// </summary>
    public static class InteractionOutcomes
    {
        public static void Applied(EntityRepository repo, Guid requestId, Entity on)
        {
            if (requestId == Guid.Empty) return;
            EditOutcomes.Report(repo, requestId, NetworkIdResolver.RuntimeNetworkIdOf(repo, on), EditStatus.Applied, "done", EditOutcomes.LocalNodeOf(repo, on));
        }

        public static void Refused(EntityRepository repo, Guid requestId, Entity on, string reason)
        {
            if (requestId == Guid.Empty) return;
            EditOutcomes.Report(repo, requestId, NetworkIdResolver.RuntimeNetworkIdOf(repo, on), EditStatus.Refused, reason, EditOutcomes.LocalNodeOf(repo, on));
        }
    }
}
