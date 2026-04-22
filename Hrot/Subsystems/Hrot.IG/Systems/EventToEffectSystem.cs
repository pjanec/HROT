using System.Numerics;
using Hrot.IG.Components;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;

namespace Hrot.IG.Systems;

/// <summary>
/// Simulation-phase system that consumes <see cref="DetonationNotification"/> and
/// <see cref="WeaponFireNotification"/> events and spawns short-lived visual effect
/// entities for each event:
/// <list type="bullet">
///   <item>
///     <b>Explosion</b> — a <see cref="EffectType.Explosion"/> entity placed at the
///     detonation hit position, expanding circle fading over
///     <see cref="VisualEffectStateConstants.ExplosionDurationSeconds"/>.
///   </item>
///   <item>
///     <b>Tracer</b> — a <see cref="EffectType.Tracer"/> entity placed at the shooter
///     position with a <see cref="TracerTarget"/> pointing at the target, fading over
///     <see cref="VisualEffectStateConstants.TracerDurationSeconds"/>.
///   </item>
/// </list>
///
/// Spawned entities carry no <see cref="EntityMaster"/> — they are ephemeral
/// render-only objects managed entirely by <see cref="VisualEffectCleanupSystem"/>.
///
/// Zero allocations on the hot path (§CODE-STANDARDS §4).
/// All colour and duration constants from <see cref="VisualEffectStateConstants"/>
/// (§CODE-STANDARDS §1).
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
[UpdateBefore(typeof(VisualEffectCleanupSystem))]
public class EventToEffectSystem : IEcsModuleSystem
{
    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        var cmd = view.GetCommandBuffer();

        // Explosions from DetonationNotification (published by HitResolutionSystem locally
        // or by MunitionDetonationIngressTranslator on the IG node).
        var detonations = view.ReadEvents<DetonationNotification>();
        foreach (ref readonly var evt in detonations)
        {
            SpawnExplosion(cmd, evt.HitX, evt.HitY);
        }

        // Tracers from WeaponFireNotification (published by FireProcessingSystem locally
        // or by WeaponFireIngressTranslator on the IG node).
        var weaponFires = view.ReadEvents<WeaponFireNotification>();
        foreach (ref readonly var evt in weaponFires)
        {
            // Skip if either entity is gone or lacks a position.
            if (evt.Shooter == Entity.Null || evt.Target == Entity.Null) continue;
            if (!view.IsAlive(evt.Shooter) || !view.IsAlive(evt.Target)) continue;
            if (!view.HasComponent<SimTransform>(evt.Shooter) || !view.HasComponent<SimTransform>(evt.Target)) continue;

            ref readonly var shooterTf = ref view.GetComponentRO<SimTransform>(evt.Shooter);
            ref readonly var targetTf  = ref view.GetComponentRO<SimTransform>(evt.Target);
            SpawnTracer(cmd, shooterTf.Position.X, shooterTf.Position.Y, targetTf.Position.X, targetTf.Position.Y);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void SpawnExplosion(IEntityCommandBuffer cmd, float x, float y)
    {
        var entity = cmd.CreateEntity();

        cmd.AddComponent(entity, new SimTransform
        {
            Position = new Vector3(x, y, 0f),
            Rotation = Quaternion.Identity,
        });

        cmd.AddComponent(entity, new VisualEffectState
        {
            Type      = EffectType.Explosion,
            Duration  = VisualEffectStateConstants.ExplosionDurationSeconds,
            ElapsedTime = 0f,
            ColorR    = VisualEffectStateConstants.ExplosionColorR,
            ColorG    = VisualEffectStateConstants.ExplosionColorG,
            ColorB    = VisualEffectStateConstants.ExplosionColorB,
            ColorA    = VisualEffectStateConstants.ExplosionColorA,
            Scale     = VisualEffectStateConstants.ExplosionInitialScale,
        });
    }

    private static void SpawnTracer(IEntityCommandBuffer cmd,
        float shooterX, float shooterY,
        float targetX,  float targetY)
    {
        var entity = cmd.CreateEntity();

        cmd.AddComponent(entity, new SimTransform
        {
            Position = new Vector3(shooterX, shooterY, 0f),
            Rotation = Quaternion.Identity,
        });

        cmd.AddComponent(entity, new TracerTarget
        {
            EndX = targetX,
            EndY = targetY,
        });

        cmd.AddComponent(entity, new VisualEffectState
        {
            Type        = EffectType.Tracer,
            Duration    = VisualEffectStateConstants.TracerDurationSeconds,
            ElapsedTime = 0f,
            ColorR      = VisualEffectStateConstants.TracerColorR,
            ColorG      = VisualEffectStateConstants.TracerColorG,
            ColorB      = VisualEffectStateConstants.TracerColorB,
            ColorA      = VisualEffectStateConstants.TracerColorA,
            Scale       = VisualEffectStateConstants.TracerScale,
        });
    }
}

/// <summary>
/// Simulation-phase system that advances the age of all visual effect entities
/// and destroys those whose lifetime has expired.
///
/// Runs in the same phase as <see cref="EventToEffectSystem"/> so that effects
/// spawned in the current frame are not immediately destroyed — the command-buffer
/// separation guarantees effects survive at least one full frame.
///
/// Zero allocations in <see cref="Execute"/> (§CODE-STANDARDS §4).
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
public class VisualEffectCleanupSystem : IEcsModuleSystem
{
    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        var cmd   = view.GetCommandBuffer();
        var query = view.Query().With<VisualEffectState>().Build();

        foreach (var entity in query)
        {
            ref readonly var effect = ref view.GetComponentRO<VisualEffectState>(entity);

            var updated = effect;
            updated.ElapsedTime += deltaTime;

            if (updated.IsExpired)
            {
                cmd.DestroyEntity(entity);
            }
            else
            {
                cmd.SetComponent(entity, updated);
            }
        }
    }
}
