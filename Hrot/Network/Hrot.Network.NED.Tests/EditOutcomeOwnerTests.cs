using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Systems;
using Hrot.Network.Translators;
using Hrot.NED.Messages;
using Xunit;
using NodeId = Hrot.NED.Common.NodeId;

namespace Hrot.Network.NED.Tests;

/// <summary>
/// ⭐ CE-2380 part 1 (R-2082, 📄 docs/DESIGN_Remote_Edit_Outcome.md §2/§4 A-B) — the OWNER's answer in <see cref="UpdateEntityAttributeRequestSystem"/> and the wire pair
/// that carries it. ⛔ The existing ack (<c>RequireAck</c>) is untouched: <c>UpdateEntityAttributeCommanderIdTests</c> and the ack tests stay as they were.
/// </summary>
public sealed class EditOutcomeOwnerTests
{
    private const int NetId = 4711;
    private const int OwnerNode = 3, OtherNode = 5;

    private sealed class QueuedRequestSource : IUpdateEntityAttributeRequestSource
    {
        private readonly Queue<UpdateEntityAttributeRequest> _queue = new();
        public void Enqueue(UpdateEntityAttributeRequest r) => _queue.Enqueue(r);
        public void ProcessRequests(Action<UpdateEntityAttributeRequest> processor) { while (_queue.TryDequeue(out var r)) processor(r); }
    }

    private sealed class CountingAckSink : IUpdateEntityAttributeAckSink
    {
        public int Acks;
        public void WriteAck(Guid requestId, int errorCode, NodeId respondingNode, ReadOnlySpan<byte> opaqueData) => Acks++;
        public void WriteErrorAck(Guid requestId, int errorCode) => Acks++;
    }

    private sealed class Node
    {
        public readonly EntityRepository World = new();
        public readonly Entity Entity;
        public readonly QueuedRequestSource Source = new();
        public readonly CountingAckSink Acks = new();
        private readonly UpdateEntityAttributeRequestSystem _system;

        /// <param name="owns">true = this node owns the entity (primary owner, SimTransform authoritative); false = a bystander.</param>
        public Node(bool owns, int localNode)
        {
            Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(World);
            var geo = new WGS84Transform();
            geo.SetOrigin(52.52, 13.405, 0.0);
            World.SetSingletonManaged<IGeographicTransform>(geo);

            Entity = World.CreateEntity();
            World.AddComponent(Entity, new SimTransform());
            World.AddComponent(Entity, new NetworkIdentity { Value = NetId });
            World.AddComponent(Entity, new NetworkAuthority(primaryOwnerId: owns ? localNode : OwnerNode, localNodeId: localNode));
            World.SetAuthority<SimTransform>(Entity, owns);
            var map = new NetworkEntityMap();
            map.Register(NetId, Entity);
            _system = new UpdateEntityAttributeRequestSystem(Source, Acks, map);
        }

        public EditOutcome[] Run(UpdateEntityAttributeRequest req)
        {
            Source.Enqueue(req);
            _system.Execute(World, 0f);
            World.Bus.SwapBuffers();
            return World.Bus.ReadManaged<EditOutcome>().ToArray();
        }
    }

    private static UpdateEntityAttributeRequest Req(string json, bool wantOutcome, bool requireAck = false)
        => new() { RequestId = Guid.NewGuid(), EntityId = NetId, AttributePatchJson = json, WantOutcome = wantOutcome, RequireAck = requireAck };

    /// <summary>The owner applies the patch ⇒ it answers Applied under the request's id, in its own log too. ✅ Red-proof: the Applied report removed ⇒ red.</summary>
    [Fact]
    public void TheOwnerAnswersApplied()
    {
        var owner = new Node(owns: true, OwnerNode);
        var req = Req("{\"Heading\":90}", wantOutcome: true);

        var outcomes = owner.Run(req);

        var o = Assert.Single(outcomes);
        Assert.Equal((req.RequestId, NetId, EditStatus.Applied, OwnerNode, false), (o.RequestId, o.NetworkId, o.Status, o.RespondingNode, o.FromNetwork));
        Assert.True(EditOutcomeLog.Of(owner.World).TryGet(req.RequestId, out var logged));
        Assert.Equal(EditStatus.Applied, logged.Status);
    }

    /// <summary>The owner with NOTHING applied refuses, and the reason names what was not applied. ✅ Red-proof: the owner-refusal arm removed ⇒ red.</summary>
    [Fact]
    public void TheOwnerRefusesWhatItCouldNotApply_AndSaysWhat()
    {
        var owner = new Node(owns: true, OwnerNode);

        var o = Assert.Single(owner.Run(Req("{\"NoSuchAttribute\":1}", wantOutcome: true)));

        Assert.Equal(EditStatus.Refused, o.Status);
        Assert.Contains("NoSuchAttribute", o.Reason);
    }

    /// <summary>A bystander (every node receives the request) applies nothing and stays SILENT (lean B). ✅ Red-proof: the owner test dropped from the refusal arm ⇒ red.</summary>
    [Fact]
    public void ABystanderIsSilent()
    {
        var bystander = new Node(owns: false, OtherNode);

        Assert.Empty(bystander.Run(Req("{\"Heading\":90}", wantOutcome: true)));
        Assert.False(EditOutcomeLog.Of(bystander.World).TryGet(Guid.Empty, out _));
        Assert.Equal(0, EditOutcomeLog.Of(bystander.World).Count);
    }

