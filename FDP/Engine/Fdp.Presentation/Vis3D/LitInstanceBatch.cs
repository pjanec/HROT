using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace Fdp.Toolkit.Vis3D
{
    /// <summary>⭐ CE-4555 (docs/DESIGN_Map_3D_Mode.md §6ak D-5) — what one instance carries besides its transform and colour: five vectors a host's
    /// surface function reads as <c>instData0..4</c> (<see cref="LitShader"/>'s instanced program). All zero = the plain surface.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LitInstanceData
    {
        public Vector4 D0, D1, D2, D3, D4;
    }

    /// <summary>
    /// ⭐ CE-4555 (docs/DESIGN_Map_3D_Mode.md §6ak.1 <c>BodyBatch</c>, G-1) — every copy of a mesh drawn in ONE instanced call: a frame
    /// <see cref="Begin"/>s, <see cref="Add"/>s instances (a raylib model matrix — what <c>Raylib.DrawMesh</c> takes —, a colour, the surface data) into
    /// the bucket of their mesh, and <see cref="Flush"/>es: one <c>glDrawElementsInstanced</c> per mesh with instances, through
    /// <see cref="LitShader.Instanced"/> — the same lighting, fog and surface code as a per-draw <c>DrawMesh</c>. ⛔ No allocation in the steady state:
    /// the CPU arrays and the GPU buffers are grown (doubled), never shrunk. A driver that refused the instanced program gets each instance drawn with
    /// the per-draw program instead (the colour kept, the surface data dropped).
    /// </summary>
    public sealed class LitInstanceBatch : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Instance
        {
            public Matrix4x4 Transform;   // GL column order (the transpose of raylib's DrawMesh matrix)
            public Vector4 Colour;
            public LitInstanceData Data;
        }

        private static readonly int Stride = Marshal.SizeOf<Instance>();   // 160 bytes

        private sealed class Bucket
        {
            public Mesh Mesh;
            public Instance[] Items = new Instance[64];
            public int Count;
            public uint Vbo;
            public int VboCapacity;   // instances
        }

        private Bucket[] _buckets = new Bucket[4];
        private int _bucketCount;

        /// <summary>Instanced draw calls and instances of the last <see cref="Flush"/> (the draw-call counter, §6ak.5 ②).</summary>
        public int DrawCalls { get; private set; }
        public int Instances { get; private set; }

        /// <summary>Instances added since <see cref="Begin"/> (all buckets).</summary>
        public int Pending { get; private set; }

        /// <summary>A mesh's bucket, for <see cref="Add"/>. Register each mesh once (the same mesh twice gets two buckets).</summary>
        public int Register(Mesh mesh)
        {
            if (_bucketCount == _buckets.Length) Array.Resize(ref _buckets, _buckets.Length * 2);
            _buckets[_bucketCount] = new Bucket { Mesh = mesh };
            return _bucketCount++;
        }

        /// <summary>Forget the last frame's instances (the arrays stay).</summary>
        public void Begin()
        {
            for (int i = 0; i < _bucketCount; i++) _buckets[i].Count = 0;
            Pending = 0;
        }

        /// <summary>One instance of bucket <paramref name="mesh"/>: <paramref name="model"/> is the matrix <c>Raylib.DrawMesh</c> would take
        /// (<c>HrotToRaylib.ModelFromHrot</c>), <paramref name="colour"/> what <c>LitShader.Tinted</c> would set.</summary>
        public void Add(int mesh, in Matrix4x4 model, Color colour, in LitInstanceData data)
        {
            var b = _buckets[mesh];
            if (b.Count == b.Items.Length) Array.Resize(ref b.Items, b.Items.Length * 2);
            ref var it = ref b.Items[b.Count++];
            it.Transform = Matrix4x4.Transpose(model);
            it.Colour = new Vector4(colour.R, colour.G, colour.B, colour.A) * (1f / 255f);
            it.Data = data;
            Pending++;
        }

        /// <summary>The instances of bucket <paramref name="mesh"/> added since <see cref="Begin"/> (rails).</summary>
        public int CountOf(int mesh) => _buckets[mesh].Count;

        /// <summary>Instance <paramref name="index"/> of bucket <paramref name="mesh"/>: its raylib model matrix, colour (0..1) and data (rails).</summary>
        public (Matrix4x4 Model, Vector4 Colour, LitInstanceData Data) Get(int mesh, int index)
        {
            ref readonly var it = ref _buckets[mesh].Items[index];
            return (Matrix4x4.Transpose(it.Transform), it.Colour, it.Data);
        }

        private const int GlFloat = 0x1406;
        private static readonly float[] White = { 1f, 1f, 1f, 1f };   // a stackalloc with an initializer allocated 64 B a frame (measured)

        /// <summary>⭐ CE-4555 — draw each instance with the PER-DRAW program instead (what the layer did before the batch): the rail that compares the
        /// two pictures, and the path a driver that refused the instanced program takes anyway.</summary>
        public bool ForcePerDraw { get; set; }

        /// <summary>Draws every bucket: one instanced call per mesh with instances, with the current camera (rlgl's matrices).
        /// <paramref name="perDrawSurface"/> sets an instance's surface data as the per-draw program's uniforms (the host's surface, e.g.
        /// <c>BodySurface.Apply</c>) on the per-draw path; null = the colour only.</summary>
        public unsafe void Flush(LitShader lit, Action<LitInstanceData>? perDrawSurface = null)
        {
            DrawCalls = 0;
            Instances = 0;
            if (Pending == 0) return;
            if (ForcePerDraw) { Rlgl.DrawRenderBatchActive(); FlushOneByOne(lit, perDrawSurface); return; }
            var shader = lit.Instanced;
            nint window = CurrentWindow();
            if (window != _window)
            {
                // a GL buffer dies with its context and its stale id still reads as valid (LitShader.Shared's note) — a new window starts empty
                for (int i = 0; i < _bucketCount; i++) { _buckets[i].Vbo = 0; _buckets[i].VboCapacity = 0; }
                _window = window;
            }
            Rlgl.DrawRenderBatchActive();   // rlgl's own buffered lines / quads go first, as they would before a DrawMesh
            if (!lit.InstancedIsValid) { FlushOneByOne(lit, perDrawSurface); return; }
            var mvp = Raymath.MatrixMultiply(Raymath.MatrixMultiply(Rlgl.GetMatrixTransform(), Rlgl.GetMatrixModelview()), Rlgl.GetMatrixProjection());
            Rlgl.EnableShader(shader.Id);
            Rlgl.SetUniformMatrix(lit.InstancedMvpLocation, mvp);
            for (int i = 0; i < _bucketCount; i++)
            {
                var b = _buckets[i];
                if (b.Count == 0) continue;
                Upload(b);
                if (!Rlgl.EnableVertexArray(b.Mesh.VaoId)) continue;
                int colourLoc = lit.InstancedVertexColorLocation;
                if (colourLoc >= 0 && b.Mesh.VboId[3] == 0)
                {
                    // ⭐ as DrawMesh does for a mesh with no colours: a constant white vertex colour
                    fixed (float* white = White) Rlgl.SetVertexAttributeDefault(colourLoc, white, (int)ShaderAttributeDataType.Vec4, 4);
                    Rlgl.DisableVertexAttribute((uint)colourLoc);
                }
                Rlgl.EnableVertexBuffer(b.Vbo);
                for (int v = 0; v < 4 + 1 + LitShader.InstanceDataVectors; v++)
                {
                    uint loc = (uint)(LitShader.InstanceTransformLocation + v);
                    Rlgl.SetVertexAttribute(loc, 4, GlFloat, false, Stride, v * 16);
                    Rlgl.EnableVertexAttribute(loc);
                    Rlgl.SetVertexAttributeDivisor(loc, 1);
                }
                if (b.Mesh.Indices != null) Rlgl.DrawVertexArrayElementsInstanced(0, b.Mesh.TriangleCount * 3, null, b.Count);
                else Rlgl.DrawVertexArrayInstanced(0, b.Mesh.VertexCount, b.Count);
                for (int v = 0; v < 4 + 1 + LitShader.InstanceDataVectors; v++)
                {
                    uint loc = (uint)(LitShader.InstanceTransformLocation + v);
                    Rlgl.SetVertexAttributeDivisor(loc, 0);
                    Rlgl.DisableVertexAttribute(loc);
                }
                Rlgl.DisableVertexBuffer();
                Rlgl.DisableVertexArray();
                DrawCalls++;
                Instances += b.Count;
            }
            Rlgl.DisableShader();
        }

        private void FlushOneByOne(LitShader lit, Action<LitInstanceData>? perDrawSurface)
        {
            for (int i = 0; i < _bucketCount; i++)
            {
                var b = _buckets[i];
                for (int k = 0; k < b.Count; k++)
                {
                    ref readonly var it = ref b.Items[k];
                    var c = it.Colour * 255f + new Vector4(0.5f);
                    perDrawSurface?.Invoke(it.Data);
                    Raylib.DrawMesh(b.Mesh, lit.Tinted(new Color((byte)c.X, (byte)c.Y, (byte)c.Z, (byte)c.W)), Matrix4x4.Transpose(it.Transform));
                    DrawCalls++;
                }
                Instances += b.Count;
            }
            if (perDrawSurface != null) perDrawSurface(default);   // the per-draw program back to the plain surface for whoever draws next
        }

        private unsafe void Upload(Bucket b)
        {
            if (b.VboCapacity < b.Count)
            {
                if (b.Vbo != 0) Rlgl.UnloadVertexBuffer(b.Vbo);
                int capacity = Math.Max(64, b.VboCapacity);
                while (capacity < b.Count) capacity *= 2;
                b.Vbo = Rlgl.LoadVertexBuffer(null, capacity * Stride, true);
                b.VboCapacity = capacity;
            }
            fixed (Instance* p = b.Items) Rlgl.UpdateVertexBuffer(b.Vbo, p, b.Count * Stride, 0);
        }

        private nint _window;

        private static unsafe nint CurrentWindow()
        {
            try { return (nint)Raylib.GetWindowHandle(); }
            catch (DllNotFoundException) { return 0; }
            catch (EntryPointNotFoundException) { return 0; }
        }

        /// <summary>Frees the GPU buffers and forgets every bucket (register the meshes again before the next use).</summary>
        public void Dispose()
        {
            for (int i = 0; i < _bucketCount; i++)
            {
                var b = _buckets[i];
                if (b.Vbo != 0) { try { Rlgl.UnloadVertexBuffer(b.Vbo); } catch (DllNotFoundException) { } }
                b.Vbo = 0; b.VboCapacity = 0;
                _buckets[i] = null!;
            }
            _bucketCount = 0;
            Pending = 0;
        }
    }
}
