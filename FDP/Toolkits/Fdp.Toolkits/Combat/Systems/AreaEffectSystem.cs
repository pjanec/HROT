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
    /// = the mean; BLAST (personnel only in v1) through closed barriers (the least-shielded point) and in the deepest diffraction
    /// shadow on the line to the highest point (terrain and colliders). Expected values (R-212).</para>
    /// <para>⚠ The entity the round struck is IN the burst too (at ~0 m; it is never its own cover) — its direct hit, from
    /// <see cref="DamageCalculationSystem"/>, comes on top: a warhead mount states no hit damage, so that is the flat default. A REMOTE detonation
    /// (<see cref="DetonationNotification.IsRemote"/>) gets no area effect here, as it gets no direct damage.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class AreaEffectSystem : IEcsModuleSystem
    {
        private readonly ColliderOcclusion _occlusion = new();
        private readonly List<TerrainWorld.FireCrossing> _crossings = new();
        private readonly List<Vector3> _points = new(3);

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
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];
                if (evt.IsRemote || evt.Ammo == 0) continue;
                var (warhead, source, _) = ParameterResolver.Warhead(db, evt.Ammo);
                if (warhead == null || !warhead.HasAreaEffect) continue;
                if (!built)
                {
                    terrain = repo.HasSingletonManaged<TerrainWorld>() ? repo.GetSingletonManaged<TerrainWorld>() : null;
                    doors = terrain is { Doors.Count: > 0 } ? DoorStates.Of(repo, terrain) : null;
                    bool bullets = repo.IsComponentTypeRegistered<BallisticProjectile>();
                    _occlusion.Build(repo, PhysicsColliderReaders.Radius, PhysicsColliderReaders.Height,
                        bullets ? static (v, e) => !v.HasComponent<BallisticProjectile>(e) : null);   // a round in flight is not cover
                    built = true;
                }
                Assess(repo, in evt, warhead, source, terrain, doors);
                Assessed++;
            }
        }

        private void Assess(EntityRepository repo, in DetonationNotification evt, Tkb.Domain.WarheadDto w, string source,
            TerrainWorld? terrain, DoorStates? doors)
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
                if (repo.GetComponentRO<Health>(e).Current <= 0f) continue;
                var tf = repo.GetComponentRO<SimTransform>(e);
                if (Vector2.Distance(new Vector2(burst.X, burst.Y), new Vector2(tf.Position.X, tf.Position.Y)) > reach + 2f) continue;

                var stance = HitModel.LogicalStance(repo, e);
                float hull = PhysicsColliderReaders.HullHeight(repo, e);
                BodyProfile.Points(repo, e, stance, hull, _points);
                float r = float.MaxValue;
                foreach (var p in _points) r = MathF.Min(r, Vector3.Distance(burst, p));

                float fragFall = AreaEffect.FragmentFalloff(r, w.FragmentRadiusM);
                float blastFall = hull > 0f ? 0f : AreaEffect.BlastFalloff(r, w.BlastLethalRadiusM, w.BlastInjuryRadiusM);   // personnel only (v1)
                if (fragFall <= 0f && blastFall <= 0f) continue;

                float exposure = 0f, barrier = 0f, shadow = 1f;
                string? shieldedBy = null;
                for (int i = 0; i < _points.Count; i++)
                {
                    var p = _points[i];
                    if (terrain != null) terrain.QueryFire(burst, p, _crossings, doors); else _crossings.Clear();
                    float ft = fragFall > 0f ? AreaEffect.FragmentTransmission(_crossings, w.FragmentPenetrationMm) : 0f;
                    if (ft > 0f && _occlusion.Blocking(burst, p, e, struck, out _) is { } blocker)
                    {
                        ft = 0f;
                        shieldedBy ??= $"entity #{blocker.Index}";
                    }
                    else if (ft <= 0f && fragFall > 0f && _crossings.Count > 0)
                        shieldedBy ??= $"{_crossings[0].Kind} {_crossings[0].Label ?? _crossings[0].Material}";
                    exposure += ft;
                    barrier = MathF.Max(barrier, AreaEffect.ClosedBarrierTransmission(_crossings));
                    if (i == _points.Count - 1)   // the highest point (the fractions ascend)
                        shadow = AreaEffect.TerrainShadow(_crossings, tf.Position.Z,
                            Vector2.Distance(new Vector2(burst.X, burst.Y), new Vector2(p.X, p.Y)));
                }
                exposure /= _points.Count;
                if (blastFall > 0f && _occlusion.Shadow(burst, _points[^1], e, struck, _points[^1].Z, tf.Position.Z,
                        AreaEffect.ShadowFactor, out float colliderShadow))
                    shadow = MathF.Min(shadow, colliderShadow);

                var armour = ArmorModel.ArmourFor(CombatTkb.PlatformOf(repo, e), ArmorModel.FacingOf(tf, burst));
                float armourChance = ArmorModel.PenetrationChance(w.FragmentPenetrationMm, armour);
                float fragDamage = w.FragmentDamage * fragFall * exposure * armourChance;
                float blastDamage = w.BlastLethalDamage * blastFall * barrier * shadow;
                float total = fragDamage + blastDamage;
                if (total > 0f) repo.Bus.Publish(new DamageAssessedEvent { HitEntity = e, TotalDamage = total });
                record.Effects.Add(new DetonationEffect(e, stance.ToString(), _points.Count, r, exposure, fragFall, armourChance, fragDamage,
                    blastFall, barrier, shadow, blastDamage, total, shieldedBy));
            }

            BreachDoors(repo, in evt, w, burst, terrain, doors, record);
            log.Add(record);
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

        /// <summary>The height above its sill a door is tested at (the middle of a standard leaf).</summary>
        private const float DoorMidHeight = 1.0f;
    }
}
