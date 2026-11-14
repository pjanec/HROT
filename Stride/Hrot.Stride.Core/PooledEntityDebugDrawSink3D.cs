#nullable enable
using System;
using System.Collections.Generic;
using SBuffer = Stride.Graphics.Buffer;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;

namespace Hrot.Stride.Core;

/// <summary>
/// Concrete GPU <see cref="IDebugDrawSink3D"/> for the Stride window (STR-D16 resolution).
///
/// <para>
/// <b>Approach — pooled-entity primitive sink.</b>
/// Stride 4.2.1.2487 ships NO immediate-mode debug-shape API
/// (<c>ImmediateDebugRenderSystem</c>/<c>DebugShapes</c> do not exist; only
/// <c>DebugTextSystem</c> for text and <c>DebugRenderer</c> as a compositor render-feature
/// requiring custom SDSL shaders). The cleanest zero-custom-shader approach maintains a pool
/// of Stride <see cref="Entity"/> objects with <see cref="ModelComponent"/>s backed by
/// procedural meshes and per-color emissive <see cref="Material"/>s.
/// </para>
///
/// <para>
/// <b>Primitive → mesh:</b>
/// <list type="bullet">
///   <item><b>Line segment</b> → a thin unit-X box (1×<see cref="LineThicknessMeters"/>×<see
///     cref="LineThicknessMeters"/>) with X aligned along the line direction and X-scale set to
///     the segment length.</item>
///   <item><b>Box</b> → a unit cube (1×1×1) scaled by the shape extents.</item>
///   <item><b>Sphere</b> → a UV-sphere (radius 0.5 m) scaled by radius × 2.</item>
/// </list>
/// All unit meshes are built once (GPU immutable buffers) and shared across every pool entry
/// of the same kind. Per-entity material overrides carry the debug color.
/// </para>
///
/// <para>
/// <b>Per-frame protocol:</b> call <see cref="BeginFrame"/> once before the renderer emits
/// shapes. The renderer then calls <see cref="DrawLine"/> / <see cref="DrawShape"/> as many
/// times as needed. Entries not activated this frame are hidden in <see cref="BeginFrame"/>.
/// No per-frame heap allocation after the pool reaches steady state.
/// </para>
///
/// <para>
/// <b>Coordinate contract:</b> all values received here are already in <b>Stride space</b>
/// (swizzled by <see cref="DebugPrimitiveRenderer3D"/>). Do NOT swizzle again.
/// </para>
///
/// <para>Thread safety: Stride game thread only (§8.3 invariant).</para>
/// </summary>
public sealed class PooledEntityDebugDrawSink3D : IDebugDrawSink3D, IDisposable
{
    // ── Configuration ──────────────────────────────────────────────────────

    /// <summary>Thickness of a rendered debug line in meters.</summary>
    public const float LineThicknessMeters = 0.03f;

    // ── Shared unit meshes (built once, shared across all same-kind pool entries) ──
    private Model? _lineModel;
    private Model? _boxModel;
    private Model? _sphereModel;

    // GPU buffer handles to keep alive as long as the models are in use.
    private readonly List<SBuffer> _gpuBuffers = new();

    // ── Material cache (key = packed ARGB) ────────────────────────────────
    private readonly Dictionary<uint, Material> _materialCache = new();

    // ── Pool sub-lists ─────────────────────────────────────────────────────
    private readonly List<PoolEntry> _linePool   = new();
    private readonly List<PoolEntry> _boxPool    = new();
    private readonly List<PoolEntry> _spherePool = new();

    private int _lineCursor;
    private int _boxCursor;
    private int _sphereCursor;

    // ── Stride scene / game ────────────────────────────────────────────────
    private readonly Scene _scene;
    private readonly Game  _game;
    private bool _disposed;

    // ── Pool entry ─────────────────────────────────────────────────────────
    private sealed class PoolEntry
    {
        public readonly Entity         Entity;
        public readonly ModelComponent ModelComp;
        public PoolEntry(Entity e, ModelComponent mc) { Entity = e; ModelComp = mc; }
    }

    // ── Construction ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates the sink. Call this after <c>BeginRun</c> (GraphicsDevice must be live).
    /// </summary>
    public PooledEntityDebugDrawSink3D(Game game, Scene scene)
    {
        _game  = game  ?? throw new ArgumentNullException(nameof(game));
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    }

