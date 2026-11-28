using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Weather;
using Hrot.UI.Common.Effects;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ SD-S6a (docs/designs/devices/DESIGN_Simulated_Devices.md §3.8a D-161) — what <see cref="LaserLayer3D"/> draws, without a GL context: the beam's pieces from
/// the device's world origin through its spot points (or to the max range), their brightness from <see cref="LaserVisibility"/> (never a table of its own),
/// fading along the beam; a glow per spot; a Preview spot too; nothing when the laser is off, for an invisible band, or under thermal; and 0 B per steady frame.
/// The PICTURE is <see cref="LaserBeamFrameRail"/>'s.
/// </summary>
public sealed class LaserLayer3DTests
{
    internal sealed class Fixture
    {
        public readonly EntityRepository World = new();
        public readonly LaserLayer3D Layer;
        public Entity Env;

        public Fixture()
        {
            World.RegisterComponent<SimTransform>();
            World.RegisterComponent<LaserPointing>();
            World.RegisterComponent<LaserSpot>();
            World.RegisterComponent<DevicePose>();
            World.RegisterComponent<WeatherConditions>();
            World.RegisterComponent<ScenarioClock>();
            World.SetSingletonUnmanaged(new GlobalTime { TotalTime = 10.0 });
            Layer = new LaserLayer3D(() => World);
        }

        /// <summary>The global weather: <paramref name="visibilityM"/> and <paramref name="humidity"/>.</summary>
        public void Weather(float visibilityM, float humidity = 0.5f)
        {
            var w = WeatherConditions.Standard;
            w.VisibilityM = visibilityM; w.RelativeHumidity = humidity;
            if (Env.IsNull)
            {
                Env = World.CreateEntity();
                World.AddComponent(Env, new ScenarioClock { StartUtcTicks = new DateTime(2026, 6, 1, 22, 0, 0, DateTimeKind.Utc).Ticks });
                World.AddComponent(Env, w);
            }
            else World.SetComponent(Env, w);
        }

        /// <summary>An emitter at <paramref name="at"/> facing +x, band <paramref name="band"/>, with its spot <paramref name="spotRange"/> m ahead (0 = no return).</summary>
        public Entity Emitter(Vector3 at, byte band, float spotRange = 60f, bool on = true, bool preview = false, float power = 1f)
        {
            var e = World.CreateEntity();
            World.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            World.AddComponent(e, new LaserPointing { On = on ? (byte)1 : (byte)0, Band = band, Power = power, Seq = 1 });
            var spot = new LaserSpot { PointingSeq = 1, Preview = preview ? (byte)1 : (byte)0 };
            if (spotRange > 0f)
            {
                spot.Count = 1;
                spot.Points[0] = new LaserSpotPoint { Point = at + new Vector3(spotRange, 0, 0), Range = spotRange, Strength = LaserSpots.StrengthAt(power, spotRange), Part = -1 };
            }
            World.AddComponent(e, spot);
            return e;
        }
    }

    [Theory]
    [InlineData(LaserBands.Visible, LaserSensor.Eye, true)]
    [InlineData(LaserBands.Visible, LaserSensor.NightVision, true)]
    [InlineData(LaserBands.Visible, LaserSensor.Thermal, false)]
    [InlineData(LaserBands.EyeSafeRangefinder, LaserSensor.Eye, false)]
    [InlineData(LaserBands.EyeSafeRangefinder, LaserSensor.NightVision, false)]
    [InlineData(LaserBands.Designator, LaserSensor.Eye, false)]
    [InlineData(LaserBands.Designator, LaserSensor.NightVision, true)]
    [InlineData(LaserBands.NearInfrared, LaserSensor.Eye, false)]
    [InlineData(LaserBands.NearInfrared, LaserSensor.NightVision, true)]
    [InlineData(LaserBands.NearInfrared, LaserSensor.Thermal, false)]
    public void D161_WhatIsDrawn_IsWhatTheRuleSees(byte band, LaserSensor sensor, bool seen)
    {
        var f = new Fixture();
        f.Weather(2_000f);
        var at = new Vector3(100, 200, 1.5f);
        f.Emitter(at, band);
        f.Layer.Resolve(sensor);
        Assert.Equal(seen, f.Layer.Pieces.Count > 0);
        Assert.Equal(seen ? 1 : 0, f.Layer.Spots.Count);
        if (!seen) return;
        Assert.Equal(at, f.Layer.Pieces[0].From);                                   // from the device's world origin
        Assert.Equal(at + new Vector3(60, 0, 0), f.Layer.Pieces[^1].To);              // … to the spot
        Assert.Equal(LaserVisibility.Spot(band, LaserSpots.StrengthAt(1f, 60f), sensor), f.Layer.Spots[0].Intensity, 5);
        float aerosol = LaserVisibility.Aerosol(2_000f, 0.5f);
        Assert.True(f.Layer.Pieces[0].Intensity <= LaserVisibility.Beam(band, 1f, sensor, aerosol) + 1e-5f);
    }

