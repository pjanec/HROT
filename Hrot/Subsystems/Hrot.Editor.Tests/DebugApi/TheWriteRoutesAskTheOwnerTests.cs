using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Replication.Events;
using Fdp.Toolkit.Replication.Services;
using Hrot.Editor.DebugApi;
using Hrot.Presentation.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-3003</c> — the debug API's two write routes, on a node that does NOT own the target.</b>
/// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.7 prerequisite 4.
///
/// <para>📐 Measured live <c>2026-10-02</c>: <c>POST /entities/{id}/attribute</c> on a non-owner skipped every key at the
/// authority gate and answered with the unchanged dump, and <c>POST /entities/{id}/component</c> wrote the replica the
/// owner then overwrote. ⇒ the routed edits of §5.7 E4/E6 could not be driven, and both routes answered <c>ok</c> about
/// work that went nowhere (<c>CE-191</c>).</para>
///
/// <para>⭐ The cluster round trip — a real IG asking a real SimHost over DDS — is
/// <c>AttributeChangeRequestRoundTripTests.ANonOwningNodesDebugAttributePatchIsAppliedByTheOwner</c>. ⭐ These rails pin the
/// DECISION the route makes, on one world, with no network: <c>direct</c> · <c>requested</c> · <c>noMatch</c> · refuse.</para>
///
/// <para>⚠ Why a new class: the route's only suite, <c>DebugApiBatch13Tests</c>, is excluded from the build pending the
/// harness reconciliation <c>DEBT-MCP-001</c>, so a rail added there would never run.</para>
/// </summary>
public sealed class TheWriteRoutesAskTheOwnerTests
{
    private const int NetworkId = 77_001;

    /// <summary><c>dtWorldPos</c>'s ordinal on NED — any ordinal would do; the map is what is under test.</summary>
    private const long WorldPosDescriptor = 2;

    private sealed class Node : System.IDisposable
    {
        public readonly EntityRepository World = new();
        public readonly NetworkEntityMap EntityMap = new();
        public readonly DescriptorOwnershipMap OwnershipMap = new();
        public readonly Entity Entity;
        public readonly DebugApiService Service;

        public Node(bool ownsSimTransform, bool simTransformIsNetworked)
        {
            Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(World);
            var geo = Fdp.Modules.Geographic.Transforms.UtmTransform.AtOrigin(52.52, 13.405, 0.0);
            World.SetSingletonManaged<IGeographicTransform>(geo);

            Entity = World.CreateEntity();
            World.AddComponent(Entity, new SimTransform());
            World.AddComponent(Entity, new NetworkIdentity { Value = NetworkId });
            World.SetAuthority<SimTransform>(Entity, ownsSimTransform);
            EntityMap.Register(NetworkId, Entity);

            // ⭐ The NED shape: the forward table only (RegisterMapping(int[])), exactly as the NED module fills it.
            if (simTransformIsNetworked)
                OwnershipMap.RegisterMapping(WorldPosDescriptor, ComponentTypeRegistry.GetId(typeof(SimTransform)));

            Service = new DebugApiService(
                new PerspectiveScopedDispatcher(
                    new ISubsystemDebugProvider[]
                    {
                        new SubsystemDebugProvider("Node", "Node",
                            world: () => World, entityMap: () => EntityMap, descriptorMap: () => OwnershipMap),
                    },
                    currentPerspective: () => "Node",
                    acksPending: null),
                geoTransform: geo);
        }

        public UpdateEntityAttributeCommand[] PublishedRequests()
        {
            World.Bus.SwapBuffers();
            return World.Bus.ReadManaged<UpdateEntityAttributeCommand>().ToArray();
        }

        public void Dispose() => World.Dispose();
    }

    private static string? Route(JsonNode? result) => result?["write"]?["route"]?.GetValue<string>();

    // ── POST /entities/{id}/attribute ───────────────────────────────────────────────────────

    [Fact]
    public void A_patch_on_a_component_this_node_owns_lands_here_and_says_direct()
    {
        using var node = new Node(ownsSimTransform: true, simTransformIsNetworked: true);
        var before = node.World.GetComponent<SimTransform>(node.Entity).Rotation;

        var (result, error) = node.Service.PatchEntityAttribute(NetworkId, "{\"Heading\":90}");

        Assert.Null(error);
        Assert.Equal("direct", Route(result));
        Assert.NotEqual(before, node.World.GetComponent<SimTransform>(node.Entity).Rotation);
        Assert.Empty(node.PublishedRequests());
    }

