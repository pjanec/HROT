using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common;
using Hrot.UI.Common.Map3D;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ <c>CE-4670</c> (📄 docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §7d; the acceptance spec is the mockup docs/UX/vehicle-openings-lights.svg A, B) —
/// the real frame draws a vehicle's FITTINGS from its replicated state: an IFV (the Afv class: a rear ramp + three hatches) with its ramp part way down and the
/// commander's head out of his hatch, and a T-72 with its head and tail lights lit. The counters and the pixels both say it; a closed vehicle draws its
/// openings shut and no light. Frames under <c>HROT_RAIL_SHOTS</c> (never git). ⛔ Skips without a display: <c>xvfb-run -a</c>.
/// 🔴 Red-proof (compiles): <c>DrawFittings</c> returning at once ⇒ no opening, no crewman, no light drawn.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class VehicleFittingsFrameRail
{
    private static int SlotOf(TkbTemplate t, string socket)
    {
        var o = VehicleOpeningsCatalog.Of(t);
        for (int i = 0; i < o.Length; i++) if (o[i].Socket == socket) return i;
        return -1;
    }

    private static int GroupsOf(TkbTemplate t, params LightKind[] kinds)
    {
        var l = VehicleLightsCatalog.Of(t);
        int mask = 0;
        for (int i = 0; i < l.Length; i++) if (Array.IndexOf(kinds, l[i].Kind) >= 0) mask |= 1 << i;
        return mask;
    }

    [SkippableFact]
    public void TheRampTheHeadOutAndTheLights_AreDrawnFromTheReplicatedState()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<VehicleOpenings>();
        world.RegisterComponent<VehicleLights>();
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        const double Now = 100.0;
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = Now });
        var query = WorldQuery.Of(world)!;
        var spot = MineClearerModelRail.OpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        Entity Put(long type, float dx, float dy)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y)), Rotation = Quaternion.Identity });
            world.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
            return e;
        }
        var ifvType = tkb.GetByType(TkbEntityTypes.IFV_Bradley)!;
        var tankType = tkb.GetByType(TkbEntityTypes.Tank_T72)!;
        var ifv = Put(TkbEntityTypes.IFV_Bradley, 0f, 0f);
        var tank = Put(TkbEntityTypes.Tank_T72, 0f, 14f);

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        sw.Set(true, animate: false);
        float ground = G(spot.X, spot.Y);
        void Look(float dx, float dy, float dist, float yaw, float pitch)
            => sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X + dx, spot.Y + dy, ground + 1.5f), dist, yaw, pitch);
        void Render() { frame.Step(canvas.Draw); frame.Step(canvas.Draw); }
        Image Shot(string name) { Render(); frame.Screenshot(Path.Combine(shots, name)); return Raylib.LoadImageFromScreen(); }

        // ① shut: the openings are drawn closed (the lids flush), nothing lit, nobody out
        Look(0f, 0f, 14f, 0.84f, -0.25f);   // behind and to the side of the IFV: its rear ramp and its roof
        var shut = Shot("fittings-1-shut.png");
        Assert.Equal(2, bodies.BodiesDrawn);
        Assert.True(bodies.OpeningsDrawn >= 7, $"the IFV's 4 and the T-72's 3 openings, drawn shut: {bodies.OpeningsDrawn}");
        Assert.Equal(0, bodies.CrewOutDrawn);
        Assert.Equal(0, bodies.LightsDrawn);

        // ② the ramp half way down, the commander's head out (the state the boarding arbiter replicates)
        int ramp = SlotOf(ifvType, "ramp_rear"), commander = SlotOf(ifvType, "hatch_commander");
        Assert.True(ramp >= 0 && commander >= 0);
        var rampSeconds = VehicleOpeningsCatalog.Of(ifvType)[ramp].OpenSeconds;
        var open = new VehicleOpenings { Count = (byte)VehicleOpeningsCatalog.Of(ifvType).Length };
        open.Slots[ramp] = new OpeningSlot { State = OpeningState.Opening, Since = Now - rampSeconds * 0.5 };
        open.Slots[commander] = new OpeningSlot { State = OpeningState.Open, Since = Now - 10.0, CrewExposed = true };
        world.AddComponent(ifv, open);
        var opened = Shot("fittings-2-ramp-and-head-out.png");
        Assert.Equal(1, bodies.CrewOutDrawn);
        int changed = Changed(shut, opened);
        Assert.True(changed > 400, $"the ramp and the man show: {changed} px changed");

        // ③ the T-72's head and tail lights
        world.AddComponent(tank, new VehicleLights { OnMask = (uint)GroupsOf(tankType, LightKind.Head, LightKind.Tail), Discipline = LightDiscipline.Normal, Since = Now - 5.0 });
        Look(0f, 14f, 13f, 2.54f, -0.15f);   // the T-72 from its front quarter
        Render();
        Assert.Equal(4, bodies.LightsDrawn);   // two head, two tail
        var lit = Shot("fittings-3-t72-lit.png");
        Raylib.UnloadImage(shut); Raylib.UnloadImage(opened); Raylib.UnloadImage(lit);
    }

    private static int Changed(Image a, Image b)
    {
        int n = 0;
        for (int y = 0; y < a.Height; y += 2)
            for (int x = 0; x < a.Width; x += 2)
            {
                Color p = Raylib.GetImageColor(a, x, y), q = Raylib.GetImageColor(b, x, y);
                if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) > 40) n++;
            }
        return n;
    }
}
