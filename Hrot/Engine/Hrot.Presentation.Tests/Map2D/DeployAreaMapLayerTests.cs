using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.Editor.UiFrameRail;
using Hrot.Common;
using Hrot.Gameplay;
using Hrot.Map.Common.Components;
using Hrot.Presentation.Tests.Map3D;
using Hrot.UI.Common.Map2D;
using Hrot.UI.Common.Panels;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map2D;

/// <summary>
/// ⭐ CE-3370 (Player Join S3-d) — choosing the spawn area by CLICKING it on the Player's 2-D map. The rules (no window): a left press inside a chosen-side area on offer selects it — the SAME
/// <see cref="DeployAreaChoice"/> the deploy screen's list holds (so the list shows it) — and it is what the next <see cref="DeployFlowSystem.Request"/> carries (<c>RequestedAreaNetId</c>); a click outside any area,
/// inside an area of the other side, a release, the right button, or a click while not Choosing changes nothing and is not consumed. The frame rail (a real window, <c>xvfb-run -a</c>; a skip is not a pass): the outlines
/// draw while Choosing — the chosen one bold — follow the LIST's choice, and are gone when not Choosing.
/// 🔴 Red-proofs (compile): <c>HandleInput</c> without the Choosing test ⇒ the "not Choosing" case selects ⇒ red; the outline drawn regardless of <c>_choosing()</c> ⇒ the frame rail's "gone" case red.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class DeployAreaMapLayerTests
{
    private static readonly Vector2[] Square = { new(0, 0), new(20, 0), new(20, 20), new(0, 20) };

    private static Entity Area(EntityRepository w, int net, ForceId side, Vector2 at)
    {
        var e = w.CreateEntity();
        w.AddComponent(e, new SimTransform { Position = new Vector3(at.X, at.Y, 0f), Rotation = Quaternion.Identity });
        w.AddComponent(e, new NetworkIdentity(net));
        w.AddComponent(e, new SpawnArea { Side = side });
        w.SetManagedComponent(e, new EditablePolyline { Points = Square });
        return e;
    }

    private static EntityRepository World()
    {
        var w = new EntityRepository();
        w.RegisterComponent<SimTransform>(); w.RegisterComponent<NetworkIdentity>(); w.RegisterComponent<SpawnArea>(); w.RegisterManagedComponent<EditablePolyline>();
        Area(w, 400, ForceId.Friend, new Vector2(100, 100));    // "Church"  100..120
        Area(w, 401, ForceId.Friend, new Vector2(200, 100));    // "Gate"    200..220
        Area(w, 402, ForceId.Hostile, new Vector2(300, 100));   // "Camp"    (the other side)
        return w;
    }

    private static readonly DeployOffer[] Offers =
    {
        new(300, ForceId.Friend, "Rifleman", 0, Kind: 1, Count: 0, Max: 4),
        new(301, ForceId.Hostile, "Sniper", 0, Kind: 1),
        new(400, ForceId.Friend, "Church", 0, Kind: 2), new(401, ForceId.Friend, "Gate", 0, Kind: 2), new(402, ForceId.Hostile, "Camp", 0, Kind: 2),
    };

    private static DeployFlowSystem ChoosingFlow(EntityRepository w)
    {
        var f = new DeployFlowSystem(new GameplayWire(null), w, new NetworkEntityMap(), 30, new DeployFlowEnvironment(() => true, () => true, () => true, _ => (true, "")));
        f.Execute(w, 0.016f);
        Assert.Equal(DeployFlowState.Choosing, f.State);
        return f;
    }

    [Fact]
    public void AClickInsideAnArea_SelectsIt_TheListShowsTheSameChoice_AndTheNextRequestCarriesIt_AnythingElseChangesNothing()
    {
        using var world = World();
        var flow = ChoosingFlow(world);
        var choice = new DeployAreaChoice();
        bool choosing = true;
        var layer = new DeployAreaMapLayer(() => world, () => choosing, () => Offers, () => choice);
        var screen = new DeployScreen(() => flow, () => false, () => choice);
        Assert.Equal(0, screen.AreaNetId);                                              // "Any area" by default

        Assert.True(layer.HandleInput(new Vector2(110, 110), MapMouseButton.Left, true));   // inside the Church
        Assert.Equal(400, choice.AreaNetId);
        Assert.Equal(400, screen.AreaNetId);                                            // the list holds the same choice

        Assert.False(layer.HandleInput(new Vector2(150, 110), MapMouseButton.Left, true));  // between the areas: nothing
        Assert.False(layer.HandleInput(new Vector2(310, 110), MapMouseButton.Left, true));  // inside the other side's Camp: nothing
        Assert.False(layer.HandleInput(new Vector2(210, 110), MapMouseButton.Left, false)); // a release
        Assert.False(layer.HandleInput(new Vector2(210, 110), MapMouseButton.Right, true)); // the right button
        Assert.Equal(400, screen.AreaNetId);

        Assert.True(layer.HandleInput(new Vector2(210, 110), MapMouseButton.Left, true));   // the Gate
        Assert.Equal(401, screen.AreaNetId);

        Assert.True(screen.Deploy(flow, ForceId.Friend, Offers[0]));                        // a role request carries the chosen area
        Assert.Equal(401, flow.RequestedAreaNetId);

        screen.SelectArea(400);                                                         // and the list drives the same choice back
        Assert.Equal(400, choice.AreaNetId);
        choosing = false;
        Assert.False(layer.HandleInput(new Vector2(210, 110), MapMouseButton.Left, true));  // not Choosing: no click is taken
        Assert.Equal(400, choice.AreaNetId);
    }

    [Fact]
    public void APlacedUnitHasNoSpawnArea_AndTheLayerIsPartOfEveryMapHost()
    {
        using var world = World();
        var flow = ChoosingFlow(world);
        var screen = new DeployScreen(() => flow, () => false);
        screen.SelectArea(401);
        Assert.True(screen.Deploy(flow, ForceId.Friend, new DeployOffer(7, ForceId.Friend, "Alpha 1", 0)));
        Assert.Equal(0, flow.RequestedAreaNetId);                                       // a placed unit: no area rides its request

        var pack = System.IO.File.ReadAllText(System.IO.Path.Combine(Fdp.Testing.TestRepo.Root, "Hrot/Engine/Hrot.Presentation/ScenarioEditor/Map/MapInteractionPack.cs"));
        Assert.Contains("DeployAreaMapLayer.ForWorld(", pack);                          // attached by the shared pack, so no host lacks it
    }

    private static int Count(Image image, Func<Color, bool> what)
    {
        int n = 0;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (what(Raylib.GetImageColor(image, x, y))) n++;
        return n;
    }

    private static bool IsChosen(Color c) => c.R > 220 && c.G > 190 && c.B < 80;
    private static bool IsOffered(Color c) => c.R is > 90 and < 160 && c.G > 170 && c.B > 230;

    /// <summary>The outlines in a REAL frame: while Choosing the chosen side's areas are drawn (the other side's not), the chosen one bold in its own colour, following the LIST's choice; not Choosing ⇒ nothing.</summary>
    [SkippableFact]
    public void TheOutlinesDrawWhileChoosing_TheChosenOneBold_AndNotOtherwise()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(1280, 800);
        using var world = World();
        var choice = new DeployAreaChoice();
        bool choosing = true;
        var layer = new DeployAreaMapLayer(() => world, () => choosing, () => Offers, () => choice);
        var canvas = new MapCanvas(input: null);
        canvas.AddLayer(layer);
        canvas.Camera.Target = new Vector2(210, 110); canvas.Camera.Zoom = 3f;     // the Church, the Gate and the Camp are all in view

        (int chosen, int offered) Shot()
        {
            for (int i = 0; i < 3; i++) { canvas.Camera.Update(0.016f); frame.Step(canvas.Draw); }
            var image = Raylib.LoadImageFromScreen();
            try { return (Count(image, IsChosen), Count(image, IsOffered)); } finally { Raylib.UnloadImage(image); }
        }

        choice.Select(401);
        var (chosen, offered) = Shot();
        Assert.Equal(new[] { 400, 401 }, layer.Drawn);                                   // the chosen side's two areas — not the Camp
        Assert.Equal(401, layer.DrawnChosen);
        Assert.True(chosen > 40, $"the chosen Gate is drawn bold ({chosen} px)");
        Assert.True(offered > 40, $"the Church is drawn as on offer ({offered} px)");

        choice.Select(400);                                                             // the list picks the Church: the map follows
        Shot();
        Assert.Equal(400, layer.DrawnChosen);

        choosing = false;
        var (c2, o2) = Shot();
        Assert.Empty(layer.Drawn);
        Assert.True(c2 == 0 && o2 == 0, $"nothing is drawn when not Choosing ({c2} / {o2} px)");
    }
}
