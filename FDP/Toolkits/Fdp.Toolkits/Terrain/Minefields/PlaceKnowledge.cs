using System;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>What a place-knowledge record is about (DESIGN_Minefields.md KR-1).</summary>
    public enum SubjectKind : byte { NoSubject = 0, Entity = 1, TerrainObject = 2, Place = 3 }   // ⭐ CE-2468 (§9b.1 K7) — a place: a derived stable key, no entity   // ⚠ unique member names: the DDS generator scopes a file's enums together

    /// <summary>The kinds of place knowledge (MF-17, KG-5) — only <see cref="Minefield"/> is built.</summary>
    public enum PlaceKnowledgeKind : byte { NoKind = 0, Minefield = 1, UnderFire = 2 }   // ⭐ CE-2468 (§9b) — the second kind: a place under fire

    /// <summary>How a side learned it.</summary>
    public enum KnowledgeSource : byte { NoSource = 0, Strike = 1, Recon = 2, Marking = 3, KnownAtStart = 4, Report = 5, Contact = 6, Burst = 7 }   // ⭐ CE-2468 — a broken contact (Direct), a witnessed shelling (Indirect)

    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T2 (MF-17, KR-1, KR-4, KR-7, KR-13) — the header EVERY place-knowledge kind shares: the subject (an entity's stable <see cref="Guid"/>, or a
    /// terrain-object index), the side + 1 (0 = none), the kind, the chunk, the state, the source, <see cref="Marked"/> and <see cref="LearnedAt"/>. Merge: the
    /// kind's policy for the payload; <see cref="LearnedAt"/> max, <see cref="Marked"/> OR.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PlaceKnowledgeHeader
    {
        public SubjectKind SubjectKind;
        public byte SidePlus1;
        public PlaceKnowledgeKind Kind;
        public byte State;
        public KnowledgeSource Source;
        public byte Marked;
        public ushort Chunk;
        /// <summary>The subject entity's stable id (<see cref="SubjectKind.Entity"/>).</summary>
        public Guid Subject;
        /// <summary>The terrain-object index (<see cref="SubjectKind.TerrainObject"/>), −1 otherwise.</summary>
        public int ObjectIndex;
        public SimInstant LearnedAt;

        /// <summary>The side (<c>ForceId</c>) or −1 when none.</summary>
        public readonly int Side => SidePlus1 == 0 ? -1 : SidePlus1 - 1;
    }

    /// <summary>The key of a side's knowledge of a field: the field's stable id and the side + 1.</summary>
    public readonly record struct KnowledgeKey(Guid Field, byte SidePlus1);

    /// <summary>One learned cell (or, with <see cref="Col"/> = <see cref="MarkOnly"/>, the field marked) — the knowledge writer's item.</summary>
    public readonly record struct KnowledgeItem(KnowledgeKey Key, byte Col, byte Row, MineCellKnowledge Level, byte Marked, KnowledgeSource Source, SimInstant At)
    {
        public const byte MarkOnly = 255;
    }

    /// <summary>A cell of a side's knowledge of a field (2 bits; the lattice order unknown &lt; suspected &lt; known-clear &lt; known-mined — MR-2).</summary>
    public enum MineCellKnowledge : byte { Unknown = 0, Suspected = 1, KnownClear = 2, KnownMined = 3 }

    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T2 (MF-13, MF-14, MR-1, KR-2, KR-7) — ONE side's knowledge of ONE minefield, written by ONE deciding node (the union of a key's
    /// entities is what every reader sees, KR-5): the shared <see cref="PlaceKnowledgeHeader"/> + a 2-bit grid of ≤ <see cref="MaxCols"/> × <see cref="MaxRows"/>
    /// cells over the field (<see cref="MinefieldKnowledgeOps.Grid"/> places it). On TKB <see cref="TkbType"/>; recorded, replicated (<c>dtMinefieldKnowledge</c>);
    /// saved through the scenario's <c>knowledge</c> entry (KR-15), never as an entity. ⚠ The 1024 B component cap: a rail asserts the size.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.MinefieldKnowledge)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct MinefieldKnowledge : IKeyedRecord<KnowledgeKey, KnowledgeItem>
    {
        KnowledgeKey IKeyedRecord<KnowledgeKey, KnowledgeItem>.RecordKey
        {
            readonly get => new(Header.Subject, Header.SidePlus1);
            set { Header.Subject = value.Field; Header.SidePlus1 = value.SidePlus1; Header.SubjectKind = SubjectKind.Entity; Header.Kind = PlaceKnowledgeKind.Minefield; Header.ObjectIndex = -1; }
        }
        public readonly bool IsFull => false;   // one record covers the whole grid
        public readonly bool Holds(in KnowledgeItem item)
        {
            if (item.Marked != 0 && Header.Marked == 0) return false;
            if (item.Col == KnowledgeItem.MarkOnly) return true;
            return Get(item.Col, item.Row) >= item.Level;
        }
        public bool TryAdd(in KnowledgeItem item)
        {
            if (item.Marked != 0) Header.Marked = 1;
            if (item.Col != KnowledgeItem.MarkOnly) Raise(item.Col, item.Row, item.Level);
            if (item.At > Header.LearnedAt) Header.LearnedAt = item.At;
            Header.Source = item.Source;
            return true;
        }

        public const int MaxCols = 60, MaxRows = 64;
        public const int CellBytes = MaxCols * MaxRows / 4;
        /// <summary>⭐ TKB 7206 <c>MinefieldKnowledgeRecord</c> (the record block 7201–7205).</summary>
        public const long TkbType = 7206;

        public PlaceKnowledgeHeader Header;
        public fixed byte Cells[CellBytes];

        public readonly MineCellKnowledge Get(int col, int row)
        {
            if ((uint)col >= MaxCols || (uint)row >= MaxRows) return MineCellKnowledge.Unknown;
            int i = row * MaxCols + col;
            return (MineCellKnowledge)((Cells[i >> 2] >> ((i & 3) * 2)) & 3);
        }

        public void Set(int col, int row, MineCellKnowledge k)
        {
            if ((uint)col >= MaxCols || (uint)row >= MaxRows) return;
            int i = row * MaxCols + col, shift = (i & 3) * 2;
            Cells[i >> 2] = (byte)((Cells[i >> 2] & ~(3 << shift)) | ((int)k << shift));
        }

        /// <summary>Raises a cell by the lattice (never lowers it — MR-2); true when it changed.</summary>
        public bool Raise(int col, int row, MineCellKnowledge k)
        {
            if (Get(col, row) >= k) return false;
            Set(col, row, k);
            return true;
        }
    }
}