    // ── Frame protocol ─────────────────────────────────────────────────────

    /// <summary>
    /// Call once at the start of each frame before the renderer emits shapes.
    /// Hides all pool entries from the previous frame and resets cursors.
    /// </summary>
    public void BeginFrame()
    {
        foreach (var e in _linePool)   e.Entity.EnableAll(false, applyOnChildren: false);
        foreach (var e in _boxPool)    e.Entity.EnableAll(false, applyOnChildren: false);
        foreach (var e in _spherePool) e.Entity.EnableAll(false, applyOnChildren: false);
        _lineCursor = _boxCursor = _sphereCursor = 0;
    }

    // ── IDebugDrawSink3D ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void DrawLine(in DebugDrawLine3D line)
    {
        var delta = line.End - line.Start;
        float len = delta.Length();
        if (len < 1e-6f) return;

        var mid      = (line.Start + line.End) * 0.5f;
        var dir      = delta / len;
        // Rotate the unit-X axis to align with the line direction.
        var rotation = RotationFromTo(Vector3.UnitX, dir);
        // Scale: X = line length, Y = Z = thickness.
        var scale    = new Vector3(len, LineThicknessMeters, LineThicknessMeters);

        var e = GetOrGrow(_linePool, ref _lineCursor, PoolKind.Line);
        Apply(e, mid, rotation, scale, line.StartColor);
    }

