using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Squad;

namespace Fdp.Toolkit.Aggregate
{
    /// <summary>
    /// ⭐⭐ <c>CE-8006</c> (ST-2, N-1 / N-4; docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md §15) — THE detection curve of
    /// a stochastic observer: the step's chance P(r) = Pmax · 2^−(r / R50)², R50 the sensor's half-chance range × the target's signature,
    /// cut by the woods' transmittance along the line and the weather / night (the SAME <c>PerceptionWeather</c> helpers the physical sensors
    /// read). The sensor ranges and signature multipliers are CALIBRATED (RV-17), named constants.
    /// </summary>
    public static class UnitDetection
    {
        /// <summary>The step's chance at zero range.</summary>
        public const float PMax = 0.9f;
        /// <summary>The detection step (s) — N-1's "~1–2 s".</summary>
        public const float StepSeconds = 1f;
        /// <summary>CALIBRATED half-chance ranges (m) against a squad on foot, standing still, in the open, by day: the eye, optics, thermal.</summary>
        public const float EyeR50 = 400f, OpticsR50 = 1200f, ThermalR50 = 1800f;
        /// <summary>CALIBRATED — radar sees only vehicles.</summary>
        public const float RadarR50 = 3000f;
        /// <summary>The farthest any observer looks (m) — the candidate grid's reach.</summary>
        public const float MaxRange = 6000f;
        /// <summary>CALIBRATED signature: a man counts this much, a vehicle this much (size); moving ×1.5, firing ×3 (N-4).</summary>
        public const float PerMan = 1f / 9f, PerVehicle = 0.5f, MovingFactor = 1.5f, FiringFactor = 3f;

        /// <summary>The target's signature as a range multiplier: √(size) × activity (a squad of 9 standing still = 1).</summary>
        public static float Signature(in AggregateUnit t, in UnitSlotTable slots, in UnitHoldings h)
        {
            float size = slots.Present() * PerMan;
            for (int i = 0; i < h.Count; i++) if (h.Items[i].Kind == HoldingKind.Vehicle) size += h.Items[i].Operational * PerVehicle;
            if (size <= 0f) return 0f;
            float s = MathF.Sqrt(size);
            if ((t.Activity & UnitActivity.Moving) != 0) s *= MovingFactor;
            if ((t.Activity & UnitActivity.Firing) != 0) s *= FiringFactor;
            return s;
        }

        /// <summary>The observer's best half-chance range against a target with vehicles or not (N-4: its sensor holdings), before weather.</summary>
        public static float SensorR50(in UnitHoldings observer, bool targetHasVehicles, out bool thermal)
        {
            float r = EyeR50; thermal = false;
            for (int i = 0; i < observer.Count; i++)
            {
                if (observer.Items[i].Kind != HoldingKind.Sensor || observer.Items[i].Operational == 0) continue;
                switch (observer.Items[i].Sensor)
                {
                    case HoldingSensor.Optics: r = MathF.Max(r, OpticsR50); break;
                    case HoldingSensor.Thermal: if (ThermalR50 > r) { r = ThermalR50; thermal = true; } break;
                    case HoldingSensor.Radar: if (targetHasVehicles && RadarR50 > r) { r = RadarR50; thermal = false; } break;
                }
            }
            return r;
        }

        /// <summary>⭐ CE-8009 — is <paramref name="range"/> past the naked eye's reach? Only when the observer's best sensor IS the eye
        /// (<paramref name="sensorR50"/> = <see cref="EyeR50"/>) and the reach is known (&gt; 0): the soldiers' own TKB <c>VisionRange</c>, the
        /// same hard limit the physical soldier sees to (<c>VisualPerception</c>) — one source, so the two resolutions see equally far.</summary>
        public static bool BeyondEye(float sensorR50, float range, float eyeReach) => sensorR50 == EyeR50 && eyeReach > 0f && range > eyeReach;

