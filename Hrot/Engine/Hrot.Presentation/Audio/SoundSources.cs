using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.Creatures;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;

namespace Hrot.UI.Common.Audio
{
    /// <summary>
    /// ⭐ CE-1120 (docs/designs/audio/DESIGN_FPS_Audio.md §2, AU-4, AU-5) — what the operator should hear this frame, as requests to the
    /// <see cref="SoundMixer"/>: the shots and bursts the spawner queued (<see cref="SoundEvents"/>), every audible mover's loop, a round
    /// passing close, the possessed unit's own reload and breathing, and the ambient beds with the distant battle. Reads only what every
    /// map host already has (AU-10). Distances fall to silence at the source's ACOUSTIC range — the AI's hearing range — so the operator
    /// hears what the AI hears.
    /// </summary>
    public sealed class SoundSources
    {
        /// <summary>A shot's audible range when its type names none (m), by the size of its sound.</summary>
        public const float ShotRangeSmall = 800f, ShotRangeMedium = 1500f, ShotRangeLarge = 3000f;

        /// <summary>A burst's audible range when the round names none (m), by its size.</summary>
        public const float BurstRangeSmall = 700f, BurstRangeMedium = 2000f, BurstRangeLarge = 4000f, BurstRangeHuge = 6000f;   // ⭐ CE-1231 — huge

        /// <summary>A round passing this close to the ear cracks (m).</summary>
        public const float PassDistance = RoundPasses.PassDistance;

        /// <summary>An automatic weapon's burst sound is restarted at most this often per shooter (s) — its events come at the behaviour's
        /// cadence, not the gun's (the CE-1105 reason for the streams).</summary>
        public const double BurstEvery = 0.9;

        /// <summary>The most movers heard at once (the loudest), inside the mixer's loop budget.</summary>
        public const int MaxMovers = 8;

        /// <summary>How fast the distant battle's level settles (s).</summary>
        public const float RumbleSeconds = 12f;

        /// <summary>The town hum is heard within this distance of buildings (m); full at <see cref="TownFull"/> buildings in it.</summary>
        public const float TownRadius = 200f;
        public const int TownFull = 12;

        /// <summary>A body boarding or leaving a vehicle is heard (a door shut) this close (m).</summary>
        public const float BoardingHearing = 40f;

        private readonly Func<ITkbDatabase?> _tkb;
        private readonly SoundMixer _mixer;
        private readonly Random _jitter = new(7);
        private readonly Dictionary<Entity, double> _lastBurst = new();
        private readonly Dictionary<Entity, Vector3> _lastPos = new();
        private readonly RoundPasses _passes = new();   // ⭐ CE-1216 — the rounds passing the ear (was _lastRound / _cracked)
        private readonly List<(Entity E, long Clip, float Gain, Vector3 At, Vector3 Vel, byte Priority, byte Loop)> _movers = new();
        private int _ownMagazines = -1;
        private float _rumble;
        private float _recentNear;
        private byte[]? _doors;
        private TerrainWorld? _doorWorld;
        private readonly HashSet<Entity> _aboard = new();
        private readonly Dictionary<Entity, (Fdp.Toolkit.Civilians.UtteranceKind Kind, double Until)> _said = new();
        private bool _saidKnown;
        private bool _aboardKnown;
        private TerrainWorld? _townWorld;
        private Vector3 _townAt = new(float.NaN);

        public SoundSources(Func<ITkbDatabase?> tkb, SoundMixer mixer)
        {
            _tkb = tkb;
            _mixer = mixer;
        }

        /// <summary>Counters for the rails.</summary>
        public int ShotsHeard { get; private set; }
        public int BurstsHeard { get; private set; }
        public int MoversHeard { get; private set; }
        public int Cracks { get; private set; }
        public float RumbleLevel => _rumble;
        public int DoorsHeard { get; private set; }
        public int BoardingsHeard { get; private set; }
        /// <summary>⭐ CE-2372 — voice lines played (a call, a command, a chant).</summary>
        public int VoicesHeard { get; private set; }

        /// <summary>How built-up it is around the ear, 0…1 — the town bed's level (re-measured when the ear moves 25 m).</summary>
        public float TownLevel { get; private set; }

        /// <summary>The ambient beds' level (AU-9 "ambient" is applied by the mixer's category volume).</summary>
        public bool Ambient { get; set; } = true;

        /// <summary>One frame: <paramref name="dt"/> the real frame time, <paramref name="now"/> the real clock.</summary>
        public void Collect(EntityRepository world, Entity own, in Ear ear, double now, float dt, IAudioOutput output)
        {
            var tkb = _tkb();
            if (SoundEvents.Of(world) is { } events)
            {
                foreach (var s in events.Shots) Shot(world, tkb, own, ear, now, s);
                foreach (var b in events.Bursts) Burst(tkb, ear, now, b);
                foreach (var c in events.Crashes) Crash(tkb, ear, now, c);   // ⭐ CE-4600 (B-3b)
                foreach (var g in events.Glasses) Glass(tkb, ear, now, g);   // ⭐ CE-5034 (GW-3)
                events.Clear();
            }
            Movers(world, tkb, own, ear, dt, output);
            Rounds(world, own, ear, now);
            Own(world, own, now, dt, output);
            Doors(world, ear, now);
            Voices(world, own, ear, now);
            Animals(world, ear, now);   // ⭐ CE-4623 AU-8
            Dragons(world, ear, now);   // ⭐ CE-4634 (DESIGN_Dragon §8.2 S)
            Boarding(world, own, ear, now);
            Town(world, ear);
            Beds(ear, dt, output);
        }

        /// <summary>Forget everything per-entity (the unit lost).</summary>
        public void Reset()
        {
            _lastBurst.Clear(); _lastPos.Clear(); _passes.Clear();
            _said.Clear(); _saidKnown = false;
            _animalSeen.Clear(); _herdSpoke.Clear(); _animalWorld = null; PerchedNear = 0;
            _ownMagazines = -1; _rumble = 0f; _recentNear = 0f;
            _movers.Clear(); _sinceSearch = float.PositiveInfinity; _moverWorld = null; _typeReach.Clear(); _typeReachAge = 0f;   // ⭐ CE-4560 — the next frame searches
            _doors = null; _doorWorld = null; _aboard.Clear(); _aboardKnown = false; _townWorld = null; _townAt = new(float.NaN); TownLevel = 0f;
        }

        /// <summary>⭐ CE-1141 — the operator's OWN movement (his footsteps, his engine) at this share (was 0.6): it is under him all the time.</summary>
        public const float OwnMoverShare = 0.5f;

        /// <summary>A sound's level: its dB, the operator's category volume and ⭐ <c>CE-1305</c> TE-11 the playback rate's rule (<see cref="SoundMixer.RateGain"/>).</summary>
        private float Volume(SoundDto s) => Spatializer.Db(s.VolumeDb) * _mixer.CategoryVolume[(int)s.Category] * _mixer.RateGain(s.Category);

        private float Jitter(SoundDto s) => 1f + s.PitchJitter * (float)(_jitter.NextDouble() * 2.0 - 1.0);

        private static AcousticSignatureDto? Acoustic(ITkbDatabase? tkb, long type)
            => type != 0 && tkb != null && tkb.TryGetByType(type, out var t) ? t?.GetDescriptor<SignaturesDto>()?.Acoustic : null;

        private static long TypeOf(EntityRepository w, Entity e)
            => w.IsAlive(e) && w.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()
               && w.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(e)
                ? w.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(e).TkbType : 0L;

