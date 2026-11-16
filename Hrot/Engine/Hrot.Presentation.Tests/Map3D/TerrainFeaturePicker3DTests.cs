using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis3D;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ CE-5011 C (docs/DESIGN_Terrain_Editor_UX.md §5 TE-16 ①; R-5035) — a placed tree is picked in 3-D by its CANOPY along the ray (the terrain mesh holds no
/// trees, so a click used to fall through to the ground behind it), answering the feature that made it; only while terrain authoring is open (R-5036).
/// 🔴 Red-proof (compiles): the picker ignoring the lock ⇒ the closed rail red; the label not mapped to the feature ⇒ the open rail red.
/// </summary>
public sealed class TerrainFeaturePicker3DTests : IDisposable
{
    private static readonly Guid Tree = Guid.Parse("c3000000-0000-4000-8000-000000000003");
    private readonly EntityRepository _w = new();

    public TerrainFeaturePicker3DTests()
    {
        _w.RegisterComponent<SimTransform>(); _w.RegisterComponent<NetworkIdentity>(); _w.RegisterComponent<TerrainFeature>(); _w.RegisterComponent<EntityGuid>();
        _w.RegisterManagedComponent<TerrainWorld>();
        _w.SetSingletonManaged(TerrainWorldParser.Parse("{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"properties\":{\"kind\":\"tree\",\"height\":10,\"label\":\""
            + Tree.ToString("D") + "/0\"},\"geometry\":{\"type\":\"Point\",\"coordinates\":[50,50]}}]}"));
        var e = _w.CreateEntity();
        _w.AddComponent(e, new SimTransform { Position = new Vector3(50, 50, 0), Rotation = Quaternion.Identity });
        _w.AddComponent(e, new NetworkIdentity(91));
        _w.AddComponent(e, new TerrainFeature { Kind = TerrainFeature.Tree });
        _w.AddComponent(e, new EntityGuid(Tree));
    }

    public void Dispose() => _w.Dispose();

    // an oblique ray from the south-west, 30 m up, at the crown 8 m up (it would meet the ground ~ 20 m behind the tree)
    private static (Vector3 O, Vector3 D) Oblique()
    {
        var o = new Vector3(20f, 20f, 30f); var target = new Vector3(50f, 50f, 8f);
        return (o, Vector3.Normalize(target - o));
    }

    [Fact]
    public void Open_AClickOnTheCanopy_PicksTheTreesFeature()
    {
        TerrainLock.SetOpen(_w, true);
        var picker = new TerrainFeaturePicker3D(() => _w);
        var (o, d) = Oblique();
        Assert.True(picker.TryPick(o, d, 500f, out var hit));
        Assert.Equal(PickKind.Entity, hit.Kind);
        Assert.Equal(91, hit.NetworkId);
        Assert.True(picker.HasBody(91));
        Assert.False(picker.TryPick(new Vector3(0, 0, 30), Vector3.Normalize(new Vector3(-1, 0, -1)), 500f, out _));   // looking away: nothing
    }

    [Fact]
    public void Closed_TheTreeIsNotPicked()
    {
        var picker = new TerrainFeaturePicker3D(() => _w);
        var (o, d) = Oblique();
        Assert.False(picker.TryPick(o, d, 500f, out _));
    }

    [Fact]
    public void TheCylinder_TakesTheNearestEntry_SideOrCrown()
    {
        // from the side, level: enters at x = 50 - 2.4
        Assert.True(TerrainFeaturePicker3D.RayCylinder(new Vector3(0, 50, 5), new Vector3(1, 0, 0), new Vector2(50, 50), 2.4f, 0f, 10f, out float t));
        Assert.Equal(47.6f, t, 2);
        // straight down onto the crown
        Assert.True(TerrainFeaturePicker3D.RayCylinder(new Vector3(50, 50, 30), new Vector3(0, 0, -1), new Vector2(50, 50), 2.4f, 0f, 10f, out t));
        Assert.Equal(20f, t, 2);
        // steep, from above and aside: the crown's top cap is met before the side
        var d = Vector3.Normalize(new Vector3(1, 0, -4));
        Assert.True(TerrainFeaturePicker3D.RayCylinder(new Vector3(46, 50, 26), d, new Vector2(50, 50), 2.4f, 0f, 10f, out t));
        Assert.Equal(10f, new Vector3(46, 50, 26).Z + d.Z * t, 2);
    }
}
