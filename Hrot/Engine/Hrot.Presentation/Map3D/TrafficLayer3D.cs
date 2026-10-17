using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Hrot.Traffic;
using Raylib_cs;
using Sim.Scene;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ AQ93 R-4024 / V7 / V8 — the SUMO net in the map's 3-D mode, <see cref="TerrainLayer3D"/>'s sibling: the road scene
/// (<see cref="TrafficNetCache.Roads"/> — asphalt, junction fills, lane markings, crosswalks, the ambient ground beyond our terrain),
/// one colour per kind, built once per net; then each frame the traffic lights in their live state and the cars that are NOT
/// entities (an entity car is drawn by <see cref="EntityBodyLayer3D"/>), from <see cref="TrafficFrame"/>. Nothing in 2-D (the
/// 2-D map draws <c>TrafficRoadGizmo</c>).
/// </summary>
/// <remarks>
/// ⭐ A flat net and our ground are both at Z 0, so each kind is LIFTED a few centimetres (paint over asphalt over junction over our
/// ground; the ambient ground just under it) — a render offset, never a height the simulation reads.
/// </remarks>
public sealed class TrafficLayer3D : IMapLayer, IDisposable
{
    public static readonly Color AsphaltColor   = new(64, 66, 70, 255);
    public static readonly Color JunctionColor  = new(74, 76, 80, 255);
    public static readonly Color MarkingColor   = new(235, 235, 225, 255);
    public static readonly Color CrosswalkColor = new(240, 240, 240, 255);
    public static readonly Color GroundColor    = new(150, 160, 128, 255);
    public static readonly Color CarColor       = new(70, 120, 190, 255);
    public static readonly Color PoleColor      = new(60, 60, 60, 255);

    private readonly Func<EntityRepository?> _world;
    private object? _identity;
    private bool _ambient;
    private Mesh _mesh, _cube;
    private bool _hasMesh, _hasCube;

    public TrafficLayer3D(Func<EntityRepository?> world) => _world = world ?? throw new ArgumentNullException(nameof(world));

    public string Name => "SUMO traffic (3-D)";
    public int LayerBitIndex => -1;
    public bool Has3D => true;

    /// <summary>Draw the ambient ground (the background floor beyond our terrain, V8). On by default.</summary>
    public bool ShowAmbientGround { get; set; } = true;

    /// <summary>Triangles in the uploaded road mesh (0 before the first 3-D frame with a net).</summary>
    public int TriangleCount { get; private set; }

    public void Update(float dt) { }
    public void Draw(RenderContext ctx) { }
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;

    public void Draw3D(RenderContext ctx)
    {
        if (_world() is not { } world || TrafficNetCache.Of(world) is not { } net) { Release(); _identity = null; return; }
        if (!ReferenceEquals(net, _identity) || _ambient != ShowAmbientGround) Rebuild(net);
        if (_hasMesh)
        {
            Rlgl.DisableBackfaceCulling();
            Raylib.DrawMesh(_mesh, LitShader.Shared.Tinted(Color.White), Matrix4x4.Identity);
            Rlgl.EnableBackfaceCulling();
        }

        if (TrafficFrame.Of(world) is not { } frame) return;
        if (!_hasCube) { _cube = Raylib.GenMeshCube(1f, 1f, 1f); _hasCube = true; }
        var shader = LitShader.Shared;
        foreach (var s in net.Roads.Signals)
        {
            if (!frame.Lights.TryGetValue(s.LaneHandle, out var signal)) continue;
            var lamp = TrafficFrame.LampOf(signal) switch
            {
                2 => new Color(40, 210, 70, 255), 1 => new Color(245, 205, 40, 255), 0 => new Color(225, 40, 40, 255), _ => PoleColor,
            };
            Raylib.DrawMesh(_cube, shader.Tinted(lamp), HrotToRaylib.Model(s.Head, Quaternion.Identity, new Vector3(0.35f, 0.35f, 0.9f)));
        }
        foreach (var car in frame.Cars)
        {
            if (frame.IsEntity(car.Handle)) continue;
            var size = new Vector3(car.Length, car.Width, 1.5f);
            var centre = new Vector3(car.X, car.Y, car.Z + size.Z / 2f);
            Raylib.DrawMesh(_cube, shader.Tinted(CarColor),
                HrotToRaylib.Model(centre, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, car.YawRad), size));
        }
    }

    private void Rebuild(TrafficNetCache net)
    {
        ReleaseMesh();
        _identity = net;
        _ambient = ShowAmbientGround;
        BuildVertexData(net.Roads, ShowAmbientGround, out var positions, out var normals, out var colors);
        TriangleCount = positions.Length / 3;
        if (TriangleCount == 0) return;
        var mesh = new Mesh(positions.Length, TriangleCount);
        mesh.AllocVertices();
        mesh.AllocNormals();
        mesh.AllocColors();
        positions.AsSpan().CopyTo(mesh.VerticesAs<Vector3>());
        normals.AsSpan().CopyTo(mesh.NormalsAs<Vector3>());
        colors.AsSpan().CopyTo(mesh.ColorsAs<Color>());
        Raylib.UploadMesh(ref mesh, false);
        _mesh = mesh;
        _hasMesh = true;
    }

    /// <summary>The un-indexed vertex streams in Raylib coordinates, each kind lifted and coloured. Pure — a rail tests it.</summary>
    public static void BuildVertexData(TrafficRoadScene roads, bool ambientGround,
        out Vector3[] positions, out Vector3[] normals, out Color[] colors)
    {
        int tris = 0;
        foreach (var m in roads.Meshes)
            if (ambientGround || m.Kind != SceneKind.AmbientGround) tris += m.TriangleCount;
        positions = new Vector3[tris * 3];
        normals = new Vector3[tris * 3];
        colors = new Color[tris * 3];
        int o = 0;
        var up = HrotToRaylib.Position(Vector3.UnitZ);
        foreach (var m in roads.Meshes)
        {
            if (!ambientGround && m.Kind == SceneKind.AmbientGround) continue;
            var (colour, lift) = Style(m.Kind);
            var lifted = new Vector3(0f, 0f, lift);
            for (int i = 0; i < m.Indices.Length; i++, o++)
            {
                positions[o] = HrotToRaylib.Position(m.Vertices[m.Indices[i]] + lifted);
                normals[o] = up;
                colors[o] = colour;
            }
        }
    }

    /// <summary>A kind's colour and its render lift (metres).</summary>
    public static (Color Colour, float Lift) Style(SceneKind kind) => kind switch
    {
        SceneKind.AmbientGround => (GroundColor, -0.03f),
        SceneKind.Junction      => (JunctionColor, 0.03f),
        SceneKind.Asphalt       => (AsphaltColor, 0.04f),
        SceneKind.LaneMarking   => (MarkingColor, 0.06f),
        SceneKind.Crosswalk     => (CrosswalkColor, 0.06f),
        _                       => (AsphaltColor, 0.04f),
    };

    private void ReleaseMesh()
    {
        if (_hasMesh) Raylib.UnloadMesh(_mesh);
        _hasMesh = false;
        TriangleCount = 0;
    }

    private void Release()
    {
        ReleaseMesh();
        if (_hasCube) Raylib.UnloadMesh(_cube);
        _hasCube = false;
    }

    public void Dispose() => Release();
}
