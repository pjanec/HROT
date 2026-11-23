using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Perception.Systems
{
    /// <summary>
    /// Brain-tier threat evaluation -- runs on the CGF node inside
    /// <c>CgfThreatEvaluationSystem</c> (via <c>CgfLogicPack</c>).
    ///
    /// <para>
    /// Each frame this system does two things:
    /// <list type="number">
    ///   <item>
    ///     <b>Decay:</b> Multiplies every existing threat score in every
    ///     <see cref="TargetMemory"/> by <c>1 - dt x ThreatScoreDecayPerSecond</c>,
    ///     providing smooth temporal forgetting.
    ///   </item>
    ///   <item>
    ///     <b>Edges</b> (CE-3039): publishes <see cref="SensorChangedEvent"/> FirstThreat / AllClear when the memory
    ///     fills from empty or empties, and Hit when the unit's health drops.
    ///   </item>
    ///   <item>
    ///     <b>Forget</b> (CE-3046): drops an entry whose target is gone, or whose score faded below
    ///     <see cref="PerceptionConstants.ForgetThreatScore"/> while no sensor tracks it.
    ///   </item>
    ///   <item>
    ///     <b>Boost:</b> For entities that also carry an <see cref="ActiveSensorTracks"/>
    ///     cognitive buffer (written by <c>SensorTrackStateIngressTranslator</c>),
    ///     calls <see cref="TargetMemory.AddOrUpdateTarget"/> for each acquired track,
    ///     applying a continuous <c>50 x deltaTime</c> score boost per second.
    ///   </item>
    /// </list>
    /// ⭐ CE-2429 — on the live world the memory is written in place (the brains read it this frame, and a same-frame seed survives);
    /// on a read-only snapshot view it goes through <c>view.GetCommandBuffer().SetComponent&lt;TargetMemory&gt;</c>.
    /// </para>
    /// <para>
    /// <b>Read-modify-write contract:</b>
    /// <list type="bullet">
    ///   <item>Read: <c>view.GetComponentRO&lt;TargetMemory&gt;</c> reads from the snapshot.</item>
    ///   <item>Modify: local copy mutated in-memory (no shared state).</item>
    ///   <item>Write: <c>ecb.SetComponent&lt;TargetMemory&gt;</c> enqueues the update.</item>
    ///   <item>Flush: ECB is replayed on the main thread after module execution completes.</item>
    /// </list>
    /// </para>
    /// </summary>
    [TickMode(TickPolicy.WhenTimeAdvances)]
    [Fdp.Core.NotState("a system/module, classified on its own")]
    public class ThreatEvaluationSystem : IEcsModuleSystem
    {
        // CE-3039 — each perceiving unit's health as last seen, for the Hit edge: ⭐ R-3039 S1 on the unit's TargetMemory (LastHealth), not
        //   here — a checkpoint restored into a fresh system carries it on.
        // ⭐ CE-2233 (CIV-0) — last frame's THREAT count per unit: the side is read live, so a neutral that turns Hostile changes the
        //   count without changing the memory — the edge needs the previous frame's answer, as Hit needs the previous health.
        [Fdp.Core.WorldDerived("empty falls back to ThreatCount of the restored memory (:203) — same edge")]
        private System.Collections.Generic.Dictionary<Entity, int> _lastThreats = new();
        [Fdp.Core.WorldDerived("swap buffer cleared each tick")]
        private System.Collections.Generic.Dictionary<Entity, int> _nextThreats = new();

        // ⭐ CE-3063 — this frame's heard contacts, grouped by the unit that heard them.
        [Fdp.Core.WorldDerived("this frame's heard contacts, cleared each tick")]
        private readonly System.Collections.Generic.Dictionary<Entity, System.Collections.Generic.List<SoundContactEvent>> _heard = new();
        // ⭐ CE-3293 — the world these maps describe. A reload clears the world and the next run's units come back under the same
        //   handles (SoftClear restarts the generations), so last run's counts would read as this run's: the first step published
        //   AllClear for a tank that had never seen anything (measured live, ua-tank-ammo, every reload but the first).
        private int _epoch;
        // ⭐ CE-2490 — this tick's sound ring (who fired each bang): read only when the listener SEES that man (the muzzle flash)
        [Fdp.Core.WorldDerived("a reference to the node's sound ring singleton, re-read each tick")]
        private Fdp.Toolkit.Perception.Signatures.SoundEmissionRing? _ring;

        /// <inheritdoc/>
        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _epoch))
            {
                _lastThreats.Clear(); _nextThreats.Clear(); _heard.Clear();
            }
            var ecb  = view.GetCommandBuffer();
            uint tick = PerceptionClock.Now(view);   // ⭐ CE-3292 — LastSeenTick is stamped in sim steps
            double now = view is EntityRepository nowRepo && nowRepo.HasSingleton<GlobalTime>() ? nowRepo.GetSingleton<GlobalTime>().TotalTime : 0d;   // ⭐ CE-2371 — the Alerted leases
            // ⭐ CE-2490 — the node's sound ring, read once here (serial) so the parallel batches never look the singleton up
            _ring = Fdp.Toolkit.Perception.Signatures.SoundEmissionRing.In(view);
            // ⭐ CE-3063 — what each unit HEARD this frame (anonymous estimates; R-205). Lists are reused across frames.
            foreach (var list in _heard.Values) list.Clear();
            if (view is EntityRepository heardRepo && heardRepo.Bus.IsRegistered<SoundContactEvent>())
                foreach (ref readonly var heard in view.ReadEvents<SoundContactEvent>())
                {
                    if (!_heard.TryGetValue(heard.Observer, out var forUnit)) _heard[heard.Observer] = forUnit = new();
                    forUnit.Add(heard);
                }

            // Iterate all entities that have TargetMemory: apply decay and optional boost.
            _nextThreats.Clear();
            var memQuery = view.Query().With<TargetMemory>().Build();
            // ⭐ P-1 (DESIGN_Parallel_Simulation §5; the parallel experiment E-3) — on the LIVE world the per-unit evaluation runs in parallel
            //   batches: each unit writes only its own TargetMemory (in place); its edges go to its batch's command buffer, merged in batch
            //   order — the same order as this serial loop's buffer; its threat count to its own slot of _threatsOut (the dictionary is
            //   filled after, serially). ⛔ The snapshot path (not a live world) stays serial.
            if (Fdp.Core.Jobs.ParallelEntities.Enabled && view is EntityRepository liveRepo)
            {
                int n = 0;
                foreach (var entity in memQuery)
                {
                    if (n == _list.Length) System.Array.Resize(ref _list, System.Math.Max(64, _list.Length * 2));
                    _list[n++] = entity;
                }
                if (_threatsOut.Length < n) _threatsOut = new int[System.Math.Max(n, _threatsOut.Length * 2)];
                // ⭐ CE-4623 (found at AB-3) — the ranking reads its targets' side, place and health: the BRAINS' common read set (one list,
                //   BrainReads). Declared here too (idempotent), so this phase never leans on another phase having declared it first in the
                //   process — under HROT_READ_CHECK that was an order-dependent red (CE2233 alone, ThreatEvaluationParallelRail).
                Fdp.Toolkit.Behavior.BrainReads.Declare();
                Fdp.Core.Jobs.ParallelEntities.Run(liveRepo, _list, n, new EvaluateJob { Owner = this, Repo = liveRepo, DeltaTime = deltaTime, Tick = tick, Now = now });
                for (int i = 0; i < n; i++) _nextThreats[_list[i]] = _threatsOut[i];
            }
            else
            {
                foreach (var entity in memQuery)
                {
                    EvaluateOne(view, entity, deltaTime, tick, now, ecb, out int threatsNow);
                    _nextThreats[entity] = threatsNow;
                }
            }
            (_lastThreats, _nextThreats) = (_nextThreats, _lastThreats);
        }

        /// <summary>
        /// ⭐ CE-2233 (CIV-0) — how many of the memory's contacts are THREATS: a heard (anonymous) contact always is (a sound has no
        /// side); a seen one unless it is Neutral (<see cref="ThreatDanger.IsNeutral"/>, read live). What FirstThreat / AllClear count.
        /// </summary>

        // ⭐ P-1 — the parallel path's buffers (grown, never shrunk; 0 B per steady frame)
        [Fdp.Core.WorldDerived("scratch — the parallel batch list, refilled from 0 each tick (exp/perf)")]
        private Entity[] _list = new Entity[64];
        [Fdp.Core.WorldDerived("scratch — each batch entity's threat count, written by this tick's job, read back the same tick (exp/perf)")]
        private int[] _threatsOut = new int[64];

        private struct EvaluateJob : Fdp.Core.Jobs.IEntityBatchJob, Fdp.Core.Jobs.IStableReads
        {
            // ⭐ N-11 (D-6) — what the ranking reads on its targets that nothing writes while it runs (measured: the scale-rig survey)
            public void DeclareStableReads(Fdp.Core.Jobs.PhaseReadSet reads) => reads
                .Unmanaged<Fdp.Toolkit.Physics.Components.PhysicsCollider>()
                .Unmanaged<Fdp.Toolkit.Perception.Signatures.AcousticEmitter>()
                .Unmanaged<global::CarKinem.Core.VehicleParams>()
                .Unmanaged<Hrot.MuscleCharacter.Animation.Components.StanceIntent>()
                // ⭐ CE-4623 (found at AB-5) — is the target an aircraft (V8's air threat): its movement class, written at spawn only
                .Unmanaged<Fdp.Toolkit.Motion.Locomotion>()
                .Unmanaged<Fdp.Toolkit.Motion.Descent>()   // — and a man coming down a rope / under a canopy (Locomotions.Of reads it first)
                // ⭐ CE-4634 (found by dr-dragon-breath under HROT_READ_CHECK) — is the remembered aircraft ON THE GROUND (AirContacts.IsAir → FlightGround):
                //   its FlightState, written by its motion model, never inside this phase
                .Unmanaged<Fdp.Toolkit.Motion.FlightState>()
                // ⭐ CE-3453 (found by the animal feature scenarios) — is the track an animal (a calm one is no contact) or an animal AREA (never one)
                .Unmanaged<Fdp.Toolkit.Animals.AnimalState>()
                .Unmanaged<Fdp.Toolkit.Animals.AnimalArea>();
            public ThreatEvaluationSystem Owner;
            public EntityRepository Repo;
            public float DeltaTime;
            public uint Tick;
            public double Now;
            public void Execute(Entity e, int index, Fdp.Core.Jobs.BatchContext ctx)
            {
                Owner.EvaluateOne(Repo, e, DeltaTime, Tick, Now, ctx.Commands, out int threatsNow);
                Owner._threatsOut[index] = threatsNow;   // this entity's own slot
            }
            public void ApplyDeferred(EntityRepository repo, in Fdp.Core.Jobs.Deferred d) { }
        }

        /// <summary>⭐ P-1 — ONE unit's evaluation (decay, boost from tracks, heard contacts, forget, the edges, the in-place write). Called by
        /// the serial loop and by the parallel batches alike: it reads <c>_heard</c> and <c>_lastThreats</c> only, writes only
        /// <paramref name="entity"/>'s memory, and sends its edges to <paramref name="ecb"/>.</summary>
        private unsafe void EvaluateOne(ISimulationView view, Entity entity, float deltaTime, uint tick, double now, Fdp.Interfaces.IEntityCommandBuffer ecb, out int threatsNow)
        {
            System.Span<byte> classBuffer = stackalloc byte[3];   // CE-3063 — a target's sound classes
            System.Span<long> seenNow = stackalloc long[PerceptionConstants.MaxTrackedTargets];   // ⭐ CE-2474 — who he SEES this tick
            int seenCount = 0;
            bool selfPlaced = view.HasComponent<SimTransform>(entity);   // ⭐ CE-2485 — the listener, for a heard-through point's bearing
            var selfAt = selfPlaced ? view.GetComponentRO<SimTransform>(entity).Position : default;

            ref readonly var memRO = ref view.GetComponentRO<TargetMemory>(entity);

            // Local copy so we can mutate without violating the snapshot contract.
            TargetMemory mem = memRO;
            int countBefore = mem.Count;

            // Decay all existing threat scores.
            float decayFactor = 1f - (deltaTime * PerceptionConstants.ThreatScoreDecayPerSecond);
            if (decayFactor < 0f) decayFactor = 0f;

            bool changed = false;
            for (int i = 0; i < mem.Count; i++)
            {
                float newScore = mem.Freshness[i] * decayFactor;
                if (newScore != mem.Freshness[i])
                {
                    mem.Freshness[i] = newScore;
                    changed = true;
                }
            }

            // Boost from ActiveSensorTracks (Brain cognitive buffer written by
            // SensorTrackStateIngressTranslator on the CGF node).
            if (view.HasComponent<ActiveSensorTracks>(entity))
            {
                ref readonly var tracksRO = ref view.GetComponentRO<ActiveSensorTracks>(entity);
                if (tracksRO.Count > 0)
                {
                    // Continuous boost: 50 threat-score units per second per active track.
                    float continuousBoost = PerceptionConstants.TrackBoostPerSecond * deltaTime;   // CE-3054: the one constant

                    for (int i = 0; i < tracksRO.Count; i++)
                    {
                        // Default to the cached entry position (ActiveSensorTracks is 2D; no
                        // cached altitude, so the flat fallback is Z = 0).
                        float posX = tracksRO.PositionsX[i];
                        float posY = tracksRO.PositionsY[i];
                        float posZ = 0f;
                        byte seenStance = 0;

                        // If we have a live replica of the target, track its real-time 3D
                        // position — including the authoritative altitude (P3D-206).
                        var targetEntity = new Entity((ulong)tracksRO.EntityIds[i]);
                        // ⭐ CE-3453 — a calm animal, or an animal AREA (an authored outline, no body), is not a contact: ⛔ they entered the
                        //   16-slot memory like any man, so on a farm the cows, the hens and the two area entities evicted every enemy as fast
                        //   as he entered — measured: a Red rifleman's memory held 16 animals and villagers and no Blue man, and nobody fired.
                        //   A DANGEROUS animal (the guard dog attacking) stays one: self-defence needs it in memory (AR-6).
                        if (Fdp.Toolkit.Animals.Animals.NotAContact(view, targetEntity, entity)) continue;   // ⭐ CE-4649 part D — game is a contact for the hunter whose rule names it
                        if (view.IsAlive(targetEntity) && view.HasComponent<SimTransform>(targetEntity))
                        {
                            ref readonly var targetTf = ref view.GetComponentRO<SimTransform>(targetEntity);
                            posX = targetTf.Position.X;
                            posY = targetTf.Position.Y;
                            posZ = targetTf.Position.Z;
                            // ⭐ CE-2326 (R-3030; Peek and Fire §11.6a) — and the LOGICAL stance he is in, so a blind burst at this spot
                            //   later is aimed as at that body. Only a SEEN contact: a heard or thermal-only track says nothing of his pose;
                            //   and only a PERSON (no hull — AimPoint.Middle's own test): a vehicle has no stance to remember.
                            if ((tracksRO.Modalities[i] == 0 || (tracksRO.Modalities[i] & (byte)SensorModality.Visual) != 0)
                                && Fdp.Toolkit.Physics.Components.PhysicsColliderReaders.HullHeight(view, targetEntity) <= 0f)
                                seenStance = (byte)(Hrot.MuscleCharacter.Animation.Components.LogicalStance.Of(view, targetEntity) + 1);
                        }

                        if (seenCount < seenNow.Length && (tracksRO.Modalities[i] == 0 || (tracksRO.Modalities[i] & (byte)SensorModality.Visual) != 0))
                            seenNow[seenCount++] = tracksRO.EntityIds[i];

                        // ⭐ CE-3063 — a sighting inside a heard contact's circle IS that contact: absorb it, no duplicate.
                        TargetMemory.AbsorbBySighting(ref mem, tracksRO.EntityIds[i], posX, posY, ClassesOf(view, targetEntity, classBuffer),
                                                      selfPlaced, selfAt.X, selfAt.Y);   // ⭐ CE-2485 — a heard-through point is his when he is beyond it

                        TargetMemory.AddOrUpdateTarget(
                            ref mem,
                            entityId:   tracksRO.EntityIds[i],
                            posX:       posX,
                            posY:       posY,
                            scoreBoost: continuousBoost,
                            tick:       tick,
                            // ⭐ CE-3060 — the kinds that actually hold the track (was: always Visual, so a thermal or heard
                            //   contact read as "in sight" — StandardInputs.HasLineOfSight).
                            modality:   tracksRO.Modalities[i] == 0 ? SensorModality.Visual : (SensorModality)tracksRO.Modalities[i],
                            posZ:       posZ,
                            seenStance: seenStance);
                    }
                    changed = true;
                }
            }

            // ⭐ CE-3063 — HEARD: refresh what is known there, else fuse with a compatible heard contact, else a new one.
            if (_heard.TryGetValue(entity, out var heardHere) && heardHere.Count > 0)
            {
                // ⭐ CE-2474 (§8.6d ②) — a shot from the bearing of a man he SEES is that man: no new anonymous contact
                bool placed = seenCount > 0 && view.HasComponent<SimTransform>(entity);
                var self = placed ? view.GetComponentRO<SimTransform>(entity).Position : default;
                foreach (var h in heardHere)
                {
                    // ⭐ CE-2490 (§8.6g) — he SAW that man fire: the flash and the bang are one instant, whatever way the sound came
                    if (seenCount > 0 && h.Sound != 0 && _ring != null && _ring.TryGetAtPlace(h.Sound, out var bang)
                        && now - bang.Time <= TargetMemory.FlashBindSeconds
                        && TargetMemory.AttributeBySeenFlash(ref mem, (long)bang.Source, h.Kind, h.SourceClass, seenNow.Slice(0, seenCount),
                                                             PerceptionConstants.HeardBoostPerContact, tick, h.Sound))
                        continue;
                    if (placed && TargetMemory.AttributeToSeen(ref mem, self.X, self.Y, h.X, h.Y, h.Kind, h.SourceClass, h.Via,
                                                               seenNow.Slice(0, seenCount), PerceptionConstants.HeardBoostPerContact, tick, h.Sound))
                        continue;
                    TargetMemory.HearContact(ref mem, h.X, h.Y, h.Z, h.Radius, h.SourceClass,
                                             PerceptionConstants.HeardBoostPerContact, tick, h.Via, h.Sound,   // ⭐ CE-4666 (D-8) · CE-2474 the token
                                             deltaTime > 0f ? deltaTime : 1f / 60f);   // ⭐ CE-2492 — how far he may have walked since
                }
                changed = true;
            }

            // ⭐ CE-3046 — FORGET: a target that no longer exists, or one no sensor tracks whose score has faded.
            //   🔴 Nothing used to remove an entry, so dead and long-lost targets kept their slots forever.
            bool hasTracks = view.HasComponent<ActiveSensorTracks>(entity);
            for (int i = mem.Count - 1; i >= 0; i--)
            {
                var target = new Entity((ulong)mem.EntityIds[i]);
                // ⭐ CE-3063 — an anonymous slot is not an entity: it fades by freshness only.
                bool dead  = !TargetMemory.IsAnonymous(in mem, i) && !view.IsAlive(target);
                bool faded = mem.Freshness[i] < PerceptionConstants.ForgetThreatScore
                          && !(hasTracks && IsTracked(in view.GetComponentRO<ActiveSensorTracks>(entity), mem.EntityIds[i]));
                // ⭐ CE-2371 (AQ104 D, R-2079) — an ALERTED contact (a lease his task renews) is never forgotten while it lives: held at the
                //   forget line, so it stays remembered (TopAim fights it, the hunt follows it) until he is down or the task lets go
                if (faded && !dead && Alerts.IsPinned(view, entity, mem.EntityIds[i], now))
                {
                    mem.Freshness[i] = PerceptionConstants.ForgetThreatScore;
                    changed = true;
                    continue;
                }
                if (!dead && !faded) continue;
                TargetMemory.Forget(ref mem, i);
                changed = true;
            }

            // ⭐ CE-3039 — HIT: the unit's health dropped (replicated to this Brain node by EntityDamage) — whoever fired, seen or not.
            //   ⭐ R-3039 S1 — the previous value is on the memory (LastHealth); the first sight of a unit only records it.
            if (view.HasComponent<Fdp.Toolkit.Combat.Components.Health>(entity))
            {
                float hp = view.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(entity).Current;
                if (mem.HasLastHealth != 0 && hp < mem.LastHealth)
                    ecb.PublishEvent(new SensorChangedEvent { Unit = entity, What = SensorChange.Hit });
                if (mem.HasLastHealth == 0 || hp != mem.LastHealth) { mem.LastHealth = hp; mem.HasLastHealth = 1; changed = true; }
            }

            // ⭐ CE-3039 — the memory's edges, from the system that holds the memory (design §7.3). AllClear is reachable
            //   since CE-3046 made the memory forget.
            // ⭐ CE-2233 (CIV-0, docs/DESIGN_Civilians_And_Crowd_Control.md §5b) — the edges count THREATS, not contacts: a soldier
            //   remembers the civilians he sees, and counted as contacts the first civilian took the one FirstThreat (the SOP
            //   reacted, found nothing to engage, went idle) and a Hostile seen after it raised none — measured live on
            //   ua-civ-neutral-gate. ⛔ SUPERSEDED: countBefore == 0 && mem.Count > 0 (any contact).
            int threatsBefore = _lastThreats.TryGetValue(entity, out int tb) ? tb : ThreatCount(view, in memRO, entity);
            threatsNow = ThreatCount(view, in mem, entity);
            if (threatsBefore == 0 && threatsNow > 0)
                ecb.PublishEvent(new SensorChangedEvent { Unit = entity, What = SensorChange.FirstThreat });
            else if (threatsBefore > 0 && threatsNow == 0)
                ecb.PublishEvent(new SensorChangedEvent { Unit = entity, What = SensorChange.AllClear });

            // ⭐ CE-2165 V8 — AIR THREAT: more air contacts than before this frame (a flyer seen, rotors heard). A COUNT, not new
            //   ids: a heard drone later SEEN is one contact (the sighting absorbs the heard slot, CE-3063), not a second alarm.
            if (AirContacts.Count(view, in mem) > AirContacts.Count(view, in memRO))
                ecb.PublishEvent(new SensorChangedEvent { Unit = entity, What = SensorChange.AirThreat });

            // ⭐ CE-2429 (Next_3 H-2) — on the LIVE world (the CGF adapter runs synchronously "immediately before CognitiveRuntimeModule, so
            //   B-Trees evaluate against freshly decayed scores") the memory is written IN PLACE: the brains see this frame's evaluation,
            //   and a seed a behaviour writes after it survives. 📐 Measured live (ua-technical-scoot): through the command buffer this whole
            //   copy was played back AFTER the brain tick and erased the attack order's same-frame seed. A snapshot view keeps the buffer.
            // ⚠ the write comes LAST: memRO is a reference into the live component, and the edges above compare it (before) with mem (now).
            if (changed)
            {
                if (view is EntityRepository live) live.GetComponentRW<TargetMemory>(entity) = mem;
                else ecb.SetComponent(entity, mem);
            }
        }

        public static unsafe int ThreatCount(ISimulationView view, in TargetMemory mem, Entity observer = default)
        {
            int n = 0;
            for (int i = 0; i < mem.Count; i++)
                if (TargetMemory.IsAnonymous(in mem, i) || !ThreatDanger.IsNeutralFor(view, observer, new Entity((ulong)mem.EntityIds[i]))) n++;
            return n;
        }

        /// <summary>What <paramref name="target"/> can sound like on this node (its <c>AcousticEmitter</c> classes); empty =
        /// unknown, compatible with any heard class.</summary>
        internal static System.ReadOnlySpan<byte> ClassesOf(ISimulationView view, Entity target, System.Span<byte> buffer)   // ⭐ CE-2107 — the squad merge asks it too
        {
            if (!view.IsAlive(target) || !view.HasComponent<Fdp.Toolkit.Perception.Signatures.AcousticEmitter>(target))
                return System.ReadOnlySpan<byte>.Empty;
            ref readonly var a = ref view.GetComponentRO<Fdp.Toolkit.Perception.Signatures.AcousticEmitter>(target);
            int n = 0;
            if (a.MovingClass != 0)     buffer[n++] = a.MovingClass;
            if (a.FiringClass != 0)     buffer[n++] = a.FiringClass;
            if (a.DetonationClass != 0) buffer[n++] = a.DetonationClass;
            return buffer.Slice(0, n);
        }

        private static unsafe bool IsTracked(in ActiveSensorTracks tracks, long id)
        {
            for (int i = 0; i < tracks.Count; i++)
                if (tracks.EntityIds[i] == id) return true;
            return false;
        }
    }
}