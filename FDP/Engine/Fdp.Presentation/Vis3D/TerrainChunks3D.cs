using System;
using System.Numerics;
using Fdp.Toolkit.World;
using Raylib_cs;

namespace Fdp.Toolkit.Vis3D
{
    /// <summary>
    /// ⭐⭐ CE-3423 (docs/DESIGN_Map_3D_Mode.md §6ah, L-1 … L-9) — <b>a chunked terrain in 3-D</b>: per chunk a ground mesh per level of detail and one mesh for what stands on it,
    /// each built on a worker (<see cref="BackgroundMeshBuild{TStreams}"/>) from <see cref="ITerrainChunkedGeometry.BuildChunk"/> and uploaded on the frame; one mesh for the
    /// outside (the apron). The frame picks each chunk's level by the eye's distance, culls by the view frustum, draws the finest level it already HAS, and asks for the one it
    /// wants — ⛔ it never builds and never allocates (index loops over arrays sized once per terrain).
    /// </summary>
    internal sealed class TerrainChunks3D : IDisposable
    {
        /// <summary>L-7 — builds running at once.</summary>
        public static int MaxInFlight { get; set; } = Math.Clamp(Environment.ProcessorCount - 1, 2, 6);
        /// <summary>L-7 — vertex bytes uploaded per frame (at least one mesh a frame).</summary>
        public const long UploadBytesPerFrame = 24L << 20;
        /// <summary>L-9 — the resident vertex bytes of the FINER levels before the least recently drawn are unloaded.</summary>
        public static long FineBudgetBytes { get; set; } = 1536L << 20;

        private const int BytesPerVertex = 12 + 12 + 4;   // position, normal, colour

        private sealed class Slot
        {
            public readonly BackgroundMeshBuild<Streams> Build = new();
            /// <summary>A ground level finer than the coarsest (the budget may unload it).</summary>
            public bool Fine;
            public Mesh Mesh;
            public bool Has;
            public long Bytes;
            public long LastDrawn;
            public int Triangles;
            /// <summary>⭐ CE-4392 — the damage changed under this slot: rebuild it once its current build is done.</summary>
            public bool Stale;
            public float MinZ = float.MaxValue, MaxZ = float.MinValue;

            public void Release()
            {
                if (Has) Raylib.UnloadMesh(Mesh);
                Has = false; Bytes = 0; Triangles = 0;
            }
        }

        internal sealed record Streams(Vector3[] Positions, Vector3[] Normals, Color[] Colors, float MinZ, float MaxZ);

        private ITerrainChunkedGeometry _geometry;   // ⭐ U-6 — replaced by Adopt
        private object _identity;
        private readonly TerrainChunkLayout _layout;
        private readonly int _n, _levels;
        private readonly Slot[] _ground;      // [chunk * levels + level]
        private readonly Slot[] _objects;     // [chunk]
        private readonly Slot[] _canopy;      // [chunk] ⭐ CE-4377 — the trees FAR (part of the coarse picture)
        private readonly Slot[] _trees;       // [chunk] ⭐ CE-4377 — the trees NEAR (built when the chunk wants level ≤ 1)
        private readonly Slot _outside = new();
        private readonly int[] _drawnLevel;  // per chunk, last frame (−1: culled or nothing built)
        private readonly int[] _flight;       // slot ids in flight: ground k*levels+l, objects N*levels + k, outside -1
        private int _inFlight;
        private long _frame, _fineBytes;
        private Vector3 _eye;                 // HROT space
        private bool _hasEye;
        private readonly Vector4[] _planes = new Vector4[6];   // raylib space
        private bool _hasPlanes;

        public TerrainChunks3D(ITerrainChunkedGeometry geometry)
        {
            _geometry = geometry;
            _identity = geometry.Identity;
            _layout = geometry.Chunks;
            _n = _layout.Nx * _layout.Ny;
            _levels = Math.Max(1, _layout.Levels);
            _ground = new Slot[_n * _levels];
            for (int i = 0; i < _ground.Length; i++) _ground[i] = new Slot { Fine = i % _levels != _levels - 1 };
            _objects = new Slot[_n];
            for (int i = 0; i < _n; i++) _objects[i] = new Slot();
            _canopy = new Slot[_n]; _trees = new Slot[_n];
            for (int i = 0; i < _n; i++) { _canopy[i] = new Slot(); _trees[i] = new Slot { Fine = true }; }
            _flight = new int[Math.Max(1, MaxInFlight)];
            _drawnLevel = new int[_n];
            Array.Fill(_drawnLevel, -1);
        }

