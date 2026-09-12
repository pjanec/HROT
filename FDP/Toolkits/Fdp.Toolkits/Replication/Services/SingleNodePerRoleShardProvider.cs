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
    /// <para>⚠ <b>Roles are opaque BITS here, not a <c>NodeRole</c></b> — that enum lives in
    /// <c>Hrot.Core</c>, which references this assembly, so the dependency cannot run the other way. A
    /// host casts at its composition root: <c>new SingleNodePerRoleShardProvider((int)NodeRole.Brain)</c>.
    /// See <see cref="IRoleShardProvider.ServesRole"/> for why this is also the RIGHT layering and not
    /// merely the possible one.</para>
    /// </summary>
    public sealed class SingleNodePerRoleShardProvider : IRoleShardProvider
    {
        private readonly int _declaredRoles;

        /// <param name="declaredRoles">
        /// The bitwise OR of every role this node declares. ⭐ <c>0</c> ("no role") is legitimate — a
        /// presentation-only node declares nothing and correctly serves nothing.
        /// </param>
        public SingleNodePerRoleShardProvider(int declaredRoles)
        {
            _declaredRoles = declaredRoles;
        }

        /// <summary>
        /// ⭐ True when this node declares <paramref name="roleBit"/>. <paramref name="key"/> is
        /// deliberately unread — see the class summary.
        ///
        /// <para>⚠ <b>Multi-bit arguments are ANY, not ALL.</b> Passing more than one bit asks
        /// <i>"do I serve any of these?"</i>, which follows from <c>&amp;</c> and matches how the policy
        /// iterates: it tests one declared role at a time, so the distinction never arises in production.
        /// ⛔ A caller wanting ALL must test each bit separately.</para>
        ///
        /// <para>⭐ <c>roleBit == 0</c> answers <c>false</c>: "no role" is served by nobody, which is what
        /// keeps a defaulted/forgotten role from quietly matching every node.</para>
        /// </summary>
        public bool ServesRole(int roleBit, in RoleShardKey key)
            => roleBit != 0 && (_declaredRoles & roleBit) != 0;
    }
}
