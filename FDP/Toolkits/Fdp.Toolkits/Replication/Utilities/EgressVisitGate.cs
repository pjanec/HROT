using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Replication.Utilities
{
    /// <summary>
    /// ⭐ CE-2407 (docs/DESIGN_Egress_Change_Detection.md §5, E1) — tells an egress translator which entities of its own query CANNOT have
    /// changed since its last scan, so it skips them before the authority check and its own publish test. The translator keeps its loop and
    /// its decision; the gate only removes entities nothing has touched.
    /// <para>📐 Real nodes spent 5–7 ms/frame in egress at ~400 entities to find ~20 changed samples (§3a).</para>
    /// <para><b>Visit</b> when ANY holds (each a version the world already keeps, compared with the version at the previous scan):</para>
    /// <list type="bullet">
    ///   <item>a FULL scan this frame — the first run, another world, the clock went back (playback), <see cref="EgressSignals.AllDirty"/>
    ///         since (H3), or a write to <see cref="NetworkAuthority"/> / <see cref="DescriptorOwnership"/> / <see cref="PartMetadata"/> anywhere
    ///         (H1: authority arrives — also for a part whose ROOT changed);</item>
    ///   <item>its structural clock moved (<c>LastChangeTick</c>: created, a component added or REMOVED, lifecycle);</item>
    ///   <item>the chunk of any declared input changed (H4, H6 — the entity's own chunk; <see cref="NetworkIdentity"/> always an input);</item>
    ///   <item><see cref="SmartEgressUtil.MarkDirty"/> named it (H8);</item>
    ///   <item>it is in this frame's sweep slice — every entity at least once per <see cref="SweepPeriod"/> frames, the bound for a change no
    ///         signal names (H5: a managed component mutated in place; an input the translator forgot to declare).</item>
    /// </list>
    /// <para>⛔ Not for a translator with a HEARTBEAT (GeoSpatial, NavigationStatus — H2) or one that writes its own input each visit (H7).
    /// <see cref="Enabled"/> = false restores the full scan everywhere. ⭐ No allocation per frame.</para>
    /// </summary>
    public sealed unsafe class EgressVisitGate
    {
        /// <summary>Off ⇒ every translator scans everything (today's behaviour): the A/B switch and the kill switch.</summary>
        public static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("HROT_EGRESS_GATE") != "0";   // HROT_EGRESS_GATE=0 ⇒ off (A/B)

        /// <summary>The longest an untouched entity goes unvisited, in frames.</summary>
        public static int SweepPeriod { get; set; } = 8;

        private readonly Type[] _inputs;
        private readonly IComponentTable?[] _tables;
        // ⭐ per unmanaged input: the entity's bytes as last seen (index × size) — a chunk version moves for ANY write in a 64 KB chunk, so
        //   for a small component one write marks thousands of entities; the bytes tell which one really changed. Managed inputs: chunk only.
        private readonly int[] _size;          // 0 ⇒ managed (chunk-level only)
        private readonly int[] _typeId;
        private byte[][] _seen;
        private IComponentTable? _authority, _ownership, _parts;
        private EntityRepository? _repo;
        private EntityIndex? _index;
        private EgressSignals.State? _signals;
        private uint _lastScan;        // GlobalVersion at the previous Begin
        private uint _since;           // what this frame compares against
        private bool _scanned, _all;
        private int _frame;

        public long FullScans { get; private set; }
        public long Visited { get; private set; }
        public long Skipped { get; private set; }

        /// <param name="inputs">every component type the translator READS for its sample (unmanaged or managed); <see cref="NetworkIdentity"/> is added.</param>
        public EgressVisitGate(params Type[] inputs)
        {
            _inputs = new Type[inputs.Length + 1];
            Array.Copy(inputs, _inputs, inputs.Length);
            _inputs[inputs.Length] = typeof(NetworkIdentity);
            _tables = new IComponentTable?[_inputs.Length];
            _size = new int[_inputs.Length];
            _typeId = new int[_inputs.Length];
            _seen = new byte[_inputs.Length][];
            for (int k = 0; k < _seen.Length; k++) _seen[k] = Array.Empty<byte>();
        }

        private static bool IsManaged(IComponentTable t)
        {
            var type = t.GetType();
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ManagedComponentTable<>);
        }

        /// <summary>Call once at the top of ScanAndPublish, before the loop.</summary>
        public void Begin(ISimulationView view)
        {
            _frame++;
            if (!Enabled || view is not EntityRepository repo) { _all = true; _repo = null; FullScans++; return; }

            if (!ReferenceEquals(repo, _repo))
            {
                _repo = repo; _index = repo.GetEntityIndex(); _signals = EgressSignals.Of(repo);
                Array.Clear(_tables); _authority = _ownership = _parts = null; _scanned = false;
                for (int k = 0; k < _seen.Length; k++) _seen[k] = Array.Empty<byte>();
            }
            for (int i = 0; i < _tables.Length; i++)
                if (_tables[i] == null && repo.TryGetTable(_inputs[i], out var t))   // a table registered later
                {
                    _tables[i] = t;
                    _size[i] = IsManaged(t) ? 0 : t.ComponentSize;
                    _typeId[i] = t.ComponentTypeId;
                }
            int need = _index!.MaxIssuedIndex + 1;
            for (int k = 0; k < _seen.Length; k++)
                if (_size[k] > 0 && _seen[k].Length < need * _size[k])
                    Array.Resize(ref _seen[k], Math.Max(need, _seen[k].Length / Math.Max(1, _size[k]) * 2) * _size[k]);
            if (_authority == null && repo.TryGetTable(typeof(NetworkAuthority), out var a)) _authority = a;
            if (_ownership == null && repo.TryGetTable(typeof(DescriptorOwnership), out var o)) _ownership = o;
            if (_parts == null && repo.TryGetTable(typeof(PartMetadata), out var p)) _parts = p;

            uint now = repo.GlobalVersion;
            uint since = _lastScan;
            bool full = !_scanned || now < since || _signals!.AllDirtyAt >= since
                        || Changed(_authority, since) || Changed(_ownership, since) || Changed(_parts, since);
            _all = full;
            if (full) FullScans++;
            _since = since;
            _lastScan = now;
            _scanned = true;
        }

        // HasChanges is "> since"; the gate asks "≥ since" — a write stamped later in the frame of the previous scan is revisited.
        private static bool Changed(IComponentTable? t, uint since) => t != null && t.HasChanges(since == 0 ? 0 : since - 1);

        /// <summary>True when the translator must look at <paramref name="e"/> this frame.</summary>
        public bool Visit(Entity e)
        {
            int i = e.Index;
            if (_all) { Remember(i); Visited++; return true; }
            uint since = _since;
            bool visit = _index!.GetMetadata(i).LastChangeTick >= since
                         || EgressSignals.DirtyAt(_signals!, i) >= since
                         || SweepPeriod <= 1 || (i + _frame) % SweepPeriod == 0;
            if (!visit)
            {
                var tables = _tables;
                for (int k = 0; k < tables.Length; k++)
                {
                    var t = tables[k];
                    if (t == null || t.GetVersionForEntity(i) < since) continue;
                    if (_size[k] == 0 || !Same(k, i)) { visit = true; break; }   // managed: the chunk is all there is · unmanaged: the bytes moved
                }
            }
            if (!visit) { Skipped++; return false; }
            Remember(i);
            Visited++;
            return true;
        }

        // ⭐ the entity's input bytes equal the copy taken when it was last visited (absent ⇒ "the same": a component coming or going is structural)
        private bool Same(int k, int i)
        {
            if (!_index!.GetComponentMask(i).IsSet(_typeId[k])) return true;
            int size = _size[k];
            var now = new ReadOnlySpan<byte>(_tables[k]!.GetRawPointer(i), size);
            return now.SequenceEqual(new ReadOnlySpan<byte>(_seen[k], i * size, size));
        }

        private void Remember(int i)
        {
            if (_repo == null) return;
            for (int k = 0; k < _tables.Length; k++)
            {
                int size = _size[k];
                if (size == 0 || _tables[k] == null || !_index!.GetComponentMask(i).IsSet(_typeId[k])) continue;
                if ((i + 1) * size > _seen[k].Length) Array.Resize(ref _seen[k], Math.Max((i + 1) * size, _seen[k].Length * 2));
                new ReadOnlySpan<byte>(_tables[k]!.GetRawPointer(i), size).CopyTo(new Span<byte>(_seen[k], i * size, size));
            }
        }
    }
}