        public object Identity => _identity;

        /// <summary>⭐ U-6 — slots (ground levels, objects, canopy, trees) the last <see cref="Adopt"/> marked for a rebuild; and the chunks it touched (rails).</summary>
        public int AdoptedSlots { get; private set; }
        public int AdoptedChunks { get; private set; }

        /// <summary>⭐ U-6 — true when <paramref name="geometry"/> has the same chunk grid as this set: a new world over the same ground can then be ADOPTED (only what changed rebuilds).</summary>
        public bool CanAdopt(ITerrainChunkedGeometry geometry) => geometry.Chunks.Equals(_layout);

        /// <summary>
        /// ⭐ U-6 (docs/DESIGN_Terrain_Authoring.md §15a, §18e-2 B-5) — the terrain was REPLACED by a world that differs from this one only inside <paramref name="dirty"/> (world metres; the editor's live bake: a block moved, a feature placed): keep every
        /// chunk's meshes and mark ONLY the chunks those rectangles meet stale — all their slots (ground at every level, objects, canopy, trees) rebuild off the frame thread from <paramref name="geometry"/>, the old mesh drawn until the new one uploads.
        /// ⛔ Never a rebuild of the whole terrain. Returns false (nothing changed) when the chunk grid differs — the caller then builds a fresh set.
        /// </summary>
        public bool Adopt(ITerrainChunkedGeometry geometry, System.Collections.Generic.IReadOnlyList<(Vector2 Min, Vector2 Max)> dirty)
        {
            if (!CanAdopt(geometry)) return false;
            _geometry = geometry;
            _identity = geometry.Identity;
            AdoptedSlots = 0; AdoptedChunks = 0;
            for (int k = 0; k < _n; k++)
            {
                var min = _layout.ChunkMin(k % _layout.Nx, k / _layout.Nx); var max = _layout.ChunkMax(k % _layout.Nx, k / _layout.Nx);
                bool touched = false;
                for (int d = 0; d < dirty.Count && !touched; d++)   // ⭐ a margin: a canopy overhangs and a fine level gathers a little past its edge
                    touched = dirty[d].Max.X + DirtyMarginMetres >= min.X && dirty[d].Min.X - DirtyMarginMetres <= max.X && dirty[d].Max.Y + DirtyMarginMetres >= min.Y && dirty[d].Min.Y - DirtyMarginMetres <= max.Y;
                if (!touched) continue;
                AdoptedChunks++;
                for (int l = 0; l < _levels; l++) MarkStale(_ground[(k * _levels) + l]);
                MarkStale(_objects[k]); MarkStale(_canopy[k]); MarkStale(_trees[k]);
            }
            return true;
        }

        /// <summary>The margin (metres) added round a dirty rectangle when deciding which chunks it touches.</summary>
        public const float DirtyMarginMetres = 8f;

        private void MarkStale(Slot s)
        {
            if (s.Build.State == MeshBuildState.Idle && !s.Has) return;   // never built: its first build reads the new geometry
            s.Stale = true; AdoptedSlots++;
        }

        private object? _damage;
        private readonly List<int> _touched = new();

        /// <summary>How many objects slots a damage change marked for a rebuild (a rail).</summary>
        public int DamageRebuilds { get; private set; }

        /// <summary>
        /// ⭐ CE-4392 (DESIGN_Map_3D_Mode §6aj X-3) — this frame's damage (<see cref="ITerrainChunkedGeometry.Damage"/>, opaque). The same instance ⇒ nothing (0 B); a new one ⇒ the
        /// objects slot of every chunk the change touches is rebuilt off the frame thread, the old mesh drawn until the new one uploads. ⛔ Never the whole terrain.
        /// </summary>
        public void SetDamage(ITerrainChunkedGeometry geometry)
        {
            var damage = geometry.Damage;
            if (ReferenceEquals(damage, _damage)) return;
            _touched.Clear();
            geometry.ChunksTouchedBy(_damage, damage, _touched);
            _damage = damage;
            foreach (int k in _touched) if ((uint)k < (uint)_objects.Length) { _objects[k].Stale = true; DamageRebuilds++; }
        }
        public TerrainChunkLayout Layout => _layout;