    /// <summary>⭐ Seats and hats H7 — an HTTP session that plays the Observer (or a Player) seat is refused the edit with why, as that person's station
    /// would be; returning to Author (seat:null) edits again; GET /session reports it. ✅ Red-proof: the SessionRefusal check removed ⇒ the observer's edit lands ⇒ red.</summary>
    [Fact]
    public void H7_ASessionPlayingTheObserverSeat_IsRefusedTheEdit()
    {
        using var node = new Node(ownsSimTransform: true, simTransformIsNetworked: true);
        var (session, seatError) = node.Service.SetSessionSeat(new System.Text.Json.Nodes.JsonObject { ["seat"] = "Observer" });
        Assert.Null(seatError);
        Assert.Equal("Observer", session!["seat"]!.GetValue<string>());
        var before = node.World.GetComponent<SimTransform>(node.Entity).Rotation;

        var (_, error) = node.Service.PatchEntityAttribute(NetworkId, "{\"Heading\":90}");
        Assert.Equal("this station is an observer", error);
        Assert.Equal(before, node.World.GetComponent<SimTransform>(node.Entity).Rotation);

        node.Service.SetSessionSeat(new System.Text.Json.Nodes.JsonObject { ["seat"] = null });
        Assert.Null(node.Service.PatchEntityAttribute(NetworkId, "{\"Heading\":90}").error);
        Assert.NotNull(node.Service.SetSessionSeat(new System.Text.Json.Nodes.JsonObject { ["seat"] = "Admiral" }).error);
        Assert.Equal("Command", node.Service.SetSessionHat(new System.Text.Json.Nodes.JsonObject { ["hat"] = "command" }).result!["hat"]!.GetValue<string>());
    }

    /// <summary>
    /// ⭐⭐⭐ The defect itself: the patch must leave as a request carrying the JSON, addressed by network id, and the
    /// local replica must NOT be edited. ⭐ Red-proof (by inverse edit): drop the <c>TryPublishJsonPatch</c> call — no
    /// request is published and this reddens.
    /// </summary>
    [Fact]
    public void A_patch_on_a_component_another_node_owns_is_requested_of_it()
    {
        using var node = new Node(ownsSimTransform: false, simTransformIsNetworked: true);
        var before = node.World.GetComponent<SimTransform>(node.Entity).Rotation;

        var (result, error) = node.Service.PatchEntityAttribute(NetworkId, "{\"Heading\":90}");

        Assert.Null(error);
        Assert.Equal("requested", Route(result));
        Assert.Equal(before, node.World.GetComponent<SimTransform>(node.Entity).Rotation);

        var request = Assert.Single(node.PublishedRequests());
        Assert.Equal(NetworkId, request.NetworkId);
        Assert.Equal("{\"Heading\":90}", request.AttributePatchJson);
        Assert.True(request.AttributeChanges is null || request.AttributeChanges.Count == 0,
            "The binary arm must stay empty: sending both would apply the change twice on the owner.");
    }

    /// <summary>⛔ <c>CE-191</c>: nothing carries the component on the network, so nobody could apply it — refuse.</summary>
    [Fact]
    public void A_patch_with_no_owner_to_ask_refuses()
    {
        using var node = new Node(ownsSimTransform: false, simTransformIsNetworked: false);

        var (result, error) = node.Service.PatchEntityAttribute(NetworkId, "{\"Heading\":90}");

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Contains("no owner to ask", error);
        Assert.Empty(node.PublishedRequests());
    }

    /// <summary>⭐ An unknown key is still not an error (a mixed-version sender) — and it is not sent anywhere either.</summary>
    [Fact]
    public void A_patch_of_unknown_keys_matches_nothing_and_asks_nobody()
    {
        using var node = new Node(ownsSimTransform: false, simTransformIsNetworked: true);

        var (result, error) = node.Service.PatchEntityAttribute(NetworkId, "{\"NoSuchAttribute\":1}");

        Assert.Null(error);
        Assert.Equal("noMatch", Route(result));
        Assert.Empty(node.PublishedRequests());
    }

    // ── POST /entities/{id}/component ───────────────────────────────────────────────────────

    /// <summary>⛔ Editing another node's component here would change only the replica the owner overwrites — refuse.</summary>
    [Fact]
    public void A_component_edit_another_node_owns_is_refused()
    {
        using var node = new Node(ownsSimTransform: false, simTransformIsNetworked: true);
        var before = node.World.GetComponent<SimTransform>(node.Entity).Position;

        var (result, error) = node.Service.EditEntityComponent(
            NetworkId, nameof(SimTransform), JsonNode.Parse("{\"Position\":{\"X\":999,\"Y\":0,\"Z\":0}}"));

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Contains("owned by another node", error);
        Assert.Equal(before, node.World.GetComponent<SimTransform>(node.Entity).Position);
    }

