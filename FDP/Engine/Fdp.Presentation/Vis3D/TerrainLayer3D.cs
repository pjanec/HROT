using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.World;
using Raylib_cs;

namespace Fdp.Toolkit.Vis3D
{
    /// <summary>
    /// ⭐ CE-1033 S1 — the terrain in the map's 3-D mode (<c>docs/DESIGN_Map_3D_Mode.md</c> M4, M5): the triangles the world query
    /// hands out through <see cref="ITerrainRenderGeometry"/> (the stand-in: its navmesh soup, unchanged), one colour per kind, lit
    /// and fogged by <see cref="LitShader"/>. Flat shading by un-indexing (each triangle its own normal). The mesh is rebuilt only
    /// when the geometry's <see cref="ITerrainRenderGeometry.Identity"/> changes — ⭐ CE-1446: built on a worker (<see cref="BackgroundMeshBuild{TStreams}"/>), uploaded on the frame. Nothing in 2-D.
    /// 🔒 R-250/R-252: the map sees triangles and a kind, never how the terrain is modelled.
    /// </summary>
    public sealed class TerrainLayer3D : ISceneLayer, IMeshLayer3D, IDisposable   // ⭐ CE-3289 — the world: filmed
    {
        public static readonly Color GroundColor = new(112, 142, 86, 255);
        public static readonly Color WallColor = new(206, 200, 188, 255);
        /// <summary>⭐ <c>CE-1341</c> — slate grey, not terracotta: a building's floors arrive as the same upward triangles as its roof, and a red
        /// floor read as the brick walls. 🔒 User, 2026-10-25: "change the roof texture to something non-red what can work as the floor as well".</summary>
        public static readonly Color RoofColor = new(112, 114, 118, 255);

        private readonly Func<ITerrainRenderGeometry?> _source;
        private Mesh _mesh;
        private bool _hasMesh;
        /// <summary>⭐ CE-1446 — the CPU streams are built on a worker, keyed by the geometry's identity; the frame thread uploads them (<see cref="PumpBuild"/>).</summary>
        private readonly BackgroundMeshBuild<Streams> _build = new();

        /// <summary>The terrain's un-indexed vertex streams.</summary>
        private sealed record Streams(Vector3[] Positions, Vector3[] Normals, Color[] Colors);

        public TerrainLayer3D(Func<ITerrainRenderGeometry?> source)
            => _source = source ?? throw new ArgumentNullException(nameof(source));

        public string Name => "Terrain (3-D)";
        public int LayerBitIndex => -1;
        public bool Has3D => true;

        /// <summary>Triangles in the uploaded mesh (0 before the first 3-D frame with a world).</summary>
        public int TriangleCount { get; private set; }

        // ⭐ CE-1446 — the upload is pumped by the map's Map3DBuild (every frame, 2-D too), not here
        public void Update(float dt) { }
        public void Draw(RenderContext ctx) { }

        string IMeshLayer3D.BuildName => "terrain";
        bool IMeshLayer3D.HasSource => _source() != null;
        bool IMeshLayer3D.IsBuilt => _source() is { } g && _build.IsBuiltFor(g.Identity);
        string? IMeshLayer3D.BuildFailure => _build.Failure;

        /// <summary>The build state of the mesh (for the rails).</summary>
        public MeshBuildState BuildState => _build.State;

        void IMeshLayer3D.RequestBuild()
        {
            if (_source() is not { } geometry)
            {
                if (_hasMesh || _build.State != MeshBuildState.Idle) { Release(); _build.Reset(); }
                return;
            }
            RequestBuild(geometry);
        }

        void IMeshLayer3D.PumpBuild() => _build.Pump(Upload);
        public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
        public Entity? PickEntity(Vector2 worldPos) => null;

