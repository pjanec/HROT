using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints.Events;

namespace Fdp.Toolkit.Blueprints.Systems;

/// <summary>
/// Input-phase system that drains <see cref="AttachInstanceBlueprintEvent"/>,
/// <see cref="RemoveInstanceBlueprintEvent"/>, and <see cref="ReplaceInstanceBlueprintEvent"/>
/// from the <see cref="FdpEventBus"/> and applies them via the core
/// <see cref="BlueprintInstanceService"/> seam.
/// </summary>
/// <remarks>
/// <para><b>Remove-before-add ordering (Design §7):</b> within a frame, ALL Remove events
/// and Replace-event old-blueprint detachments are applied before ANY Attach event or
/// Replace-event new-blueprint attachment.  This guarantees that an in-place swap
/// (remove X + add same-size Y) frees and dense-compacts the slot first, and the add
/// reuses that capacity — no spurious tier upgrade.</para>
///
/// <para>Entity validity is checked via <see cref="EntityRepository.IsAlive"/> before
/// every mutation.  Events targeting dead entities are silently skipped.</para>
/// </remarks>
[UpdateInPhase(SystemPhase.Input)]
public sealed class BlueprintEventIngressSystem : IEcsModuleSystem
{
    private readonly BlueprintRegistry _registry;

    public BlueprintEventIngressSystem(BlueprintRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo)
            throw new InvalidOperationException(
                $"{nameof(BlueprintEventIngressSystem)} requires direct EntityRepository access " +
                $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

        // ── Phase 1: Apply ALL Removes FIRST (Design §7) ──

        // Drain Remove events.
        foreach (var evt in repo.Bus.Read<RemoveInstanceBlueprintEvent>())
        {
            if (!repo.IsAlive(evt.Entity)) continue;
            BlueprintInstanceService.DetachFromEntity(repo, evt.BlueprintId, evt.Entity);
        }

        // Drain Replace events — detach the OLD blueprint (remove half).
        foreach (var evt in repo.Bus.Read<ReplaceInstanceBlueprintEvent>())
        {
            if (!repo.IsAlive(evt.Entity)) continue;
            BlueprintInstanceService.DetachFromEntity(repo, evt.OldBlueprintId, evt.Entity);
        }

        // ── Phase 2: Apply ALL Attaches AFTER all removes ──

        // Drain Attach events.
        foreach (var evt in repo.Bus.Read<AttachInstanceBlueprintEvent>())
        {
            if (!repo.IsAlive(evt.Entity)) continue;
            BlueprintInstanceService.AttachToEntity(repo, _registry, evt.BlueprintId, evt.Entity);
        }

        // Drain Replace events — attach the NEW blueprint (add half).
        // Note: Read<T>() is non-consuming within a frame; it returns the same
        // events from the read buffer.  The buffer is only cleared at SwapBuffers
        // (end-of-frame).  Calling it twice — once in Phase 1 for the old id,
        // once in Phase 2 for the new id — is intentional and correct.
        foreach (var evt in repo.Bus.Read<ReplaceInstanceBlueprintEvent>())
        {
            if (!repo.IsAlive(evt.Entity)) continue;
            BlueprintInstanceService.AttachToEntity(repo, _registry, evt.NewBlueprintId, evt.Entity);
        }
    }
}