    /// <summary>⭐ A component no descriptor carries is node-local: it is still written here (the editor's case).</summary>
    [Fact]
    public void A_component_edit_of_a_node_local_component_still_writes()
    {
        using var node = new Node(ownsSimTransform: false, simTransformIsNetworked: false);

        var (_, error) = node.Service.EditEntityComponent(
            NetworkId, nameof(SimTransform), JsonNode.Parse("{\"Position\":{\"X\":999,\"Y\":0,\"Z\":0}}"));

        Assert.Null(error);
        Assert.Equal(999f, node.World.GetComponent<SimTransform>(node.Entity).Position.X, precision: 3);
    }

    /// <summary>⭐ N-15 (CE-4477) — the component route is the inspector's write over HTTP, so it is ONE step of the editor's history where one is recorded
    /// (Edit mode): Undo brings the old value back. Outside Edit (the recorder off) it leaves no entry. 🔴 Red-proof: the route without its
    /// <c>EditSteps</c> bracket ⇒ <c>CanUndo</c> false.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_component_edit_is_one_undo_step_in_edit_mode(bool editMode)
    {
        using var node = new Node(ownsSimTransform: true, simTransformIsNetworked: false);
        var history = new Hrot.Presentation.Edits.EditHistory();
        EditSteps.Install(node.World, new Hrot.Presentation.Edits.WorldUndoRecorder(history, new Fdp.Toolkit.Replay.WorldSnapshotRing(maxDepth: 8), () => editMode));

        var (_, error) = node.Service.EditEntityComponent(
            NetworkId, nameof(SimTransform), JsonNode.Parse("{\"Position\":{\"X\":999,\"Y\":0,\"Z\":0}}"));

        Assert.Null(error);
        Assert.Equal(editMode, history.CanUndo);
        if (!editMode) return;
        history.Undo();
        Assert.Equal(0f, node.World.GetComponent<SimTransform>(node.Entity).Position.X, precision: 3);
    }
    /// <summary>⭐ CE-4572 (X-8) — an HTTP <c>DELETE /entities/{id}</c> is ONE step of the editor's history in Edit (like the component edit,
    /// D-38): undo after the teardown lands brings the entity back; outside Edit nothing is recorded. 🔴 Red-proof: the bracket removed from
    /// <c>DeleteEntity</c> ⇒ no step (<c>CanUndo</c> false).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void X8_A_delete_is_one_undo_step_in_edit_mode(bool editMode)
    {
        using var node = new Node(ownsSimTransform: true, simTransformIsNetworked: false);
        var history = new Hrot.Presentation.Edits.EditHistory();
        EditSteps.Install(node.World, new Hrot.Presentation.Edits.WorldUndoRecorder(history, new Fdp.Toolkit.Replay.WorldSnapshotRing(maxDepth: 8), () => editMode));

        var (_, error, _) = node.Service.DeleteEntity(NetworkId);
        Assert.Null(error);
        node.World.DestroyEntity(node.Entity);                     // the ELM teardown, a tick later
        Assert.Equal(editMode, history.CanUndo);
        if (!editMode) return;
        history.Undo();
        Assert.True(node.World.IsAlive(node.Entity), "undo after the HTTP delete did not bring the entity back");
    }

    // ── CE-2380 part 1 — the OUTCOME of the request comes back to the node that asked ───────

    private sealed class OneShotSource : Hrot.Map.Common.Systems.IUpdateEntityAttributeRequestSource
    {
        public Hrot.NED.Messages.UpdateEntityAttributeRequest? Pending;
        public void ProcessRequests(System.Action<Hrot.NED.Messages.UpdateEntityAttributeRequest> processor)
        {
            if (Pending is { } r) { Pending = null; processor(r); }
        }
    }

    private sealed class CapturedWire : Hrot.Map.Common.Dds.IDdsWriter<Hrot.NED.Messages.EntityEditOutcome>
    {
        public readonly System.Collections.Generic.List<Hrot.NED.Messages.EntityEditOutcome> Written = new();
        public void Write(Hrot.NED.Messages.EntityEditOutcome sample) => Written.Add(sample);
        public void DisposeInstance(Hrot.NED.Messages.EntityEditOutcome key) { }
    }

