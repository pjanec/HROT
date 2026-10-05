using System.Collections.Generic;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Inspector.ActionBinding;
using Hrot.Hsm.Editor.Model;
using StructEdit.Core.Attributes;

namespace Hrot.Hsm.Editor.Inspector;

// Inspector facet struct for a StateNode. Shown when a state is selected.
public struct StateFacet
{
    [EditDisplayName("Name")]
    public string Name;

    // ⭐⭐⭐ CE-417 slice 4b — ONE binding facet per slot, each with its OWN target variable (B-2), drawn by the ONE
    //   ActionBindingDrawer. 📄 DESIGN_Behavior_Action_Binding.md §5.4.
    // ⛔ Replaces the four flat *Action fields, the activity blueprint name/id pair and the ONE state-wide
    //   "Params seed" field. The state's seed (what a hosted occurrence reads) is still DERIVED from the slots by
    //   StateNode.StateWideTargetField — the Activity's variable first — so the emitter and the editor cannot disagree.
    // ⚠ Only the Activity may run a BLUEPRINT (CE-385, design §9 ③: method XOR blueprint).
    [EditDisplayName("On Entry")]
    [ActionBinding(BindingSlotKind.Action)]
    public BehaviorActionBindingFacet OnEntry;

    [EditDisplayName("On Exit")]
    [ActionBinding(BindingSlotKind.Action)]
    public BehaviorActionBindingFacet OnExit;

    [EditDisplayName("Activity (tick)")]
    [ActionBinding(BindingSlotKind.Action, allowsBlueprint: true)]
    public BehaviorActionBindingFacet Activity;

    /// <summary>⭐ The persisted RENAME SURVIVOR of the activity blueprint and the id the emitter bakes. ⛔ Never typed —
    /// written by the pick. Shown so a dangling reference is diagnosable.</summary>
    [EditReadOnly]
    [EditDisplayName("Activity blueprint asset id")]
    public string ActivityBlueprintAssetId;

    [EditDisplayName("Timer")]
    [ActionBinding(BindingSlotKind.Action)]
    public BehaviorActionBindingFacet Timer;

    // ⭐⭐⭐ HSM SUBTREE AUTHORING — 📄 HSM_Editor_NodeEditor_Host_Design.md §11.1a.
    // 🔒 User, 2026-09-26: "the tree asset must be pickable."
    // 🔴 Until this field existed, E5's whole runtime for "an HSM state hosts a BTree" was
    //    UNREACHABLE on a real asset: nothing outside the mapper could write SubtreeAssetId /
    //    SubtreeName, so validator rules 8/8b/10 could never fire.
    // ⭐ ONE editable field; the Guid and the resolved flag are DERIVED and shown read-only —
    //   deliberately the same shape as BTreeSubtreeFacet, whose pair is already read-only.
    [EditDisplayName("Hosted subtree (BTree asset)")]
    [AiAssetPicker(AssetKind.BTree)]
    public string? SubtreeName;

    /// <summary>⭐ The persisted RENAME SURVIVOR. ⛔ Never typed — written by the pick, healed by
    /// <c>HsmSubtreeResolver</c>. Shown so a dangling reference is diagnosable.</summary>
    [EditReadOnly]
    [EditDisplayName("Hosted subtree asset id")]
    public string SubtreeAssetId;

    /// <summary>⚠ DERIVED, never persisted — recomputed against the catalogue on load/hot-reload.</summary>
    [EditReadOnly]
    [EditDisplayName("Subtree resolves")]
    public bool IsSubtreeResolved;

    // ⭐ CE-2083 — an SOP order this state issues; it RUNS AS the activity, so leave "Activity (tick)" empty when one is set.
    //   📄 DESIGN_Decision_Layer.md §4.10. The behaviour is picked (every registered behaviour, any tier); picking one composes
    //   a variable of its params type (edited in the blackboard); none ⇒ the behaviour's authored defaults.
    [EditDisplayName("SOP order")]
    public HsmSopOrderKind SopOrder;

    [EditDisplayName("SOP behaviour")]
    [Hrot.Editor.AiShared.Inspector.AiBehaviorPicker]
    public string? SopBehavior;

    [EditReadOnly]
    [EditDisplayName("SOP params variable")]
    public string SopParamsVariable;

    [EditDisplayName("SOP urgency (React)")]
    public Hrot.AiEditor.Persistence.BTree.SopUrgencyDto SopUrgency;

    public StateFlags Flags;

    [EditDisplayName("Deferred events")]
    [HsmEventPicker]
    public List<ushort> DeferredEventIds;

    [EditReadOnly]
    [EditDisplayName("Output lanes (inferred)")]
    public string OutputLanesSummary;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string StableId;

