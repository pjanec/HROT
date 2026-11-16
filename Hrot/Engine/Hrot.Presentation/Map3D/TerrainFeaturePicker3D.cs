using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis3D;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-5011 C (docs/DESIGN_Terrain_Editor_UX.md §5 TE-16 ①; R-5035) — a PLACED tree is picked in 3-D by its own volume along the RAY. 📐 Measured: the 3-D
/// pickers held the terrain mesh and the entity boxes, not trees, so a click on a canopy fell through to the ground behind it (metres off from an oblique view).
/// The live terrain's tree prisms carry the label <c>"&lt;feature guid&gt;/&lt;i&gt;"</c> of the feature that made them (a lone tree, a Row, a Group): each is a
/// vertical canopy cylinder (<see cref="CanopyRadius"/>, base to top). A hit answers the FEATURE's entity (its own position — the point its 2-D pick lies on).
/// ⭐ Active only while terrain authoring is open (R-5036 — a locked tree is not picked at all). The index is rebuilt (cold) when the live terrain or the feature set
/// changes; a pick allocates nothing.
/// </summary>
public sealed class TerrainFeaturePicker3D : IBodyPicker3D
{
    /// <summary>The canopy radius a click hits (the 2-D plan draws a lone tree's canopy at 2.4 m — <c>TerrainPlan2D.CanopyRadius</c>).</summary>
    public const float CanopyRadius = 2.4f;

    private readonly Func<EntityRepository?> _world;
    private readonly List<(Vector2 At, float Z0, float Z1, int Net, Vector3 Anchor)> _trees = new();
    private readonly HashSet<long> _nets = new();
    private TerrainWorld? _builtFor;
    private uint _version = uint.MaxValue;
    private EntityRepository? _builtWorld;

    public TerrainFeaturePicker3D(Func<EntityRepository?> world) => _world = world ?? throw new ArgumentNullException(nameof(world));

    public PickFilter Kind => PickFilter.Entities;

    /// <summary>Trees indexed (for rails).</summary>
    public int TreeCount => _trees.Count;

    /// <inheritdoc/>
    public bool HasBody(long networkId) => _nets.Contains(networkId);

    /// <inheritdoc/>
    public bool TryPick(Vector3 origin, Vector3 direction, float maxDistance, out PickResult hit)
    {
        hit = PickResult.None;
        var w = _world();
        if (w is null || !TerrainLock.IsOpen(w)) return false;
        Refresh(w);
        float best = maxDistance;
        int found = -1;
        for (int i = 0; i < _trees.Count; i++)
        {
            var t = _trees[i];
            if (RayCylinder(origin, direction, t.At, CanopyRadius, t.Z0, t.Z1, out float d) && d < best) { best = d; found = i; }
        }
        if (found < 0) return false;
        hit = new PickResult(_trees[found].Anchor, PickKind.Entity, best, _trees[found].Net);
        return true;
    }

    private void Refresh(EntityRepository w)
    {
        var live = w.HasSingletonManaged<TerrainWorld>() ? w.GetSingletonManaged<TerrainWorld>() : null;
        if (ReferenceEquals(live, _builtFor) && ReferenceEquals(w, _builtWorld) && w.GlobalVersion == _version) return;
        _builtFor = live; _builtWorld = w; _version = w.GlobalVersion;
        _trees.Clear(); _nets.Clear();
        if (live is null || !w.IsComponentTypeRegistered<TerrainFeature>() || !w.IsComponentTypeRegistered<EntityGuid>()
            || !w.IsComponentTypeRegistered<NetworkIdentity>()) return;
        // the placed features by guid (cold: only on a change)
        var byGuid = new Dictionary<string, (int Net, Vector3 Anchor)>();
        foreach (var e in w.Query().With<TerrainFeature>().With<EntityGuid>().With<NetworkIdentity>().With<SimTransform>().Build())
            byGuid[w.GetComponentRO<EntityGuid>(e).Value.ToString("D")] = (w.GetComponentRO<NetworkIdentity>(e).Value, w.GetComponentRO<SimTransform>(e).Position);
        if (byGuid.Count == 0) return;
        foreach (var p in live.Prisms)
        {
            if (p.Kind != TerrainPrismKind.Tree || p.Label is not { Length: > 36 } label) continue;
            int slash = label.IndexOf('/');
            if (slash != 36 || !byGuid.TryGetValue(label.Substring(0, 36), out var owner)) continue;
            var c = (p.Min + p.Max) * 0.5f;
            _trees.Add((c, p.BaseZ, p.TopZ, owner.Net, owner.Anchor));
            _nets.Add(owner.Net);
        }
    }

    /// <summary>The ray against a vertical cylinder (axis at <paramref name="at"/>, radius <paramref name="r"/>, from <paramref name="z0"/> to <paramref name="z1"/>): the nearest entry distance.</summary>
    internal static bool RayCylinder(Vector3 o, Vector3 d, Vector2 at, float r, float z0, float z1, out float t)
    {
        t = 0f;
        float ox = o.X - at.X, oy = o.Y - at.Y;
        float a = d.X * d.X + d.Y * d.Y, b = 2f * (ox * d.X + oy * d.Y), c = ox * ox + oy * oy - r * r;
        if (a < 1e-9f)   // a vertical ray: inside the circle ⇒ it meets the top (from above) or the bottom
        {
            if (c > 0f) return false;
            float zt = d.Z < 0f ? z1 : z0;
            t = (zt - o.Z) / d.Z;
            return t >= 0f;
        }
        float best = float.PositiveInfinity;
        float disc = b * b - 4f * a * c;
        if (disc >= 0f)
        {
            float t0 = (-b - MathF.Sqrt(disc)) / (2f * a);   // the side ENTRY
            float z = o.Z + d.Z * t0;
            if (t0 >= 0f && z >= z0 && z <= z1) best = t0;
        }
        // the caps (looking down onto the crown, or up at it)
        if (MathF.Abs(d.Z) > 1e-9f)
            foreach (float zc in stackalloc float[] { z1, z0 })
            {
                float tc = (zc - o.Z) / d.Z;
                if (tc < 0f || tc >= best) continue;
                float x = ox + d.X * tc, y = oy + d.Y * tc;
                if (x * x + y * y <= r * r) best = tc;
            }
        if (float.IsPositiveInfinity(best)) return false;
        t = best;
        return true;
    }
}