        public void Draw3D(RenderContext ctx)
        {
            var geometry = _source();
            if (geometry == null) return;
            // a frame drawn in 3-D wants its mesh: ask (a no-op when asked), and upload what a worker has finished — the build itself never runs here
            RequestBuild(geometry);
            _build.Pump(Upload);
            if (!_hasMesh) return;
            Rlgl.DisableBackfaceCulling();
            var lit = LitShader.Shared;
            lit.SetGroundDetail(1f);   // ⭐ CE-1328 / CE-1333 — the materials' patterns (grass, brick, concrete, sandbags, roof tiles …)
            Raylib.DrawMesh(_mesh, lit.Tinted(Color.White), Matrix4x4.Identity);
            lit.SetGroundDetail(0f);
            Rlgl.EnableBackfaceCulling();
        }

        /// <summary>
        /// ⭐ CE-1446 — starts the CPU build for <paramref name="geometry"/> on a worker. 📐 Safe off the frame thread: <see cref="ITerrainRenderGeometry.BuildTagged"/> of the terrain reads only its
        /// <c>TerrainWorld</c> (init-only, never mutated after the terrain is committed — <c>TerrainWorldMesh.Build</c> is static and pure).
        /// </summary>
        private void RequestBuild(ITerrainRenderGeometry geometry)
            => _build.Request(geometry.Identity, () =>
            {
                // ⭐ CE-1033 S4 — the tagged build: each triangle's material, and the water surfaces.
                geometry.BuildTagged(out var verts, out var indices, out var kinds, out var materials);
                BuildVertexData(verts, indices, kinds, materials, out var positions, out var normals, out var colors);
                return new Streams(positions, normals, colors);
            });

        /// <summary>FRAME THREAD: the finished streams onto the GPU, replacing the previous mesh.</summary>
        private void Upload(Streams streams)
        {
            Release();
            var positions = streams.Positions;
            TriangleCount = positions.Length / 3;
            if (TriangleCount == 0) return;

            var mesh = new Mesh(positions.Length, TriangleCount);
            mesh.AllocVertices();
            mesh.AllocNormals();
            mesh.AllocColors();
            positions.AsSpan().CopyTo(mesh.VerticesAs<Vector3>());
            streams.Normals.AsSpan().CopyTo(mesh.NormalsAs<Vector3>());
            streams.Colors.AsSpan().CopyTo(mesh.ColorsAs<Color>());
            Raylib.UploadMesh(ref mesh, false);
            _mesh = mesh;
            _hasMesh = true;
        }

        /// <summary>
        /// The un-indexed, flat-shaded vertex streams in Raylib coordinates: three vertices per triangle, each carrying the
        /// triangle's normal and its kind's colour. Pure — the rail tests it without a window.
        /// </summary>
        public static void BuildVertexData(Vector3[] verts, int[] indices, TerrainSurfaceKind[] kinds,
            out Vector3[] positions, out Vector3[] normals, out Color[] colors)
            => BuildVertexData(verts, indices, kinds, null, out positions, out normals, out colors);

        /// <summary>⭐ CE-1033 S4 — as above, coloured by kind AND material (<see cref="ColourOf(TerrainSurfaceKind, string?)"/>).</summary>
        public static void BuildVertexData(Vector3[] verts, int[] indices, TerrainSurfaceKind[] kinds, string?[]? materials,
            out Vector3[] positions, out Vector3[] normals, out Color[] colors)
        {
            int tris = indices.Length / 3;
            positions = new Vector3[tris * 3];
            normals = new Vector3[tris * 3];
            colors = new Color[tris * 3];
            for (int t = 0; t < tris; t++)
            {
                var a = HrotToRaylib.Position(verts[indices[3 * t]]);
                var b = HrotToRaylib.Position(verts[indices[3 * t + 1]]);
                var c = HrotToRaylib.Position(verts[indices[3 * t + 2]]);
                var n = Vector3.Cross(b - a, c - a);
                float len = n.Length();
                n = len > 1e-12f ? n / len : Vector3.UnitY;
                var kind = t < kinds.Length ? kinds[t] : TerrainSurfaceKind.Ground;
                string? material = materials != null && t < materials.Length ? materials[t] : null;
                var colour = ColourOf(kind, material);
                colour.A = MaterialCode(kind, material);   // ⭐ CE-1333 — the shader's pattern, carried in the alpha (the terrain draws opaque)
                positions[3 * t] = a; positions[3 * t + 1] = b; positions[3 * t + 2] = c;
                normals[3 * t] = normals[3 * t + 1] = normals[3 * t + 2] = n;
                colors[3 * t] = colors[3 * t + 1] = colors[3 * t + 2] = colour;
            }
        }