        /// <summary>The possessed unit fired it — his own weapon, or a weapon system he controls (a station gun).</summary>
        public static bool IsOwn(EntityRepository w, Entity shooter, Entity own)
        {
            if (own.IsNull || shooter.IsNull) return false;
            if (shooter == own) return true;
            return Fdp.Toolkit.Combat.WeaponSystems.IsWeaponSystem(w, shooter) && Fdp.Toolkit.Combat.WeaponSystems.ControllerOf(w, shooter) == own;
        }

        private void Shot(EntityRepository w, ITkbDatabase? tkb, Entity own, in Ear ear, double now, in SoundEvents.Shot s)
        {
            var (near, far, burst) = SoundTkbCatalog.ShotFor(tkb, s.Ammo, s.DamagePerHit, s.ShooterType);
            if (near == 0 && far == 0) return;
            bool mine = IsOwn(w, s.Shooter, own);
            float d = mine ? 0f : Vector3.Distance(s.At, ear.Position);
            float range = Acoustic(tkb, s.ShooterType)?.FiringAudibleRange is > 0f and var r ? r
                : near == SoundTkbCatalog.ShotLarge ? ShotRangeLarge : near == SoundTkbCatalog.ShotMedium ? ShotRangeMedium : ShotRangeSmall;
            if (d >= range) { _rumble = MathF.Min(1f, _rumble + 0.04f); return; }   // out of earshot: it feeds the distant battle
            ShotsHeard++;
            _recentNear = MathF.Min(1f, _recentNear + 0.3f);

            long nearClip = near;
            if (s.Automatic)
            {
                if (_lastBurst.TryGetValue(s.Shooter, out var last) && now - last < BurstEvery) return;
                _lastBurst[s.Shooter] = now;
                nearClip = burst != 0 ? burst : near;
            }
            double delay = mine ? 0.0 : Spatializer.Delay(d);
            float share = Spatializer.NearShare(d);
            if (SoundTkbCatalog.SoundOf(tkb, nearClip) is { } n && share > 0f)
            {
                float g = Spatializer.Gain(d, n.ReferenceM, range) * share * Volume(n) * (mine ? 1.2f : 1f);
                _mixer.Request(nearClip, s.At, !mine, g, Jitter(n), mine ? SoundTkbCatalog.PriorityOwn : n.Priority, now, delay);
            }
            if (!mine && SoundTkbCatalog.SoundOf(tkb, far) is { } f && share < 1f)
            {
                float g = Spatializer.Gain(d, f.ReferenceM, range) * (1f - share) * Volume(f);
                _mixer.Request(far, s.At, true, g, Jitter(f), f.Priority, now, delay);
            }
        }

        /// <summary>⭐ CE-4600 (B-3b, V-17) — a barrier broken by a vehicle: the crash, by distance (a bump open a little quieter).</summary>
        private void Crash(ITkbDatabase? tkb, in Ear ear, double now, in SoundEvents.Crash c)
        {
            if (SoundTkbCatalog.SoundOf(tkb, SoundTkbCatalog.Crash) is not { } s) return;
            float d = Vector3.Distance(c.At, ear.Position);
            if (s.RangeM > 0f && d >= s.RangeM) return;
            CrashesHeard++;
            _mixer.Request(SoundTkbCatalog.Crash, c.At, true, Spatializer.Gain(d, s.ReferenceM, s.RangeM) * Volume(s) * (c.Heavy ? 1f : 0.6f), Jitter(s), s.Priority, now, Spatializer.Delay(d));
        }

        /// <summary>⭐ CE-5034 (glass windows GW-3, D-8) — a pane shattered: the breaking glass, by distance.</summary>
        private void Glass(ITkbDatabase? tkb, in Ear ear, double now, in SoundEvents.Glass g)
        {
            if (SoundTkbCatalog.SoundOf(tkb, SoundTkbCatalog.Glass) is not { } s) return;
            float d = Vector3.Distance(g.At, ear.Position);
            if (s.RangeM > 0f && d >= s.RangeM) return;
            GlassHeard++;
            _mixer.Request(SoundTkbCatalog.Glass, g.At, true, Spatializer.Gain(d, s.ReferenceM, s.RangeM) * Volume(s), Jitter(s), s.Priority, now, Spatializer.Delay(d));
        }

        /// <summary>⭐ CE-5034 — panes heard shattering (a rail).</summary>
        public int GlassHeard { get; private set; }

        /// <summary>⭐ CE-4600 — crashes played (a rail).</summary>
        public int CrashesHeard { get; private set; }

        private void Burst(ITkbDatabase? tkb, in Ear ear, double now, in SoundEvents.Burst b)
        {
            long bang = SoundTkbCatalog.DetonationFor(tkb, b.Ammo, b.Damage);
            float d = Vector3.Distance(b.At, ear.Position);
            if (bang != 0 && SoundTkbCatalog.SoundOf(tkb, bang) is { } s)
            {
                float range = bang == SoundTkbCatalog.ExplosionHuge ? BurstRangeHuge : bang == SoundTkbCatalog.ExplosionLarge ? BurstRangeLarge
                            : bang == SoundTkbCatalog.ExplosionMedium ? BurstRangeMedium : BurstRangeSmall;
                if (d >= range) { _rumble = MathF.Min(1f, _rumble + 0.08f); return; }
                BurstsHeard++;
                _recentNear = MathF.Min(1f, _recentNear + 0.5f);
                _mixer.Request(bang, b.At, true, Spatializer.Gain(d, s.ReferenceM, range) * Volume(s), Jitter(s), s.Priority, now, Spatializer.Delay(d));
                return;
            }
            // a bullet's strike (no blast): a thud on a body, a ricochet off the world
            long hit = b.HitBody ? SoundTkbCatalog.ImpactSoft : SoundTkbCatalog.ImpactHard;
            if (SoundTkbCatalog.SoundOf(tkb, hit) is { } h)
                _mixer.Request(hit, b.At, true, Spatializer.Gain(d, h.ReferenceM, h.RangeM) * Volume(h), Jitter(h), h.Priority, now, Spatializer.Delay(d));
        }

        /// <summary>⭐ CE-4560 (render-perf G-6) — how often the world is searched for moving sources (Hz): the audio's own rate. Between two searches the
        /// sources found are HELD every frame (the mixer stops a loop nobody held), with the gain, pan and pitch of the last search.</summary>
        public const float MoverSearchHz = 20f;

        /// <summary>⭐ CE-4560 — searches for moving sources run (a rail: about <see cref="MoverSearchHz"/> a second, not one a frame).</summary>
        public int MoverSearches { get; private set; }

        private float _sinceSearch = float.PositiveInfinity;
        private bool _followUp;
        private EntityRepository? _moverWorld;
        private EntityQuery? _moverQuery;
        private ITkbDatabase? _acousticTkb;
        private readonly Dictionary<long, AcousticSignatureDto?> _acoustic = new();
        private readonly List<Entity> _goneMovers = new();
        private static readonly Comparison<(Entity E, long Clip, float Gain, Vector3 At, Vector3 Vel, byte Priority, byte Loop)> LouderFirst = (x, y) => y.Gain.CompareTo(x.Gain);

        /// <summary>⭐ CE-4560 — the farthest a body of <paramref name="type"/> can be heard moving (m): the type's own range when it names one; else the widest its
        /// bodies' LOCOMOTION has given so far (a rifleman's steps: 60 m — measured: with the jet's 4 km for every man, a street ear searched the whole army);
        /// else, before any body of it was looked at, the loudest locomotion's (a jet).</summary>
        private float ReachOf(ITkbDatabase? tkb, long type)
        {
            var ac = AcousticCached(tkb, type);
            float moving = ac is { MovingAudibleRange: > 0f } a ? a.MovingAudibleRange
                         : _typeReach.TryGetValue(type, out float seen) ? seen : MaxDefaultRange;
            return MathF.Max(moving, FlameRangeOf(ac));   // ⭐ CE-4635 — a flamer is heard roaring beyond his steps (a man's 60 m)
        }

