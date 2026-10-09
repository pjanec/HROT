using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Terrain;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐ <c>CE-3133</c> (R-233) — <see cref="NavmeshGizmo"/> draws the baked navmesh's polygon outlines on its own <c>Navmesh</c> layer,
/// the layer the setting chooses (Infantry by default), a doorway polygon in its door's state colour; nothing where no navmesh is
/// baked. 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5c.
/// </summary>
public sealed class NavmeshGizmoTests : IDisposable
{
    private readonly EntityRepository _w = new();
    private readonly DebugPrimitiveBuffer _draw = new();

    public void Dispose() => _w.Dispose();

    private DebugPrimitive[] Lines() => _draw.GetFrame().ToArray()
        .Where(p => p.DebugLayer == DebugTraceLayers.Navmesh && p.Shape == DebugPrimitiveShape.Line).ToArray();

    /// <summary>A provider whose Infantry layer is a square (ground) plus a triangle (door 0), and whose Vehicle layer is one square.</summary>
    private sealed class Baked : INavmeshProvider, INavmeshDebugGeometry
    {
        private static readonly NavmeshDebugMesh Infantry = new(NavLayerMask.Infantry, 1,
            new[] { new Vector3(0, 0, 0), new Vector3(4, 0, 0), new Vector3(4, 4, 0), new Vector3(0, 4, 0),
                    new Vector3(4, 0, 0), new Vector3(5, 0, 0), new Vector3(4, 1, 0) },
            new[] { 0, 4, 7 }, new[] { -1, 0 });
        private static readonly NavmeshDebugMesh Vehicle = new(NavLayerMask.Vehicle, 1,
            new[] { new Vector3(0, 0, 0), new Vector3(8, 0, 0), new Vector3(8, 8, 0), new Vector3(0, 8, 0) },
            new[] { 0, 4 }, new[] { -1 });

        public NavmeshDebugMesh? DebugMesh(NavLayerMask layer)
            => layer == NavLayerMask.Infantry ? Infantry : layer == NavLayerMask.Vehicle ? Vehicle : null;

        public bool IsWalkable(Vector3 position, uint layerMask = 0xFFFFFFFF) => true;
        public bool ProjectToNavmesh(Vector3 position, out Vector3 snapped, uint layerMask = 0xFFFFFFFF) { snapped = position; return true; }
        public int SampleNavmeshPoints(Vector3 center, float radius, Span<Vector3> results, uint layerMask = 0xFFFFFFFF) => 0;
        public bool PathExists(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => true;
        public float PathCost(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => Vector3.Distance(from, to);
        public uint QueryVersion() => 1;
        public int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask = 0xFFFFFFFF) => 0;
    }

    [Fact]
    public void CE3133_TheInfantryNavmesh_IsDrawnByDefault_ADoorwayInItsDoorColour()
    {
        _w.SetSingletonManaged<INavmeshProvider>(new Baked());

        new NavmeshGizmo().Draw(_w, _draw);   // no settings registry: the default, Infantry

        var lines = Lines();
        Assert.Equal(4 + 3, lines.Length);                                                       // a square and a triangle, closed
        Assert.Equal(4, lines.Count(l => l.Color.Equals(NavmeshGizmo.InfantryColor)));
        Assert.Equal(3, lines.Count(l => l.Color.Equals(DoorLeafGizmo.ColorOf(TerrainDoorState.Open))));   // no terrain: as authored, open
        Assert.All(lines, l => Assert.Equal(NavmeshGizmo.Lift, l.LineStart.Z, 3));
    }

    [Theory]
    [InlineData(NavmeshDrawLayers.Vehicle, 0, 4)]
    [InlineData(NavmeshDrawLayers.All, 7, 4)]
    public void CE3133_TheSetting_ChoosesTheLayer(NavmeshDrawLayers choice, int infantry, int vehicle)
    {
        _w.SetSingletonManaged<INavmeshProvider>(new Baked());
        var settings = new GizmoSettingsRegistry();
        NavmeshLayerSetting.Register(settings);
        NavmeshLayerSetting.Set(settings, choice);

        new NavmeshGizmo(settings).Draw(_w, _draw);

        var lines = Lines();
        Assert.Equal(vehicle, lines.Count(l => l.Color.Equals(NavmeshGizmo.VehicleColor)));
        Assert.Equal(infantry, lines.Length - vehicle);
    }

    [Fact]
    public void CE3133_NoBakedNavmesh_DrawsNothing()
    {
        new NavmeshGizmo().Draw(_w, _draw);                                          // no provider (CGF, IG, Replay Browser)
        var node = new SwitchableNavmeshProvider();
        _w.SetSingletonManaged<INavmeshProvider>(node);
        new NavmeshGizmo().Draw(_w, _draw);                                          // a node before its terrain bakes
        _w.SetSingletonManaged<INavmeshProvider>(new Fdp.Toolkit.Navigation.EngineBacked.EngineBackedNavmeshProvider());
        new NavmeshGizmo().Draw(_w, _draw);                                          // a provider that cannot export
        Assert.Empty(Lines());
    }
}
