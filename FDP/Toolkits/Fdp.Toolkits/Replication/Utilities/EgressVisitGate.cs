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
    ///         since (H3);</item>
    ///   <item>its AUTHORITY moved (H1) — ⭐ CE-4519: per entity, the three facts <c>AuthorityExtensions.HasAuthority</c> reads: its own
    ///         <see cref="PartMetadata"/>, its ROOT's <see cref="NetworkAuthority"/> bytes and its root's <see cref="DescriptorOwnership"/>
    ///         (an immutable record, replaced whole ⇒ a new reference), compared with the copy taken at its last visit. The claim bits
    ///         (<c>SetAuthority</c>) stamp the structural clock. ⛔ SUPERSEDED: any write to one of the three tables anywhere ⇒ a full scan —
    ///         📐 on the 1 000-man CGF that was 5 205 of 5 281 full scans and 0 skipped;</item>
    ///   <item>its structural clock moved (<c>LastChangeTick</c>: created, a component added or REMOVED, lifecycle);</item>
    ///   <item>the chunk of any declared input changed (H4, H6 — the entity's own chunk; <see cref="NetworkIdentity"/> always an input) AND the
    ///         entity's own value did: its bytes for an unmanaged input (§5a), its object REFERENCE for a managed one (CE-4519 — immutable records,
    ///         replaced whole);</item>
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
        public static bool Enabled { get; set; } = EnvVars.Read(EnvVars.HrotEgressGate) != "0";   // HROT_EGRESS_GATE=0 ⇒ off (A/B)

        /// <summary>The longest an untouched entity goes unvisited, in frames.</summary>
        public static int SweepPeriod { get; set; } = 8;

        private readonly Type[] _inputs;
        private readonly IComponentTable?[] _tables;
        // ⭐ per unmanaged input: the entity's bytes as last seen (index × size) — a chunk version moves for ANY write in a 64 KB chunk, so
        //   for a small component one write marks thousands of entities; the bytes tell which one really changed. Managed inputs: chunk only.
        private readonly int[] _size;          // 0 ⇒ managed (chunk-level only)
        private readonly int[] _typeId;
        private byte[][] _seen;
        // ⭐ CE-4519 — per MANAGED input: the object it held at the entity's last visit. Managed components are immutable records replaced whole
        //   (CLAUDE.md, CE-4046) ⇒ a new reference is a change and the same reference is none. 📐 The chunk alone (16 384 entities) visited every
        //   sensor whenever ONE payload was set: 2.08 M of 4.15 M non-full visits on the 1 000-man CGF. In-place mutation stays the sweep's (H5).
        private object?[][] _seenRef;
        private IComponentTable? _authority, _ownership, _parts;
        // ⭐ CE-4519 — per entity index, its authority facts at its last visit: the root, the root's record, the root's NetworkAuthority bytes
        private struct AuthMark { public Entity Root; public object? Record; public bool HasNa; }
        private AuthMark[] _mark = Array.Empty<AuthMark>();
        private byte[] _markNa = Array.Empty<byte>();
        private bool _authMoved;       // one of the three tables moved since the last scan: only then are the marks compared
        private EntityRepository? _repo;
        private EntityIndex? _index;
        private EgressSignals.State? _signals;
        private uint _lastScan;        // GlobalVersion at the previous Begin
        private uint _since;           // what this frame compares against
        private bool _scanned, _all;
        private int _frame;

        // ⭐ diagnostics: process-wide totals, printed every ~10 s when HROT_EGRESS_GATE_STATS=1 (why a full scan happened, how much was skipped)
        private static readonly bool s_stats = EnvVars.Read(EnvVars.HrotEgressGateStats) == "1";
        private static long s_begins, s_full, s_visited, s_skipped, s_fullAuth, s_fullDirty, s_fullClock, s_fullNetAuth, s_fullOwn, s_fullParts;
        // ⭐ CE-4519 — WHY a non-full visit happened (stats only): the structural clock, a MarkDirty, the sweep, a managed input's chunk, an unmanaged input's bytes
        private static long s_whyStruct, s_whyDirty, s_whySweep, s_whyManaged, s_whyBytes, s_whyAuth, s_authMoved;
        private static long s_nextPrint = System.Diagnostics.Stopwatch.GetTimestamp();

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
            _seenRef = new object?[_inputs.Length][];
            for (int k = 0; k < _seenRef.Length; k++) _seenRef[k] = Array.Empty<object?>();
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
                for (int k = 0; k < _seenRef.Length; k++) _seenRef[k] = Array.Empty<object?>();
            }
            for (int i = 0; i < _tables.Length; i++)
                if (_tables[i] == null && repo.TryGetTable(_inputs[i], out var t))   // a table registered later
                {
                    _tables[i] = t;
                    _size[i] = IsManaged(t) ? 0 : t.ComponentSize;
                    _typeId[i] = t.ComponentTypeId;
                }
            int need = _index!.MaxIssuedIndex + 1;
            for (int k = 0; k < _seenRef.Length; k++)
                if (_size[k] == 0 && _tables[k] != null && _seenRef[k].Length < need)
                    Array.Resize(ref _seenRef[k], Math.Max(need, _seenRef[k].Length * 2));
            for (int k = 0; k < _seen.Length; k++)
                if (_size[k] > 0 && _seen[k].Length < need * _size[k])
                    Array.Resize(ref _seen[k], Math.Max(need, _seen[k].Length / Math.Max(1, _size[k]) * 2) * _size[k]);
            if (_authority == null && repo.TryGetTable(typeof(NetworkAuthority), out var a)) _authority = a;
            if (_ownership == null && repo.TryGetTable(typeof(DescriptorOwnership), out var o)) _ownership = o;
            if (_parts == null && repo.TryGetTable(typeof(PartMetadata), out var p)) _parts = p;

            if (_mark.Length < need) Array.Resize(ref _mark, Math.Max(need, _mark.Length * 2));
            int naSize = _authority?.ComponentSize ?? 0;
            if (_markNa.Length < need * naSize) Array.Resize(ref _markNa, Math.Max(need, _markNa.Length / Math.Max(1, naSize) * 2) * naSize);

            uint now = repo.GlobalVersion;
            uint since = _lastScan;
            bool full = !_scanned || now < since || _signals!.AllDirtyAt >= since;
            _authMoved = Changed(_authority, since) || Changed(_ownership, since) || Changed(_parts, since);
            _all = full;
            if (full) FullScans++;
            if (s_stats)
            {
                bool ca = Changed(_authority, since), co = Changed(_ownership, since), cp = Changed(_parts, since);
                if (ca) s_fullNetAuth++; if (co) s_fullOwn++; if (cp) s_fullParts++;
                if (_authMoved) s_authMoved++;
                Stats(full, !_scanned || now < since, _signals!.AllDirtyAt >= since, false);
            }
            _since = since;
            _lastScan = now;
            _scanned = true;
        }

        private void Stats(bool full, bool clockOrFirst, bool dirty, bool auth)
        {
            s_begins++;
            if (full) { s_full++; if (auth) s_fullAuth++; if (dirty) s_fullDirty++; if (clockOrFirst) s_fullClock++; }
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (now < s_nextPrint) return;
            s_nextPrint = now + System.Diagnostics.Stopwatch.Frequency * 10;
            Console.WriteLine($"[EgressGate] begins {s_begins} full {s_full} (all-dirty {s_fullDirty}, first/clock {s_fullClock}) · authority tables moved {s_authMoved} [NetworkAuthority {s_fullNetAuth}, DescriptorOwnership {s_fullOwn}, PartMetadata {s_fullParts}] · visited {s_visited} skipped {s_skipped}"
                + $" · non-full visits by reason: structural {s_whyStruct} · dirty {s_whyDirty} · sweep {s_whySweep} · managed input {s_whyManaged} · input bytes {s_whyBytes} · authority {s_whyAuth}");
        }

        // HasChanges is "> since"; the gate asks "≥ since" — a write stamped later in the frame of the previous scan is revisited.
        private static bool Changed(IComponentTable? t, uint since) => t != null && t.HasChanges(since == 0 ? 0 : since - 1);

        /// <summary>True when the translator must look at <paramref name="e"/> this frame.</summary>
        public bool Visit(Entity e)
        {
            int i = e.Index;
            if (_all) { Remember(e); Visited++; if (s_stats) s_visited++; return true; }
            uint since = _since;
            bool visit = _index!.GetMetadata(i).LastChangeTick >= since
                         || EgressSignals.DirtyAt(_signals!, i) >= since
                         || SweepPeriod <= 1 || (i + _frame) % SweepPeriod == 0;
            if (visit && s_stats)
            {
                if (_index!.GetMetadata(i).LastChangeTick >= since) s_whyStruct++;
                else if (EgressSignals.DirtyAt(_signals!, i) >= since) s_whyDirty++;
                else s_whySweep++;
            }
            if (!visit)
            {
                var tables = _tables;
                for (int k = 0; k < tables.Length; k++)
                {
                    var t = tables[k];
                    if (t == null || t.GetVersionForEntity(i) < since) continue;   // ⭐ the chunk is the cheap first filter; then the entity's own value
                    if (_size[k] == 0 ? !ReferenceEquals(t.GetRawObject(i), _seenRef[k][i]) : !Same(k, i))
                    {
                        visit = true;   // managed: the chunk is all there is · unmanaged: the bytes moved
                        if (s_stats) { if (_size[k] == 0) s_whyManaged++; else s_whyBytes++; }
                        break;
                    }
                }
            }
            if (!visit && _authMoved && AuthorityMoved(e)) { visit = true; if (s_stats) s_whyAuth++; }
            if (!visit) { Skipped++; if (s_stats) s_skipped++; return false; }
            Remember(e);
            Visited++;
            if (s_stats) s_visited++;
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

        // ⭐ CE-4519 — the root HasAuthority resolves to (one level: a part's PartMetadata.ParentEntity, else the entity itself)
        private Entity RootOf(Entity e)
        {
            if (_parts != null && _index!.GetComponentMask(e.Index).IsSet(_parts.ComponentTypeId))
                return ((PartMetadata*)_parts.GetRawPointer(e.Index))->ParentEntity;
            return e;
        }

        private bool AuthorityMoved(Entity e)
        {
            ref readonly var was = ref _mark[e.Index];
            var root = RootOf(e);
            if (root != was.Root) return true;
            if (!_repo!.IsAlive(root)) return false;   // a dead root answers "no authority" — as at the last visit, the root unchanged
            object? record = _ownership != null && _repo.HasManagedComponent<DescriptorOwnership>(root) ? _ownership.GetRawObject(root.Index) : null;
            if (!ReferenceEquals(record, was.Record)) return true;
            bool hasNa = _authority != null && _index!.GetComponentMask(root.Index).IsSet(_authority.ComponentTypeId);
            if (hasNa != was.HasNa) return true;
            if (!hasNa) return false;
            int size = _authority!.ComponentSize;
            return !new ReadOnlySpan<byte>(_authority.GetRawPointer(root.Index), size).SequenceEqual(new ReadOnlySpan<byte>(_markNa, e.Index * size, size));
        }

        private void Remember(Entity e)
        {
            if (_repo == null) return;
            int i = e.Index;
            var root = RootOf(e);
            ref var mark = ref _mark[i];
            mark.Root = root;
            bool alive = _repo.IsAlive(root);
            mark.Record = alive && _ownership != null && _repo.HasManagedComponent<DescriptorOwnership>(root) ? _ownership.GetRawObject(root.Index) : null;
            mark.HasNa = alive && _authority != null && _index!.GetComponentMask(root.Index).IsSet(_authority.ComponentTypeId);
            if (mark.HasNa)
            {
                int size = _authority!.ComponentSize;
                new ReadOnlySpan<byte>(_authority.GetRawPointer(root.Index), size).CopyTo(new Span<byte>(_markNa, i * size, size));
            }
            for (int k = 0; k < _tables.Length; k++)
            {
                int size = _size[k];
                if (size == 0) { if (_tables[k] != null) _seenRef[k][i] = _tables[k]!.GetRawObject(i); continue; }   // CE-4519 — the reference
                if (_tables[k] == null || !_index!.GetComponentMask(i).IsSet(_typeId[k])) continue;
                if ((i + 1) * size > _seen[k].Length) Array.Resize(ref _seen[k], Math.Max((i + 1) * size, _seen[k].Length * 2));
                new ReadOnlySpan<byte>(_tables[k]!.GetRawPointer(i), size).CopyTo(new Span<byte>(_seen[k], i * size, size));
            }
        }
    }
}
