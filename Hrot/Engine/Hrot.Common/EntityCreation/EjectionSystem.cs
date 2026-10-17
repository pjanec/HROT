using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Common.EntityCreation
{
    /// <summary>
    /// ⭐⭐ Airborne insertion B-8 / B-9 (docs/designs/airborne/DESIGN_Airborne_Insertion.md §7d, D-14…D-17) — the crew EJECTS: on an
    /// <see cref="EjectRequestEvent"/> (a behaviour's Eject action) or on its own when the aircraft's health falls to its type's
    /// <see cref="EjectionDto.AutoEjectBelowFraction"/>, the node owning the aircraft's <see cref="Health"/> (<see cref="Embarkation.OwnsHealth"/>)
    /// <list type="number">
    ///   <item>SPAWNS the pilot (<see cref="EjectionDto.PilotType"/>) through the entity-creation path at the seat's world point, with the
    ///     aircraft's velocity plus the rocket's climb (<see cref="ParachuteModel.EjectUpMps"/>) and a <see cref="Descent"/>{Ejection}; then</item>
    ///   <item>ABANDONS the aircraft: a <see cref="DamageAssessedEvent"/> of all its health — it falls and crashes as a killed one
    ///     (<c>FixedWingModel</c>, <c>DroneImpactSystem</c>). The pilot is never a rider, so he does not die with it (R-2021).</item>
    /// </list>
    /// Once per aircraft (a latch), and only while it is alive. Scheduled in the Interactions capability: every node, gated on what it owns.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class EjectionSystem : IEcsModuleSystem
    {
        private readonly HashSet<Entity> _ejected = new();
        private readonly Dictionary<long, EjectionDto?> _byType = new();
        private readonly List<Entity> _toEject = new();

        /// <summary>Ejections this node made (a rail and a diagnostics counter read it).</summary>
        public long Ejections { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<Health>()) return;
            _toEject.Clear();
            if (repo.Bus.IsRegistered<EjectRequestEvent>())
                foreach (var e in repo.Bus.Read<EjectRequestEvent>()) _toEject.Add(e.Aircraft);
            if (repo.IsComponentTypeRegistered<TkbIdentity>())
                foreach (var e in repo.Query().With<Health>().With<TkbIdentity>().With<SimTransform>().Build())
                {
                    if (EjectionOf(repo, e) is not { AutoEjectBelowFraction: > 0f } ej) continue;
                    ref readonly var h = ref repo.GetComponentRO<Health>(e);
                    if (h.Current > 0f && h.Max > 0f && h.Current <= ej.AutoEjectBelowFraction * h.Max || Wrecked(repo, e)) _toEject.Add(e);   // ⭐ CE-3215
                }
            foreach (var aircraft in _toEject) Eject(repo, aircraft);
        }

        private void Eject(EntityRepository repo, Entity aircraft)
        {
            if (_ejected.Contains(aircraft) || !repo.IsAlive(aircraft) || !repo.HasComponent<Health>(aircraft)) return;
            ref readonly var health = ref repo.GetComponentRO<Health>(aircraft);
            if (health.Current <= 0f && !Wrecked(repo, aircraft) || !Embarkation.OwnsHealth(repo, aircraft)) return;   // ⭐ CE-3215 — a wreck still ejects
            if (EjectionOf(repo, aircraft) is not { PilotType: > 0 } ej || !repo.HasComponent<SimTransform>(aircraft)) return;
            // ⭐ CE-3228 / CE-3226 (Entity Creation §3.4d) — the pilot is ASKED FOR on the bus; the request system reads it next frame.
            //   A world whose bus cannot carry the request (no shared registry, no creation pack) creates nothing here.
            if (!repo.Bus.IsRegisteredManaged<Fdp.Toolkit.NetworkSpawning.Events.EntityCreationRequest>()) return;
            _ejected.Add(aircraft);

            // ① the pilot, at the seat, with the aircraft's velocity and the rocket's climb, coming down from the start
            // ⭐ CE-2210 — where the jet is and how it moves AS THIS NODE KNOWS IT: the Health owner (the Brain) does not own the jet's
            //   motion, so its SimVelocity is never refreshed there (a pilot born with the rocket's climb and none of the jet's speed).
            ref readonly var tf = ref repo.GetComponentRO<SimTransform>(aircraft);
            Fdp.Toolkit.Replication.ReplicatedMotion.Of(repo, aircraft, out var at, out var v);
            var seat = at + Vector3.Transform(new Vector3(ej.SeatX, ej.SeatY, ej.SeatZ), tf.Rotation);
            float yaw = RotorcraftModel.YawOf(tf.Rotation);
            repo.Bus.PublishManaged(new Fdp.Toolkit.NetworkSpawning.Events.EntityCreationRequest
            {
                RequestId = Guid.NewGuid(),
                OwnerAppInstanceId = Hrot.Core.Network.EntityCreationRouting.DefaultEntityCreationRequestProcessor,
                TkbType = ej.PilotType,
                InitialComponents = new List<object>
                {
                    new SimTransform { Position = seat, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) },
                    new SimVelocity { Linear = v + new Vector3(0f, 0f, ParachuteModel.EjectUpMps) },
                    new Descent { Kind = DescentKind.Ejection, Phase = DescentPhase.Exit, Canopy = CanopyKind.Round, PhaseSince = DescentRules.Now(repo) },
                },
            });

            // ② then the aircraft is abandoned: all its health, through the one damage chain
            if (repo.Bus.IsRegistered<DamageAssessedEvent>())
                repo.Bus.Publish(new DamageAssessedEvent { HitEntity = aircraft, TotalDamage = health.Current });
            Ejections++;
        }

        /// <summary>⭐ <c>CE-3215</c> (hit location §12 S2c-6) — killed in one blow by a CATASTROPHIC part (its tail, an engine): the crew
        /// still gets out (once — the latch).</summary>
        private static bool Wrecked(EntityRepository repo, Entity aircraft)
            => Fdp.Toolkit.Physics.BodyParts.Wounds.Has(repo, aircraft, WoundEffect.Catastrophic);

        private EjectionDto? EjectionOf(EntityRepository repo, Entity e)
        {
            if (!repo.IsComponentTypeRegistered<TkbIdentity>() || !repo.HasComponent<TkbIdentity>(e)) return null;
            long type = repo.GetComponentRO<TkbIdentity>(e).TkbType;
            if (_byType.TryGetValue(type, out var known)) return known;
            var ej = CombatTkb.TryGetTemplate(repo, e, out var t) ? t.GetDescriptor<EjectionDto>() : null;
            _byType[type] = ej;
            return ej;
        }
    }
}
