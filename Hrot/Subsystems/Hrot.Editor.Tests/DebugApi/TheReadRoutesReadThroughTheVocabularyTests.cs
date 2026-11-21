using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.DebugApi;
using Hrot.Presentation.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>
/// ⭐⭐⭐ <b>R-1 (AR-8) — the debug API's READ routes over the attribute vocabulary</b> (📄 <c>docs/UX/DESIGN_Review_Perspective.md</c> §6.13a):
/// <c>GET /entities/{id}/attributes</c> (through <c>AttributeReaders</c>), <c>GET /entities/{id}/field</c> (through <c>ComponentJsonPath</c>) and the schema's <c>readable</c> + <c>unit</c>.
/// ⭐ Beside <see cref="TheWriteRoutesAskTheOwnerTests"/>, the write twins' rails. ⚠ A new class because the routes' own suite, <c>DebugApiBatch13Tests</c>, is excluded from the
/// build (<c>DEBT-MCP-001</c>) — a rail added there would never run. The Replay Browser serves these routes through the same service, so one rail covers both.
/// </summary>
public sealed class TheReadRoutesReadThroughTheVocabularyTests
{
    private const int NetworkId = 88_001;

    private sealed class Node : System.IDisposable
    {
        public readonly EntityRepository World = new();
        public readonly NetworkEntityMap EntityMap = new();
        public readonly DebugApiService Service;

        public Node()
        {
            Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(World);
            World.RegisterComponent<Health>(); World.RegisterComponent<WeaponState>();
            var e = World.CreateEntity();
            var st = new SimTransform();
            st.Position = new WorldPoint { X = 32f, Y = 34f, Z = 5f };
            World.AddComponent(e, st);
            World.AddComponent(e, new NetworkIdentity { Value = NetworkId });
            World.AddComponent(e, new Health { Current = 50f, Max = 100f });
            World.AddComponent(e, new WeaponState { Ammo = 7, MaxAmmo = 10 });
            EntityMap.Register(NetworkId, e);
            Service = new DebugApiService(
                new PerspectiveScopedDispatcher(
                    new ISubsystemDebugProvider[] { new SubsystemDebugProvider("Node", "Node", world: () => World, entityMap: () => EntityMap) },
                    currentPerspective: () => "Node", acksPending: null),
                geoTransform: null);
        }

        public void Dispose() => World.Dispose();
    }

    private static JsonObject Row(JsonNode result, string path)
        => result["attributes"]!.AsArray().Select(n => n!.AsObject()).Single(o => o["path"]!.GetValue<string>() == path);

    [Fact]
    public void WithNoPathEveryAttributeTheEntityHasIsListedWithItsValueKindAndUnit()
    {
        using var node = new Node();
        var (result, error, status) = node.Service.GetEntityAttributes(NetworkId, null);

        Assert.Null(error);
        Assert.Equal(200, status);
        Assert.Equal(50d, Row(result!, "Health.Current")["value"]!.GetValue<double>());
        Assert.Equal("number", Row(result!, "Health.Current")["kind"]!.GetValue<string>());
        Assert.Null(Row(result!, "Health.Current")["unit"]);
        Assert.Equal(32d, Row(result!, "Position.X")["value"]!.GetValue<double>());
        Assert.Equal("m", Row(result!, "Position.X")["unit"]!.GetValue<string>());
        Assert.Equal(7, Row(result!, "Ammo.0")["value"]!.GetValue<int>());      // a wildcard is listed per sub-index it has
        Assert.Equal("rounds", Row(result!, "Ammo.0")["unit"]!.GetValue<string>());
        Assert.DoesNotContain(result!["attributes"]!.AsArray(), n => n!["path"]!.GetValue<string>().StartsWith("Weather."));   // a component it lacks is not an answer
    }

    [Fact]
    public void PathsSelectAndALackedOneIsNotPresentNotAnError()
    {
        using var node = new Node();
        var (result, error, _) = node.Service.GetEntityAttributes(NetworkId, new[] { "Health.Current", "Ammo.0", "Weather.FogTopM", "Ammo.2" });

        Assert.Null(error);
        Assert.Equal(new[] { "Health.Current", "Ammo.0" }, result!["attributes"]!.AsArray().Select(n => n!["path"]!.GetValue<string>()).ToArray());
        Assert.Equal(new[] { "Weather.FogTopM", "Ammo.2" }, result["notPresent"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());
    }

    [Fact]
    public void AnUnknownPathIs400AndAnUnknownEntityIs404()
    {
        using var node = new Node();
        Assert.Equal(400, node.Service.GetEntityAttributes(NetworkId, new[] { "Health.Nope" }).status);
        Assert.Equal(404, node.Service.GetEntityAttributes(NetworkId + 1, null).status);
        Assert.Equal(404, node.Service.GetEntityField(NetworkId + 1, "Health.Current").status);
        Assert.Equal(400, node.Service.GetEntityField(NetworkId, "").status);
    }

    [Fact]
    public void TheFieldRouteWalksTheComponentJsonCaseInsensitively()
    {
        using var node = new Node();
        var (result, error, _) = node.Service.GetEntityField(NetworkId, "simtransform.position.0");   // a point dumps as an ARRAY: a number part indexes it

        Assert.Null(error);
        Assert.Equal(32d, result!["value"]!.GetValue<double>());
        Assert.Equal(100d, node.Service.GetEntityField(NetworkId, "Health.Max").result!["value"]!.GetValue<double>());
        Assert.Equal(404, node.Service.GetEntityField(NetworkId, "Health.Nope").status);
    }

    [Fact]
    public void TheSchemaSaysPerPathWhetherItIsReadableAndItsUnit()
    {
        using var node = new Node();
        var rows = node.Service.GetAttributesSchema()["attributes"]!.AsArray().Select(n => n!.AsObject()).ToArray();

        Assert.Equal(Fdp.Toolkit.Replication.Attributes.AttributeVocabulary.All.Count, rows.Length);
        Assert.All(rows, r => Assert.True(r["readable"]!.GetValue<bool>(), r["path"]!.GetValue<string>()));
        Assert.Equal("hPa", rows.Single(r => r["path"]!.GetValue<string>() == "Weather.SeaLevelPressureHpa")["unit"]!.GetValue<string>());
        Assert.True(rows.Single(r => r["path"]!.GetValue<string>() == "Ammo.*")["wildcard"]!.GetValue<bool>());
    }

    [Fact]
    public void ComponentJsonPathKeepsTheThreeWalkersRules()
    {
        var root = JsonNode.Parse("{\"Waypoints\":[{\"X\":1},{\"X\":2}],\"Ammo\":{\"2\":5}}");

        Assert.Equal(2, ComponentJsonPath.Get(root, "waypoints.1.x")!.GetValue<int>());   // keys ignore case, a number part indexes an array
        Assert.Equal(5, ComponentJsonPath.Get(root, "AMMO.2")!.GetValue<int>());            // on an object a number is just a key
        Assert.Null(ComponentJsonPath.Get(root, "Waypoints.2"));
        Assert.Null(ComponentJsonPath.Get(root, "Waypoints.X"));
        Assert.Same(root, ComponentJsonPath.Get(root, ""));
    }
}
