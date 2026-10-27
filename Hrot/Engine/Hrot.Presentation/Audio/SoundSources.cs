using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
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
        private readonly List<(Entity E, long Clip, float Gain, Vector3 At, Vector3 Vel, byte Priority)> _movers = new();
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
                events.Clear();
            }
            Movers(world, tkb, own, ear, dt, output);
            Rounds(world, own, ear, now);
            Own(world, own, now, dt, output);
            Doors(world, ear, now);
            Voices(world, own, ear, now);
            Boarding(world, own, ear, now);
            Town(world, ear);
            Beds(ear, dt, output);
        }

        /// <summary>Forget everything per-entity (the unit lost).</summary>
        public void Reset()
        {
            _lastBurst.Clear(); _lastPos.Clear(); _passes.Clear();
            _said.Clear(); _saidKnown = false;
            _ownMagazines = -1; _rumble = 0f; _recentNear = 0f;
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

        private void Movers(EntityRepository w, ITkbDatabase? tkb, Entity own, in Ear ear, float dt, IAudioOutput output)
        {
            _movers.Clear();
            MoversHeard = 0;
            if (dt <= 0f || !w.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()) return;
            bool embarked = w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            bool flight = w.IsComponentTypeRegistered<Fdp.Toolkit.Motion.FlightState>();
            foreach (var e in w.Query().With<Fdp.Toolkit.Replication.Components.TkbIdentity>().With<SimTransform>().Build())
            {
                var at = w.GetComponentRO<SimTransform>(e).Position;
                bool had = _lastPos.TryGetValue(e, out var before);
                _lastPos[e] = at;
                if (embarked && w.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(e)) continue;   // its vehicle is heard
                long type = w.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(e).TkbType;
                var loco = Fdp.Toolkit.Motion.Locomotions.Of(w, e);
                var a = Acoustic(tkb, type) is { MovingAudibleRange: > 0f } own_ ? own_ : MovingDefault(loco);
                if (a == null) continue;
                float d = Vector3.Distance(at, ear.Position);
                if (d >= a.MovingAudibleRange) continue;
                // ⭐ CE-1305 TE-11 — the velocity per SIM second (dt is the frame's wall time): an engine keeps its natural pitch in slow motion
                double rate = _mixer.Rate;
                var vel = had && rate > 1e-9 && double.IsFinite(rate) ? (at - before) / (dt * (float)rate) : Vector3.Zero;
                float speed = vel.Length();
                bool airborne = flight && w.HasComponent<Fdp.Toolkit.Motion.FlightState>(e) && w.GetComponentRO<Fdp.Toolkit.Motion.FlightState>(e).Airborne != 0;
                float reference = a.ReferenceSpeed > 0f ? a.ReferenceSpeed : 5f;
                float level = airborne ? 1f : speed > 0.5f ? MathF.Min(1f, speed / reference) : 0f;
                if (level <= 0f) continue;
                bool fixedWing = loco == LocomotionClass.FixedWing;
                long clip = SoundTkbCatalog.MovingFor(tkb, type, a.MovingClass, fixedWing);
                if (SoundTkbCatalog.SoundOf(tkb, clip) is not { } s) continue;
                float g = Spatializer.Gain(d, s.ReferenceM, a.MovingAudibleRange) * (0.35f + 0.65f * level) * Volume(s) * (e == own ? OwnMoverShare : 1f);
                if (g <= 1e-3f) continue;
                _movers.Add((e, clip, g, at, vel, e == own ? SoundTkbCatalog.PriorityOwn : s.Priority));
            }
            // forget the gone
            if (_lastPos.Count > 4 * Math.Max(16, _movers.Count))
                foreach (var gone in new List<Entity>(_lastPos.Keys)) if (!w.IsAlive(gone)) _lastPos.Remove(gone);
            _movers.Sort((x, y) => y.Gain.CompareTo(x.Gain));
            for (int i = 0; i < _movers.Count && i < MaxMovers; i++)
            {
                var m = _movers[i];
                float pitch = (0.9f + 0.25f * MathF.Min(1f, m.Vel.Length() / 15f)) * (m.E == own ? 1f : Spatializer.Doppler(ear, m.At, m.Vel));
                _mixer.Hold(output, m.E.Index + 1L, m.Clip, m.Gain, m.E == own ? 0f : Spatializer.Pan(ear, m.At), pitch, m.Priority);
                MoversHeard++;
            }
        }

        /// <summary>
        /// ⭐ CE-1120 (AU-4) — a type that names no moving sound (most built-in vehicles and soldiers today) is heard by its LOCOMOTION:
        /// a man's steps 60 m, a wheeled engine 400 m, a tracked one 800 m, a rotor 2.5 km, a jet 4 km; null for a body that does not
        /// move by itself (an effect, a weapon on the ground). A type's own <c>AcousticSignatureDto</c> always wins.
        /// </summary>
        public static AcousticSignatureDto? MovingDefault(LocomotionClass loco) => loco switch
        {
            LocomotionClass.Pedestrian => new AcousticSignatureDto { MovingAudibleRange = 60f, MovingClass = SoundSourceClass.Footsteps, ReferenceSpeed = 3f },
            LocomotionClass.Wheeled => new AcousticSignatureDto { MovingAudibleRange = 400f, MovingClass = SoundSourceClass.WheeledEngine, ReferenceSpeed = 10f },
            LocomotionClass.Tracked => new AcousticSignatureDto { MovingAudibleRange = 800f, MovingClass = SoundSourceClass.TrackedEngine, ReferenceSpeed = 8f },
            LocomotionClass.Rotorcraft => new AcousticSignatureDto { MovingAudibleRange = 2500f, MovingClass = SoundSourceClass.Rotor, ReferenceSpeed = 30f },
            LocomotionClass.FixedWing => new AcousticSignatureDto { MovingAudibleRange = 4000f, MovingClass = SoundSourceClass.Rotor, ReferenceSpeed = 100f },
            _ => null,
        };

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
            Bed(tkb, output, -2, SoundTkbCatalog.Birds, MathF.Max(0f, 1f - 2f * _recentNear));   // birds go quiet under fire
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
            double sim = w.HasSingletonUnmanaged<GlobalTime>() ? w.GetSingletonUnmanaged<GlobalTime>().TotalTime : 0.0;
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
