using System;
using System.Linq;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Systems;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Terrain;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐ <c>CE-3117</c> (R-226) — the debug-trace gizmos draw what their RECORDED components hold, on their own layer bits, and age it by
/// sim time. 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5a.
/// </summary>
public sealed class DebugTraceGizmoTests : IDisposable
{
    private readonly EntityRepository _w = new();
    private readonly DebugPrimitiveBuffer _draw = new();

    public DebugTraceGizmoTests()
    {
        _w.RegisterComponent<SimTransform>();
        Now(10.0);
    }

    public void Dispose() => _w.Dispose();

    private void Now(double t) => _w.SetSingletonUnmanaged(new GlobalTime { TotalTime = t });

    private DebugPrimitive[] Frame(byte layer) => _draw.GetFrame().ToArray().Where(p => p.DebugLayer == layer).ToArray();

    private void Burst(double at)
    {
        var t = default(DetonationTraces);
        ref var d = ref t.SlotsRW()[0];
        d.Time = at; d.Burst = new Vector3(0, 0, 0.1f); d.FragmentRadius = 15f; d.BlastInjuryRadius = 6f;
        d.RayCount = 2;
        d.Rays[0] = new DetonationRay { To = new Vector3(0, 8, 1.5f), Transmission = 1f };
        d.Rays[1] = new DetonationRay { To = new Vector3(2, 8, 0.3f), StopAt = new Vector3(1, 4, 0.2f), HasStop = 1, Transmission = 0f };
        d.TargetCount = 1;
        d.Targets[0] = new DetonationTarget { At = new Vector3(0, 8, 0), Exposure = 0.5f, Damage = 30f };
        t.Count = 1;
        _w.SetSingletonUnmanaged(in t);
    }

    [Fact]
    public void CE3117_ABurst_DrawsItsRaysAndRings_ForOneSecondOfSimTime()
    {
        Burst(at: 9.6);
        new DetonationGizmo().Draw(_w, _draw);
        var blast = Frame(DebugTraceLayers.Blast);
        Assert.Equal(3, blast.Count(p => p.Shape == DebugPrimitiveShape.Sphere));   // fragment ring, blast ring, the burst
        // the clear ray (1 line) + the blocked ray to its stop (1 line) + the ✕ at the stop (2 lines)
        Assert.Equal(4, blast.Count(p => p.Shape == DebugPrimitiveShape.Line));
        Assert.Contains(blast, p => p.Shape == DebugPrimitiveShape.Line && p.LineEnd == new Vector3(1, 4, 0.2f));
        Assert.Single(blast, p => p.Shape == DebugPrimitiveShape.Text);

        _draw.Clear();
        Now(10.7);                                   // 1.1 s later: gone
        new DetonationGizmo().Draw(_w, _draw);
        Assert.Empty(Frame(DebugTraceLayers.Blast));
    }

    [Fact]
    public void CE3117_AShotSound_ExpandsFromItsSource_AndAHeardEstimateShowsItsUncertainty()
    {
        _w.RegisterComponent<AcousticEmitter>();
        _w.RegisterComponent<HeardTraces>();
        var shooter = _w.CreateEntity();
        _w.AddComponent(shooter, new SimTransform { Position = new Vector3(50, 0, 0) });
        _w.AddComponent(shooter, new AcousticEmitter
        {
            FiringAudibleRange = 200f, ShotTimeLeft = SoundEmissionSystem.SoundLingerSeconds / 2f, ShotX = 50, ShotY = 0,
        });
        var listener = _w.CreateEntity();
        _w.AddComponent(listener, new SimTransform { Position = new Vector3(0, 0, 0) });
        var heard = default(HeardTraces);
        heard.Add(new HeardTrace { Time = 9.8, At = new Vector3(48, 3, 0), Radius = 5f, SourceClass = 4 });
        _w.AddComponent(listener, heard);

        new HearingGizmo().Draw(_w, _draw);
        var layer = Frame(DebugTraceLayers.Hearing);
        var ring = Assert.Single(layer, p => p.Shape == DebugPrimitiveShape.Sphere && p.SphereCenter == new Vector3(50, 0, 0));
        Assert.Equal(100f, ring.SphereRadius, 3);    // half the linger gone = half the audible range
        Assert.Single(layer, p => p.Shape == DebugPrimitiveShape.Sphere && p.SphereCenter == new Vector3(48, 3, 0) && p.SphereRadius == 5f);
        Assert.Single(layer, p => p.Shape == DebugPrimitiveShape.Line && p.LineEnd == new Vector3(48, 3, 0));
    }

    [Fact]
    public void CE3117_ThePlannedPath_DrawsTheLookaheadPointTheControllerSteersAt()
    {
        _w.RegisterComponent<NavState>();
        _w.RegisterComponent<PathTrace>();
        _w.RegisterComponent<VehicleParams>();
        _w.RegisterComponent<VehicleState>();
        var mover = _w.CreateEntity();
        var trace = new PathTrace { TrajectoryId = 1, TotalLength = 100f, Count = 2 };
        trace.PointsRW()[0] = new PathTracePoint { Position = new Vector3(0, 0, 0), S = 0f };
        trace.PointsRW()[1] = new PathTracePoint { Position = new Vector3(100, 0, 0), S = 100f };
        _w.AddComponent(mover, trace);
        _w.AddComponent(mover, new NavState { Mode = KinematicsMode.CustomTrajectory, TrajectoryId = 1, ProgressS = 20f });
        var p = new VehicleParams { WheelBase = 0.5f, LookaheadTimeMin = 0.5f };
        _w.AddComponent(mover, p);
        _w.AddComponent(mover, new VehicleState { Speed = 10f });

        new PlannedPathGizmo().Draw(_w, mover, _draw);

        float ahead = CarKinematicsSystem.PathLookahead(in p, 10f);   // 5 m
        var spheres = Frame(DebugTraceLayers.Paths).Where(x => x.Shape == DebugPrimitiveShape.Sphere).ToArray();
        Assert.Contains(spheres, x => x.SphereCenter == new Vector3(20, 0, 0));            // the progress point
        Assert.Contains(spheres, x => x.SphereCenter == new Vector3(20 + ahead, 0, 0));    // the look-ahead point
    }

    [Fact]
    public void CE3117_ADoorLeaf_LiesAlongTheWallClosed_AndSwingsNinetyDegreesOpen()
    {
        // the leaf footprint BuildDoorLeaves makes for a 1 m door from (0,0) along +X: { p0−n, p1−n, p1+n, p0+n }
        var fp = new[] { new Vector2(0, -0.02f), new Vector2(1, -0.02f), new Vector2(1, 0.02f), new Vector2(0, 0.02f) };
        var (hinge, closedTip) = DoorLeafGizmo.Leaf(fp);
        Assert.Equal(new Vector2(0, 0), hinge);
        Assert.Equal(new Vector2(1, 0), closedTip);
        Assert.Equal(new Vector2(0, 1), DoorLeafGizmo.OpenTip(hinge, closedTip));
    }
}