        /// <summary>Triangles drawn last frame / meshes drawn last frame (the rails' and the status file's numbers).</summary>
        public int TrianglesDrawn { get; private set; }
        public int MeshesDrawn { get; private set; }
        /// <summary>Resident vertex bytes (all levels, objects, outside).</summary>
        public long ResidentBytes { get; private set; }
        public string? Failure { get; private set; }

        /// <summary>The level chunk (cx, cy) drew last frame (−1: culled or nothing built) — a rail.</summary>
        public int LevelDrawn(int cx, int cy) => _drawnLevel[(cy * _layout.Nx) + cx];

        /// <summary>Builds running or waiting for upload (0 = everything the eye wants is in).</summary>
        public int Pending => _inFlight;

        /// <summary>L-7 — the load's "built": the outside, and every chunk's COARSEST ground and its objects.</summary>
        public bool CoarseBuilt
        {
            get
            {
                if (!_outside.Has && _outside.Build.State != MeshBuildState.Failed) return false;
                int coarse = _levels - 1;
                for (int k = 0; k < _n; k++)
                {
                    if (!Done(_ground[(k * _levels) + coarse])) return false;
                    if (!Done(_objects[k])) return false;
                    if (!Done(_canopy[k])) return false;
                }
                return true;
            }
        }

        private static bool Done(Slot s) => s.Has || s.Build.State is MeshBuildState.Built or MeshBuildState.Failed;

        /// <summary>The level wanted for chunk (cx, cy) at the eye's distance (L-2): &lt; 1.5 chunk → 0, &lt; 3 → 1, &lt; 6 → 2, else 3 (capped by the layout's levels).</summary>
        public static int LevelFor(TerrainChunkLayout layout, int cx, int cy, Vector3 eye)
        {
            var min = layout.ChunkMin(cx, cy); var max = layout.ChunkMax(cx, cy);
            float dx = MathF.Max(0f, MathF.Max(min.X - eye.X, eye.X - max.X));
            float dy = MathF.Max(0f, MathF.Max(min.Y - eye.Y, eye.Y - max.Y));
            float dz = MathF.Max(0f, MathF.Max(layout.MinZ - eye.Z, eye.Z - layout.MaxZ));
            float d = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) / layout.ChunkSize;
            int level = d < 1.5f ? 0 : d < 3f ? 1 : d < 6f ? 2 : 3;
            return Math.Min(level, Math.Max(1, layout.Levels) - 1);
        }

        /// <summary>Called every frame (2-D too, by the map's build): lands finished builds, then starts the next ones.</summary>
        public void Pump()
        {
            _frame++;
            long uploaded = 0;
            for (int f = 0; f < _inFlight; f++)
            {
                var slot = SlotOf(_flight[f]);
                if (uploaded >= UploadBytesPerFrame && uploaded > 0) break;
                _uploading = slot; _uploadedBytes = 0;
                bool done = slot.Build.Pump(_upload);   // ⭐ a cached delegate: no closure a frame
                uploaded += _uploadedBytes;
                if (slot.Build.State == MeshBuildState.Failed) { Failure ??= slot.Build.Failure; done = true; }
                if (done) { _flight[f] = _flight[--_inFlight]; f--; }
            }
            RescheduleStale(_objects);   // ⭐ CE-4392 — a stale slot whose build is finished goes back to the schedule
            if (AdoptedSlots > 0) { RescheduleStale(_ground); RescheduleStale(_canopy); RescheduleStale(_trees); }   // ⭐ U-6 — the same for what a live bake changed

            Schedule();
            Evict();
        }

        private static void RescheduleStale(Slot[] slots)
        {
            for (int k = 0; k < slots.Length; k++)
            {
                var o = slots[k];
                if (!o.Stale) continue;
                if (o.Build.State is MeshBuildState.Built or MeshBuildState.Failed) { o.Build.Reset(); o.Stale = false; }
                else if (o.Build.State == MeshBuildState.Idle) o.Stale = false;   // not built yet: its first build reads the current geometry
            }
        }

        private Slot? _uploading;
        private long _uploadedBytes;
        private Action<Streams>? _uploadDelegate;
        private Action<Streams> _upload => _uploadDelegate ??= st => _uploadedBytes = Upload(_uploading!, st);

        /// <summary>Slot ids: ground [0, G) · objects [G, G+n) · canopy [G+n, G+2n) · trees [G+2n, G+3n) · the outside −1.</summary>
        private Slot SlotOf(int id)
        {
            if (id < 0) return _outside;
            if (id < _ground.Length) return _ground[id];
            int r = id - _ground.Length;
            return r < _n ? _objects[r] : r < 2 * _n ? _canopy[r - _n] : _trees[r - (2 * _n)];
        }

