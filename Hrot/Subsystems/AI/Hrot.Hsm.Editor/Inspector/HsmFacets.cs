using System.Collections.Generic;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared;
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

    [EditDisplayName("Timer action")]
    [HsmActionPicker]
    public string? TimerAction;

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

    [EditDisplayName("Effect action")]
    [HsmActionPicker]
    public string? ActionFunction;

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

    [EditDisplayName("Priority")]
    [EditRange(0, 255)]
    public byte Priority;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}
