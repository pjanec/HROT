using System;
using System.Runtime.InteropServices;
using Fdp.Kernel;

namespace FDP.Toolkit.Perception.Components
{
    // ── Faction ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Identifies the side an entity belongs to for perception filtering.
    /// Entities with the same <see cref="FactionId"/> are considered friendly and
    /// are excluded from vision-broadphase checks.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(250)]
    public struct Faction
    {
        /// <summary>
        /// Faction identifier. Entities sharing the same ID are treated as allies.
        /// Convention: 0 = neutral/unset, 1 = Blue force, 2 = Red force.
        /// </summary>
        public byte FactionId;
    }

    // ── PerceptionReceptor ────────────────────────────────────────────────────────

    /// <summary>
    /// Defines the sensory capabilities of an entity.
    /// Attach to any entity that should react to audio stimuli or perform visual scans.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(251)]
    public struct PerceptionReceptor
    {
        /// <summary>Maximum distance (meters) at which this entity can hear audio stimuli.</summary>
        public float HearingRange;

        /// <summary>Maximum distance (meters) at which this entity can see targets.</summary>
        public float VisionRange;

        /// <summary>
        /// Precomputed cosine of the half-FOV angle.
        /// Example: 60° FOV → half-FOV = 30° → store <c>MathF.Cos(MathF.PI / 6f) ≈ 0.866f</c>.
        /// A dot-product against the normalised observer→target vector is compared to this value;
        /// targets below the threshold are outside the cone and are ignored.
        /// Storing the cosine avoids per-frame trig on the hot path.
        /// </summary>
        public float FieldOfViewCos;
    }

    // ── TargetMemory ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Fixed-size unmanaged threat table attached to entities with perception.
    /// Holds up to <see cref="PerceptionConstants.MaxTrackedTargets"/> perceived targets,
    /// sorted descending by <see cref="ThreatScores"/>.
    /// <para>
    /// All fixed array sizes use <see cref="PerceptionConstants.MaxTrackedTargets"/> — never raw literals.
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(252)]
    public unsafe struct TargetMemory
    {
        /// <summary>Number of valid entries currently stored (0–<see cref="PerceptionConstants.MaxTrackedTargets"/>).</summary>
        public int Count;

        /// <summary>Entity indices of tracked targets. Only indices [0, Count) are valid.</summary>
        public fixed long EntityIds[PerceptionConstants.MaxTrackedTargets];

        /// <summary>Last-known X position (meters, ground plane) for each target slot.</summary>
        public fixed float PositionsX[PerceptionConstants.MaxTrackedTargets];

        /// <summary>Last-known Y position (meters, ground plane) for each target slot.</summary>
        public fixed float PositionsY[PerceptionConstants.MaxTrackedTargets];

        /// <summary>
        /// Accumulated threat score per slot.
        /// Boosted by perception events, decayed every frame by
        /// <see cref="PerceptionConstants.ThreatScoreDecayPerSecond"/>.
        /// </summary>
        public fixed float ThreatScores[PerceptionConstants.MaxTrackedTargets];

        /// <summary>Simulation tick when this target was last perceived.</summary>
        public fixed uint LastSeenTick[PerceptionConstants.MaxTrackedTargets];

        // ── Mutation API ──────────────────────────────────────────────────────────

        /// <summary>
        /// Adds or updates a target entry in this memory.
        /// <list type="bullet">
        ///   <item>If <paramref name="entityId"/> already exists, its score is incremented by <paramref name="scoreBoost"/> and its position updated.</item>
        ///   <item>If not found and <see cref="Count"/> &lt; <see cref="PerceptionConstants.MaxTrackedTargets"/>, a new slot is allocated.</item>
        ///   <item>If the table is full, the slot with the lowest current score is replaced (if it is lower than <paramref name="scoreBoost"/>).</item>
        ///   <item>The table is then sorted descending by threat score so slot 0 is always the highest threat.</item>
        /// </list>
        /// </summary>
        /// <param name="entityId">Index of the perceived target entity.</param>
        /// <param name="posX">Target X position (ground plane).</param>
        /// <param name="posY">Target Y position (ground plane).</param>
        /// <param name="scoreBoost">Score contribution from this perception event.</param>
        /// <param name="tick">Current simulation tick.</param>
        public static void AddOrUpdateTarget(
            ref TargetMemory mem,
            long entityId,
            float posX,
            float posY,
            float scoreBoost,
            uint tick)
        {
            // 1. Look for an existing slot with the same entity ID.
            int foundSlot = -1;
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.EntityIds[i] == entityId)
                {
                    foundSlot = i;
                    break;
                }
            }

            if (foundSlot >= 0)
            {
                // Accumulate score and refresh position / tick.
                mem.ThreatScores[foundSlot] += scoreBoost;
                mem.PositionsX[foundSlot]    = posX;
                mem.PositionsY[foundSlot]    = posY;
                mem.LastSeenTick[foundSlot]  = tick;
            }
            else if (mem.Count < PerceptionConstants.MaxTrackedTargets)
            {
                // Add a new slot.
                int slot = mem.Count;
                mem.EntityIds[slot]    = entityId;
                mem.PositionsX[slot]   = posX;
                mem.PositionsY[slot]   = posY;
                mem.ThreatScores[slot] = scoreBoost;
                mem.LastSeenTick[slot] = tick;
                mem.Count++;
            }
            else
            {
                // Table is full — replace the lowest-score slot if the new score exceeds it.
                int lowestIdx = 0;
                float lowestScore = mem.ThreatScores[0];
                for (int i = 1; i < PerceptionConstants.MaxTrackedTargets; i++)
                {
                    if (mem.ThreatScores[i] < lowestScore)
                    {
                        lowestScore = mem.ThreatScores[i];
                        lowestIdx   = i;
                    }
                }

                if (scoreBoost > lowestScore)
                {
                    mem.EntityIds[lowestIdx]    = entityId;
                    mem.PositionsX[lowestIdx]   = posX;
                    mem.PositionsY[lowestIdx]   = posY;
                    mem.ThreatScores[lowestIdx] = scoreBoost;
                    mem.LastSeenTick[lowestIdx] = tick;
                }
            }

            // 2. Sort descending by ThreatScore (insertion sort — MaxTrackedTargets is tiny).
            for (int i = 1; i < mem.Count; i++)
            {
                long   idTmp    = mem.EntityIds[i];
                float  pxTmp    = mem.PositionsX[i];
                float  pyTmp    = mem.PositionsY[i];
                float  scoreTmp = mem.ThreatScores[i];
                uint   tickTmp  = mem.LastSeenTick[i];

                int j = i - 1;
                while (j >= 0 && mem.ThreatScores[j] < scoreTmp)
                {
                    mem.EntityIds[j + 1]    = mem.EntityIds[j];
                    mem.PositionsX[j + 1]   = mem.PositionsX[j];
                    mem.PositionsY[j + 1]   = mem.PositionsY[j];
                    mem.ThreatScores[j + 1] = mem.ThreatScores[j];
                    mem.LastSeenTick[j + 1] = mem.LastSeenTick[j];
                    j--;
                }

                mem.EntityIds[j + 1]    = idTmp;
                mem.PositionsX[j + 1]   = pxTmp;
                mem.PositionsY[j + 1]   = pyTmp;
                mem.ThreatScores[j + 1] = scoreTmp;
                mem.LastSeenTick[j + 1] = tickTmp;
            }
        }
    }
}