        /// <summary>⭐ CE-4635 — how far this type's FLAME roar is heard (m): its firing audible range when its firing class is <see cref="Fdp.Toolkit.Tkb.Domain.SoundSourceClass.Flame"/>
        /// (backend's contract: the FlamethrowerSoldier, 200 m); else 0. Read from the cached signature — no TKB walk per entity.</summary>
        private static float FlameRangeOf(AcousticSignatureDto? a)
            => a is { FiringClass: Fdp.Toolkit.Tkb.Domain.SoundSourceClass.Flame, FiringAudibleRange: > 0f } ? a.FiringAudibleRange : 0f;

        private readonly Dictionary<long, float> _typeReach = new();

        /// <summary>⭐ CE-4560 — how often the per-type reach is forgotten and learned again (s): a type whose bodies change locomotion (a man boarding a
        /// helicopter is embarked — skipped — but a new kind of body could share a type) is heard by its widest range again within this.</summary>
        public const float TypeReachSeconds = 10f;
        private float _typeReachAge;

        private const float MaxDefaultRange = 4000f;   // MovingDefault's widest (a fixed wing)

        private AcousticSignatureDto? AcousticCached(ITkbDatabase? tkb, long type)
        {
            if (!ReferenceEquals(tkb, _acousticTkb)) { _acoustic.Clear(); _acousticTkb = tkb; }
            if (_acoustic.TryGetValue(type, out var a)) return a;
            a = Acoustic(tkb, type);
            _acoustic[type] = a;
            return a;
        }

        private void Movers(EntityRepository w, ITkbDatabase? tkb, Entity own, in Ear ear, float dt, IAudioOutput output)
        {
            MoversHeard = 0;
            if (dt <= 0f || !w.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()) { _movers.Clear(); return; }
            // ⭐ CE-4560 (G-6) — the search runs at the audio's rate; between searches the last one's sources are held as they were
            _sinceSearch += dt;
            if (_sinceSearch < 1f / MoverSearchHz && !_followUp && ReferenceEquals(w, _moverWorld)) { HoldMovers(w, own, ear, dt, output); return; }
            _followUp = false;
            float span = float.IsPositiveInfinity(_sinceSearch) ? dt : _sinceSearch;   // the time the positions moved over (the first search: this frame)
            _typeReachAge += span;
            if (_typeReachAge > TypeReachSeconds) { _typeReach.Clear(); _typeReachAge = 0f; }   // learned again from the bodies
            _sinceSearch = 0f;
            MoverSearches++;
            _movers.Clear();
            if (!ReferenceEquals(w, _moverWorld))
            {
                _moverWorld = w;
                _moverQuery = w.Query().With<Fdp.Toolkit.Replication.Components.TkbIdentity>().With<SimTransform>().Build();
                _lastPos.Clear();
            }
            bool embarked = w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            bool flight = w.IsComponentTypeRegistered<Fdp.Toolkit.Motion.FlightState>();
            double simNow = Fdp.Toolkit.Time.SimClock.TotalTimeOf(w);   // ⭐ CE-4635 — the boxing-free reader
            foreach (var e in _moverQuery!)
            {
                var at = w.GetComponentRO<SimTransform>(e).Position;
                long type = w.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(e).TkbType;
                // ⭐ CE-4560 — out of any hearing first: no component, TKB or locomotion read for a body nobody can hear
                float reach = ReachOf(tkb, type);
                if (Vector3.DistanceSquared(at, ear.Position) >= reach * reach) { _lastPos.Remove(e); continue; }
                bool had = _lastPos.TryGetValue(e, out var before);
                _lastPos[e] = at;
                if (!had) _followUp = true;   // first heard: the next frame searches again, so it moves (and sounds) at once, not a search later
                if (embarked && w.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(e)) continue;   // its vehicle is heard
                var loco = Fdp.Toolkit.Motion.Locomotions.Of(w, e);
                var a = AcousticCached(tkb, type) is { MovingAudibleRange: > 0f } own_ ? own_ : MovingDefault(loco);
                bool animalMover = loco is LocomotionClass.Quadruped or LocomotionClass.Bird;   // ⭐ CE-4623 — hooves by gait, wings: the animals' own clips
                // ⭐ CE-4560 — what this type's bodies are heard at, learned from the bodies themselves (a silent locomotion counts as 0)
                float heard = a?.MovingAudibleRange ?? 0f;
                if (!_typeReach.TryGetValue(type, out float known) || heard > known) _typeReach[type] = heard;
                float d = Vector3.Distance(at, ear.Position);
                // ⭐ CE-1305 TE-11 — the velocity per SIM second (span is wall time): an engine keeps its natural pitch in slow motion
                double rate = _mixer.Rate;
                var vel = had && rate > 1e-9 && double.IsFinite(rate) ? (at - before) / (span * (float)rate) : Vector3.Zero;
                // ⭐ CE-4635 (flame weapons §8) — a flamer whose JET is lit (FlameJets.TryGet, the one reader the picture uses too) is HELD as a roar loop — one roar for the burst, not a sample per
                //   fuel shot — under its own mixer key beside the engine's (the flail's precedent), heard standing still and out to the type's FLAME range (200 m), past his steps' 60 m
                if (FlameRangeOf(AcousticCached(tkb, type)) is > 0f and var flameRange && d < flameRange
                    && Fdp.Toolkit.Combat.FlameJets.TryGet(w, e, simNow, out _) && SoundTkbCatalog.SoundOf(tkb, SoundTkbCatalog.FlameRoar) is { } rs)
                {
                    float rg = Spatializer.Gain(d, rs.ReferenceM, flameRange) * Volume(rs) * (e == own ? OwnMoverShare : 1f);
                    if (rg > 1e-3f) _movers.Add((e, SoundTkbCatalog.FlameRoar, rg, at, vel, rs.Priority, LoopFlame));
                }
                if (a == null) continue;
                if (d >= a.MovingAudibleRange) continue;
                // ⭐ CE-4624 (MS-U4) — a mine clearer's FLAIL is a loop of its own while the flail is ON (FlailActivity, the one reader — the drum spins on the same answer), heard even standing,
                //   louder than the engine and at FlailPitch; the engine below is held beside it. ⚠ No sound asset of its own yet: it is the tracked-engine loop, pitched up (a finding).
                if (Hrot.UI.Common.Map3D.FlailActivity.IsOn(w, e) && SoundTkbCatalog.SoundOf(tkb, SoundTkbCatalog.TrackedEngine) is { } fs)
                {
                    float fg = Spatializer.Gain(d, fs.ReferenceM, a.MovingAudibleRange) * Volume(fs) * FlailLoudness * (e == own ? OwnMoverShare : 1f);
                    if (fg > 1e-3f)
                        _movers.Add((e, SoundTkbCatalog.TrackedEngine, fg, at, vel, fs.Priority, LoopFlail));
                }
                float speed = vel.Length();
                bool airborne = flight && w.HasComponent<Fdp.Toolkit.Motion.FlightState>(e) && w.GetComponentRO<Fdp.Toolkit.Motion.FlightState>(e).Airborne != 0;
                float reference = a.ReferenceSpeed > 0f ? a.ReferenceSpeed : 5f;
                float level = airborne ? 1f : speed > 0.5f ? MathF.Min(1f, speed / reference) : 0f;
                if (level <= 0f) continue;
                bool fixedWing = loco == LocomotionClass.FixedWing;
                long clip = animalMover ? AnimalMoveClip(type, loco, speed) : SoundTkbCatalog.MovingFor(tkb, type, a.MovingClass, fixedWing);
                if ((animalMover ? AnimalSoundCatalog.SoundOf(tkb, clip) : SoundTkbCatalog.SoundOf(tkb, clip)) is not { } s) continue;
                float g = Spatializer.Gain(d, s.ReferenceM, a.MovingAudibleRange) * (0.35f + 0.65f * level) * Volume(s) * (e == own ? OwnMoverShare : 1f);
                if (g <= 1e-3f) continue;
                _movers.Add((e, clip, g, at, vel, e == own ? SoundTkbCatalog.PriorityOwn : s.Priority, LoopEngine));
            }
            // forget the gone
            if (_lastPos.Count > 4 * Math.Max(16, _movers.Count))
            {
                _goneMovers.Clear();
                foreach (var gone in _lastPos.Keys) if (!w.IsAlive(gone)) _goneMovers.Add(gone);
                foreach (var gone in _goneMovers) _lastPos.Remove(gone);
            }
            _movers.Sort(LouderFirst);
            HoldMovers(w, own, ear, 0f, output);
        }