    /// <inheritdoc/>
    public void DrawShape(in DebugDrawShape3D shape)
    {
        switch (shape.Kind)
        {
            case DebugDrawShapeKind.Box:
            {
                var e = GetOrGrow(_boxPool, ref _boxCursor, PoolKind.Box);
                Apply(e, shape.Position, shape.Rotation, shape.Scale, shape.Color);
                break;
            }
            case DebugDrawShapeKind.Sphere:
            {
                // Unit sphere has radius 0.5; shape.Scale.X = desired radius → scale = radius * 2.
                float s = shape.Scale.X * 2f;
                var e = GetOrGrow(_spherePool, ref _sphereCursor, PoolKind.Sphere);
                Apply(e, shape.Position, shape.Rotation, new Vector3(s, s, s), shape.Color);
                break;
            }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void Apply(PoolEntry entry, Vector3 pos, Quaternion rot, Vector3 scale, Color color)
    {
        entry.Entity.Transform.Position = pos;
        entry.Entity.Transform.Rotation = rot;
        entry.Entity.Transform.Scale    = scale;
        ApplyColor(entry.ModelComp, color);
        entry.Entity.EnableAll(true, applyOnChildren: false);
    }

    private enum PoolKind { Line, Box, Sphere }

    private PoolEntry GetOrGrow(List<PoolEntry> pool, ref int cursor, PoolKind kind)
    {
        if (cursor < pool.Count)
            return pool[cursor++];

        var model = kind switch
        {
            PoolKind.Line   => GetOrBuildLineModel(),
            PoolKind.Box    => GetOrBuildBoxModel(),
            PoolKind.Sphere => GetOrBuildSphereModel(),
            _               => GetOrBuildBoxModel(),
        };

        var entity    = new Entity($"DebugGizmo_{kind}_{pool.Count}");
        var modelComp = new ModelComponent { Model = model };
        entity.Add(modelComp);
        entity.EnableAll(false, applyOnChildren: false);
        _scene.Entities.Add(entity);

        var entry = new PoolEntry(entity, modelComp);
        pool.Add(entry);
        cursor++;
        return entry;
    }

    // ── Rotation helper ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="Quaternion"/> that rotates unit vector <paramref name="from"/>
    /// onto unit vector <paramref name="to"/>. Anti-parallel case uses a perpendicular axis.
    /// Exposed as public for testing.
    /// </summary>
    public static Quaternion RotationFromTo(Vector3 from, Vector3 to)
    {
        float dot = Vector3.Dot(from, to);
        if (dot > 0.99999f) return Quaternion.Identity;

        if (dot < -0.99999f)
        {
            var perp = MathF.Abs(from.X) < 0.9f
                ? Vector3.Cross(from, Vector3.UnitX)
                : Vector3.Cross(from, Vector3.UnitY);
            perp.Normalize();
            return Quaternion.RotationAxis(perp, MathF.PI);
        }

        var  axis = Vector3.Cross(from, to);
        float w   = MathF.Sqrt((1f + dot) * 2f);
        float inv = 1f / w;
        return new Quaternion(axis.X * inv, axis.Y * inv, axis.Z * inv, w * 0.5f);
    }

    // ── Material cache ──────────────────────────────────────────────────────

    private void ApplyColor(ModelComponent mc, Color color)
    {
        var mat = GetOrCreateMaterial(color);
        if (mc.Materials.Count == 0) mc.Materials.Add(0, mat);
        else mc.Materials[0] = mat;
    }

    private Material GetOrCreateMaterial(Color color)
    {
        uint key = ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        if (_materialCache.TryGetValue(key, out var cached)) return cached;

        // Unlit emissive material: fully visible regardless of scene lighting.
        var descriptor = new MaterialDescriptor
        {
            Attributes =
            {
                Emissive = new MaterialEmissiveMapFeature(new ComputeColor(color))
                {
                    Intensity = new ComputeFloat(1f),
                    UseAlpha  = color.A < 255,
                },
            },
        };
        var mat = Material.New(_game.GraphicsDevice, descriptor);
        _materialCache[key] = mat;
        return mat;
    }

    // ── Procedural mesh builders ────────────────────────────────────────────

    private Model GetOrBuildLineModel()
    {
        if (_lineModel != null) return _lineModel;
        // Unit cube: each axis spans [-0.5, +0.5]. Aligned to X by default.
        _lineModel = BuildUnitCubeModel();
        return _lineModel;
    }

    private Model GetOrBuildBoxModel()
    {
        if (_boxModel != null) return _boxModel;
        _boxModel = BuildUnitCubeModel();
        return _boxModel;
    }

    private Model GetOrBuildSphereModel()
    {
        if (_sphereModel != null) return _sphereModel;
        _sphereModel = BuildUvSphereModel(latBands: 8, lonBands: 12);
        return _sphereModel;
    }

    /// <summary>
    /// Builds a unit cube model (−0.5 to +0.5 on all axes) with 24 flat-shaded
    /// vertices (4 per face) and 36 indices. All GPU buffers are immutable and
    /// registered in <see cref="_gpuBuffers"/> for later cleanup.
    /// </summary>
    private Model BuildUnitCubeModel()
    {
        // 8 corner positions.
        var c = new Vector3[]
        {
            new(-0.5f, -0.5f, -0.5f), // 0 lbb
            new( 0.5f, -0.5f, -0.5f), // 1 rbb
            new( 0.5f,  0.5f, -0.5f), // 2 rtb
            new(-0.5f,  0.5f, -0.5f), // 3 ltb
            new(-0.5f, -0.5f,  0.5f), // 4 lbf
            new( 0.5f, -0.5f,  0.5f), // 5 rbf
            new( 0.5f,  0.5f,  0.5f), // 6 rtf
            new(-0.5f,  0.5f,  0.5f), // 7 ltf
        };

        // 6 faces; each face = 4 vertices + 2 triangles.
        var verts   = new VertexPositionNormalTexture[24];
        var indices = new ushort[36];
        int vi = 0, ii = 0;

        void Face(int a, int b, int d, int e, Vector3 n)
        {
            int baseV = vi;
            verts[vi++] = new VertexPositionNormalTexture(c[a], n, Vector2.Zero);
            verts[vi++] = new VertexPositionNormalTexture(c[b], n, Vector2.Zero);
            verts[vi++] = new VertexPositionNormalTexture(c[d], n, Vector2.Zero);
            verts[vi++] = new VertexPositionNormalTexture(c[e], n, Vector2.Zero);
            indices[ii++] = (ushort)(baseV + 0); indices[ii++] = (ushort)(baseV + 1); indices[ii++] = (ushort)(baseV + 2);
            indices[ii++] = (ushort)(baseV + 0); indices[ii++] = (ushort)(baseV + 2); indices[ii++] = (ushort)(baseV + 3);
        }

        Face(0, 1, 2, 3, -Vector3.UnitZ); // back
        Face(5, 4, 7, 6,  Vector3.UnitZ); // front
        Face(4, 0, 3, 7, -Vector3.UnitX); // left
        Face(1, 5, 6, 2,  Vector3.UnitX); // right
        Face(4, 5, 1, 0, -Vector3.UnitY); // bottom
        Face(3, 2, 6, 7,  Vector3.UnitY); // top

        return AssembleModel(verts, indices,
            new BoundingBox(new Vector3(-0.5f), new Vector3(0.5f)));
    }

    /// <summary>
    /// Builds a UV-sphere model (radius 0.5 m, i.e. diameter 1 m) with
    /// <paramref name="latBands"/> × <paramref name="lonBands"/> quads.
    /// </summary>
    private Model BuildUvSphereModel(int latBands, int lonBands)
    {
        var vertList  = new List<VertexPositionNormalTexture>();
        var indexList = new List<ushort>();

        for (int lat = 0; lat <= latBands; lat++)
        {
            float theta    = lat * MathF.PI / latBands;
            float sinTheta = MathF.Sin(theta);
            float cosTheta = MathF.Cos(theta);

            for (int lon = 0; lon <= lonBands; lon++)
            {
                float phi    = lon * 2f * MathF.PI / lonBands;
                float sinPhi = MathF.Sin(phi);
                float cosPhi = MathF.Cos(phi);
                var n = new Vector3(cosPhi * sinTheta, cosTheta, sinPhi * sinTheta);
                vertList.Add(new VertexPositionNormalTexture(n * 0.5f, n, Vector2.Zero));
            }
        }

        for (int lat = 0; lat < latBands; lat++)
        {
            for (int lon = 0; lon < lonBands; lon++)
            {
                int a = lat * (lonBands + 1) + lon;
                int b = a + lonBands + 1;
                indexList.Add((ushort)a);     indexList.Add((ushort)b);     indexList.Add((ushort)(a + 1));
                indexList.Add((ushort)b);     indexList.Add((ushort)(b + 1)); indexList.Add((ushort)(a + 1));
            }
        }

        return AssembleModel(vertList.ToArray(), indexList.ToArray(),
            new BoundingBox(new Vector3(-0.5f), new Vector3(0.5f)));
    }

    /// <summary>
    /// Uploads vertex and index data to immutable GPU buffers and wraps them in a
    /// single-mesh <see cref="Model"/> with one material slot.
    /// </summary>
    private Model AssembleModel(
        VertexPositionNormalTexture[] verts,
        ushort[] indices,
        BoundingBox bbox)
    {
        var dev = _game.GraphicsDevice;

        var vb = SBuffer.Vertex.New(dev, verts,   GraphicsResourceUsage.Immutable);
        var ib = SBuffer.Index.New( dev, indices,  GraphicsResourceUsage.Immutable);
        _gpuBuffers.Add(vb);
        _gpuBuffers.Add(ib);

        var meshDraw = new MeshDraw
        {
            PrimitiveType = PrimitiveType.TriangleList,
            DrawCount     = indices.Length,
            IndexBuffer   = new IndexBufferBinding(ib, is32Bit: false, indices.Length),
            VertexBuffers = new[]
            {
                new VertexBufferBinding(vb, VertexPositionNormalTexture.Layout, verts.Length),
            },
        };

        var mesh = new Mesh { Draw = meshDraw, BoundingBox = bbox, MaterialIndex = 0 };
        var model = new Model { mesh };
        model.BoundingBox = bbox;
        model.Materials.Add(new MaterialInstance());
        return model;
    }

    // ── IDisposable ─────────────────────────────────────────────────────────

    /// <summary>Removes all pool entities from the scene and releases GPU resources.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        RemovePool(_linePool);
        RemovePool(_boxPool);
        RemovePool(_spherePool);

        _materialCache.Clear(); // GPU resources released when GraphicsDevice is disposed.

        foreach (var buf in _gpuBuffers) buf.Dispose();
        _gpuBuffers.Clear();

        _lineModel = _boxModel = _sphereModel = null;
    }

    private void RemovePool(List<PoolEntry> pool)
    {
        foreach (var entry in pool)
        {
            _scene.Entities.Remove(entry.Entity);
            entry.Entity.Dispose();
        }
        pool.Clear();
    }
}
