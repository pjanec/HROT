using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis3D;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ <c>CE-3289</c> (docs/DESIGN_Map_3D_Mode.md §6ab, R-3032) — filming without a window: the shot's geometry, the director following a unit,
/// the director looking AT a point (fog as the free camera), the studio's mailbox, and the capture layer taking the camera and giving it
/// back. The pixels are <see cref="FilmFrameRail"/>'s.
/// </summary>
[Collection(Map3DFrameRail.Serial)]   // FilmStudio is process-wide
public sealed class FilmTests : System.IDisposable
{
    public FilmTests() => FilmStudio.Reset();
    public void Dispose() => FilmStudio.Reset();

    /// <summary>An eye and a target make the pose whose eye IS that eye (the camera's own <see cref="MapCamera3D.Eye"/>).</summary>
    [Fact]
    public void CE3289_ALookingShot_PutsTheEyeWhereAsked()
    {
        var eye = new Vector3(10f, -40f, 25f);
        var target = new Vector3(30f, 5f, 2f);
        var shot = FilmShot.Looking(eye, target)!;
        var camera = new MapCamera3D { Pose = shot.Pose!.Value };
        Assert.True(Vector3.Distance(eye, camera.Eye) < 1e-3f, $"eye {camera.Eye}");
        Assert.Equal(target, camera.LookAt);
        Assert.Null(FilmShot.Looking(eye, eye));
    }

    /// <summary>
    /// The director follows a unit by its network id: the point looked at is its position raised by the lift, every time it is placed;
    /// a unit that is not here leaves the camera where it was. 🔴 Red-proof: drop the lift ⇒ the Z is the ground's.
    /// </summary>
    [Fact]
    public void CE3289_TheDirector_FollowsAUnit_ByItsNetworkId()
    {
        using var world = new EntityRepository();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<NetworkIdentity>();
        var unit = world.CreateEntity();
        world.AddComponent(unit, new SimTransform { Position = new Vector3(5f, 6f, 0f), Rotation = Quaternion.Identity });
        world.AddComponent(unit, new NetworkIdentity { Value = 77 });
        var director = new FilmDirector(() => world) { Shot = new FilmShot { Follow = 77, Distance = 20f, Yaw = 1f, Pitch = -0.3f, Lift = 2f } };
        var camera = new MapCamera3D();

        Assert.True(director.Place(camera));
        Assert.Equal(new Vector3(5f, 6f, 2f), camera.LookAt);
        Assert.Equal((20f, 1f, -0.3f), (camera.Distance, camera.Yaw, camera.Pitch));

        world.GetComponentRW<SimTransform>(unit).Position = new Vector3(50f, 6f, 10f);   // he moved — so does the camera
        director.Update(camera, 0.016f);
        Assert.Equal(new Vector3(50f, 6f, 12f), camera.LookAt);

        director.Shot = new FilmShot { Follow = 999 };   // nobody: the camera stays
        Assert.False(director.Place(camera));
        Assert.Equal(new Vector3(50f, 6f, 12f), camera.LookAt);
    }

    /// <summary>
    /// The director looks AT a point (a driver that is not a pair of eyes): the fog starts part way there, as the free camera's —
    /// a first-person driver's starts at the lens. 🔴 Red-proof: <c>ViewsFromEye</c> true ⇒ 0.
    /// </summary>
    [Fact]
    public void CE3289_TheDirectorsFog_StartsAsTheFreeCamerasDoes()
    {
        using var world = new EntityRepository();
        var camera = new MapCamera3D { Pose = new CameraPose(Vector3.Zero, 100f, 0f, -0.3f) };
        float free = camera.FogStart;
        camera.Driver = new FilmDirector(() => world) { Shot = new FilmShot { Pose = camera.Pose } };
        Assert.Equal(free, camera.FogStart);
        Assert.True(camera.FogStart > 0f);
    }

