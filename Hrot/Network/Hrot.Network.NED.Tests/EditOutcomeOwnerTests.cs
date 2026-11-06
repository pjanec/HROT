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
        public readonly NetworkEntityMap Map;

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
            Map = map;
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
    // ── ⭐ edit framework F1 (docs/UX/DESIGN_Edit_Framework.md §2.2, §4 F1): Edits.Submit end to end, a non-owner and the owner ──────────────────────

    /// <summary>The requester's commands as the egress writes them to the wire.</summary>
    private static UpdateEntityAttributeRequest SentBy(Node requester)
    {
        requester.World.Bus.SwapBuffers();
        var writer = new CapturingRequestWriter();
        new Hrot.Map.Common.Replication.Egress.UpdateEntityAttributeCommandEgressTranslator(writer).ScanAndPublish(requester.World);
        return Assert.Single(writer.Written);
    }

    private sealed class CapturingRequestWriter : Hrot.Map.Common.Dds.IDdsWriter<UpdateEntityAttributeRequest>
    {
        public readonly List<UpdateEntityAttributeRequest> Written = new();
        public void Write(UpdateEntityAttributeRequest sample) => Written.Add(sample);
        public void DisposeInstance(UpdateEntityAttributeRequest key) { }
    }

    /// <summary>The owner's answer back on the requester (egress → wire → ingress), then the requester's journal.</summary>
    private static EditJournalEntry AnswerBack(Node owner, Node requester, Guid id)
    {
        var writer = new CapturingWriter();
        new EditOutcomeEgressTranslator(writer).ScanAndPublish(owner.World);
        var ingress = new EditOutcomeIngressTranslator(null);
        foreach (var w in writer.Written) ingress.ProcessSample(in w, requester.World);
        var journal = EditJournal.Of(requester.World);
        journal.Refresh(requester.World);
        return journal.Find(id)!;
    }

    /// <summary>⭐ CE-4274 — a RENAME submitted on a station that does not own the unit reaches the owner, is applied THERE (never to the replica), and the
    /// requester's journal reads Applied. ✅ Red-proof: the modal's old path (a local write) leaves the owner's name unchanged ⇒ red.</summary>
    [Fact]
    public void F1_ARenameOnANonOwner_IsAppliedByTheOwner_AndTheJournalReadsApplied()
    {
        var owner = new Node(owns: true, OwnerNode);
        var requester = new Node(owns: false, OtherNode);
        owner.World.AddComponent(owner.Entity, new EntityInfo { Name = new FixedString64("Alpha") });
        owner.World.SetAuthority<EntityInfo>(owner.Entity, true);   // the owner owns its descriptor (the Name handler is gated on CanWrite<EntityInfo>)
        requester.World.AddComponent(requester.Entity, new EntityInfo { Name = new FixedString64("Alpha") });

        var ticket = Edits.Submit(requester.World, EditOp.Write(requester.Entity,
            new Fdp.Toolkit.Replication.Patching.EntityAttributeChange
            {
                AttributeId = Fdp.Toolkit.Replication.Patching.AttributeIds.Name,
                Value = Fdp.Toolkit.Replication.Patching.AttributeValue.FromString("Bravo"),
            }, "Rename to Bravo"));
        Assert.Equal(Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Requested, ticket.Route);
        Assert.Equal("Alpha", requester.World.GetComponent<EntityInfo>(requester.Entity).Name.ToString());   // the replica is NOT written
        Assert.Equal(EditStatus.Pending, EditJournal.Of(requester.World).Find(ticket.RequestId)!.Status);

        var req = SentBy(requester);
        Assert.Equal((ticket.RequestId, true), (req.RequestId, req.WantOutcome));
        owner.Run(req);
        Assert.Equal("Bravo", owner.World.GetComponent<EntityInfo>(owner.Entity).Name.ToString());

        var entry = AnswerBack(owner, requester, ticket.RequestId);
        Assert.Equal((EditStatus.Applied, "Rename to Bravo"), (entry.Status, entry.Label));
    }

    /// <summary>⭐ CE-4275 — an ORBAT re-parent on a non-owner goes to the owner as "CommanderId": the OWNER publishes the assignment and answers Applied; the
    /// requester publishes NO local assignment (which wrote the replica). ✅ Red-proof: the adapter's old local CmdAssignSubordinate ⇒ the requester's bus holds one ⇒ red.</summary>
    [Fact]
    public void F1_AnOrbatAssignOnANonOwner_IsAppliedByTheOwner()
    {
        const int CmdNet = 4712;
        var owner = new Node(owns: true, OwnerNode);
        var requester = new Node(owns: false, OtherNode);
        Entity Commander(Node n)
        {
            var c = n.World.CreateEntity();
            n.World.AddComponent(c, new NetworkIdentity { Value = CmdNet });
            n.Map.Register(CmdNet, c);
            return c;
        }
        var ownerCmd = Commander(owner);
        var reqCmd = Commander(requester);

        var ticket = Edits.Submit(requester.World, EditOp.SetCommander(requester.Entity, reqCmd, "Assign to a commander"));
        Assert.Equal(Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Requested, ticket.Route);
        var req = SentBy(requester);
        Assert.Empty(requester.World.Bus.Read<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>().ToArray());
        Assert.Contains("\"CommanderId\":4712", req.AttributePatchJson);

        owner.Run(req);
        var assigned = Assert.Single(owner.World.Bus.Read<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>().ToArray());
        Assert.Equal((owner.Entity, ownerCmd), (assigned.Subordinate, assigned.Commander));
        Assert.Equal(EditStatus.Applied, AnswerBack(owner, requester, ticket.RequestId).Status);
    }

    /// <summary>⭐ The gate — a replaying world refuses every edit on every host (Review mode), and an installed gate refuses with ITS reason; nothing is sent
    /// either way and the journal holds the reason. ✅ Red-proof: the ReplayState check removed ⇒ the first edit is Requested ⇒ red.</summary>
    [Fact]
    public void F1_AReviewOrObserverEdit_IsRefusedWithTheReason_AndNothingIsSent()
    {
        var requester = new Node(owns: false, OtherNode);
        var op = EditOp.WritePatch(requester.Entity, "{\"Heading\":90}", "Turn");

        Fdp.Toolkit.Replay.ReplayState.Enter(requester.World);
        var review = Edits.Submit(requester.World, op);
        Fdp.Toolkit.Replay.ReplayState.Leave(requester.World);
        Assert.Equal(Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Refused, review.Route);
        var r = EditJournal.Of(requester.World).Find(review.RequestId)!;
        Assert.Equal((EditStatus.Refused, Edits.ReplayRefusal, "Review"), (r.Status, r.Reason, r.Mode));

        Hrot.Common.Editing.HrotEditGate.Install(requester.World, () => Hrot.Common.Editing.EditingMode.Live, () => Hrot.Common.Editing.EditingLevel.Observer);
        var observer = Edits.Submit(requester.World, op);
        var o = EditJournal.Of(requester.World).Find(observer.RequestId)!;
        Assert.Equal((EditStatus.Refused, "this station is an observer", "Live"), (o.Status, o.Reason, o.Mode));
        Assert.True(EditOutcomeLog.Of(requester.World).TryGet(observer.RequestId, out var logged) && logged.Status == EditStatus.Refused);

        requester.World.Bus.SwapBuffers();
        Assert.Empty(requester.World.Bus.ReadManaged<Fdp.Toolkit.Replication.Events.UpdateEntityAttributeCommand>());
    }

    /// <summary>An edit the owner never answers becomes Lost after the time-out (the journal's local status). ✅ Red-proof: the Lost branch removed ⇒ red.</summary>
    [Fact]
    public void F1_AnUnansweredEdit_IsLost_AfterTheTimeOut()
    {
        var requester = new Node(owns: false, OtherNode);
        var t = Edits.Submit(requester.World, EditOp.WritePatch(requester.Entity, "{\"Heading\":90}", "Turn"));
        var journal = EditJournal.Of(requester.World);
        journal.Refresh(EditOutcomeLog.Of(requester.World), DateTime.UtcNow);
        Assert.Equal(EditStatus.Pending, journal.Find(t.RequestId)!.Status);
        journal.Refresh(EditOutcomeLog.Of(requester.World), DateTime.UtcNow + EditJournal.LostAfter + TimeSpan.FromSeconds(1));
        Assert.Equal(EditStatus.Lost, journal.Find(t.RequestId)!.Status);
    }
}