        private bool Idle(Slot s) => s.Build.State == MeshBuildState.Idle;

        /// <summary>Fills the free build slots: the outside, then every chunk's coarsest ground + objects (nearest first), then the wanted finer levels (nearest first).</summary>
        private void Schedule()
        {
            while (_inFlight < _flight.Length)
            {
                int pick = int.MinValue;
                if (Idle(_outside)) pick = -1;
                else
                {
                    float best = float.MaxValue;
                    int coarse = _levels - 1;
                    for (int k = 0; k < _n; k++)
                    {
                        float d = Distance(k);
                        int g = (k * _levels) + coarse;
                        if (Idle(_ground[g]) && d < best) { best = d; pick = g; }
                        if (Idle(_objects[k]) && d < best) { best = d; pick = _ground.Length + k; }
                        if (Idle(_canopy[k]) && d < best) { best = d; pick = _ground.Length + _n + k; }
                    }
                    if (pick == int.MinValue && _hasEye)
                    {
                        best = float.MaxValue;
                        for (int k = 0; k < _n; k++)
                        {
                            int want = LevelFor(_layout, k % _layout.Nx, k / _layout.Nx, _eye);
                            int g = (k * _levels) + want;
                            float d = Distance(k);
                            if (Idle(_ground[g])) { if (d < best) { best = d; pick = g; } }
                            else if (want <= 1 && Idle(_trees[k]) && d < best) { best = d; pick = _ground.Length + (2 * _n) + k; }   // T-2: the near trees after the ground
                        }
                    }
                }
                if (pick == int.MinValue) return;
                Start(pick);
            }
        }

        private float Distance(int k)
        {
            if (!_hasEye) return k;   // no eye yet: grid order
            var min = _layout.ChunkMin(k % _layout.Nx, k / _layout.Nx);
            var c = min + new Vector2(_layout.ChunkSize * 0.5f);
            return Vector2.DistanceSquared(c, new Vector2(_eye.X, _eye.Y));
        }

        private void Start(int id)
        {
            var slot = SlotOf(id);
            var g = _geometry;
            int r = id - _ground.Length;
            int k = id < 0 ? -1 : id < _ground.Length ? id / _levels : r % _n;
            int level = id >= 0 && id < _ground.Length ? id % _levels : 0;
            var part = id < _ground.Length ? TerrainChunkPart.ChunkGround : r < _n ? TerrainChunkPart.ChunkObjects : r < 2 * _n ? TerrainChunkPart.ChunkCanopy : TerrainChunkPart.ChunkTrees;
            int cx = k < 0 ? 0 : k % _layout.Nx, cy = k < 0 ? 0 : k / _layout.Nx;
            _flight[_inFlight++] = id;
            var damage = part == TerrainChunkPart.ChunkObjects ? _damage : null;   // ⭐ CE-4392 — the objects as this frame's damage leaves them
            slot.Build.Request(_identity, () =>
            {
                Vector3[] v; int[] i; TerrainSurfaceKind[] kinds; string?[] mats;
                if (k < 0) g.BuildOutside(out v, out i, out kinds, out mats);
                else g.BuildChunk(cx, cy, level, part, damage, out v, out i, out kinds, out mats);
                TerrainLayer3D.BuildVertexData(v, i, kinds, mats, out var positions, out var normals, out var colors);
                float lo = float.MaxValue, hi = float.MinValue;
                for (int j = 0; j < v.Length; j++) { lo = MathF.Min(lo, v[j].Z); hi = MathF.Max(hi, v[j].Z); }
                return new Streams(positions, normals, colors, lo, hi);
            });
        }

        /// <summary>FRAME THREAD: one finished mesh onto the GPU. Returns its vertex bytes.</summary>
        private long Upload(Slot slot, Streams st)
        {
            ResidentBytes -= slot.Bytes;
            if (IsFine(slot)) _fineBytes -= slot.Bytes;
            slot.Release();
            int verts = st.Positions.Length;
            slot.MinZ = st.MinZ; slot.MaxZ = st.MaxZ;
            if (verts == 0) return 0;
            var mesh = new Mesh(verts, verts / 3);
            mesh.AllocVertices(); mesh.AllocNormals(); mesh.AllocColors();
            st.Positions.AsSpan().CopyTo(mesh.VerticesAs<Vector3>());
            st.Normals.AsSpan().CopyTo(mesh.NormalsAs<Vector3>());
            st.Colors.AsSpan().CopyTo(mesh.ColorsAs<Color>());
            Raylib.UploadMesh(ref mesh, false);
            slot.Mesh = mesh; slot.Has = true; slot.Triangles = verts / 3;
            slot.Bytes = (long)verts * BytesPerVertex;
            slot.LastDrawn = _frame;
            ResidentBytes += slot.Bytes;
            if (IsFine(slot)) _fineBytes += slot.Bytes;
            return slot.Bytes;
        }

