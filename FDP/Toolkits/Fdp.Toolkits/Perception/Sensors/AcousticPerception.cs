using System;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Perception.Systems;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Spatial.Eqs;

namespace Fdp.Toolkit.Perception.Sensors
{
    /// <summary>
    /// ⭐ <c>CE-3062</c> — <b>hearing as a sensor</b> (docs/DESIGN_Thermal_And_Acoustic_Sensing.md §5.1, §6 B–D, R-205). The
    /// generator walks the other-force entities near the unit (<see cref="VisionBroadphase"/>, all round, no line of sight) and
    /// answers one POSITIONAL estimate per sound it can hear — movement, a recent shot, a recent detonation — each off by a
    /// deterministic error inside an uncertainty radius that grows with distance. ⛔ No answer carries the source's id: the
    /// memory stage turns the answers into anonymous <c>SoundContactEvent</c>s.
    /// </summary>
    public static class AcousticPerception
    {
        public const string AssetId = "9d2e4b61-3062-4f0a-b7c3-1a8e6d5f2c07";

        public static readonly Guid AssetGuid = new(AssetId);

        public static uint BlueprintId => EqsTemplateRegistry.BlueprintIdOf(AssetGuid);

        /// <summary>The smallest uncertainty radius (metres), however close the sound.</summary>
        public const float MinRadius = 2f;

        /// <summary>The flag bits an answer's <see cref="SoundKind"/> is written in (bits 8–9; the low bits belong to EQS tests).</summary>
        public const short KindShift = 8, KindMask = 0x3 << 8;

        /// <summary>⭐ <c>CE-3063</c> — the flag bits an answer's <c>SoundSourceClass</c> is written in (bits 10–12).</summary>
        public const short ClassShift = 10, ClassMask = 0x7 << 10;

        public static EqsQueryTemplate Template(SpatialHashGrid grid) => new()
        {
            BlueprintId   = BlueprintId,
            Generator     = new AcousticSensorGenerator(grid),
            MaxCandidates = VisionBroadphase.MaxCandidatesPerObserver * 3,
        };

        /// <summary>⭐ <c>CE-2106</c> — the sensor a TKB that lists no sensors gets when it can HEAR (the twin of
        /// <c>VisualPerception.ImplicitEntry</c>): its ears reach <see cref="SensorCapabilitiesDto.HearingRange"/>.</summary>
        public static SensorEntryDto ImplicitEntry(SensorCapabilitiesDto dto) => new()
        {
            Kind         = SensorModality.Acoustic,
            Template     = AssetGuid,
            SearchRadius = dto.HearingRange,
            Acoustic     = new AcousticSensorDto { Range = dto.HearingRange },
        };

        public static void Register(EqsTemplateRegistry registry, SpatialHashGrid grid)
            => registry.Register(AssetGuid, Template(grid), "Acoustic");

        /// <summary>The (range, uncertainty per metre) a sensor listens with — its current acoustic parameters.</summary>
        public static bool Ears(ISimulationView view, Entity sensor, out float range, out float perMeter)
        {
            if (view.HasManagedComponent<SensorCapability>(sensor)
                && view.GetManagedComponentRO<SensorCapability>(sensor).Current.Acoustic is { } a)
            {
                range    = a.Range;
                perMeter = a.UncertaintyPerMeter > 0f ? a.UncertaintyPerMeter : 0.15f;
                return range > 0f;
            }
            range = 0f; perMeter = 0f;
            return false;
        }

        /// <summary>⭐ <c>CE-3276</c> (R-3023, §8.2) — a LOUD class (a heavy weapon, an explosion): heard wherever it carries, past the
        /// listener's ear range.</summary>
        public static bool IsLoud(byte sourceClass)
            => sourceClass is (byte)SoundSourceClass.HeavyWeapon or (byte)SoundSourceClass.Explosion;

        /// <summary>The uncertainty radius of a sound heard at <paramref name="distance"/>.</summary>
        public static float RadiusAt(float distance, float perMeter) => MathF.Max(MinRadius, distance * perMeter);

