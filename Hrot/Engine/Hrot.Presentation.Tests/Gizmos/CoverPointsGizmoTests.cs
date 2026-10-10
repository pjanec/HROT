using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Terrain;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐ <c>CE-3134</c> (Stage 7a, §3l C7) — <see cref="CoverPointsGizmo"/> draws the terrain's cover database on its own <c>Cover</c>
/// layer: one dot and one facing tick per point, cover and window firing positions in their own colours, at the point's Z;
/// nothing without a terrain database. 📄 docs/DESIGN_Building_Interiors.md §3l.
/// </summary>
public sealed class CoverPointsGizmoTests : IDisposable
{
    private readonly EntityRepository _w = new();
    private readonly DebugPrimitiveBuffer _draw = new();

    public void Dispose() => _w.Dispose();

    private DebugPrimitive[] Cover() => _draw.GetFrame().ToArray().Where(p => p.DebugLayer == DebugTraceLayers.Cover).ToArray();

    /// <summary>A box building whose south wall is one panel with a window.</summary>
    private static TerrainWorld House()
    {
        var fp = new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 8), new Vector2(0, 8) };
        TerrainWallPanel Wall(float z) => new()
        {
            A = new Vector2(0, 0), B = new Vector2(10, 0), Thickness = 0.3f, BaseZ = z, TopZ = z + 3f, Building = 0, Storey = 0,
            Openings = new[] { new TerrainOpening { Kind = TerrainOpeningKind.Window, At = 4f, Width = 1.2f, SillZ = z + 0.9f, HeadZ = z + 2.1f } },
        };
        return new TerrainWorld
        {
            BoundsMin = new Vector2(-50, -50), BoundsMax = new Vector2(50, 50),
            Panels = new[] { Wall(0f) },
            Buildings = new[] { new TerrainBuilding { Label = "box", Footprint = fp, StoreyZ = new[] { 0f, 3f } } },
        };
    }

    [Fact]
    public void CE3134_TheCoverDatabase_IsDrawnOnItsOwnLayer_ByKind()
    {
        var cover = TerrainCoverProvider.Build(House());
        Assert.Contains(cover.Points, p => p.Kind == CoverKind.WindowFiring);
        _w.SetSingletonManaged<ICoverProvider>(cover);

        new CoverPointsGizmo().Draw(_w, _draw);

        var drawn = Cover();
        var dots = drawn.Where(p => p.Shape == DebugPrimitiveShape.Sphere).ToArray();
        Assert.Equal(cover.Points.Count, dots.Length);
        Assert.Equal(cover.Points.Count, drawn.Count(p => p.Shape == DebugPrimitiveShape.Line));
        Assert.Equal(cover.Points.Count(p => p.Kind == CoverKind.WindowFiring),
            dots.Count(d => d.Color.Equals(CoverPointsGizmo.WindowColor)));
        Assert.Equal(cover.Points.Count(p => p.Kind == CoverKind.Cover), dots.Count(d => d.Color.Equals(CoverPointsGizmo.CoverColor)));
    }

    /// <summary>
    /// ⭐ <c>CE-3143</c> — the points beside a STANDING vehicle are drawn too (lighter green), from the same producer the cover query
    /// uses; a driving vehicle has none. A 4.5 × 1.8 car: 2 per long side + 1 per end = 6 (rounded up). 🔴 Red-proof: drop the
    /// <c>VehicleCover.StandingPoints</c> loop and the parked car draws nothing.
    /// </summary>
    [Fact]
    public void CE3143_TheCoverLayer_ShowsTheLivePointsBesideAStandingVehicle()
    {
        _w.RegisterComponent<SimTransform>();
        _w.RegisterComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>();
        _w.RegisterComponent<global::CarKinem.Core.VehicleParams>();
        _w.RegisterComponent<global::CarKinem.Core.VehicleState>();
        _w.RegisterComponent<StaticObstacle>();
        _w.SetSingletonManaged<ICoverProvider>(TerrainCoverProvider.Build(new TerrainWorld()));
        var car = _w.CreateEntity();
        _w.AddComponent(car, new SimTransform { Position = new Vector3(30, 30, 0), Rotation = Quaternion.Identity });
        _w.AddComponent(car, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = 2.4f, Height = 1.5f, CollisionLayer = 1 });
        _w.AddComponent(car, new global::CarKinem.Core.VehicleParams { Class = global::CarKinem.Core.VehicleClass.PersonalCar, Length = 4.5f, Width = 1.8f });
        _w.AddComponent(car, new global::CarKinem.Core.VehicleState { Speed = 0f });

        new CoverPointsGizmo().Draw(_w, _draw);
        var dots = Cover().Where(p => p.Shape == DebugPrimitiveShape.Sphere).ToArray();
        Assert.Equal(6, dots.Length);
        Assert.All(dots, d => Assert.Equal(CoverPointsGizmo.VehicleColor, d.Color));

        _w.SetComponent(car, new global::CarKinem.Core.VehicleState { Speed = 5f });
        _draw.Clear();
        new CoverPointsGizmo().Draw(_w, _draw);
        Assert.Empty(Cover());
    }

    [Fact]
    public void CE3134_NoTerrainCoverDatabase_DrawsNothing()
    {
        new CoverPointsGizmo().Draw(_w, _draw);                                                         // no singleton
        _w.SetSingletonManaged<ICoverProvider>(new ManualCoverProvider(new[] { new CoverPoint() }));   // not the terrain's
        new CoverPointsGizmo().Draw(_w, _draw);
        _w.SetSingletonManaged<ICoverProvider>(TerrainCoverProvider.Build(new TerrainWorld()));         // a terrain with no walls
        new CoverPointsGizmo().Draw(_w, _draw);
        Assert.Empty(Cover());
    }
}
