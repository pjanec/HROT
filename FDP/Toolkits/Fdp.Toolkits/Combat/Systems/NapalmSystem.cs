using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Perception.Signatures;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-3487</c> (flame FL-3 — F-5, N-3…N-5; 📄 docs/designs/weapons/DESIGN_Flame_Weapons.md §7b) — a NAPALM burst: an <see cref="Fdp.Toolkit.Tkb.Domain.WarheadKind.Incendiary"/>
    /// warhead with an area (<see cref="Fdp.Toolkit.Tkb.Domain.WarheadDto.AreaLengthM"/> × <see cref="Fdp.Toolkit.Tkb.Domain.WarheadDto.AreaWidthM"/>) lays its fire in an
    /// ELLIPSE along the round's flight line. Decided where the detonation is (SimHost's <c>DamageAssessmentModule</c>, after <see cref="AreaEffectSystem"/>, which already
    /// raises its smoke from the warhead's cloud):
    /// <list type="bullet">
    /// <item>⭐ the FIREBALL — every living person inside the ellipse is set alight: a <see cref="DamageAssessedEvent"/> with <see cref="HitEffect.Burning"/>
    /// (<see cref="Ignition.BurningManSeconds"/>), which crosses to his Health owner like a stun (N-5: the fireball's 1 s exposure crosses a man's 0.3 s, not a hull's 1.5 s);</item>
    /// <item>⭐ the GROUND FIRE — <see cref="SpotsOf"/> spots along the axis, each a <see cref="Fdp.Toolkit.Terrain.GroundFireEvent"/> for the cell-record writer
    /// (<see cref="Fdp.Toolkit.Terrain.GroundFireSystem"/>); <see cref="BurnSystem"/> then burns whoever stands in them, men and vehicles alike.</item>
    /// </list>
    /// ⛔ Never in a replay. 0 B per steady frame (a burst is an event).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class NapalmSystem : IEcsModuleSystem
    {
        /// <summary>A spot's radius over the half-width (the spots overlap so the ellipse has no cold gap between them).</summary>
        public const float SpotRadiusFactor = 1.2f;

        /// <summary>Napalm bursts this node decided (rails, diagnostics).</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter of napalm bursts decided")]
        public long Strikes { get; private set; }

        private EntityQuery? _bodies;
        private EntityRepository? _bodiesRepo;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view) || view is not EntityRepository repo) return;
            if (!repo.Bus.IsRegistered<DetonationNotification>() || !repo.HasSingletonManaged<ITkbDatabase>()) return;
            var events = repo.ReadEvents<DetonationNotification>();
            if (events.Length == 0) return;
            double now = HitModel.Now(repo);
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];
                if (evt.IsRemote || evt.Ammo == 0) continue;
                if (CombatTkb.WarheadOf(repo, evt.Ammo) is not { Kind: Fdp.Toolkit.Tkb.Domain.WarheadKind.Incendiary, AreaLengthM: > 0f, AreaWidthM: > 0f } w) continue;
                var at = new Vector3(evt.HitX, evt.HitY, evt.HitZ);
                var axis = Axis(evt.TravelX, evt.TravelY);
                float seconds = w.BurnSeconds > 0f ? w.BurnSeconds : Ignition.DefaultVehicleBurnSeconds;
                float intensity = w.FlameIntensity > 0f ? MathF.Min(w.FlameIntensity, 1f) : 1f;
                Fireball(repo, in evt, at, axis, w.AreaLengthM, w.AreaWidthM);
                LightBuildings(repo, at, axis, w.AreaLengthM, w.AreaWidthM, now, intensity);   // ⭐ CE-3488 (FL-4 B-3)
                // ⭐ the burst goes into the combat report's detonation log as every other burst does (AreaEffectSystem writes only those with blast or
                //   fragments): the scenario check's `blast` cue and the /combat routes read it
                var log = DetonationLog.For(repo);
                log.Add(new DetonationRecord
                {
                    Seq = log.NextSeq(), Tick = repo.HasSingleton<GlobalTime>() ? (uint)repo.GetSingleton<GlobalTime>().FrameNumber : 0u,
                    Shooter = evt.Shooter, Struck = evt.Target, Burst = at, Ammo = evt.Ammo,
                    Warhead = $"Incendiary, {w.AreaLengthM:0} x {w.AreaWidthM:0} m ellipse", WarheadSource = "napalm",
                });
                if (repo.Bus.IsRegistered<Fdp.Toolkit.Terrain.GroundFireEvent>())
                {
                    int n = SpotsOf(w.AreaLengthM, w.AreaWidthM);
                    float step = w.AreaLengthM / n, radius = w.AreaWidthM * 0.5f * SpotRadiusFactor;
                    for (int k = 0; k < n; k++)
                    {
                        var c = at + new Vector3(axis * (-0.5f * w.AreaLengthM + step * (k + 0.5f)), 0f);
                        repo.Bus.Publish(Fdp.Toolkit.Terrain.GroundFireEvent.Of(new FireSpot(c.X, c.Y, at.Z, radius, (SimInstant)now, seconds, intensity)));
                    }
                }
                Strikes++;
            }
        }

        /// <summary>⭐ N-4 — how many spots lay an ellipse <paramref name="length"/> × <paramref name="width"/> along its axis (at least one).</summary>
        public static int SpotsOf(float length, float width) => Math.Max(1, (int)MathF.Round(length / MathF.Max(width, 1f)));

        /// <summary>The flight line on the ground (the round's travel); east when it fell straight down.</summary>
        public static Vector2 Axis(float travelX, float travelY)
        {
            var d = new Vector2(travelX, travelY);
            return d.LengthSquared() > 1e-6f ? Vector2.Normalize(d) : Vector2.UnitX;
        }

        /// <summary>Is <paramref name="p"/> inside the ellipse centred on <paramref name="at"/>, <paramref name="length"/> along <paramref name="axis"/>?</summary>
        public static bool InEllipse(Vector3 at, Vector2 axis, float length, float width, Vector3 p)
        {
            var d = new Vector2(p.X - at.X, p.Y - at.Y);
            float along = Vector2.Dot(d, axis), across = axis.X * d.Y - axis.Y * d.X;
            float a = length * 0.5f, b = width * 0.5f;
            return along * along / (a * a) + across * across / (b * b) <= 1f;
        }

        /// <summary>⭐ <c>CE-3488</c> (FL-4 B-3) — every BURNABLE building the ellipse reaches (its footprint's centre inside the ellipse grown by the footprint's
        /// radius) is set alight, by the shared <see cref="Fdp.Toolkit.Terrain.BuildingFires"/> rule.</summary>
        private static void LightBuildings(EntityRepository repo, Vector3 at, Vector2 axis, float length, float width, double now, float intensity)
        {
            if (!repo.Bus.IsRegistered<Fdp.Toolkit.Terrain.BuildingFireEvent>() || !repo.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>()
                || repo.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() is not { } terrain) return;
            var burnable = Fdp.Toolkit.Terrain.BuildingFires.Burnable(terrain);
            for (int i = 0; i < burnable.Count; i++)
            {
                var (c, r) = Fdp.Toolkit.Terrain.BuildingFires.Extent(terrain.Buildings[burnable[i]]);
                if (!InEllipse(at, axis, length + 2f * r, width + 2f * r, new Vector3(c, at.Z))) continue;
                if (Fdp.Toolkit.Terrain.BuildingFires.TryOf(terrain, burnable[i], now, intensity, out var fire)) repo.Bus.Publish(fire);
            }
        }

        /// <summary>⭐ N-5 — every living person inside the ellipse is set alight on his Health owner.</summary>
        private void Fireball(EntityRepository repo, in DetonationNotification evt, Vector3 at, Vector2 axis, float length, float width)
        {
            if (!repo.Bus.IsRegistered<DamageAssessedEvent>() || !repo.IsComponentTypeRegistered<Health>()) return;
            if (!ReferenceEquals(repo, _bodiesRepo)) { _bodies = repo.Query().With<Health>().With<SimTransform>().Build(); _bodiesRepo = repo; }
            foreach (var e in _bodies!)
            {
                if (repo.GetComponentRO<Health>(e).Current <= 0f || Ignition.Of(repo, e) != Ignition.Kind.Person) continue;
                if (!InEllipse(at, axis, length, width, repo.GetComponentRO<SimTransform>(e).Position)) continue;
                repo.Bus.Publish(new DamageAssessedEvent
                {
                    HitEntity = e, TotalDamage = 0f, Effect = HitEffect.Burning, EffectSeconds = Ignition.BurningManSeconds,
                    Source = evt.Shooter, OriginalSource = evt.OriginalShooter,
                });
            }
        }
    }
}
