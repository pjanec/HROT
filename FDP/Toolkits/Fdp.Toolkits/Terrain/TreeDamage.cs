using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐⭐ <c>CE-4603</c> (B-6; 📄 docs/DESIGN_Vehicle_Breaching.md §7.7, D-6 R-4241) — the lone trunks ONE deciding node felled in ONE ground cell: each trunk by its
    /// centimetre position (a trunk never moves, so its position IS its stable key — a piece index renumbers when the overlay re-sorts). Unmanaged, recorded, replicated
    /// (<c>dtTreeDamage</c>), read by every node through <see cref="FelledTrees"/> on <see cref="DoorStates.Trees"/>. The <see cref="GroundDamage"/> shape.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.TreeDamage)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct TreeDamage : ICellRecord<(int X, int Y)>
    {
        int ICellRecord<(int X, int Y)>.CellX { get => CellX; set => CellX = value; }
        int ICellRecord<(int X, int Y)>.CellY { get => CellY; set => CellY = value; }
        readonly int ICellRecord<(int X, int Y)>.ItemCount => Count;
        readonly bool ICellRecord<(int X, int Y)>.IsFull => Count >= Capacity;
        readonly (int X, int Y) ICellRecord<(int X, int Y)>.Item(int i) => Get(i);
        bool ICellRecord<(int X, int Y)>.TryAdd(in (int X, int Y) item, out bool full) => TreeDamages.Add(ref this, item, out full);

        public const int Capacity = 16;
        public int CellX;
        public int CellY;
        public byte Count;
        /// <summary>Each felled trunk's centre, centimetres.</summary>
        public fixed int X[Capacity];
        public fixed int Y[Capacity];

        public readonly (int X, int Y) Get(int i)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            return (X[i], Y[i]);
        }

        public void Set(int i, (int X, int Y) key)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            X[i] = key.X; Y[i] = key.Y;
        }
    }

    /// <summary>⭐ <c>CE-4603</c> — the rules of a <see cref="TreeDamage"/>: a trunk's key, its cell, adding one.</summary>
    public static class TreeDamages
    {
        /// <summary>⭐ The TKB type of a tree-damage entity (bare).</summary>
        public const long TkbType = 7205;

        /// <summary>A trunk's key: its box centre in centimetres.</summary>
        public static (int X, int Y) KeyOf(TerrainPrism trunk)
            => ((int)MathF.Round((trunk.Min.X + trunk.Max.X) * 50f), (int)MathF.Round((trunk.Min.Y + trunk.Max.Y) * 50f));

        public static (int X, int Y) CellOf((int X, int Y) key)
            => ((int)MathF.Floor(key.X / (GroundDamage.CellSize * 100f)), (int)MathF.Floor(key.Y / (GroundDamage.CellSize * 100f)));

        public static bool Add(ref TreeDamage d, (int X, int Y) key, out bool full)
        {
            full = false;
            for (int i = 0; i < d.Count; i++) if (d.Get(i) == key) return false;
            if (d.Count >= TreeDamage.Capacity) { full = true; return false; }
            d.Set(d.Count, key); d.Count++;
            return true;
        }
    }

    /// <summary>⭐ <c>CE-4603</c> — "this trunk was felled": published by the deciding node (a blast, a player's hull), read by <see cref="TreeDamageSystem"/> the frame after. LOCAL.</summary>
    [EventId(TerrainEventIds.TreeFell)]
    [StructLayout(LayoutKind.Sequential)]
    public struct TreeFellEvent
    {
        public int X;
        public int Y;
        public Entity By;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4603</c> — the felled trunks AS ONE VIEW SEES THEM (the union of every <see cref="TreeDamage"/> entity): sight, fire and the hull skip a felled trunk at
    /// QUERY time (R-4232's rule; the bake only removes it from the navmesh and the drawing). Rides on <see cref="DoorStates.Trees"/>; built when the records change.
    /// </summary>
    public sealed class FelledTrees
    {
        private readonly HashSet<long> _keys;
        private readonly (int X, int Y)[] _sorted;
        private FelledTrees((int X, int Y)[] sorted)
        {
            _sorted = sorted;
            _keys = new HashSet<long>(sorted.Length);
            ulong h = 1469598103934665603UL;
            foreach (var k in sorted) { _keys.Add(Key(k)); h ^= (ulong)Key(k); h *= 1099511628211UL; }
            Hash = h == 0 ? 1 : h;
        }

        private static long Key((int X, int Y) k) => ((long)k.X << 32) | (uint)k.Y;

        public int Count => _sorted.Length;
        public (int X, int Y) this[int i] => _sorted[i];
        /// <summary>A hash of the felled set (never 0): the bake's input signature.</summary>
        public ulong Hash { get; }

        /// <summary>True when <paramref name="prism"/> is a lone trunk this view has felled. ⛔ No allocation.</summary>
        public bool Felled(TerrainPrism prism) => prism.Kind == TerrainPrismKind.Tree && _keys.Contains(Key(TreeDamages.KeyOf(prism)));

        public bool Contains((int X, int Y) key) => _keys.Contains(Key(key));

        public static FelledTrees? Of(ISimulationView view)
        {
            if (view is not EntityRepository repo)
            {
                var q = view.Query().With<TreeDamage>().WithLifecycle(EntityLifecycle.All).Build();
                var scratch = new List<(int, int)>();
                foreach (var e in q) Collect(view.GetComponentRO<TreeDamage>(e), scratch);
                return Make(scratch);
            }
            if (!repo.IsComponentTypeRegistered<TreeDamage>()) return null;
            if (ViewTableCache<TreeDamage>.Holds(repo)) return t_last;   // ⭐ CE-5000 — the one stamp cache
            var list = t_scratch ??= new List<(int, int)>(32);
            list.Clear();
            foreach (var e in ViewTableCache<TreeDamage>.Begin(repo))
            {
                Collect(repo.GetComponentRO<TreeDamage>(e), list);
                ViewTableCache<TreeDamage>.Read(e);
            }
            ViewTableCache<TreeDamage>.Taken(repo);
            list.Sort();
            for (int i = list.Count - 1; i > 0; i--) if (list[i] == list[i - 1]) list.RemoveAt(i);
            if (t_last is { } last && last.Same(list)) return last;
            if (list.Count == 0) return t_last = null;
            return t_last = new FelledTrees(list.ToArray());
        }

        private bool Same(List<(int, int)> sorted)
        {
            if (sorted.Count != _sorted.Length) return false;
            for (int i = 0; i < _sorted.Length; i++) if (_sorted[i] != sorted[i]) return false;
            return true;
        }

        private static FelledTrees? Make(List<(int, int)> list)
        {
            if (list.Count == 0) return null;
            list.Sort();
            for (int i = list.Count - 1; i > 0; i--) if (list[i] == list[i - 1]) list.RemoveAt(i);
            return new FelledTrees(list.ToArray());
        }

        private static void Collect(in TreeDamage d, List<(int, int)> into)
        {
            int n = Math.Min((int)d.Count, TreeDamage.Capacity);
            for (int i = 0; i < n; i++) into.Add(d.Get(i));
        }

        [ThreadStatic] private static FelledTrees? t_last;
        [ThreadStatic] private static List<(int, int)>? t_scratch;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4603</c> (B-6) — turns <see cref="TreeFellEvent"/>s into <see cref="TreeDamage"/> entities: THE cell-record writer (<see cref="CellRecordSystem{TRecord, TItem}"/>,
    /// CE-5000) with the felling event and a trunk's cell. ⛔ Never in a replay; writes only entities this node owns.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class TreeDamageSystem : CellRecordSystem<TreeDamage, (int X, int Y)>
    {
        /// <summary>The wire ordinal of <c>EDescriptorType.dtTreeDamage</c> (NED). ⚠ A rail pins the two equal.</summary>
        public const long DescriptorOrdinal = 185;
        public static readonly long OwnershipKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

        protected override long DescriptorKey => OwnershipKey;
        protected override long TkbType => TreeDamages.TkbType;
        protected override (int X, int Y) CellOf(in (int X, int Y) item) => TreeDamages.CellOf(item);

        protected override void ReadItems(EntityRepository repo, List<(int X, int Y)> into)
        {
            if (!repo.Bus.IsRegistered<TreeFellEvent>()) return;
            var events = repo.ReadEvents<TreeFellEvent>();
            for (int i = 0; i < events.Length; i++) into.Add((events[i].X, events[i].Y));
        }

        public static bool OwnsDamage(EntityRepository repo, Entity e) => OwnsRecord(repo, e, OwnershipKey);
    }

    /// <summary>⭐ <c>CE-4603</c> — <c>trees.json</c>: which lone trunks a blast fells.</summary>
    public sealed class TreeFellTable
    {
        public float MinKg { get; init; } = 2f;
        public float FellPerCbrtKg { get; init; } = 1.2f;

        private static readonly Lazy<TreeFellTable> s_shared = new(() =>
        {
            using var s = typeof(TreeFellTable).Assembly.GetManifestResourceStream("Fdp.Toolkit.Terrain.trees.json") ?? throw new InvalidOperationException("trees.json is not embedded");
            using var r = new System.IO.StreamReader(s);
            using var doc = System.Text.Json.JsonDocument.Parse(r.ReadToEnd());
            return new TreeFellTable { MinKg = doc.RootElement.GetProperty("minKg").GetSingle(), FellPerCbrtKg = doc.RootElement.GetProperty("fellPerCbrtKg").GetSingle() };
        });
        public static TreeFellTable Shared => s_shared.Value;

        /// <summary>The radius a blast of <paramref name="kg"/> fells lone trunks in, 0 below the threshold.</summary>
        public float Radius(float kg) => kg >= MinKg ? FellPerCbrtKg * MathF.Cbrt(kg) : 0f;
    }

    /// <summary>
    /// ⭐ <c>CE-4603</c> — the LONE tree trunks of a world, indexed by a 16 m grid (built once per world): "which trunks stand near a point" for a blast and the
    /// bulldozer's resolver, never a scan of every piece.
    /// </summary>
    public static class LoneTrees
    {
        private const float Cell = 16f;
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TerrainWorld, Dictionary<long, int[]>> s_index = new();

        private static long Key(int x, int y) => ((long)x << 32) | (uint)y;

        /// <summary>Appends to <paramref name="into"/> every lone trunk (a prism index) whose centre lies within <paramref name="radius"/> of <paramref name="at"/>.</summary>
        public static void Near(TerrainWorld world, Vector2 at, float radius, List<int> into)
        {
            var index = s_index.GetValue(world, Build);
            int x0 = (int)MathF.Floor((at.X - radius) / Cell), x1 = (int)MathF.Floor((at.X + radius) / Cell);
            int y0 = (int)MathF.Floor((at.Y - radius) / Cell), y1 = (int)MathF.Floor((at.Y + radius) / Cell);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (index.TryGetValue(Key(x, y), out var list))
                        foreach (int i in list)
                        {
                            var p = world.Prisms[i];
                            if (Vector2.Distance((p.Min + p.Max) * 0.5f, at) <= radius) into.Add(i);
                        }
        }

        private static Dictionary<long, int[]> Build(TerrainWorld world)
        {
            var lists = new Dictionary<long, List<int>>();
            for (int i = 0; i < world.Prisms.Count; i++)
            {
                var p = world.Prisms[i];
                if (p.Kind != TerrainPrismKind.Tree) continue;
                var c = (p.Min + p.Max) * 0.5f;
                long k = Key((int)MathF.Floor(c.X / Cell), (int)MathF.Floor(c.Y / Cell));
                if (!lists.TryGetValue(k, out var l)) lists[k] = l = new List<int>(4);
                l.Add(i);
            }
            var d = new Dictionary<long, int[]>(lists.Count);
            foreach (var (k, l) in lists) d[k] = l.ToArray();
            return d;
        }
    }
}
