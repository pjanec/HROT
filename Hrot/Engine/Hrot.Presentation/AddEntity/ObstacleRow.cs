using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common;

namespace Hrot.UI.Common.AddEntity;

/// <summary>How the obstacles of a row stand to its line.</summary>
public enum RowFacing { Along, Across }

/// <summary>
/// ⭐⭐ CE-5008 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-5, R-4264, R-5041; mockup D) — the options of the OBSTACLE ROW tool and its commit: a LINE drawn on the map becomes N obstacle ENTITIES (never one polyline entity), each of the chosen
/// obstacle TKB type, at the shared <see cref="FeatureExpander.LinePlacesSpaced"/> places (every <c>type length + gap</c> m along the line, or <c>type width + gap</c> across it), every one carrying the SAME <see cref="PlacementGroup"/> id,
/// all created inside ONE <see cref="EditSteps"/> bracket (one undo step). Pure but for <see cref="Place"/>.
/// </summary>
public sealed class ObstacleRowOptions
{
    /// <summary>The placement spec that arms the row tool (rides in the area tool's style JSON like a terrain spec).</summary>
    public const string Spec = "obstacles:row";

    /// <summary>The label of the undo step.</summary>
    public const string StepLabel = "Row of obstacles";

    /// <summary>The obstacle TKB types the row can be made of, with the ONE <see cref="ClutterTypes"/> box each stands for and its shared icon key.</summary>
    public static readonly (long Type, string Name, ClutterTypes.Type Box, string IconKey)[] Types =
    {
        (TkbEntityTypes.Obstacle_Car, "Car", ClutterTypes.Car, "clutter/car"),
        (TkbEntityTypes.Obstacle_SandbagWall, "Sandbag wall", ClutterTypes.SandbagWall, "clutter/sandbags"),
        (TkbEntityTypes.Obstacle_ConcreteBlock, "Concrete block", ClutterTypes.ConcreteBlock, "clutter/block"),
        (TkbEntityTypes.Obstacle_Crate, "Crate", ClutterTypes.Crate, "clutter/crate"),
    };

    /// <summary>The options of <paramref name="spec"/>, or null when it is not the row tool's spec.</summary>
    public static ObstacleRowOptions? From(string? spec) => spec == Spec ? new ObstacleRowOptions() : null;

    /// <summary>The obstacle TKB type (default: the concrete block).</summary>
    public long TkbType { get; private set; } = TkbEntityTypes.Obstacle_ConcreteBlock;

    /// <summary>The clear distance between two obstacles, metres (default 1.5; 0 = end to end).</summary>
    public float Gap { get; set; } = 1.5f;

    public RowFacing Facing { get; set; } = RowFacing.Along;

    public void SetType(long tkbType) { foreach (var t in Types) if (t.Type == tkbType) { TkbType = tkbType; return; } }

    public (long Type, string Name, ClutterTypes.Type Box, string IconKey) TypeInfo { get { foreach (var t in Types) if (t.Type == TkbType) return t; return Types[2]; } }

    /// <summary>The distance between two obstacle centres: the box's length (facing along the line) or width (across it) plus the gap.</summary>
    public float Spacing => Math.Max(0.1f, (Facing == RowFacing.Along ? TypeInfo.Box.Length : TypeInfo.Box.Width) + Math.Max(0f, Gap));

    /// <summary>The places (world position, yaw in degrees — 0 = the box's length along +X) along <paramref name="points"/>. ⭐ The SAME routine the tree Row uses.</summary>
    public IReadOnlyList<(Vector2 P, float YawDeg)> Places(IReadOnlyList<Vector2> points)
    {
        var l = new List<(Vector2, float)>();
        if (points.Count < 2) return l;
        foreach (var (p, deg) in FeatureExpander.LinePlacesSpaced(points, Spacing, FeatureExpander.MaxScatterItems))
            l.Add((p, deg + (Facing == RowFacing.Across ? 90f : 0f)));
        return l;
    }

    /// <summary>How many obstacles the row makes along <paramref name="points"/> (the panel's "4 concrete blocks").</summary>
    public int CountOf(IReadOnlyList<Vector2> points) => points.Count < 2 ? 0 : FeatureExpander.CountAlong(points, Spacing);

    /// <summary>The panel's line: <c>4 concrete blocks · 1.5 m gaps · Enter ends</c>.</summary>
    public string Summary(IReadOnlyList<Vector2> points)
        => $"{CountOf(points)} {TypeInfo.Name.ToLowerInvariant()}{(CountOf(points) == 1 ? "" : "s")} · {Gap.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} m gaps · Enter ends";

    /// <summary>One spawn command per place: the type, the position (at <paramref name="z"/>), the yaw, and the <see cref="PlacementGroup"/> every member shares. ⭐ The rest (the name, the numbering) is the host's, as for any placed unit.</summary>
    public List<SpawnEntityCommand> Commands(IReadOnlyList<Vector2> points, float z, uint groupId)
    {
        var l = new List<SpawnEntityCommand>();
        foreach (var (p, yaw) in Places(points))
            l.Add(new SpawnEntityCommand
            {
                NetworkId = 0,
                TkbType = (TkbTypeId)TkbType,
                OwnerNodeId = (NodeId)0,
                InitType = ReliableInitType.AllPeers,
                InitialTransform = new SimTransform { Position = new Vector3(p.X, p.Y, z), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw * (MathF.PI / 180f)) },
                InitialComponents = new List<object> { new PlacementGroup { Id = groupId } },
            });
        return l;
    }

    /// <summary>
    /// ⭐ Sends every command of a row through <paramref name="request"/> inside ONE edit step (<see cref="EditSteps.Begin"/> … <see cref="EditSteps.End"/>, the Unpack precedent): one undo removes the whole row.
    /// The step is cancelled when a send throws (nothing half-recorded).
    /// </summary>
    public static void Place(EntityRepository? world, IReadOnlyList<SpawnEntityCommand> commands, Action<SpawnEntityCommand> request)
    {
        EditSteps.Begin(world, StepLabel);
        try { foreach (var c in commands) request(c); }
        catch { EditSteps.Cancel(world); throw; }
        EditSteps.End(world);
    }
}