        /// <summary>⭐ N-1's curve: the step's chance at <paramref name="range"/> for half-chance range <paramref name="r50"/> (already × signature,
        /// weather), × the line's transmittance.</summary>
        public static float StepChance(float range, float r50, float transmittance = 1f)
        {
            if (r50 <= 0f || range > MaxRange) return 0f;
            float x = range / r50;
            return PMax * MathF.Pow(2f, -x * x) * Math.Clamp(transmittance, 0f, 1f);
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-8006</c> (ST-2, N-1 … N-5) — the stochastic units' SENSING, every <see cref="UnitDetection.StepSeconds"/> of sim time: each
    /// stochastic observer looks at every stochastic unit of another side within <see cref="UnitDetection.MaxRange"/> (a uniform grid built
    /// serially); line of sight between a few samples of each footprint through THE world query (<c>SightBlocked</c>), the woods'
    /// transmittance, the weather; the step's chance ACCUMULATES in its own <see cref="UnitDetections"/> (N-5) and a pair over
    /// <see cref="UnitDetections.ContactAt"/> is written into the observer's OWN squad contact pool (N-2) — the same pool the staff's SPOT and
    /// every commander already read. Faded after <see cref="UnitDetections.FadeAfterSeconds"/> out of sight. ⭐ A per-entity phase: an observer
    /// writes only itself; the targets' state is a declared stable read (nothing in this phase writes it). 0 B per steady step.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class StochasticUnitSensorSystem : IEcsModuleSystem
    {
        /// <summary>Footprint samples per side (centre, the two flanks).</summary>
        public const int Samples = 3;
        private const float EyeHeight = 2f, GridCell = 2000f;
        private readonly bool _gateOnAuthority;
        private EntityQuery? _units;
        private EntityRepository? _queriesFor;
        [Fdp.Core.FrameScratch("this run's observing units: refilled from index 0 for the parallel job")]
        internal Entity[] _observers = new Entity[32];
        [Fdp.Core.FrameScratch("this run's units: the count reset to 0 each run, refilled")]
        internal Entity[] _all = new Entity[32];
        [Fdp.Core.FrameScratch("count reset to 0 at 127 each run")]
        private int _allCount;
        // ⭐ CE-8018 (N-3) — the PHYSICAL bodies a stochastic observer may see, gathered serially (the phase reads none of their components)
        [Fdp.Core.FrameScratch("physical target array refilled from 0 (_physicalCount reset at 302)")]
        internal PhysicalTarget[] _physical = new PhysicalTarget[64];
        [Fdp.Core.FrameScratch("count reset to 0 at 302 each run")]
        private int _physicalCount;
        private EntityQuery? _bodies;

        /// <summary>⭐ N-3 — one physical body as a stochastic observer sees it: where, which side, how big (the signature), whether a vehicle.</summary>
        internal struct PhysicalTarget { public Entity Entity; public Vector3 At; public ForceId Force; public float Signature; public bool Vehicle; }
        [Fdp.Core.WorldDerived("step-gate timestamp derived from sim time")]
        private SimInstant _lastStep = (SimInstant)(-1.0);
        // ⭐ CE-8009 — the naked eye's reach (the personal soldiers' TKB VisionRange; 0 = no cap), resolved serially, once per database
        [Fdp.Core.WorldDerived("eye reach derived from the TKB, recomputed when the TKB changes")]
        private float _eyeReach;
        [Fdp.Core.NotState("static readonly menu item object")]
        private object? _eyeReachFor;

        public StochasticUnitSensorSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<AggregateUnit>() || !repo.IsComponentTypeRegistered<UnitDetections>()
                || !repo.IsComponentTypeRegistered<UnitSlotTable>() || !repo.IsComponentTypeRegistered<EntityInfo>()) return;
            SimInstant now = repo.HasSingleton<GlobalTime>() ? (SimInstant)repo.GetSingleton<GlobalTime>().TotalTime : SimInstant.Zero;
            if ((double)now - (double)_lastStep < UnitDetection.StepSeconds - 1e-6) return;
            _lastStep = now;
            if (!ReferenceEquals(_queriesFor, repo))
            {
                _units = repo.Query().With<AggregateUnit>().With<SimTransform>().With<UnitSlotTable>().With<EntityInfo>().Build();
                _bodies = repo.Query().With<SimTransform>().With<EntityInfo>().Without<AggregateUnit>().Build();
                _queriesFor = repo;
            }
            // serial pre-pass: every stochastic unit (the targets), the observers this node owns, their detections component present
            _allCount = 0; int n = 0;
            foreach (var e in _units!)
            {
                if (repo.GetComponentRO<AggregateUnit>(e).Effective != AggregateResolution.Stochastic) continue;
                if (_allCount == _all.Length) Array.Resize(ref _all, _all.Length * 2);
                _all[_allCount++] = e;
                if (_gateOnAuthority && !repo.HasAuthority<AggregateUnit>(e)) continue;
                if (!repo.HasComponent<UnitDetections>(e)) repo.AddComponent(e, new UnitDetections());
                if (n == _observers.Length) Array.Resize(ref _observers, n * 2);
                _observers[n++] = e;
            }
            if (n == 0) return;
            GatherPhysical(repo);
            ResolveEyeReach(repo);
            var job = new SenseJob { Repo = repo, EyeReach = _eyeReach, All = _all, AllCount = _allCount, Physical = _physical, PhysicalCount = _physicalCount, Now = now, World = Fdp.Toolkit.World.WorldQuery.Of(repo),
                                     Terrain = repo.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() ? repo.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() : null };
            if (Fdp.Core.Jobs.ParallelEntities.Enabled) Fdp.Core.Jobs.ParallelEntities.Run(repo, _observers, n, in job);
            else for (int i = 0; i < n; i++) job.Execute(_observers[i], i, null!);
        }

