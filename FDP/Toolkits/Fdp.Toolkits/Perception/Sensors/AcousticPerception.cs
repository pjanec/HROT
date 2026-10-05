using System;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Perception.Systems;
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
        private readonly VisionBroadphase _broadphase = new();
        private ISimulationView? _builtFor;
        private uint _builtTick;

        public AcousticSensorGenerator(SpatialHashGrid grid) => _grid = grid;

        public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            var unit = EqsContext.Self(view, observer, sensor);
            if (unit.IsNull || !view.IsAlive(unit) || !view.HasComponent<SimTransform>(unit)) return 0;
            if (!AcousticPerception.Ears(view, observer, out float range, out float perMeter)) return 0;
            if (!ReferenceEquals(view, _builtFor) || view.Tick != _builtTick)
            {
                _broadphase.Rebuild(view, _grid);
                _builtFor  = view;
                _builtTick = view.Tick;
            }

            var ear = view.GetComponentRO<SimTransform>(unit).Position;
            int n = 0;
            foreach (var (_, source) in _broadphase.Select(view, unit, range, fovCos: -1f))
            {
                if (!view.HasComponent<AcousticEmitter>(source)) continue;
                var a  = view.GetComponentRO<AcousticEmitter>(source);
                var at = view.GetComponentRO<SimTransform>(source).Position;
                ulong src = source.PackedValue, lis = unit.PackedValue;
                uint tick = view.Tick;
                if (a.CurrentMovingRange > 0f)
                    Hear(candidates, ref n, ear, at, a.CurrentMovingRange, range, perMeter, SoundKind.Movement, a.MovingClass, src, lis, tick);
                if (a.ShotTimeLeft > 0f)
                    Hear(candidates, ref n, ear, new Vector3(a.ShotX, a.ShotY, a.ShotZ), a.FiringAudibleRange, range, perMeter, SoundKind.Shot, a.FiringClass, src, lis, tick);
                if (a.DetonationTimeLeft > 0f)
                    Hear(candidates, ref n, ear, new Vector3(a.DetonationX, a.DetonationY, a.DetonationZ), a.DetonationAudibleRange, range, perMeter, SoundKind.Detonation, a.DetonationClass, src, lis, tick);
            }
            return n;
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