    /// <summary>No WantOutcome ⇒ no outcome (lean A), whoever owns; and the ack path is unchanged: RequireAck still acks the owner's apply.</summary>
    [Fact]
    public void WithoutWantOutcomeNothingIsReported_AndTheAckIsAsBefore()
    {
        var owner = new Node(owns: true, OwnerNode);

        Assert.Empty(owner.Run(Req("{\"Heading\":90}", wantOutcome: false, requireAck: true)));
        Assert.Equal(1, owner.Acks.Acks);
        Assert.Equal(0, EditOutcomeLog.Of(owner.World).Count);
        Assert.Empty(owner.Run(Req("{\"NoSuchAttribute\":1}", wantOutcome: false)));
    }

    /// <summary>An outcome asked for does not change the ack: with both flags the owner sends its ack AND reports the outcome.</summary>
    [Fact]
    public void BothFlags_AckAndOutcome()
    {
        var owner = new Node(owns: true, OwnerNode);
        Assert.Single(owner.Run(Req("{\"Heading\":90}", wantOutcome: true, requireAck: true)));
        Assert.Equal(1, owner.Acks.Acks);
    }

    // ── the wire pair ──────────────────────────────────────────────────────────

    private sealed class CapturingWriter : Hrot.Map.Common.Dds.IDdsWriter<EntityEditOutcome>
    {
        public readonly List<EntityEditOutcome> Written = new();
        public void Write(EntityEditOutcome sample) => Written.Add(sample);
        public void DisposeInstance(EntityEditOutcome key) { }
    }

    /// <summary>CE-2380 part 2 — the ingress dedupes against the LOG, not a node id (some nodes fall back to id 0): the same answer arriving twice is logged once, and another node's answer is logged even when its id is 0.
    /// ✅ Red-proof: the dedupe removed ⇒ the first assert reddens; the dedupe keyed on the responder alone ⇒ the 0-id answer is dropped ⇒ red.</summary>
    [Fact]
    public void TheIngressDedupesAgainstTheLog_NotAgainstANodeId()
    {
        var node = new Node(owns: false, OtherNode);
        var id = Guid.NewGuid();
        var ingress = new EditOutcomeIngressTranslator(null);
        var own = new EntityEditOutcome { RequestId = id, EntityId = NetId, Status = (int)EditStatus.Applied, RespondingNode = 0, Reason = "" };

        EditOutcomes.Report(node.World, id, NetId, EditStatus.Applied, "", respondingNode: 0);   // this node's own answer (id 0), logged locally
        Assert.False(ingress.ProcessSample(in own, node.World));                                // …and coming back: not doubled
        Assert.Equal(1, EditOutcomeLog.Of(node.World).Count);

        var other = own; other.RespondingNode = 9;                                              // another node's answer
        Assert.True(ingress.ProcessSample(in other, node.World));
        var zero = new EntityEditOutcome { RequestId = id, EntityId = NetId, Status = (int)EditStatus.Pending, RespondingNode = 0, Reason = "" };   // a different status from a 0-id node
        Assert.True(ingress.ProcessSample(in zero, node.World));
        Assert.Equal(3, EditOutcomeLog.Of(node.World).Count);
    }

    /// <summary>Egress → wire → ingress: the owner's outcome reaches ANOTHER node's log and bus (FromNetwork); the sender's own node ignores its sample; an arrived outcome is never re-sent.
    /// ✅ Red-proof: the ingress's log Add removed ⇒ red.</summary>
    [Fact]
    public void AnOutcomeCrossesTheWire_IntoTheRequestersLog()
    {
        var owner = new Node(owns: true, OwnerNode);
        var requester = new Node(owns: false, OtherNode);
        var req = Req("{\"Heading\":90}", wantOutcome: true);
        owner.Run(req);   // Run swaps the bus, so the owner's EditOutcome event is readable now

        var writer = new CapturingWriter();
        var egress = new EditOutcomeEgressTranslator(writer);
        egress.ScanAndPublish(owner.World);
        var wire = Assert.Single(writer.Written);
        Assert.Equal((req.RequestId, NetId, (int)EditStatus.Applied, OwnerNode), (wire.RequestId, wire.EntityId, wire.Status, wire.RespondingNode));

        var ingress = new EditOutcomeIngressTranslator(null);
        Assert.True(ingress.ProcessSample(in wire, requester.World));
        Assert.True(EditOutcomeLog.Of(requester.World).TryGet(req.RequestId, out var arrived));
        Assert.Equal((EditStatus.Applied, true), (arrived.Status, arrived.FromNetwork));

        // the owner's own node skips its own sample (the log already holds that request / status / responder)
        Assert.False(ingress.ProcessSample(in wire, owner.World));
        Assert.Equal(1, EditOutcomeLog.Of(owner.World).Count);

        // an arrived outcome is not sent again by the requester's egress
        requester.World.Bus.SwapBuffers();
        var again = new CapturingWriter();
        new EditOutcomeEgressTranslator(again).ScanAndPublish(requester.World);
        Assert.Empty(again.Written);
    }
}