        private struct SenseJob : Fdp.Core.Jobs.IEntityBatchJob, Fdp.Core.Jobs.IStableReads, Fdp.Core.Jobs.ICoarseJob
        {
            /// <summary>A unit is a whole unit's work (CE-4517): 2 units a batch, so a battalion's 18 spread over the threads.</summary>
            public int BatchSize => 2;

            public EntityRepository Repo;
            public Entity[] All;
            public int AllCount;
            public PhysicalTarget[] Physical;
            public int PhysicalCount;
            public SimInstant Now;
            public float EyeReach;
            public Fdp.Toolkit.World.IWorldQuery? World;
            public Fdp.Toolkit.Terrain.TerrainWorld? Terrain;

            /// <summary>The TARGETS' state, read from the tick-start view: nothing in this phase writes another unit's.</summary>
            public void DeclareStableReads(Fdp.Core.Jobs.PhaseReadSet reads) => reads
                .Unmanaged<AggregateUnit>().Unmanaged<UnitSlotTable>().Unmanaged<UnitHoldings>().Unmanaged<SimTransform>().Unmanaged<EntityInfo>()
                .Unmanaged<Fdp.Toolkit.Combat.Components.Health>();   // ⭐ CE-8018 — a seen body's death (nothing in this phase writes Health)

            public void ApplyDeferred(EntityRepository repo, in Fdp.Core.Jobs.Deferred d) { }

            public void Execute(Entity self, int index, Fdp.Core.Jobs.BatchContext ctx)
                => SenseOne(Repo, self, All, AllCount, Now, World, Terrain, Physical, PhysicalCount, EyeReach);
        }

