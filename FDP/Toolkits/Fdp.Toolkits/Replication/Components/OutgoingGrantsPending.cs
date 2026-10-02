using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>
    /// ⭐ <b>Creator-local: the descriptors this node granted to another node at creation and has not yet seen
    /// confirmed.</b> 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.2, §5.6 S5 (Q79 P6, F7).
    ///
    /// <para>The creator gives up the claim of a granted group at once (the yield), but its record keeps saying
    /// "mine" until the grantee's <c>OwnershipUpdate</c> arrives. That lag is intended: the creator's last sample
    /// of the descriptor is what lets the grantee's ghost promote and take over. The record recompute would read
    /// "claim cleared, record mine" as a disagreement and fix it, so nobody would publish in that window. This
    /// component marks those descriptors so the recompute leaves them alone; the confirming
    /// <c>OwnershipUpdate</c> removes each one.</para>
    ///
    /// <para>The counterpart of <see cref="PendingAuthorityGrants"/>, which the grantee holds. LOCAL and transient:
    /// never sent, saved, recorded or snapshotted — it lives for the few frames of one handover.</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.OutgoingGrantsPending)]
    [DataPolicy(DataPolicy.Transient)]
    public class OutgoingGrantsPending
    {
        /// <summary>Descriptor type ids granted away and not yet confirmed.</summary>
        public HashSet<long> Descriptors { get; } = new();
    }
}
