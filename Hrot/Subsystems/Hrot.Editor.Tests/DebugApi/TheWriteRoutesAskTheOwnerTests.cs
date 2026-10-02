using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Components;
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
    private const long NetworkId = 77_001L;

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
            var geo = new Fdp.Modules.Geographic.Transforms.WGS84Transform();
            geo.SetOrigin(52.52, 13.405, 0.0);
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
}
