using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Weather;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// ⭐ <c>CE-3219</c> part 3 (civilians B-1, R-3014; docs/DESIGN_Civilians_And_Crowd_Control.md §7.3b) — the owner's side of a gas / smoke
    /// cloud, ONCE A SECOND per cloud (🔒 user: "not every tick"): it DRIFTS by the ground wind × the time since its last step
    /// (<see cref="WeatherField.WindAt"/> at its position, so a scenario with no weather leaves it where it burst), and a tear-gas cloud
    /// IRRITATES every living PERSON inside (<see cref="PhysicsColliderReaders.IsPerson"/> — a vehicle is not gassed): an Irritant hit with no
    /// damage, through the one damage chain to the owner of his <c>Health</c>. It ENDS at its time (checked each tick; the delete replicates).
    /// Scheduled on every node (the Interactions capability) and gated on what this node owns, so whoever owns a cloud runs it.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class AreaEffectCloudSystem : IEcsModuleSystem
    {
        /// <summary>How often a cloud steps — drifts, and irritates whoever is in it (s).</summary>
        public const double StepSeconds = 1.0;

        private readonly Dictionary<Entity, double> _lastStep = new();
        private readonly HashSet<Entity> _ending = new();   // ⭐ a networked cloud whose end was asked for (once)
        private readonly List<Entity> _gone = new();

        /// <summary>Irritant hits this node published (a rail and a diagnostics counter read it).</summary>
        public long Pulses { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<AreaEffectCloud>()) return;
            double now = SimClock.Of(repo).TotalTime;
            var cmd = view.GetCommandBuffer();
            _gone.Clear();
            foreach (var e in _lastStep.Keys) if (!repo.IsAlive(e)) _gone.Add(e);
            foreach (var e in _gone) _lastStep.Remove(e);
            _ending.RemoveWhere(e => !repo.IsAlive(e));

            foreach (var e in repo.Query().With<AreaEffectCloud>().With<SimTransform>().Build())
            {
                if (!repo.HasAuthority(e)) continue;
                var cloud = repo.GetComponentRO<AreaEffectCloud>(e);
                if (cloud.IsOver(now))
                {
                    End(repo, cmd, e);
                    _lastStep.Remove(e);
                    continue;
                }
                if (!_lastStep.TryGetValue(e, out double last)) last = cloud.BornAtSimSeconds;   // the first step comes at once (a burst irritates at once)
                else if (now - last < StepSeconds) continue;
                _lastStep[e] = now;

                // ① drift on the wind at the cloud (the ground wind: its base height), for the time since its last step — ⭐ CE-3236 (B-14,
                //   §7.15 VC-4) unless it still sits on its venting canister
                ref var tf = ref repo.GetComponentRW<SimTransform>(e);
                if (!SitsOnItsSource(repo, e))
                {
                    // ⭐ CE-3258 (CE-2287, R-2037) — the wind at the cloud's middle over the ground it lies on (its own Z — no terrain read; its
                    //   owner is CGF, a brain node)
                    var wind = AreaEffectClouds.DriftWind(repo, tf.Position, cloud.Height);
                    float elapsed = (float)(now - last);
                    if (elapsed > 0f && wind != Vector2.Zero) tf.Position += new Vector3(wind * elapsed, 0f);
                }

                // ② a tear-gas cloud irritates whoever stands in it
                if (cloud.Kind == CloudKind.TearGas && cloud.IrritantSeconds > 0f) Pulse(repo, in cloud, tf.Position);
            }
        }

        /// <summary>
        /// ⭐ <c>CE-3228</c> — a REPLICATED cloud ends through the one teardown path, <see cref="Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand"/>
        /// (Entity Creation §3.4c): the lifecycle teardown deletes it on the wire, so every replica goes too. 📐 A direct
        /// <c>DestroyEntity</c> removed only the owner's copy — measured by <c>EntityOrdersAcrossNodesTests.CE3219_…</c> once the default
        /// processor owned it (gone on CGF, still on SimHost and the IG). A cloud with no network identity (an offline world) is destroyed here.
        /// </summary>
        private void End(EntityRepository repo, Fdp.Interfaces.IEntityCommandBuffer cmd, Entity e)
        {
            if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()
                && repo.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e)
                && repo.Bus.IsRegisteredManaged<Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand>())
            {
                if (!_ending.Add(e)) return;   // asked already; the teardown is under way
                repo.Bus.PublishManaged(new Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand
                {
                    NetworkId = repo.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value,
                    Reason    = "AreaEffectCloud ended",
                });
                return;
            }
            cmd.DestroyEntity(e);
        }

        /// <summary>
        /// ⭐ <c>CE-3236</c> (B-14, §7.15 VC-4) — does the cloud still sit on its venting canister? While its source resolves, yes; once it is
        /// KNOWN gone (or cannot be resolved on a node with no entity map — one world), the cloud lets go: <c>SourceNetworkId</c> = 0, and it
        /// drifts from then on. A source not yet seen here (its replica on the way) holds it.
        /// </summary>
        private static bool SitsOnItsSource(EntityRepository repo, Entity cloud)
        {
            long source = repo.GetComponentRO<AreaEffectCloud>(cloud).SourceNetworkId;
            if (source <= 0) return false;
            if (!Fdp.Toolkit.Replication.Services.NetworkIdResolver.ResolveNetworkId(repo, source).IsNull) return true;
            bool mapless = !repo.HasSingletonManaged<Fdp.Toolkit.Replication.Services.NetworkEntityMap>();
            if (!mapless && !Fdp.Toolkit.Replication.Services.NetworkIdResolver.IsKnownDeadAnchor(repo, source)) return true;
            repo.GetComponentRW<AreaEffectCloud>(cloud).SourceNetworkId = 0;
            return false;
        }

        /// <summary>⭐ <c>CE-3236</c> (B-14, VC-6) — one Irritant hit on <paramref name="person"/> (no damage, <paramref name="seconds"/> long),
        /// through the one damage chain: the cloud's pulse and a canister burning in a man's hand.</summary>
        public static bool Irritate(EntityRepository repo, Entity person, float seconds)
        {
            if (!repo.Bus.IsRegistered<DamageAssessedEvent>()) return false;
            repo.Bus.Publish(new DamageAssessedEvent { HitEntity = person, TotalDamage = 0f, Effect = HitEffect.Irritant, EffectSeconds = seconds });
            return true;
        }

        private void Pulse(EntityRepository repo, in AreaEffectCloud cloud, Vector3 at)
        {
            if (!repo.IsComponentTypeRegistered<Health>() || !repo.Bus.IsRegistered<DamageAssessedEvent>()) return;
            foreach (var p in repo.Query().With<Health>().With<SimTransform>().Build())
            {
                if (!PhysicsColliderReaders.IsPerson(repo, p)) continue;               // people only — a vehicle is not gassed
                if (repo.GetComponentRO<Health>(p).Current <= 0f) continue;
                if (Perception.PhysicalPresence.IsHidden(repo, p)) continue;           // inside an enclosed hull
                if (!AreaEffectClouds.Contains(in cloud, at, repo.GetComponentRO<SimTransform>(p).Position)) continue;
                if (Irritate(repo, p, cloud.IrritantSeconds)) Pulses++;
            }
        }
    }
}
