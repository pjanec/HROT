using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Perception.LineOfSight;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.World;
using Fdp.Toolkit.Tkb.Parameters;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// ⭐⭐ Buildings programme Stage 6 (<c>CE-1032</c>, R-225 W-5) — the AREA effect of a detonation, beside the direct hit
    /// (<see cref="DamageCalculationSystem"/>, same module, same phase): for each local <see cref="DetonationNotification"/> whose
    /// munition has a warhead (<see cref="ParameterResolver.Warhead(ITkbDatabase?, long, Tkb.Reference.ReferenceLibrary?)"/>), every
    /// living <see cref="Health"/> entity in reach gets ONE <see cref="DamageAssessedEvent"/> — the existing pipeline to its owner
    /// (R-171); no new damage wire. Doors in the lethal radius with a clear line are breached (W-9). 📄 docs/DESIGN_Building_Interiors.md §3k.
    /// <para>Per target, per body point of its stance (<see cref="BodyProfile.Points"/>): FRAGMENTS through the terrain
    /// (<see cref="TerrainWorld.QueryFire(Vector3, Vector3, List{TerrainWorld.FireCrossing}, DoorStates?)"/> ×
    /// <see cref="AreaEffect.FragmentTransmission"/>) and the colliders (<see cref="ColliderOcclusion"/> — the test sight uses), exposure
    /// = the mean; BLAST (⭐ R-2019: every target, a hull × its <see cref="ArmorModel.BlastSusceptibility"/>) through closed barriers (the least-shielded point) and in the deepest diffraction
    /// shadow on the line to the highest point (terrain and colliders). Expected values (R-212).</para>
    /// <para>⚠ The entity the round struck is IN the burst too (at ~0 m; it is never its own cover) — its direct hit, from
    /// <see cref="DamageCalculationSystem"/>, comes on top: a warhead mount states no hit damage, so that is the flat default. A REMOTE detonation
    /// (<see cref="DetonationNotification.IsRemote"/>) gets no area effect here, as it gets no direct damage.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class AreaEffectSystem : IEcsModuleSystem
    {
        private readonly ColliderOcclusion _occlusion = new();
        private readonly List<TerrainWorld.FireCrossing> _crossings = new();   // BreachDoors — the stand-in's door list (not on the seam yet)
        private readonly List<TraceCrossing> _traces = new();                   // ⭐ CE-1035 Q1 — exposure, through IWorldQuery
        private readonly List<Vector3> _points = new(3);
        private readonly float[] _shares = new float[Fdp.Toolkit.Physics.BodyParts.PartDamage.MaxParts];       // ⭐ CE-3212
        private readonly float[] _partDamage = new float[Fdp.Toolkit.Physics.BodyParts.PartDamage.MaxParts];   // ⭐ CE-3212

        /// <summary>Detonations this system assessed (a rail and a diagnostics counter read it).</summary>
        public long Assessed { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            var events = repo.Bus.Read<DetonationNotification>();
            if (events.Length == 0) return;
            if (!repo.HasSingletonManaged<ITkbDatabase>() || !repo.IsComponentTypeRegistered<Health>()) return;
            var db = repo.GetSingletonManaged<ITkbDatabase>();

            bool built = false;
            TerrainWorld? terrain = null;
            DoorStates? doors = null;
            IWorldQuery? world = null;
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];
                if (evt.IsRemote || evt.Ammo == 0) continue;
                var (warhead, source, _) = ParameterResolver.Warhead(db, evt.Ammo);
                if (warhead == null) continue;
                if (warhead.HasCloud) RequestCloud(repo, in evt, warhead);   // ⭐ CE-3219 part 3 — tear gas / smoke: a cloud that drifts
                if (!warhead.HasAreaEffect) continue;
                if (!built)
                {
                    terrain = repo.HasSingletonManaged<TerrainWorld>() ? repo.GetSingletonManaged<TerrainWorld>() : null;
                    doors = terrain is { Doors.Count: > 0 } ? DoorStates.Of(repo, terrain) : null;
                    world = WorldQuery.Of(repo);
                    bool bullets = repo.IsComponentTypeRegistered<BallisticProjectile>();
                    _occlusion.Build(repo, PhysicsColliderReaders.Radius, PhysicsColliderReaders.BlockerHeight,   // ⭐ CE-2340 — a man lying down shields only his lying height
                        bullets ? static (v, e) => !v.HasComponent<BallisticProjectile>(e) : null);   // a round in flight is not cover
                    built = true;
                }
                Assess(repo, in evt, warhead, source, world, terrain, doors);
                Assessed++;
            }
        }

        /// <summary>
        /// ⭐ <c>CE-3219</c> part 3 (docs/DESIGN_Civilians_And_Crowd_Control.md §7.3b) — a tear-gas or smoke burst asks for a cloud where it
        /// burst (the entity-creation request on the FDP bus, owner 0: the system picks); its owner drifts it, irritates with it and ends it.
        /// </summary>
        private static void RequestCloud(EntityRepository repo, in DetonationNotification evt, Tkb.Domain.WarheadDto w)
        {
            bool gas = w.Kind == Tkb.Domain.WarheadKind.Irritant;
            // ⭐ CE-3236 (B-14, §7.15 VC-2/VC-4) — a canister still lying where it burst is the cloud's SOURCE, and its burn is the cloud's time
            float seconds = w.CloudSeconds;
            long source = 0;
            if (!evt.Round.IsNull && repo.IsAlive(evt.Round) && repo.IsComponentTypeRegistered<BallisticProjectile>()
                && repo.HasComponent<BallisticProjectile>(evt.Round))
            {
                ref readonly var round = ref repo.GetComponentRO<BallisticProjectile>(evt.Round);
                if (round.BurnSeconds > 0f) seconds = round.BurnSeconds;
                if ((round.Warhead & (WarheadRound.Vents | WarheadRound.Landed)) == (WarheadRound.Vents | WarheadRound.Landed)
                    && repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()
                    && repo.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(evt.Round))
                    source = repo.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(evt.Round).Value;
            }
            AreaEffectClouds.Request(repo, new Vector3(evt.HitX, evt.HitY, evt.HitZ), new AreaEffectCloud
            {
                Kind = gas ? CloudKind.TearGas : CloudKind.Smoke,
                Radius = w.CloudRadiusM,
                Height = w.CloudHeightM > 0f ? w.CloudHeightM : 3f,
                SightTransmittance = gas ? 0.4f : 0.1f,
                IrritantSeconds = gas ? MathF.Max(w.EffectSeconds, (float)AreaEffectCloudSystem.StepSeconds + 0.5f) : 0f,
                BornAtSimSeconds = Fdp.Toolkit.Time.SimClock.Of(repo).TotalTime,
                Seconds = seconds,
                SourceNetworkId = source,
            });
        }

        /// <summary>The point of segment <paramref name="a"/>–<paramref name="b"/> closest to <paramref name="q"/>.</summary>
        private static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 q)
        {
            var ab = b - a;
            float len2 = ab.LengthSquared();
            float t = len2 > 1e-8f ? Math.Clamp(Vector3.Dot(q - a, ab) / len2, 0f, 1f) : 0f;
            return a + ab * t;
        }

        private void Assess(EntityRepository repo, in DetonationNotification evt, Tkb.Domain.WarheadDto w, string source,
            IWorldQuery? world, TerrainWorld? terrain, DoorStates? doors)
        {
            var burst = new Vector3(evt.HitX, evt.HitY, evt.HitZ);
            var struck = evt.Target.IsNull ? ColliderOcclusion.Nobody : evt.Target;   // ⚠ never skip "index 0" for a terrain burst
            float reach = MathF.Max(w.FragmentRadiusM, w.BlastInjuryRadiusM);
            var log = DetonationLog.For(repo);
            var record = new DetonationRecord
            {
                Seq = log.NextSeq(), Tick = repo.HasSingleton<GlobalTime>() ? (uint)repo.GetSingleton<GlobalTime>().FrameNumber : 0u,
                Shooter = evt.Shooter, Struck = evt.Target, Burst = burst, Ammo = evt.Ammo,
                Warhead = $"{w.Kind}, {w.ExplosiveKg:0.###} kg", WarheadSource = source,
                FragmentRadius = w.FragmentRadiusM, BlastInjuryRadius = w.BlastInjuryRadiusM,
            };

            foreach (var e in repo.Query().With<Health>().With<SimTransform>().Build())
            {
                if (!repo.IsAlive(e)) continue;   // ⚠ the STRUCK entity is included: it is at the burst (its direct hit is extra)
                if (Fdp.Toolkit.Perception.PhysicalPresence.IsHidden(repo, e)) continue;   // ⭐ CE-3110 I-2c (R-271) — inside an enclosed hull
                if (repo.GetComponentRO<Health>(e).Current <= 0f) continue;
                var tf = repo.GetComponentRO<SimTransform>(e);
                if (Vector2.Distance(new Vector2(burst.X, burst.Y), new Vector2(tf.Position.X, tf.Position.Y)) > reach + 2f) continue;

                var stance = HitModel.LogicalStance(repo, e);
                float hull = PhysicsColliderReaders.HullHeight(repo, e);
                BodyProfile.Points(repo, e, stance, hull, _points);
                float r = float.MaxValue;
                foreach (var p in _points) r = MathF.Min(r, Vector3.Distance(burst, p));

                float fragFall = AreaEffect.FragmentFalloff(r, w.FragmentRadiusM);
                // ⭐ R-2019 D-M2 — blast reaches hulls too, scaled by the hull's susceptibility (a soft skin ≈ 1, an MBT 0) after the SAME
                //   barrier and shadow a person gets (was: personnel only, v1)
                var platform = CombatTkb.PlatformOf(repo, e);
                float susceptibility = ArmorModel.BlastSusceptibility(platform, hull);
                float blastFall = susceptibility > 0f ? AreaEffect.BlastFalloff(r, w.BlastLethalRadiusM, w.BlastInjuryRadiusM) : 0f;
                if (fragFall <= 0f && blastFall <= 0f) continue;

                float exposure = 0f, barrier = 0f, shadow = 1f;
                string? shieldedBy = null;
                for (int i = 0; i < _points.Count; i++)
                {
                    var p = _points[i];
                    if (world != null) world.Trace(burst, p, TracePurpose.Fire, _traces); else _traces.Clear();
                    float ft = fragFall > 0f ? AreaEffect.FragmentTransmission(_traces, w.FragmentPenetrationMm) : 0f;
                    // ⭐ CE-3117 — the first obstacle on the ray, for the map: a terrain piece where the fragments enter it, or a collider
                    Vector3? stopAt = _traces.Count > 0 ? burst + (p - burst) * _traces[0].T : null;
                    if (ft > 0f && _occlusion.Blocking(burst, p, e, struck, out _) is { } blocker)
                    {
                        ft = 0f;
                        shieldedBy ??= $"entity #{blocker.Index}";
                        if (repo.HasComponent<SimTransform>(blocker))
                            stopAt = ClosestOnSegment(burst, p, repo.GetComponentRO<SimTransform>(blocker).Position);
                    }
                    else if (ft <= 0f && fragFall > 0f && _traces.Count > 0)
                        shieldedBy ??= $"{_traces[0].Kind} {_traces[0].Label ?? _traces[0].Material}";
                    if (fragFall > 0f) record.Rays.Add(new DetonationRayRecord(p, ft, stopAt));
                    exposure += ft;
                    barrier = MathF.Max(barrier, AreaEffect.ClosedBarrierTransmission(_traces));
                    if (i == _points.Count - 1)   // the highest point (the fractions ascend)
                        shadow = AreaEffect.TerrainShadow(_traces, tf.Position.Z,
                            Vector2.Distance(new Vector2(burst.X, burst.Y), new Vector2(p.X, p.Y)));
                }
                exposure /= _points.Count;
                if (blastFall > 0f && _occlusion.Shadow(burst, _points[^1], e, struck, _points[^1].Z, tf.Position.Z,
                        AreaEffect.ShadowFactor, out float colliderShadow))
                    shadow = MathF.Min(shadow, colliderShadow);

                // ⭐ M1 — the face toward the burst in 3-D, at the hull's middle: a burst ABOVE a hull meets its roof. Fragments
                //   are kinetic (the jet of a shaped charge is its direct hit, not its fragments).
                var armour = ArmorModel.ArmourFor(platform, ArmorModel.FacingOf(tf, burst, hull * 0.5f));
                // ⭐⭐ CE-3203 (Building Interiors §3k.1, W-6″) — ONE fragment law for a man, a hull (AE5) and a weapon: the fragments that
                //   strike it, the speed they keep, what one does to it. A warhead with no fragment count keeps the old dose.
                var (kind, area, atDistance) = FragmentTargetOf(repo, e, platform, hull, stance, r);
                var strike = AreaEffect.Fragments(w, kind, area, atDistance, armour, exposure);
                float armourChance = strike?.ArmourChance ?? ArmorModel.PenetrationChance(w.FragmentPenetrationMm, armour);
                float fragDamage = strike?.Damage ?? w.FragmentDamage * fragFall * exposure * armourChance;
                float blastDamage = w.BlastLethalDamage * blastFall * barrier * shadow * susceptibility;
                // ⭐⭐ CE-3212 (hit location §10) — a man with a body: the hits land on the parts that FACE the burst, each meeting its zone's
                //   armour (helmet, vest) and multiplier — one damage event per part struck, so PartDamage and the wounds follow. An
                //   unarmoured man's total is the calibrated law's (S3-3).
                bool split = kind == AreaEffect.FragmentTarget.Person && strike is { Hits: > 0f } st
                             && SplitOverParts(repo, e, tf, burst, w, atDistance, st.Hits, exposure, armour, out armourChance);
                var facing = ArmorModel.FacingOf(tf, burst, hull * 0.5f);
                if (split)
                {
                    fragDamage = 0f;
                    for (byte p = 0; p < _partDamage.Length; p++)
                    {
                        if (_partDamage[p] <= 0f) continue;
                        fragDamage += _partDamage[p];
                        repo.Bus.Publish(new DamageAssessedEvent { HitEntity = e, TotalDamage = _partDamage[p], HasBodyPart = 1, BodyPart = p, Source = evt.Shooter });
                    }
                    if (blastDamage > 0f) repo.Bus.Publish(new DamageAssessedEvent { HitEntity = e, TotalDamage = blastDamage, Source = evt.Shooter });
                }
                float total = fragDamage + blastDamage;
                if (!split && total > 0f) repo.Bus.Publish(new DamageAssessedEvent { HitEntity = e, TotalDamage = total, Source = evt.Shooter });   // ⭐ CE-3237 (B-15, DS-1) — every burst event names who fired it
                // ⭐ CE-3219 (civilians B-1, CV-6) — a STUN burst stuns everyone its blast reaches, for its EffectSeconds scaled by the same blast
                //   fall-off, barrier and shadow (a wall between a man and the flash spares him). Health is untouched; the owner stores it.
                float stunSeconds = w.Kind == Fdp.Toolkit.Tkb.Domain.WarheadKind.Stun ? w.EffectSeconds * blastFall * barrier * shadow : 0f;
                if (stunSeconds > 0.05f)
                    repo.Bus.Publish(new DamageAssessedEvent { HitEntity = e, TotalDamage = 0f, Effect = HitEffect.Stun, EffectSeconds = stunSeconds, Source = evt.Shooter });
                // ⭐ CE-3210 (HL-10) / CE-3212 (S3-5) — the marks the fragments leave on a man, as many as struck him (capped), seeded by the
                //   burst so every run draws the same; on the parts by their shares when he has a body.
                if (kind == AreaEffect.FragmentTarget.Person && strike is { Hits: >= 0.5f } s)
                    MarkFragments(repo, e, (int)MathF.Round(s.Hits), record.Seq, facing, split ? _shares : default);
                record.Effects.Add(new DetonationEffect(e, stance.ToString(), _points.Count, r, exposure, fragFall, armourChance, fragDamage,
                    blastFall, barrier, shadow, blastDamage, total, shieldedBy)
                    { At = tf.Position, BlastSusceptibility = susceptibility, FragmentHits = strike?.Hits ?? 0f, Target = kind });
            }

            BreachDoors(repo, in evt, w, burst, terrain, doors, record);
            log.Add(record);
        }

        /// <summary>⭐ <c>CE-3210</c> (HL-10) — at most this many fragment marks per man per burst (the ring holds 24).</summary>
        internal const int MaxFragmentMarks = 6;

        /// <summary>⭐ <c>CE-3210</c> (HL-10) — <paramref name="hits"/> fragment marks on <paramref name="body"/>'s parts, on the face toward the
        /// burst, at points drawn from a hash of the burst and the body (deterministic: a replay draws the same).</summary>
        internal static void MarkFragments(EntityRepository repo, Entity body, int hits, long burstSeq, Facing face,
                                           ReadOnlySpan<float> shares = default)
        {
            int n = Math.Min(hits, MaxFragmentMarks);
            uint h = (uint)(burstSeq * 2654435761L) ^ (uint)(body.Index * 40503);
            for (int i = 0; i < n; i++)
            {
                h = Next(h); byte part = shares.IsEmpty ? (byte)(h % Fdp.Toolkit.Physics.BodyParts.PersonPart.Count) : Draw(shares, (h % 10000) / 10000f);
                h = Next(h); float a = (h % 1000) / 1000f - 0.5f;
                h = Next(h); float b = (h % 1000) / 1000f - 0.5f;
                // the point lies on the face entered: front/rear on the part's x side, the side on its y, the top on its z
                var local = face switch
                {
                    Facing.Front => new Vector3(0.5f, a, b),
                    Facing.Rear  => new Vector3(-0.5f, a, b),
                    Facing.Side  => new Vector3(a, 0.5f, b),
                    _            => new Vector3(a, b, 0.5f),
                };
                Fdp.Toolkit.Physics.BodyParts.HitMarksWriter.Mark(repo, body, part, local, (byte)face, Fdp.Toolkit.Physics.BodyParts.HitMarkKind.Fragment);
            }
            static uint Next(uint x) { x ^= x << 13; x ^= x >> 17; x ^= x << 5; return x == 0 ? 2463534242u : x; }
            static byte Draw(ReadOnlySpan<float> w, float u)
            {
                byte last = 0;
                for (byte i = 0; i < w.Length; i++)
                {
                    if (w[i] <= 0f) continue;
                    last = i;
                    u -= w[i];
                    if (u < 0f) return i;
                }
                return last;
            }
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3212</c> (hit location §10 S3-1..S3-3) — spreads a man's expected fragment <paramref name="hits"/> over his parts by their
        /// area facing the burst (<see cref="Fdp.Toolkit.Physics.BodyParts.FragmentSplit"/>); each part's hits meet its zone's armour on that
        /// face (an inherited / unstated face keeps <paramref name="platformArmour"/>) and count by its multiplier over the area-weighted mean,
        /// so an unarmoured man's total is the law's. Fills <c>_shares</c> and <c>_partDamage</c>; false when he has no person body.
        /// </summary>
        private bool SplitOverParts(EntityRepository repo, Entity e, in SimTransform tf, Vector3 burst, Tkb.Domain.WarheadDto w, float distance,
                                    float hits, float exposure, float platformArmour, out float meanChance)
        {
            meanChance = 1f;
            Array.Clear(_partDamage);
            if (Fdp.Toolkit.Physics.BodyParts.BodyZones.Of(repo, e) is not { Plan: Fdp.Toolkit.Physics.BodyParts.BodyPlan.Person } body) return false;
            var pose = Fdp.Toolkit.Physics.BodyParts.PersonBody.PoseOf(repo, e);
            float scale = Fdp.Toolkit.Physics.BodyParts.PersonBody.ScaleOf(
                repo.HasComponent<PhysicsCollider>(e) ? repo.GetComponentRO<PhysicsCollider>(e).Height : 0f);
            var (centre, _) = Fdp.Toolkit.Physics.BodyParts.PersonBody.Bound(pose);
            var toward = Vector3.Transform(burst - (tf.Position + Vector3.Transform(centre * scale, tf.Rotation)), Quaternion.Inverse(tf.Rotation));
            if (Fdp.Toolkit.Physics.BodyParts.FragmentSplit.Shares(Fdp.Toolkit.Physics.BodyParts.PersonBody.Boxes(pose), toward, _shares) <= 0f) return false;
            var face = Fdp.Toolkit.Physics.BodyParts.BodyPartHit.FaceOf(Vector3.Normalize(toward));
            var (perHit, penetration) = AreaEffect.FragmentHit(w, AreaEffect.FragmentTarget.Person, distance);
            float mean = 0f;
            for (byte p = 0; p < _shares.Length; p++)
                if (_shares[p] > 0f) mean += _shares[p] * (body.ZoneOf(p)?.Multiplier ?? 1f);
            if (mean <= 0f) return false;
            meanChance = 0f;
            for (byte p = 0; p < _shares.Length; p++)
            {
                if (_shares[p] <= 0f) continue;
                var zone = body.ZoneOf(p);
                float a = zone?.ArmourOn(face) ?? platformArmour;
                if (float.IsNaN(a)) a = platformArmour;
                float chance = ArmorModel.PenetrationChance(penetration, a);
                meanChance += _shares[p] * chance;
                _partDamage[p] = hits * _shares[p] * perHit * chance * ((zone?.Multiplier ?? 1f) / mean) * MathF.Max(exposure, 0f);
            }
            return true;
        }

        /// <summary>
        /// ⭐ W-9 — a door inside the lethal blast radius, with nothing but its own leaf between it and the burst, is breached: a
        /// <see cref="DoorCommandEvent"/> (<see cref="DoorVerb.Breach"/>) that its owner applies, as for any door command.
        /// </summary>
        private void BreachDoors(EntityRepository repo, in DetonationNotification evt, Tkb.Domain.WarheadDto w, Vector3 burst,
            TerrainWorld? terrain, DoorStates? doors, DetonationRecord record)
        {
            if (terrain == null || terrain.Doors.Count == 0 || w.BlastLethalRadiusM <= 0f || !repo.Bus.IsRegistered<DoorCommandEvent>()) return;
            for (int i = 0; i < terrain.Doors.Count; i++)
            {
                var d = terrain.Doors[i];
                var centre = new Vector3(d.Center.X, d.Center.Y, d.SillZ + DoorMidHeight);
                if (Vector3.Distance(burst, centre) > w.BlastLethalRadiusM) continue;
                var state = doors != null ? doors[i] : d.Initial;
                if (state == TerrainDoorState.Destroyed || state == TerrainDoorState.Open) continue;
                terrain.QueryFire(burst, centre, _crossings, doors);
                bool clear = true;
                foreach (var c in _crossings) if (c.Kind != "door") { clear = false; break; }
                if (!clear) continue;
                var door = TerrainObjects.Find(repo, d.Key);
                if (!repo.IsAlive(door)) continue;
                repo.Bus.Publish(new DoorCommandEvent { Door = door, Verb = DoorVerb.Breach, Actor = evt.Shooter });
                record.DoorsBreached.Add(d.Key);
            }
        }

        /// <summary>
        /// ⭐ CE-3203 (§3k.1) — what the burst meets in <paramref name="e"/>: a WEAPON (equipment — its type states a presented area), a HULL
        /// (a collider with height: its silhouette, measured from its skin), or a MAN (his stance area). The distance a hull is measured at
        /// is never inside its own radius.
        /// </summary>
        internal static (AreaEffect.FragmentTarget Kind, float Area, float Distance) FragmentTargetOf(EntityRepository repo, Entity e,
            Fdp.Toolkit.Tkb.Domain.CombatPlatformDefDto? platform, float hullHeight, Fdp.Toolkit.Tkb.Domain.StanceId stance, float distance)
        {
            if (platform is { PresentedAreaM2: > 0f } stated) return (AreaEffect.FragmentTarget.Equipment, stated.PresentedAreaM2, distance);
            if (hullHeight > 0f)
            {
                float radius = PhysicsColliderReaders.Radius(repo, e);
                return (AreaEffect.FragmentTarget.Hull, ArmorModel.PresentedArea(radius, hullHeight), MathF.Max(distance, radius));
            }
            return (AreaEffect.FragmentTarget.Person, AreaEffect.PersonArea(stance), distance);
        }

        /// <summary>The height above its sill a door is tested at (the middle of a standard leaf).</summary>
        private const float DoorMidHeight = 1.0f;
    }
}
