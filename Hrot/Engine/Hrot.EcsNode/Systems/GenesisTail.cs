using Fdp.ModuleHost;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// ⭐⭐ CE-4013 D11 — the entity-creation TAIL every ECS host composes, written once: the genesis materialisation that resolves a loaded
    /// scenario's cross-entity intents, and the initial-brain materialisation (CE-3042) that takes a saved AI to the ingress or drops it.
    /// 📄 docs/DESIGN_Subsystem_Composition_Unification.md §4.1f ("belongs with the pack, not a role module") · §4.1ae.1.
    /// </summary>
    /// <remarks>
    /// ⛔ It lives here, not on <c>EntityCreation.RegisterOn</c>, only because both systems are in this assembly and the pack is in
    /// <c>Hrot.Common</c>. Four hosts registered this pair by hand; IG and the traffic node registered neither.
    /// </remarks>
    public static class GenesisTail
    {
        /// <summary>Registers the genesis materialization (Input phase) on <paramref name="kernel"/>, at the caller's point. ⭐ CE-3494 — the brain half went to BehaviorIngressSystem.</summary>
        public static void RegisterOn(ModuleHostKernel kernel, NetworkEntityMap entityMap)
        {
            kernel.RegisterGlobalSystem(new GenesisMaterializationSystem(entityMap));
            // ⛔ CE-3494 — InitialBrainMaterializationSystem is gone: BehaviorIngressSystem applies the loaded UnitIntent on the first
            //   frame where time advances (DESIGN_Unit_Intent.md §1 R1); it started the AI on a PAUSED frame.
        }
    }
}
