using System;
using System.Collections.Generic;
using Xunit;
using Fdp.Core;
using Fdp.Toolkit.Replication.Abstractions;
using Fdp.Toolkit.Replication.Services;

namespace Fdp.Toolkit.Replication.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>P3</c> step <c>1a</c> — the ROLE-SHARD SEAM.</b>
    /// 📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.8, §6 step <c>1a</c>.
    ///
    /// <para>🔒 <b>The ruling these enforce</b> (user, <c>2026-09-10</c>): <i>"its not just multi brain, it
    /// is also multi muscle or multi perception… i need the shard provider interface for these,
    /// implemented for single brain and single muscle case we have now, but reimplementable later."</i>
    /// ⇒ the SEAM ships; only a sharding implementation is deferred.</para>
    ///
    /// <para>⭐⭐ <b>A new test class is justified here</b> (<c>R-142</c> ④ normally forbids one): role
    /// sharding is a new feature with no existing suite. <c>Replication/OwnershipTests.cs</c> is the wire
    /// ownership PROTOCOL (<c>DescriptorAuthorityChanged</c> and friends) — a different subject, and
    /// folding these in would bury them.</para>
    ///
    /// <para>⭐ <b><see cref="NodeRole"/> is a <c>[Flags]</c> enum in <c>Fdp.Core</c></b> — 🔒 user ruling,
    /// <c>2026-09-12</c>: <i>"roles has nothing to do with concrete network… roles can be defined in fdp
    /// if needed as they are pretty generic."</i> ⛔ The engine holds the LABEL only; which components a
    /// role owns is a <c>BitMask512</c> the application supplies (§2.3), and nothing here maps one to the
    /// other.</para>
    /// </summary>
    public class RoleShardSeamTests
    {
        // ⚠ ImageGenerator is a KNOWN-WRONG name whose rename to Map2D is owned by CE-212
        //   (docs/DESIGN_Stride_Node_Modes.md §S10, "needs Roslyn, run twice and unioned"). These rails
        //   name it only because the enum does; nothing here depends on the spelling.
        private const NodeRole Brain            = NodeRole.Brain;
        private const NodeRole MuscleGround     = NodeRole.MuscleGround;
        private const NodeRole ImageGenerator   = NodeRole.ImageGenerator;
        private const NodeRole Perception       = NodeRole.Perception;
        private const NodeRole NavigationSolver = NodeRole.NavigationSolver;

        private static IEnumerable<RoleShardKey> AssortedKeys()
        {
            yield return default;                                              // everything zero
            yield return new RoleShardKey(0, 0, default);                      // ⭐ the networkless case
            yield return new RoleShardKey(1, 1001, default);
            yield return new RoleShardKey(long.MaxValue, long.MaxValue,
                                          new DISEntityType { Value = ulong.MaxValue });
            yield return new RoleShardKey(-7, -7, new DISEntityType { Value = 42 });
        }

        /// <summary>
        /// ⭐⭐ <b>Step 1a's gate, first half — the default answers <c>true</c> for every DECLARED role and
        /// <c>false</c> otherwise, FOR ANY KEY.</b>
        ///
        /// <para>⭐ Including <c>NetworkId == 0</c>, which is the networkless node: it has no
        /// <c>NetworkIdentity</c> at all, and §2.3 rules that ownership must mean the same thing there as
        /// on a DDS cluster.</para>
        /// </summary>
        [Fact]
        public void Default_ServesExactlyTheDeclaredRoles_ForEveryKey()
        {
            var provider = new SingleNodePerRoleShardProvider(Brain | Perception);

            foreach (var key in AssortedKeys())
            {
                Assert.True(provider.ServesRole(Brain, key));
                Assert.True(provider.ServesRole(Perception, key));

                Assert.False(provider.ServesRole(MuscleGround, key));
                Assert.False(provider.ServesRole(ImageGenerator, key));
                Assert.False(provider.ServesRole(NavigationSolver, key));
            }
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Step 1a's gate, second half — THE CONTRACT RAIL: the default IGNORES the key.</b>
        ///
        /// <para>⛔⛔ This is the one that protects the design's entire safety property. <c>ServesRole</c>
        /// may read only inputs IDENTICAL ON EVERY NODE; the moment a default starts discriminating on
        /// <c>NetworkId</c> it has smuggled in a sharding POLICY, and a node that computes an answer from
        /// anything node-local can disagree with its peers ⇒ <b>two owners for one component</b>, the
        /// exact conflict this design removes.</para>
        ///
        /// <para>📐 <b>How it proves it, rather than asserting it:</b> the answer must be constant across
        /// keys that differ in <i>every</i> field. ⭐ Red-proof — make <c>ServesRole</c> read
        /// <c>key.NetworkId</c> (e.g. <c>… &amp;&amp; key.NetworkId % 2 == 0</c>) and this reddens.</para>
        /// </summary>
        [Fact]
        public void Default_IgnoresTheKeyEntirely_SoEveryNodeAgrees()
        {
            var provider = new SingleNodePerRoleShardProvider(MuscleGround);

            bool first = provider.ServesRole(MuscleGround, default);

            foreach (var key in AssortedKeys())
            {
                Assert.Equal(first, provider.ServesRole(MuscleGround, key));
                Assert.Equal(false, provider.ServesRole(Brain, key));
            }

            // ⛔ Anti-vacuity: if the declared role were not served, "constant across keys" would hold
            //    trivially at false and this rail would assert nothing.
            Assert.True(first);
        }

        /// <summary>
        /// ⭐ A node that declares NO role serves nothing — a presentation-only node (IG) is the real case.
        /// ⛔ If this ever answered <c>true</c>, such a node would claim ownership of everything it
        /// materialised, which is the blanket grant this design exists to replace.
        /// </summary>
        [Fact]
        public void Default_DeclaringNoRole_ServesNothing()
        {
            var provider = new SingleNodePerRoleShardProvider(NodeRole.None);

            foreach (var key in AssortedKeys())
            {
                Assert.False(provider.ServesRole(Brain, key));
                Assert.False(provider.ServesRole(MuscleGround, key));
                Assert.False(provider.ServesRole(ImageGenerator, key));
            }
        }

        /// <summary>
        /// ⭐ Asking about "no role" answers <c>false</c>, whatever the node declares.
        /// ⚠ This is what stops a defaulted or forgotten <see cref="NodeRole.None"/> (or an uninitialised
        /// field) from quietly matching every node — <c>(anything &amp; 0) != 0</c> is already false, but
        /// stating it as a rail makes the intent survive a refactor of the expression.
        /// </summary>
        [Fact]
        public void Default_TheEmptyRole_IsServedByNobody()
        {
            var all = new SingleNodePerRoleShardProvider(
                Brain | MuscleGround | ImageGenerator | Perception | NavigationSolver);

            Assert.False(all.ServesRole(NodeRole.None, default));
        }

        /// <summary>
        /// ⭐ A multi-flag argument means ANY, not ALL — documented because it follows from <c>&amp;</c> and
        /// would otherwise be an accident. ⚠ In production the policy tests one declared role at a time,
        /// so the distinction never arises; this pins the behaviour for anyone who does ask.
        /// </summary>
        [Fact]
        public void Default_AMultiFlagQuery_IsAny_NotAll()
        {
            var brainOnly = new SingleNodePerRoleShardProvider(Brain);

            Assert.True(brainOnly.ServesRole(Brain | MuscleGround, default));
            Assert.False(brainOnly.ServesRole(MuscleGround | Perception, default));
        }

        /// <summary>
        /// ⭐⭐ <b><c>RoleShardKey.TkbType</c> is a <c>long</c>, and this rail is why.</b>
        ///
        /// <para>⚠ §3.8 first wrote it as an <c>int</c>. Every `TkbType` in the system is a <c>long</c>
        /// (<c>TkbIdentity</c>, <c>SpawnEntityCommand</c>, <c>TkbTemplate</c>), so an <c>int</c> field
        /// would have <b>silently truncated</b> — and the failure mode is nasty for a SPECIALISATION
        /// shard: two genuinely different entity types whose low 32 bits collide would map to the same
        /// shard, so one node would serve entities it was never assigned.</para>
        /// </summary>
        [Fact]
        public void Key_CarriesFullWidthTkbTypeAndNetworkId()
        {
            long wide = (1L << 40) | 1234L;
            var key = new RoleShardKey(wide, wide, new DISEntityType { Value = 0xDEADBEEFCAFEUL });

            Assert.Equal(wide, key.TkbType);
            Assert.Equal(wide, key.NetworkId);
            Assert.Equal(0xDEADBEEFCAFEUL, key.DisType.Value);

            // ⛔ The truncation this guards against would have been invisible:
            Assert.NotEqual(1234L, key.TkbType);
        }
    }
}