        /// <summary>⭐ CE-4560 — the loudest <see cref="MaxMovers"/> sources of the last search held this frame (the mixer stops a loop not held).</summary>
        private void HoldMovers(EntityRepository w, Entity own, in Ear ear, float dt, IAudioOutput output)
        {
            double rate = _mixer.Rate;
            double simNow = Fdp.Toolkit.Time.SimClock.TotalTimeOf(w);
            for (int i = 0; i < _movers.Count && i < MaxMovers; i++)
            {
                var m = _movers[i];
                // ⭐ CE-4635 — the roar follows the JET, not the search: the moment it goes out (≤ 0.25 s after the last fuel shot) the loop is no longer held
                if (m.Loop == LoopFlame && !Fdp.Toolkit.Combat.FlameJets.TryGet(w, m.E, simNow, out _)) continue;
                // between searches the held few follow their source: where it is now and how fast it moves (pan, Doppler) — at most MaxMovers reads
                if (dt > 0f && w.IsAlive(m.E) && w.HasComponent<SimTransform>(m.E))
                {
                    var now = w.GetComponentRO<SimTransform>(m.E).Position;
                    var vel = rate > 1e-9 && double.IsFinite(rate) ? (now - m.At) / (dt * (float)rate) : Vector3.Zero;
                    _movers[i] = m = m with { At = now, Vel = vel };   // (_lastPos keeps the search's sample: the next search measures over its own span)
                }
                float doppler = m.E == own ? 1f : Spatializer.Doppler(ear, m.At, m.Vel);
                float pitch = (m.Loop == LoopFlame ? 1f : 0.9f + 0.25f * MathF.Min(1f, m.Vel.Length() / 15f)) * doppler;   // ⭐ CE-4635 — the roar keeps its own pitch (+ Doppler)
                // ⭐ CE-4624 — the flail loop: its own mixer key (FlailKey, beside the engine's), the engine clip a third higher and louder; ⭐ CE-4635 — the flame roar: FlameKey
                long key = m.Loop == LoopFlail ? FlailKey(m.E) : m.Loop == LoopFlame ? FlameKey(m.E) : m.E.Index + 1L;
                _mixer.Hold(output, key, m.Clip, m.Gain, m.E == own ? 0f : Spatializer.Pan(ear, m.At), m.Loop == LoopFlail ? pitch * FlailPitch : pitch, m.Priority);
                MoversHeard++;
            }
        }

        /// <summary>
        /// ⭐ CE-1120 (AU-4) — a type that names no moving sound (most built-in vehicles and soldiers today) is heard by its LOCOMOTION:
        /// a man's steps 60 m, a wheeled engine 400 m, a tracked one 800 m, a rotor 2.5 km, a jet 4 km; null for a body that does not
        /// move by itself (an effect, a weapon on the ground). A type's own <c>AcousticSignatureDto</c> always wins.
        /// </summary>
        /// <summary>⭐ CE-4624 — how much louder than its engine a mine clearer's flail is, and how much higher (×) its pitch: the tracked-engine loop stands in until an asset is named.</summary>
        public const float FlailLoudness = 1.5f, FlailPitch = 1.7f;

        /// <summary>The mixer key of an entity's flail loop (a key range apart from the engines' <c>Index + 1</c> and the beds' negatives).</summary>
        private static long FlailKey(Entity e) => (1L << 40) + e.Index;

        /// <summary>⭐ CE-4635 — the mixer key of a flamer's roar loop (its own range, beside the engine's and the flail's).</summary>
        public static long FlameKey(Entity e) => (2L << 40) + e.Index;

        /// <summary>The kind of a held mover loop: its body's engine / steps, a mine clearer's flail (CE-4624), a flamethrower's roar (CE-4635).</summary>
        private const byte LoopEngine = 0, LoopFlail = 1, LoopFlame = 2;

        public static AcousticSignatureDto? MovingDefault(LocomotionClass loco) => loco switch
        {
            // ⭐ CE-4560 — one instance each (it was a new one per entity per frame); ⛔ never mutated
            LocomotionClass.Pedestrian => DefaultSteps,
            LocomotionClass.Wheeled => DefaultWheeled,
            LocomotionClass.Tracked => DefaultTracked,
            LocomotionClass.Rotorcraft => DefaultRotor,
            LocomotionClass.FixedWing => DefaultJet,
            LocomotionClass.Quadruped => DefaultHooves,   // ⭐ CE-4623 — hooves (a cow 90 m, a horse the same), by gait
            LocomotionClass.Bird => DefaultWings,
            _ => null,
        };

        private static readonly AcousticSignatureDto DefaultHooves = new() { MovingAudibleRange = 90f, MovingClass = SoundSourceClass.Footsteps, ReferenceSpeed = 6f };
        private static readonly AcousticSignatureDto DefaultWings = new() { MovingAudibleRange = 100f, MovingClass = SoundSourceClass.Footsteps, ReferenceSpeed = 6f };
        private static readonly AcousticSignatureDto DefaultSteps = new() { MovingAudibleRange = 60f, MovingClass = SoundSourceClass.Footsteps, ReferenceSpeed = 3f };
        private static readonly AcousticSignatureDto DefaultWheeled = new() { MovingAudibleRange = 400f, MovingClass = SoundSourceClass.WheeledEngine, ReferenceSpeed = 10f };
        private static readonly AcousticSignatureDto DefaultTracked = new() { MovingAudibleRange = 800f, MovingClass = SoundSourceClass.TrackedEngine, ReferenceSpeed = 8f };
        private static readonly AcousticSignatureDto DefaultRotor = new() { MovingAudibleRange = 2500f, MovingClass = SoundSourceClass.Rotor, ReferenceSpeed = 30f };
        private static readonly AcousticSignatureDto DefaultJet = new() { MovingAudibleRange = MaxDefaultRange, MovingClass = SoundSourceClass.Rotor, ReferenceSpeed = 100f };

        /// <summary>⭐ A2 — a round passing the ear cracks once (⭐ CE-1216 — the detection is <see cref="RoundPasses"/>, shared with the
        /// first-person station's edge darkening).</summary>
        private void Rounds(EntityRepository w, Entity own, in Ear ear, double now)
            => _passes.Scan(w, own, ear.Position, (point, d) =>
            {
                if (SoundTkbCatalog.SoundOf(_tkb(), SoundTkbCatalog.BulletPass) is not { } s) return;
                _mixer.Request(SoundTkbCatalog.BulletPass, point, true, Spatializer.Gain(d, s.ReferenceM, PassDistance * 6f) * Volume(s), Jitter(s), s.Priority, now, 0.0);
                Cracks++;
            });