    /// <summary>
    /// A shot may narrow the lens (a telephoto: a far target fills the frame); the camera gets its own lens back when the director
    /// lets go. 🔴 Red-proof: drop the restore in <c>Detached</c> ⇒ the operator's map stays zoomed at 12°.
    /// </summary>
    [Fact]
    public void CE3289_AShotsLens_IsGivenBack_WhenTheDirectorLetsGo()
    {
        using var world = new EntityRepository();
        var camera = new MapCamera3D();
        float own = camera.FovyDegrees;
        var director = new FilmDirector(() => world) { Shot = new FilmShot { Pose = new CameraPose(Vector3.Zero, 200f, 0f, -0.2f), FovDeg = 12f } };
        camera.Driver = director;
        camera.Update(0.016f);
        Assert.Equal(12f, camera.FovyDegrees);
        director.Shot = director.Shot with { FovDeg = null };   // a shot without a lens: the camera's own
        camera.Update(0.016f);
        Assert.Equal(own, camera.FovyDegrees);
        director.Shot = director.Shot with { FovDeg = 20f };
        camera.Update(0.016f);
        camera.Driver = null;
        Assert.Equal(own, camera.FovyDegrees);
    }

    /// <summary>The mailbox: captures wait in order; one the caller gave up on is skipped; nothing is taken twice.</summary>
    [Fact]
    public void CE3289_TheStudio_HandsOutWaitingCaptures_InOrder_SkippingAbandonedOnes()
    {
        var first = FilmStudio.Capture(new FilmCaptureRequest("/tmp/a.png", 64, 64));
        var second = FilmStudio.Capture(new FilmCaptureRequest("/tmp/b.png", 64, 64));
        Assert.True(FilmStudio.TryTake(out var r1, out var d1));
        Assert.Equal("/tmp/a.png", r1.Path);
        d1.TrySetCanceled();                                          // abandoned by its caller (the route's 30 s)
        Assert.True(FilmStudio.TryTake(out var r2, out var d2));
        Assert.Equal("/tmp/b.png", r2.Path);
        Assert.False(FilmStudio.TryTake(out _, out _));
        d2.TrySetResult(default);
        Assert.True(first.IsCanceled);
        Assert.True(second.IsCompletedSuccessfully);
    }

    /// <summary>
    /// The capture layer hands the 3-D camera to its director while a shot is held (switching the map to 3-D), and gives it back on
    /// release; a map that was not DRAWN since its last update serves no capture (in <c>--mode all</c> only the map on screen films).
    /// 🔴 Red-proofs: drop the drawn gate ⇒ the undrawn map takes the capture; drop the release ⇒ the director keeps the camera.
    /// </summary>
    [Fact]
    public void CE3289_TheLayer_TakesTheCamera_ForAShot_GivesItBack_AndAnUndrawnMapFilmsNothing()
    {
        using var world = new EntityRepository();
        var canvas = new MapCanvas(input: null);
        var view = new MapViewSwitch(canvas, canvas.Camera);
        var film = new FilmCaptureLayer(canvas, view, () => world);
        Assert.False(view.Is3D);

        FilmStudio.Direct(new FilmShot { Pose = new CameraPose(new Vector3(1f, 2f, 3f), 40f, 0.5f, -0.4f) });
        film.Update(0.016f);
        Assert.True(view.Is3D);
        Assert.Same(film.Director, view.Camera3D.Driver);
        view.Camera3D.Update(0.016f);
        Assert.Equal(new Vector3(1f, 2f, 3f), view.Camera3D.LookAt);

        var pending = FilmStudio.Capture(new FilmCaptureRequest("/tmp/never.png", 64, 64));
        film.Update(0.016f);                                          // never drawn ⇒ not its capture
        Assert.False(pending.IsCompleted);
        Assert.Equal(0, film.Served);
        Assert.True(FilmStudio.TryTake(out _, out var done));         // still waiting for a drawn map
        done.TrySetCanceled();

        FilmStudio.Release();
        film.Update(0.016f);
        Assert.Null(view.Camera3D.Driver);
    }
}
