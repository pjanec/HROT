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
    /// when the geometry's <see cref="ITerrainRenderGeometry.Identity"/> changes. Nothing in 2-D.
    /// 🔒 R-250/R-252: the map sees triangles and a kind, never how the terrain is modelled.
    /// </summary>
    public sealed class TerrainLayer3D : IMapLayer, IDisposable
    {
        public static readonly Color GroundColor = new(112, 142, 86, 255);
        public static readonly Color WallColor = new(206, 200, 188, 255);
        public static readonly Color RoofColor = new(150, 84, 66, 255);

        private readonly Func<ITerrainRenderGeometry?> _source;
        private object? _identity;
        private Mesh _mesh;
        private bool _hasMesh;

        public TerrainLayer3D(Func<ITerrainRenderGeometry?> source)
            => _source = source ?? throw new ArgumentNullException(nameof(source));

        public string Name => "Terrain (3-D)";
        public int LayerBitIndex => -1;
        public bool Has3D => true;

        /// <summary>Triangles in the uploaded mesh (0 before the first 3-D frame with a world).</summary>
        public int TriangleCount { get; private set; }

        public void Update(float dt) { }
        public void Draw(RenderContext ctx) { }
        public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
        public Entity? PickEntity(Vector2 worldPos) => null;

        public void Draw3D(RenderContext ctx)
        {
            var geometry = _source();
            if (geometry == null) return;
            if (!ReferenceEquals(geometry.Identity, _identity)) Rebuild(geometry);
            if (!_hasMesh) return;
            Rlgl.DisableBackfaceCulling();
            Raylib.DrawMesh(_mesh, LitShader.Shared.Tinted(Color.White), Matrix4x4.Identity);
            Rlgl.EnableBackfaceCulling();
        }

        private void Rebuild(ITerrainRenderGeometry geometry)
        {
            Release();
            _identity = geometry.Identity;
            geometry.Build(out var verts, out var indices, out var kinds);
            BuildVertexData(verts, indices, kinds, out var positions, out var normals, out var colors);
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

        /// <summary>
        /// The un-indexed, flat-shaded vertex streams in Raylib coordinates: three vertices per triangle, each carrying the
        /// triangle's normal and its kind's colour. Pure — the rail tests it without a window.
        /// </summary>
        public static void BuildVertexData(Vector3[] verts, int[] indices, TerrainSurfaceKind[] kinds,
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
                var colour = ColourOf(t < kinds.Length ? kinds[t] : TerrainSurfaceKind.Ground);
                positions[3 * t] = a; positions[3 * t + 1] = b; positions[3 * t + 2] = c;
                normals[3 * t] = normals[3 * t + 1] = normals[3 * t + 2] = n;
                colors[3 * t] = colors[3 * t + 1] = colors[3 * t + 2] = colour;
            }
        }

        public static Color ColourOf(TerrainSurfaceKind kind) => kind switch
        {
            TerrainSurfaceKind.Wall => WallColor,
            TerrainSurfaceKind.Roof => RoofColor,
            _ => GroundColor,
        };

        private void Release()
        {
            if (_hasMesh) Raylib.UnloadMesh(_mesh);
            _hasMesh = false;
            TriangleCount = 0;
        }

        public void Dispose() => Release();
    }
}
