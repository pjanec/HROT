using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Possession;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

public sealed partial class PossessionStationTests
{
    /// <summary>
    /// ⭐⭐ CE-1204 (docs/DESIGN_Entity_Control.md §8l LK-2, LK-4, R-1017) — a person's switchable mouse look: locked by default (the mouse
    /// turns his body); V frees it — the mouse then turns his look within his neck (70°) while the frame's heading stays, past the neck the
    /// body is dragged; A/D turn him at 120°/s (his view with him) and never strafe; the HUD says LOOK FREE (V); V again locks it (the body
    /// turns to the look); the choice is the station's, so a new possession keeps it.
    /// ✅ Red-proof: the frame's <c>Yaw = BodyYaw</c> put back to <c>_yaw</c> ⇒ the first free frame's heading follows the mouse.
    /// </summary>
    [Fact]
    public void CE1204_VFreesAPersonsLook_TheMouseTurnsHisHeadWithinTheNeck_DraggingTheBody_ADTurnHim_AndTheChoiceStays()
    {
        var (w, unit, _) = World();
        using (w)
        {
            w.AddComponent(unit, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
            var (_, sw, _) = Map();
            var station = new PossessionStation(sw, () => w);
            station.Update(0.016f);
            sw.Camera3D.Update(0.016f);
            var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
            Assert.False(driver.LookFree);                                                   // locked by default (today's)

            OperatorControlFrameEvent Frame(float dx = 0f, Raylib_cs.KeyboardKey? press = null, Raylib_cs.KeyboardKey? hold = null)
            {
                var input = new Input { MouseDelta = new Vector2(dx, 0f) };
                if (press is { } p) input.Pressed.Add((int)p);
                if (hold is { } h) input.Keys.Add((int)h);
                sw.Camera3D.HandleInput(input);
                w.Bus.SwapBuffers();
                return Assert.Single(w.Bus.Read<OperatorControlFrameEvent>().ToArray());
            }
            static float Deg(float r) => r * 180f / MathF.PI;

            float y0 = driver.Yaw;
            var locked = Frame(dx: -100f);
            Assert.Equal(driver.Yaw, locked.Yaw);                                            // locked: the mouse turns him
            float perPixel = (driver.Yaw - y0) / 100f;                                       // + = to his left
            Assert.True(perPixel > 0f);

            float body = driver.Yaw;
            var free = Frame(press: Raylib_cs.KeyboardKey.V);
            Assert.True(driver.LookFree);
            Assert.Equal(body, free.Yaw, 4);
            Assert.Contains(PossessionStation.Hud.LookFreeLine, PossessionStation.Hud.Lines(w, unit, 0, false, false, lookFree: driver.LookFree));
            Assert.Contains(PossessionStation.Hud.Lines(w, unit, 0, false, false, lookFree: true), l => l.Contains("A/D turn") && l.Contains("V lock look"));

            var within = Frame(dx: -(30f * MathF.PI / 180f) / perPixel);                    // 30° left: within the neck
            Assert.Equal(body, within.Yaw, 4);                                               // his body stays
            Assert.Equal(30f, Deg(driver.Yaw - body), 1);                                    // his look turned

            var past = Frame(dx: -(60f * MathF.PI / 180f) / perPixel);                      // 90° in all: 20° past the 70° neck
            Assert.Equal(70f, Deg(driver.Yaw - past.Yaw), 1);                                // the body is dragged, the look on the neck
            Assert.Equal(20f, Deg(past.Yaw - body), 1);

            sw.Camera3D.Update(0.5f);                                                        // half a second with A held
            float lookBefore = driver.Yaw;
            var turning = Frame(hold: Raylib_cs.KeyboardKey.A);
            Assert.Equal(0f, turning.Lateral);                                               // no strafe
            Assert.Equal(60f, Deg(turning.Yaw - past.Yaw), 1);                               // 120°/s for 0.5 s
            Assert.Equal(60f, Deg(driver.Yaw - lookBefore), 1);                              // his view turns with him

            var relocked = Frame(press: Raylib_cs.KeyboardKey.V);
            Assert.False(driver.LookFree);
            Assert.Equal(driver.Yaw, relocked.Yaw);                                          // locked: the body turns to the look

            Frame(press: Raylib_cs.KeyboardKey.V);                                           // free again …
            w.SetComponent(unit, new PossessedBy { StationNodeId = 77, Mode = PossessionMode.Fps, Epoch = 2 });
            station.Update(0.016f);
            w.SetComponent(unit, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 3 });
            station.Update(0.016f);
            var again = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
            Assert.NotSame(driver, again);
            Assert.True(again.LookFree);                                                     // … and still free in the next possession
        }
    }

    /// <summary>⭐ CE-1204 (LK-2) — the drag: a look within the neck leaves the body, one past it pulls the body behind it, either way
    /// round, across ±180°.</summary>
    [Fact]
    public void CE1204_DragBody_KeepsTheLookWithinTheNeck()
    {
        float neck = 70f * MathF.PI / 180f;
        Assert.Equal(0.3f, FpsCameraDriver.DragBody(0.3f, 0.3f + 1.0f, neck));
        Assert.Equal(1.6f - neck, FpsCameraDriver.DragBody(0f, 1.6f, neck), 5);
        Assert.Equal(-1.6f + neck, FpsCameraDriver.DragBody(0f, -1.6f, neck), 5);
        float across = FpsCameraDriver.DragBody(3.0f, -3.0f, neck);                         // 0.28 rad off, across the wrap
        Assert.Equal(3.0f, across);
    }
}
