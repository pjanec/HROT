using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-1068 (docs/DESIGN_Map_3D_Mode.md §6k) — a door as it really is in the 3-D map: its LEAF, a wooden slab the size of the
/// doorway (the terrain's <see cref="TerrainWorld.DoorLeaves"/>: hinge → closed tip, sill → head, 5 cm thick), turned about its
/// HINGE by the door's live state — closed and locked in the wall, open swung 90° to the side <c>DoorLeafGizmo</c> opens it to,
/// destroyed gone. The door ENTITY (<see cref="TerrainObjectKey"/> + <see cref="DoorState"/>, DESIGN_Building_Interiors.md §3j) is
/// what the map draws, picks and selects; its key finds the leaf.
/// </summary>
public static class DoorLeaf3D
{
    /// <summary>How far an open door stands from its wall (radians).</summary>
    public const float OpenAngle = MathF.PI / 2f;

    /// <summary>How long a door takes to swing fully open or shut (s, sim time).</summary>
    public const float SwingSeconds = 0.8f;

    /// <summary>The swing angle a state asks for; null for a destroyed door (nothing is drawn).</summary>
    public static float? TargetAngle(TerrainDoorState state) => state switch
    {
        TerrainDoorState.Open => OpenAngle,
        TerrainDoorState.Destroyed => null,
        _ => 0f,
    };

    /// <summary>
    /// The leaf of door entity <paramref name="e"/> and its live state: false when <paramref name="e"/> is not a door, or this host
    /// has no terrain that defines it (then there is nothing to draw — never an unknown box).
    /// </summary>
    public static bool TryLeaf(EntityRepository world, Entity e, out TerrainPrism leaf, out TerrainDoorState state)
    {
        leaf = null!; state = TerrainDoorState.Closed;
        if (!IsDoor(world, e) || !world.HasSingletonManaged<TerrainWorld>()) return false;
        var terrain = world.GetSingletonManaged<TerrainWorld>();
        if (terrain == null) return false;
        int i = terrain.DoorIndexOf(world.GetComponent<TerrainObjectKey>(e).Key);
        if (i < 0 || i >= terrain.DoorLeaves.Count) return false;
        leaf = terrain.DoorLeaves[i];
        state = world.GetComponentRO<DoorState>(e).State;
        return leaf.Footprint.Length >= 4;
    }

    /// <summary>True for a door entity: a terrain object with a door state (its body is the leaf, never a shape kit).</summary>
    public static bool IsDoor(EntityRepository world, Entity e)
        => world.IsComponentTypeRegistered<DoorState>() && world.HasComponent<DoorState>(e)
        && world.HasManagedComponent<TerrainObjectKey>(e);

    /// <summary>
    /// The leaf's oriented box at swing <paramref name="angle"/> (radians, 0 = shut in the wall): it turns about the hinge's vertical
    /// axis, toward the footprint's +n side. Centre, rotation (about Z) and half-extents (along the leaf, across it, up).
    /// </summary>
    public static (Vector3 Centre, Quaternion Rotation, Vector3 Half) Box(TerrainPrism leaf, float angle)
    {
        var fp = leaf.Footprint;
        var hinge = (fp[0] + fp[3]) * 0.5f;
        var along = (fp[1] + fp[2]) * 0.5f - hinge;
        float width = along.Length();
        float thickness = (fp[3] - fp[0]).Length();
        float yaw = MathF.Atan2(along.Y, along.X) + angle;
        var dir = new Vector2(MathF.Cos(yaw), MathF.Sin(yaw));
        var centre = hinge + dir * (width * 0.5f);
        return (new Vector3(centre, (leaf.BaseZ + leaf.TopZ) * 0.5f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw),
                new Vector3(width * 0.5f, thickness * 0.5f, (leaf.TopZ - leaf.BaseZ) * 0.5f));
    }
}

/// <summary>
/// ⭐ CE-1068 — the picture's own timing of a door's swing, on SIMULATION time (a pause freezes it, every host shows the same): the
/// leaf turns toward its state's angle at a constant speed (<see cref="DoorLeaf3D.SwingSeconds"/> for a full quarter turn). The state
/// is the door's replicated <see cref="DoorState"/>; nothing here is sent — like <c>PostureBlend</c>, a door first seen (a load, a
/// late host, a replay jump) simply stands in its state, and a clock that goes back snaps.
/// </summary>
public sealed class DoorSwing
{
    private struct State { public float Angle; public double At; }

    private readonly Dictionary<Entity, State> _states = new();
    private readonly List<Entity> _gone = new();

    /// <summary>The angle <paramref name="e"/>'s leaf is drawn at this frame, moving toward <paramref name="target"/>.</summary>
    public float Angle(Entity e, float target, double simTime)
    {
        if (!_states.TryGetValue(e, out var st) || simTime < st.At || simTime - st.At > 5.0)
            st = new State { Angle = target, At = simTime };   // first seen, a clock jump or a long gap: no swing
        else
        {
            float step = (float)(simTime - st.At) * DoorLeaf3D.OpenAngle / DoorLeaf3D.SwingSeconds;
            float d = target - st.Angle;
            st.Angle = MathF.Abs(d) <= step ? target : st.Angle + MathF.Sign(d) * step;
            st.At = simTime;
        }
        _states[e] = st;
        return st.Angle;
    }

    /// <summary>The angle last drawn for <paramref name="e"/>, or <paramref name="fallback"/> when it has not been drawn.</summary>
    public float Current(Entity e, float fallback) => _states.TryGetValue(e, out var st) ? st.Angle : fallback;

    /// <summary>Forgets everyone not drawn this frame.</summary>
    public void EndFrame(HashSet<Entity> drawn)
    {
        _gone.Clear();
        foreach (var e in _states.Keys) if (!drawn.Contains(e)) _gone.Add(e);
        foreach (var e in _gone) _states.Remove(e);
    }
}
