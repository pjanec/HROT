using System;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-5033</c> GW-2 — one damaged window of <see cref="WindowDamage"/>: the window (its panel — GLOBAL here, within-building on the wire — and
    /// its opening on that panel), its hits (1, 2 cracked; <see cref="WindowDamages.BrokenHits"/> = broken) and up to two cracks, each at (u, v) on the
    /// pane (0 = its sill / its A end, 255 = its head / its far end).
    /// </summary>
    public readonly record struct WindowEntry(int Panel, byte Opening, byte Hits, byte U0, byte V0, byte U1, byte V1)
    {
        public bool Broken => Hits >= WindowDamages.BrokenHits;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-5033</c> GW-2 (📄 docs/DESIGN_Glass_Windows.md D-3, D-4, D-6, D-9) — the damaged WINDOWS of one building, on the SAME runtime entity as its
    /// <see cref="BuildingDamage"/> (the same key, owner, save and lifecycle): only windows that were hit are listed; an unlisted glazed window is intact.
    /// Replicated as <c>dtWindowDamage</c>; unmanaged ⇒ recorded by bytes. Read on every node through <see cref="BuildingDamageTable"/>
    /// (<see cref="BuildingDamageTable.PaneBroken"/>, the cracks); written only by <see cref="BuildingDamageSystem"/> on the deciding node (SimHost).
    /// <para>⭐ Its own component so a glass hit never re-sends the breaches, and is never in the navmesh's damage hash (D-7: no rebake).</para>
    /// <para>⛔ <c>NoScenario</c>: saved through the terrain-object section (the building's <c>"windows"</c>).</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.WindowDamage)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct WindowDamage
    {
        /// <summary>How many damaged windows one entity holds (D-9).</summary>
        public const int Capacity = 64;

        /// <summary>The building's index in <see cref="TerrainWorld.Buildings"/> (the same as its <see cref="BuildingDamage.Building"/>).</summary>
        public int Building;
        /// <summary>Windows in use, in the order they were first hit (the oldest first — D-9 evicts from the front).</summary>
        public byte Count;

        public fixed int Panel[Capacity];
        public fixed byte Opening[Capacity];
        public fixed byte Hits[Capacity];
        public fixed byte U0[Capacity];
        public fixed byte V0[Capacity];
        public fixed byte U1[Capacity];
        public fixed byte V1[Capacity];
        /// <summary>⭐ <c>CE-11051</c> — per window: the sim time (s, <c>SimClock.TotalTime</c>) of its LAST hit (a crack or the break); 0 = loaded from a save (no live
        /// change). It moves with its entry when an older one is evicted (D-9). <see cref="HitAtOf"/> types it.</summary>
        public fixed double HitAt[Capacity];

        /// <summary>Overwrites window slot <paramref name="i"/>'s last-hit time (a reader outside an unsafe context: the wire's ingress).</summary>
        public void SetHitAt(int i, double at)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            HitAt[i] = at;
        }

        /// <summary>When window slot <paramref name="i"/> was last hit (0 = from a save).</summary>
        public readonly SimInstant HitAtOf(int i)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            return (SimInstant)HitAt[i];
        }

        public readonly WindowEntry Get(int i)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            return new WindowEntry(Panel[i], Opening[i], Hits[i], U0[i], V0[i], U1[i], V1[i]);
        }

        public void Set(int i, in WindowEntry w)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            Panel[i] = w.Panel; Opening[i] = w.Opening; Hits[i] = w.Hits; U0[i] = w.U0; V0[i] = w.V0; U1[i] = w.U1; V1[i] = w.V1;
        }

        /// <summary>The slot of window (<paramref name="panel"/>, <paramref name="opening"/>), or −1.</summary>
        public readonly int IndexOf(int panel, int opening)
        {
            for (int i = 0; i < Count; i++) if (Panel[i] == panel && Opening[i] == opening) return i;
            return -1;
        }

        /// <summary>The entries as a fresh array (a test / diagnostics read; ⛔ never per frame).</summary>
        public readonly WindowEntry[] ToArray()
        {
            var a = new WindowEntry[Count];
            for (int i = 0; i < a.Length; i++) a[i] = Get(i);
            return a;
        }
    }

    /// <summary>The outcome of <see cref="WindowDamages.Hit"/> / <see cref="WindowDamages.Break"/>.</summary>
    public enum WindowChange : byte
    {
        /// <summary>Nothing written (already broken).</summary>
        Unchanged = 0,
        /// <summary>One more crack.</summary>
        Cracked = 1,
        /// <summary>The pane turned BROKEN now.</summary>
        Broke = 2,
        /// <summary>The record is full of broken windows: not recorded (D-9).</summary>
        Full = 3,
    }

    /// <summary>⭐ <c>CE-5033</c> GW-2 — the one writer-side rule for a <see cref="WindowDamage"/> (D-4, D-9). Allocation-free.</summary>
    public static unsafe class WindowDamages
    {
        /// <summary>The hit count that means BROKEN: the third round (D-4).</summary>
        public const byte BrokenHits = 3;

        /// <summary>A pane coordinate (0..1) as a byte.</summary>
        public static byte Quantize(float t) => (byte)Math.Clamp((int)MathF.Round(Math.Clamp(t, 0f, 1f) * 255f), 0, 255);

        /// <summary>
        /// A round crossed the pane of (<paramref name="panel"/>, <paramref name="opening"/>) at pane coordinates (<paramref name="u"/>, <paramref name="v"/>):
        /// one more hit with its crack kept; the third breaks it (its cracks dropped). A new window past capacity evicts the OLDEST cracked-not-broken entry;
        /// a record full of broken windows records nothing (<see cref="WindowChange.Full"/>).
        /// </summary>
        public static WindowChange Hit(ref WindowDamage d, int panel, int opening, float u, float v) => Hit(ref d, panel, opening, u, v, 0d);

        /// <summary>⭐ <c>CE-11051</c> — <see cref="Hit(ref WindowDamage, int, int, float, float)"/> stamping the window's last hit at <paramref name="at"/> (sim seconds).</summary>
        public static WindowChange Hit(ref WindowDamage d, int panel, int opening, float u, float v, double at)
        {
            int i = d.IndexOf(panel, opening);
            if (i >= 0)
            {
                var w = d.Get(i);
                if (w.Broken) return WindowChange.Unchanged;
                byte hits = (byte)(w.Hits + 1);
                d.HitAt[i] = at;
                if (hits >= BrokenHits) { d.Set(i, new WindowEntry(panel, (byte)opening, BrokenHits, 0, 0, 0, 0)); return WindowChange.Broke; }
                d.Set(i, w with { Hits = hits, U1 = Quantize(u), V1 = Quantize(v) });
                return WindowChange.Cracked;
            }
            if (!MakeRoom(ref d)) return WindowChange.Full;
            d.Set(d.Count, new WindowEntry(panel, (byte)opening, 1, Quantize(u), Quantize(v), 0, 0));
            d.HitAt[d.Count] = at;
            d.Count++;
            return WindowChange.Cracked;
        }

        /// <summary>The pane of (<paramref name="panel"/>, <paramref name="opening"/>) breaks at once — a man firing out, a blast, a breach over it (D-4, D-10).</summary>
        public static WindowChange Break(ref WindowDamage d, int panel, int opening) => Break(ref d, panel, opening, 0d);

        /// <summary>⭐ <c>CE-11051</c> — <see cref="Break(ref WindowDamage, int, int)"/> stamping the break at <paramref name="at"/> (sim seconds).</summary>
        public static WindowChange Break(ref WindowDamage d, int panel, int opening, double at)
        {
            int i = d.IndexOf(panel, opening);
            if (i >= 0)
            {
                if (d.Get(i).Broken) return WindowChange.Unchanged;
                d.Set(i, new WindowEntry(panel, (byte)opening, BrokenHits, 0, 0, 0, 0));
                d.HitAt[i] = at;
                return WindowChange.Broke;
            }
            if (!MakeRoom(ref d)) return WindowChange.Full;
            d.Set(d.Count, new WindowEntry(panel, (byte)opening, BrokenHits, 0, 0, 0, 0));
            d.HitAt[d.Count] = at;
            d.Count++;
            return WindowChange.Broke;
        }

        /// <summary>D-9 — room for one more entry: when full, the oldest cracked-not-broken entry is dropped (the rest keep their order).</summary>
        private static bool MakeRoom(ref WindowDamage d)
        {
            if (d.Count < WindowDamage.Capacity) return true;
            for (int i = 0; i < d.Count; i++)
            {
                if (d.Get(i).Broken) continue;
                for (int k = i + 1; k < d.Count; k++) { d.Set(k - 1, d.Get(k)); d.HitAt[k - 1] = d.HitAt[k]; }   // ⭐ CE-11051 — the time moves with its entry
                d.Count--;
                d.HitAt[d.Count] = 0d;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The pane coordinates (u along the opening from its start, v up from its sill, both 0..1) of the point <paramref name="at"/> on the window — a crack's position.
        /// </summary>
        public static (float U, float V) PaneUv(TerrainWorld world, int panel, int opening, System.Numerics.Vector3 at)
        {
            var p = world.Panels[panel];
            var o = p.Openings[opening];
            var dir = p.B - p.A;
            float len = dir.Length();
            float along = len > 0f ? System.Numerics.Vector2.Dot(new System.Numerics.Vector2(at.X, at.Y) - p.A, dir / len) : 0f;
            float u = o.Width > 0f ? (along - o.At) / o.Width : 0f;
            float v = o.HeadZ > o.SillZ ? (at.Z - o.SillZ) / (o.HeadZ - o.SillZ) : 0f;
            return (Math.Clamp(u, 0f, 1f), Math.Clamp(v, 0f, 1f));
        }

        /// <summary>
        /// ⭐ CE-4427 R-4 (the <see cref="BuildingDamages.Remap"/> shape) — re-keys <paramref name="d"/> from world <paramref name="from"/> to <paramref name="to"/>: the
        /// building by its key, each window's panel by its within-building rank (its opening index is the panel's own). A window the new world no longer has is
        /// dropped. True when <paramref name="d"/> changed.
        /// </summary>
        public static bool Remap(ref WindowDamage d, TerrainWorld from, TerrainWorld to, string? key = null)
        {
            if (ReferenceEquals(from, to)) return false;
            int nb = -1;
            if (!string.IsNullOrEmpty(key)) nb = BuildingDamages.IndexOfKey(to, key);
            else if ((uint)d.Building < (uint)WallGroups.Count(from)) nb = BuildingDamages.IndexOfKey(to, BuildingDamages.KeyOf(from, d.Building));
            var n = default(WindowDamage);
            n.Building = nb;
            if (nb >= 0)
            {
                int k = 0;
                for (int i = 0; i < d.Count; i++)
                {
                    var w = d.Get(i);
                    if ((uint)w.Panel >= (uint)from.Panels.Count) continue;
                    int panel = BuildingPanelIndex.ToGlobal(to, nb, BuildingPanelIndex.ToLocal(from, w.Panel));
                    if (panel < 0 || w.Opening >= to.Panels[panel].Openings.Count || !to.Panels[panel].Openings[w.Opening].Glazed) continue;
                    n.HitAt[k] = d.HitAt[i];   // ⭐ CE-11051
                    n.Set(k++, w with { Panel = panel });
                }
                n.Count = (byte)k;
            }
            bool changed = n.Building != d.Building || n.Count != d.Count;
            for (int i = 0; !changed && i < n.Count; i++) changed = n.Get(i) != d.Get(i) || n.HitAt[i] != d.HitAt[i];
            if (changed) d = n;
            return changed;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-5033</c> GW-2 (D-4, D-10) — "this window's pane was hit / must break": published on the deciding node (SimHost) by the round's flight
    /// (<c>BallisticsSystem</c>), a burst (<c>AreaEffectSystem</c>) or a breach over the window (<see cref="BuildingDamageSystem"/> itself), read by
    /// <see cref="BuildingDamageSystem"/>. LOCAL (never on the wire): the <see cref="WindowDamage"/> it writes is what replicates.
    /// </summary>
    [EventId(TerrainEventIds.WindowHit)]
    [StructLayout(LayoutKind.Sequential)]
    public struct WindowHitEvent
    {
        public int Building;
        /// <summary>The GLOBAL panel index.</summary>
        public int Panel;
        public int Opening;
        /// <summary>Where the round crossed the pane (0..1; unused by a shatter).</summary>
        public float U, V;
        /// <summary>1 = break it at once (a man firing out, a blast, a breach); 0 = one hit.</summary>
        public byte Shatter;
        /// <summary>1 = no shatter sound (a breach's own debris is the effect, D-10).</summary>
        public byte Silent;
        /// <summary>Who did it (for the sound's source; <see cref="Entity.Null"/> when unknown).</summary>
        public Entity Source;
    }

    /// <summary>
    /// ⭐ <c>CE-5033</c> GW-2 (D-8) — "a pane shattered here": published by <see cref="BuildingDamageSystem"/> on the deciding node when a window turns broken
    /// (never for a breach's own, D-10), read by the sound emission (AI hearing). LOCAL: every map plays its shard burst from the replicated state instead.
    /// </summary>
    [EventId(TerrainEventIds.WindowBroken)]
    [StructLayout(LayoutKind.Sequential)]
    public struct WindowBrokenEvent
    {
        public int Building;
        public int Panel;
        public int Opening;
        /// <summary>The pane's centre.</summary>
        public float X, Y, Z;
        public Entity Source;
    }
}

namespace Fdp.Toolkit.Terrain
{
    using System.Collections.Generic;
    using Fdp.ModuleHost.Abstractions;

    /// <summary>
    /// ⭐ <c>CE-5033</c> GW-2 (D-6, D-7) — the damaged windows AS ONE VIEW SEES THEM: the union of every <see cref="WindowDamage"/> entity (two deciding nodes
    /// may each hold one for a building: the higher hit count wins, with its cracks). Rides on <see cref="DoorStates.Windows"/> like the felled trees — its own
    /// stamp (<see cref="ViewTableCache{T}"/>), the same instance while nothing changed, ⛔ never part of the breach table (a window is no geometry: no
    /// obstacle re-bake, no navmesh hash, D-7).
    /// </summary>
    public sealed class WindowTable
    {
        private readonly WindowEntry[] _sorted;          // by (Panel, Opening)
        private readonly HashSet<long> _broken;

        private WindowTable(WindowEntry[] sorted)
        {
            _sorted = sorted;
            _broken = new HashSet<long>();
            foreach (var w in sorted) if (w.Broken) _broken.Add(Key(w.Panel, w.Opening));
        }

        private static long Key(int panel, int opening) => ((long)panel << 8) | (uint)(opening & 0xFF);

        public int Count => _sorted.Length;
        public WindowEntry this[int i] => _sorted[i];
        public int BrokenCount => _broken.Count;

        /// <summary>True when the pane of window (<paramref name="panel"/>, <paramref name="opening"/>) is broken in this view. ⛔ No allocation.</summary>
        public bool PaneBroken(int panel, int opening) => _broken.Count > 0 && _broken.Contains(Key(panel, opening));

        /// <summary>The window's entry, or false when it was never hit (intact, no cracks).</summary>
        public bool TryGet(int panel, int opening, out WindowEntry entry)
        {
            int lo = 0, hi = _sorted.Length - 1;
            while (lo <= hi)
            {
                int m = (lo + hi) >> 1;
                var w = _sorted[m];
                int c = w.Panel != panel ? w.Panel.CompareTo(panel) : w.Opening.CompareTo((byte)opening);
                if (c == 0) { entry = w; return true; }
                if (c < 0) lo = m + 1; else hi = m - 1;
            }
            entry = default;
            return false;
        }

        public static WindowTable? Of(ISimulationView view)
        {
            if (view is not EntityRepository repo)
            {
                var q = view.Query().With<WindowDamage>().WithLifecycle(EntityLifecycle.All).Build();
                var scratch = new List<WindowEntry>();
                foreach (var e in q) Collect(view.GetComponentRO<WindowDamage>(e), scratch);
                return Make(scratch);
            }
            if (!repo.IsComponentTypeRegistered<WindowDamage>()) return null;
            if (ViewTableCache<WindowDamage>.Holds(repo)) return t_last;
            var list = t_scratch ??= new List<WindowEntry>(32);
            list.Clear();
            foreach (var e in ViewTableCache<WindowDamage>.Begin(repo))
            {
                Collect(repo.GetComponentRO<WindowDamage>(e), list);
                ViewTableCache<WindowDamage>.Read(e);
            }
            ViewTableCache<WindowDamage>.Taken(repo);
            Normalise(list);
            if (t_last is { } last && last.Same(list)) return last;
            if (list.Count == 0) return t_last = null;
            return t_last = new WindowTable(list.ToArray());
        }

        /// <summary>The table built from <paramref name="view"/>'s components now, bypassing the per-thread cache (a judge or a diagnostic; ⛔ never per frame).</summary>
        public static WindowTable? Fresh(ISimulationView view)
        {
            var q = view.Query().With<WindowDamage>().WithLifecycle(EntityLifecycle.All).Build();
            var scratch = new List<WindowEntry>();
            foreach (var e in q) Collect(view.GetComponentRO<WindowDamage>(e), scratch);
            return Make(scratch);
        }

        private static readonly Comparison<WindowEntry> Order = static (a, b) =>
        {
            int c = a.Panel.CompareTo(b.Panel); if (c != 0) return c;
            c = a.Opening.CompareTo(b.Opening); if (c != 0) return c;
            return b.Hits.CompareTo(a.Hits);   // the most hits first: it wins the merge
        };

        /// <summary>Sorted by window; one entry per window — the one with the most hits (then the first in order, so the result does not depend on entity order).</summary>
        private static void Normalise(List<WindowEntry> list)
        {
            list.Sort((a, b) => { int c = Order(a, b); if (c != 0) return c; return ((long)a.U0 << 24 | (long)a.V0 << 16 | (long)a.U1 << 8 | a.V1).CompareTo((long)b.U0 << 24 | (long)b.V0 << 16 | (long)b.U1 << 8 | b.V1); });
            for (int i = list.Count - 1; i > 0; i--)
                if (list[i].Panel == list[i - 1].Panel && list[i].Opening == list[i - 1].Opening) list.RemoveAt(i);
        }

        private bool Same(List<WindowEntry> sorted)
        {
            if (sorted.Count != _sorted.Length) return false;
            for (int i = 0; i < _sorted.Length; i++) if (_sorted[i] != sorted[i]) return false;
            return true;
        }

        private static WindowTable? Make(List<WindowEntry> list)
        {
            Normalise(list);
            return list.Count == 0 ? null : new WindowTable(list.ToArray());
        }

        private static void Collect(in WindowDamage d, List<WindowEntry> into)
        {
            int n = Math.Min((int)d.Count, WindowDamage.Capacity);
            for (int i = 0; i < n; i++) into.Add(d.Get(i));
        }

        [ThreadStatic] private static WindowTable? t_last;
        [ThreadStatic] private static List<WindowEntry>? t_scratch;
    }
}

namespace Fdp.Toolkit.Terrain
{
    /// <summary>⭐ <c>CE-5033</c> GW-2 (D-4) — <c>glass.json</c>: which panes a burst shatters.</summary>
    public sealed class GlassShatterTable
    {
        public float BlastPerCbrtKg { get; init; } = 6f;
        public float FragmentShatterM { get; init; } = 8f;

        private static readonly Lazy<GlassShatterTable> s_shared = new(() =>
        {
            using var s = typeof(GlassShatterTable).Assembly.GetManifestResourceStream("Fdp.Toolkit.Terrain.glass.json") ?? throw new InvalidOperationException("glass.json is not embedded");
            using var r = new System.IO.StreamReader(s);
            using var doc = System.Text.Json.JsonDocument.Parse(r.ReadToEnd());
            return new GlassShatterTable
            {
                BlastPerCbrtKg = doc.RootElement.GetProperty("blastPerCbrtKg").GetSingle(),
                FragmentShatterM = doc.RootElement.GetProperty("fragmentShatterM").GetSingle(),
            };
        });
        public static GlassShatterTable Shared => s_shared.Value;

        /// <summary>The radius the pressure of a blast of <paramref name="kg"/> shatters panes in (through walls); 0 with no explosive.</summary>
        public float PressureRadius(float kg) => kg > 0f ? BlastPerCbrtKg * MathF.Cbrt(kg) : 0f;
    }
}