        private void Own(EntityRepository w, Entity own, double now, float dt, IAudioOutput output)
        {
            if (own.IsNull || !w.IsAlive(own)) return;
            var tkb = _tkb();
            // the reload: a magazine of his refilled
            if (w.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.WeaponAmmo>() && w.HasComponent<Fdp.Toolkit.Combat.Components.WeaponAmmo>(own))
            {
                ref readonly var a = ref w.GetComponentRO<Fdp.Toolkit.Combat.Components.WeaponAmmo>(own);
                int mags = a.Mag0 + a.Mag1 + a.Mag2 + a.Mag3;
                if (_ownMagazines >= 0 && mags > _ownMagazines && SoundTkbCatalog.SoundOf(tkb, SoundTkbCatalog.Reload) is { } r)
                    _mixer.Request(SoundTkbCatalog.Reload, Vector3.Zero, false, Volume(r), Jitter(r), r.Priority, now, 0.0);
                _ownMagazines = mags;
            }
            // breathing when he runs or is hurt
            float speed = dt > 0f ? SpeedOf(w, own, dt) : 0f;
            bool hurt = Fdp.Toolkit.Combat.Injury.IsInTheFight(w, own) && w.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>()
                        && w.HasComponent<Fdp.Toolkit.Combat.Components.Health>(own)
                        && w.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(own).Current < 0.5f * w.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(own).Max;
            if ((speed > 3.5f || hurt) && SoundTkbCatalog.SoundOf(tkb, SoundTkbCatalog.Breathing) is { } b)
                _mixer.Hold(output, -10, SoundTkbCatalog.Breathing, Volume(b) * (hurt ? 1.3f : 1f), 0f, 1f, b.Priority);
        }

        private readonly Dictionary<Entity, Vector3> _speedPos = new();

        private float SpeedOf(EntityRepository w, Entity e, float dt)
        {
            var at = w.GetComponentRO<SimTransform>(e).Position;
            float v = _speedPos.TryGetValue(e, out var before) ? Vector3.Distance(at, before) / dt : 0f;
            _speedPos[e] = at;
            return v;
        }

        private void Beds(in Ear ear, float dt, IAudioOutput output)
        {
            float k = dt > 0f ? MathF.Min(1f, dt / RumbleSeconds) : 0f;
            _rumble *= 1f - k;
            _recentNear *= 1f - MathF.Min(1f, dt / 6f);
            if (!Ambient) return;
            var tkb = _tkb();
            Bed(tkb, output, -1, SoundTkbCatalog.Wind, 1f);
            // ⭐ CE-4623 (AU-8, AN-19) — the birdsong bed grows where birds PERCH (an orchard, a park, a lone tree: flocks and birds perched near the ear) and, like before, falls SILENT after shooting
            Bed(tkb, output, -2, SoundTkbCatalog.Birds, MathF.Max(0f, 1f + 0.6f * MathF.Min(1f, PerchedNear / 3f) - 2f * _recentNear));
            Bed(tkb, output, -3, SoundTkbCatalog.BattleRumble, _rumble);
            Bed(tkb, output, -4, SoundTkbCatalog.Town, TownLevel * MathF.Max(0.3f, 1f - _recentNear));   // a town goes quieter under fire, never silent
        }

        /// <summary>A door whose state changed (DESIGN_Building_Interiors.md §3j's <see cref="DoorState"/>, as this view sees it): a door
        /// sound where it is — opening or shutting at full level, a lock turning on a closed door at half. A door blown in is heard by its
        /// blast. The first frame only learns the states.</summary>
        private void Doors(EntityRepository w, in Ear ear, double now)
        {
            if (DoorStates.Of(w) is not { } states || !w.HasSingletonManaged<TerrainWorld>() || w.GetSingletonManaged<TerrainWorld>() is not { } terrain)
            { _doors = null; return; }
            if (_doors == null || !ReferenceEquals(_doorWorld, terrain) || _doors.Length != states.Count)
            {
                _doors = new byte[states.Count];
                for (int i = 0; i < _doors.Length; i++) _doors[i] = (byte)states[i];
                _doorWorld = terrain;
                return;
            }
            var s = SoundTkbCatalog.SoundOf(_tkb(), SoundTkbCatalog.Door);
            for (int i = 0; i < _doors.Length; i++)
            {
                var now_ = states[i];
                var was = (TerrainDoorState)_doors[i];
                if (now_ == was) continue;
                _doors[i] = (byte)now_;
                if (now_ == TerrainDoorState.Destroyed || was == TerrainDoorState.Destroyed || s == null || i >= terrain.Doors.Count) continue;
                var def = terrain.Doors[i];
                var at = new Vector3(def.Center, def.SillZ + 1f);
                float d = Vector3.Distance(at, ear.Position);
                float level = (was == TerrainDoorState.Open) != (now_ == TerrainDoorState.Open) ? 1f : 0.5f;
                float g = Spatializer.Gain(d, s.ReferenceM, s.RangeM > 0f ? s.RangeM : 60f) * Volume(s) * level;
                if (g <= 1e-3f) continue;
                _mixer.Request(SoundTkbCatalog.Door, at, true, g, Jitter(s), s.Priority, now, Spatializer.Delay(d));
                DoorsHeard++;
            }
        }

        /// <summary>A body that boarded or left a vehicle (<c>IsEmbarkedTag</c> came or went) within <see cref="BoardingHearing"/>: a
        /// vehicle door, where he is. The possessed unit's own boarding plays centred.</summary>
        private void Boarding(EntityRepository w, Entity own, in Ear ear, double now)
        {
            if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>()) { _aboard.Clear(); _aboardKnown = false; return; }
            var aboard = new HashSet<Entity>();
            foreach (var e in w.Query().With<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>().Build()) aboard.Add(e);
            if (_aboardKnown)
            {
                var s = SoundTkbCatalog.SoundOf(_tkb(), SoundTkbCatalog.Door);
                foreach (var e in aboard) if (!_aboard.Contains(e)) Board(w, e, own, ear, now, s);
                foreach (var e in _aboard) if (!aboard.Contains(e) && w.IsAlive(e)) Board(w, e, own, ear, now, s);
            }
            _aboard.Clear();
            _aboard.UnionWith(aboard);
            _aboardKnown = true;
        }

        private void Board(EntityRepository w, Entity e, Entity own, in Ear ear, double now, SoundDto? s)
        {
            if (s == null || !w.HasComponent<SimTransform>(e)) return;
            bool mine = e == own;
            var at = w.GetComponentRO<SimTransform>(e).Position;
            float d = mine ? 0f : Vector3.Distance(at, ear.Position);
            if (d >= BoardingHearing) return;
            float g = Spatializer.Gain(d, s.ReferenceM, BoardingHearing) * Volume(s);
            if (g <= 1e-3f) return;
            _mixer.Request(SoundTkbCatalog.Door, at, !mine, g, Jitter(s), mine ? SoundTkbCatalog.PriorityOwn : s.Priority, now, mine ? 0.0 : Spatializer.Delay(d));
            BoardingsHeard++;
        }

        /// <summary>A man's mouth above his feet (m) — where his voice comes from.</summary>
        public const float MouthHeight = 1.6f;

