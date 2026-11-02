using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Navigation;
using Hrot.Network.NED.SimHost;
using Xunit;

namespace Hrot.Network.NED.Tests
{
    /// <summary>
    /// ⭐ Macros S3a ⑤ (docs/DESIGN_Scenario_Macros.md §14.2 SR-180, R-134) — the placement query's scale-out wire without DDS: the request and the response
    /// each cross encode → decode (PathfindingTranslatorsTests' shape). 🔴 Mutation: drop a field copy in a <c>ToWire</c> / <c>FromWire</c> ⇒ red.
    /// </summary>
    public sealed class PlacementTranslatorsTests
    {
        [Fact]
        public void TheRequest_CrossesTheWire_FieldForField()
        {
            var sent = new PlacementRequestBatch { RequestId = 77, SourceNodeId = (Fdp.Core.NodeId)(5), MaxShift = 12.5f };
            sent.Slots.Add(new PlacementSlot { Key = 1, Point = new Vector3(100f, 200f, 3f), Layer = PlacementLayer.Vehicle, Radius = 2.5f });
            sent.Slots.Add(new PlacementSlot { Key = 2, Point = new Vector3(104f, 197f, 3.5f), Layer = PlacementLayer.Air, Radius = 0.5f });

            var wire = PlacementRequestBrainEgressTranslator.ToWire(sent);
            var got = PlacementRequestSolverIngressTranslator.FromWire(in wire);

            Assert.Equal(77, got.RequestId);
            Assert.Equal(5, (int)got.SourceNodeId);
            Assert.Equal(12.5f, got.MaxShift);
            Assert.Equal(2, got.Slots.Count);
            for (int i = 0; i < 2; i++)
            {
                Assert.Equal(sent.Slots[i].Key, got.Slots[i].Key);
                Assert.Equal(sent.Slots[i].Layer, got.Slots[i].Layer);
                Assert.Equal(sent.Slots[i].Radius, got.Slots[i].Radius);
                Assert.True(Vector3.Distance(sent.Slots[i].Point, got.Slots[i].Point) < 1e-3f, $"{sent.Slots[i].Point} vs {got.Slots[i].Point}");
            }
        }

        [Fact]
        public void TheResponse_CrossesTheWire_FieldForField_AndIsAddressedToTheAskingNode()
        {
            var sent = new PlacementResponseBatch { RequestId = 77, SourceNodeId = (Fdp.Core.NodeId)(5) };
            sent.Answers.Add(new PlacementAnswer { Key = 1, Outcome = PlacementOutcome.Shifted, Point = new Vector3(101f, 198f, 3f), Reason = string.Empty });
            sent.Answers.Add(new PlacementAnswer { Key = 2, Outcome = PlacementOutcome.Failed, Point = new Vector3(110f, 190f, 0f), Reason = "no dry ground within 30 m" });

            var wire = PlacementResponseSolverEgressTranslator.ToWire(sent);
            Assert.Equal(5, wire.TargetNodeId);
            var got = PlacementResponseBrainIngressTranslator.FromWire(in wire);

            Assert.Equal(77, got.RequestId);
            Assert.Equal(5, (int)got.SourceNodeId);
            Assert.Equal(sent.Answers.Select(a => (a.Key, a.Outcome, a.Reason)), got.Answers.Select(a => (a.Key, a.Outcome, a.Reason)));
            for (int i = 0; i < 2; i++)
                Assert.True(Vector3.Distance(sent.Answers[i].Point, got.Answers[i].Point) < 1e-3f);
        }

        [Fact]
        public void TheOrdinals_AreTheMeasuredFreeOnes()
        {
            Assert.Equal(171, (int)Hrot.NED.Descriptors.EDescriptorType.dtPlacementRequestBatch);
            Assert.Equal(172, (int)Hrot.NED.Descriptors.EDescriptorType.dtPlacementResponseBatch);
        }
    }
}
