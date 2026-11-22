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
    [ComponentId(GlobalComponentIds.PerceptionReceptor)]
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
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
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
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

        /// <summary>⭐ <c>CE-2474</c> (Sensing §8.6d) — the TOKEN of the last sound heard into the slot (the emission's serial; 0 = none).
        /// ⛔ Never the source: it says "this is the bang I heard", so the squad can fold everyone's hearing of one bang into one contact.</summary>
        public fixed long SoundIds[PerceptionConstants.MaxTrackedTargets];

        /// <summary>The last synthetic serial given to an anonymous contact (its id is <c>-serial</c>), so a behaviour can
        /// follow ONE heard contact across ticks.</summary>
        public int AnonymousSerial;

        /// <summary>⭐ <c>CE-2326</c> (R-3030; DESIGN_Peek_And_Fire §11.6a) — the LOGICAL stance he was in when last SEEN, as
        /// <c>StanceId + 1</c>; 0 = unknown (heard only, or never seen standing still). A blind burst at the spot is aimed as at that
        /// body (<see cref="StanceSeen"/>).</summary>
        public fixed byte SeenStance[PerceptionConstants.MaxTrackedTargets];

        /// <summary>⭐ <c>R-3039</c> S1 — the unit's OWN health as <c>ThreatEvaluationSystem</c> last saw it, for the Hit edge (valid when
        /// <see cref="HasLastHealth"/> is 1). Was a field of the system: a checkpoint restored into a fresh system lost it, and a hit in the first
        /// step after the restore raised no Hit. 📄 docs/DESIGN_System_State_Inventory.md §6 #6.</summary>
        public float LastHealth;
        public byte HasLastHealth;

        /// <summary>⭐ <c>CE-2326</c> — the stance slot <paramref name="slot"/> was last seen in, or null when unknown.</summary>
        public static Fdp.Toolkit.Tkb.Domain.StanceId? StanceSeen(in TargetMemory mem, int slot)
        {
            fixed (byte* s = mem.SeenStance)
                return (uint)slot < (uint)mem.Count && s[slot] != 0 ? (Fdp.Toolkit.Tkb.Domain.StanceId)(s[slot] - 1) : null;
        }

        // ⛔ CE-2238 part 4 — the KNOWN COMBATANTS list moved OUT of this component to Fdp.Toolkit.Combat.KnownCombatantList: the
        //    ThreatEvaluationSystem copies this memory and writes the copy back through the command buffer, which erased a mark made
        //    later in the same frame (measured: ua-armed-blend-back). 📄 docs/DESIGN_Civilians_And_Crowd_Control.md §5h.6 KC-1.

        /// <summary>⭐ The ONE rule every reader calls: true when slot <paramref name="slot"/> is a heard, unidentified
        /// contact. ⛔ An ENTITY reader (aim, fire, entity reads) skips it; a POSITION reader may use it.</summary>
        public static bool IsAnonymous(in TargetMemory mem, int slot)
        {
            fixed (byte* a = mem.Anonymous) return (uint)slot < (uint)mem.Count && a[slot] != 0;
        }

        /// <summary>⭐ <c>CE-2348</c> ③ (R-2071 E-1, Sensing §8.6b) — the slot is a heard BURST: anonymous, sounding like an explosion. A
        /// burst says where a round LANDED, never where an enemy is: every "is an enemy here?" reader skips it; the alert readers and
        /// hide-from-a-point keep it.</summary>
        public static bool IsBurst(in TargetMemory mem, int slot)
        {
            fixed (byte* c = mem.SourceClass) return IsAnonymous(in mem, slot) && c[slot] == BurstClass;
        }

        /// <summary>⭐ <c>CE-4666</c> (D-8, user-approved; Sensing §8.6c) — the slot is a heard contact whose every hearing came FROM an opening or a
        /// corner on its way (AQ106 F / K): its point is where the sound CAME IN, not where its source is. Like a burst (<see cref="IsBurst"/>), it says
        /// where to LOOK, never where an enemy IS: the squad's SPOT leaves it out of an element's position; the man's own reactions keep it.</summary>
        public static bool IsHeardThroughAnOpening(in TargetMemory mem, int slot)
        {
            fixed (byte* a = mem.Anonymous) return (uint)slot < (uint)mem.Count && a[slot] == HeardThroughAnOpening;
        }

        /// <summary>⭐ <c>CE-4666</c> — the <see cref="Anonymous"/> values: heard straight (1) / only through an opening or round a corner (2).
        /// ⛔ Every "is it heard?" reader tests <c>!= 0</c>; only <see cref="IsHeardThroughAnOpening"/> tells the two apart.</summary>
        public const byte HeardStraight = 1, HeardThroughAnOpening = 2;

        /// <summary>The class a burst carries — <c>Tkb.Domain.SoundSourceClass.Explosion</c>.</summary>
        public const byte BurstClass = (byte)Fdp.Toolkit.Tkb.Domain.SoundSourceClass.Explosion;

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
            float posZ = 0f,
            byte seenStance = 0)
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
                if (seenStance != 0) mem.SeenStance[foundSlot] = seenStance;   // ⭐ CE-2326 — 0 keeps the last one seen
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
                mem.SoundIds[slot]     = 0;
                mem.SeenStance[slot]   = seenStance;
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
                    mem.SoundIds[lowestIdx]     = 0;
                    mem.SeenStance[lowestIdx]   = seenStance;
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
                long   sndTmp   = mem.SoundIds[i];
                byte   stTmp    = mem.SeenStance[i];

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
                    mem.SoundIds[j + 1]     = mem.SoundIds[j];
                    mem.SeenStance[j + 1]   = mem.SeenStance[j];
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
                mem.SoundIds[j + 1]     = sndTmp;
                mem.SeenStance[j + 1]   = stTmp;
            }
        }

        /// <summary>⭐ <c>CE-2486</c> (Sensing §8.6e; 📐 pe-house-shot) — a sound heard within its circle PLUS this (m) of a man he already KNOWS is that man: the
        /// memory holds where he was last SEEN, and a man moves a few steps between sightings. 📐 the ear 12 m from the shooter, sure to 1.1 m, heard him 2.7 m
        /// from where he had last seen him and held him twice. ⚠ Two enemies closer than this are heard as one — a buddy pair, as a listener would.</summary>
        public const float KnownMoveSlackMetres = 3f;

        /// <summary>⭐ <c>CE-2492</c> — how fast a man only heard may have moved since he was last heard (m/s): a brisk walk. Caps at <see cref="KnownMoveSlackMetres"/>.</summary>
        public const float WalkMetresPerSecond = 1.5f;

        /// <summary>⭐ <c>CE-2492</c> — how far apart in time two bangs heard in ONE step may be (s): the acoustic solve delivers every bang since the last
        /// at once (📐 the batch's ages spanned 0.02–0.30 s). The walking slack counts at least this long.</summary>
        public const float HeardBatchSeconds = 0.5f;

        /// <summary>⭐ <c>CE-2477</c> (Sensing §8.6e) — the tightest a fused heard circle may get, as a share of ONE hearing's circle: a lone listener
        /// localises a sound to a few degrees of bearing and judges range only by loudness, so however many shots he hears the estimate does not
        /// sharpen past ~a third of one hearing's circle (≈ 3° at the 0.15 × distance circle). 📐 pe-seen-shooter: 146 fused shots took 26 m to
        /// 2.15 m at 177 m, and the shooter, SEEN 2.76 m off, no longer absorbed his own heard contact — one man, two contacts.</summary>
        public const float FusedRadiusFloor = 1f / 3f;

        /// <summary>⭐ <c>CE-2477</c> — the inverse-variance radius of a fusion of total weight <paramref name="weight"/>, floored at
        /// <see cref="FusedRadiusFloor"/> × the newest hearing's <paramref name="radius"/>.</summary>
        public static float FusedRadius(float weight, float radius)
            => System.MathF.Max(1f / System.MathF.Sqrt(weight), radius * FusedRadiusFloor);

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
                                       float scoreBoost, uint tick, byte via = 0, long sound = 0, float stepSeconds = 1f / 60f)
        {
            radius = radius > 0f ? radius : 1f;
            bool burst = sourceClass == BurstClass;   // ⭐ R-2071 E-1 — a burst refreshes no known contact and fuses only with a burst
            int best = -1; float bestD2 = float.MaxValue;
            // ⭐ CE-2486 — he may have moved a few steps since he was last seen. ⭐ CE-2489 — a STRAIGHT hearing only: a point heard round an opening is the WAY
            //   to him (D-8), and folding it into his stale spot loses the route (📐 ia-house-hunt: the stairwell, 3.9 m from where the defender was seen upstairs)
            float knownReach = via == 0 ? radius + KnownMoveSlackMetres : radius;
            for (int i = 0; !burst && i < mem.Count; i++)
            {
                if (mem.Anonymous[i] != 0) continue;
                float dx = mem.PositionsX[i] - x, dy = mem.PositionsY[i] - y, d2 = dx * dx + dy * dy;
                if (d2 <= knownReach * knownReach && d2 < bestD2) { best = i; bestD2 = d2; }
            }
            if (best >= 0)
            {
                mem.Freshness[best] += scoreBoost;
                mem.LastSeenTick[best]  = tick;
                mem.Modalities[best]   |= (byte)SensorModality.Acoustic;
                if (mem.SourceClass[best] == 0) mem.SourceClass[best] = sourceClass;
                if (sound != 0) mem.SoundIds[best] = sound;   // ⭐ CE-2474
                // ⭐ CE-2488 — heard OUTSIDE the circle of where he was seen: he has moved, and the ear says where to (📐 ia-house-hunt: CE-2486 kept the stale
                //   spot, a room away, and the hunt went there). ⛔ Not a point heard round an opening — that is where to look, not where he is (D-8)
                if (via == 0 && bestD2 > radius * radius)
                {
                    mem.PositionsX[best] = x; mem.PositionsY[best] = y; mem.PositionsZ[best] = z;
                }
                long known = mem.EntityIds[best];
                Sort(ref mem);
                return known;
            }

            best = -1; bestD2 = float.MaxValue;
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.Anonymous[i] == 0 || !ClassesCompatible(mem.SourceClass[i], sourceClass)) continue;
                if (burst != (mem.SourceClass[i] == BurstClass)) continue;
                float dx = mem.PositionsX[i] - x, dy = mem.PositionsY[i] - y, d2 = dx * dx + dy * dy;
                float reach = mem.Radius[i] + radius;
                // ⭐ CE-2492 — a man only HEARD may have WALKED since he was last heard (📐 pe-wall-and-door: a shooter walking to his target, 1.6 m
                //   between two shots, 0.7 m circles ⇒ held twice). Straight hearings only (a point round an opening is the way in, CE-2489), never a burst
                //   ⭐ at least HeardBatchSeconds: one acoustic solve delivers every bang since the last, in ONE step (📐 P2b: bangs 86 and 88 in the same
                //   step, 1.6 m apart — the elapsed steps were 0)
                if (!burst && via == 0 && mem.Anonymous[i] == HeardStraight && tick >= mem.LastSeenTick[i])
                    reach += MathF.Min(KnownMoveSlackMetres, WalkMetresPerSecond * MathF.Max(HeardBatchSeconds, (tick - mem.LastSeenTick[i]) * stepSeconds));
                if (d2 <= reach * reach && d2 < bestD2) { best = i; bestD2 = d2; }
            }
            if (best >= 0)
            {
                float r1 = mem.Radius[best], w1 = 1f / (r1 * r1), w2 = 1f / (radius * radius), w = w1 + w2;
                mem.PositionsX[best]   = (mem.PositionsX[best] * w1 + x * w2) / w;
                mem.PositionsY[best]   = (mem.PositionsY[best] * w1 + y * w2) / w;
                mem.PositionsZ[best]   = (mem.PositionsZ[best] * w1 + z * w2) / w;
                mem.Radius[best]       = FusedRadius(w, radius);   // ⭐ CE-2477 — never surer than a lone ear can be
                mem.Freshness[best] += scoreBoost;
                mem.LastSeenTick[best]  = tick;
                if (mem.SourceClass[best] == 0) mem.SourceClass[best] = sourceClass;
                if (via == 0) mem.Anonymous[best] = HeardStraight;   // ⭐ CE-4666 (D-8) — one straight hearing makes the fused point a source estimate
                if (sound != 0) mem.SoundIds[best] = sound;   // ⭐ CE-2474
                long fused = mem.EntityIds[best];
                Sort(ref mem);
                return fused;
            }

            long id = -(long)(++mem.AnonymousSerial);
            AddOrUpdateTarget(ref mem, id, x, y, scoreBoost, tick, SensorModality.Acoustic, z);
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.EntityIds[i] != id) continue;
                mem.Anonymous[i]   = via != 0 ? HeardThroughAnOpening : HeardStraight;   // ⭐ CE-4666 (D-8)
                mem.Radius[i]      = radius;
                mem.SourceClass[i] = sourceClass;
                mem.SoundIds[i]    = sound;   // ⭐ CE-2474
                break;
            }
            return id;
        }

        /// <summary>⭐ <c>CE-2474</c> (Sensing §8.6d ②) — how far off a seen man's bearing a heard shot may come and still be HIS (degrees).</summary>
        public const float AttributeDegrees = 12f;

        /// <summary>⭐ <c>CE-2484</c> — the same for a shot heard THROUGH AN OPENING (D-8): its point is the way in, so the seen man must stand BEYOND it (farther
        /// than the opening) and within this of its bearing — the sound bent round the opening's edge. 📐 pe-house-shot: the east-room ear saw the shooter
        /// through the hall door and heard his shots via the stairwell 17° off — one man, held as two.</summary>
        public const float AttributeThroughDegrees = 45f;

        /// <summary>
        /// ⭐⭐ <c>CE-2474</c> (Sensing §8.6d ②; 🔒 user: "Can't we somehow assign the sound position to the seen position of the enemy?") — a
        /// listener at (<paramref name="fromX"/>, <paramref name="fromY"/>) heard a SHOT whose estimate lies at (<paramref name="x"/>, <paramref name="y"/>):
        /// when an identified contact he SEES NOW (<paramref name="seenNow"/>) stands within <see cref="AttributeDegrees"/> of that bearing, the shot is
        /// HIS — that slot is refreshed (Acoustic added, the sound's token stamped) instead of a new anonymous slot. The nearest bearing wins.
        /// ⛔ Not for a footstep or an engine (only a shot is sudden and directional), never for a burst (R-2071: where a round LANDED). ⭐ CE-2484 — a hearing
        /// through an opening or round a corner (D-8: its point is the way in) is attributed only to a seen man BEYOND that point, within
        /// <see cref="AttributeThroughDegrees"/>.
        /// Returns true when it attributed.
        /// </summary>
        public static bool AttributeToSeen(ref TargetMemory mem, float fromX, float fromY, float x, float y, byte kind, byte sourceClass, byte via,
                                           System.ReadOnlySpan<long> seenNow, float scoreBoost, uint tick, long sound)
        {
            if (kind != ShotKind || sourceClass == BurstClass || seenNow.IsEmpty) return false;
            float hx = x - fromX, hy = y - fromY, hd2 = hx * hx + hy * hy;
            if (hd2 < 1f) return false;   // an estimate on top of the listener has no bearing
            float hd = System.MathF.Sqrt(hd2);
            // ⭐ CE-2484 — heard through an opening: the shooter is BEYOND the opening, within a wider cone (the sound bent round its edge)
            float cosMax = System.MathF.Cos((via != 0 ? AttributeThroughDegrees : AttributeDegrees) * (System.MathF.PI / 180f));
            int best = -1; float bestCos = cosMax;
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.Anonymous[i] != 0) continue;
                long id = mem.EntityIds[i];
                bool seen = false;
                for (int k = 0; k < seenNow.Length && !seen; k++) seen = seenNow[k] == id;
                if (!seen) continue;
                float sx = mem.PositionsX[i] - fromX, sy = mem.PositionsY[i] - fromY, sd2 = sx * sx + sy * sy;
                if (sd2 < 1e-4f) continue;
                if (via != 0 && sd2 < hd2) continue;   // ⭐ CE-2484 — nearer than the opening: not the man beyond it
                float cos = (hx * sx + hy * sy) / (hd * System.MathF.Sqrt(sd2));
                if (cos >= bestCos) { best = i; bestCos = cos; }
            }
            if (best < 0) return false;
            CreditShot(ref mem, best, sourceClass, scoreBoost, tick, sound);
            return true;
        }

        /// <summary>⭐ <c>CE-2490</c> — how long after a bang its flash, seen, still binds it (s): the sound and the flash are one instant; this is the
        /// engine's own lag (the acoustic solve, the sighting's hysteresis), not a human's.</summary>
        public const double FlashBindSeconds = 1.0;

        /// <summary>
        /// ⭐⭐ <c>CE-2490</c> (Sensing §8.6g; 🔒 user: "its the timing that binds the visual muzzle flash with the sound from side doors, so it should be
        /// perceived as single guys") — a listener heard a SHOT fired by <paramref name="shooter"/>, and he SEES that man now (<paramref name="seenNow"/>):
        /// he saw the muzzle flash at the moment of the bang, so the shot is that man's WHATEVER WAY THE SOUND CAME — round a door, up a stair. That slot is
        /// credited (Acoustic added, the sound's token stamped) instead of a new anonymous slot. ⛔ Only through sight: a shooter he does not see stays
        /// anonymous (§6 D). Not a burst (where a round LANDED, R-2071). Returns true when it attributed.
        /// </summary>
        public static bool AttributeBySeenFlash(ref TargetMemory mem, long shooter, byte kind, byte sourceClass,
                                                System.ReadOnlySpan<long> seenNow, float scoreBoost, uint tick, long sound)
        {
            if (kind != ShotKind || sourceClass == BurstClass || shooter <= 0) return false;
            bool seen = false;
            for (int k = 0; k < seenNow.Length && !seen; k++) seen = seenNow[k] == shooter;
            if (!seen) return false;
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.Anonymous[i] != 0 || mem.EntityIds[i] != shooter) continue;
                CreditShot(ref mem, i, sourceClass, scoreBoost, tick, sound);
                return true;
            }
            return false;
        }

        private static void CreditShot(ref TargetMemory mem, int slot, byte sourceClass, float scoreBoost, uint tick, long sound)
        {
            mem.Freshness[slot]   += scoreBoost;
            mem.LastSeenTick[slot] = tick;
            mem.Modalities[slot]  |= (byte)SensorModality.Acoustic;
            if (mem.SourceClass[slot] == 0) mem.SourceClass[slot] = sourceClass;
            if (sound != 0) mem.SoundIds[slot] = sound;
            Sort(ref mem);
        }

        /// <summary><c>Signatures.SoundKind.Shot</c>.</summary>
        public const byte ShotKind = 2;

        /// <summary>
        /// ⭐⭐ <c>CE-3063</c> — a sense confirmed <paramref name="entityId"/> at (<paramref name="x"/>, <paramref name="y"/>): an
        /// anonymous contact whose circle holds that position and whose class is compatible with what the entity can sound
        /// like (<paramref name="entityClasses"/>, any match; empty = unknown) BECOMES it — no duplicate. Its freshness is kept
        /// (the higher of the two when the entity already has a slot). Returns true when one was absorbed.
        /// </summary>
        public static bool AbsorbBySighting(ref TargetMemory mem, long entityId, float x, float y, System.ReadOnlySpan<byte> entityClasses,
                                            bool listenerKnown = false, float listenerX = 0f, float listenerY = 0f)
        {
            float cosThrough = System.MathF.Cos(AttributeThroughDegrees * (System.MathF.PI / 180f));
            float sx = x - listenerX, sy = y - listenerY, sd2 = sx * sx + sy * sy;
            int anon = -1; float bestD2 = float.MaxValue;
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.Anonymous[i] == 0) continue;
                byte c = mem.SourceClass[i];
                if (c == BurstClass) continue;   // ⭐ R-2071 E-1 — the man seen there did not make the bang
                bool compatible = c == 0 || entityClasses.Length == 0;
                for (int k = 0; !compatible && k < entityClasses.Length; k++) compatible = ClassesCompatible(c, entityClasses[k]);
                if (!compatible) continue;
                float dx = mem.PositionsX[i] - x, dy = mem.PositionsY[i] - y, d2 = dx * dx + dy * dy;
                // ⭐ CE-2487 — CE-2486's slack, the other way round: heard first, seen a few steps from the point (📐 pe-house-shot, 0.9 m off a 0.7 m circle)
                float reach = mem.Radius[i] + KnownMoveSlackMetres;
                bool inside = d2 <= reach * reach;
                // ⭐ CE-2485 — a contact heard THROUGH AN OPENING (D-8: its point is the way in) is his when he stands BEYOND that point, within
                //   AttributeThroughDegrees of its bearing from the listener (📐 uf-corner-assault: steps heard round the corner, the man then seen behind it,
                //   held as two enemies — the attack stopped at 23.9 s; pe-house-shot: heard via the stairwell, seen through the door)
                if (!inside && listenerKnown && mem.Anonymous[i] == HeardThroughAnOpening)
                {
                    float hx = mem.PositionsX[i] - listenerX, hy = mem.PositionsY[i] - listenerY, hd2 = hx * hx + hy * hy;
                    inside = hd2 >= 1f && sd2 >= hd2 && (hx * sx + hy * sy) >= cosThrough * System.MathF.Sqrt(hd2 * sd2);
                }
                if (inside && d2 < bestD2) { anon = i; bestD2 = d2; }
            }
            if (anon < 0) return false;

            int known = -1;
            for (int i = 0; i < mem.Count; i++) if (mem.Anonymous[i] == 0 && mem.EntityIds[i] == entityId) { known = i; break; }
            if (known >= 0)
            {
                if (mem.Freshness[anon] > mem.Freshness[known]) mem.Freshness[known] = mem.Freshness[anon];
                mem.Modalities[known] |= mem.Modalities[anon];
                if (mem.SourceClass[known] == 0) mem.SourceClass[known] = mem.SourceClass[anon];
                if (mem.SoundIds[anon] != 0) mem.SoundIds[known] = mem.SoundIds[anon];   // ⭐ CE-2474 — the bang is now his
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
                mem.SoundIds[i]     = mem.SoundIds[i + 1];
                mem.SeenStance[i]   = mem.SeenStance[i + 1];   // ⭐ CE-2326
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
    [ComponentId(GlobalComponentIds.SensorContactList)]
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

        /// <summary>⭐ <c>CE-3260</c> (BR-7, §5.7 AQ-5) — when the current unbroken run of sightings began (sim s): a man unseen longer than
        /// the lost threshold starts a new run.</summary>
        public fixed float VisibleSince[PerceptionConstants.MaxTrackedTargets];

        /// <summary>⭐ <c>CE-3260</c> (AQ-1/AQ-2) — how long he must stay seen before he is acquired (s), from the latest sighting's range,
        /// exposure and the observer's proficiency. 0 ⇒ the tick he is seen (a fully exposed man within 100 m).</summary>
        public fixed float AcquireAfter[PerceptionConstants.MaxTrackedTargets];

        /// <summary>
        /// Updates the last-seen tick for an existing contact, or adds a new
        /// <see cref="SensorContactState.Pending"/> slot if the entity is not yet tracked.
        /// Does nothing if the list is full and the entity is not already tracked.
        /// </summary>
        public static void UpdateSighting(ref SensorContactList list, long entityId, uint tick) => UpdateSighting(ref list, entityId, tick, 0f, 0f);

        /// <summary>⭐ <c>CE-3260</c> — <see cref="UpdateSighting(ref SensorContactList, long, uint)"/> with the time and the dwell this
        /// sighting needs (<see cref="AcquireAfter"/>); a sighting after a silence longer than the lost threshold starts a new run.</summary>
        public static void UpdateSighting(ref SensorContactList list, long entityId, uint tick, float now, float acquireAfter)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list.EntityIds[i] == entityId)
                {
                    if (tick - list.LastSeenTick[i] > Fdp.Toolkit.Perception.Systems.ContactHysteresis.TrackLostThresholdTicks) list.VisibleSince[i] = now;
                    list.LastSeenTick[i] = tick;
                    list.AcquireAfter[i] = acquireAfter;
                    return;
                }
            }

            if (list.Count < PerceptionConstants.MaxTrackedTargets)
            {
                int idx = list.Count++;
                list.EntityIds[idx]    = entityId;
                list.LastSeenTick[idx] = tick;
                list.State[idx]        = (byte)SensorContactState.Pending;
                list.VisibleSince[idx] = now;
                list.AcquireAfter[idx] = acquireAfter;
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
    [ComponentId(GlobalComponentIds.ActiveSensorTracks)]
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
