using System.Collections.Generic;
using Hrot.Core.Network;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>D1</c> — the forwarder listens to the REQUEST, not the local ORDER.</b>
    ///
    /// <para>📄 <c>docs/DESIGN_Entity_Creation_Unification.md</c> §3.4b. 🔒 Approved by the user
    /// <c>2026-09-02</c> as option (b).</para>
    ///
    /// <para>🔴 <b>What was broken.</b> <c>SpawnEntityCommandEgressTranslator</c> reads
    /// <c>SpawnEntityCommand</c> — the node-local order the request system publishes AFTER deciding to
    /// act. On a host that also materialises entities, both consumers read the same non-draining bus, so
    /// one gesture produced two entities; and a request addressed to a remote owner published no order
    /// at all, so nothing was forwarded. ⇒ <b>both owner values were broken</b>, which is why this is a
    /// level change rather than a filter.</para>
    /// </summary>
    public class EntityCreationForwardingRails
    {
        private const int LocalNodeId  = 7;
        private const int RemoteNodeId = 9;

        private sealed class RecordingEgress : IEntityCreationRequestEgress
        {
            public List<EntityCreationRequest> Sent { get; } = new();
            public void Send(EntityCreationRequest request) => Sent.Add(request);
        }

        private static EntityCreationRequest Request(int owner) => new()
        {
            RequestId          = System.Guid.NewGuid(),
            OwnerAppInstanceId = owner,
            TkbType            = 42L,
        };

        private static (List<EntityCreationRequest> handled, RecordingEgress egress) Drain(
            IEnumerable<EntityCreationRequest> requests,
            bool isDefaultProcessor = false)
        {
            var local = new ScenarioEntityCreationRequestSource();
            foreach (var r in requests) local.Enqueue(r);

            var egress = new RecordingEgress();
            var sut    = new ForwardingEntityCreationRequestSource(
                local, egress, LocalNodeId, isDefaultProcessor);

            var handled = new List<EntityCreationRequest>();
            sut.ProcessRequests(handled.Add);
            return (handled, egress);
        }

        /// <summary>
        /// ⭐⭐ <b>owner == me ⇒ handled locally, and NOTHING is forwarded.</b>
        /// 📌 This is the half that produced the double spawn: the old translator forwarded even when the
        /// node was building the entity itself.
        /// </summary>
        [Fact]
        public void ARequestOwnedByThisNode_IsHandledLocally_AndNotForwarded()
        {
            var (handled, egress) = Drain(new[] { Request(LocalNodeId) });

            Assert.Single(handled);
            Assert.Empty(egress.Sent);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>owner == someone else ⇒ forwarded, and NOT handled locally.</b>
        /// 📌 The half that could not work at all before: the Level-1 guard returned early, so no order
        /// was published and the old translator had nothing to read. The request reached nobody.
        /// </summary>
        [Fact]
        public void ARequestOwnedByAnotherNode_IsForwarded_AndNotHandledLocally()
        {
            var (handled, egress) = Drain(new[] { Request(RemoteNodeId) });

            Assert.Empty(handled);
            var sent = Assert.Single(egress.Sent);
            Assert.Equal(RemoteNodeId, sent.OwnerAppInstanceId);
        }

        /// <summary>
        /// ⭐ <b>An untargeted request follows the default-processor tiebreaker.</b>
        ///
        /// <para>⚠ The non-arbiter arm is a deliberate capability GAIN, not merely preserved behaviour:
        /// today such a request is silently dropped by the Level-1 guard and <b>nothing is created
        /// anywhere</b>. Forwarding it to the arbiter is what the author asked for.</para>
        /// </summary>
        [Theory]
        [InlineData(true,  1, 0)]   // arbiter: services it itself
        [InlineData(false, 0, 1)]   // non-arbiter: forwards it to whoever is
        public void AnUntargetedRequest_FollowsTheDefaultProcessorTiebreaker(
            bool isDefaultProcessor, int expectedHandled, int expectedForwarded)
        {
            var (handled, egress) = Drain(new[] { Request(0) }, isDefaultProcessor);

            Assert.Equal(expectedHandled,   handled.Count);
            Assert.Equal(expectedForwarded, egress.Sent.Count);
        }

        /// <summary>
        /// 🔴🔴🔴 <b>THE BOUNCE HAZARD — a request that ARRIVED from the wire must never be re-forwarded.</b>
        ///
        /// <para>📐 <c>CompositeEntityCreationRequestSource</c> merges the local queue with the network
        /// source, and <c>CreateEntityRequestSystem</c> cannot tell them apart. ⛔ A forwarder placed on
        /// the MERGED stream would see a peer's request addressed to a third node and send it out again —
        /// an unbounded bounce between nodes, on the hot path.</para>
        ///
        /// <para>⭐⭐ The fix is structural, not a flag: the forwarder wraps the LOCAL source only, so a
        /// wire-originated request is unreachable to it. ⚠ This rail encodes the COMPOSITION that makes
        /// that true — network source OUTSIDE the wrap, beside it in the composite — because the defect
        /// is not in the forwarder's logic but in where someone might attach it.</para>
        /// </summary>
        [Fact]
        public void ARequestArrivingFromTheWire_IsNeverReForwarded()
        {
            var local   = new ScenarioEntityCreationRequestSource();
            var network = new ScenarioEntityCreationRequestSource();   // stands in for the DDS ingress
            var egress  = new RecordingEgress();

            // ⭐ A peer's request for a THIRD node — the exact shape that would bounce.
            network.Enqueue(Request(RemoteNodeId));

            var composed = new CompositeEntityCreationRequestSource(new IEntityCreationRequestSource[]
            {
                new ForwardingEntityCreationRequestSource(local, egress, LocalNodeId),
                network,   // ⛔ OUTSIDE the wrap — this is the load-bearing detail
            });

            var handled = new List<EntityCreationRequest>();
            composed.ProcessRequests(handled.Add);

            Assert.Empty(egress.Sent);   // 🔴 the bounce
            // ⭐ It still reaches the request system, whose own guard ignores it — one rule, one place.
            Assert.Single(handled);
            Assert.False(EntityCreationRouting.IsHandledLocally(handled[0], LocalNodeId, isDefaultProcessor: false));
        }

        /// <summary>
        /// ⚠⚠ <b>ONE routing rule, not two.</b>
        ///
        /// <para>📌 The forwarder and <c>CreateEntityRequestSystem</c> must agree exactly: if they ever
        /// disagreed, a request would be serviced twice (both act) or by nobody (neither does). ⭐ The
        /// guard was extracted into <see cref="EntityCreationRouting"/> for that reason, so this rail
        /// checks the system still routes through it rather than re-implementing the test inline.</para>
        /// </summary>
        [Fact]
        public void TheRequestSystemUsesTheSharedRoutingRule_NotItsOwnCopy()
        {
            var code = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(
                    "Hrot/Engine/Hrot.Common/Systems/CreateEntityRequestSystem.cs"));

            Assert.Contains("EntityCreationRouting.IsHandledLocally", code);
            Assert.DoesNotContain("isTargetedAtMe", code);
        }
    }
}
