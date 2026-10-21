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
/// one colour per kind, built once per net; then each frame the traffic lights in their live state, from <see cref="TrafficFrame"/>.
/// ⭐ CE-4031 (AQ93 §15.8) — SUMO's cars and pedestrians are drawn by <see cref="EntityBodyLayer3D"/>, entity or not (the far ones through
/// <see cref="TrafficAmbientBodies"/>): our figures and car kits, never boxes. Nothing in 2-D (the 2-D map draws <c>TrafficRoadGizmo</c>).
/// </summary>
/// <remarks>
/// ⭐ A flat net and our ground are both at Z 0, so each kind is LIFTED a few centimetres (paint over asphalt over junction over our
/// ground; the ambient ground just under it) — a render offset, never a height the simulation reads.
/// ⭐ <c>CE-1196</c> (docs/DESIGN_Map_3D_Mode.md §6z CA-4) — and the ROADS are drawn <see cref="MapCamera3D.GroundBiasSteps"/> depth steps
/// toward the eye (<see cref="LitShader.SetDepthBias"/>): from afar a few centimetres are less than one depth step, so they fought our
/// ground. The ambient ground is its own mesh, unbiased — it lies UNDER everything.
/// </remarks>
public sealed class TrafficLayer3D : IMapLayer, IDisposable
{
    public static readonly Color AsphaltColor   = new(64, 66, 70, 255);
    public static readonly Color JunctionColor  = new(74, 76, 80, 255);
    public static readonly Color MarkingColor   = new(235, 235, 225, 255);
    public static readonly Color CrosswalkColor = new(240, 240, 240, 255);
    public static readonly Color GroundColor    = new(150, 160, 128, 255);
    public static readonly Color PoleColor      = new(60, 60, 60, 255);

    private readonly Func<EntityRepository?> _world;
    private object? _identity;
    private bool _ambient;
    private Mesh _mesh, _ground, _cube;
    private bool _hasMesh, _hasGround, _hasCube;

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
        if (_hasMesh || _hasGround)
        {
            var lit = LitShader.Shared;
            Rlgl.DisableBackfaceCulling();
            if (_hasGround) Raylib.DrawMesh(_ground, lit.Tinted(Color.White), Matrix4x4.Identity);
            if (_hasMesh)
            {
                lit.SetDepthBias(MapCamera3D.GroundBiasNdc);   // ⭐ CE-1196 — the roads over the ground they lie on, at every distance
                Raylib.DrawMesh(_mesh, lit.Tinted(Color.White), Matrix4x4.Identity);
                lit.SetDepthBias(0f);
            }
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
    }

    private void Rebuild(TrafficNetCache net)
    {
        ReleaseMesh();
        _identity = net;
        _ambient = ShowAmbientGround;
        BuildVertexData(net.Roads, k => k != SceneKind.AmbientGround, out var positions, out var normals, out var colors);
        _hasMesh = Upload(positions, normals, colors, out _mesh);
        TriangleCount = positions.Length / 3;
        if (!ShowAmbientGround) return;
        BuildVertexData(net.Roads, k => k == SceneKind.AmbientGround, out positions, out normals, out colors);
        _hasGround = Upload(positions, normals, colors, out _ground);
        TriangleCount += positions.Length / 3;
    }

    private static bool Upload(Vector3[] positions, Vector3[] normals, Color[] colors, out Mesh uploaded)
    {
        uploaded = default;
        int tris = positions.Length / 3;
        if (tris == 0) return false;
        var mesh = new Mesh(positions.Length, tris);
        mesh.AllocVertices();
        mesh.AllocNormals();
        mesh.AllocColors();
        positions.AsSpan().CopyTo(mesh.VerticesAs<Vector3>());
        normals.AsSpan().CopyTo(mesh.NormalsAs<Vector3>());
        colors.AsSpan().CopyTo(mesh.ColorsAs<Color>());
        Raylib.UploadMesh(ref mesh, false);
        uploaded = mesh;
        return true;
    }

    /// <summary>The un-indexed vertex streams in Raylib coordinates, each kind lifted and coloured. Pure — a rail tests it.</summary>
    public static void BuildVertexData(TrafficRoadScene roads, bool ambientGround,
        out Vector3[] positions, out Vector3[] normals, out Color[] colors)
        => BuildVertexData(roads, k => ambientGround || k != SceneKind.AmbientGround, out positions, out normals, out colors);

    /// <summary>⭐ <c>CE-1196</c> — the streams of the kinds <paramref name="include"/> keeps (the roads, biased; the ambient ground, not).</summary>
    public static void BuildVertexData(TrafficRoadScene roads, Func<SceneKind, bool> include,
        out Vector3[] positions, out Vector3[] normals, out Color[] colors)
    {
        int tris = 0;
        foreach (var m in roads.Meshes)
            if (include(m.Kind)) tris += m.TriangleCount;
        positions = new Vector3[tris * 3];
        normals = new Vector3[tris * 3];
        colors = new Color[tris * 3];
        int o = 0;
        var up = HrotToRaylib.Position(Vector3.UnitZ);
        foreach (var m in roads.Meshes)
        {
            if (!include(m.Kind)) continue;
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
        if (_hasGround) Raylib.UnloadMesh(_ground);
        _hasMesh = _hasGround = false;
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
