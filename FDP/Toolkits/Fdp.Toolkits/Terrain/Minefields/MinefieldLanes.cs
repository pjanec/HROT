using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>What a mine clearer is working with.</summary>
    public enum MineClearingTool : byte { NoTool = 0, Plough = 1, Flail = 2, LineCharge = 3 }

    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T4 (DESIGN_Minefields.md MF-9, MR-11, MR-15, MR-17) — a mine clearer's "my tool is working": set by its brain's <c>ClearLane</c> order (behaviors),
    /// cleared when the order ends — the bulldozer's <see cref="BladeCut"/> pattern. The brain only drives and marks; the MUSCLE (the serial trigger) clears the swath under an
    /// active plough / flail, or fires the line charge toward <see cref="LineTo"/>. Unmanaged, recorded; Brain group, replicated (<c>dtMineClearing</c>).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.MineClearing)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct MineClearing
    {
        /// <summary>1 while the tool works.</summary>
        public byte Active;
        public MineClearingTool Tool;
        /// <summary>The swath's width (m) — a plough / flail ~4.5 m; a line charge ~8 m.</summary>
        public float WidthM;
        /// <summary>A line charge: where it lands (it clears from the launcher to here, ~100 m).</summary>
        public Vector3 LineTo;
    }

    /// <summary>One cleared lane (the lanes writer's item).</summary>
    public readonly record struct LaneItem(Guid Field, Vector2 A, Vector2 B, float Width, byte SidePlus1);

    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T4 (MR-11, MR-18; lane decision R-5048) — the LANES a deciding node cleared through ONE field at runtime (straight segments, width, the clearing side):
    /// a record owned by that node (the breach-record pattern), never a write to the authored <see cref="Minefield"/> another node owns. Recorded, replicated
    /// (<c>dtMinefieldLanes</c>), saved with the field's strikes. The navmesh opens each lane (a later "open" volume) for the sides that know it: the clearing side.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.MinefieldLanes)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct MinefieldLanes : IKeyedRecord<Guid, LaneItem>
    {
        public const int Capacity = 8;
        /// <summary>⭐ TKB 7208 <c>MinefieldLanesRecord</c> (free on every lane 2026-11-16).</summary>
        public const long TkbType = 7208;

        public Guid Field;
        public ushort Chunk;
        public byte Count;
        public fixed float Ax[Capacity];
        public fixed float Ay[Capacity];
        public fixed float Bx[Capacity];
        public fixed float By[Capacity];
        public fixed float Width[Capacity];
        public fixed byte SidePlus1[Capacity];

        public readonly LaneItem Get(int i)
        {
            if ((uint)i >= Capacity) throw new ArgumentOutOfRangeException(nameof(i));
            return new LaneItem(Field, new Vector2(Ax[i], Ay[i]), new Vector2(Bx[i], By[i]), Width[i], SidePlus1[i]);
        }

        Guid IKeyedRecord<Guid, LaneItem>.RecordKey { readonly get => Field; set => Field = value; }
        public readonly bool IsFull => Count >= Capacity;
        public readonly bool Holds(in LaneItem item)
        {
            for (int i = 0; i < Count; i++)
                if (Vector2.DistanceSquared(new Vector2(Ax[i], Ay[i]), item.A) < 0.01f && Vector2.DistanceSquared(new Vector2(Bx[i], By[i]), item.B) < 0.01f) return true;
            return false;
        }
        public bool TryAdd(in LaneItem item)
        {
            if (Count >= Capacity) return false;
            Ax[Count] = item.A.X; Ay[Count] = item.A.Y; Bx[Count] = item.B.X; By[Count] = item.B.Y; Width[Count] = item.Width; SidePlus1[Count] = item.SidePlus1;
            Count++;
            return true;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T4 — writes every <see cref="LaneCleared"/> with a runtime segment into the field's <see cref="MinefieldLanes"/> on the deciding node, through the ONE keyed
    /// record writer. Serial; never in a replay. (The mines in the swath are recorded <see cref="MineGone.Cleared"/> in <see cref="MinefieldStrikes"/> by the clearing step.)
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class MinefieldLanesSystem : RecordWriterCore<MinefieldLanes, Guid, LaneItem>
    {
        public static readonly long OwnershipKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(Minefields.LanesDescriptorOrdinal, 0);
        protected override long DescriptorKey => OwnershipKey;
        protected override long TkbType => MinefieldLanes.TkbType;
        protected override Guid KeyOf(in LaneItem item) => item.Field;
        protected override MinefieldLanes NewRecord(in Guid key, int existing) => new() { Field = key, Chunk = (ushort)existing };

        protected override void ReadItems(EntityRepository repo, List<LaneItem> into)
        {
            if (!repo.Bus.IsRegistered<LaneCleared>()) return;
            foreach (ref readonly var l in repo.ReadEvents<LaneCleared>())
                if (l.Width > 0f && MinefieldIds.TryGuid(repo, l.Field, out var g))
                    into.Add(new LaneItem(g, new Vector2(l.Ax, l.Ay), new Vector2(l.Bx, l.By), l.Width, l.SidePlus1));
        }

        /// <summary>Every runtime lane of <paramref name="field"/> (the union of every node's records) into <paramref name="into"/>. Cold.</summary>
        public static unsafe void LanesOf(ISimulationView view, Guid field, List<LaneItem> into)
        {
            into.Clear();
            foreach (var e in view.Query().With<MinefieldLanes>().WithLifecycle(EntityLifecycle.AllButTearDown).Build())
            {
                var r = view.GetComponentRO<MinefieldLanes>(e);
                if (r.Field != field) continue;
                for (int i = 0; i < r.Count && i < MinefieldLanes.Capacity; i++) into.Add(r.Get(i));
            }
        }
    }
}
