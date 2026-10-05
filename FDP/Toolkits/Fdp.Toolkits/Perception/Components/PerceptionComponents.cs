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
    /// sorted descending by <see cref="Freshness"/>.
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
        /// How RECENTLY each slot was perceived — boosted while a sensor reports the contact, decayed every frame by
        /// <see cref="PerceptionConstants.ThreatScoreDecayPerSecond"/>, forgotten below the floor unless tracked.
        /// ⭐ <c>CE-3054</c> A (R-201) — renamed from <c>ThreatScores</c>: it never held a DANGER. Memory stores identity +
        /// freshness; danger is judged at read time (<c>ThreatDanger</c>). 📄 docs/DESIGN_Sensors_And_Doctrine.md §7.8.
        /// ⚠ The scenario save key stays the literal <c>"Score"</c> (<c>TargetMemoryTranslator</c>) — saved files are unchanged.
        /// </summary>
        public fixed float Freshness[PerceptionConstants.MaxTrackedTargets];

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

        // ── ⭐ CE-3063 — ANONYMOUS contacts (heard, never identified; R-205 / R-207) ─────────────────────────────────────
        //    📄 docs/DESIGN_Thermal_And_Acoustic_Sensing.md §6 D′–D″, §6.1a. One memory, one freshness rule (R-194).

        /// <summary>⭐ K1 — 1 when the slot is a heard, unidentified contact ("something about here"). ⛔ Its id is a synthetic
        /// NEGATIVE serial, never an entity: read <see cref="IsAnonymous"/> before turning an id into an entity.</summary>
        public fixed byte Anonymous[PerceptionConstants.MaxTrackedTargets];

        /// <summary>How uncertain the slot's position is (metres); 0 for an identified contact.</summary>
        public fixed float Radius[PerceptionConstants.MaxTrackedTargets];

        /// <summary>⭐ K3 — what the contact SOUNDED like (<c>Tkb.Domain.SoundSourceClass</c>); 0 = unknown.</summary>
        public fixed byte SourceClass[PerceptionConstants.MaxTrackedTargets];

        /// <summary>The last synthetic serial given to an anonymous contact (its id is <c>-serial</c>), so a behaviour can
        /// follow ONE heard contact across ticks.</summary>
        public int AnonymousSerial;

        /// <summary>⭐ The ONE rule every reader calls: true when slot <paramref name="slot"/> is a heard, unidentified
        /// contact. ⛔ An ENTITY reader (aim, fire, entity reads) skips it; a POSITION reader may use it.</summary>
        public static bool IsAnonymous(in TargetMemory mem, int slot)
        {
            fixed (byte* a = mem.Anonymous) return (uint)slot < (uint)mem.Count && a[slot] != 0;
        }

        /// <summary>⭐ <c>CE-3063</c> ② — the first slot that names an ENTITY (the most threatening identified contact, slots being
        /// sorted), or -1 when every remembered contact is heard only.</summary>
        public static int FirstIdentified(in TargetMemory mem)
        {
            for (int i = 0; i < mem.Count; i++)
                if (!IsAnonymous(in mem, i)) return i;
            return -1;
        }

        // ── Mutation API ──────────────────────────────────────────────────────────

        /// <summary>
        /// Adds or updates a target entry in this memory.
        /// <list type="bullet">
        ///   <item>If <paramref name="entityId"/> already exists, its score is incremented by <paramref name="scoreBoost"/> and its position updated.</item>
        ///   <item>If not found and <see cref="Count"/> &lt; <see cref="PerceptionConstants.MaxTrackedTargets"/>, a new slot is allocated.</item>
        ///   <item>If the table is full, the slot with the lowest current score is replaced — a new contact always enters (CE-3046).</item>
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
                mem.Freshness[foundSlot] += scoreBoost;
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
                mem.Freshness[slot] = scoreBoost;
                mem.LastSeenTick[slot] = tick;
                mem.Modalities[slot]   = (byte)modality;
                mem.Anonymous[slot]    = 0;
                mem.Radius[slot]       = 0f;
                mem.SourceClass[slot]  = 0;
                mem.Count++;
                mem.ChangeEpoch++;
            }
            else
            {
                // ⭐ CE-3046 — the table is full: a NEW contact ALWAYS enters, evicting the least dangerous entry (lowest
                //   score; ties: the oldest sighting, then the later slot). 🔴 It used to enter only when its first boost beat
                //   the lowest ACCUMULATED score — which a fresh contact (50 x dt) never does, so a unit holding 16 contacts
                //   was blind to every new threat. 📄 docs/DESIGN_Sensors_And_Doctrine.md §5.4, §11.1.
                int lowestIdx = 0;
                for (int i = 1; i < PerceptionConstants.MaxTrackedTargets; i++)
                {
                    float si = mem.Freshness[i], sl = mem.Freshness[lowestIdx];
                    if (si < sl || (si == sl && mem.LastSeenTick[i] <= mem.LastSeenTick[lowestIdx]))
                        lowestIdx = i;
                }

                {
                    mem.EntityIds[lowestIdx]    = entityId;
                    mem.PositionsX[lowestIdx]   = posX;
                    mem.PositionsY[lowestIdx]   = posY;
                    mem.PositionsZ[lowestIdx]   = posZ;
                    mem.Freshness[lowestIdx] = scoreBoost;
                    mem.LastSeenTick[lowestIdx] = tick;
                    // Fresh modality for the new entry (eviction resets the bitmask).
                    mem.Modalities[lowestIdx]   = (byte)modality;
                    mem.Anonymous[lowestIdx]    = 0;
                    mem.Radius[lowestIdx]       = 0f;
                    mem.SourceClass[lowestIdx]  = 0;
                    mem.ChangeEpoch++;
                }
            }

            Sort(ref mem);
        }

        /// <summary>Sorts the table descending by threat score (insertion sort — <see cref="PerceptionConstants.MaxTrackedTargets"/>
        /// is tiny), carrying every per-slot field.</summary>
        public static void Sort(ref TargetMemory mem)
        {
            for (int i = 1; i < mem.Count; i++)
            {
                long   idTmp    = mem.EntityIds[i];
                float  pxTmp    = mem.PositionsX[i];
                float  pyTmp    = mem.PositionsY[i];
                float  pzTmp    = mem.PositionsZ[i];
                float  scoreTmp = mem.Freshness[i];
                uint   tickTmp  = mem.LastSeenTick[i];
                byte   modTmp   = mem.Modalities[i];
                byte   anonTmp  = mem.Anonymous[i];
                float  radTmp   = mem.Radius[i];
                byte   clsTmp   = mem.SourceClass[i];

                int j = i - 1;
                while (j >= 0 && mem.Freshness[j] < scoreTmp)
                {
                    mem.EntityIds[j + 1]    = mem.EntityIds[j];
                    mem.PositionsX[j + 1]   = mem.PositionsX[j];
                    mem.PositionsY[j + 1]   = mem.PositionsY[j];
                    mem.PositionsZ[j + 1]   = mem.PositionsZ[j];
                    mem.Freshness[j + 1] = mem.Freshness[j];
                    mem.LastSeenTick[j + 1] = mem.LastSeenTick[j];
                    mem.Modalities[j + 1]   = mem.Modalities[j];
                    mem.Anonymous[j + 1]    = mem.Anonymous[j];
                    mem.Radius[j + 1]       = mem.Radius[j];
                    mem.SourceClass[j + 1]  = mem.SourceClass[j];
                    j--;
                }

                mem.EntityIds[j + 1]    = idTmp;
                mem.PositionsX[j + 1]   = pxTmp;
                mem.PositionsY[j + 1]   = pyTmp;
                mem.PositionsZ[j + 1]   = pzTmp;
                mem.Freshness[j + 1] = scoreTmp;
                mem.LastSeenTick[j + 1] = tickTmp;
                mem.Modalities[j + 1]   = modTmp;
                mem.Anonymous[j + 1]    = anonTmp;
                mem.Radius[j + 1]       = radTmp;
                mem.SourceClass[j + 1]  = clsTmp;
            }
        }

        /// <summary>⭐ CE-3063 (§6.1a K3) — two sound classes may be the same source: equal, or either unknown (0).</summary>
        public static bool ClassesCompatible(byte a, byte b) => a == 0 || b == 0 || a == b;

        /// <summary>
        /// ⭐⭐ <c>CE-3063</c> — a unit HEARD something at (<paramref name="x"/>, <paramref name="y"/>) within
        /// <paramref name="radius"/>, sounding like <paramref name="sourceClass"/>. 📄 DESIGN_Thermal_And_Acoustic_Sensing §5.1, §6 D′.
        /// <list type="number">
        ///   <item>an IDENTIFIED contact inside the radius is refreshed (nearest; the Acoustic kind is added) — hearing what you know;</item>
        ///   <item>else an ANONYMOUS contact of a compatible class whose circle meets the estimate is FUSED: the position moves
        ///     to the inverse-variance mean and the radius SHRINKS (<c>1/r² = 1/r₁² + 1/r₂²</c>) — repeated shots narrow it;</item>
        ///   <item>else a new anonymous slot (a synthetic negative id; full table: the weakest entry is evicted, as for sight).</item>
        /// </list>
        /// Returns the slot's id. ⛔ Never an identity: the source is not known here, only "something about here".
        /// </summary>
        public static long HearContact(ref TargetMemory mem, float x, float y, float z, float radius, byte sourceClass,
                                       float scoreBoost, uint tick)
        {
            radius = radius > 0f ? radius : 1f;
            int best = -1; float bestD2 = float.MaxValue;
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.Anonymous[i] != 0) continue;
                float dx = mem.PositionsX[i] - x, dy = mem.PositionsY[i] - y, d2 = dx * dx + dy * dy;
                if (d2 <= radius * radius && d2 < bestD2) { best = i; bestD2 = d2; }
            }
            if (best >= 0)
            {
                mem.Freshness[best] += scoreBoost;
                mem.LastSeenTick[best]  = tick;
                mem.Modalities[best]   |= (byte)SensorModality.Acoustic;
                if (mem.SourceClass[best] == 0) mem.SourceClass[best] = sourceClass;
                long known = mem.EntityIds[best];
                Sort(ref mem);
                return known;
            }

            best = -1; bestD2 = float.MaxValue;
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.Anonymous[i] == 0 || !ClassesCompatible(mem.SourceClass[i], sourceClass)) continue;
                float dx = mem.PositionsX[i] - x, dy = mem.PositionsY[i] - y, d2 = dx * dx + dy * dy;
                float reach = mem.Radius[i] + radius;
                if (d2 <= reach * reach && d2 < bestD2) { best = i; bestD2 = d2; }
            }
            if (best >= 0)
            {
                float r1 = mem.Radius[best], w1 = 1f / (r1 * r1), w2 = 1f / (radius * radius), w = w1 + w2;
                mem.PositionsX[best]   = (mem.PositionsX[best] * w1 + x * w2) / w;
                mem.PositionsY[best]   = (mem.PositionsY[best] * w1 + y * w2) / w;
                mem.PositionsZ[best]   = (mem.PositionsZ[best] * w1 + z * w2) / w;
                mem.Radius[best]       = 1f / System.MathF.Sqrt(w);
                mem.Freshness[best] += scoreBoost;
                mem.LastSeenTick[best]  = tick;
                if (mem.SourceClass[best] == 0) mem.SourceClass[best] = sourceClass;
                long fused = mem.EntityIds[best];
                Sort(ref mem);
                return fused;
            }

            long id = -(long)(++mem.AnonymousSerial);
            AddOrUpdateTarget(ref mem, id, x, y, scoreBoost, tick, SensorModality.Acoustic, z);
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.EntityIds[i] != id) continue;
                mem.Anonymous[i]   = 1;
                mem.Radius[i]      = radius;
                mem.SourceClass[i] = sourceClass;
                break;
            }
            return id;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3063</c> — a sense confirmed <paramref name="entityId"/> at (<paramref name="x"/>, <paramref name="y"/>): an
        /// anonymous contact whose circle holds that position and whose class is compatible with what the entity can sound
        /// like (<paramref name="entityClasses"/>, any match; empty = unknown) BECOMES it — no duplicate. Its freshness is kept
        /// (the higher of the two when the entity already has a slot). Returns true when one was absorbed.
        /// </summary>
        public static bool AbsorbBySighting(ref TargetMemory mem, long entityId, float x, float y, System.ReadOnlySpan<byte> entityClasses)
        {
            int anon = -1; float bestD2 = float.MaxValue;
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.Anonymous[i] == 0) continue;
                byte c = mem.SourceClass[i];
                bool compatible = c == 0 || entityClasses.Length == 0;
                for (int k = 0; !compatible && k < entityClasses.Length; k++) compatible = ClassesCompatible(c, entityClasses[k]);
                if (!compatible) continue;
                float dx = mem.PositionsX[i] - x, dy = mem.PositionsY[i] - y, d2 = dx * dx + dy * dy;
                if (d2 <= mem.Radius[i] * mem.Radius[i] && d2 < bestD2) { anon = i; bestD2 = d2; }
            }
            if (anon < 0) return false;

            int known = -1;
            for (int i = 0; i < mem.Count; i++) if (mem.Anonymous[i] == 0 && mem.EntityIds[i] == entityId) { known = i; break; }
            if (known >= 0)
            {
                if (mem.Freshness[anon] > mem.Freshness[known]) mem.Freshness[known] = mem.Freshness[anon];
                mem.Modalities[known] |= mem.Modalities[anon];
                if (mem.SourceClass[known] == 0) mem.SourceClass[known] = mem.SourceClass[anon];
                Forget(ref mem, anon);
            }
            else
            {
                mem.EntityIds[anon] = entityId;
                mem.Anonymous[anon] = 0;
                mem.Radius[anon]    = 0f;
                mem.PositionsX[anon] = x;
                mem.PositionsY[anon] = y;
                mem.ChangeEpoch++;
            }
            Sort(ref mem);
            return true;
        }

        /// <summary>
        /// ⭐ CE-3046 — removes entry <paramref name="slot"/>; the entries after it move up, so the table stays sorted.
        /// </summary>
        public static void Forget(ref TargetMemory mem, int slot)
        {
            if ((uint)slot >= (uint)mem.Count) return;
            for (int i = slot; i < mem.Count - 1; i++)
            {
                mem.EntityIds[i]    = mem.EntityIds[i + 1];
                mem.PositionsX[i]   = mem.PositionsX[i + 1];
                mem.PositionsY[i]   = mem.PositionsY[i + 1];
                mem.PositionsZ[i]   = mem.PositionsZ[i + 1];
                mem.Freshness[i] = mem.Freshness[i + 1];
                mem.LastSeenTick[i] = mem.LastSeenTick[i + 1];
                mem.Modalities[i]   = mem.Modalities[i + 1];
                mem.Anonymous[i]    = mem.Anonymous[i + 1];
                mem.Radius[i]       = mem.Radius[i + 1];
                mem.SourceClass[i]  = mem.SourceClass[i + 1];
            }
            mem.Count--;
            mem.ChangeEpoch++;
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
    [DataPolicy(DataPolicy.NoScenario)]
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
    [DataPolicy(DataPolicy.NoScenario)]
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

        /// <summary>⭐ <c>CE-3060</c> — the <see cref="SensorModality"/> kinds that hold each target (OR); feeds
        /// <c>TargetMemory.Modalities</c> instead of an assumed Visual.</summary>
        public fixed byte Modalities[PerceptionConstants.MaxTrackedTargets];
    }
}