        public static Color ColourOf(TerrainSurfaceKind kind) => kind switch
        {
            TerrainSurfaceKind.Wall => WallColor,
            TerrainSurfaceKind.Roof => RoofColor,
            TerrainSurfaceKind.Water => WaterColor,
            _ => GroundColor,
        };

        public static readonly Color WaterColor = new(66, 112, 158, 255);
        public static readonly Color ForestColor = new(66, 100, 54, 255);

        /// <summary>
        /// ⭐ CE-1033 S4 — a triangle's colour from its kind and its material (the shared material library's names). A building's
        /// concrete or brick roof keeps the roof colour; a fence, a hedge, sandbags, a berm, glass or a car body shows its own.
        /// </summary>
        public static Color ColourOf(TerrainSurfaceKind kind, string? material)
        {
            if (kind == TerrainSurfaceKind.Water) return WaterColor;
            if (kind == TerrainSurfaceKind.Ground) return material == "forest" ? ForestColor : GroundColor;
            Color? own = material switch
            {
                "brick" => new Color(172, 96, 72, 255),
                "glass" => new Color(150, 184, 206, 255),
                "steel-plate" or "fence-metal-sheet" => new Color(124, 130, 138, 255),
                "fence-wood" or "door-wood" => new Color(140, 104, 68, 255),
                "fence-chainlink" => new Color(156, 158, 160, 255),
                "hedge" => new Color(56, 94, 46, 255),
                "sandbags" => new Color(182, 162, 112, 255),
                "earth-berm" => new Color(128, 108, 78, 255),
                "car-body" => new Color(92, 98, 112, 255),
                _ => null,   // concrete and unknown: the kind's colour
            };
            if (kind == TerrainSurfaceKind.Roof)
                return material is null or "concrete" or "brick" ? RoofColor : Shade(own ?? RoofColor, 0.85f);
            return own ?? WallColor;
        }

        /// <summary>⭐ <c>CE-1333</c> — which pattern the lit shader paints on a terrain triangle (<see cref="LitShader.SetGroundDetail"/>), carried
        /// in its vertex colour's alpha: 250 open ground (grass) · 249 forest floor · 248 brick · 247 concrete · 246 sandbags · 245 roof tiles ·
        /// 244 wood · 243 earth · 242 hedge · 241 a brick building's roof (tiles on top, brick on its sides) · 255 none (water, glass, metal,
        /// a car body).</summary>
        public static byte MaterialCode(TerrainSurfaceKind kind, string? material) => kind switch
        {
            TerrainSurfaceKind.Water => 255,
            TerrainSurfaceKind.Ground => material == "forest" ? (byte)249 : (byte)250,
            TerrainSurfaceKind.Roof => material == "brick" ? (byte)241 : material is null or "concrete" ? (byte)245 : CodeOf(material),
            _ => CodeOf(material),
        };

        private static byte CodeOf(string? material) => material switch
        {
            null or "concrete" => 247,
            "brick" => 248,
            "sandbags" => 246,
            "fence-wood" or "door-wood" => 244,
            "earth-berm" => 243,
            "hedge" => 242,
            _ => 255,
        };

        private static Color Shade(Color c, float k) => new((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k), c.A);

        private void Release()
        {
            if (_hasMesh) Raylib.UnloadMesh(_mesh);
            _hasMesh = false;
            TriangleCount = 0;
        }

        public void Dispose() => Release();
    }
}
