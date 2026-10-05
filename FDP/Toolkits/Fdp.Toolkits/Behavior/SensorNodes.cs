using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Sensors;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Spatial.Eqs;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>⭐ <c>CE-3054</c> D — which of the unit's sensors to read, and what counts as "sees".</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SensorReadParams
    {
        /// <summary>The sensor kind (the unit's sensor of this kind, lowest part id — <see cref="UnitSensors.Of"/>).</summary>
        public SensorModality Kind;
        /// <summary><see cref="SensorNodes.Sees"/>: at least this many results (0 counts as 1).</summary>
        public byte MinCount;
        /// <summary><see cref="SensorNodes.Sees"/>: the top result scores at least this.</summary>
        public float MinTopScore;
    }

    /// <summary>⭐ <c>CE-3054</c> D — what <see cref="SensorNodes.Read"/> last read (the caller's working state).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SensorReading
    {
        /// <summary>The number of results in the sensor's last answer (0 when it has none yet).</summary>
        public int Count;
        /// <summary>The top result's score.</summary>
        public float TopScore;
        /// <summary>The top result's entity (none for a point answer, or when it has no network id).</summary>
        public EntityRef Top;
        /// <summary>The top result's position.</summary>
        public Vector3 TopPosition;
        /// <summary>The answer's stamp (<see cref="EqsCognitiveBuffer.LastUpdateTick"/>); 0 = no answer yet.</summary>
        public uint AnswerTick;
    }

    /// <summary>⭐ <c>CE-3054</c> D — params of <see cref="SensorNodes.ThreatsAtLeast"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ThreatCountParams
    {
        /// <summary>At least this many contacts (0 counts as 1).</summary>
        public byte Count;
        /// <summary>Only contacts at least this dangerous (<see cref="ThreatDanger"/>).</summary>
        public float MinDanger;
        /// <summary>Only LIVE contacts (<see cref="ThreatFreshness.IsLive"/>) when set; every remembered one otherwise.</summary>
        public bool LiveOnly;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3054</c> D — the AI reads its sensors and its threat memory, as shared nodes a BTree and an HSM bind
    /// (R-201). 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.8a. The kind is a PARAM — one node for every sensor kind.
    /// </summary>
    public static class SensorNodes
    {
        /// <summary>True when the unit's <see cref="SensorReadParams.Kind"/> sensor's last answer has at least
        /// <see cref="SensorReadParams.MinCount"/> results and its top scores at least <see cref="SensorReadParams.MinTopScore"/>.</summary>
        [SharedAiCondition]
        public static bool Sees(ref SensorReadParams p, Entity self, EntityRepository world)
        {
            if (!UnitSensors.TryGetResults(world, self, p.Kind, out var buffer) || !buffer.IsReady) return false;
            int min = p.MinCount == 0 ? 1 : p.MinCount;
            return buffer.Count >= min && buffer.GetTop().Score >= p.MinTopScore;
        }

        /// <summary>Reads the unit's <see cref="SensorReadParams.Kind"/> sensor into the working state each tick (Running,
        /// like <c>UtilityNodes.RankCandidates</c>) — the count, the top result and the answer stamp.</summary>
        [SharedAiAction]
        public static NodeStatus Read(ref SensorReadParams p, ref SensorReading ws, Entity self, EntityRepository world)
        {
            if (!UnitSensors.TryGetResults(world, self, p.Kind, out var buffer) || !buffer.IsReady)
            {
                ws = default;
                return NodeStatus.Running;
            }
            ws.Count      = buffer.Count;
            ws.AnswerTick = buffer.LastUpdateTick;
            if (buffer.Count == 0)
            {
                ws.TopScore = 0f;
                ws.Top = EntityRef.None;
                ws.TopPosition = default;
                return NodeStatus.Running;
            }
            ref readonly var top = ref buffer.GetTop();
            ws.TopScore    = top.Score;
            ws.TopPosition = new Vector3(top.PositionX, top.PositionY, top.PositionZ);
            ws.Top         = top.EntityId > 0 ? EntityRef.Of(world, new Entity((ulong)top.EntityId)) : EntityRef.None;
            return NodeStatus.Running;
        }

        /// <summary>True when the unit remembers at least <see cref="ThreatCountParams.Count"/> contacts at least
        /// <see cref="ThreatCountParams.MinDanger"/> dangerous (live ones only when <see cref="ThreatCountParams.LiveOnly"/>) —
        /// "two armed enemies in sight".</summary>
        [SharedAiCondition]
        public static unsafe bool ThreatsAtLeast(ref ThreatCountParams p, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<TargetMemory>(self)) return false;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            int need = p.Count == 0 ? 1 : p.Count, found = 0;
            for (int i = 0; i < mem.Count; i++)
            {
                if (p.LiveOnly && !ThreatFreshness.IsLive(world, self, in mem, i)) continue;
                if (ThreatDanger.Of(world, self, new Entity((ulong)mem.EntityIds[i])) < p.MinDanger) continue;
                if (++found >= need) return true;
            }
            return false;
        }
    }
}
