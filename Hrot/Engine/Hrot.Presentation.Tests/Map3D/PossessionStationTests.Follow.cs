using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Hrot.Common.Constants;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

public sealed partial class PossessionStationTests
{
    /// <summary>
    /// ⭐⭐ CE-1293 (docs/DESIGN_Unit_Camera.md UC-2) — following a unit: the wheel walks the continuum (in to his eyes, out from his eyes
    /// to the shoulder); far, the camera looks AT his centre; at zero it is at his eye looking where he looks; RMB orbits; W, Esc or a map drag
    /// hands the camera back; his end hands it back too.
    /// </summary>
    [Fact]
    public void CE1293_Follow_TheWheelWalksTheContinuum_FarItLooksAtHim_AtZeroItIsHisEyes_AndWOrEscOrHisEndLetGo()
    {
        Assert.Equal(0f, FollowCameraDriver.Step(0.6f, 1f));                                    // in, near: his eyes
        Assert.Equal(UnitCameraRig.SideFullAt, FollowCameraDriver.Step(0f, -1f));               // out from his eyes: the shoulder
        Assert.Equal(14f / FollowCameraDriver.WheelFactor, FollowCameraDriver.Step(14f, 1f), 3);
        Assert.Equal(FollowCameraDriver.MaxDistance, FollowCameraDriver.Step(FollowCameraDriver.MaxDistance, -1f));

        var (w, unit, _) = World();
        using (w)
        {
            var cam = new MapCamera3D { ViewportOverride = Viewport };
            var follow = new FollowCameraDriver(() => w, unit, bodies: null);
            cam.Driver = follow;
            cam.Update(0.016f);
            var centre = new Vector3(100f, 50f, 0.8f);                                         // his feet at 0, eyes at 1.6
            var toCentre = Vector3.Normalize(centre - cam.Eye);
            Assert.True(Vector3.Dot(toCentre, cam.Forward) > 0.999f, $"far: the camera looks at him ({cam.Eye} → {cam.Forward})");
            Assert.Equal(FollowCameraDriver.StartDistance, Vector3.Distance(cam.Eye, centre), 1);
            Assert.False(follow.ViewsFromEye);

            float yaw = cam.Yaw;                                                                  // RMB drag: the orbit turns
            Assert.True(cam.HandleInput(new Input { RightDown = true, MouseDelta = new Vector2(100f, 0f) }));
            cam.Update(0.016f);
            Assert.NotEqual(yaw, cam.Yaw, 3);

            for (int i = 0; i < 40 && !follow.ViewsFromEye; i++) cam.HandleInput(new Input { MouseWheelMove = 1f });
            Assert.True(follow.ViewsFromEye);
            cam.Update(0.016f);
            Assert.True(Vector3.Distance(cam.Eye, new Vector3(100f, 50f, 1.6f)) < 1e-3f, $"at zero: his eye ({cam.Eye})");
            Assert.True(Vector3.Dot(cam.Forward, Vector3.UnitX) > 0.99f, "looking where he looks (his heading, no gaze kept)");

            var walk = new Input();
            walk.Keys.Add((int)Raylib_cs.KeyboardKey.W);
            Assert.False(cam.HandleInput(walk));                                                  // W: the free camera's again
            Assert.Null(cam.Driver);

            cam.Driver = follow;
            var esc = new Input();
            esc.Pressed.Add((int)MapKeyboardKey.Escape);
            cam.HandleInput(esc);
            Assert.Null(cam.Driver);

            cam.Driver = follow;
            cam.HandleInput(new Input { MiddleDown = true });                                     // a map drag
            Assert.Null(cam.Driver);

            cam.Driver = follow;
            w.DestroyEntity(unit);
            cam.Update(0.016f);
            Assert.Null(cam.Driver);                                                              // his end: let go
        }
    }

    /// <summary>⭐ CE-1342 — Ctrl+M switches 2-D ↔ 3-D centred on the selected unit; plain M is not it (the unit menu's Move).
    /// ✅ Red-proof: the centring dropped ⇒ the 3-D view looks where the 2-D map did, not at him.</summary>
    [Fact]
    public void CE1342_CtrlM_SwitchesTheView_CentredOnTheSelectedUnit()
    {
        var (w, unit, _) = World();
        using (w)
        {
            var (_, sw, _) = Map();
            bool ctrl = false;
            var layer = new MapViewModeLayer(sw, w.Bus) { World = () => w, SelectedUnit = () => unit, CtrlHeld = () => ctrl };
            var m = (MapKeyboardKey)Raylib_cs.KeyboardKey.M;
            Assert.False(layer.HandleKeyInput(m));                                                  // plain M: not the switch
            Assert.False(sw.Is3D);
            ctrl = true;
            Assert.True(layer.HandleKeyInput(m));
            Assert.True(sw.Is3D);
            sw.Camera3D.Update(1f);
            Assert.True(Vector2.Distance(new Vector2(sw.Camera3D.LookAt.X, sw.Camera3D.LookAt.Y), new Vector2(100f, 50f)) < 0.5f,
                $"3-D looks at him ({sw.Camera3D.LookAt})");
            sw.Camera3D.Update(1f);
            Assert.True(layer.HandleKeyInput(m));                                                   // and back to 2-D
            sw.Camera3D.Update(1f);
            Assert.False(sw.Is3D);
        }
    }