        /// <summary>One observer's step (the test seam).</summary>
        internal static void SenseOne(EntityRepository repo, Entity self, Entity[] all, int allCount, SimInstant now,
                                      Fdp.Toolkit.World.IWorldQuery? world, Fdp.Toolkit.Terrain.TerrainWorld? terrain,
                                      PhysicalTarget[]? physical = null, int physicalCount = 0, float eyeReach = 0f)
        {
            ref readonly var me = ref repo.GetComponentRO<AggregateUnit>(self);
            var myForce = repo.GetComponentRO<EntityInfo>(self).ForceId;
            var myAt = repo.GetComponentRO<SimTransform>(self).Position;
            var myHoldings = repo.HasComponent<UnitHoldings>(self) ? repo.GetComponentRO<UnitHoldings>(self) : default;
            if (repo.GetComponentRO<UnitSlotTable>(self).Present() == 0) return;   // nobody left to look
            ref var det = ref repo.GetComponentRW<UnitDetections>(self);
            Fdp.Toolkit.Weather.WeatherSample w = default;
            bool weather = Fdp.Toolkit.Perception.Sensors.PerceptionWeather.TrySample(repo, self, out w);
            Span<Vector3> mine = stackalloc Vector3[Samples];
            Span<Vector3> theirs = stackalloc Vector3[Samples];
            Footprint(in me, myAt, mine);
            for (int j = 0; j < allCount; j++)
            {
                var t = all[j];
                if (t == self || !repo.IsAlive(t)) continue;
                var force = repo.GetComponentRO<EntityInfo>(t).ForceId;
                if (force == myForce || force == ForceId.Neutral) continue;
                var tAt = repo.GetComponentRO<SimTransform>(t).Position;
                float range = Vector2.Distance(new Vector2(myAt.X, myAt.Y), new Vector2(tAt.X, tAt.Y));
                if (range > UnitDetection.MaxRange) continue;
                ref readonly var tu = ref repo.GetComponentRO<AggregateUnit>(t);
                var th = repo.HasComponent<UnitHoldings>(t) ? repo.GetComponentRO<UnitHoldings>(t) : default;
                float sig = UnitDetection.Signature(in tu, repo.GetComponentRO<UnitSlotTable>(t), in th);
                if (sig <= 0f) continue;
                float eye = UnitDetection.SensorR50(in myHoldings, th.HasVehicles(), out bool thermal);
                bool beyond = UnitDetection.BeyondEye(eye, range, eyeReach);   // ⭐ CE-8009 — the naked eye stops where the soldier's does
                float r50 = eye * sig;
                if (weather) r50 = thermal ? Fdp.Toolkit.Perception.Sensors.PerceptionWeather.ThermalRange(r50, in w)
                                           : Fdp.Toolkit.Perception.Sensors.PerceptionWeather.VisionRange(r50, in w);
                Footprint(in tu, tAt, theirs);
                float best = 0f;   // the best transmittance over the sample pairs with a clear line
                for (int a = 0; a < Samples && best < 1f; a++)
                    for (int b = 0; b < Samples && best < 1f; b++)
                    {
                        var from = mine[a] + new Vector3(0, 0, EyeHeight);
                        var to = theirs[b] + new Vector3(0, 0, 1f);
                        if (world != null && world.SightBlocked(from, to)) continue;
                        float tr = terrain?.Woods != null ? terrain.Woods.Transmittance(from, to, terrain, out _) : 1f;
                        if (tr > best) best = tr;
                    }
                if (beyond) best = 0f;
                Accumulate(repo, self, ref det, t, tAt, best, UnitDetection.StepChance(range, r50, best), MathF.Min(1f, sig * 0.5f), now);
            }
            // ⭐ CE-8018 (N-3) — the PHYSICAL bodies, one way: the same curve, a body's own signature, one sample point (no footprint)
            for (int j = 0; j < physicalCount; j++)
            {
                ref readonly var b = ref physical![j];
                if (b.Force == myForce || b.Force == ForceId.Neutral) continue;
                float range = Vector2.Distance(new Vector2(myAt.X, myAt.Y), new Vector2(b.At.X, b.At.Y));
                if (range > UnitDetection.MaxRange) continue;
                float eye = UnitDetection.SensorR50(in myHoldings, b.Vehicle, out bool thermal);
                bool beyond = UnitDetection.BeyondEye(eye, range, eyeReach);
                float r50 = eye * b.Signature;
                if (weather) r50 = thermal ? Fdp.Toolkit.Perception.Sensors.PerceptionWeather.ThermalRange(r50, in w)
                                           : Fdp.Toolkit.Perception.Sensors.PerceptionWeather.VisionRange(r50, in w);
                var to = b.At + new Vector3(0, 0, 1f);
                float best = 0f;
                for (int a = 0; a < Samples && best < 1f; a++)
                {
                    var from = mine[a] + new Vector3(0, 0, EyeHeight);
                    if (world != null && world.SightBlocked(from, to)) continue;
                    float tr = terrain?.Woods != null ? terrain.Woods.Transmittance(from, to, terrain, out _) : 1f;
                    if (tr > best) best = tr;
                }
                if (beyond) best = 0f;
                Accumulate(repo, self, ref det, b.Entity, b.At, best, UnitDetection.StepChance(range, r50, best), MathF.Min(1f, b.Signature * 0.5f), now);
            }

            // targets gone (dead, merged away): their pairs fade too
            bool health = repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>();
            for (int i = det.Count - 1; i >= 0; i--)
            {
                var t = det.Pairs[i].Target;
                if (!repo.IsAlive(t) || health && repo.HasComponent<Fdp.Toolkit.Combat.Components.Health>(t)
                                          && repo.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(t).Current <= 0f)   // ⭐ CE-8018 — a dead body
                    Drop(repo, self, ref det, i);
            }
        }

