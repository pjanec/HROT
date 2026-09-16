using System;
using System.Collections.Generic;

namespace Fdp.Core
{
    /// <summary>
    /// The closed, bit-backed enum↔token table for <see cref="NodeRole"/> — the SINGLE mapping point
    /// between the <c>[Flags]</c> role mask and its <c>fdp.role.*</c> capability tokens.
    ///
    /// <para>⭐⭐⭐ <b>WHY THIS EXISTS (AQ-70 §Q70-C, <c>2026-09-16</c>).</b> Roles are the bit-backed
    /// SUBSET of the open host-capability token vocabulary. A host advertises <c>fdp.role.*</c> tokens on the
    /// durable <c>NodeCapabilities</c> descriptor; the orchestrator/cache <b>DERIVES</b> the <see cref="NodeRole"/>
    /// mask from those tokens at ingest via this table. 🔒 <b>User:</b> <i>"can't the flags be constructed from
    /// the capabilities? so host does not need to publish BOTH and keep them in sync? Everyone can derive the
    /// role mask from the capability strings."</i> ⇒ the token set is the SOLE wire source; the mask is a local
    /// projection. Having exactly ONE table means no two derivers can diverge — it supersedes CE-282's
    /// roles-on-heartbeat (<c>NodeHeartbeat.RolesMask</c>).</para>
    ///
    /// <para>⛔ <b>Total + graceful:</b> every role bit has a token (projection is total), an unknown
    /// <c>fdp.role.*</c> token (a newer role an older deriver lacks) simply omits its bit, and a non-role token
    /// is ignored — the same degrade as any capability.</para>
    /// </summary>
    public static class NodeRoleTokens
    {
        /// <summary>The namespace prefix for role tokens — the bit-backed subset of the capability vocabulary.</summary>
        public const string RolePrefix = "fdp.role.";

        public const string Brain            = "fdp.role.brain";
        public const string MuscleGround     = "fdp.role.muscle-ground";
        public const string Map2D            = "fdp.role.map2d";
        public const string Perception       = "fdp.role.perception";
        public const string NavigationSolver = "fdp.role.navigation-solver";

        // The closed table. Each row is (bit, token); the two directions read the same rows so they cannot diverge.
        private static readonly (NodeRole Role, string Token)[] Table =
        {
            (NodeRole.Brain,            Brain),
            (NodeRole.MuscleGround,     MuscleGround),
            (NodeRole.Map2D,            Map2D),
            (NodeRole.Perception,       Perception),
            (NodeRole.NavigationSolver, NavigationSolver),
        };

        /// <summary>The <c>fdp.role.*</c> tokens for every bit set in <paramref name="mask"/> (empty for
        /// <see cref="NodeRole.None"/>). The emit side — a host projects its boot role mask to tokens.</summary>
        public static IEnumerable<string> TokensFromMask(NodeRole mask)
        {
            foreach (var (role, token) in Table)
                if ((mask & role) != 0)
                    yield return token;
        }

        /// <summary>The derived <see cref="NodeRole"/> mask = OR of the bit for every recognised
        /// <c>fdp.role.*</c> token in <paramref name="tokens"/>. Unknown/non-role tokens are ignored; a null or
        /// empty set yields <see cref="NodeRole.None"/>. The derive side — the orchestrator/cache computes the
        /// mask once at ingest.</summary>
        public static NodeRole MaskFromTokens(IEnumerable<string>? tokens)
        {
            if (tokens is null) return NodeRole.None;
            var mask = NodeRole.None;
            foreach (var t in tokens)
            {
                if (string.IsNullOrEmpty(t)) continue;
                foreach (var (role, token) in Table)
                    if (string.Equals(t, token, StringComparison.Ordinal))
                    {
                        mask |= role;
                        break;
                    }
            }
            return mask;
        }
    }
}
