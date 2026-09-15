using Fdp.Core;
using Fdp.Toolkit.Replication.Abstractions;

namespace Fdp.Toolkit.Replication.Services
{
    /// <summary>
    /// ⭐⭐⭐ <b>The one <see cref="IRoleShardProvider"/> implementation <c>P3</c> ships: exactly one node
    /// serves each role, so the answer is <i>"do I declare this role?"</i> and nothing else.</b>
    ///
    /// <para>📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.8, §6 step <c>1a</c>.</para>
    ///
    /// <para>⭐⭐ <b>It IGNORES the key, and that is the whole point.</b> Ignoring it makes this
    /// byte-identical to the single-Brain / single-Muscle behaviour the cluster has today ⇒ adopting the
    /// seam changes no observable behaviour, and the rest of <c>P3</c>'s rails keep their meaning. ⛔ A
    /// default that read <see cref="RoleShardKey.NetworkId"/> would be a sharding POLICY smuggled in as a
    /// default, and it would silently break the networkless case.</para>
    ///
    /// <para>⭐⭐ <b>Correct on a NETWORKLESS node for free.</b> Such a node has no <c>NetworkIdentity</c>,
    /// so <c>NetworkId</c> is <c>0</c> — and because this never reads it, §2.3's network-agnosticism
    /// ruling holds <b>by construction</b> rather than by care.</para>
    ///
    /// <para>⭐ <b>Its input is something every host already declares</b> — the role it was configured
    /// with. No new configuration, no new table.</para>
    ///
    /// <para>⭐ <see cref="NodeRole"/> is a <c>[Flags]</c> enum in <c>Fdp.Core</c>, so <i>"the roles I
    /// declare"</i> is ONE value and this is ONE bitwise test. ⛔ The engine holds the LABEL only — it
    /// never learns which components a role owns (§2.3); that table is the application's.</para>
    /// </summary>
    public sealed class SingleNodePerRoleShardProvider : IRoleShardProvider
    {
        private readonly NodeRole _declaredRoles;

        /// <param name="declaredRoles">
        /// The bitwise OR of every role this node declares. ⭐ <see cref="NodeRole.None"/> is legitimate —
        /// a presentation-only node declares nothing and correctly serves nothing.
        /// </param>
        public SingleNodePerRoleShardProvider(NodeRole declaredRoles)
        {
            _declaredRoles = declaredRoles;
        }

        /// <summary>
        /// ⭐ True when this node declares <paramref name="role"/>. <paramref name="key"/> is
        /// deliberately unread — see the class summary.
        ///
        /// <para>⚠ <b>Multi-flag arguments are ANY, not ALL.</b> Passing more than one flag asks
        /// <i>"do I serve any of these?"</i>, which follows from <c>&amp;</c> and matches how the policy
        /// iterates: it tests one declared role at a time, so the distinction never arises in production.
        /// ⛔ A caller wanting ALL must test each flag separately.</para>
        ///
        /// <para>⭐ <see cref="NodeRole.None"/> answers <c>false</c>: "no role" is served by nobody, which
        /// is what keeps a defaulted or forgotten role from quietly matching every node.</para>
        /// </summary>
        public bool ServesRole(NodeRole role, in RoleShardKey key)
            => role != NodeRole.None && (_declaredRoles & role) != 0;
    }
}
