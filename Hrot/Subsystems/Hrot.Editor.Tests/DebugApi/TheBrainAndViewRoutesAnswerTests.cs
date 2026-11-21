using System;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.Editor.DebugApi;
using Hrot.Map.Common.Components;
using Hrot.Presentation.DebugApi;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>
/// ⭐⭐ <c>CE-1248</c> / <c>CE-1249</c> / <c>CE-1257</c> / <c>CE-1262</c> / <c>CE-1263</c> (R-1029) — the agent's routes answer from the same
/// readers the operator's tab uses. 📄 docs/DESIGN_Agent_Shared_View.md §6a.
/// </summary>
public sealed class TheBrainAndViewRoutesAnswerTests : IDisposable
{
    private readonly EntityRepository _world = new();
    private readonly NetworkEntityMap _map = new();
    private readonly DebugApiService _svc;

    public TheBrainAndViewRoutesAnswerTests()
    {
        _world.RegisterComponent<BehaviorState>();
        _world.RegisterComponent<BrainJournal>();
        _world.RegisterComponent<SimTransform>();
        _world.RegisterComponent<NetworkIdentity>();
        _world.RegisterComponent<SelectionState>();
        _world.RegisterComponent<CullingState>();
        _svc = new DebugApiService(new PerspectiveScopedDispatcher(
            new ISubsystemDebugProvider[] { new SubsystemDebugProvider("CGF", "Scenario", world: () => _world, entityMap: () => _map) },
            currentPerspective: () => "Scenario",
            acksPending: null));
    }

    public void Dispose() => _world.Dispose();

    private Entity Unit(int id, Vector3 at, bool brain = true)
    {
        var e = _world.CreateEntity();
        _map.Register(id, e);
        _world.AddComponent(e, new NetworkIdentity(id));
        _world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        if (!brain) return e;
        _world.AddComponent(e, new BehaviorState { ActiveBehaviorHash = 5, Origin = BehaviorOrigin.Superior });
        _world.AddComponent(e, new BrainJournal());
        return e;
    }

    /// <summary>⭐ <c>GET /entities/{id}/brain</c> is the tab's snapshot; <c>/journal?since=</c> returns only what came after.
    /// 🔴 Red-proof: drop the <c>e.Seq &lt;= since</c> skip in <c>JournalOf</c> ⇒ red.</summary>
    [Fact]
    public void TheBrainAndTheJournal_Answer_AndSinceReturnsOnlyTheNewEntries()
    {
        var u = Unit(1000, Vector3.Zero);
        for (int i = 1; i <= 3; i++) BrainJournalOf.Write(_world, u, BrainJournalKind.TaskStarted, a: i);

        var brain = _svc.GetEntityBrain(1000);
        Assert.True(brain["hasBrain"]!.GetValue<bool>());
        Assert.Equal("Superior", brain["origin"]!.GetValue<string>());
        Assert.Equal(3u, brain["journal"]!["seq"]!.GetValue<uint>());

        var journal = _svc.GetEntityJournal(1000, since: 2);
        var entries = journal["entries"]!.AsArray();
        Assert.Single(entries);
        Assert.Equal(3u, entries[0]!["seq"]!.GetValue<uint>());
        Assert.False(journal["gap"]!.GetValue<bool>());

        for (int i = 0; i < 30; i++) BrainJournalOf.Write(_world, u, BrainJournalKind.MoveStarted, a: i);
        Assert.True(_svc.GetEntityJournal(1000, since: 3)["gap"]!.GetValue<bool>());   // more than the ring holds were written
    }

    /// <summary>⭐ <c>CE-1329</c> (BV-72) — <c>GET/POST /entities/{id}/debug-settings</c> reads and writes the one store the Brain tab's ticks
    /// use: pins replace the set, trace is the user's switch, reset forgets the unit, an unknown family is refused.
    /// ⛔ SUPERSEDED: <c>POST /entities/{id}/debug</c> ("Debug this unit").</summary>
    [Fact]
    public void CE1329_DebugSettings_AreTheBrainTabsSwitches()
    {
        Unit(1001, Vector3.Zero);
        var (set, error) = _svc.SetDebugSettings(1001, System.Text.Json.Nodes.JsonNode.Parse("{\"pins\":[\"Perception\",\"Contacts\"],\"trace\":true}"));
        Assert.Null(error);
        Assert.Equal(new[] { "Perception", "Contacts" }, set!["pins"]!.AsArray().Select(p => p!.GetValue<string>()));
        Assert.True(set["trace"]!.GetValue<bool>());
        var store = Fdp.Toolkit.Behavior.Diagnostics.UnitDebugSettings.Of(_world);
        Assert.True(store.IsPinned(1001, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.TargetMemory));

        var (got, _) = _svc.GetDebugSettings(1001);
        Assert.Equal(2, got!["pins"]!.AsArray().Count);
        Assert.NotNull(_svc.SetDebugSettings(1001, System.Text.Json.Nodes.JsonNode.Parse("{\"pins\":[\"Nope\"]}")).Error);
        var (reset, _) = _svc.SetDebugSettings(1001, System.Text.Json.Nodes.JsonNode.Parse("{\"reset\":true}"));
        Assert.Empty(reset!["pins"]!.AsArray());
        Assert.False(reset["trace"]!.GetValue<bool>());
        Assert.NotNull(_svc.GetDebugSettings(9999).Error);
    }

