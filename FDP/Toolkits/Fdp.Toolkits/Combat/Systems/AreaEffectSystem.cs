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
        private readonly List<int> _buildings = new();                           // ⭐ CE-4391 S-2 — the buildings near a burst (the collapse test)
        private readonly List<int> _collapsedNow = new();
        private readonly List<int> _panels = new();                              // ⭐ CE-4391 — BreachWalls: the building panels near a burst
        private readonly float[] _shares = new float[Fdp.Toolkit.Physics.BodyParts.PartDamage.MaxParts];       // ⭐ CE-3212
        private readonly float[] _partDamage = new float[Fdp.Toolkit.Physics.BodyParts.PartDamage.MaxParts];   // ⭐ CE-3212

        /// <summary>Detonations this system assessed (a rail and a diagnostics counter read it).</summary>
        public long Assessed { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // ⭐ CE-3329 P2 — a replay: the recording owns the world (DESIGN_Replay_Time_Axis_And_Review §12l)
            if (view is not EntityRepository repo) return;
            var events = repo.ReadEvents<DetonationNotification>();
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
                    doors = terrain is { HasLiveState: true } ? DoorStates.Of(repo, terrain) : null;   // ⭐ CE-4391 — doors OR breachable buildings
                    world = WorldQuery.Of(repo);
                    bool bullets = repo.IsComponentTypeRegistered<BallisticProjectile>();
                    _occlusion.Build(repo, PhysicsColliderReaders.Radius, PhysicsColliderReaders.BlockerHeight,   // ⭐ CE-2340 — a man lying down shields only his lying height
                        bullets ? static (v, e) => !v.HasComponent<BallisticProjectile>(e) : null);   // a round in flight is not cover
                    built = true;
                }
                Assess(repo, view.GetCommandBuffer(), in evt, warhead, source, world, terrain, doors);
                Assessed++;
            }
        }

        /// <summary>
        /// ⭐ <c>CE-3219</c> part 3 (docs/DESIGN_Civilians_And_Crowd_Control.md §7.3b) — a tear-gas or smoke burst asks for a cloud where it
        /// burst (the entity-creation request on the FDP bus, owner 0: the system picks); its owner drifts it, irritates with it and ends it.
        /// </summary>
        /// <summary>⭐ <c>R-2085</c> — the hit kind a burst's DAMAGE carries: a non-lethal warhead's (the same four kinds <c>Armament.IsLethal</c>
        /// calls non-lethal), else Kinetic. Its effect time stays 0 here — the stun is its own event below.</summary>
        internal static HitEffect NonLethalEffectOf(Tkb.Domain.WarheadKind kind) => kind switch
        {
            Tkb.Domain.WarheadKind.Stun => HitEffect.Stun,
            Tkb.Domain.WarheadKind.Irritant or Tkb.Domain.WarheadKind.Smoke => HitEffect.Irritant,
            Tkb.Domain.WarheadKind.Blunt => HitEffect.Blunt,
            _ => HitEffect.Kinetic,
        };

        private static void RequestCloud(EntityRepository repo, in DetonationNotification evt, Tkb.Domain.WarheadDto w)
        {
            bool gas = w.Kind == Tkb.Domain.WarheadKind.Irritant;
            // ⭐ CE-3236 (B-14, §7.15 VC-2/VC-4) — a canister still lying where it burst is the cloud's SOURCE, and its burn is the cloud's time
            float seconds = w.CloudSeconds;
            int source = 0;
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
                BornAtSimSeconds = (SimInstant)(Fdp.Toolkit.Time.SimClock.Of(repo).TotalTime),
                Seconds = seconds,
                SourceNetworkId = source,
                // ⭐ CE-4155 (SR-82) — who threw it (and who threw it first): every pulse of the cloud names them
                CreatorNetworkId = NetworkIdOf(repo, evt.Shooter),
                OriginalCreatorNetworkId = NetworkIdOf(repo, evt.OriginalShooter),
            });
        }

        /// <summary>⭐ <c>CE-4155</c> — <paramref name="e"/>'s network id, 0 when it has none (an offline world) or is null.</summary>
        internal static int NetworkIdOf(EntityRepository repo, Entity e)
            => !e.IsNull && repo.IsAlive(e) && repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()
               && repo.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e)
                ? repo.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value : 0;

        /// <summary>The point of segment <paramref name="a"/>–<paramref name="b"/> closest to <paramref name="q"/>.</summary>
        private static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 q)
        {
            var ab = b - a;
            float len2 = ab.LengthSquared();
            float t = len2 > 1e-8f ? Math.Clamp(Vector3.Dot(q - a, ab) / len2, 0f, 1f) : 0f;
            return a + ab * t;
        }

        private void Assess(EntityRepository repo, IEntityCommandBuffer cmd, in DetonationNotification evt, Tkb.Domain.WarheadDto w, string source,
            IWorldQuery? world, TerrainWorld? terrain, DoorStates? doors)
        {
            var burst = new Vector3(evt.HitX, evt.HitY, evt.HitZ);
            var struck = evt.Target.IsNull ? ColliderOcclusion.Nobody : evt.Target;   // ⚠ never skip "index 0" for a terrain burst
            float reach = MathF.Max(w.FragmentRadiusM, w.BlastInjuryRadiusM);
            // ⭐⭐ CE-4584 (AQ101 §R C, R-4227) — a burst is FIRE to every person within 2 × its blast INJURY radius (the field of that name: a few
            //   metres for a mortar bomb, so ~10–15 m): the ONE near miss a passing round gives (HitModel.NearMiss — the event the brain senses,
            //   and the under-fire stamp). ⛔ Not the fragment radius (2 × that reaches 40–70 m: a man that far is not shot at). Every person,
            //   either side and civilians (fire is fire); not the entity the round struck (that is a hit), not a hull or a piece of equipment.
            float nearMiss = 2f * w.BlastInjuryRadiusM;
            Vector3? origin = null;
            if (nearMiss > 0f && !evt.Round.IsNull && repo.IsAlive(evt.Round) && repo.IsComponentTypeRegistered<BallisticProjectile>()
                && repo.HasComponent<BallisticProjectile>(evt.Round) && (Vector3)repo.GetComponentRO<BallisticProjectile>(evt.Round).Muzzle is var muzzle
                && muzzle != Vector3.Zero)
                origin = muzzle;   // where it was fired from (CE-3262) — the mortar's tube, as a rifle's muzzle
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
                if (nearMiss > 0f && e != evt.Target && Vector3.Distance(burst, tf.Position) <= nearMiss && IsPerson(repo, e))
                    HitModel.NearMiss(repo, cmd, e, burst, origin);   // ⭐ CE-4584 — before the damage reach: the near-miss ring may be the wider one
                if (Vector2.Distance(new Vector2(burst.X, burst.Y), new Vector2(tf.Position.X, tf.Position.Y)) > reach + 2f) continue;
                // ⭐⭐ CE-6003 (city battle O-1, Peek and Fire §9.9) — a destructible OBSTACLE is terrain: blast by charge and closeness, never the
                //   person / hull law below (its own prism would shield its body points, and that law's blast is the same for every warhead)
                if (TerrainObstacles.IsObstacle(repo, e)) { AssessObstacle(repo, in evt, w, burst, e, in tf, world, record); continue; }

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
                    int firstSolid = 0;   // ⭐ CE-4369 — a wood (a stop share, no surface) is not where fragments enter an obstacle
                    while (firstSolid < _traces.Count && _traces[firstSolid].Stop > 0f) firstSolid++;
                    Vector3? stopAt = firstSolid < _traces.Count ? burst + (p - burst) * _traces[firstSolid].T : null;
                    if (ft > 0f && _occlusion.Blocking(burst, p, e, struck, out _) is { } blocker)
                    {
                        ft = 0f;
                        shieldedBy ??= $"entity #{blocker.Index}";
                        if (repo.HasComponent<SimTransform>(blocker))
                            stopAt = ClosestOnSegment(burst, p, repo.GetComponentRO<SimTransform>(blocker).Position);
                    }
                    else if (ft <= 0f && fragFall > 0f && firstSolid < _traces.Count)
                        shieldedBy ??= $"{_traces[firstSolid].Kind} {_traces[firstSolid].Label ?? _traces[firstSolid].Material}";
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
                // ⭐ R-2085 — a NON-LETHAL warhead's burst damage (a stun or gas grenade) carries its non-lethal kind, so the Health owner's
                //   floor applies (HealthApplicationSystem.NonLethalFloor): it never kills. Kinetic for every lethal warhead, as before.
                var dmgEffect = NonLethalEffectOf(w.Kind);
                if (split)
                {
                    fragDamage = 0f;
                    for (byte p = 0; p < _partDamage.Length; p++)
                    {
                        if (_partDamage[p] <= 0f) continue;
                        fragDamage += _partDamage[p];
                        repo.Bus.Publish(new DamageAssessedEvent { HitEntity = e, TotalDamage = _partDamage[p], HasBodyPart = 1, BodyPart = p, Effect = dmgEffect, Source = evt.Shooter, OriginalSource = evt.OriginalShooter });
                    }
                    if (blastDamage > 0f) repo.Bus.Publish(new DamageAssessedEvent { HitEntity = e, TotalDamage = blastDamage, Effect = dmgEffect, Source = evt.Shooter, OriginalSource = evt.OriginalShooter });
                }
                float total = fragDamage + blastDamage;
                if (!split && total > 0f) repo.Bus.Publish(new DamageAssessedEvent { HitEntity = e, TotalDamage = total, Effect = dmgEffect, Source = evt.Shooter, OriginalSource = evt.OriginalShooter });   // ⭐ CE-3237 (B-15, DS-1) — every burst event names who fired it
                // ⭐ CE-3219 (civilians B-1, CV-6) — a STUN burst stuns everyone its blast reaches, for its EffectSeconds scaled by the same blast
                //   fall-off, barrier and shadow (a wall between a man and the flash spares him). Health is untouched; the owner stores it.
                float stunSeconds = w.Kind == Fdp.Toolkit.Tkb.Domain.WarheadKind.Stun ? w.EffectSeconds * blastFall * barrier * shadow : 0f;
                if (stunSeconds > 0.05f)
                    repo.Bus.Publish(new DamageAssessedEvent { HitEntity = e, TotalDamage = 0f, Effect = HitEffect.Stun, EffectSeconds = stunSeconds, Source = evt.Shooter, OriginalSource = evt.OriginalShooter });
                // ⭐ CE-3210 (HL-10) / CE-3212 (S3-5) — the marks the fragments leave on a man, as many as struck him (capped), seeded by the
                //   burst so every run draws the same; on the parts by their shares when he has a body.
                if (kind == AreaEffect.FragmentTarget.Person && strike is { Hits: >= 0.5f } s)
                    MarkFragments(repo, e, (int)MathF.Round(s.Hits), record.Seq, facing, split ? _shares : default);
                record.Effects.Add(new DetonationEffect(e, stance.ToString(), _points.Count, r, exposure, fragFall, armourChance, fragDamage,
                    blastFall, barrier, shadow, blastDamage, total, shieldedBy)
                    { At = tf.Position, BlastSusceptibility = susceptibility, FragmentHits = strike?.Hits ?? 0f, Target = kind });
            }

            BreachDoors(repo, in evt, w, burst, terrain, doors, record);
            BreachWalls(repo, in evt, w, burst, terrain, doors, record);   // ⭐ CE-4391 S-1 (B-5) — the walls, by the same detonation
            log.Add(record);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-6003</c> (city battle O-1, 📄 docs/DESIGN_Peek_And_Fire.md §9.9) — a burst on a destructible static obstacle: <see cref="ObstacleDamage.BlastDamage"/>
        /// at the distance to the obstacle's NEAREST FACE (its box, not body points), through the closed barriers on the line to that face — traced to a
        /// point 2 cm outside it, so the obstacle's own prism is never crossed (as <see cref="LineClearToFace"/> does for a wall). A non-lethal warhead
        /// (stun, gas, smoke) or a kinetic round (no explosive) does nothing. ONE <see cref="DamageAssessedEvent"/> to its Health owner, as for everyone.
        /// </summary>
        private void AssessObstacle(EntityRepository repo, in DetonationNotification evt, Tkb.Domain.WarheadDto w, Vector3 burst, Entity e,
            in SimTransform tf, IWorldQuery? world, DetonationRecord record)
        {
            if (NonLethalEffectOf(w.Kind) != HitEffect.Kinetic || w.ExplosiveKg <= 0f || !repo.HasComponent<ObstacleShape>(e)) return;
            var near = ObstacleDamage.NearestPoint(tf.Position, TerrainObstacles.Yaw(tf.Rotation), in repo.GetComponentRO<ObstacleShape>(e), burst);
            float d = Vector3.Distance(burst, near);
            float raw = ObstacleDamage.BlastDamage(w.ExplosiveKg, d);
            if (raw <= 0f) return;
            float barrier = 1f;
            if (d > 0.05f && world != null)
            {
                world.Trace(burst, near + (burst - near) * (0.02f / d), TracePurpose.Fire, _traces);
                barrier = AreaEffect.ClosedBarrierTransmission(_traces);
            }
            float damage = raw * barrier;
            if (damage <= 0f) return;
            repo.Bus.Publish(new DamageAssessedEvent { HitEntity = e, TotalDamage = damage, Source = evt.Shooter, OriginalSource = evt.OriginalShooter });
            record.Effects.Add(new DetonationEffect(e, "Obstacle", 0, d, 0f, 0f, 0f, 0f, raw / (ObstacleDamage.DamagePerKg * w.ExplosiveKg), barrier, 1f,
                damage, damage, barrier < 1f ? "a barrier" : null) { At = tf.Position });
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
            long t0 = Fdp.Toolkit.Diagnostics.QueryTimings.Begin();   // ⭐ CE-3427 — it scans every door of the terrain per detonation
            BreachDoorsCore(repo, in evt, w, burst, terrain, doors, record);
            Fdp.Toolkit.Diagnostics.QueryTimings.End(S_BreachDoors, t0);
        }
        private static readonly int S_BreachDoors = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("doors.BreachDoors");

        private void BreachDoorsCore(EntityRepository repo, in DetonationNotification evt, Tkb.Domain.WarheadDto w, Vector3 burst,
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
        /// ⭐⭐ <c>CE-4391</c> S-1 (docs/DESIGN_Terrain_Import_UTM.md §8 D-4, §8.2 B-5, B-6) — the walls of a building within reach of a burst with
        /// explosive in it are breached by <see cref="BreachRule"/> (scaled distance Z = d / ∛kg, the table's rows, the material's resistance), each as a
        /// <see cref="BuildingDamageEvent"/> its <see cref="BuildingDamageSystem"/> writes. The line from the burst to the panel's near face must be CLEAR
        /// (a wall behind another wall is spared). ⛔ A kinetic round (no explosive) makes none: a sabot's hole is too small to change sight or movement.
        /// <para>⭐ The deciding point is the detonation: a round that stops at a wall bursts <c>BurstStandOffMetres</c> back along its flight
        /// (<c>BallisticsSystem</c>), so d ≈ 0 against the wall it hit. ⭐ The panels come from <see cref="TerrainWorld.BuildingPanels"/>, never a scan of every panel.</para>
        /// </summary>
        private void BreachWalls(EntityRepository repo, in DetonationNotification evt, Tkb.Domain.WarheadDto w, Vector3 burst,
            TerrainWorld? terrain, DoorStates? doors, DetonationRecord record)
        {
            if (terrain == null || terrain.Panels.Count == 0 || w.ExplosiveKg <= 0f || !repo.Bus.IsRegistered<BuildingDamageEvent>()) return;
            var table = terrain.Breaches;
            if (w.ExplosiveKg < table.MinKg) return;
            long t0 = Fdp.Toolkit.Diagnostics.QueryTimings.Begin();
            BreachWallsCore(repo, in evt, w, burst, terrain, table, doors, record);
            Fdp.Toolkit.Diagnostics.QueryTimings.End(S_BreachWalls, t0);
        }
        private static readonly int S_BreachWalls = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("buildings.BreachWalls");

        private void BreachWallsCore(EntityRepository repo, in DetonationNotification evt, Tkb.Domain.WarheadDto w, Vector3 burst,
            TerrainWorld terrain, BreachTable table, DoorStates? doors, DetonationRecord record)
        {
            _collapsedNow.Clear();
            if (w.ExplosiveKg >= table.CollapseMinKg) CollapseBuildings(repo, in evt, w, burst, terrain, table, doors, record);   // ⭐ S-2: a collapsed building's breaches are moot
            float reach = BreachRule.Reach(table, w.ExplosiveKg);
            _panels.Clear();
            terrain.BuildingPanels.Near(new Vector2(burst.X, burst.Y), reach + 1f, _panels);   // +1 m: a thick wall's face
            for (int k = 0; k < _panels.Count; k++)
            {
                int pi = _panels[k];
                var panel = terrain.Panels[pi];
                if (panel.Building >= 0 && (_collapsedNow.Contains(panel.Building) || doors?.Damage?.Collapsed(panel.Building) == true)) continue;
                int group = WallGroups.Of(terrain, pi);   // ⭐ CE-4599 (B-2, B-D7) — a building, or a free-wall line: blasts breach garden walls too
                if (!BreachRule.TryDecide(table, panel, pi, burst, w.ExplosiveKg, w.Fuze, out var breach)) continue;
                if (!LineClearToFace(terrain, doors, panel, burst)) continue;
                repo.Bus.Publish(new BuildingDamageEvent
                {
                    Building = group, Panel = pi, From = breach.From, To = breach.To, Bottom = breach.Bottom, Top = breach.Top,
                    Kind = (byte)breach.Kind, Shooter = evt.Shooter,
                });
                record.BuildingBreaches.Add($"{BuildingDamages.KeyOf(terrain, group)} panel {pi} {breach.Kind} " +
                    $"{breach.From:0.##}..{breach.To:0.##} m x z {breach.Bottom:0.##}..{breach.Top:0.##}");
            }
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4391</c> S-2 (UTM §8 D-7, §8.2 B-9) — a burst of at least <c>collapseMinKg</c> inside or beside a building (<see cref="BreachRule.Collapses"/>)
        /// COLLAPSES it: a collapse <see cref="BuildingDamageEvent"/> for <see cref="BuildingDamageSystem"/>, and — in this same assessment — every living
        /// <see cref="Health"/> entity inside its footprint, between its base and roof, takes lethal damage through the SAME <see cref="DamageAssessedEvent"/> path the
        /// blast uses for every victim (so its owner applies it, wherever it runs, R-171). An already-collapsed building is not collapsed twice.
        /// </summary>
        private void CollapseBuildings(EntityRepository repo, in DetonationNotification evt, Tkb.Domain.WarheadDto w, Vector3 burst,
            TerrainWorld terrain, BreachTable table, DoorStates? doors, DetonationRecord record)
        {
            _buildings.Clear();
            terrain.BuildingFootprints.Near(new Vector2(burst.X, burst.Y), table.CollapseZ * MathF.Cbrt(w.ExplosiveKg) + 1f, _buildings);
            for (int k = 0; k < _buildings.Count; k++)
            {
                int bi = _buildings[k];
                if (doors?.Damage?.Collapsed(bi) == true) continue;
                var building = terrain.Buildings[bi];
                if (!BreachRule.Collapses(table, building, burst, w.ExplosiveKg)) continue;
                _collapsedNow.Add(bi);
                repo.Bus.Publish(new BuildingDamageEvent { Building = bi, Panel = -1, Collapse = 1, Shooter = evt.Shooter });
                record.BuildingBreaches.Add($"{BuildingDamages.KeyOf(terrain, bi)} COLLAPSED");
                float baseZ = building.StoreyZ.Count > 0 ? building.StoreyZ[0] : building.BaseZ;
                float roofZ = building.StoreyZ.Count > 1 ? building.StoreyZ[building.StoreyZ.Count - 1] : baseZ + 3f;
                foreach (var e in repo.Query().With<Health>().With<SimTransform>().Build())
                {
                    if (!repo.IsAlive(e)) continue;
                    var hp = repo.GetComponentRO<Health>(e);
                    if (hp.Current <= 0f) continue;
                    var at = repo.GetComponentRO<SimTransform>(e).Position;
                    if (at.Z < baseZ - 0.5f || at.Z > roofZ + 0.5f || !PolygonMath.Contains(building.Footprint, new Vector2(at.X, at.Y))) continue;
                    repo.Bus.Publish(new DamageAssessedEvent
                    {
                        HitEntity = e, TotalDamage = MathF.Max(hp.Current, hp.Max) * 2f, Source = evt.Shooter, OriginalSource = evt.OriginalShooter,
                    });
                }
            }
        }

        /// <summary>
        /// True when nothing of the terrain stands between <paramref name="burst"/> and the panel's NEAR FACE: the trace ends just outside the face
        /// (so the panel's own pieces are never crossed — a <c>FireCrossing</c> names its building and storey, not its panel, so "only this panel" could
        /// not be tested by name). A burst already within the wall's thickness has nothing between it and the face.
        /// </summary>
        private bool LineClearToFace(TerrainWorld terrain, DoorStates? doors, TerrainWallPanel panel, Vector3 burst)
        {
            var ab = panel.B - panel.A;
            float len2 = ab.LengthSquared();
            var p = new Vector2(burst.X, burst.Y);
            float t = len2 > 1e-8f ? Math.Clamp(Vector2.Dot(p - panel.A, ab) / len2, 0f, 1f) : 0f;
            var onWall = panel.A + ab * t;
            var toward = p - onWall;
            float dh = toward.Length();
            float z = Math.Clamp(burst.Z, panel.BaseZ, panel.TopZ);
            if (dh <= panel.Thickness * 0.5f + 0.03f) return true;   // against (or inside) the wall
            var face = onWall + toward / dh * (panel.Thickness * 0.5f + 0.02f);
            terrain.QueryFire(burst, new Vector3(face, z), _crossings, doors);
            return _crossings.Count == 0;
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

        /// <summary>⭐ <c>CE-4584</c> — a person in the sense <see cref="FragmentTargetOf"/> uses: no stated presented area (equipment) and no hull,
        /// and not a destructible obstacle.</summary>
        internal static bool IsPerson(EntityRepository repo, Entity e)
            => !TerrainObstacles.IsObstacle(repo, e)
               && PhysicsColliderReaders.HullHeight(repo, e) <= 0f
               && CombatTkb.PlatformOf(repo, e) is not { PresentedAreaM2: > 0f };

        /// <summary>The height above its sill a door is tested at (the middle of a standard leaf).</summary>
        private const float DoorMidHeight = 1.0f;
    }
}