    [EditReadOnly]
    public int IncomingTransitionCount;

    [EditReadOnly]
    public int OutgoingTransitionCount;
}

/// <summary>⭐ <c>CE-2083</c> — whether a state issues an SOP order, and which.</summary>
public enum HsmSopOrderKind
{
    None = 0,
    DoWhenIdle = 1,
    React = 2,
}

// Inspector facet struct for a TransitionNode. Shown when a transition is selected.
public struct TransitionFacet
{
    [EditDisplayName("Source state")]
    [EditReadOnly]
    public string SourceStateName;

    [EditDisplayName("Target state")]
    [HsmStateSelector]
    public string TargetStateName;

    [EditDisplayName("Event")]
    [HsmEventPicker]
    public ushort EventId;

    // ⭐⭐⭐ CE-417 slice 4b — the guard and the effect action are two bindings, each with its OWN variable (B-2): the
    //   guard's is the INPUT it reads, the action's the OUTPUT it writes. ⛔ Before 4b one field served both.
    // ⚠ Only the guard may run a BLUEPRINT (CE-385; method XOR blueprint, design §9 ③).
    [EditDisplayName(ReactiveGuardVocabulary.HsmTransitionGuardDisplayName)]
    [ActionBinding(BindingSlotKind.Guard, allowsBlueprint: true)]
    public BehaviorActionBindingFacet Guard;

    /// <summary>⭐ The persisted RENAME SURVIVOR and the id the emitter bakes. ⛔ Never typed.</summary>
    [EditReadOnly]
    [EditDisplayName("Guard blueprint asset id")]
    public string GuardBlueprintAssetId;

    /// <summary>
    /// ⭐⭐⭐ <c>CE-381</c> — evaluate this transition's guard on every QUIESCENT tick, with no
    /// event posted. ⛔ Not "a transition with no event": that one is the RTC loop's COMPLETION
    /// pass and fires once. 📄 §2.3, §3.1.
    /// </summary>
    [EditDisplayName("Polled (guard runs every idle tick)")]
    public bool IsPolled;

    [EditDisplayName("Effect action")]
    [ActionBinding(BindingSlotKind.Action)]
    public BehaviorActionBindingFacet Action;

    [EditDisplayName("Priority")]
    [EditRange(0, 255)]
    public byte Priority;

    public TransitionKind Kind;

    [EditDisplayName("Sync group")]
    [HsmSyncGroupPicker]
    public ushort SyncGroupId;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;

    [EditReadOnly]
    [EditDisplayName("LCA (least common ancestor)")]
    public string LcaStateName;

    [EditReadOnly]
    [EditDisplayName("LCA cost")]
    public ushort LcaCost;
}

// Inspector facet struct for a RegionNode. Shown when a region is selected.
public struct RegionFacet
{
    [EditDisplayName("Region name")]
    public string Name;

    [EditDisplayName("Priority")]
    [EditRange(0, 255)]
    public byte Priority;

    [EditDisplayName("Initial child")]
    [HsmStateSelector]
    public string? InitialChildName;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Color override")]
    public string? ColorOverride;

    [EditReadOnly]
    public string StableId;
}

// Inspector facet struct for an EventDefinition. Shown when an event row is selected.
public struct EventFacet
{
    [EditDisplayName("Event name")]
    public string Name;

    [EditReadOnly]
    public ushort EventId;

    [EditDisplayName("Payload size (bytes)")]
    public int PayloadSize;

    public bool IsIndirect;

    [EditDisplayName("Priority class")]
    public EventPriority Priority;

    [EditReadOnly]
    [EditDisplayName("Deferred by")]
    public string DeferredByStatesSummary;

    [EditReadOnly]
    [EditDisplayName("Used in transitions")]
    public int TransitionReferenceCount;

    [EditReadOnly]
    [EditDisplayName("Global transition")]
    public string? GlobalTransitionTarget;
}

// Inspector facet struct for a GlobalTransitionNode. Shown when a global is selected.
public struct GlobalTransitionFacet
{
    [EditDisplayName("Event")]
    [HsmEventPicker]
    public ushort EventId;

    [EditDisplayName("Target state")]
    [HsmStateSelector]
    public string TargetStateName;

    // ⭐ CE-417 slice 4b — the same two bindings as a transition (B-3).
    [EditDisplayName(ReactiveGuardVocabulary.HsmTransitionGuardDisplayName)]
    [ActionBinding(BindingSlotKind.Guard)]
    public BehaviorActionBindingFacet Guard;

    [EditDisplayName("Effect action")]
    [ActionBinding(BindingSlotKind.Action)]
    public BehaviorActionBindingFacet Action;

    [EditDisplayName("Priority")]
    [EditRange(0, 255)]
    public byte Priority;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}
