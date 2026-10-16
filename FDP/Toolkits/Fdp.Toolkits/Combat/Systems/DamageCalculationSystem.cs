using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// Consumes <see cref="DetonationNotification"/> events, computes the HP loss through <see cref="ArmorModel"/> (⭐ <c>CE-3071</c>; was flat)
    /// value and publishes a <see cref="DamageAssessedEvent"/> for the authoritative
    /// node to apply to the entity's <c>Health</c> component.
    ///
    /// <para>
    /// <b>No entity-ownership gate:</b> This system runs exclusively on the Muscle node.
    /// Because the Muscle is the designated damage-calculation authority for all detonations
    /// it observes (via <c>HitResolutionSystem</c> or <c>MunitionDetonationIngressTranslator</c>),
    /// the existence of a live target entity is sufficient to publish the verdict.
    /// Entity CQRS ownership (Brain vs. Muscle) is not checked here.
    /// </para>
    ///
    /// <para>
    /// <b>Execution phase:</b> <see cref="SimulationSystemGroup"/> — runs after ingress
    /// translators so that <see cref="DetonationNotification"/> events published by
    /// <c>MunitionDetonationIngressTranslator</c> in the same tick are available.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public class DamageCalculationSystem : IEcsModuleSystem
    {
        public DamageCalculationSystem()
        {
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(DamageCalculationSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var events = repo.Bus.Read<DetonationNotification>();
            if (events.Length == 0) return;

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];
                if (evt.IsRemote) continue;

                // PACK-P003: evt.Target is already a local ECS Entity handle.
                // Skip if the entity is not alive on this node.
                var targetEntity = evt.Target;
                if (!repo.IsAlive(targetEntity)) continue;

                // No entity-ownership gate here: DamageCalculationSystem runs exclusively
                // on the Muscle node. The fact that a DetonationNotification was emitted
                // (by HitResolutionSystem or MunitionDetonationIngressTranslator) is
                // sufficient -- the Muscle is the designated damage-calculation authority for
                // all detonations it observes, regardless of which node owns the entity in
                // the CQRS ownership map.

                // ⭐⭐ CE-3071 (R-212) — ammunition vs armour, through the ONE model the AI also uses (ArmorModel): the
                //   face the shot came from (the shooter, or the hit point when the shooter is gone), that face's armour
                //   from the target's TKB type, and the round's penetration and damage the bullet carried. An unknown
                //   munition (an external detonation: penetration 0) keeps the flat default. Was: a flat 25 for every hit.
                //   ⭐ M1/M2 (DESIGN_Armour_Hit_Model §3) — the face from the way the ROUND was travelling (Top when it descends
                //   ≥ 45°), and the armour column its penetrator reads (a shaped charge: the …Ce column). A detonation with no
                //   travel (published by hand, ingress) keeps the legacy rule: the face from the shooter (or the hit point),
                //   horizontal, as before M1.
                float armour = 0f;
                if (evt.Penetration > 0f && repo.HasComponent<SimTransform>(targetEntity))
                {
                    var targetTf = repo.GetComponent<SimTransform>(targetEntity);
                    var travel = new Vector3(evt.TravelX, evt.TravelY, evt.TravelZ);
                    Facing facing;
                    // ⭐ CE-3209 (HL-5) — a round that met a body PART: the face it ENTERED (the part's side, in the body's frame)
                    if (evt.HasBodyPart != 0)
                        facing = (Facing)evt.Face;
                    else if (travel.LengthSquared() > 1e-8f)
                        facing = ArmorModel.FacingOfArrival(targetTf, travel);
                    else
                    {
                        var from = repo.IsAlive(evt.Shooter) && repo.HasComponent<SimTransform>(evt.Shooter)
                            ? repo.GetComponent<SimTransform>(evt.Shooter).Position
                            : new Vector3(evt.HitX, evt.HitY, evt.HitZ);
                        from.Z = targetTf.Position.Z;   // the legacy rule is horizontal
                        facing = ArmorModel.FacingOf(targetTf, from);
                    }
                    armour = ArmorModel.ArmourFor(CombatTkb.PlatformOf(repo, targetEntity), facing, CombatTkb.PenetratorOf(repo, evt.Ammo));
                }
                // ⭐⭐ CE-3210 (hit location HL-4, HL-12) — a round on a body PART: that zone's armour on the face entered (a helmet, a
                //   vest), its damage multiplier (the head ×4, a limb below 1). The type's zones resolve through its override, its own
                //   armour (the torso's), its DIS-class profile, the engine — BodyZones. ⛔ No part ⇒ the whole body, as before.
                float multiplier = 1f;
                if (evt.HasBodyPart != 0 && Fdp.Toolkit.Physics.BodyParts.BodyZones.Of(repo, targetEntity)?.ZoneOf(evt.BodyPart) is { } zone)
                {
                    // ⭐ CE-3211 (S2-7) — a vehicle zone with no number of its own (NaN) keeps the platform's armour on the face entered
                    if (evt.Penetration > 0f && !float.IsNaN(zone.ArmourOn((Facing)evt.Face))) armour = zone.ArmourOn((Facing)evt.Face);
                    multiplier = zone.Multiplier;
                }
                float damage = ArmorModel.HitDamage(evt.Penetration, evt.Damage, armour) * multiplier;

                // ⭐ CE-3219 (civilians B-1, CV-6) — a BLUNT round (a baton round, a thrown stone) knocks the man it hits down for its
                //   warhead's EffectSeconds; the Health owner stores it (EffectState). Every other round is Kinetic, as before.
                var effectWarhead = evt.Ammo != 0 ? CombatTkb.WarheadOf(repo, evt.Ammo) : null;
                bool blunt = effectWarhead is { Kind: Fdp.Toolkit.Tkb.Domain.WarheadKind.Blunt, EffectSeconds: > 0f };

                repo.Bus.Publish(new DamageAssessedEvent
                {
                    HitEntity   = targetEntity,
                    TotalDamage = damage,
                    HasBodyPart = evt.HasBodyPart,   // ⭐ CE-3210 — the Health owner records it on the part
                    BodyPart    = evt.BodyPart,
                    Effect        = blunt ? HitEffect.Blunt : HitEffect.Kinetic,
                    EffectSeconds = blunt ? effectWarhead!.EffectSeconds : 0f,
                });
            }
        }
    }
}