        /// <summary>One step's sighting of <paramref name="t"/> (a unit or a body — ONE implementation) into the observer's own levels (N-5) and, past the threshold, its pool (N-2).</summary>
        private static void Accumulate(EntityRepository repo, Entity self, ref UnitDetections det, Entity t, Vector3 tAt, float best, float p, float threat, SimInstant now)
        {
            int slot = Find(in det, t);
            if (best <= 0f)
            {
                if (slot >= 0 && (double)now - (double)det.Pairs[slot].LastSeenAt >= UnitDetections.FadeAfterSeconds) Drop(repo, self, ref det, slot);
                return;
            }
            if (slot < 0)
            {
                if (det.Count >= UnitDetections.Capacity) return;
                slot = det.Count++;
                det.Pairs[slot] = new DetectionPair { Target = t };
            }
            ref var pair = ref det.Pairs[slot];
            pair.Level = MathF.Min(pair.Level + p, 2f * UnitDetections.ContactAt);
            pair.LastSeenAt = now;
            if (pair.Level >= UnitDetections.ContactAt)
            {
                pair.Reported = 1;
                Report(repo, self, t, tAt, threat, now);
            }
        }

        /// <summary>⭐ CE-8009 — the personal soldiers' naked-eye reach from the TKB (serial; once per database and soldier map).</summary>
        private void ResolveEyeReach(EntityRepository repo)
        {
            var personal = KillerVictimTkb.PersonalSoldiers;
            if (!repo.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>()) { _eyeReach = 0f; _eyeReachFor = null; return; }
            var db = repo.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>();
            if (db == null) { _eyeReach = 0f; _eyeReachFor = null; return; }
            var key = (object?)personal ?? db;
            if (ReferenceEquals(key, _eyeReachFor)) return;
            _eyeReachFor = key;
            _eyeReach = KillerVictimTkb.SoldierEyeReach(db, personal);
        }

        /// <summary>
        /// ⭐ CE-8018 (N-3) — every PHYSICAL body a stochastic observer may see: placed, of a side, not a unit, not hidden (a passenger inside, a
        /// round, a drawn area — <c>PhysicalPresence.IsHidden</c>), not a part (a mount, a sensor), alive. A man counts as one man, a vehicle as a
        /// vehicle (<see cref="UnitDetection.PerMan"/> / <see cref="UnitDetection.PerVehicle"/>, √ as a unit's size). Serial; grows, never shrinks.
        /// </summary>
        private void GatherPhysical(EntityRepository repo)
        {
            _physicalCount = 0;
            bool parts = repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.PartMetadata>();
            foreach (var e in _bodies!)
            {
                var f = repo.GetComponentRO<EntityInfo>(e).ForceId;
                if (f == ForceId.Neutral) continue;
                if (parts && repo.HasComponent<Fdp.Toolkit.Replication.Components.PartMetadata>(e)) continue;
                if (Fdp.Toolkit.Perception.PhysicalPresence.IsHidden(repo, e)) continue;
                if (repo.HasComponent<Fdp.Toolkit.Combat.Components.Health>(e) && repo.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(e).Current <= 0f) continue;
                bool vehicle = Fdp.Toolkit.Motion.Locomotions.IsVehicle(repo, e);
                if (_physicalCount == _physical.Length) Array.Resize(ref _physical, _physical.Length * 2);
                _physical[_physicalCount++] = new PhysicalTarget
                {
                    Entity = e, At = repo.GetComponentRO<SimTransform>(e).Position, Force = f, Vehicle = vehicle,
                    Signature = MathF.Sqrt(vehicle ? UnitDetection.PerVehicle : UnitDetection.PerMan),
                };
            }
        }