        private static bool IsFine(Slot slot) => slot.Fine;

        /// <summary>L-9 — over the budget: unload the least recently drawn finer level (one a frame is enough to converge).</summary>
        private void Evict()
        {
            if (_fineBytes <= FineBudgetBytes) return;
            int oldest = -1; long when = long.MaxValue;
            for (int i = 0; i < _ground.Length; i++)
            {
                if (i % _levels == _levels - 1) continue;
                var s = _ground[i];
                if (s.Has && s.LastDrawn < when && s.LastDrawn < _frame) { when = s.LastDrawn; oldest = i; }
            }
            Slot? slot = oldest >= 0 ? _ground[oldest] : null;
            for (int k = 0; k < _n; k++)   // the near trees are fine too
            {
                var s = _trees[k];
                if (s.Has && s.LastDrawn < when && s.LastDrawn < _frame) { when = s.LastDrawn; slot = s; }
            }
            if (slot == null) return;
            ResidentBytes -= slot.Bytes; _fineBytes -= slot.Bytes;
            slot.Release();
            slot.Build.Reset();
        }

        /// <summary>FRAME THREAD, inside the 3-D pass: reads the eye and the frustum from rlgl, then draws each visible chunk's finest built level (≤ the wanted one first).</summary>
        public void Draw()
        {
            ReadView();
            var lit = LitShader.Shared;
            var material = lit.Tinted(Color.White);
            int tris = 0, meshes = 0;
            if (_outside.Has) { Raylib.DrawMesh(_outside.Mesh, material, Matrix4x4.Identity); tris += _outside.Triangles; meshes++; }
            int coarse = _levels - 1;
            for (int k = 0; k < _n; k++)
            {
                int cx = k % _layout.Nx, cy = k / _layout.Nx;
                var obj = _objects[k];
                float lo = _layout.MinZ, hi = _layout.MaxZ;
                _drawnLevel[k] = -1;
                if (!Visible(cx, cy, lo, hi)) continue;
                int want = _hasEye ? LevelFor(_layout, cx, cy, _eye) : coarse;
                Slot? g = null;
                for (int l = want; l < _levels && g == null; l++) if (_ground[(k * _levels) + l].Has) g = _ground[(k * _levels) + l];   // the wanted, else coarser
                for (int l = want - 1; l >= 0 && g == null; l--) if (_ground[(k * _levels) + l].Has) g = _ground[(k * _levels) + l];  // else a finer one still resident
                if (g != null) { _drawnLevel[k] = Array.IndexOf(_ground, g, k * _levels, _levels) - (k * _levels); g.LastDrawn = _frame; Raylib.DrawMesh(g.Mesh, material, Matrix4x4.Identity); tris += g.Triangles; meshes++; }
                if (obj.Has) { obj.LastDrawn = _frame; Raylib.DrawMesh(obj.Mesh, material, Matrix4x4.Identity); tris += obj.Triangles; meshes++; }
                // ⭐ CE-4377 §6ai — the trees: near where the chunk wants level ≤ 1 and they are built, else the far canopy
                var veg = want <= 1 && _trees[k].Has ? _trees[k] : _canopy[k].Has ? _canopy[k] : null;
                if (veg != null) { veg.LastDrawn = _frame; Raylib.DrawMesh(veg.Mesh, material, Matrix4x4.Identity); tris += veg.Triangles; meshes++; }
            }
            TrianglesDrawn = tris; MeshesDrawn = meshes;
            TerrainDrawStats.Record(tris, meshes, ResidentBytes);   // ⭐ CE-4385 — the process' last drawn chunked terrain, for GET /diagnostics/render
        }

