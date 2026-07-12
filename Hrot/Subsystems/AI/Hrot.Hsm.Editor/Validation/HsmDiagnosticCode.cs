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
}
