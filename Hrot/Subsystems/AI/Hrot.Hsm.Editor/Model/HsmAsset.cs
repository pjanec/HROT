using System;
using System.Collections.Generic;
using System.Numerics;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared;

namespace Hrot.Hsm.Editor.Model;

// Editor-side model of an HSM asset.
// Implements IEditableAsset so the shared asset catalog can hold it.
// Mutable; tracks layout, editor-specific identity, and a reference to the kernel blob.
public sealed class HsmAsset : IEditableAsset
{
    // Identity
    public Guid AssetId { get; }
    public string Name { get; set; }
    public AssetKind Kind => AssetKind.Hsm;
    public string SourceFilePath { get; }
    public bool IsDirty { get; internal set; }
    public bool IsEditorOwned { get; }
    public string TargetNamespace { get; }

    // Kernel-side data (read-only after projection)
    public HsmDefinitionBlob Blob { get; }
    public MachineMetadata Metadata { get; }

    // Editor-side state hierarchy
    // RootState is the synthetic root (never rendered; parent of top-level states)
    public StateNode RootState { get; }

    public IReadOnlyList<StateNode> AllStates { get; }
    public IReadOnlyList<TransitionNode> AllTransitions { get; }
    public IReadOnlyList<GlobalTransitionNode> AllGlobalTransitions { get; }
    public IReadOnlyList<RegionNode> AllRegions { get; }
    public IReadOnlyList<EventDefinition> AllEvents { get; }

    // Canvas layout
    public Vector2 CanvasPanOffset { get; set; }
    public float CanvasZoomLevel { get; set; } = 1.0f;

    // Identity bridges (built once on projection; rebuilt on reload)
    private readonly Dictionary<Guid, StateNode> _stableIdToState;
    private readonly Dictionary<Guid, TransitionNode> _visualIdToTransition;
    private readonly Dictionary<Guid, RegionNode> _stableIdToRegion;
    private readonly Dictionary<ushort, StateNode> _flatIndexToState;
    private readonly Dictionary<ushort, TransitionNode> _flatIndexToTransition;
    private readonly Dictionary<ushort, EventDefinition> _eventIdToEvent;

    public event Action? Changed;

    internal HsmAsset(
        Guid assetId,
        string name,
        string sourceFilePath,
        bool isEditorOwned,
        string targetNamespace,
        HsmDefinitionBlob blob,
        MachineMetadata metadata,
        StateNode rootState,
        List<StateNode> allStates,
        List<TransitionNode> allTransitions,
        List<GlobalTransitionNode> allGlobalTransitions,
        List<RegionNode> allRegions,
        List<EventDefinition> allEvents)
    {
        AssetId = assetId;
        Name = name;
        SourceFilePath = sourceFilePath;
        IsEditorOwned = isEditorOwned;
        TargetNamespace = targetNamespace;
        Blob = blob;
        Metadata = metadata;
        RootState = rootState;
        AllStates = allStates.AsReadOnly();
        AllTransitions = allTransitions.AsReadOnly();
        AllGlobalTransitions = allGlobalTransitions.AsReadOnly();
        AllRegions = allRegions.AsReadOnly();
        AllEvents = allEvents.AsReadOnly();

        _stableIdToState = new Dictionary<Guid, StateNode>(allStates.Count);
        _visualIdToTransition = new Dictionary<Guid, TransitionNode>(allTransitions.Count);
        _stableIdToRegion = new Dictionary<Guid, RegionNode>(allRegions.Count);
        _flatIndexToState = new Dictionary<ushort, StateNode>(allStates.Count);
        _flatIndexToTransition = new Dictionary<ushort, TransitionNode>(allTransitions.Count);
        _eventIdToEvent = new Dictionary<ushort, EventDefinition>(allEvents.Count);

        foreach (var s in allStates)
        {
            _stableIdToState[s.StableId] = s;
            _flatIndexToState[s.FlatIndex] = s;
        }
        foreach (var t in allTransitions)
        {
            _visualIdToTransition[t.VisualId] = t;
            _flatIndexToTransition[t.FlatIndex] = t;
        }
        foreach (var r in allRegions)
            _stableIdToRegion[r.StableId] = r;
        foreach (var e in allEvents)
            _eventIdToEvent[e.EventId] = e;
    }

    // Identity bridge lookups
    public StateNode? FindStateByStableId(Guid stableId) =>
        _stableIdToState.GetValueOrDefault(stableId);

    public TransitionNode? FindTransitionByVisualId(Guid visualId) =>
        _visualIdToTransition.GetValueOrDefault(visualId);

    public RegionNode? FindRegionByStableId(Guid stableId) =>
        _stableIdToRegion.GetValueOrDefault(stableId);

    public StateNode? FindStateByFlatIndex(ushort flatIndex) =>
        _flatIndexToState.GetValueOrDefault(flatIndex);

    public TransitionNode? FindTransitionByFlatIndex(ushort flatIndex) =>
        _flatIndexToTransition.GetValueOrDefault(flatIndex);

