using System;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Perception.Components
{
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
    [ComponentId(GlobalComponentIds.TargetMemory)]
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

        /// <summary>Last-known Z position (meters, altitude — Sim Z-up) for each target slot.
        /// 3D Cognitive Spatial Awareness promotion (P3D-206).</summary>
        public fixed float PositionsZ[PerceptionConstants.MaxTrackedTargets];

        /// <summary>
        /// Accumulated threat score per slot.
        /// Boosted by perception events, decayed every frame by
        /// <see cref="PerceptionConstants.ThreatScoreDecayPerSecond"/>.
        /// </summary>
        public fixed float ThreatScores[PerceptionConstants.MaxTrackedTargets];

        /// <summary>Simulation tick when this target was last perceived.</summary>
        public fixed uint LastSeenTick[PerceptionConstants.MaxTrackedTargets];

        /// <summary>
        /// Bitmask of <see cref="SensorModality"/> values that have detected each target.
        /// OR-accumulated on re-observation; reset to the new modality on eviction.
        /// </summary>
        public fixed byte Modalities[PerceptionConstants.MaxTrackedTargets];

        /// <summary>
        /// Incremented each time a contact is added or evicted.
        /// Does NOT increment on score updates to existing contacts.
        /// Consumers (e.g. SquadPerceptionMergeSystem) XOR this value to detect
        /// structural changes without iterating the table on every tick.
        /// </summary>
        public uint ChangeEpoch;

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
        /// <param name="modality">Sensor modality that detected the target (default: <see cref="SensorModality.Visual"/>).</param>
        /// <param name="posZ">Target Z position (altitude — Sim Z-up). 3D promotion (P3D-206); defaults to 0 for flat callers.</param>
        public static void AddOrUpdateTarget(
            ref TargetMemory mem,
            long entityId,
            float posX,
            float posY,
            float scoreBoost,
            uint tick,
            SensorModality modality = SensorModality.Visual,
            float posZ = 0f)
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
                mem.PositionsZ[foundSlot]    = posZ;
                mem.LastSeenTick[foundSlot]  = tick;
                // OR the new modality into the existing modality bitmask.
                mem.Modalities[foundSlot]   |= (byte)modality;
            }
            else if (mem.Count < PerceptionConstants.MaxTrackedTargets)
            {
                // Add a new slot.
                int slot = mem.Count;
                mem.EntityIds[slot]    = entityId;
                mem.PositionsX[slot]   = posX;
                mem.PositionsY[slot]   = posY;
                mem.PositionsZ[slot]   = posZ;
                mem.ThreatScores[slot] = scoreBoost;
                mem.LastSeenTick[slot] = tick;
                mem.Modalities[slot]   = (byte)modality;
                mem.Count++;
                mem.ChangeEpoch++;
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
                    mem.PositionsZ[lowestIdx]   = posZ;
                    mem.ThreatScores[lowestIdx] = scoreBoost;
                    mem.LastSeenTick[lowestIdx] = tick;
                    // Fresh modality for the new entry (eviction resets the bitmask).
                    mem.Modalities[lowestIdx]   = (byte)modality;
                    mem.ChangeEpoch++;
                }
            }

            // 2. Sort descending by ThreatScore (insertion sort — MaxTrackedTargets is tiny).
            for (int i = 1; i < mem.Count; i++)
            {
                long   idTmp    = mem.EntityIds[i];
                float  pxTmp    = mem.PositionsX[i];
                float  pyTmp    = mem.PositionsY[i];
                float  pzTmp    = mem.PositionsZ[i];
                float  scoreTmp = mem.ThreatScores[i];
                uint   tickTmp  = mem.LastSeenTick[i];
                byte   modTmp   = mem.Modalities[i];

                int j = i - 1;
                while (j >= 0 && mem.ThreatScores[j] < scoreTmp)
                {
                    mem.EntityIds[j + 1]    = mem.EntityIds[j];
                    mem.PositionsX[j + 1]   = mem.PositionsX[j];
                    mem.PositionsY[j + 1]   = mem.PositionsY[j];
                    mem.PositionsZ[j + 1]   = mem.PositionsZ[j];
                    mem.ThreatScores[j + 1] = mem.ThreatScores[j];
                    mem.LastSeenTick[j + 1] = mem.LastSeenTick[j];
                    mem.Modalities[j + 1]   = mem.Modalities[j];
                    j--;
                }

                mem.EntityIds[j + 1]    = idTmp;
                mem.PositionsX[j + 1]   = pxTmp;
                mem.PositionsY[j + 1]   = pyTmp;
                mem.PositionsZ[j + 1]   = pzTmp;
                mem.ThreatScores[j + 1] = scoreTmp;
                mem.LastSeenTick[j + 1] = tickTmp;
                mem.Modalities[j + 1]   = modTmp;
            }
        }
    }

    // ── SensorContactState ────────────────────────────────────────────────────────

    /// <summary>
    /// Hysteresis state of a single physical sensor contact on the Muscle (SimHost) node.
    /// Transitions: Pending → Acquired (first confirmed sighting) → Lost (occlusion timeout).
    /// </summary>
    public enum SensorContactState : byte
    {
        /// <summary>Contact recorded but not yet confirmed as acquired.</summary>
        Pending  = 0,
        /// <summary>Contact actively visible within the debounce window.</summary>
        Acquired = 1,
        /// <summary>Contact has exceeded the occlusion threshold and is declared lost.</summary>
        Lost     = 2,
    }

    // ── SensorContactList ─────────────────────────────────────────────────────────

    /// <summary>
    /// Muscle-tier raw physical track list. Holds up to <see cref="PerceptionConstants.MaxTrackedTargets"/>
    /// sensor contacts with their last-seen tick and hysteresis state.
    /// No cognitive data (no threat scores, no decay curves).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(PerceptionApplicationComponentIds.SensorContactList)]
    [DataPolicy(DataPolicy.NoSave)]
    public unsafe struct SensorContactList
    {
        /// <summary>Number of valid contact entries (0 to <see cref="PerceptionConstants.MaxTrackedTargets"/>).</summary>
        public int Count;

        /// <summary>Local ECS entity packed values of tracked contacts.</summary>
        public fixed long EntityIds[PerceptionConstants.MaxTrackedTargets];

        /// <summary>Simulation tick when each contact was last seen.</summary>
        public fixed uint LastSeenTick[PerceptionConstants.MaxTrackedTargets];

        /// <summary>Hysteresis state of each contact slot.</summary>
        public fixed byte State[PerceptionConstants.MaxTrackedTargets];

        /// <summary>
        /// Updates the last-seen tick for an existing contact, or adds a new
        /// <see cref="SensorContactState.Pending"/> slot if the entity is not yet tracked.
        /// Does nothing if the list is full and the entity is not already tracked.
        /// </summary>
        public static void UpdateSighting(ref SensorContactList list, long entityId, uint tick)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list.EntityIds[i] == entityId)
                {
                    list.LastSeenTick[i] = tick;
                    return;
                }
            }

            if (list.Count < PerceptionConstants.MaxTrackedTargets)
            {
                int idx = list.Count++;
                list.EntityIds[idx]    = entityId;
                list.LastSeenTick[idx] = tick;
                list.State[idx]        = (byte)SensorContactState.Pending;
            }
        }
    }

    // ── ActiveSensorTracks ────────────────────────────────────────────────────────

    /// <summary>
    /// Brain-tier cognitive buffer. Holds the set of targets currently confirmed as
    /// <see cref="SensorContactState.Acquired"/> by the Muscle-side sensor pipeline.
    /// Written by <c>SensorTrackStateIngressTranslator</c>; read by <c>ThreatEvaluationSystem</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(PerceptionApplicationComponentIds.ActiveSensorTracks)]
    [DataPolicy(DataPolicy.NoSave)]
    public unsafe struct ActiveSensorTracks
    {
        /// <summary>Number of currently acquired tracks (0 to <see cref="PerceptionConstants.MaxTrackedTargets"/>).</summary>
        public int Count;

        /// <summary>Local ECS entity packed values of acquired targets.</summary>
        public fixed long EntityIds[PerceptionConstants.MaxTrackedTargets];

        /// <summary>Last-known X position (ground plane) for each acquired target.</summary>
        public fixed float PositionsX[PerceptionConstants.MaxTrackedTargets];

        /// <summary>Last-known Y position (ground plane) for each acquired target.</summary>
        public fixed float PositionsY[PerceptionConstants.MaxTrackedTargets];
    }
}
