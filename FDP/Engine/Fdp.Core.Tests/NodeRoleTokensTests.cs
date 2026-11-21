using System.Linq;
using Fdp.Core;
using Xunit;

namespace Fdp.Tests
{
    /// <summary>
    /// CE-286 (C-roles) — the closed enum↔token table (AQ-70 §Q70-C). Roles are the bit-backed subset of the
    /// capability vocabulary: a host advertises <c>fdp.role.*</c> tokens and everyone DERIVES the mask from them
    /// via this single table, so no two derivers can diverge and the mask never rides the wire.
    /// </summary>
    public class NodeRoleTokensTests
    {
        [Fact]
        public void TokensFromMask_ProjectsEveryBit()
        {
            var mask = NodeRole.MuscleGround | NodeRole.Perception;
            var tokens = NodeRoleTokens.TokensFromMask(mask).ToArray();
            Assert.Contains(NodeRoleTokens.MuscleGround, tokens);
            Assert.Contains(NodeRoleTokens.Perception, tokens);
            Assert.Equal(2, tokens.Length);
        }

        [Fact]
        public void MaskFromTokens_RoundTripsTheMultiRoleMask()
        {
            var mask = NodeRole.MuscleGround | NodeRole.Perception;
            var derived = NodeRoleTokens.MaskFromTokens(NodeRoleTokens.TokensFromMask(mask));
            Assert.Equal(mask, derived);
        }

        [Fact]
        public void RoundTrip_CoversEverySingleRole()
        {
            foreach (var role in new[] { NodeRole.Brain, NodeRole.MuscleGround, NodeRole.Map2D,
                                         NodeRole.Perception, NodeRole.NavigationSolver })
            {
                Assert.Equal(role, NodeRoleTokens.MaskFromTokens(NodeRoleTokens.TokensFromMask(role)));
            }
        }

        [Fact]
        public void None_ProjectsToNoTokens_AndBack()
        {
            Assert.Empty(NodeRoleTokens.TokensFromMask(NodeRole.None));
            Assert.Equal(NodeRole.None, NodeRoleTokens.MaskFromTokens(new string[0]));
            Assert.Equal(NodeRole.None, NodeRoleTokens.MaskFromTokens(null));
        }

        [Fact]
        public void MaskFromTokens_IgnoresNonRoleAndUnknownTokens()
        {
            // A feature token, an unknown role token (a newer role an older deriver lacks), and a null — all ignored;
            // recognised role tokens still contribute their bit. This is the graceful-degrade property.
            var tokens = new[] { "fdp.reliable-init", "fdp.role.brain", "fdp.role.future-role", null! };
            Assert.Equal(NodeRole.Brain, NodeRoleTokens.MaskFromTokens(tokens));
        }
    }
}
