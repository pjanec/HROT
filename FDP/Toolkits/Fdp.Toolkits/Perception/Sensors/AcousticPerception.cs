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

        /// <summary>⭐ <c>CE-3283</c> (Q95 A; Sensing §8.4) — the flag bits an answer's <c>SoundFamily</c> is written in (bits 13–15, the last free
        /// ones) — set only when the listener's ear can tell it (a Regular or better).</summary>
        public const short FamilyShift = 13;
        public const short FamilyMask = unchecked((short)(0x7 << 13));

        /// <summary>⭐ <c>CE-3283</c> — what a listener's ear can tell a sound by, from his proficiency (Q95 A): below Regular the class only, a
        /// Regular + the family, a Veteran or Elite + the weapon type itself.</summary>
        public static byte IdentificationOf(Fdp.Toolkit.Behavior.Components.ProficiencyTier tier)
            => tier >= Fdp.Toolkit.Behavior.Components.ProficiencyTier.Veteran ? (byte)2
             : tier >= Fdp.Toolkit.Behavior.Components.ProficiencyTier.Regular ? (byte)1 : (byte)0;

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

        /// <summary>⭐ AQ105 D (R-2080) — how far a SHOUT carries (m): a man whose <c>Utterance</c> is a shouted kind
        /// (<c>Utterances.IsShouted</c>) is heard by an enemy within this, as long as he speaks. High band (walls take their full share).
        /// ⚠ Heard as a man's sound — kind Movement, class Footsteps: the answer's kind and class bits are all taken (flags 8–15), so a
        /// listener cannot yet tell a voice from steps; both say "a man there".</summary>
        public const float VoiceRange = 40f;

        /// <summary>⭐ AQ104 E ② (R-2079; WQ-E Q3 v1) — the most one wall or slab takes off a sound (dB): it HALVES the distance the sound
        /// carries (20·log10 2), never more than its material stops (a chain-link fence 0, a hedge 5).</summary>
        public const float WallCapDb = 6.0206f;

        /// <summary>⭐ AQ104 E ② — how high above the feet a sound is made and heard (m): a line at the body, so two men on one floor
        /// cross its walls, never its slab, and a man upstairs is heard through the slab between them.</summary>
        public const float BodyHeight = 1.2f;

        /// <summary>⭐ AQ104 E ② — what an obstacle's dB is worth to a sound of <paramref name="sourceClass"/>'s band (Q95 B3: bass passes
        /// walls): Low ×0.2, Mid ×0.6, High ×1. A class with no band counts as Mid.</summary>
        public static float BandFactor(byte sourceClass) => SoundBands.Of((SoundSourceClass)sourceClass) switch
        {
            SoundBand.Low  => 0.2f,
            SoundBand.High => 1.0f,
            _              => 0.6f,
        };

        /// <summary>
        /// ⭐⭐ AQ104 E ② (R-2079; docs/DESIGN_World_Query_Seam.md WQ-E Q3 v1, Sensing §8.8) — the share of its carry a sound made at
        /// <paramref name="source"/> keeps at <paramref name="ear"/> on the STRAIGHT line: <c>10^(−Σ/20)</c> over the walls and slabs it crosses,
        /// each <c>min(its dB, </c><see cref="WallCapDb"/><c>) × </c><see cref="BandFactor"/>. 1 with no world, or nothing between.
        /// ⭐ AQ106 — the ONE implementation is <see cref="SoundPaths.Route"/>; this is its straight half (no buildings given).
        /// </summary>
        public static float WallFactor(Fdp.Toolkit.World.IWorldQuery? world, Vector3 ear, Vector3 source, byte sourceClass)
            => SoundPaths.Route(world, null, null, ear, source, sourceClass).Factor;

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

        // ⭐ AQ105 D — a voice is misheard apart from the same man's steps in one solve
        private const uint VoiceSeed = 0x5A17C0DEu;

        public AcousticSensorGenerator(SpatialHashGrid grid) => _grid = grid;

        public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            var unit = EqsContext.Self(view, observer, sensor);
            if (!TryEars(view, observer, unit, out var ears)) return 0;
            float range = ears.Range;
            var broadphase = _broadphase.For(view, _grid);
            int n = 0;
            // MOVEMENT is continuous: the emitters near the unit, through the grid (§5.1)
            double now = view is EntityRepository clock && clock.HasSingleton<GlobalTime>() ? clock.GetSingleton<GlobalTime>().TotalTime : 0d;
            foreach (var (_, source) in broadphase.Select(view, unit, range, fovCos: -1f))
            {
                // ⭐ AQ105 D — a SHOUT (his replicated Utterance, on whichever node hears): heard while he speaks, like his steps
                if (Fdp.Toolkit.Civilians.Utterances.IsShouted(Fdp.Toolkit.Civilians.Utterances.Of(view, source, now, out _)))
                    Sound(candidates, ref n, in ears, view.GetComponentRO<SimTransform>(source).Position, AcousticPerception.VoiceRange,
                          SoundKind.Movement, (byte)SoundSourceClass.Footsteps, source.PackedValue, ears.Tick ^ VoiceSeed);
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
            long after = known ? view.GetComponentRO<AcousticCursor>(sensorEntity).LastPlace : 0L;
            // ⭐ CE-3293 — read as of THIS view's time (a background solve's snapshot carries its frame's clock), never the writer's: the
            //   main thread goes on appending the dispatching step's sounds while the solve runs, so only what came before it is fixed
            //   (a view with no clock reads everything, as before)
            double asOf = view is EntityRepository clockRepo && clockRepo.HasSingleton<GlobalTime>()
                ? clockRepo.GetSingleton<GlobalTime>().TotalTime : double.PositiveInfinity;
            // a sensor with no cursor yet hears only what is still ringing (the old linger), never the minutes before it existed
            double notBefore = known ? double.MinValue
                : (double.IsPositiveInfinity(asOf) ? ring.Now : asOf) - SoundEmissionSystem.SoundLingerSeconds;
            Span<SoundEmission> sounds = stackalloc SoundEmission[MaxSoundsPerSolve];
            int k = ring.ReadAfter(after, sounds, out long last, notBefore, asOf);
            byte ourForce = view.HasComponent<EntityInfo>(unit) ? (byte)view.GetComponentRO<EntityInfo>(unit).ForceId : (byte)0;
            for (int i = 0; i < k; i++)
            {
                ref readonly var e = ref sounds[i];
                if (n == candidates.Length) { last = e.Serial - 1; break; }   // no room: this one and the rest wait for the next solve
                if (e.Source == unit.PackedValue || e.Force == ourForce) continue;   // not his own sound, not his own side's (the broadphase's rule)
                // ⭐ CE-3292 — the error's seed is the sound's place in THIS timeline: the ring's serial keeps counting across loads, so the
                //   raw serial gave the same shot a different mishearing in every run. ⭐ CE-3315 — the ring hands out the place itself
                //   (it goes on across a restore, where serial − first did not).
                uint seed = unchecked((uint)e.Serial);
                Sound(candidates, ref n, in ears, e.At, e.Carry, (SoundKind)e.Kind, e.Class, e.Source, seed, e.Family, e.Token);
            }
            if (!cursors || (known && last == after)) return;   // ⭐ a sensor's FIRST solve always plants its cursor, even with nothing to read —
            var cmd = view.GetCommandBuffer();                       //   else a sound after a long skip would meet the no-cursor window
            if (known) cmd.SetComponent(sensorEntity, new AcousticCursor { LastPlace = last });
            else cmd.AddComponent(sensorEntity, new AcousticCursor { LastPlace = last });
        }

        /// <summary>⭐ AQ106 L — <paramref name="unit"/>'s ears through the acoustic sensor <paramref name="sensorEntity"/>, as a solve hears
        /// with them (the debug route builds them the same way).</summary>
        internal static bool TryEars(ISimulationView view, Entity sensorEntity, Entity unit, out Ears ears)
        {
            ears = default;
            if (unit.IsNull || !view.IsAlive(unit) || !view.HasComponent<SimTransform>(unit)) return false;
            if (!AcousticPerception.Ears(view, sensorEntity, out float range, out float perMeter)) return false;
            // ⭐ CE-3216 S2-4 (R-3017) — wind and rain mask the listener (none without a clock)
            float masking = PerceptionWeather.TrySample(view, unit, out var weather) ? PerceptionWeather.HearingFactor(in weather) : 1f;
            range *= masking;
            var ear = view.GetComponentRO<SimTransform>(unit).Position;
            ears = new Ears(ear, range, masking, perMeter, unit.PackedValue, PerceptionClock.Now(view),   // ⭐ CE-3292 — the seed: the step
                            AcousticPerception.IdentificationOf(Fdp.Toolkit.Behavior.Components.ProficiencyOf.Tier(view, unit)),
                            Fdp.Toolkit.World.WorldQuery.Of(view),   // ⭐ AQ104 E ② — the walls and slabs between (this view's doors)
                            Terrain(view, out var doors), doors);    // ⭐ AQ106 — the buildings' openings and corners
            return true;
        }

        /// <summary>One listener's ears for this tick: where, how far they reach (masked), the weather's masking, the uncertainty.</summary>
        /// ⭐ CE-3283 — <c>Identifies</c>: 0 the class, 1 + the family, 2 + the type (<see cref="AcousticPerception.IdentificationOf"/>).
        internal readonly record struct Ears(Vector3 At, float Range, float Masking, float PerMeter, ulong Listener, uint Tick, byte Identifies,
                                            Fdp.Toolkit.World.IWorldQuery? World, Fdp.Toolkit.Terrain.TerrainWorld? Terrain,
                                            Fdp.Toolkit.Terrain.DoorStates? Doors);

        // ⭐ AQ106 — the terrain and its doors as this view sees them (null on a node with no terrain)
        private static Fdp.Toolkit.Terrain.TerrainWorld? Terrain(ISimulationView view, out Fdp.Toolkit.Terrain.DoorStates? doors)
        {
            doors = null;
            if (view is not EntityRepository repo || !repo.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>()) return null;
            var world = repo.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>();
            if (world != null && world.Doors.Count > 0) doors = Fdp.Toolkit.Terrain.DoorStates.Of(view, world);
            return world;
        }

        /// <summary>⭐ AQ106 L — the ONE verdict on one sound at these ears: the sensor's <c>Sound</c> and the debug route's explain both call
        /// it, so they cannot disagree. A LOUD class carries past the ear range (× the weather's masking), a quiet one within both.</summary>
        internal static HearingVerdict Judge(in Ears ears, Vector3 where, float carries, byte sourceClass, List<Vector3>? waypoints = null)
        {
            bool loud = AcousticPerception.IsLoud(sourceClass);
            float carry = loud ? carries * ears.Masking : carries, range = loud ? float.MaxValue : ears.Range;
            float d = Vector3.Distance(ears.At, where);
            if (carry <= 0f) return new HearingVerdict(HearingReason.Silent, d, 0f, default);
            if (d > carry) return new HearingVerdict(HearingReason.BeyondItsCarry, d, carry, default);
            if (d > range) return new HearingVerdict(HearingReason.BeyondHisEar, d, carry, default);
            // ⭐ AQ104 E ② / ⭐ AQ106 (R-3050) — the walls and slabs between, or the way round through the openings and corners, whichever
            //   is louder (traced only for a sound heard in the open); it is heard FROM the last opening or corner on that way (F)
            var route = SoundPaths.Route(ears.World, ears.Terrain, ears.Doors, ears.At, where, sourceClass, waypoints);
            float effective = carry * route.Factor;
            return new HearingVerdict(d > effective ? HearingReason.WallsOrCorners : HearingReason.Heard, d, effective, route);
        }

        // ⭐ CE-3276 (R-3023) — a QUIET sound is heard within its carry AND the ear range; a LOUD one within its carry × the weather's
        //   masking alone, whatever the ear range.
        //   ⭐ CE-3282 — <paramref name="seed"/> varies the error: the tick for a continuous sound, the ring serial for a short one (two shots
        //   heard in one solve are misheard apart, deterministically).
        private static void Sound(Span<EqsResult> candidates, ref int n, in Ears ears, Vector3 where, float carries, SoundKind kind,
                                  byte sourceClass, ulong source, uint seed, byte family = 0, ushort token = 0)
        {
            if (n == candidates.Length) return;
            var verdict = Judge(in ears, where, carries, sourceClass);
            if (verdict.Reason != HearingReason.Heard) return;
            Answer(candidates, ref n, verdict.Distance, verdict.Route.HeardFrom, ears.PerMeter, kind, sourceClass, source, ears.Listener, seed);
            // ⭐ CE-3283 (Q95 A) — what THIS ear can tell, decided here, once: downstream carries only that (anonymity by construction, §6 D)
            ref var answer = ref candidates[n - 1];
            if (ears.Identifies >= 1 && family != 0)
            {
                answer.Flags           |= (short)((family & 0x7) << AcousticPerception.FamilyShift);
                answer.FlagsMeaningful |= AcousticPerception.FamilyMask;
            }
            if (ears.Identifies >= 2) answer.SignatureToken = token;
        }

        // One heard sound, answered as an anonymous estimate placed where it seems to come from (⭐ AQ106 F: the last opening or corner).
        private static void Answer(Span<EqsResult> candidates, ref int n, float d, Vector3 from, float perMeter, SoundKind kind,
                                   byte sourceClass, ulong source, ulong listener, uint tick)
        {
            float radius = AcousticPerception.RadiusAt(d, perMeter);
            var err = AcousticPerception.Error(source, listener, tick, radius);
            candidates[n++] = new EqsResult
            {
                EntityId        = 0L,   // ⛔ anonymous: never the source
                PositionX       = from.X + err.X,
                PositionY       = from.Y + err.Y,
                PositionZ       = from.Z,
                Score           = radius,   // ⭐ the template has no score test: Score CARRIES the uncertainty radius, exactly
                Flags           = (short)(((byte)kind << AcousticPerception.KindShift) | ((sourceClass & 0x7) << AcousticPerception.ClassShift)),
                FlagsMeaningful = (short)(AcousticPerception.KindMask | AcousticPerception.ClassMask),
            };
        }
    }
}