    /// <summary>The designator under night vision is FAINT next to a near-IR pointer (× 0.25), its beam and its spot alike.</summary>
    [Fact]
    public void D159_TheDesignator_IsFaintUnderNightVision()
    {
        var f = new Fixture(); f.Weather(2_000f);
        f.Emitter(new Vector3(0, 0, 1), LaserBands.Designator);
        f.Layer.Resolve(LaserSensor.NightVision);
        float designatorBeam = f.Layer.Pieces[0].Intensity, designatorSpot = f.Layer.Spots[0].Intensity;
        var g = new Fixture(); g.Weather(2_000f);
        g.Emitter(new Vector3(0, 0, 1), LaserBands.NearInfrared);
        g.Layer.Resolve(LaserSensor.NightVision);
        Assert.Equal(0.25f * g.Layer.Pieces[0].Intensity, designatorBeam, 4);
        Assert.Equal(0.25f * g.Layer.Spots[0].Intensity, designatorSpot, 4);
    }

    /// <summary>D-160 — a hazy evening's beam is brighter than a clear one's; along the beam it fades; the spot does not care about the haze.</summary>
    [Fact]
    public void D160_AHazyBeam_IsBrighter_AndFadesAlongItsLength()
    {
        var f = new Fixture(); f.Weather(20_000f);
        f.Emitter(new Vector3(0, 0, 1), LaserBands.Visible, spotRange: 600f);
        f.Layer.Resolve(LaserSensor.Eye);
        float clear = f.Layer.Pieces[0].Intensity, clearSpot = f.Layer.Spots[0].Intensity;
        f.Weather(1_500f, 0.9f);
        f.Layer.Resolve(LaserSensor.Eye);
        float hazy = f.Layer.Pieces[0].Intensity;
        Assert.True(hazy > 5f * clear, $"hazy {hazy} vs clear {clear}");
        Assert.Equal(clearSpot, f.Layer.Spots[0].Intensity, 5);
        for (int i = 1; i < f.Layer.Pieces.Count; i++)
            Assert.True(f.Layer.Pieces[i].Intensity < f.Layer.Pieces[i - 1].Intensity, "the scatter fades along the beam");
    }

    /// <summary>No return within range: the beam runs out to the max range; the device's pose and direction set where it goes.</summary>
    [Fact]
    public void D161_NoSpot_TheBeamRunsToMaxRange_AlongTheDevicesPose()
    {
        var f = new Fixture(); f.Weather(1_000f);
        var e = f.Emitter(new Vector3(0, 0, 2), LaserBands.Visible, spotRange: 0f);
        var yaw90 = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        f.World.AddComponent(e, new DevicePose { Orientation = yaw90 });            // the device turned left: the beam goes +y
        f.Layer.Resolve(LaserSensor.Eye);
        Assert.Empty(f.Layer.Spots);
        Assert.NotEmpty(f.Layer.Pieces);
        var dir = Vector3.Normalize(f.Layer.Pieces[0].To - f.Layer.Pieces[0].From);
        Assert.True(dir.Y > 0.999f, $"beam direction {dir}");
        var p = f.World.GetComponentRO<LaserPointing>(e);
        p.Direction = new Vector3(0, -1, 0);                                      // the pointing turned right, in the device's frame
        f.World.SetComponent(e, p);
        f.Layer.Resolve(LaserSensor.Eye);
        dir = Vector3.Normalize(f.Layer.Pieces[0].To - f.Layer.Pieces[0].From);
        Assert.True(dir.X > 0.999f, $"device left × pointing right = forward: {dir}");
    }

    /// <summary>A Preview spot (written while paused) is drawn — the map observes; an emitter that is off draws nothing.</summary>
    [Fact]
    public void D161_APreviewSpot_IsDrawn_AndAnOffLaserIsNot()
    {
        var f = new Fixture(); f.Weather(2_000f);
        f.Emitter(new Vector3(0, 0, 1), LaserBands.Visible, preview: true);
        f.Layer.Resolve(LaserSensor.Eye);
        Assert.Single(f.Layer.Spots);
        var g = new Fixture(); g.Weather(2_000f);
        g.Emitter(new Vector3(0, 0, 1), LaserBands.Visible, on: false);
        g.Layer.Resolve(LaserSensor.Eye);
        Assert.Empty(g.Layer.Pieces); Assert.Empty(g.Layer.Spots);
        Assert.Equal(0, g.Layer.EmittersDrawn);
    }

    /// <summary>⛔ 0 B per steady frame: after a warm-up, 300 resolves of two emitters allocate nothing.</summary>
    [Fact]
    public void TheLayer_AllocatesNothing_PerSteadyFrame()
    {
        var f = new Fixture(); f.Weather(2_000f, 0.9f);
        f.Emitter(new Vector3(0, 0, 1), LaserBands.Visible);
        f.Emitter(new Vector3(0, 10, 1), LaserBands.NearInfrared, spotRange: 0f);
        for (int i = 0; i < 30; i++) { f.Layer.Resolve(LaserSensor.Eye); f.Layer.Resolve(LaserSensor.NightVision); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 300; i++) { f.Layer.Resolve(LaserSensor.Eye); f.Layer.Resolve(LaserSensor.NightVision); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(2, f.Layer.EmittersDrawn);
    }

    /// <summary>The shared attach builds it (every map host has it — the "no host builds its own" half is in <c>EveryMapHostAttachesTheSharedLayersTests</c>).</summary>
    [Fact]
    public void TheSharedAttach_BuildsTheLaserLayer()
    {
        var pack = File.ReadAllText(Path.Combine(Fdp.Testing.TestRepo.Root, "Hrot/Engine/Hrot.Presentation/ScenarioEditor/Map/MapInteractionPack.cs"));
        Assert.Contains("new Hrot.UI.Common.Effects.LaserLayer3D(worldProvider", pack);
        Assert.Contains("canvas.AddLayer(lasers);", pack);
    }
}