        /// <summary>The eye (HROT space) and the six frustum planes (raylib space) from rlgl's current matrices — the camera of THIS pass, on screen or in a film.</summary>
        private void ReadView()
        {
            var view = Rlgl.GetMatrixModelview();
            var proj = Rlgl.GetMatrixProjection();
            _hasEye = View.TryEye(view, out var eyeRaylib);
            if (_hasEye) _eye = HrotToRaylib.ToHrot(eyeRaylib);
            _hasPlanes = View.TryPlanes(view, proj, _planes);
        }

        private bool Visible(int cx, int cy, float lo, float hi)
        {
            if (!_hasPlanes) return true;
            var min = _layout.ChunkMin(cx, cy); var max = _layout.ChunkMax(cx, cy);
            // HROT (x, y, z) → raylib (x, z, −y): the box's raylib extent
            var rMin = new Vector3(min.X, lo, -max.Y);
            var rMax = new Vector3(max.X, hi, -min.Y);
            return View.BoxInside(_planes, rMin, rMax);
        }

        /// <summary>Sets the eye by hand (a rail with no 3-D pass).</summary>
        internal void SetEye(Vector3 eyeHrot) { _eye = eyeHrot; _hasEye = true; }

        public void Dispose()
        {
            _outside.Release();
            foreach (var s in _ground) s.Release();
            foreach (var s in _objects) s.Release();
            foreach (var s in _canopy) s.Release();
            foreach (var s in _trees) s.Release();
            ResidentBytes = 0; _fineBytes = 0;
        }
    }

    /// <summary>
    /// ⭐ CE-3423 L-8 — the eye and the frustum from rlgl's matrices. 📐 raylib-cs hands raylib's <c>Matrix</c> over as a <see cref="Matrix4x4"/> whose ROWS are the maths rows
    /// (translation in M14 / M24 / M34, column vectors): the eye is the inverse view's translation; the planes are Gribb–Hartmann on projection × view. Proven by
    /// <c>TheTerrainChunksTests</c> against a known <c>Camera3D</c>.
    /// </summary>
    internal static class View   // ⭐ CE-4388 — the one implementation moved to the shared ViewFrustum; this forwards (no rename)
    {
        public static bool TryEye(Matrix4x4 view, out Vector3 eye) => ViewFrustum.TryEye(view, out eye);
        public static bool TryPlanes(Matrix4x4 view, Matrix4x4 proj, Vector4[] planes) => ViewFrustum.TryPlanes(view, proj, planes);
        public static bool BoxInside(Vector4[] planes, Vector3 min, Vector3 max) => ViewFrustum.BoxInside(planes, min, max);
    }
}

namespace Fdp.Toolkit.Vis3D
{
    /// <summary>⭐ CE-4385 (handoff L-3) — the LAST chunked terrain drawn in this process (one window): triangles, meshes (= draw calls) and resident mesh bytes,
    /// read by the render telemetry (<c>GET /diagnostics/render</c>).</summary>
    public static class TerrainDrawStats
    {
        public static int Triangles { get; private set; }
        public static int Meshes { get; private set; }
        public static long ResidentBytes { get; private set; }
        internal static void Record(int triangles, int meshes, long residentBytes) { Triangles = triangles; Meshes = meshes; ResidentBytes = residentBytes; }
    }

    /// <summary>⭐ CE-4555 / CE-4557 / CE-4558 (docs/DESIGN_Map_3D_Mode.md §6ak.5) — the LAST body layer drawn in this process: its instanced draw calls and
    /// instances, the bodies by screen-size band, the doors drawn and hidden, and the pick's work — read by <c>GET /diagnostics/render</c>.</summary>
    public static class BodyDrawStats
    {
        public static int DrawCalls { get; private set; }
        public static int Instances { get; private set; }
        public static int Bodies { get; private set; }
        public static int Doors { get; private set; }
        public static int Full { get; private set; }
        public static int Far { get; private set; }
        public static int Hidden { get; private set; }
        public static int DoorsHidden { get; private set; }
        /// <summary>Boxes the last pick tested, and picks run since the process started (0 work while nothing moves, D-6).</summary>
        public static int PickBoxesTested { get; private set; }
        public static long Picks { get; private set; }

        public static void Record(int drawCalls, int instances, int bodies, int doors) { DrawCalls = drawCalls; Instances = instances; Bodies = bodies; Doors = doors; }
        public static void RecordBands(int full, int far, int hidden, int doorsHidden) { Full = full; Far = far; Hidden = hidden; DoorsHidden = doorsHidden; }
        public static void RecordPick(int boxesTested) { PickBoxesTested = boxesTested; Picks++; }
    }
}
