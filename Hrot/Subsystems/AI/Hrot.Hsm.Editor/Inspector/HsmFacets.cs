using System.Collections.Generic;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Hsm.Editor.Model;
using StructEdit.Core.Attributes;

namespace Hrot.Hsm.Editor.Inspector;

// Inspector facet struct for a StateNode. Shown when a state is selected.
public struct StateFacet
{
    [EditDisplayName("Name")]
    public string Name;

    [EditDisplayName("On Entry action")]
    [HsmActionPicker]
    public string? OnEntryAction;

    [EditDisplayName("On Exit action")]
    [HsmActionPicker]
    public string? OnExitAction;

    [EditDisplayName("Activity (tick) action")]
    [HsmActionPicker]
    public string? ActivityAction;

    // ⭐⭐⭐ CE-385 — the activity hosted by a BLUEPRINT instead of a C# method.
    // 📄 DESIGN_Hsm_Blueprint_Behaviour_Authoring.md §3.2, §7.
    // ⛔⛔ MUTUALLY EXCLUSIVE with ActivityAction above — the validator rejects an asset that sets
    //    both (design §9 ③), because the two resolve through DIFFERENT id spaces and one would
    //    silently win. ⚠ Deliberately shown side by side so the choice is visible.
    [EditDisplayName("Activity blueprint (instead of an action)")]
    [AiAssetPicker(AssetKind.Blueprint)]
    public string? ActivityBlueprintName;

    /// <summary>⭐ The persisted RENAME SURVIVOR and the id the emitter actually bakes. ⛔ Never
    /// typed — written by the pick. Shown so a dangling reference is diagnosable.</summary>
    [EditReadOnly]
    [EditDisplayName("Activity blueprint asset id")]
    public string ActivityBlueprintAssetId;

    [EditDisplayName("Timer action")]
    [HsmActionPicker]
    public string? TimerAction;

    /// <summary>
    /// ⭐⭐⭐ <c>CE-387</c> — which blackboard variable THIS STATE's hosted occurrence seeds its
    /// params from. 📄 design §3.4; <c>DESIGN_Occurrence_Scoped_Storage.md</c> §28.6.
    /// ⛔⛔ <b>Same field name as <see cref="TransitionFacet.ExpressionTargetField"/>, opposite
    /// direction:</b> a transition's RECEIVES its action's result; a state's is the SEED it reads.
    /// ⚠ Unbound is the COMMON case and means "seed from offset 0", not an error.
    /// </summary>
    [EditDisplayName("Params seed (blackboard variable)")]
    [HsmBlackboardFieldPicker]
    public string? ExpressionTargetField;

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

    [EditDisplayName(ReactiveGuardVocabulary.HsmTransitionGuardDisplayName)]
    [HsmGuardPicker]
    public string? GuardFunction;

    // ⭐⭐⭐ CE-385 — the guard hosted by a BLUEPRINT. ⛔⛔ MUTUALLY EXCLUSIVE with GuardFunction
    //    (design §9 ③). 📄 §3.2, §7.
    [EditDisplayName("Guard blueprint (instead of a guard function)")]
    [AiAssetPicker(AssetKind.Blueprint)]
    public string? GuardBlueprintName;

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
    [HsmActionPicker]
    public string? ActionFunction;

    [EditDisplayName("Expression target (blackboard field)")]
    [HsmBlackboardFieldPicker]
    public string? ExpressionTargetField;

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

    [EditDisplayName(ReactiveGuardVocabulary.HsmTransitionGuardDisplayName)]
    [HsmGuardPicker]
    public string? GuardFunction;

    [EditDisplayName("Effect action")]
    [HsmActionPicker]
    public string? ActionFunction;

    [EditDisplayName("Expression target (blackboard field)")]
    [HsmBlackboardFieldPicker]
    public string? ExpressionTargetField;

    [EditDisplayName("Priority")]
    [EditRange(0, 255)]
    public byte Priority;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}
