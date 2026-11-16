using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-4624</c> MS-B2 (Minefields MF-13 ②③ + MR-7; 📄 docs/designs/minefields/DESIGN_Minefields.md §11) — <c>ReconMinefield</c>, run by an
    /// engineer or a scout, on the field's knowledge grid (<see cref="MinefieldKnowledgeOps.Grid"/>):
    /// <list type="number">
    /// <item><b>Seek</b> — from the cell he stands in, one 4-neighbour step at a time toward the suspected point <c>At</c>;</item>
    /// <item>every step is a <b>probe</b> at detector pace (half a cell's length): a cell holding a live mine (the derived lattice — inside the outline,
    ///   not in a lane) is KNOWN-MINED and he does not enter it; else KNOWN-CLEAR and he walks into it (a cell is known only when fully swept);</item>
    /// <item>the first mined cell starts the <b>trace</b>: the mine kept on his LEFT (left-hand rule: left, straight, right, back), he steps only into
    ///   cells he has swept clear, until he is back at the trace's start (closed) or has swept <c>MaxCells</c>.</item>
    /// </list>
    /// Every swept cell is reported (<see cref="MinefieldCellsSwept"/>); a closed trace — or <c>Mark</c> — marks the field for his side
    /// (<see cref="MinefieldMarked"/>). ⛔ He never writes the field or the knowledge record: the serial <c>PlaceKnowledgeSystem</c> applies the events
    /// (MR-12). The field's parameters, outline and grid are read ONCE, at the start, into the blackboard.
    /// </summary>
    public static class ReconMinefieldBehavior
    {
        public const float DefaultPace = 0.2f;
        public const int DefaultMaxCells = 200, MaxOutline = 24;
        public const byte Seek = 0, Trace = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct ReconMinefieldParams
        {
            public int FieldNetId;
            public Vector3 At;
            public byte Mark;
            public int MaxCells;
            public float Pace;
        }

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct ReconMinefieldState
        {
            public byte Started, Phase, Dir, Tried, Moving, Probing;
            public int Col, Row, StartCol, StartRow, Steps, ToCol, ToRow;
            public SimInstant ProbeUntil;
            public Minefield Field;
            public Vector2 Anchor, OriginUv;
            public float Cell;
            public int Cols, Rows;
            public fixed float OX[MaxOutline];
            public fixed float OY[MaxOutline];
            public int OutlineCount;
            /// <summary>Cells swept so far: mined and clear (rails read them).</summary>
            public int Mined, Clear;
            public byte Marked;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ReconMinefieldBlackboard
        {
            public ReconMinefieldParams Params;
            public ReconMinefieldState State;
        }

        [BehaviorResolver(BehaviorNames.ReconMinefield)]
        public static void Resolve(in ReconMinefieldParamsJsonDto authored, ref ReconMinefieldBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new ReconMinefieldParams
            {
                FieldNetId = authored.Field.Ref.NetworkId, At = authored.At, Mark = (byte)(authored.Mark ? 1 : 0),
                MaxCells = authored.MaxCells > 0 ? authored.MaxCells : DefaultMaxCells,
                Pace = authored.Pace > 0f ? authored.Pace : DefaultPace,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.ReconMinefield, Params = typeof(ReconMinefieldParamsJsonDto), Categories = BehaviorCategory.Infantry,
            Description = "Sweeps toward a suspected point at detector pace, traces the minefield's edge on swept-clear cells and marks it.")]
        [BTreeDefinition(BehaviorNames.ReconMinefield, Curated = true, Group = "Library/Engineers", ParamsType = typeof(ReconMinefieldBlackboard))]
        public static BTreeBuilder<ReconMinefieldBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<ReconMinefieldBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Tick);

        [ThreadStatic] private static List<Vector2>? t_outline;

        // direction d: 0 +col, 1 +row, 2 −col, 3 −row; LEFT of d is d + 1
        private static int DCol(int d) => d == 0 ? 1 : d == 2 ? -1 : 0;
        private static int DRow(int d) => d == 1 ? 1 : d == 3 ? -1 : 0;

        public static unsafe NodeStatus Tick(ref ReconMinefieldParams p, ref ReconMinefieldState s, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<SimTransform>(self)) return NodeStatus.Failure;
            var field = p.FieldNetId != 0 ? new Fdp.Toolkit.Replication.EntityRef(p.FieldNetId).Resolve(world) : Entity.Null;
            if (field.IsNull) return NodeStatus.Failure;
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : world.SimulationTime;
            var here = world.GetComponentRO<SimTransform>(self).Position;
            if (s.Started == 0 && !Start(ref p, ref s, world, field, here)) return NodeStatus.Failure;

            if (s.Probing != 0)
            {
                if (now < (double)s.ProbeUntil) return NodeStatus.Running;
                s.Probing = 0;
                bool mined = Mined(ref s, s.ToCol, s.ToRow);
                Report(world, self, field, ref s, s.ToCol, s.ToRow, mined);
                if (!mined)
                {
                    if (s.Phase == Trace) s.Dir = (byte)((s.Dir + Turn(s.Tried)) & 3);   // the seek keeps its heading
                    s.Tried = 0;
                    s.Moving = 1;
                    var c = Centre(ref s, s.ToCol, s.ToRow);
                    return LocomotionMoveTo.Issue(world, self, new Vector3(c, here.Z), p.Pace, 0.5f, MoveGait.Keep) ? NodeStatus.Running : NodeStatus.Failure;
                }
                if (s.Phase == Seek)
                {
                    s.Phase = Trace;                       // the first mine: the trace starts here, the mine on the LEFT
                    s.StartCol = s.Col; s.StartRow = s.Row;
                    s.Dir = (byte)((s.Dir + 3) & 3);
                    s.Tried = 0;
                }
                else s.Tried++;                            // the trace's next candidate (left → straight → right → back)
            }
            else if (s.Moving != 0)
            {
                var c = Centre(ref s, s.ToCol, s.ToRow);
                bool there = Vector2.Distance(new Vector2(here.X, here.Y), c) <= 1f || LocomotionMoveTo.Status(world, self) == NodeStatus.Success;
                if (!there) return NodeStatus.Running;
                s.Moving = 0;
                s.Col = s.ToCol; s.Row = s.ToRow;
                s.Steps++;
                if (s.Phase == Trace && s.Col == s.StartCol && s.Row == s.StartRow && s.Steps >= 4) return End(ref p, ref s, world, self, field, closed: true);
                if (s.Mined + s.Clear >= p.MaxCells) return End(ref p, ref s, world, self, field, closed: false);
            }
            if (s.Mined + s.Clear >= p.MaxCells || s.Tried > 3) return End(ref p, ref s, world, self, field, closed: false);

            // the next probe: seek straight on toward the point; trace — left, straight, right, back
            int d = s.Phase == Seek ? SeekDir(ref s, ref p) : (s.Dir + Turn(s.Tried)) & 3;
            if (s.Phase == Seek) s.Dir = (byte)d;
            s.ToCol = s.Col + DCol(d); s.ToRow = s.Row + DRow(d);
            s.Probing = 1;
            s.ProbeUntil = (SimInstant)(now + 0.5 * s.Cell / Math.Max(p.Pace, 0.01f));
            LocomotionMoveTo.Stop(world, self);
            return NodeStatus.Running;
        }

        /// <summary>The trace's k-th candidate turn: left (+1), straight (0), right (+3), back (+2).</summary>
        private static int Turn(int k) => k switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 };

        private static int SeekDir(ref ReconMinefieldState s, ref ReconMinefieldParams p)
        {
            var uv = MinePattern.ToField(new Vector2(p.At.X, p.At.Y), s.Anchor, s.Field.OrientationDeg) - s.OriginUv;
            float tc = uv.X / s.Cell - 0.5f - s.Col, tr = uv.Y / s.Cell - 0.5f - s.Row;
            if (MathF.Abs(tc) < 0.5f && MathF.Abs(tr) < 0.5f) return s.Dir;
            return MathF.Abs(tc) >= MathF.Abs(tr) ? (tc > 0 ? 0 : 2) : (tr > 0 ? 1 : 3);
        }

        private static unsafe bool Start(ref ReconMinefieldParams p, ref ReconMinefieldState s, EntityRepository world, Entity field, Vector3 here)
        {
            if (!world.HasComponent<Minefield>(field)) return false;
            var outline = t_outline ??= new List<Vector2>(MaxOutline);
            if (!MinefieldKnowledgeOps.TryOutline(world, field, outline, out var anchor)) return false;
            s.Field = world.GetComponentRO<Minefield>(field);
            if (s.Field.SpacingM <= 0f) return false;
            var g = MinefieldKnowledgeOps.Grid(in s.Field, anchor, outline);
            s.Anchor = anchor; s.OriginUv = g.OriginUv; s.Cell = g.Cell; s.Cols = g.Cols; s.Rows = g.Rows;
            s.OutlineCount = Math.Min(outline.Count, MaxOutline);
            for (int i = 0; i < s.OutlineCount; i++) { s.OX[i] = outline[i].X; s.OY[i] = outline[i].Y; }
            var uv = MinePattern.ToField(new Vector2(here.X, here.Y), anchor, s.Field.OrientationDeg) - s.OriginUv;
            s.Col = (int)MathF.Floor(uv.X / s.Cell); s.Row = (int)MathF.Floor(uv.Y / s.Cell);   // may lie outside the grid: open ground
            s.Phase = Seek;
            s.Started = 1;
            return true;
        }

        private static Vector2 Centre(ref ReconMinefieldState s, int col, int row)
            => MinePattern.ToWorld(s.OriginUv + new Vector2((col + 0.5f) * s.Cell, (row + 0.5f) * s.Cell), s.Anchor, s.Field.OrientationDeg);

        /// <summary>⭐ The truth the detector finds: does cell (col, row) hold a live-by-geometry mine (inside the outline, not in a lane)? Lattice
        /// arithmetic over the cell — no allocation. Cells off the grid hold none.</summary>
        public static unsafe bool Mined(ref ReconMinefieldState s, int col, int row)
        {
            if ((uint)col >= (uint)s.Cols || (uint)row >= (uint)s.Rows) return false;
            float sp = s.Field.SpacingM;
            float u0 = s.OriginUv.X + col * s.Cell, v0 = s.OriginUv.Y + row * s.Cell;
            int r0 = (int)MathF.Floor(v0 / sp) - 1, r1 = (int)MathF.Ceiling((v0 + s.Cell) / sp) + 1;
            int c0 = (int)MathF.Floor(u0 / sp) - 1, c1 = (int)MathF.Ceiling((u0 + s.Cell) / sp) + 1;
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                {
                    var w = MinePattern.At(in s.Field, s.Anchor, r, c);
                    var uv = MinePattern.ToField(w, s.Anchor, s.Field.OrientationDeg);
                    if (uv.X < u0 || uv.X >= u0 + s.Cell || uv.Y < v0 || uv.Y >= v0 + s.Cell) continue;
                    if (!InOutline(ref s, w) || MinePattern.InLane(in s.Field, w)) continue;
                    return true;
                }
            return false;
        }

        private static unsafe bool InOutline(ref ReconMinefieldState s, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = s.OutlineCount - 1; i < s.OutlineCount; j = i++)
            {
                float ax = s.OX[i], ay = s.OY[i], bx = s.OX[j], by = s.OY[j];
                if ((ay > p.Y) != (by > p.Y) && p.X < (bx - ax) * (p.Y - ay) / (by - ay) + ax) inside = !inside;
            }
            return inside;
        }

        private static unsafe void Report(EntityRepository world, Entity self, Entity field, ref ReconMinefieldState s, int col, int row, bool mined)
        {
            if (mined) s.Mined++; else s.Clear++;
            if ((uint)col >= (uint)s.Cols || (uint)row >= (uint)s.Rows || !world.Bus.IsRegistered<MinefieldCellsSwept>()) return;
            var e = new MinefieldCellsSwept { Field = field, SidePlus1 = SidePlus1(world, self), Count = 1, By = self };
            e.Col[0] = (byte)col; e.Row[0] = (byte)row;
            e.Found[0] = (byte)(mined ? MineCellKnowledge.KnownMined : MineCellKnowledge.KnownClear);
            world.Bus.Publish(e);
        }

        private static NodeStatus End(ref ReconMinefieldParams p, ref ReconMinefieldState s, EntityRepository world, Entity self, Entity field, bool closed)
        {
            LocomotionMoveTo.Stop(world, self);
            if ((closed || p.Mark != 0) && s.Marked == 0 && world.Bus.IsRegistered<MinefieldMarked>())
            {
                world.Bus.Publish(new MinefieldMarked { Field = field, SidePlus1 = SidePlus1(world, self), By = self });
                s.Marked = 1;
            }
            return NodeStatus.Success;
        }

        private static byte SidePlus1(EntityRepository world, Entity self)
            => world.HasComponent<EntityInfo>(self) ? (byte)((int)world.GetComponentRO<EntityInfo>(self).ForceId + 1) : (byte)0;
    }
}
