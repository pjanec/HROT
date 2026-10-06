namespace Hrot.Hsm.Editor.Validation;

// Diagnostic codes for the HSM editor validator.
// See HSM_Editor_NodeEditor_Host_Design.md section 12.
public enum HsmDiagnosticCode
{
    // A composite state (with children) has no child marked IsInitial,
    // or more than one child marked IsInitial.
    CompositeWithoutInitialChild,

    // A composite state has more than one child marked IsInitial.
    MultipleInitialChildrenInSameParent,

    // A history pseudo-state's parent is not a composite state.
    HistoryOutsideComposite,

    // A final state (IsFinal=true) has one or more child states.
    FinalStateWithChildren,

    // A final state (IsFinal=true) has one or more outgoing transitions.
    FinalStateWithOutgoingTransition,

    // An action FQN referenced by a state or transition was not found in the registry.
    UnboundAction,

    // A guard FQN referenced by a transition was not found in the registry.
    UnboundGuard,

    // Two states in different parallel regions of the same composite write to
    // the same CommandLane via their OutputLaneMask.
    OutputLaneConflict,

    // Two sub-trees in different parallel regions of the same composite both write
    // to the same master blackboard variable (Approach A alias, Approach B sync-out,
    // or both). The writes are concurrent and non-deterministic.
    CrossRegionBlackboardConflict,

    // A state's depth in the tree exceeds 16 (kernel byte limit).
    StateDepthExceeded,

    // A parallel composite has more regions than the allowed tier count.
    RegionCountExceedsTier,

    // Static analysis found a potential infinite microstep due to a cycle
    // of same-priority transitions reachable in one RTC tick.
    TransitionPriorityCycle,

    // A transition references an event ID that is no longer present in AllEvents.
    EventReferenceDangling,

    // An action's Lane attribute changed since the last snapshot;
    // OutputLaneMask was updated automatically.
    ActionSignatureMismatch,

    // (HSM-012) A state carries a Timer action binding, but the kernel never arms a timer:
    // HsmKernelCore does not read StateDef.TimerActionId and every production write of
    // TimerDeadlines[] is zero. The binding is emitted and will never fire. The editor no
    // longer offers the field; this reports the ones already in a hand-authored asset.
    TimerActionNotImplemented,

    // After a hot reload, a reference in the asset points to a symbol
    // that no longer exists in the new assembly.
    DanglingReferenceAfterReload,

    // The same stateful Subtree asset is referenced in two or more orthogonal
    // parallel regions of the same composite. Because stateful subtrees use
    // FNV-1a(BehaviorAssetId, NodeVisualId) synthetic keys, concurrent execution
    // in two regions produces the same key for both → race-write corruption.
    // Hard-error; must be resolved before the asset can be used at runtime.
    ConcurrentStatefulSubtree,

    // (S3-6) Two stateful nodes in distinct orthogonal parallel regions of the same
    // composite resolve to the SAME Behavior/Entity shared-slot key (same scope+variable),
    // even when they live in different subtree assets. Behavior/Entity-scoped working state
    // is shared per entity, so concurrent writes from two regions race and corrupt the slot.
    // The shared-slot analogue of ConcurrentStatefulSubtree. Hard-error.
    ConcurrentSharedScopeKey,

    // (E5 item 7) This asset hosts a sub-tree that — directly or through a chain of further
    // hosts — hosts this asset again: A hosts B hosts A. Hosting is expanded INLINE by
    // HsmRunner.TickHostedChildren, so a ring has no base case and recurses until the
    // stack dies. Detected over the ASSET graph at validation time, never at runtime.
    // DESIGN_Occurrence_Scoped_Storage.md §32.16. Hard-error.
    SubtreeAssetCycle,

    // (§11.1a) A state names a hosted subtree that resolves to no BTree asset in the catalogue —
    // neither by name nor by its stored Guid. The BTree twin is Rule 6 (Subtree with
    // IsResolved == false); this is the HSM side of the same claim.
    // ⚠ Reported rather than auto-cleared: the asset may simply be absent from THIS session's
    // catalogue (an unloaded project, a partial checkout), and erasing the reference would turn a
    // recoverable situation into data loss. HSM_Editor_NodeEditor_Host_Design.md §11.1a.
    SubtreeReferenceDangling,

    // ⭐⭐⭐ (design §9 ③) A state names BOTH an activity ACTION and an activity BLUEPRINT, or a
    // transition names BOTH a guard FUNCTION and a guard BLUEPRINT.
    //
    // 🔴 WHY THIS IS AN ERROR AND NOT A WARNING. The two resolve through DIFFERENT id spaces — a
    // name through FNV1a16(FQN), a blueprint through (ushort)BlueprintId = FNV-1a32 of the asset
    // Guid — and the flattener's explicit-id override (CE-383) makes the BLUEPRINT win silently.
    // ⇒ the asset compiles, one binding is discarded with no message, and the designer watches the
    // wrong behaviour run. ⛔ Exactly the silent TryGetValue miss E6 spent a batch on.
    // 📄 DESIGN_Hsm_Blueprint_Behaviour_Authoring.md §3.2, §9 ③. Hard-error.
    MethodAndBlueprintBothBound,

    // ⭐ CE-503 (CE-434's BTree rule, shared) — a bound resolver asset whose recorded block-shape hash no longer matches
    // the behaviour's block. ⚠ A WARNING on purpose: the C# compile of the resolver against the generated block is the
    // backstop (BP1677), this is the ergonomic early notice. 📄 Q76 §12.21.
    ResolverOutOfDate,

    // ⭐ CE-2083 — a state's SOP order RUNS AS its activity (DESIGN_Decision_Layer §4.10 D3): an order AND an Activity binding
    // is one slot with two owners (the generator refuses it, HSM0001); and an order must name a behaviour. Hard-error.
    SopOrderInvalid,

    // ⭐ CE-1001 / HSM-006 — two states share a name. Emit binds a transition to its target BY NAME, so this is a
    // silently wrong machine (the builder binds whichever it resolves first). Hard-error.
    DuplicateStateName,

    // ⭐ CE-1003 (Q84 B) / HSM-002 — a declared region of a parallel state, holding states, names no initial state
    // (RegionNode.InitialChild is unset or not one of its members). Hard-error.
    RegionWithoutInitialState,
}