        /// <summary>
        /// A deterministic offset inside <paramref name="radius"/> for (source, listener, tick): the same run always mishears the
        /// same way (replay is exact), and the error changes as time passes, so repeated sounds spread around the truth.
        /// </summary>
        public static Vector2 Error(ulong source, ulong listener, uint tick, float radius)
        {
            ulong h = source * 0x9E3779B97F4A7C15UL ^ listener * 0xC2B2AE3D27D4EB4FUL ^ (ulong)tick * 0x165667B19E3779F9UL;
            h ^= h >> 33; h *= 0xFF51AFD7ED558CCDUL; h ^= h >> 33; h *= 0xC4CEB9FE1A85EC53UL; h ^= h >> 33;
            float angle = (h & 0xFFFF) / 65536f * 2f * MathF.PI;
            float r     = ((h >> 16) & 0xFFFF) / 65536f * radius;
            return new Vector2(MathF.Cos(angle) * r, MathF.Sin(angle) * r);
        }
    }

    /// <summary>The acoustic sensor's answers — see <see cref="AcousticPerception"/>.</summary>
    public sealed class AcousticSensorGenerator : IEqsGenerator
    {
        private readonly SpatialHashGrid _grid;
        private readonly BroadphaseCache _broadphase = new();   // ⭐ CE-1055 — one per calling thread (the solver, the map's gizmo)
        /// <summary>⭐ CE-3282 — the most ring sounds one solve reads (on the stack); the cursor stops at the last one read, the next solve reads on.</summary>
        public const int MaxSoundsPerSolve = 256;

        public AcousticSensorGenerator(SpatialHashGrid grid) => _grid = grid;

        public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            var unit = EqsContext.Self(view, observer, sensor);
            if (unit.IsNull || !view.IsAlive(unit) || !view.HasComponent<SimTransform>(unit)) return 0;
            if (!AcousticPerception.Ears(view, observer, out float range, out float perMeter)) return 0;
            // ⭐ CE-3216 S2-4 (R-3017) — wind and rain mask the listener (none without a clock)
            float masking = PerceptionWeather.TrySample(view, unit, out var weather) ? PerceptionWeather.HearingFactor(in weather) : 1f;
            range *= masking;
            var broadphase = _broadphase.For(view, _grid);

            var ear = view.GetComponentRO<SimTransform>(unit).Position;
            var ears = new Ears(ear, range, masking, perMeter, unit.PackedValue, view.Tick);
            int n = 0;
            // MOVEMENT is continuous: the emitters near the unit, through the grid (§5.1)
            foreach (var (_, source) in broadphase.Select(view, unit, range, fovCos: -1f))
            {
                if (!view.HasComponent<AcousticEmitter>(source)) continue;
                var a  = view.GetComponentRO<AcousticEmitter>(source);
                if (a.CurrentMovingRange > 0f)
                    Sound(candidates, ref n, in ears, view.GetComponentRO<SimTransform>(source).Position, a.CurrentMovingRange, SoundKind.Movement,
                          a.MovingClass, source.PackedValue, ears.Tick);
            }
            // ⭐⭐ CE-3282 (Q95 C; Sensing §8.3) — SHOTS and BURSTS are short: every one made since this sensor last read, from the lossless ring
            //   (distance only — on the terrain or off it, Q95 B), whenever this solve runs
            HearRing(observer, unit, view, candidates, ref n, in ears);
            return n;
        }

