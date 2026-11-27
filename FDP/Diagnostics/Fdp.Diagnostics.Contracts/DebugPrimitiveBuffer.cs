extern alias GizmoMapContracts;

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using Fdp.Core;
// Alias for the GizmoMap-side FixedString32 used in DebugPrimitive fields.
using GizmoStr = GizmoMapContracts::Fdp.Toolkit.Diagnostics.Gizmos.FixedString32;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    // Thread-safe append-only buffer of DebugPrimitive values.
    // Pre-allocated at construction; no per-frame heap allocation on the draw path.
    // When capacity is exhausted, primitives are silently dropped (DroppedCount tracks overflow).
    [Fdp.Core.WorldDerived("debug-draw scratch with persistent re-emit lifetimes; diagnostics only")]
    public sealed class DebugPrimitiveBuffer : IDebugDrawBuilder
    {
        private DebugPrimitive[] _primitives;   // ⭐ CE-1470 — grows (never shrinks) at a frame reset after a frame that overflowed
        private int _count;
        private int _droppedCount;
        private readonly StringInternMap _internMap;

        // Persistent re-emission: primitives with LifetimeSeconds > 0 survive across frames.
        private readonly DebugPrimitive[] _persistent;
        private readonly float[] _remainingLife;
        private int _persistentCount;
        private const int PersistentCapacity = 256;

        // Number of primitives dropped due to capacity overflow.
        public int DroppedCount => _droppedCount;

        // ⭐⭐ CE-1071 (docs/designs/gizmos-1/DESIGN.md §11.11) — THE LAYER MASK AT EMIT, AND THE PRIMITIVES OVERFLOW MAY NOT DROP.
        //   📐 Measured 2026-10-14 in the live editor (ua-fire-and-movement, one rifleman possessed): 4 096 primitives a frame and
        //   400-700 DROPPED, the hidden Navmesh / Cover / SpatialGrid layers filling 3 500 of the slots. The drops fell on whatever
        //   came last — the LayerControlMask (so the 3-D triage drew EVERY hidden layer: 612 spheres and 2 255 draped lines), the
        //   main-menu binding (View ▸ 2-D / 3-D Map and RTS Command Mode were missing) and a tool's InputCaptureBinding (the RTS
        //   tool never received a click). ⭐ A hidden-layer primitive is now refused at emit (it is counted, not stored), and when
        //   the buffer IS full a protected meta primitive takes the slot of the latest plain one instead of being dropped.
        private LayerMask256 _activeLayers;
        private bool _layersKnown;
        private int _suppressedCount;
        private int _evictFrom = int.MaxValue;
        private readonly object _evictLock = new();

        /// <summary>⭐ CE-1071 — primitives refused this frame because their layer is hidden (not an overflow).</summary>
        public int SuppressedCount => _suppressedCount;

        /// <summary>⭐ CE-1480 G5 — moves when the set of active layers changes (what <see cref="IsLayerActive"/> answers).</summary>
        public long LayersVersion { get; private set; }

        /// <inheritdoc/>
        public void SetActiveLayers(in LayerMask256 activeLayers)
        {
            // ⭐ CE-1480 G5 — LayersVersion moves only when the mask (or "known") actually changes, so a rule that skips drawing on a hidden layer can be held
            if (!_layersKnown || !System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref System.Runtime.CompilerServices.Unsafe.AsRef(in _activeLayers), 1))
                    .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref System.Runtime.CompilerServices.Unsafe.AsRef(in activeLayers), 1))))
                LayersVersion++;
            _activeLayers = activeLayers;
            _layersKnown = true;
        }

        /// <inheritdoc/>
        public bool IsLayerActive(byte layer) => !_layersKnown || _activeLayers.IsSet(layer);

        /// <summary>⭐ CE-1071 — the meta primitives a frame cannot lose: the layer mask, the menus, a tool's input capture and a
        /// panel. Never refused by layer, never dropped on overflow (they evict a plain primitive).</summary>
        public static bool IsProtected(DebugPrimitiveShape shape) => PrimitiveKind.IsProtected(shape);   // ⭐ CC-14 — the one classifier (anchors included, CC-16)

        /// <summary>⭐ CE-1160 — a PICK TARGET: an invisible <see cref="DebugPrimitiveShape.Box2D"/> carrying an anchor id, emitted purely so
        /// the terminal's hit-test can find the entity (<c>EntityPresentationGizmoShared.EmitPickBox</c>, <c>DebugPrimitive.MakePickSegment</c>).
        /// 🔒 User, 2026-10-16: <i>"I need the entity selection and right click work always independently of what i pick in the debug layers
        /// menu."</i> 🔴 It sits on layer 0 (so a tool handle on a higher layer still wins the pick), and CE-1071 refused hidden layers at
        /// emit ⇒ turning <c>Entities</c> off removed every pick box: no hover, no selection, no entity menu. Never refused by layer and
        /// never evicted for a protected primitive (like an anchor); it is drawn by nobody, so the mask has nothing to hide.</summary>
        public static bool IsPickTarget(in DebugPrimitive p) => PrimitiveKind.IsPickTarget(in p);   // ⭐ CC-14 — moved down; this forwards

        private bool RefusedByLayer(in DebugPrimitive p)
        {
            if (!_layersKnown || PrimitiveKind.IsLayerExempt(in p)) return false;
            if (_activeLayers.IsSet(p.DebugLayer)) return false;
            Interlocked.Increment(ref _suppressedCount);
            return true;
        }

        /// <summary>The buffer is full: a protected primitive replaces the latest plain one (not an anchor, not another protected
        /// one), so the frame keeps its mask, menus and bindings. The replaced one counts as dropped.</summary>
        private void StoreProtectedWhenFull(in DebugPrimitive p)
        {
            lock (_evictLock)
            {
                for (int i = Math.Min(_evictFrom, _primitives.Length - 1); i >= 0; i--)
                {
                    if (PrimitiveKind.IsLayerExempt(in _primitives[i])) continue;
                    _primitives[i] = p;
                    _evictFrom = i - 1;
                    Interlocked.Increment(ref _droppedCount);
                    return;
                }
                _evictFrom = -1;
                Interlocked.Increment(ref _droppedCount);
            }
        }

        // Snapshot of the current transient write cursor (clamped to capacity).
        // Safe to call from the same thread that calls UpdateAndDraw. Used as a
        // mark before a gizmo draws so that StampGizmoTypeId can identify which
        // primitives the gizmo emitted.
        public int Count => Math.Min(_count, _primitives.Length);

        // The intern map used by DrawTextLong. Exposed for consumers that resolve long-text hashes.
        public StringInternMap InternMap => _internMap;

        public DebugPrimitiveBuffer(int capacity = 4096, StringInternMap? internMap = null)
        {
            _primitives    = new DebugPrimitive[capacity];
            _persistent    = new DebugPrimitive[PersistentCapacity];
            _remainingLife = new float[PersistentCapacity];
            _internMap     = internMap ?? new StringInternMap();
        }

        // ⭐ EXPERIMENT (threaded kernel X-4, exp/threaded-kernel) — a PUBLISHED copy of the last finished frame for a reader on another thread.
        //   Triple buffer: the writer (the kernel's thread) copies its finished frame into a slot that is neither the latest nor the leased one;
        //   the reader (the drawing thread) leases the latest once per drawn frame. Off unless ReaderUsesPublished is set.
        private readonly DebugPrimitive[][] _pub = new DebugPrimitive[3][];
        private readonly int[] _pubCount = new int[3];
        private int _pubLatest = -1, _pubLeased = -1;
        private readonly object _pubLock = new();

        /// <summary>⭐ EXPERIMENT (X-4) — the drawing thread reads <see cref="ReaderFrame"/> (the published copy) instead of the live buffer.</summary>
        public bool ReaderUsesPublished { get; set; }

        /// <summary>⭐ EXPERIMENT (X-4) — the writer's thread: copy this finished frame into a free slot and make it the latest.</summary>
        public void PublishFrame()
        {
            int slot;
            lock (_pubLock) { slot = 0; while (slot == _pubLatest || slot == _pubLeased) slot++; }
            int n = Math.Min(_count, _primitives.Length);
            if (_pub[slot] is not { } arr || arr.Length < n) _pub[slot] = arr = new DebugPrimitive[Math.Max(n, _primitives.Length)];
            Array.Copy(_primitives, arr, n);
            lock (_pubLock) { _pubCount[slot] = n; _pubLatest = slot; }
        }

        /// <summary>⭐ EXPERIMENT (X-4) — the reader's thread, once per drawn frame: lease the latest published frame.</summary>
        public void LeasePublished() { lock (_pubLock) _pubLeased = _pubLatest; }

        /// <summary>The frame a DRAWING reader uses: the leased published copy when <see cref="ReaderUsesPublished"/>, else <see cref="GetFrame"/>.</summary>
        public ReadOnlySpan<DebugPrimitive> ReaderFrame()
        {
            if (!ReaderUsesPublished) return GetFrame();
            int l = _pubLeased;
            return l < 0 || _pub[l] is not { } a ? ReadOnlySpan<DebugPrimitive>.Empty : a.AsSpan(0, _pubCount[l]);
        }

        // Returns a zero-copy span of all primitives written this frame.
        public ReadOnlySpan<DebugPrimitive> GetFrame()
        {
            int count = Math.Min(_count, _primitives.Length);
            return _primitives.AsSpan(0, count);
        }

        /// <summary>⭐ <c>CE-1470</c> — the most the buffer grows to (primitives): past it a frame still drops, and says so.</summary>
        public const int MaxCapacity = 1 << 18;

        /// <summary>⭐ CE-1470 — the primitives the LAST finished frame dropped (the frame stats and the render telemetry read it; <see cref="DroppedCount"/>
        /// is the frame being written).</summary>
        public int LastFrameDropped { get; private set; }

        /// <summary>⭐ CE-1470 — telemetry over EVERY buffer in the process (the frame-stats line): the most primitives one frame dropped, and the largest
        /// capacity grown to. Written only at a frame reset that overflowed or grew.</summary>
        public static int PeakDropped, PeakCapacity;

        /// <summary>⭐ CE-1470 — how many times the buffer grew (telemetry).</summary>
        public int Grown { get; private set; }

        /// <summary>The slots there are now.</summary>
        public int Capacity => _primitives.Length;

        /// <summary>
        /// ⭐ <c>CE-1470</c> — at a frame reset (the one single-threaded moment): a frame that dropped primitives grows the buffer to hold all it was asked for
        /// (the next power of two, at most <see cref="MaxCapacity"/>), so the NEXT frame loses none. 📐 Measured on Windows (scale-1 + 1 000 men, 3-D, filter off):
        /// all 16 384 slots full and the rest dropped silently — which gizmos vanished depended on the order they drew in. Allocates only when it grows.
        /// </summary>
        private void GrowAfterOverflow()
        {
            LastFrameDropped = _droppedCount;
            if (_droppedCount > PeakDropped) PeakDropped = _droppedCount;
            if (_droppedCount <= 0 || _primitives.Length >= MaxCapacity) return;
            int want = Math.Max(_count, _primitives.Length + 1);
            int size = _primitives.Length;
            while (size < want && size < MaxCapacity) size <<= 1;
            if (size == _primitives.Length) return;
            Array.Resize(ref _primitives, Math.Min(size, MaxCapacity));
            Grown++;
            if (_primitives.Length > PeakCapacity) PeakCapacity = _primitives.Length;
        }

        // Resets the transient write cursor for the next frame. Persistent entries are NOT affected.
        // For frame-boundary management call EndFrame(deltaTime) instead.
        public void Clear()
        {
            GrowAfterOverflow();
            _count        = 0;
            _droppedCount = 0;
            _suppressedCount = 0;
            _evictFrom    = int.MaxValue;
            _scopeSlot    = NoScope;
            EmptyScopesDropped = 0;
            _foreignInScope = 0;
        }

        /// <summary>
        /// Appends a primitive directly into the transient buffer without persistence tracking.
        /// Used by network ingress (<see cref="DebugPrimitivesIngressTranslator"/>) to restore
        /// received primitives. Thread-safe (uses Interlocked).
        /// </summary>
        public void AppendRaw(in DebugPrimitive primitive)
        {
            // ⭐ §6.8 — the SAME invariant, on the other funnel. Every gizmo pick box, pick segment and
            //   binding reaches the buffer through EmitRaw, so this is the arm that actually catches a
            //   gizmo emitting without an identity.
            DebugPrimitive.AssertHasIdentity(in primitive);
            if (RefusedByLayer(in primitive)) return;   // ⭐ CE-1071
            if (Volatile.Read(ref _scopeSlot) >= 0 && Environment.CurrentManagedThreadId != _scopeThread)
                Interlocked.Increment(ref _foreignInScope);   // ⭐ S1d-1 (R7-13) — a foreign primitive inside a HUD run
            int slot = Interlocked.Increment(ref _count) - 1;
            if ((uint)slot < (uint)_primitives.Length)
                _primitives[slot] = primitive;
            else if (PrimitiveKind.IsProtected(in primitive))
                StoreProtectedWhenFull(in primitive);   // ⭐ CE-1071
            else
                Interlocked.Increment(ref _droppedCount);
        }

        // ⭐⭐⭐ Simulated devices S1d-1 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.6 D-41 / R4-5 / R5-7 / R6-5 / R6-9) — A HUD SCOPE:
        //   the SYSTEM that runs a HUD projector opens a scope for the projector's source, the projector draws, the system closes it — and the
        //   primitives in between are the source's HUD run, drawn ONLY by a view bound to that source (HudScopes.Keep). One scope open at a time,
        //   one writer while it is open (R7-13: the projectors run serially; the threaded node's contiguity is railed).
        private const int NoScope = -1, UnstoredScope = -2;
        private int _scopeSlot = NoScope;
        private int _scopeThread;
        private int _foreignInScope;

        /// <summary>⭐ S1d-1 (R7-13) — primitives appended by ANOTHER thread while a scope was open, this frame: each one is a foreign primitive
        /// inside a HUD run (the scope assumes one writer). 0 is the invariant; a non-zero value means the scope must be written per producer
        /// and merged. The check is one compare per append.</summary>
        public int ForeignAppendsInScope => Volatile.Read(ref _foreignInScope);

        /// <summary>⭐ S1d-1 — scopes this frame that were dropped empty (a rail reads it).</summary>
        public int EmptyScopesDropped { get; private set; }

        /// <summary>
        /// ⭐ S1d-1 (D-41) — opens a HUD scope for <paramref name="sourceNetworkId"/>: every primitive appended until <see cref="EndScope"/> is that
        /// source's HUD run. Returns the scope's slot, or −1 when the buffer is FULL (the scope is not stored — the run it would own cannot be
        /// stored either; counted in <see cref="DroppedCount"/>). An unclosed earlier scope is closed first. ⛔ Never refused by layer (a scope is
        /// protected). 0 B.
        /// </summary>
        public int BeginScope(long sourceNetworkId)
        {
            if (_scopeSlot != NoScope) EndScope();
            int slot = Interlocked.Increment(ref _count) - 1;
            if ((uint)slot < (uint)_primitives.Length)
            {
                _primitives[slot] = HudScopes.Make(sourceNetworkId);
                _scopeThread = Environment.CurrentManagedThreadId;
                Volatile.Write(ref _scopeSlot, slot);
                return slot;
            }
            Interlocked.Decrement(ref _count);   // nothing was stored: give the cursor back (the overflow count stays honest)
            Interlocked.Increment(ref _droppedCount);
            _scopeSlot = UnstoredScope;
            return -1;
        }

        /// <summary>
        /// ⭐ S1d-1 (D-41, R5-7) — closes the open scope: its <see cref="DebugPrimitive.HudScopeCount"/> is stamped at the scope's ACTUAL slot — the
        /// primitives STORED after it (a run that overflowed keeps the part that fit, still scoped: ⛔ a count of 0 there would release that part to
        /// every reader). An EMPTY scope is dropped (the cursor rolled back when it is the last primitive; else it stays as a count-0 meta shape every
        /// drawer skips). A slot inside the run that a protected primitive took on overflow (<c>StoreProtectedWhenFull</c>) is a meta shape — skipped
        /// by every drawer, already counted in <see cref="DroppedCount"/>. Returns the run's length. 0 B.
        /// </summary>
        public int EndScope()
        {
            int slot = _scopeSlot;
            Volatile.Write(ref _scopeSlot, NoScope);
            if (slot < 0) return 0;
            int stored = Math.Min(Volatile.Read(ref _count), _primitives.Length);
            int run = stored - (slot + 1);
            if (run <= 0)
            {
                if (Volatile.Read(ref _count) == slot + 1) Interlocked.Decrement(ref _count);
                else _primitives[slot].HudScopeCount = 0;
                EmptyScopesDropped++;
                return 0;
            }
            _primitives[slot].HudScopeCount = run;
            return run;
        }

        /// <summary>
        /// ⭐ S1d-1 (D-41 "a re-appended reused picture's scopes are re-stamped") — re-appends a HELD picture (CE-1480 G5 / CE-2402): each scope in it
        /// is re-opened and re-closed around its run, so its count is true for THIS frame's slots (a picture that no longer fits keeps a scope that
        /// owns only what was stored). 0 B.
        /// </summary>
        public void AppendHeld(ReadOnlySpan<DebugPrimitive> held)
        {
            int closeAt = -1;
            for (int k = 0; k < held.Length; k++)
            {
                if (HudScopes.IsScope(in held[k]))
                {
                    if (closeAt >= 0) { EndScope(); closeAt = -1; }
                    BeginScope(held[k].HudScopeSource);
                    closeAt = HudScopes.After(held, k);
                    if (closeAt == k + 1) { EndScope(); closeAt = -1; }
                    continue;
                }
                AppendRaw(in held[k]);
                if (k + 1 == closeAt) { EndScope(); closeAt = -1; }
            }
            if (closeAt >= 0) EndScope();
        }

        // IDebugDrawBuilder.EmitRaw -- forwards to AppendRaw so the interaction manager
        // can inject InputCaptureBinding meta-primitives via the draw builder interface.
        public void EmitRaw(in DebugPrimitive prim) => AppendRaw(in prim);

        /// <summary>
        /// Stamps <paramref name="gizmoTypeId"/> into the <see cref="DebugPrimitive.GizmoTypeId"/>
        /// field of every transient primitive in the half-open range [<paramref name="fromIndex"/>,
        /// <see cref="Count"/>). Only shapes for which offset 60 is free are written:
        /// <see cref="DebugPrimitiveShape.Box2D"/>, <see cref="DebugPrimitiveShape.StructInspector"/>,
        /// and <see cref="DebugPrimitiveShape.ContextMenuBinding"/>.
        /// SemanticShape, SpatialAnchor, and all other shapes are skipped to avoid corrupting their
        /// payload (SemanticShape.ResolvedRollRad shares offset 60).
        /// Persistent primitives (<c>_persistent</c>) are NOT stamped.
        /// </summary>
        public void StampGizmoTypeId(int fromIndex, uint gizmoTypeId)
        {
            int to = Count;
            for (int i = fromIndex; i < to; i++)
            {
                ref var p = ref _primitives[i];
                if (p.Shape == DebugPrimitiveShape.Box2D ||
                    p.Shape == DebugPrimitiveShape.StructInspector ||
                    p.Shape == DebugPrimitiveShape.ContextMenuBinding)
                {
                    p.GizmoTypeId = gizmoTypeId;
                }
            }
        }

        /// <summary>
        /// ⭐ <c>CE-1382</c> — moves every primitive of the half-open range [<paramref name="fromIndex"/>, <see cref="Count"/>) that sits on the
        /// default layer 0 onto <paramref name="layer"/> (a tool's handles and previews → <c>DebugTraceLayers.ToolHandles</c>, out of a film). Meta
        /// primitives (bindings, the layer mask, anchors) are left alone — they are routed by shape, not by layer. A primitive the tool put on a layer
        /// of its own stays there. Same mark-then-range shape as <see cref="StampGizmoTypeId"/>.
        /// </summary>
        public void MoveDefaultLayer(int fromIndex, byte layer)
        {
            int to = Count;
            for (int i = fromIndex; i < to; i++)
            {
                ref var p = ref _primitives[i];
                if (p.DebugLayer != 0 || PrimitiveKind.IsLayerExempt(in p)) continue;   // ⭐ CC-16 — a pick target keeps its layer too
                p.DebugLayer = layer;
            }
        }

        /// <summary>
        /// Advances the persistence clock, evicts expired entries, clears the transient buffer,
        /// and re-injects surviving persistent primitives. Call once per frame BEFORE gizmo
        /// systems execute (owned by DataDrivenGizmoSystem).
        /// </summary>
        public void EndFrame(float deltaTime)
        {
            // Compact persistent array: keep entries whose remaining life exceeds deltaTime.
            int writeIdx = 0;
            int count = Math.Min(_persistentCount, _persistent.Length);
            for (int i = 0; i < count; i++)
            {
                float newLife = _remainingLife[i] - deltaTime;
                if (newLife > 0f)
                {
                    _persistent[writeIdx]    = _persistent[i];
                    _remainingLife[writeIdx] = newLife;
                    writeIdx++;
                }
            }
            _persistentCount = writeIdx;

            // Reset the transient buffer.
            GrowAfterOverflow();   // ⭐ CE-1470
            _count        = 0;
            _droppedCount = 0;
            _suppressedCount = 0;
            _evictFrom    = int.MaxValue;
            _scopeSlot    = NoScope;
            EmptyScopesDropped = 0;
            _foreignInScope = 0;

            // Re-inject surviving persistent primitives into the start of the transient buffer.
            for (int i = 0; i < _persistentCount; i++)
            {
                int slot = Interlocked.Increment(ref _count) - 1;
                if ((uint)slot < (uint)_primitives.Length)
                    _primitives[slot] = _persistent[i];
                else
                    Interlocked.Increment(ref _droppedCount);
            }
        }

        // ---- IDebugDrawBuilder implementation --------------------------------

        public void DrawLine(
            Vector3 start, Vector3 end, Rgba32 color,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            LineStyle style = LineStyle.Solid)
        {
            Append(DebugPrimitive.MakeLine(start, end, color, thickness, sizeMode, target, layer, style));
        }

        public void DrawLineGradient(
            Vector3 start, Vector3 end, Rgba32 startColor, Rgba32 endColor,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            LineStyle style = LineStyle.Solid)
        {
            var p = DebugPrimitive.MakeLine(start, end, startColor, thickness, sizeMode, target, layer, style);
            p.EndColor = endColor;
            Append(p);
        }

        public void DrawSphere(
            Vector3 center, float radius, Rgba32 color,
            float thickness = 0f,
            SizeMode sizeMode = SizeMode.WorldMeters,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            Rgba32 fillColor = default,
            LineStyle style = LineStyle.Solid)
        {
            Append(DebugPrimitive.MakeSphere(center, radius, color, thickness, sizeMode, target, layer, fillColor, style));
        }

        public void DrawBox2D(
            Vector2 center, Vector2 extents, Rgba32 color,
            float angleDeg = 0f,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            Rgba32 fillColor = default,
            LineStyle style = LineStyle.Solid,
            long anchorId = 0,
            ushort subElementId = 0)
        {
            Append(DebugPrimitive.MakeBox2D(center, extents, color, angleDeg, thickness, sizeMode, target, layer, fillColor, style, anchorId, subElementId));
        }

        public void DrawFilledTriangle(
            Vector2 a, Vector2 b, Vector2 c, Rgba32 color,
            byte layer = 0,
            PipelineTarget target = PipelineTarget.All)
        {
            Append(DebugPrimitive.MakeFilledTriangle(a, b, c, color, target, layer));
        }

        public void DrawArrow(
            Vector3 from, Vector3 to, Rgba32 color,
            float headSize = 1f,
            byte layer = 0)
        {
            Append(DebugPrimitive.MakeArrow(from, to, color, headSize, layer));
        }

        public void DrawText(
            float x, float y, FixedString32 text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World,
            byte layer = 0,
            float fontSizePx = 0f,
            float lineOffsetPx = 0f, GizmoTextStyle style = GizmoTextStyle.Plain)
        {
            // StringHash is always 0 for inline FixedString32 mode.
            // Fdp.Core.FixedString32 and GizmoMap.Contracts.FixedString32 share identical
            // 32-byte sequential layout; reinterpret for the MakeText factory method.
            var gizmoText = Unsafe.As<FixedString32, GizmoStr>(ref text);
            Append(DebugPrimitive.MakeText(x, y, gizmoText, color, space, layer, fontSizePx, lineOffsetPx, style));
        }

        public void DrawTextLong(
            float x, float y, string text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World,
            byte layer = 0,
            float fontSizePx = 0f,
            float lineOffsetPx = 0f, GizmoTextStyle style = GizmoTextStyle.Plain)
        {
            uint hash = StringInternMap.Fnv1a32(text);
            _internMap.Intern(hash, text);   // idempotent; allocates only on first call

            var p = default(DebugPrimitive);
            p.Shape      = DebugPrimitiveShape.Text;
            p.Space      = space;
            p.Color      = color;
            p.TargetView = PipelineTarget.All;
            p.DebugLayer = layer;
            p.TextX      = x;
            p.TextY      = y;
            // StringHash overlay at offset 8 (overlapping AnchorIndex)
            p.StringHash  = hash;
            // Store first MaxLength chars inline as a preview; FixedString32 auto-truncates.
            // Construct via Fdp.Core.FixedString32 (same layout) then reinterpret.
            var coreStr = new FixedString32(text);
            p.TextContent = Unsafe.As<FixedString32, GizmoStr>(ref coreStr);
            // ThicknessU16 repurposed for Text: carries desired screen-pixel font size (not * 10).
            if (fontSizePx > 0f)
                p.ThicknessU16 = (ushort)fontSizePx;
            // Offset 12 carries the screen-pixel line offset for Text primitives (S6, signed).
            if (lineOffsetPx != 0f)
                p.LineOffsetPx = (short)lineOffsetPx;
            p.TextStyle = style;   // ⭐ B-17 P7 ⑥ — offset 14, Text only
            Append(p);
        }

        public void DrawEntityBadge(
            Entity target, FixedString32 richText,
            PipelineTarget targetPipeline = PipelineTarget.All)
        {
            var p = default(DebugPrimitive);
            p.Shape            = DebugPrimitiveShape.EntityBadge;
            p.TargetView       = targetPipeline;
            p.BadgeTargetIndex = target.Index;
            p.BadgeTargetGen   = target.Generation;
            // Fdp.Core.FixedString32 and GizmoMap.Contracts.FixedString32 have identical layout.
            p.BadgeRichText    = Unsafe.As<FixedString32, GizmoStr>(ref richText);
            Append(p);
        }

        public void DrawEntityLocal(
            int anchorNetworkId, Vector3 localStart, Vector3 localEnd,
            Rgba32 color, float thickness = 1f, byte layer = 0)
        {
            var p = default(DebugPrimitive);
            p.Shape            = DebugPrimitiveShape.Line;
            p.Space            = CoordinateSpace.EntityLocal;
            p.Color            = color;
            p.EndColor         = color;
            p.TargetView       = PipelineTarget.All;
            p.DebugLayer       = layer;
            p.SizeMode         = SizeMode.ScreenPixels;
            p.ThicknessU16     = (ushort)(thickness * 10f);
            // ⭐⭐⭐ CE-259z — offset 8 is the SpatialAnchor cache KEY, a NETWORK id.
            //   ⛔ It used to be `anchor.Index` (an ECS index) against a cache keyed by
            //     SpatialAnchor.NetworkId ⇒ the lookup missed and the primitive was SILENTLY SKIPPED.
            //   ⛔ And no AnchorGeneration is stamped: the generation is not part of the key, and
            //     stamping it here is what made offset 12 look like an identity component.
            AssertFitsAnchorKey(anchorNetworkId);
            p.AnchorIndex      = (int)anchorNetworkId;
            p.LineStart        = localStart;
            p.LineEnd          = localEnd;
            Append(p);
        }

        public void DrawEntityLocalInteractive(
            int anchorNetworkId, Vector3 localStart, Vector3 localEnd,
            Rgba32 color, ushort subElementId,
            float thickness = 1f, byte layer = 0)
        {
            var p = default(DebugPrimitive);
            p.Shape            = DebugPrimitiveShape.Line;
            p.Space            = CoordinateSpace.EntityLocal;
            p.Color            = color;
            p.EndColor         = color;
            p.TargetView       = PipelineTarget.All;
            p.DebugLayer       = layer;
            p.SizeMode         = SizeMode.ScreenPixels;
            p.ThicknessU16     = (ushort)(thickness * 10f);
            // ⭐ CE-259z — see DrawEntityLocal.
            // ⛔⛔ AND NO `BoxAnchorId`, THOUGH IT WOULD BE THE IDENTITY (S5) — IT PHYSICALLY DOES NOT
            //   FIT. Measured 2026-09-10: for a Line the payload union is LineStart @24-35 and LineEnd
            //   @36-47, with EndColor @48-51; `BoxAnchorId` is a long @44-51 ⇒ it OVERLAPS LineEnd.Z
            //   AND EndColor. Stamping it corrupts the geometry, or is silently overwritten by it
            //   (which is what the first attempt did — the rail read back 0).
            //   ⇒ ⭐⭐ THIS IS THE DEEPER REASON A LINE CANNOT BE INTERACTIVE, and it is stronger than
            //     "the hit-test does not handle Line" (CE-259ac): a Line has NO SLOT FOR AN IDENTITY.
            //     Anything pickable must be Box2D or Sphere, whose payloads leave offset 44 free.
            AssertFitsAnchorKey(anchorNetworkId);
            p.AnchorIndex      = (int)anchorNetworkId;
            p.LineStart        = localStart;
            p.LineEnd          = localEnd;
            p.SubElementId     = subElementId;
            Append(p);
        }

        public void DrawEntitySphere(
            int    anchorNetworkId,
            Vector3 worldCenter,
            float   radius,
            Rgba32  color,
            byte    layer = 0)
        {
            var p = default(DebugPrimitive);
            p.Shape            = DebugPrimitiveShape.Sphere;
            p.Space            = CoordinateSpace.World;
            p.SizeMode         = SizeMode.WorldMeters;
            p.TargetView       = PipelineTarget.Map2D;
            p.Color            = color;
            p.SphereCenter     = worldCenter;
            p.SphereRadius     = radius;
            p.DebugLayer       = layer;
            // ⭐⭐ §6.7 — IDENTITY, in the field the hit-test actually routes on. ⛔ This was
            //   `AnchorIndex = anchor.Index; AnchorGeneration = anchor.Generation` — an ECS handle in
            //   the two offsets nothing reads for identity any more, which left the sphere pickable in
            //   its doc comment and unpickable in fact. ⭐ A Sphere's payload (SphereCenter @24-35,
            //   SphereRadius @36-39) leaves offset 44 free, so BoxAnchorId fits — unlike a Line, whose
            //   LineEnd/EndColor overlap it (see DebugPrimitive.MakePickSegment's note).
            p.BoxAnchorId      = anchorNetworkId;
            Append(p);
        }

        /// <summary>
        /// ⭐⭐ <b>C7 / CE-259z — the <c>EntityLocal</c> anchor key is 32 bits and the narrowing is
        /// unchecked.</b> An id above <c>int.MaxValue</c> wraps, misses the <c>SpatialAnchor</c> cache and
        /// the primitive is skipped in silence. ⛔ It cannot be widened (<c>SemanticShape</c>'s payload
        /// union is full; 64 bytes is a DDS invariant) ⇒ assert, do not wrap.
        /// ⚠ <c>Debug.Assert</c>, not a throw: a diagnostic emitter must never take down a frame, and
        /// production ids count from 1 (<c>SequentialIdAllocator</c>), so this is a latent limit.
        /// </summary>
        private static void AssertFitsAnchorKey(int anchorNetworkId)
            => System.Diagnostics.Debug.Assert(
                   anchorNetworkId >= int.MinValue && anchorNetworkId <= int.MaxValue,
                   $"EntityLocal anchor id {anchorNetworkId} does not fit the 32-bit SpatialAnchor cache "
                 + "key; the primitive would silently fail to resolve. See DebugPrimitive.cs offset 8.");

        // ---- Internal helpers -----------------------------------------------

        // GZ057: SpatialAnchor and SemanticShape emit implementations.

        public void DrawSpatialAnchor(
            int  networkId,
            float worldX,
            float worldY,
            float worldZ,
            float headingDeg,
            float pitchDeg = 0f,
            float rollDeg = 0f,
            byte  layer = 0)
        {
            var p = default(DebugPrimitive);
            p.Shape        = DebugPrimitiveShape.SpatialAnchor;
            p.TargetView   = PipelineTarget.All;
            p.DebugLayer   = layer;
            p.NetworkId    = networkId;
            p.AnchorWorldX = worldX;
            p.AnchorWorldY = worldY;
            p.AnchorWorldZ = worldZ;
            p.Heading      = headingDeg;
            p.Pitch        = pitchDeg;
            p.Roll         = rollDeg;
            Append(p);
        }

        public void DrawSemanticShape(
            int   networkId,
            ulong  profileId,
            float  lengthMeters  = 0f,
            float  widthMeters   = 0f,
            uint   conditionMask = 0,
            byte   layer         = 0)
        {
            var p = default(DebugPrimitive);
            p.Shape         = DebugPrimitiveShape.SemanticShape;
            p.Space         = CoordinateSpace.EntityLocal;
            p.TargetView    = PipelineTarget.All;
            p.DebugLayer    = layer;
            // ⭐⭐⭐ S7 (DESIGN_Gizmo_Anchor_Identity.md §6, CE-259z) — offset 8 is the SpatialAnchor
            //   cache KEY for an EntityLocal primitive, and it is 32 bits wide. The cache is FILLED with
            //   the full 64-bit SpatialAnchor.NetworkId (DebugPrimitiveRenderer2D:63) and PROBED with
            //   this value widened back to long (:105), so an id above int.MaxValue wraps here and the
            //   lookup misses -- the shape is silently skipped, never drawn.
            //   ⛔ It cannot be widened: SemanticShape's payload union is full and 64 bytes is a
            //     DDS-marshalled invariant. ⇒ assert instead of wrapping in silence.
            //   ⚠ Debug.Assert, not a throw: a diagnostic emitter must never take down a frame, and
            //     production ids count from 1 (SequentialIdAllocator) so this is a latent limit, not a
            //     live failure. A throw here would be a new way to lose the map.
            System.Diagnostics.Debug.Assert(
                networkId >= int.MinValue && networkId <= int.MaxValue,
                $"SemanticShape anchor id {networkId} does not fit the 32-bit EntityLocal anchor key; " +
                "the primitive would silently fail to resolve. See DebugPrimitive.cs offset 8.");
            p.AnchorIndex   = (int)networkId;
            p.ProfileId     = profileId;
            p.LengthMeters  = lengthMeters;
            p.WidthMeters   = widthMeters;
            p.ConditionMask = conditionMask;
            Append(p);
        }

        public void DrawContextMenuBinding(int networkId, string menuJson)
        {
            uint hash = StringInternMap.Fnv1a32(menuJson);
            _internMap.Intern(hash, menuJson);   // idempotent; allocates only on first call
            Append(DebugPrimitive.MakeContextMenuBinding(networkId, hash));
        }

        /// <summary>⭐ B-17 P2 — a context-menu binding keyed by a 64-bit anchor (a TOOL id is above int range): the same interning, the same primitive.</summary>
        public void DrawContextMenuBinding(long anchorId, string menuJson)
        {
            uint hash = StringInternMap.Fnv1a32(menuJson);
            _internMap.Intern(hash, menuJson);
            Append(DebugPrimitive.MakeContextMenuBinding(anchorId, hash));
        }

        public void DrawMainMenuBinding(string menuJson)
        {
            uint hash = StringInternMap.Fnv1a32(menuJson);
            _internMap.Intern(hash, menuJson);   // idempotent; allocates only on first call
            Append(DebugPrimitive.MakeMainMenuBinding(hash));
        }

        internal void Append(DebugPrimitive p)
        {
            DebugPrimitive.AssertHasIdentity(in p);
            if (RefusedByLayer(in p)) return;   // ⭐ CE-1071 — a hidden layer is not stored, and not persisted either
            if (Volatile.Read(ref _scopeSlot) >= 0 && Environment.CurrentManagedThreadId != _scopeThread)
                Interlocked.Increment(ref _foreignInScope);   // ⭐ S1d-1 (R7-13) — a foreign primitive inside a HUD run
            int slot = Interlocked.Increment(ref _count) - 1;
            if ((uint)slot < (uint)_primitives.Length)
                _primitives[slot] = p;
            else if (PrimitiveKind.IsProtected(in p))
                StoreProtectedWhenFull(in p);   // ⭐ CE-1071
            else
                Interlocked.Increment(ref _droppedCount);

            // Persist primitives with a positive lifetime.
            if (p.LifetimeSeconds > 0f)
            {
                int pSlot = Interlocked.Increment(ref _persistentCount) - 1;
                if ((uint)pSlot < (uint)_persistent.Length)
                {
                    _persistent[pSlot]    = p;
                    _remainingLife[pSlot] = p.LifetimeSeconds;
                }
                else
                {
                    Interlocked.Decrement(ref _persistentCount);
                    Interlocked.Increment(ref _droppedCount);
                }
            }
        }
    }
}