    public EventDefinition? FindEventById(ushort eventId) =>
        _eventIdToEvent.GetValueOrDefault(eventId);

    internal void MarkDirty()
    {
        IsDirty = true;
        Changed?.Invoke();
    }
}

// Editor-side representation of a single state.
// Augments the kernel-side StateDef with editor-only fields (layout, comments, etc.).
public sealed class StateNode
{
    // Primary editor identity (stable across hot reloads if layout method is present)
    public Guid StableId;
    // Re-derived on each reload; index into HsmDefinitionBlob.States
    public ushort FlatIndex;

    public string Name;
    public StateNode? Parent;
    public List<StateNode> Children { get; } = new();
    public List<TransitionNode> OutgoingTransitions { get; } = new();
    public List<RegionNode> Regions { get; } = new();

    // State configuration (from StateDef.Flags)
    public bool IsInitial;
    public bool IsHistory;
    public bool IsDeepHistory;
    public bool IsParallel;
    public bool IsFinal;

    // Action names (resolved from MachineMetadata; null means no action)
    public string? OnEntryAction;
    public string? OnExitAction;
    public string? ActivityAction;
    public string? TimerAction;

    // Inferred from action declarations; read-only in the editor
    public byte OutputLaneMask;

    // Event IDs deferred while in this state (to be populated from blob deferred-event table
    // in a later task; empty for now)
    public List<ushort> DeferredEventIds { get; } = new();

    // Editor-only (persisted in layout method)
    public Vector2 Position;
    public Vector2? Size;
    public string? Comment;
    public bool IsCollapsed;
    public string? ColorOverride;

    // Editor-only ephemeral (not persisted)
    public bool IsBreakpoint;

    // Hidden pin IDs used by HsmTransitionLink to connect this state in the node graph.
    // Derived deterministically from StableId so they are stable across reloads.
    // Output pin = source side of a transition FROM this state.
    // Input pin  = target side of a transition TO this state.
    public Guid HiddenOutputPinId => DeriveOutputPinId(StableId);
    public Guid HiddenInputPinId  => DeriveInputPinId(StableId);

    public StateNode(string name)
    {
        Name = name;
        StableId = Guid.NewGuid();  // replaced by projector if layout provides one
    }

    // Derives a deterministic output pin GUID from a state's StableId.
    // Flips bit 0 of the last byte to ensure distinct from input pin and state ID.
    internal static Guid DeriveOutputPinId(Guid stableId)
    {
        var bytes = stableId.ToByteArray();
        bytes[15] = (byte)(bytes[15] ^ 0x01);
        return new Guid(bytes);
    }

    // Derives a deterministic input pin GUID from a state's StableId.
    // Flips bit 1 of the last byte to ensure distinct from output pin and state ID.
    internal static Guid DeriveInputPinId(Guid stableId)
    {
        var bytes = stableId.ToByteArray();
        bytes[15] = (byte)(bytes[15] ^ 0x02);
        return new Guid(bytes);
    }
}

// Editor-side representation of a transition between two states.
public sealed class TransitionNode
{
    // Primary editor identity (stable if layout method is present)
    public Guid VisualId;
    // Re-derived on each reload; index into HsmDefinitionBlob.Transitions
    public ushort FlatIndex;

    public StateNode Source = null!;
    public StateNode Target = null!;
    public ushort EventId;
    public string? EventName;    // symbolicated from MachineMetadata; for display
    public string? GuardFunction;
    public string? ActionFunction;
    public byte Priority;
    public TransitionKind Kind;
    public ushort SyncGroupId;

    // Editor-only (persisted in layout method)
    public List<Vector2> Waypoints { get; } = new();
    public string? Comment;
    public bool IsBreakpoint;
}

public enum TransitionKind { External, Internal, Local }

// Editor-side representation of a global (unconditional-source) transition.
public sealed class GlobalTransitionNode
{
    public Guid VisualId;
    public ushort FlatIndex;
    public StateNode Target = null!;
    public ushort EventId;
    public string? EventName;
    public string? GuardFunction;
    public string? ActionFunction;
    public byte Priority;
    public string? Comment;
    public bool IsBreakpoint;
}

// Editor-side representation of an orthogonal region within a parallel state.
public sealed class RegionNode
{
    // Separate identity from the parent state
    public Guid StableId;
    // 0..(RegionCount-1) within parent state
    public byte RegionIndex;
    // Editor-only label; not stored in the kernel
    public string Name;
    public byte Priority;
    public StateNode? InitialChild;

    // Editor-only
    public string? Comment;
    public string? ColorOverride;

    public RegionNode(string name)
    {
        Name = name;
        StableId = Guid.NewGuid();
    }
}

// Editor-side representation of an event declared in the state machine.
public sealed class EventDefinition
{
    public ushort EventId;
    public string Name;
    public int PayloadSize;
    public bool IsIndirect;
    public bool IsDeferrable;     // whether some state defers this event
    public bool HasGlobalTransition;  // derived from the GlobalTransitions list

    public EventDefinition(string name, ushort eventId)
    {
        Name = name;
        EventId = eventId;
    }
}