        // ⭐ CE-3282 — the ring after this sensor's cursor; the cursor moves through the solver's command buffer (a discarded solve re-reads).
        private static void HearRing(Entity sensorEntity, Entity unit, ISimulationView view, Span<EqsResult> candidates, ref int n, in Ears ears)
        {
            var ring = SoundEmissionRing.In(view);
            if (ring == null) return;
            bool cursors = view is EntityRepository repo && repo.IsComponentTypeRegistered<AcousticCursor>();
            bool known = cursors && view.HasComponent<AcousticCursor>(sensorEntity);
            long after = known ? view.GetComponentRO<AcousticCursor>(sensorEntity).LastSerial : 0L;
            // a sensor with no cursor yet hears only what is still ringing (the old linger), never the minutes before it existed
            double notBefore = known ? double.MinValue : ring.Now - SoundEmissionSystem.SoundLingerSeconds;
            Span<SoundEmission> sounds = stackalloc SoundEmission[MaxSoundsPerSolve];
            int k = ring.ReadAfter(after, sounds, out long last, notBefore);
            byte ourForce = view.HasComponent<EntityInfo>(unit) ? (byte)view.GetComponentRO<EntityInfo>(unit).ForceId : (byte)0;
            for (int i = 0; i < k; i++)
            {
                ref readonly var e = ref sounds[i];
                if (n == candidates.Length) { last = e.Serial - 1; break; }   // no room: this one and the rest wait for the next solve
                if (e.Source == unit.PackedValue || e.Force == ourForce) continue;   // not his own sound, not his own side's (the broadphase's rule)
                Sound(candidates, ref n, in ears, e.At, e.Carry, (SoundKind)e.Kind, e.Class, e.Source, (uint)e.Serial);
            }
            if (!cursors || (known && last == after)) return;   // ⭐ a sensor's FIRST solve always plants its cursor, even with nothing to read —
            var cmd = view.GetCommandBuffer();                       //   else a sound after a long skip would meet the no-cursor window
            if (known) cmd.SetComponent(sensorEntity, new AcousticCursor { LastSerial = last });
            else cmd.AddComponent(sensorEntity, new AcousticCursor { LastSerial = last });
        }

        /// <summary>One listener's ears for this tick: where, how far they reach (masked), the weather's masking, the uncertainty.</summary>
        private readonly record struct Ears(Vector3 At, float Range, float Masking, float PerMeter, ulong Listener, uint Tick);

        // ⭐ CE-3276 (R-3023) — a QUIET sound is heard within its carry AND the ear range; a LOUD one within its carry × the weather's
        //   masking alone, whatever the ear range.
        //   ⭐ CE-3282 — <paramref name="seed"/> varies the error: the tick for a continuous sound, the ring serial for a short one (two shots
        //   heard in one solve are misheard apart, deterministically).
        private static void Sound(Span<EqsResult> candidates, ref int n, in Ears ears, Vector3 where, float carries, SoundKind kind,
                                  byte sourceClass, ulong source, uint seed)
        {
            if (AcousticPerception.IsLoud(sourceClass))
                Hear(candidates, ref n, ears.At, where, carries * ears.Masking, float.MaxValue, ears.PerMeter, kind, sourceClass, source, ears.Listener, seed);
            else
                Hear(candidates, ref n, ears.At, where, carries, ears.Range, ears.PerMeter, kind, sourceClass, source, ears.Listener, seed);
        }

        // One sound: heard when it carries this far AND the ear reaches this far; answered as an anonymous estimate.
        private static void Hear(Span<EqsResult> candidates, ref int n, Vector3 ear, Vector3 where, float carries, float range,
                                 float perMeter, SoundKind kind, byte sourceClass, ulong source, ulong listener, uint tick)
        {
            if (n == candidates.Length || carries <= 0f) return;
            float d = Vector3.Distance(ear, where);
            if (d > carries || d > range) return;
            float radius = AcousticPerception.RadiusAt(d, perMeter);
            var err = AcousticPerception.Error(source, listener, tick, radius);
            candidates[n++] = new EqsResult
            {
                EntityId        = 0L,   // ⛔ anonymous: never the source
                PositionX       = where.X + err.X,
                PositionY       = where.Y + err.Y,
                PositionZ       = where.Z,
                Score           = radius,   // ⭐ the template has no score test: Score CARRIES the uncertainty radius, exactly
                Flags           = (short)(((byte)kind << AcousticPerception.KindShift) | ((sourceClass & 0x7) << AcousticPerception.ClassShift)),
                FlagsMeaningful = (short)(AcousticPerception.KindMask | AcousticPerception.ClassMask),
            };
        }
    }
}
