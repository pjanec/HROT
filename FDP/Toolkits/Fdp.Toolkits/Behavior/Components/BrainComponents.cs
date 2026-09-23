// ⛔⛔⛔ O7c (2026-09-22 / 2026-09-23) — THIS FILE DECLARES NOTHING. It is kept as the TOMBSTONE for
//   the three root brain components, because the reasoning for each deletion is what a future reader
//   will come here looking for. ⭐ Deleting the file would leave those arguments only in git history.
//   📄 DESIGN_Occurrence_Scoped_Storage.md §31.

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

    // ⛔⛔⛔ O7c-④d (2026-09-23) — BrainHsm128 IS DELETED. THE FILE NOW DECLARES NOTHING.
    //   ⭐⭐ THE REASON IS CAPABILITY, NOT BYTES — the same argument that retired BrainBTreeState.
    //   A component is addressed by its TYPE, so the instance was permanently 128 bytes whatever
    //   HsmInstanceManager.SelectTier said about the machine: a 2-region machine wasted half of it,
    //   and an 8-region machine COULD NOT EXIST without a new component, a new GlobalComponentIds
    //   entry and a new tick registration. ⭐ The instance now lives in a keyed occurrence slot at
    //   OccurrenceSlotKey.ComputeRootHsmKey(ActiveBehaviorHash), sized by SelectTier at attach ⇒
    //   §9.4 literally: the tier stops being a TYPE and becomes a PAYLOAD SIZE, and the 64- and
    //   256-byte tiers become reachable for the first time.
    //   ⚠ Its id 36 stays RESERVED, like 23, 31, 35 and 74: a stale recording must not bind it to a
    //   different component.
    //   📄 DESIGN_Occurrence_Scoped_Storage.md §31.19. Reached through RootHsmAccess.
}