    /// <summary>⭐ CE-1339 — Esc from a follow leaves the free camera where the follow was: the eye does not drop to the ground.
    /// ✅ Red-proof: the driver setter's look-at match dropped ⇒ the eye falls to ~1 m (the relief re-pin).</summary>
    [Fact]
    public void CE1339_EscFromAFollow_KeepsTheCameraWhereItWas()
    {
        var (w, unit, _) = World();
        using (w)
        {
            var cam = new MapCamera3D { ViewportOverride = Viewport, GroundHeight = (_, _) => 0f };
            cam.Update(0.016f);                                                                   // the free camera matched its look-at to the ground
            cam.Driver = new FollowCameraDriver(() => w, unit, bodies: null);
            cam.Update(0.016f);
            var eye = cam.Eye;
            Assert.True(eye.Z > 3f, $"following from 14 m: the eye is up ({eye})");
            var esc = new Input();
            esc.Pressed.Add((int)MapKeyboardKey.Escape);
            cam.HandleInput(esc);
            Assert.Null(cam.Driver);
            cam.Update(0.016f);
            cam.Update(0.016f);
            Assert.True(Vector3.Distance(eye, cam.Eye) < 1e-2f, $"the eye stays where the follow ended ({eye} → {cam.Eye})");
        }
    }

    /// <summary>⭐ CE-1338 — a wall pulling the arm in lifts the camera over his head instead of bringing it to his belly; no wall ⇒ unchanged.
    /// ✅ Red-proof: the lift dropped ⇒ the squeezed camera stays at belly height.</summary>
    [Fact]
    public void CE1338_AWallPullingTheArmIn_LiftsTheCameraOverHisHead()
    {
        var eye = new Vector3(0f, 0f, 1.6f);
        var belly = new Vector3(-0.3f, 0f, 0.8f);
        Assert.Equal(belly, UnitCameraRig.AboveHeadWhenSqueezed(belly, eye, arm: 14f, fullArm: 14f));                // free: unchanged
        var squeezed = UnitCameraRig.AboveHeadWhenSqueezed(belly, eye, arm: 0.3f, fullArm: 14f);
        Assert.True(squeezed.Z > eye.Z, $"pulled right in: above his eyes ({squeezed})");
        var half = UnitCameraRig.AboveHeadWhenSqueezed(belly, eye, arm: 7f, fullArm: 14f);
        Assert.InRange(half.Z, belly.Z + 0.1f, squeezed.Z - 0.1f);                                                       // in between: part way
        var high = new Vector3(-5f, 0f, 9f);
        Assert.Equal(high, UnitCameraRig.AboveHeadWhenSqueezed(high, eye, arm: 2f, fullArm: 14f));                     // never lowered
    }

    /// <summary>
    /// ⭐ CE-1293 (UC-2) — "Follow with the camera" is on every unit's map menu, its action publishes the event, and the view-mode layer
    /// has the 3-D camera follow him (not while the station possesses one); the map registers itself for <c>POST /view/follow</c>.
    /// </summary>
    [Fact]
    public void CE1293_Follow_IsOnTheUnitMenu_ItsActionHasTheCameraFollowHim_NotWhilePossessing()
    {
        var (w, unit, _) = World();
        using (w)
        {
            Assert.Contains($"\"id\":{GlobalActionIds.FollowUnit},", ContextMenuProjectorGizmo.MenuJsonFor(w, unit));

            var host = Hrot.ScenarioEditor.Map.SharedHostActions.Create();                         // the host's interaction bus
            var mi = Hrot.ScenarioEditor.Map.MapInteractionPack.Build(new Hrot.ScenarioEditor.Map.MapInteractionContext { World = w, Host = host, StartEnabled = true });
            Assert.True(mi.Actions.TryGetHandler(GlobalActionIds.FollowUnit, out var handler));
            handler!(w, unit);
            host.InteractionBus.SwapBuffers();

            var (_, sw, _) = Map();
            var layer = new MapViewModeLayer(sw, host.InteractionBus) { World = () => w };
            layer.Update(0.016f);
            Assert.True(sw.Is3D);                                                                   // the map goes 3-D …
            Assert.Equal(unit, Assert.IsType<FollowCameraDriver>(sw.Camera3D.Driver).Unit);         // … and follows him
            Assert.Same(layer, ViewControl.Of(w));                                                  // POST /view/follow finds this map

            Assert.Null(layer.FollowUnit(unit, 0f));                                                // the HTTP distance: his eyes
            Assert.Equal(0f, layer.Following!.Distance);
            layer.StopFollowing();
            Assert.Null(sw.Camera3D.Driver);

            // possessing, the station's view IS his eyes: following refuses
            w.AddComponent(unit, new Fdp.Toolkit.Behavior.Possession.PossessedBy { StationNodeId = 0, Mode = Fdp.Toolkit.Behavior.Possession.PossessionMode.Fps, Epoch = 1 });
            var station = new PossessionStation(sw, () => w);
            station.Update(0.016f);
            Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
            Assert.NotNull(layer.FollowUnit(unit));
            Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
        }
    }
}