    /// <summary>
    /// ⭐ CE-2380 part 1 (R-2082) — the whole loop on two worlds, no DDS: the REQUESTING node edits an entity it does not own (<c>EditRequests.WriteJson</c>), the command crosses to the
    /// OWNER's <c>UpdateEntityAttributeRequestSystem</c> (the one hand-made hop: the command → <c>UpdateEntityAttributeRequest</c> conversion is the egress translator's, which needs a live
    /// participant), the owner applies and answers, the outcome crosses back through the REAL egress / ingress pair, and the requesting node's <c>EditOutcomeLog</c> ends with Applied
    /// for its request id. ✅ Red-proof: the owner's report removed from the request system ⇒ the log stays empty ⇒ red.
    /// </summary>
    [Fact]
    public void An_edit_asked_of_the_owner_comes_back_applied_into_the_requesters_log()
    {
        using var requester = new Node(ownsSimTransform: false, simTransformIsNetworked: true);
        using var owner = new Node(ownsSimTransform: true, simTransformIsNetworked: true);

        var ticket = EditRequests.WriteJson(requester.World, requester.Entity, "{\"Heading\":90}");
        Assert.Equal(Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Requested, ticket.Route);
        var cmd = Assert.Single(requester.PublishedRequests());
        Assert.Equal((ticket.RequestId, true), (cmd.RequestId, cmd.WantOutcome));
        Assert.False(EditOutcomeLog.Of(requester.World).TryGet(ticket.RequestId, out _));   // nothing known yet

        var source = new OneShotSource
        {
            Pending = new Hrot.NED.Messages.UpdateEntityAttributeRequest
            {
                RequestId = cmd.RequestId, EntityId = cmd.NetworkId, AttributePatchJson = cmd.AttributePatchJson, WantOutcome = cmd.WantOutcome,
            },
        };
        var system = new Hrot.Map.Common.Systems.UpdateEntityAttributeRequestSystem(source, owner.EntityMap);
        system.Execute(owner.World, 0f);
        owner.World.Bus.SwapBuffers();

        var wire = new CapturedWire();
        new Hrot.Network.Translators.EditOutcomeEgressTranslator(wire).ScanAndPublish(owner.World);
        var sample = Assert.Single(wire.Written);

        var ingress = new Hrot.Network.Translators.EditOutcomeIngressTranslator(null);
        Assert.True(ingress.ProcessSample(in sample, requester.World));

        Assert.True(EditOutcomeLog.Of(requester.World).TryGet(ticket.RequestId, out var outcome));
        Assert.Equal((EditStatus.Applied, NetworkId, true), (outcome.Status, outcome.NetworkId, outcome.FromNetwork));
    }
    /// <summary>
    /// CE-2380 part 2 — <c>POST /entities/{id}/attribute</c> answers a <c>write.requestId</c> (the request's own when it went to the owner, a fresh one when the write landed here) and
    /// <c>GET /edits/{requestId}</c> returns what the node's <c>EditOutcomeLog</c> holds: nothing yet for a request still out ⇒ the not-found answer, the outcome once one is logged, and a bad id is
    /// refused. ✅ Red-proof: <c>GetEditOutcome</c> reading a fresh log instead of <c>EditOutcomeLog.Of(_world)</c> ⇒ the status assert reddens.
    /// </summary>
    [Fact]
    public void The_write_answers_a_request_id_and_GET_edits_reads_the_log()
    {
        using var node = new Node(ownsSimTransform: false, simTransformIsNetworked: true);

        var (result, error) = node.Service.PatchEntityAttribute(NetworkId, "{\"Heading\":90}");
        Assert.Null(error);
        var idText = result!["write"]!["requestId"]!.GetValue<string>();
        Assert.True(System.Guid.TryParse(idText, out var id));
        Assert.Equal(id, Assert.Single(node.PublishedRequests()).RequestId);                       // the id on the wire request is the one answered

        var (none, notYet) = node.Service.GetEditOutcome(idText);
        Assert.Null(none);
        Assert.StartsWith("No outcome known", notYet);

        EditOutcomes.Report(node.World, id, NetworkId, EditStatus.Refused, "the owner said no", respondingNode: 4);
        var (got, err) = node.Service.GetEditOutcome(idText);
        Assert.Null(err);
        Assert.Equal(("Refused", "the owner said no", 4), (got!["status"]!.GetValue<string>(), got["reason"]!.GetValue<string>(), got["respondingNode"]!.GetValue<int>()));

        Assert.NotNull(node.Service.GetEditOutcome("not-a-guid").error);

        // a write that lands HERE is reported at once under its own id
        using var owner = new Node(ownsSimTransform: true, simTransformIsNetworked: true);
        var (direct, e2) = owner.Service.PatchEntityAttribute(NetworkId, "{\"Heading\":90}");
        Assert.Null(e2);
        var (applied, _) = owner.Service.GetEditOutcome(direct!["write"]!["requestId"]!.GetValue<string>());
        Assert.Equal("Applied", applied!["status"]!.GetValue<string>());
    }
}
