using Fdp.Core;

namespace Fdp.Toolkit.Blueprints.Components;

/// <summary>
/// ⭐⭐⭐ <b>THE tier set, and the ONE place any world registers it.</b>
///
/// <para>📄 <c>docs/DESIGN_Entity_Creation_Unification.md</c> §2.3b.</para>
///
/// <para>🔴🔴 <b>Why this type exists — a MEASURED production crash, <c>2026-09-03</c>.</b> A
/// <c>--mode all</c> cluster aborted on the first live scenario load:</para>
/// <code>
/// InvalidOperationException: Component BlueprintBlackboard1024 is not registered.
///   at BehaviorIngressSystem.AddAndInitializeTier(...)
///   at BehaviorIngressSystem.ProvisionStatefulSlots(...)
///   at Hrot.CGF.CgfSubsystem.Update(...)          → process Aborted
/// </code>
///
/// <para>📐 <b>The gap, exactly.</b> <see cref="Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem"/>
/// provisions these tiers, and <c>MissionControlModule</c> schedules that system — but the only code that
/// REGISTERED them was <c>Hrot.Blueprints.Editor.Runtime.BlueprintRuntimeWiring.RegisterTierComponents</c>,
/// whose sole production caller is the Editor. ⇒ <b>CGF scheduled the system and never registered its
/// precondition</b>, so any host running behaviours outside the editor crashed on the first entity with
/// stateful behaviour slots.</para>
///
/// <para>⛔⛔ <b>And it was a REGISTRATION owned from the wrong layer.</b> These components live HERE, in
/// <c>Fdp.Toolkits</c>, in the same assembly as the system that needs them — there was never a
/// reference-graph reason for the registration to sit several layers up in <c>Hrot.Blueprints.Editor</c>.
/// ⭐ That is the whole defect: a host-level assembly owning a toolkit-level precondition, so every host
/// that did not go through that one assembly was silently missing it.</para>
///
/// <para>⭐⭐ <b>Why a list here rather than a line per host.</b> 🔒 The standing ruling on this
/// programme: <i>"every ECS node must use the same shared code."</i> A per-host call is a per-host chance
/// to forget — and this one was forgotten by three of the four node bootstrappers. ⇒ the tier list lives
/// with the tier types, and the Hrot-wide component registry calls it once for every node.</para>
///
/// <para>⚠ <b>Registration is not allocation.</b> Registering a component type creates its table
/// metadata; storage is per-entity and only materialises when the component is actually added. So a node
/// that never provisions a blackboard pays for the registration, not for 16 KB an entity — which is why
/// registering all three everywhere is the cheap, uniform answer rather than a per-role subset.</para>
/// </summary>
public static class BlueprintBlackboardTiers
{
    /// <summary>
    /// Registers every blueprint blackboard tier on <paramref name="world"/>.
    ///
    /// <para>⭐ Idempotent: <c>EntityRepository.RegisterComponent</c> resolves an already-registered type
    /// to its existing id, so a host that reaches this through two paths is unharmed. ⛔ Do not rely on
    /// that to justify a second call site — there should be one.</para>
    /// </summary>
    public static void RegisterAll(EntityRepository world)
    {
        if (world is null) throw new System.ArgumentNullException(nameof(world));

        world.RegisterComponent<BlueprintBlackboard1024>();
        world.RegisterComponent<BlueprintBlackboard4096>();
        world.RegisterComponent<BlueprintBlackboard16384>();
    }
}
