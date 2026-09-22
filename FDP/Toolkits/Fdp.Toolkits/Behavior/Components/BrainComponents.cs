using System.Runtime.InteropServices;
using Fbt;
using Fhsm.Kernel.Data;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    // ⛔⛔⛔ O7c-② / CE-319 (2026-09-22) — BrainBTreeState IS DELETED.
    //   🔒 THE REASON IS CAPABILITY, NOT BYTES (user: "i thought the reason is to allow for subtrees
    //   (multiple trees on a single entity)"). A component is addressed by its TYPE, so an entity
    //   could only ever have ONE tree cursor — which is exactly the limit that forced
    //   BTreeOrchestratorEmitCore to hand a hosted subtree the MASTER's `ref state`, the defect C1
    //   railed. A KEYED occurrence slot is what makes "multiple trees on one entity" expressible.
    //   ⭐ The cursor now lives at OccurrenceSlotKey.ComputeRootStateKey(ActiveBehaviorHash), reached
    //   through RootStateAccess — the deliberate mirror of RootParamsAccess, member for member.
    //   ⚠ Its id 31 stays RESERVED, like 23, 35 and 74: a stale recording must not bind it to a
    //   different component.
    //   📄 DESIGN_Occurrence_Scoped_Storage.md §31.5 step ②.

    // ⛔⛔⛔ O7c-① (2026-09-22) — BrainHsm64 IS DELETED.
    //   📐 It had ZERO production attach sites: BehaviorTkbTranslator.Inject is the ONE production
    //   path that gives an entity an HSM brain, and it writes BrainHsm128 unconditionally. Every
    //   `new BrainHsm64()` in the tree was in a test ⇒ HsmTickSystem<BrainHsm64> was registered,
    //   scheduled and ticked EVERY FRAME against a query that could never match.
    //   ⚠ An empty query costs no correctness, which is exactly why nothing ever found it. It cost
    //   a registration, a role-set bit, an ingress reset branch, two debug-session branches and a
    //   hot-reload sweep — all maintained for a component with no instances.
    //   ⭐⭐ THE 64-BYTE TIER IS NOT DELETED. Fhsm's HsmInstance64 is a live kernel tier that
    //   HsmInstanceManager.SelectTier can still return; what died is the ECS WRAPPER. §9.4's point
    //   exactly: the tier stops being a TYPE and becomes a PAYLOAD SIZE — and after O7c's HSM slice
    //   a 64-byte instance becomes REACHABLE for the first time, because the slot is sized from the
    //   blob instead of from a hard-coded AddComponent.
    //   📄 DESIGN_Occurrence_Scoped_Storage.md §31.5 step ①. Rail:
    //   CognitiveRuntimeModuleTests.EveryHsmTickSystem_IsRegisteredForAnAttachableComponent_O7c1.

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.BrainHsm128)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct BrainHsm128
    {
        public HsmInstance128 State;
    }
}