    /// <summary>⭐ <c>GET /view</c>: no canvas ⇒ camera null but the selection still answers; a published 3-D view lists the unit in front
    /// and not the one behind, and names the crosshair target. 🔴 Red-proof: drop the <c>along &lt;= 0.1f</c> test in
    /// <c>ViewReader.TryProject</c> ⇒ red (the unit behind is listed).</summary>
    [Fact]
    public void TheView_ListsWhatIsInFront_AndTheSelection()
    {
        var front = Unit(2000, new Vector3(0, 50, 0), brain: false);
        Unit(2001, new Vector3(0, -50, 0), brain: false);
        _world.AddComponent(front, new SelectionState { IsSelected = true, IsPrimarySelection = true });

        var none = _svc.GetView(50);
        Assert.Null(none["camera"]);
        Assert.Equal(2000L, none["primary"]!.GetValue<long>());

        ViewTelemetry.Publish(_world, new ViewSnapshot("FPS", Vector3.Zero, Vector3.UnitY, 90f, 0f, 0f, 45f, new Vector2(1600, 900),
            null, null, Entity.Null, AimTarget: 2000, ZoomFovDeg: null, PublishedAtSeconds: 0));
        var view = _svc.GetView(50);
        Assert.Equal("FPS", view["mode"]!.GetValue<string>());
        Assert.Equal(2000L, view["aimTarget"]!.GetValue<long>());
        var inView = view["inView"]!.AsArray();
        Assert.Single(inView);
        Assert.Equal(2000L, inView[0]!["networkId"]!.GetValue<int>());
        Assert.Equal(2000L, _svc.GetSelection()["primary"]!.GetValue<long>());
    }

    /// <summary>⭐ CE-4571 (X-7) — threaded, the map draws (and publishes its view under) the SHOWN world A, not the sim's S: <c>GET /view</c>
    /// reads A — its camera, its units in view, their ids from A's map — and <c>POST /view/follow</c> finds the map drawing A. 🔴 Red-proof:
    /// <c>GetView</c> reading <c>_world</c> again ⇒ <i>"no map canvas"</i> (camera null).</summary>
    [Fact]
    public void X7_TheViewRoutes_ReadTheShownWorld_NotTheSims()
    {
        using var shown = new EntityRepository();
        shown.RegisterComponent<SimTransform>();
        shown.RegisterComponent<NetworkIdentity>();
        shown.RegisterComponent<SelectionState>();
        shown.RegisterComponent<CullingState>();
        var shownMap = new NetworkEntityMap();
        var e = shown.CreateEntity();
        shownMap.Register(4000, e);
        shown.AddComponent(e, new NetworkIdentity(4000));
        shown.AddComponent(e, new SimTransform { Position = new Vector3(0, 50, 0), Rotation = Quaternion.Identity });
        _svc.PresentationWorld = shown;
        _svc.PresentationMap = shownMap;
        ViewTelemetry.Publish(shown, new ViewSnapshot("FPS", Vector3.Zero, Vector3.UnitY, 90f, 0f, 0f, 45f, new Vector2(1600, 900),
            null, null, e, AimTarget: 4000, ZoomFovDeg: null, PublishedAtSeconds: 0));

        var view = _svc.GetView(50);
        Assert.Equal("FPS", view["mode"]!.GetValue<string>());
        Assert.Equal(4000, view["possessed"]!.GetValue<int>());                       // the id from A's map
        Assert.Equal(4000, Assert.Single(view["inView"]!.AsArray())!["networkId"]!.GetValue<int>());
        var (_, error) = _svc.FollowUnit(4000, null);
        Assert.Contains("No map canvas", error);                                      // no map draws A here: the refusal names the camera, not "not found"
    }

