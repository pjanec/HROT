using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;

namespace Hrot.Map.Common;

/// <summary>
/// Shared registration for mission-state components.
/// </summary>
public static class MissionComponentRegistry
{
    /// <summary>
    /// Registers mission component schema.
    /// </summary>
    public static void RegisterAll(EntityRepository world)
    {
        world.RegisterManagedComponent<ActiveMissionPlan>();
        // ⭐⭐ MOVED HERE 2026-09-12 from CognitiveComponentRegistry (CE-259bf slice 2, design §3.9a).
        //   MissionPlanQueue is the mission tier's queue and belongs beside ActiveMissionPlan.
        //   ⚠ BEHAVIOUR-PRESERVING: SimHost and CGF both already call this registry, so the set each
        //   host registers is unchanged. It is what lets SimHost stop calling the BRAIN's registry.
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.MissionPlanQueue>();
        world.RegisterManagedEvent<Hrot.Common.Events.MissionControlIntent>();

        // ⭐⭐⭐ CE-270 — the ACK half of the same CQRS pair. ADDED 2026-09-13 after a MEASURED live
        //   cluster crash: on a 3-process run (orchestrator/CGF/SimHost/IG), a single
        //   `POST /missions/{id}/task` KILLED CGF outright with
        //     "Strict Mode Violation: Unmanaged event type 'MissionControlAckEvent' (ID: 6002) was
        //      published without being explicitly registered."
        //     at MissionControlExecutionSystem.PublishAck(...) MissionControlExecutionSystem.cs:251
        //
        // 📐 The shape, measured: `MissionControlExecutionSystem` lives in Hrot.Common and is scheduled
        //   on CGF, but the ONLY registrations of its ack were SimHostComponentRegistry.cs:65 and
        //   StrideNodeBootstrapper.cs:320 — two host registries out of four. CGF scheduled the publisher
        //   and never registered its event. ⛔ Unlike the ModuleHost's swallowed system exceptions, this
        //   one escapes `Update()` and ABORTS THE PROCESS, so it is not a silent degradation.
        //
        // ⭐ Home chosen so the pair cannot drift: the INTENT is registered on the line above, in this
        //   registry, and all four node bootstrappers already call it (IgRoleComponentRegistry.cs:40,
        //   CgfComponentRegistry.cs:39, SimHostComponentRegistry.cs:44, StrideNodeBootstrapper.cs:313).
        //   ⛔ Deliberately NOT a line added to CgfComponentRegistry — that is a fourth chance to forget,
        //   which is exactly how this arose. Same reasoning as the OwnershipUpdate fix recorded in
        //   HrotSharedComponentRegistry.RegisterAll.
        //
        // ⚠ The two host registrations are left in place: `Bus.Register<T>()` is GetOrCreate, so a second
        //   call is a no-op, and "no rush removals" applies — deleting them is a separate, evidenced step.
        world.RegisterEvent<Hrot.Common.Events.MissionControlAckEvent>();
    }
}