        /// <summary>
        /// ⭐ CE-2372 (AQ105 F, R-2080; DESIGN_FPS_Audio §9) — what people SAY, from the replicated <see cref="Fdp.Toolkit.Civilians.Utterance"/>
        /// as <c>OverheadMarkGizmo</c> reads it: a NEW utterance (its kind or its end changed, and it is not over) plays its line ONCE at the
        /// speaker's mouth, in his side's voice (<see cref="VoiceTkbCatalog.VoiceOf"/>); a contact call names the side the contact is on,
        /// relative to the speaker's facing. The possessed unit's own voice plays centred. The first frame only learns what is being said.
        /// </summary>
        private void Voices(EntityRepository w, Entity own, in Ear ear, double now)
        {
            if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Civilians.Utterance>()) { _said.Clear(); _saidKnown = false; return; }
            double sim = w.HasSingletonUnmanaged<GlobalTime>() ? w.GetSingletonUnmanagedRO<GlobalTime>().TotalTime : 0.0;
            var tkb = _tkb();
            var seen = new HashSet<Entity>();
            foreach (var e in w.Query().With<Fdp.Toolkit.Civilians.Utterance>().Build())
            {
                ref readonly var u = ref w.GetComponentRO<Fdp.Toolkit.Civilians.Utterance>(e);
                seen.Add(e);
                bool fresh = !_said.TryGetValue(e, out var was) || was.Kind != u.Kind || was.Until != u.Until;
                _said[e] = (u.Kind, u.Until);
                if (!_saidKnown || !fresh || sim >= u.Until || !w.HasComponent<SimTransform>(e)) continue;
                ref readonly var tf = ref w.GetComponentRO<SimTransform>(e);
                var force = w.HasComponent<EntityInfo>(e) ? w.GetComponentRO<EntityInfo>(e).ForceId : ForceId.Neutral;
                int voice = VoiceTkbCatalog.VoiceOf(force);
                VoiceTkbCatalog.ContactSide? side = VoiceTkbCatalog.IsContact(u.Kind) && w.IsAlive(u.Target) && w.HasComponent<SimTransform>(u.Target)
                    ? SideOf(tf, w.GetComponentRO<SimTransform>(u.Target).Position) : null;
                long clip = VoiceTkbCatalog.ClipFor(voice, u.Kind, side);
                if (clip == 0 || VoiceTkbCatalog.SoundOf(tkb, clip) is not { } s) continue;
                bool mine = e == own;
                var at = tf.Position + new Vector3(0f, 0f, MouthHeight);
                float d = mine ? 0f : Vector3.Distance(at, ear.Position);
                float g = Spatializer.Gain(d, s.ReferenceM, s.RangeM > 0f ? s.RangeM : Fdp.Toolkit.Perception.Sensors.AcousticPerception.VoiceRange) * Volume(s);
                if (g <= 1e-3f) continue;
                _mixer.Request(clip, at, !mine, g, Jitter(s), mine ? SoundTkbCatalog.PriorityOwn : s.Priority, now, mine ? 0.0 : Spatializer.Delay(d));
                VoicesHeard++;
            }
            if (_said.Count != seen.Count) foreach (var e in new List<Entity>(_said.Keys)) if (!seen.Contains(e)) _said.Remove(e);
            _saidKnown = true;
        }

        /// <summary>Which side of a man facing <paramref name="tf"/>'s heading <paramref name="at"/> lies: front / left / right / rear by
        /// quadrant (his forward is his body's +x, his left +y).</summary>
        public static VoiceTkbCatalog.ContactSide SideOf(in SimTransform tf, Vector3 at)
        {
            var fwd = Vector3.Transform(Vector3.UnitX, tf.Rotation);
            var left = Vector3.Transform(Vector3.UnitY, tf.Rotation);
            var to = at - tf.Position;
            float f = Vector3.Dot(to, fwd), l = Vector3.Dot(to, left);
            if (MathF.Abs(f) >= MathF.Abs(l)) return f >= 0f ? VoiceTkbCatalog.ContactSide.Front : VoiceTkbCatalog.ContactSide.Rear;
            return l >= 0f ? VoiceTkbCatalog.ContactSide.Left : VoiceTkbCatalog.ContactSide.Right;
        }

        // ═══ ⭐ CE-4634 (DESIGN_Dragon §8.2 S) — what a DRAGON sounds like ═════════════════════════════════════════════════════════════════════════════════════════════════

        private struct DragonSeen { public long Strokes, Calls; public byte Airborne; public bool Dead; public float Health; public double Burst, LastRoar, Sim; public Vector3 At; }
        private readonly Dictionary<Entity, DragonSeen> _dragonSeen = new();
        private readonly List<Entity> _dragonGone = new();
        private EntityRepository? _dragonWorld;
        private EntityQuery? _dragonQuery;
        private int _dragonFrame;

        /// <summary>⭐ CE-4634 — the dragons' sounds played so far (rails): a whoosh per down-stroke, a roar, a dying roar.</summary>
        public int DragonWhooshes { get; private set; }
        public int DragonRoars { get; private set; }
        public int DragonDeathRoars { get; private set; }

        /// <summary>A dragon roars on its own now and then in flight — about once in this many sim seconds (its phase seeded by its id) — and never twice inside <see cref="RoarQuietSeconds"/> (a death roar always plays).</summary>
        public const double RoarEverySeconds = 20.0, RoarQuietSeconds = 3.0;

        /// <summary>
        /// ⭐ CE-4634 (DESIGN_Dragon §8.2 S; NOTE_Backend_To_Ui_Dragon_Visuals) — a winged beast (a type whose own acoustic MOVING class is <see cref="SoundSourceClass.Roar"/>: the TKB says what it is, not
        /// a type id) as the operator hears it, every sound from sim state: ⭐ a WHOOSH on each down-stroke while it beats its wings (the stroke count is <see cref="Hrot.UI.Common.Map3D.DragonPoses"/>' sim-time
        /// phase and its beat rule — the same the look draws, so the whoosh lands on the drawn stroke; gliding and landed: none); a ROAR when it starts to breathe fire (beside the flame's own roar), when it
        /// takes off (<c>Airborne</c> 0 → 1), when it is hit (a <c>Health</c> drop), and now and then in flight (a closed-form count, so a seek or a late join hears the same); a DYING ROAR once, when it dies.
        /// The first frame only learns its state. 0 B in the steady state.
        /// </summary>
        private void Dragons(EntityRepository w, in Ear ear, double now)
        {
            var tkb = _tkb();
            if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()) { _dragonSeen.Clear(); return; }
            if (!ReferenceEquals(w, _dragonWorld))
            {
                _dragonWorld = w; _dragonSeen.Clear();
                _dragonQuery = w.Query().With<Fdp.Toolkit.Replication.Components.TkbIdentity>().With<SimTransform>().Build();
            }
            double sim = Fdp.Toolkit.Time.SimClock.TotalTimeOf(w);
            bool flight = w.IsComponentTypeRegistered<Fdp.Toolkit.Motion.FlightState>();
            bool health = w.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>();
            bool netId = w.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            foreach (var e in _dragonQuery!)
            {
                long type = w.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(e).TkbType;
                if (AcousticCached(tkb, type) is not { MovingClass: SoundSourceClass.Roar }) continue;
                var at = w.GetComponentRO<SimTransform>(e).Position;
                bool known = _dragonSeen.TryGetValue(e, out var seen);
                var fs = flight && w.HasComponent<Fdp.Toolkit.Motion.FlightState>(e) ? w.GetComponentRO<Fdp.Toolkit.Motion.FlightState>(e) : default;
                bool airborne = fs.Airborne != 0;
                float hp = health && w.HasComponent<Fdp.Toolkit.Combat.Components.Health>(e) ? w.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(e).Current : 1f;
                bool dead = health && Fdp.Toolkit.Combat.CombatLife.IsDestroyed(w, e);
                double burst = Fdp.Toolkit.Combat.FlameJets.TryGet(w, e, sim, out var jet) ? jet.BurstStartedAt : seen.Burst;
                uint key = netId && w.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e) && w.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value is int nid && nid != 0 ? (uint)nid : (uint)e.Index;
                double phase = (key * 2654435761u % 1000u) / 1000.0 * RoarEverySeconds;
                long calls = (long)Math.Floor((sim + phase) / RoarEverySeconds);
                long strokes = (long)Math.Floor(sim * Hrot.UI.Common.Map3D.DragonPoses.BeatHz - 0.25);   // a down-stroke begins a quarter beat in (the wing at its top)
                double dt = sim - seen.Sim;
                float speed = known && dt > 1e-4 ? new Vector2(at.X - seen.At.X, at.Y - seen.At.Y).Length() / (float)dt : 0f;
                var next = new DragonSeen { Strokes = strokes, Calls = calls, Airborne = fs.Airborne, Dead = dead, Health = hp, Burst = burst, LastRoar = seen.LastRoar, Sim = sim, At = at };
                if (known && !seen.Dead)
                {
                    float d = Vector3.Distance(at, ear.Position);
                    if (dead) { if (DragonSound(tkb, SoundTkbCatalog.DragonDeathRoar, at, d, now)) DragonDeathRoars++; }
                    else
                    {
                        bool roar = (airborne && seen.Airborne == 0)                         // a take-off
                                    || hp < seen.Health - 0.5f                               // hit
                                    || burst > seen.Burst + 0.5                              // a new breath
                                    || (airborne && calls > seen.Calls);                     // now and then in flight
                        if (roar && sim - seen.LastRoar >= RoarQuietSeconds && DragonSound(tkb, SoundTkbCatalog.DragonRoar, at, d, now)) { DragonRoars++; next.LastRoar = sim; }
                        if (airborne && strokes > seen.Strokes && Hrot.UI.Common.Map3D.DragonPoses.BeatTarget(speed, fs.VerticalSpeed) > 0f
                            && DragonSound(tkb, SoundTkbCatalog.DragonWhoosh, at, d, now)) DragonWhooshes++;
                    }
                }
                _dragonSeen[e] = next;
            }
            if (_dragonSeen.Count > 16 && ++_dragonFrame % 300 == 0)
            {
                _dragonGone.Clear();
                foreach (var g in _dragonSeen.Keys) if (!w.IsAlive(g)) _dragonGone.Add(g);
                foreach (var g in _dragonGone) _dragonSeen.Remove(g);
            }
        }

        /// <summary>Plays a dragon's one-shot <paramref name="clip"/> at <paramref name="at"/>, <paramref name="d"/> m from the ear; false when out of its reach.</summary>
        private bool DragonSound(ITkbDatabase? tkb, long clip, Vector3 at, float d, double now)
        {
            if (SoundTkbCatalog.SoundOf(tkb, clip) is not { } s) return false;
            float g = Spatializer.Gain(d, s.ReferenceM, s.RangeM) * Volume(s);
            if (g <= 1e-3f) return false;
            _mixer.Request(clip, at, true, g, Jitter(s), s.Priority, now, Spatializer.Delay(d));
            return true;
        }

        // ═══ ⭐ CE-4623 (animals AU-8, AN-19) — what the animals SAY, as the operator hears it ════════════════════════════════════════════════════════════════════════════

        private readonly Dictionary<Entity, (long Calls, AnimalMode Mode, FlockMode Flock, long Barks)> _animalSeen = new();
        private readonly Dictionary<Entity, double> _herdSpoke = new();
        private readonly List<Entity> _animalGone = new();
        private EntityRepository? _animalWorld;
        private EntityQuery? _animalQuery, _flockQuery;
        private int _animalFrame, _animalSpoke;

        /// <summary>⭐ CE-4623 — animal sounds played by the last frame (calls, alarms, barks, take-offs) and flocks / birds perched within <see cref="PerchedReach"/> of the ear (the birdsong bed's lift).</summary>
        public int AnimalSoundsHeard { get; private set; }
        public float PerchedNear { get; private set; }

        /// <summary>A species' rest calls are only considered inside this fraction of its range beyond which it cannot be heard anyway; a herd speaks at most once in this many seconds; all herds together at most <see cref="MaxAnimalSoundsPerFrame"/> a frame.</summary>
        public const float HerdQuietSeconds = 2.5f, PerchedReach = 80f;
        public const int MaxAnimalSoundsPerFrame = 3;

        /// <summary>The hooves' (quadruped) or wings' (bird) clip of a body of TKB <paramref name="type"/> moving at <paramref name="speed"/>: the species' own, the gait's line; 0 for a type that is no animal.</summary>
        private static long AnimalMoveClip(long type, LocomotionClass loco, float speed)
        {
            int species = AnimalSoundCatalog.SpeciesOfEntityType(type);
            if (species < 0) return 0;
            if (loco == LocomotionClass.Bird) return AnimalSoundCatalog.TypeOf(species, AnimalSoundCatalog.Wings);
            var family = species switch { 0 => Hrot.UI.Common.Map3D.VisualFamily.Cattle, 1 => Hrot.UI.Common.Map3D.VisualFamily.Sheep, 2 or 3 or 4 => Hrot.UI.Common.Map3D.VisualFamily.Horse,
                                          5 => Hrot.UI.Common.Map3D.VisualFamily.Dog, 6 => Hrot.UI.Common.Map3D.VisualFamily.Cat, 10 => Hrot.UI.Common.Map3D.VisualFamily.Pig, _ => Hrot.UI.Common.Map3D.VisualFamily.Goat };
            var bands = Hrot.UI.Common.Map3D.QuadrupedGaits.BandsOf(family);
            return AnimalSoundCatalog.TypeOf(species, AnimalSoundCatalog.HoovesLine(speed, bands.WalkMax, bands.TrotMax));
        }

        private void Speak(EntityRepository w, ITkbDatabase? tkb, Entity e, Entity herd, int species, int line, in Ear ear, Vector3 at, double now)
        {
            if (_animalSpoke >= MaxAnimalSoundsPerFrame) return;
            long clip = AnimalSoundCatalog.TypeOf(species, line);
            if (AnimalSoundCatalog.SoundOf(tkb, clip) is not { } s) return;
            Entity group = herd.IsNull ? e : herd;
            float quietFor = line == AnimalSoundCatalog.Call ? HerdQuietSeconds : line == AnimalSoundCatalog.Alarm ? 1f : 0f;   // a herd is not a choir: rest calls once in 2.5 s, alarms once a second; a bark is a warning, never throttled
            if (quietFor > 0f && _herdSpoke.TryGetValue(group, out double last) && now - last < quietFor) return;
            float d = Vector3.Distance(at, ear.Position);
            float g = Spatializer.Gain(d, s.ReferenceM, s.RangeM) * Volume(s);
            if (g <= 1e-3f) return;
            if (line is AnimalSoundCatalog.Call or AnimalSoundCatalog.Alarm) _herdSpoke[group] = now;
            _mixer.Request(clip, at, true, g, Jitter(s), s.Priority, now, Spatializer.Delay(d));
            _animalSpoke++; AnimalSoundsHeard++;
        }

        /// <summary>
        /// ⭐ CE-4623 (AU-8, AN-19, AR-7) — the animals' calls as the operator hears them: a REST CALL when an animal's closed-form call count goes up (<see cref="AnimalSoundCatalog.CallsMade"/> — a pure function of its
        /// key and sim time, so a seek or a late join hears the same), an ALARM when its brain turns Alert / Flee (a hen's cackle, a bellow, a whinny), a dog's BARKS while it guards, a flock's WINGS as it takes off. Calls fall
        /// silent after shooting (<c>_recentNear</c>), a herd speaks at most once in <see cref="HerdQuietSeconds"/>, and a frame plays at most <see cref="MaxAnimalSoundsPerFrame"/>. Hooves and wings in flight are movers
        /// (<see cref="Movers"/>). The first frame only learns what the animals are doing.
        /// </summary>
        private void Animals(EntityRepository w, in Ear ear, double now)
        {
            AnimalSoundsHeard = 0; _animalSpoke = 0; PerchedNear = 0f;
            bool hasAnimal = w.IsComponentTypeRegistered<AnimalState>(), hasFlock = w.IsComponentTypeRegistered<FlockState>();
            if (!hasAnimal && !hasFlock) { _animalSeen.Clear(); return; }
            double sim = w.HasSingletonUnmanaged<GlobalTime>() ? w.GetSingletonUnmanagedRO<GlobalTime>().TotalTime : 0.0;
            var tkb = _tkb();
            if (!ReferenceEquals(w, _animalWorld))
            {
                _animalWorld = w; _animalSeen.Clear(); _herdSpoke.Clear();
                _animalQuery = hasAnimal ? w.Query().With<AnimalState>().With<SimTransform>().Build() : null;
                _flockQuery = hasFlock ? w.Query().With<FlockState>().With<SimTransform>().Build() : null;
            }
            bool quiet = _recentNear > 0.3f;   // after shooting: no song, no calls — the alarm itself is what is heard
            bool netId = w.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            if (_animalQuery != null)
                foreach (var e in _animalQuery)
                {
                    ref readonly var st = ref w.GetComponentRO<AnimalState>(e);
                    if (!AnimalSpeciesOf.TryOf(w, tkb, e, out var animalSpecies)) continue;   // ⭐ CE-4628 — the species is the TKB type's (a per-type table), not state
                    int species = (int)animalSpecies;
                    var at = w.GetComponentRO<SimTransform>(e).Position;
                    float d = Vector3.Distance(at, ear.Position);
                    bool known = _animalSeen.TryGetValue(e, out var seen);
                    uint key = netId && w.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e) && w.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value is int nid && nid != 0 ? (uint)nid : (uint)e.Index;
                    long calls = AnimalSoundCatalog.CallsMade(species, key, 0u, sim);
                    long barks = st.Mode == AnimalMode.Guard ? (long)Math.Floor(sim / 1.2) : 0L;
                    if (species == (int)AnimalSpecies.Bird && st.Mode == AnimalMode.Calm && d < PerchedReach) PerchedNear += 1f;
                    _animalSeen[e] = (calls, st.Mode, seen.Flock, barks);
                    if (!known || d > AnimalSoundCatalog.CallRange(species) * 1.5f) continue;
                    var herd = w.IsComponentTypeRegistered<GroupMember>() && w.HasComponent<GroupMember>(e) ? w.GetComponentRO<GroupMember>(e).Group : Entity.Null;
                    if ((st.Mode == AnimalMode.Alert || st.Mode == AnimalMode.Flee) && seen.Mode != st.Mode && seen.Mode is AnimalMode.Calm or AnimalMode.Recover or AnimalMode.Rest)
                    {
                        Speak(w, tkb, e, herd, species, AnimalSoundCatalog.Alarm, ear, at, now);
                        if (species == (int)AnimalSpecies.Hen && st.Mode == AnimalMode.Flee) Speak(w, tkb, e, Entity.Null, species, AnimalSoundCatalog.Flutter, ear, at, now);
                    }
                    else if (st.Mode == AnimalMode.Guard && barks > seen.Barks) Speak(w, tkb, e, Entity.Null, species, AnimalSoundCatalog.Special, ear, at, now);
                    else if (calls > seen.Calls && st.Mode is AnimalMode.Calm or AnimalMode.Recover && !quiet) Speak(w, tkb, e, herd, species, AnimalSoundCatalog.Call, ear, at, now);
                }
            if (_flockQuery != null)
                foreach (var e in _flockQuery)
                {
                    ref readonly var fl = ref w.GetComponentRO<FlockState>(e);
                    var at = w.GetComponentRO<SimTransform>(e).Position;
                    float d = Vector3.Distance(at, ear.Position);
                    bool known = _animalSeen.TryGetValue(e, out var seen);
                    uint key = (uint)e.Index ^ fl.Seed;
                    long calls = AnimalSoundCatalog.CallsMade((int)AnimalSpecies.BirdFlock, key, fl.Seed, sim);
                    if (fl.Mode == FlockMode.Perched && d < PerchedReach) PerchedNear += 1f;
                    _animalSeen[e] = (calls, seen.Mode, fl.Mode, 0L);
                    if (!known || d > AnimalSoundCatalog.CallRange((int)AnimalSpecies.BirdFlock) * 1.5f) continue;
                    if (fl.Mode == FlockMode.Flying && seen.Flock != FlockMode.Flying)   // a take-off: the wings, and the flock's alarm chatter
                    {
                        Speak(w, tkb, e, Entity.Null, (int)AnimalSpecies.BirdFlock, AnimalSoundCatalog.Wings, ear, at, now);
                        Speak(w, tkb, e, Entity.Null, (int)AnimalSpecies.BirdFlock, AnimalSoundCatalog.Alarm, ear, at, now);
                    }
                    else if (calls > seen.Calls && fl.Mode != FlockMode.Flying && !quiet) Speak(w, tkb, e, Entity.Null, (int)AnimalSpecies.BirdFlock, AnimalSoundCatalog.Call, ear, at, now);
                }
            if (_animalSeen.Count > 4 * Math.Max(16, _animalSeen.Count / 4) && ++_animalFrame % 300 == 0)
            {
                _animalGone.Clear();
                foreach (var g in _animalSeen.Keys) if (!w.IsAlive(g)) _animalGone.Add(g);
                foreach (var g in _animalGone) { _animalSeen.Remove(g); _herdSpoke.Remove(g); }
            }
        }

        /// <summary>How built-up it is: the buildings (placed buildings and building prisms) within <see cref="TownRadius"/> of the ear.</summary>
        private void Town(EntityRepository w, in Ear ear)
        {
            if (!w.HasSingletonManaged<TerrainWorld>() || w.GetSingletonManaged<TerrainWorld>() is not { } terrain) { TownLevel = 0f; _townWorld = null; return; }
            if (ReferenceEquals(terrain, _townWorld) && Vector3.DistanceSquared(_townAt, ear.Position) < 25f * 25f) return;
            _townWorld = terrain;
            _townAt = ear.Position;
            TownLevel = Built(terrain, new Vector2(ear.Position.X, ear.Position.Y));
        }

        /// <summary>0…1: the buildings within <see cref="TownRadius"/> of <paramref name="at"/>, full at <see cref="TownFull"/>.</summary>
        public static float Built(TerrainWorld terrain, Vector2 at)
        {
            const float r2 = TownRadius * TownRadius;
            int n = 0;
            foreach (var b in terrain.Buildings) if (Vector2.DistanceSquared(b.Position, at) < r2) n++;
            foreach (var p in terrain.Prisms)
                if (p.Kind == TerrainPrismKind.Building && Vector2.DistanceSquared((p.Min + p.Max) * 0.5f, at) < r2) n++;
            return MathF.Min(1f, n / (float)TownFull);
        }

        private void Bed(ITkbDatabase? tkb, IAudioOutput output, long key, long clip, float level)
        {
            if (SoundTkbCatalog.SoundOf(tkb, clip) is not { } s) return;
            _mixer.Hold(output, key, clip, level <= 0.02f ? 0f : Volume(s) * level, 0f, 1f, s.Priority);
        }
    }
}