        /// <summary>The footprint samples: the centre and the two flanks (± half the frontage, across the heading).</summary>
        internal static void Footprint(in AggregateUnit u, Vector3 at, Span<Vector3> into)
        {
            float rad = u.Heading.Degrees * (MathF.PI / 180f);
            var across = new Vector3(MathF.Cos(rad), -MathF.Sin(rad), 0f);   // the heading's right hand (0° = north ⇒ east)
            float half = MathF.Max(0f, u.Width) * 0.5f;
            into[0] = at; into[1] = at + across * half; into[2] = at - across * half;
        }

        private static int Find(in UnitDetections d, Entity t)
        {
            for (int i = 0; i < d.Count; i++) if (d.Pairs[i].Target == t) return i;
            return -1;
        }

        private static void Drop(EntityRepository repo, Entity self, ref UnitDetections d, int slot)
        {
            var target = d.Pairs[slot].Target;
            d.Pairs[slot] = d.Pairs[d.Count - 1];
            d.Pairs[d.Count - 1] = default;
            d.Count--;
            Forget(repo, self, target);
        }

        /// <summary>N-2 — the contact into the observer's OWN pool (insert or refresh), kept sorted by threat.</summary>
        internal static void Report(EntityRepository repo, Entity self, Entity target, Vector3 at, float threat, SimInstant now)
        {
            if (!repo.IsComponentTypeRegistered<SquadCognitiveState>() || !repo.HasComponent<SquadCognitiveState>(self)) return;
            ref var pool = ref repo.GetComponentRW<SquadCognitiveState>(self).Contacts;
            var slots = MemoryMarshal.CreateSpan(ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref pool.Contacts), 16);
            long id = (long)target.PackedValue;
            int at_ = -1;
            for (int i = 0; i < pool.Count; i++) if (slots[i].EntityId == id) { at_ = i; break; }
            if (at_ < 0)
            {
                if (pool.Count < 16) at_ = pool.Count++;
                else if (slots[15].ThreatScore < threat) at_ = 15;
                else return;
            }
            slots[at_] = new SquadContact
            {
                EntityId = id, PositionX = at.X, PositionY = at.Y, PositionZ = at.Z, ThreatScore = threat,
                LastSeenTick = (uint)Math.Max(0, (long)((double)now * 60.0)), Flags = 1, SourceMembersMask = 1,
            };
            for (int i = at_; i > 0 && slots[i - 1].ThreatScore < slots[i].ThreatScore; i--) (slots[i - 1], slots[i]) = (slots[i], slots[i - 1]);
        }

        private static void Forget(EntityRepository repo, Entity self, Entity target)
        {
            if (!repo.IsComponentTypeRegistered<SquadCognitiveState>() || !repo.HasComponent<SquadCognitiveState>(self)) return;
            ref var pool = ref repo.GetComponentRW<SquadCognitiveState>(self).Contacts;
            var slots = MemoryMarshal.CreateSpan(ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref pool.Contacts), 16);
            long id = (long)target.PackedValue;
            for (int i = 0; i < pool.Count; i++)
                if (slots[i].EntityId == id)
                {
                    for (int k = i; k < pool.Count - 1; k++) slots[k] = slots[k + 1];
                    slots[--pool.Count] = default;
                    return;
                }
        }
    }
}