    /// <summary>⭐ CE-1451 — <c>GET /view</c> says a 3-D request is waiting on the map's background build and what that build is doing (what the 2-D map's "Building 3-D map…" shows).
    /// 🔴 Red-proof: drop <c>["pending3D"]</c> from <c>GetView</c> ⇒ red.</summary>
    [Fact]
    public void CE1451_TheView_SaysWhenA3DRequestWaitsForTheMapBuild()
    {
        ViewTelemetry.Publish(_world, new ViewSnapshot("2D", Vector3.Zero, Vector3.UnitY, 90f, 0f, 0f, 45f, new Vector2(1600, 900),
            null, null, Entity.Null, AimTarget: 0, ZoomFovDeg: null, PublishedAtSeconds: 0,
            Pending3D: true, Pending3DFailure: null, MapBuilt: false, MapBuildStep: "3-D map: terrain"));
        var view = _svc.GetView(50);
        Assert.True(view["pending3D"]!.GetValue<bool>());
        Assert.False(view["map"]!["built"]!.GetValue<bool>());
        Assert.Equal("3-D map: terrain", view["map"]!["step"]!.GetValue<string>());
    }

    /// <summary>⭐ In 2-D the published rectangle decides, not <see cref="CullingState"/> — which reads visible wherever its host's viewport
    /// is unset (measured live on the editor: units 300 px off the bottom were listed). 🔴 Red-proof: test <c>CullingState</c> first in
    /// <c>ViewReader.InView</c> ⇒ red.</summary>
    [Fact]
    public void In2D_TheRectangleDecides_NotAStaleCullingFlag()
    {
        var inside = Unit(3000, new Vector3(10, 10, 0), brain: false);
        var below = Unit(3001, new Vector3(10, -80, 0), brain: false);
        _world.AddComponent(inside, new CullingState { IsVisible = true });
        _world.AddComponent(below, new CullingState { IsVisible = true });   // an unset viewport culls nothing (CE-131)
        ViewTelemetry.Publish(_world, new ViewSnapshot("2D", new Vector3(0, 0, 300), -Vector3.UnitZ, 90f, -89.9f, 300f, 45f, new Vector2(1600, 900),
            new Vector2(-50, -25), new Vector2(50, 25), Entity.Null, AimTarget: 0, ZoomFovDeg: null, PublishedAtSeconds: 0));
        var inView = _svc.GetView(50)["inView"]!.AsArray();
        Assert.Single(inView);
        Assert.Equal(3000L, inView[0]!["networkId"]!.GetValue<int>());
    }

    /// <summary>⭐ <c>CE-1293</c> (Unit Camera UC-10) — <c>POST /view/follow</c>: refused where no map draws the world; on a map, the camera
    /// follows the unit at the asked distance and <c>GET /view</c> says so; <c>entity: null</c> hands it back. 🔴 Red-proof: skip
    /// <c>ViewControl.Register</c> in <c>MapViewModeLayer.Update</c> ⇒ red (the route finds no map).</summary>
    [Fact]
    public void FollowUnit_IsRefusedHeadless_FollowsOnAMap_AndLetsGo()
    {
        var unit = Unit(4000, new Vector3(5, 5, 0), brain: false);
        Assert.NotNull(_svc.FollowUnit(4000, null).Error);                                   // a headless host: no camera

        var canvas = new Fdp.Toolkit.Vis2D.MapCanvas(input: null);
        var sw = new Fdp.Toolkit.Vis3D.MapViewSwitch(canvas, new Fdp.Toolkit.Vis2D.Components.MapCamera());
        var layer = new MapViewModeLayer(sw, _world.Bus) { World = () => _world };
        layer.Update(0.016f);                                                                  // the map registers itself

        var (ok, error) = _svc.FollowUnit(4000, 20f);
        Assert.Null(error);
        Assert.Equal(4000, ok!["following"]!.GetValue<int>());
        Assert.Equal(unit, Assert.IsType<FollowCameraDriver>(sw.Camera3D.Driver).Unit);
        Assert.Equal(20f, layer.Following!.Distance);
        Assert.NotNull(_svc.FollowUnit(9999, null).Error);                                    // no such unit

        ViewTelemetry.Publish(_world, new ViewSnapshot("3D", Vector3.Zero, Vector3.UnitY, 90f, 0f, 20f, 45f, new Vector2(1600, 900),
            null, null, Entity.Null, AimTarget: 0, ZoomFovDeg: null, PublishedAtSeconds: 0));
        Assert.Equal(4000, _svc.GetView(50)["following"]!.GetValue<int>());

        Assert.Null(_svc.FollowUnit(null, null).Error);                                       // entity: null — let go
        Assert.Null(sw.Camera3D.Driver);
    }
}
