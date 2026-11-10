using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Common.Systems;
using Hrot.Network.Translators;
using Hrot.NED.Messages;
using Xunit;
using NodeId = Hrot.NED.Common.NodeId;

namespace Hrot.Network.NED.Tests;

/// <summary>
/// ⭐ CE-2380 part 1 (R-2082, 📄 docs/DESIGN_Remote_Edit_Outcome.md §2/§4 A-B) — the OWNER's answer in <see cref="UpdateEntityAttributeRequestSystem"/> and the wire pair
/// that carries it. ⭐ Edit framework F2 (R-4126): the old ack (<c>RequireAck</c> → <c>CreateUpdateDeleteEntityAck</c>) is retired — this is the one answer.
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

    private sealed class Node
    {
        public readonly EntityRepository World = new();
        public readonly Entity Entity;
        public readonly QueuedRequestSource Source = new();
        private readonly UpdateEntityAttributeRequestSystem _system;

        /// <param name="owns">true = this node owns the entity (primary owner, SimTransform authoritative); false = a bystander.</param>
        public readonly NetworkEntityMap Map;

        public Node(bool owns, int localNode)
        {
            Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(World);
            var geo = UtmTransform.AtOrigin(52.52, 13.405, 0.0);
            World.SetSingletonManaged<IGeographicTransform>(geo);

            Entity = World.CreateEntity();
            World.AddComponent(Entity, new SimTransform());
            World.AddComponent(Entity, new NetworkIdentity { Value = NetId });
            World.AddComponent(Entity, new NetworkAuthority(primaryOwnerId: owns ? localNode : OwnerNode, localNodeId: localNode));
            World.SetAuthority<SimTransform>(Entity, owns);
            var map = new NetworkEntityMap();
            map.Register(NetId, Entity);
            Map = map;
            _system = new UpdateEntityAttributeRequestSystem(Source, map);
        }

        public EditOutcome[] Run(UpdateEntityAttributeRequest req)
        {
            Source.Enqueue(req);
            _system.Execute(World, 0f);
            World.Bus.SwapBuffers();
            return World.Bus.ReadManaged<EditOutcome>().ToArray();
        }
    }

    private static UpdateEntityAttributeRequest Req(string json, bool wantOutcome)
        => new() { RequestId = Guid.NewGuid(), EntityId = NetId, AttributePatchJson = json, WantOutcome = wantOutcome };

    /// <summary>The owner applies the patch ⇒ it answers Applied under the request's id, in its own log too. ✅ Red-proof: the Applied report removed ⇒ red.</summary>
    [Fact]
    public void TheOwnerAnswersApplied()
    {
        var owner = new Node(owns: true, OwnerNode);
        var req = Req("{\"Heading\":90}", wantOutcome: true);

        var outcomes = owner.Run(req);

        var o = Assert.Single(outcomes);
        Assert.Equal((req.RequestId, NetId, EditStatus.Applied, OwnerNode, false), (o.RequestId, o.NetworkId, o.Status, (int)o.RespondingNode, o.FromNetwork));
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

    /// <summary>No WantOutcome ⇒ no outcome (lean A), whoever owns.</summary>
    [Fact]
    public void WithoutWantOutcomeNothingIsReported()
    {
        var owner = new Node(owns: true, OwnerNode);

        Assert.Empty(owner.Run(Req("{\"Heading\":90}", wantOutcome: false)));
        Assert.Equal(0, EditOutcomeLog.Of(owner.World).Count);
        Assert.Empty(owner.Run(Req("{\"NoSuchAttribute\":1}", wantOutcome: false)));
    }

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

    // ── ⭐ B-4 / Details forms D2 (docs/UX/DESIGN_Details_Forms.md §4.1): each form attribute, written from a NON-OWNER, reaches the owner and is applied THERE ──────────

    private static void Give<T>(Node n, T value, bool owned) where T : unmanaged
    {
        if (!n.World.IsComponentTypeRegistered<T>()) n.World.RegisterComponent<T>();
        n.World.AddComponent(n.Entity, value);
        if (owned) n.World.SetAuthority<T>(n.Entity, true);   // the form-attribute handlers gate on CanWrite<T>: only the owner applies
    }

    /// <summary>Submits <paramref name="change"/> on the requester, carries it to the owner, returns the requester's journal entry. Asserts it was REQUESTED, not written.</summary>
    private static EditJournalEntry FromANonOwner(Node owner, Node requester, Fdp.Toolkit.Replication.Patching.EntityAttributeChange change, string label)
    {
        var ticket = Edits.Submit(requester.World, EditOp.Write(requester.Entity, change, label));
        Assert.Equal(Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Requested, ticket.Route);
        var req = SentBy(requester);
        Assert.Equal((ticket.RequestId, true), (req.RequestId, req.WantOutcome));
        owner.Run(req);
        return AnswerBack(owner, requester, ticket.RequestId);
    }

    private static Fdp.Toolkit.Replication.Patching.EntityAttributeChange Change(ushort id, Fdp.Toolkit.Replication.Patching.AttributeValue v, short sub = 0)
        => new() { AttributeId = id, SubIndex1 = sub, Value = v };

    private static (Node Owner, Node Requester) Pair<T>(T value) where T : unmanaged
    {
        var owner = new Node(owns: true, OwnerNode);
        var requester = new Node(owns: false, OtherNode);
        Give(owner, value, owned: true);
        Give(requester, value, owned: false);
        return (owner, requester);
    }

    [Fact]
    public void D2_HealthCurrent_OnANonOwner_IsAppliedByTheOwner_AndClamped()
    {
        var (owner, requester) = Pair(new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
        var entry = FromANonOwner(owner, requester, Change(Fdp.Toolkit.Replication.Patching.AttributeIds.HealthCurrent, Fdp.Toolkit.Replication.Patching.AttributeValue.FromDouble(40)), "Set the strength");
        Assert.Equal(EditStatus.Applied, entry.Status);
        Assert.Equal(40f, owner.World.GetComponent<Fdp.Toolkit.Combat.Components.Health>(owner.Entity).Current);
        Assert.Equal(100f, requester.World.GetComponent<Fdp.Toolkit.Combat.Components.Health>(requester.Entity).Current);   // the replica is not written
        FromANonOwner(owner, requester, Change(Fdp.Toolkit.Replication.Patching.AttributeIds.HealthCurrent, Fdp.Toolkit.Replication.Patching.AttributeValue.FromDouble(500)), "Over the top");
        Assert.Equal(100f, owner.World.GetComponent<Fdp.Toolkit.Combat.Components.Health>(owner.Entity).Current);   // clamped to Max
    }

    [Fact]
    public void D2_PlayableSideAndSlotName_OnANonOwner_AreAppliedByTheOwner()
    {
        var (owner, requester) = Pair(new Hrot.Common.Playable { Side = ForceId.Friend, SlotName = new FixedString32("Old") });
        Assert.Equal(EditStatus.Applied, FromANonOwner(owner, requester, Change(Fdp.Toolkit.Replication.Patching.AttributeIds.PlayableSide, Fdp.Toolkit.Replication.Patching.AttributeValue.FromString("FORCE_OPPOSING")), "Side").Status);
        Assert.Equal(EditStatus.Applied, FromANonOwner(owner, requester, Change(Fdp.Toolkit.Replication.Patching.AttributeIds.PlayableSlotName, Fdp.Toolkit.Replication.Patching.AttributeValue.FromString("Alpha 1")), "Slot").Status);
        var p = owner.World.GetComponent<Hrot.Common.Playable>(owner.Entity);
        Assert.Equal((ForceId.Hostile, "Alpha 1"), (p.Side, p.SlotName.ToString()));
    }

    [Fact]
    public void D2_SpawnAreaSideAndFacing_OnANonOwner_AreAppliedByTheOwner()
    {
        var (owner, requester) = Pair(new Hrot.Common.SpawnArea { Side = ForceId.Friend, FacingDeg = 0f });
        Assert.Equal(EditStatus.Applied, FromANonOwner(owner, requester, Change(Fdp.Toolkit.Replication.Patching.AttributeIds.SpawnAreaSide, Fdp.Toolkit.Replication.Patching.AttributeValue.FromInt(2)), "Side").Status);   // the ordinal shape
        Assert.Equal(EditStatus.Applied, FromANonOwner(owner, requester, Change(Fdp.Toolkit.Replication.Patching.AttributeIds.SpawnAreaFacingDeg, Fdp.Toolkit.Replication.Patching.AttributeValue.FromDouble(135)), "Facing").Status);
        var a = owner.World.GetComponent<Hrot.Common.SpawnArea>(owner.Entity);
        Assert.Equal((ForceId.Hostile, 135f), (a.Side, a.FacingDeg));
    }

    /// <summary>Mount 0 is the unit's own WeaponState; mount 2 is a CHILD entity found by WeaponMountInfo.MountIndex — both reach the owner as ONE attribute with the mount as the sub-index; clamped to MaxAmmo.</summary>
    [Fact]
    public void D2_Ammo_OnANonOwner_IsAppliedByTheOwner_OnTheUnitAndOnAMountChild()
    {
        var (owner, requester) = Pair(new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 3, MaxAmmo = 30 });
        if (!owner.World.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.WeaponMountInfo>()) owner.World.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponMountInfo>();
        if (!owner.World.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.PartMetadata>()) owner.World.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
        var child = owner.World.CreateEntity();
        owner.World.AddComponent(child, new Fdp.Toolkit.Combat.Components.WeaponMountInfo { MountIndex = 2 });
        owner.World.AddComponent(child, new Fdp.Toolkit.Replication.Components.PartMetadata { ParentEntity = owner.Entity });
        owner.World.AddComponent(child, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 1, MaxAmmo = 10 });
        owner.World.SetAuthority<Fdp.Toolkit.Combat.Components.WeaponState>(child, true);

        Assert.Equal(EditStatus.Applied, FromANonOwner(owner, requester, Change(Fdp.Toolkit.Replication.Patching.AttributeIds.Ammo, Fdp.Toolkit.Replication.Patching.AttributeValue.FromInt(12), 0), "Mount 0").Status);
        Assert.Equal(12, owner.World.GetComponent<Fdp.Toolkit.Combat.Components.WeaponState>(owner.Entity).Ammo);
        Assert.Equal(3, requester.World.GetComponent<Fdp.Toolkit.Combat.Components.WeaponState>(requester.Entity).Ammo);   // the replica is not written

        Assert.Equal(EditStatus.Applied, FromANonOwner(owner, requester, Change(Fdp.Toolkit.Replication.Patching.AttributeIds.Ammo, Fdp.Toolkit.Replication.Patching.AttributeValue.FromInt(7), 2), "Mount 2").Status);
        Assert.Equal(7, owner.World.GetComponent<Fdp.Toolkit.Combat.Components.WeaponState>(child).Ammo);
        Assert.Equal(12, owner.World.GetComponent<Fdp.Toolkit.Combat.Components.WeaponState>(owner.Entity).Ammo);          // the unit's own mount is untouched

        FromANonOwner(owner, requester, Change(Fdp.Toolkit.Replication.Patching.AttributeIds.Ammo, Fdp.Toolkit.Replication.Patching.AttributeValue.FromInt(99), 2), "Too many");
        Assert.Equal(10, owner.World.GetComponent<Fdp.Toolkit.Combat.Components.WeaponState>(child).Ammo);                  // clamped to that mount's MaxAmmo

        Assert.Equal(EditStatus.Refused, FromANonOwner(owner, requester, Change(Fdp.Toolkit.Replication.Patching.AttributeIds.Ammo, Fdp.Toolkit.Replication.Patching.AttributeValue.FromInt(1), 3), "No such mount").Status);
    }

    [Fact]
    public void D2_Weather_OnANonOwner_IsAppliedByTheOwner()
    {
        var (owner, requester) = Pair(Fdp.Toolkit.Weather.WeatherConditions.Standard);
        Assert.Equal(EditStatus.Applied, FromANonOwner(owner, requester, Change(Fdp.Toolkit.Replication.Patching.AttributeIds.WeatherVisibilityM, Fdp.Toolkit.Replication.Patching.AttributeValue.FromDouble(4000)), "Visibility").Status);
        Assert.Equal(EditStatus.Applied, FromANonOwner(owner, requester, Change(Fdp.Toolkit.Replication.Patching.AttributeIds.WeatherPrecipitation, Fdp.Toolkit.Replication.Patching.AttributeValue.FromInt((int)Fdp.Toolkit.Weather.PrecipitationKind.Rain)), "Rain").Status);
        Assert.Equal(EditStatus.Applied, FromANonOwner(owner, requester, Change(Fdp.Toolkit.Replication.Patching.AttributeIds.WeatherRelativeHumidity, Fdp.Toolkit.Replication.Patching.AttributeValue.FromDouble(7)), "Humidity").Status);
        var w = owner.World.GetComponent<Fdp.Toolkit.Weather.WeatherConditions>(owner.Entity);
        Assert.Equal((4000f, (byte)2, 1f), (w.VisibilityM, w.Precipitation, w.RelativeHumidity));   // humidity clamped to 0..1
        Assert.Equal(20_000f, requester.World.GetComponent<Fdp.Toolkit.Weather.WeatherConditions>(requester.Entity).VisibilityM);   // the replica is not written
        var wind = Fdp.Toolkit.Weather.WeatherConditions.Standard;   // the all-ten set: each id reaches the right field
        foreach (var (id, value) in new (ushort, double)[] {
            (Fdp.Toolkit.Replication.Patching.AttributeIds.WeatherGroundTemperatureC, 3), (Fdp.Toolkit.Replication.Patching.AttributeIds.WeatherLapseRateKPerKm, 5), (Fdp.Toolkit.Replication.Patching.AttributeIds.WeatherSeaLevelPressureHpa, 990),
            (Fdp.Toolkit.Replication.Patching.AttributeIds.WeatherFogTopM, 250), (Fdp.Toolkit.Replication.Patching.AttributeIds.WeatherFogVisibilityM, 80), (Fdp.Toolkit.Replication.Patching.AttributeIds.WeatherPrecipitationMmPerHour, 6), (Fdp.Toolkit.Replication.Patching.AttributeIds.WeatherFeatherM, 700) })
            Assert.Equal(EditStatus.Applied, FromANonOwner(owner, requester, Change(id, Fdp.Toolkit.Replication.Patching.AttributeValue.FromDouble(value)), "w" + id).Status);
        w = owner.World.GetComponent<Fdp.Toolkit.Weather.WeatherConditions>(owner.Entity);
        Assert.Equal((3f, 5f, 990f, 250f, 80f, 6f, 700f), (w.GroundTemperatureC, w.LapseRateKPerKm, w.SeaLevelPressureHpa, w.FogTopM, w.FogVisibilityM, w.PrecipitationMmPerHour, w.FeatherM));
    }

    private static (Node Owner, Node Requester) StancePair(StanceId start = StanceId.Standing)
    {
        var (owner, requester) = Pair(new Hrot.MuscleCharacter.Animation.Components.StanceIntent { TargetStance = start, BlendTime = 0.3f });
        Give(owner, new Hrot.MuscleCharacter.Animation.Components.StanceStatus { CurrentStance = start }, owned: true);
        return (owner, requester);
    }

    /// <summary>⭐ Q7 — in a world whose time is NOT advancing (paused / Edit mode) the stance is set at once: intent AND status, acknowledged, so the body shows it and nothing plays.</summary>
    [Fact]
    public void D2_Stance_InAPausedWorld_SetsIntentAndStatusAtOnce()
    {
        var (owner, requester) = StancePair();
        Assert.Equal(EditStatus.Applied, FromANonOwner(owner, requester, Change(Fdp.Toolkit.Replication.Patching.AttributeIds.Stance, Fdp.Toolkit.Replication.Patching.AttributeValue.FromInt((int)StanceId.Prone)), "Prone").Status);
        var intent = owner.World.GetComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(owner.Entity);
        var status = owner.World.GetComponent<Hrot.MuscleCharacter.Animation.Components.StanceStatus>(owner.Entity);
        Assert.Equal((StanceId.Prone, StanceId.Prone, intent.Version), (intent.TargetStance, status.CurrentStance, status.AckVersion));
        Assert.Equal(Hrot.MuscleCharacter.Animation.Components.StanceTransitionPhase.Idle, status.Phase);
        Assert.Equal(StanceId.Standing, requester.World.GetComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(requester.Entity).TargetStance);   // the replica is not written
    }

    /// <summary>⭐ Q7 — while time advances it is an ordinary request (intent + new Version, with its blend): the status is left to StanceTransitionSystem.</summary>
    [Fact]
    public void D2_Stance_WhileTimeAdvances_IsAnOrdinaryRequest()
    {
        var (owner, requester) = StancePair();
        owner.World.SetSingleton(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1f });
        Assert.Equal(EditStatus.Applied, FromANonOwner(owner, requester, Change(Fdp.Toolkit.Replication.Patching.AttributeIds.Stance, Fdp.Toolkit.Replication.Patching.AttributeValue.FromString("Crouched")), "Crouch").Status);   // the name shape
        var intent = owner.World.GetComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(owner.Entity);
        var status = owner.World.GetComponent<Hrot.MuscleCharacter.Animation.Components.StanceStatus>(owner.Entity);
        Assert.Equal((StanceId.Crouched, 0.3f, 1u), (intent.TargetStance, (float)(intent.BlendTime), intent.Version));
        Assert.Equal((StanceId.Standing, 0u), (status.CurrentStance, status.AckVersion));   // the transition system will animate it
    }

    /// <summary>A stance the unit's TYPE does not declare is REFUSED (the owner reports it), and so is a token that is no stance at all.</summary>
    [Fact]
    public void D2_Stance_TheTypeDoesNotDeclare_IsRefused()
    {
        var (owner, requester) = StancePair();
        const long Tkb = 777;
        var db = new Fdp.Toolkit.Tkb.TkbDatabase();
        var t = new Fdp.Interfaces.TkbTemplate("Mannequin", Tkb);
        t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.CharacterAnimationDefDto
        {
            Slots = Array.Empty<Fdp.Toolkit.Tkb.Domain.SlotDefDto>(), Montages = Array.Empty<Fdp.Toolkit.Tkb.Domain.MontageDefDto>(),
            StanceTransitions = Array.Empty<Fdp.Toolkit.Tkb.Domain.StanceTransitionDto>(), NotifyMarkers = Array.Empty<Fdp.Toolkit.Tkb.Domain.NotifyMarkerDefDto>(),
            SupportedStances = new[] { StanceId.Standing, StanceId.Crouched },   // no Prone
        });
        db.Register(t);
        owner.World.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
        if (!owner.World.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()) owner.World.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
        owner.World.AddComponent(owner.Entity, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = (TkbTypeId)Tkb });

        Assert.Equal(EditStatus.Refused, FromANonOwner(owner, requester, Change(Fdp.Toolkit.Replication.Patching.AttributeIds.Stance, Fdp.Toolkit.Replication.Patching.AttributeValue.FromInt((int)StanceId.Prone)), "Prone").Status);
        Assert.Equal(StanceId.Standing, owner.World.GetComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(owner.Entity).TargetStance);
        Assert.Equal(EditStatus.Refused, FromANonOwner(owner, requester, Change(Fdp.Toolkit.Replication.Patching.AttributeIds.Stance, Fdp.Toolkit.Replication.Patching.AttributeValue.FromInt(9)), "No such stance").Status);
        Assert.Equal(EditStatus.Applied, FromANonOwner(owner, requester, Change(Fdp.Toolkit.Replication.Patching.AttributeIds.Stance, Fdp.Toolkit.Replication.Patching.AttributeValue.FromInt((int)StanceId.Crouched)), "Crouch").Status);   // a declared one is fine
    }

    // ── ⭐ edit framework F3 (docs/UX/DESIGN_Edit_Framework.md §3): add / remove a form component, owner-side ───────────────────────────────

    /// <summary>The Playable toggle on a non-owner: the request reaches the owner, which ADDS the component (and answers Applied); the replica is untouched;
    /// a remove goes the same way; a component the vocabulary does not target is refused with why. (Not mutation-proved: the owner's add is asserted directly.)</summary>
    [Fact]
    public void F3_ThePlayableToggleOnANonOwner_IsAppliedByTheOwner()
    {
        var owner = new Node(owns: true, OwnerNode);
        var requester = new Node(owns: false, OtherNode);

        var add = Edits.Submit(requester.World, EditOp.AddComponent(requester.Entity, typeof(Hrot.Common.Playable), "Make playable"));
        Assert.Equal(Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Requested, add.Route);
        Assert.False(requester.World.HasComponent<Hrot.Common.Playable>(requester.Entity));
        owner.Run(SentBy(requester));
        Assert.True(owner.World.HasComponent<Hrot.Common.Playable>(owner.Entity));
        Assert.Equal(EditStatus.Applied, AnswerBack(owner, requester, add.RequestId).Status);

        var remove = Edits.Submit(requester.World, EditOp.RemoveComponent(requester.Entity, typeof(Hrot.Common.Playable), "Make not playable"));
        owner.World.Bus.SwapBuffers();
        owner.Run(SentBy(requester));
        Assert.False(owner.World.HasComponent<Hrot.Common.Playable>(owner.Entity));
        Assert.Equal(EditStatus.Applied, AnswerBack(owner, requester, remove.RequestId).Status);

        // the owner itself, and a component that is not a form component
        var bad = Edits.Submit(owner.World, EditOp.AddComponent(owner.Entity, typeof(NetworkIdentity), "No"));
        Assert.Equal(Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Refused, bad.Route);
        Assert.True(EditOutcomeLog.Of(owner.World).TryGet(bad.RequestId, out var o) && o.Reason.Contains("cannot be added"));
    }

    /// <summary>⭐ Seats H1/H2 — the gate reads the SEAT's level: a Player seat refuses an edit with why but lets an interaction (possess, embark) through;
    /// an Instructor edits; the mapping is §2.2's table. ✅ Red-proof: the Interact exemption removed ⇒ the possess is refused ⇒ red.</summary>
    [Fact]
    public void H1_ThePlayerSeatRefusesEdits_ButNotInteractions()
    {
        var station = new Node(owns: false, OtherNode);
        var seat = Hrot.Common.Infrastructure.Seat.Player;
        Hrot.Common.Editing.HrotEditGate.Install(station.World, () => Hrot.Common.Editing.EditingMode.Live, () => Hrot.Common.Editing.HrotEditGate.LevelOf(seat));
        var edit = Edits.Submit(station.World, EditOp.WritePatch(station.Entity, "{\"Heading\":90}", "Turn"));
        Assert.Equal(Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Refused, edit.Route);
        Assert.Equal("this station's seat does not edit", EditJournal.Of(station.World).Find(edit.RequestId)!.Reason);
        bool sent = false;
        var possess = Edits.Submit(station.World, EditOp.Interact(station.Entity, _ => sent = true, "Take control"));
        Assert.True(sent);
        Assert.NotEqual(Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Refused, possess.Route);
        seat = Hrot.Common.Infrastructure.Seat.Instructor;   // the level is read live (a launch flag / the scenario may change the seat)
        Assert.NotEqual(Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Refused, Edits.Submit(station.World, EditOp.WritePatch(station.Entity, "{\"Heading\":90}", "Turn")).Route);
        Assert.Equal((Hrot.Common.Editing.EditingLevel.Full, Hrot.Common.Editing.EditingLevel.Observer, Hrot.Common.Editing.EditingLevel.None),
            (Hrot.Common.Editing.HrotEditGate.LevelOf(Hrot.Common.Infrastructure.Seat.Author), Hrot.Common.Editing.HrotEditGate.LevelOf(Hrot.Common.Infrastructure.Seat.Observer),
             Hrot.Common.Editing.HrotEditGate.LevelOf(Hrot.Common.Infrastructure.Seat.Reviewer)));
    }

    /// <summary>⭐ B-5 S6 (R-4137, docs/UX/DESIGN_Initial_State.md §3) — bravery set on a station that does NOT own the unit's brain reaches the OWNER, is applied
    /// THERE (the authored bit set; the replica untouched), and a reset (a negative value) clears the bit again — the type's value is read once more. The requester's
    /// journal reads Applied both times. ✅ Red-proof: the Temperament.RiskAcceptance binary handler left out ⇒ the owner's bravery stays unauthored ⇒ red.</summary>
    [Fact]
    public void S6_BraverySetOnANonOwner_ReachesTheOwner_AndAResetClearsIt()
    {
        var owner = new Node(owns: true, OwnerNode);
        var requester = new Node(owns: false, OtherNode);
        owner.World.AddComponent(owner.Entity, new Fdp.Toolkit.Behavior.Components.CombatTemperament { TypeRiskAcceptance = 0.3f, TypeSet = Fdp.Toolkit.Behavior.Components.CombatTemperament.RiskAcceptanceSet });
        owner.World.SetAuthority<Fdp.Toolkit.Behavior.Components.CombatTemperament>(owner.Entity, true);
        requester.World.AddComponent(requester.Entity, default(Fdp.Toolkit.Behavior.Components.CombatTemperament));

        Fdp.Toolkit.Replication.Edits.EditTicket Ask(double value, string label) => Edits.Submit(requester.World, EditOp.Write(requester.Entity,
            new Fdp.Toolkit.Replication.Patching.EntityAttributeChange
            {
                AttributeId = Fdp.Toolkit.Replication.Patching.AttributeIds.TemperamentRiskAcceptance,
                Value = Fdp.Toolkit.Replication.Patching.AttributeValue.FromDouble(value),
            }, label));

        var set = Ask(0.9, "Set bravery");
        Assert.Equal(Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Requested, set.Route);
        Assert.Equal(0f, requester.World.GetComponent<Fdp.Toolkit.Behavior.Components.CombatTemperament>(requester.Entity).RiskAcceptance);   // the replica is NOT written
        owner.Run(SentBy(requester));
        var t = owner.World.GetComponent<Fdp.Toolkit.Behavior.Components.CombatTemperament>(owner.Entity);
        Assert.Equal((0.9f, Fdp.Toolkit.Behavior.Components.CombatTemperament.RiskAcceptanceSet), (t.RiskAcceptance, (byte)(t.Set & Fdp.Toolkit.Behavior.Components.CombatTemperament.RiskAcceptanceSet)));
        Assert.Equal(EditStatus.Applied, AnswerBack(owner, requester, set.RequestId).Status);

        var reset = Ask(-1, "Reset bravery to the type's");
        owner.Run(SentBy(requester));
        t = owner.World.GetComponent<Fdp.Toolkit.Behavior.Components.CombatTemperament>(owner.Entity);
        Assert.Equal(0, t.Set & Fdp.Toolkit.Behavior.Components.CombatTemperament.RiskAcceptanceSet);   // the bit cleared: the reader falls back to the type's 0.3
        Assert.Equal(0.3f, t.TypeRiskAcceptance);
        Assert.Equal(EditStatus.Applied, AnswerBack(owner, requester, reset.RequestId).Status);
    }

    /// <summary>⭐ B-5 S5 (docs/UX/DESIGN_Initial_State.md §3 "Damage") — a part's damage level set on a station that does NOT own the unit's Health reaches the OWNER, is applied
    /// THERE (the component added with the body's plan when absent; the replica untouched), and the requester's journal reads Applied. ✅ Red-proof: the Damage.Part binary handler left out ⇒ the owner
    /// never gets the component ⇒ red.</summary>
    [Fact]
    public void S5_DamageSetOnANonOwner_ReachesTheOwner()
    {
        var owner = new Node(owns: true, OwnerNode);
        var requester = new Node(owns: false, OtherNode);
        owner.World.AddComponent(owner.Entity, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
        owner.World.SetAuthority<Fdp.Toolkit.Combat.Components.Health>(owner.Entity, true);
        requester.World.AddComponent(requester.Entity, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });

        var ticket = Edits.Submit(requester.World, EditOp.Write(requester.Entity,
            new Fdp.Toolkit.Replication.Patching.EntityAttributeChange
            {
                AttributeId = Fdp.Toolkit.Replication.Patching.AttributeIds.DamagePart,
                SubIndex1 = Fdp.Toolkit.Physics.BodyParts.PersonPart.Torso,
                Value = Fdp.Toolkit.Replication.Patching.AttributeValue.FromDouble(90),
            }, "Set torso damage"));
        Assert.Equal(Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Requested, ticket.Route);
        Assert.False(requester.World.HasComponent<Fdp.Toolkit.Physics.BodyParts.PartDamage>(requester.Entity));   // the replica is NOT written
        owner.Run(SentBy(requester));
        var d = owner.World.GetComponent<Fdp.Toolkit.Physics.BodyParts.PartDamage>(owner.Entity);
        Assert.Equal((Fdp.Toolkit.Physics.BodyParts.BodyPlan.Person, (byte)90), (d.Plan, d.LevelOf(Fdp.Toolkit.Physics.BodyParts.PersonPart.Torso)));
        Assert.Equal(EditStatus.Applied, AnswerBack(owner, requester, ticket.RequestId).Status);
    }
}
