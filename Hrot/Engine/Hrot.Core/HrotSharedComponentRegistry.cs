using Hrot.Map.Common.Events;
using Hrot.Map.Definitions.Tkb;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Messages;

namespace Hrot.Map.Common;

/// <summary>
/// Single source of truth for ECS component and event registrations that are
/// shared by every Hrot application (IG, SimHost, any future subsystem).
///
/// <para>This covers the "network replication" layer, the geographic foundation,
/// shared managed definitions, and the lifecycle event protocol.  Application-
/// or subsystem-specific registrations (e.g. behaviour-AI, CarKinem physics,
/// IG visual components) are handled by their respective registries.</para>
///
/// <para>Usage example:
/// <code>
/// var world = new EntityRepository();
/// HrotSharedComponentRegistry.RegisterAll(world);
/// // … then add application-specific registrations …
/// </code>
/// </para>
/// </summary>
public static class HrotSharedComponentRegistry
{
    /// <summary>
    /// Registers all shared components and events into <paramref name="world"/>.
    /// Must be called immediately after <see cref="EntityRepository"/> construction,
    /// before any module or system is initialised.
    /// </summary>
    public static void RegisterAll(EntityRepository world)
    {
        // ── Network replication components ────────────────────────────────────
        world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<NetworkOwnership>();
        world.RegisterComponent<NetworkAuthority>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<GhostStateTracker>();
        world.RegisterComponent<PendingNetworkAck>();
        world.RegisterComponent<NetworkTransform>();
        world.RegisterComponent<NetworkVelocity>();

        // ── Geographic / physics ──────────────────────────────────────────────
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<SimVelocity>();

        // ── Hierarchical entity linking (personal routes, sub-entities) ──────
        world.RegisterComponent<PartMetadata>();

        // ── Shared managed definitions ────────────────────────────────────────
        world.RegisterComponent<VisualData>();
        world.RegisterManagedComponent<SimCombatDef>();
        world.RegisterManagedComponent<TkbCompositionDef>();

        // ── Network replication managed components ────────────────────────────
        // DescriptorOwnership: per-entity descriptor → owner node tracking.
        // PendingAuthorityGrants: pre-genesis routing intent (Muscle role).
        world.RegisterManagedComponent<DescriptorOwnership>();
        world.RegisterManagedComponent<PendingAuthorityGrants>();

        // ── Lifecycle events (network entity construction / destruction) ──────
        world.RegisterEvent<ConstructionOrder>();
        world.RegisterEvent<ConstructionAck>();
        world.RegisterEvent<DestructionOrder>();
        world.RegisterEvent<DestructionAck>();

        // ── Combat ────────────────────────────────────────────────────────────
        // Health is shared so Brain, Muscle, and IG all materialise the same
        // unmanaged struct layout when applying a TKB template.
        world.RegisterComponent<Health>();

        // ── Application-layer events ──────────────────────────────────────────
        world.RegisterEvent<FireInteractionEvent>();
        world.RegisterManagedEvent<AdvanceFrameIntent>();
        world.RegisterManagedEvent<FrameStepCompletedEvent>();
        world.RegisterManagedEvent<PauseTimeIntent>();
        world.RegisterManagedEvent<ResumeTimeIntent>();
        world.RegisterManagedEvent<StepTimeIntent>();
        world.RegisterManagedEvent<SetTimeScaleIntent>();
        world.RegisterManagedEvent<SlaveNodeSetUpdatedEvent>();
        world.RegisterManagedEvent<SpawnEntityCommand>();
        world.RegisterManagedEvent<UpdateEntityCommand>();
        world.RegisterManagedEvent<DestroyEntityCommand>();
        world.RegisterManagedEvent<DeferredTakeOwnershipCommand>();
        world.RegisterEvent<SwitchTimeModeEvent>();
        world.RegisterEvent<TimeSyncRequest>();
        world.RegisterEvent<TimeSyncResponse>();
        world.RegisterEvent<TimeSyncOffsetCalculatedEvent>();
        world.RegisterEvent<Fdp.Toolkit.Replication.Components.DescriptorAuthorityChanged>();
        world.RegisterManagedEvent<Fdp.Toolkit.Replication.Events.UpdateEntityAttributeCommand>();

        // ── Blueprint blackboard tiers ────────────────────────────────────────
        // ⭐⭐⭐ ADDED 2026-09-03 after a MEASURED production crash: a `--mode all` cluster aborted on
        //    its first live scenario load with
        //      "Component BlueprintBlackboard1024 is not registered"
        //    from BehaviorIngressSystem.ProvisionStatefulSlots, on CGF.
        //
        // 📐 BehaviorIngressSystem provisions these tiers and MissionControlModule schedules it, but the
        //    only code that REGISTERED them was Hrot.Blueprints.Editor's BlueprintRuntimeWiring, whose
        //    sole production caller is the Editor. ⇒ CGF scheduled the system and never registered its
        //    precondition. Three of the four node bootstrappers were missing it.
        //
        // ⭐ The tier LIST lives with the tier TYPES (Fdp.Toolkits, the same assembly as the system that
        //    needs them); this registry is the one Hrot-wide call site, so no node can be missing it.
        //    ⛔ Deliberately NOT a line added to CgfComponentRegistry — that would be a fourth chance to
        //    forget, which is exactly how this arose.
        // 📄 docs/DESIGN_Entity_Creation_Unification.md §2.3b.
        Fdp.Toolkit.Blueprints.Components.BlueprintBlackboardTiers.RegisterAll(world);
    }
}
