using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Hsm.Editor.Host;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Model;

// Editor-side model of an HSM asset.
// Implements IEditableAsset so the shared asset catalog can hold it.
// Mutable; tracks layout, editor-specific identity, and a reference to the kernel blob.
public sealed class HsmAsset : IEditableAsset, IBlackboardManagedAsset, IStitchableAsset
{
    // Identity
    public Guid AssetId { get; }
    public string Name { get; set; }
    public AssetKind Kind => AssetKind.Hsm;
    public string SourceFilePath { get; }
    public bool IsDirty { get; internal set; }
    public bool IsEditorOwned { get; }
    public string TargetNamespace { get; }

    /// <summary>
    /// Name of the blackboard struct type generated for this HSM.
    /// Defaults to <c>&lt;SanitizedName&gt;_Blackboard</c> on construction.
    /// Can be overridden by the editor when the user renames the asset.
    /// </summary>
    public string BlackboardTypeName { get; set; }

    // Kernel-side data (mutable via UpdateBlob for PU-302 stitch; read-only otherwise)
    private HsmDefinitionBlob _blob;
    private MachineMetadata _metadata;

    public HsmDefinitionBlob Blob => _blob;
    public MachineMetadata Metadata => _metadata;

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

    // ---- IBlackboardManagedAsset ----
    private readonly List<BlackboardVariableEntry> _blackboardVariables = new();
    private readonly Dictionary<string, List<BlackboardAliasBinding>> _aliases = new();
    // Variables explicitly permitted for concurrent writes from parallel regions (TASK-BB-1f-02).
    // Session-only: persistence to the layout method is deferred to TASK-BB-1f-05.
    private readonly HashSet<(string VariableName, string WriterPairKey)> _conflictSuppressions = new();
    private readonly HashSet<string> _unusedSuppressions = new();
    public bool IsBlackboardEditorManaged { get; set; }
    public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => _blackboardVariables;

    /// <summary>Load-time health of the companion blackboard file. Defaults to Clean.</summary>
    public BlackboardLoadState LoadState { get; private set; }

    /// <summary>Diagnostic message when LoadState is non-Clean; null otherwise.</summary>
    public string? LoadDiagnosticMessage { get; private set; }

    /// <summary>Sets the load diagnostic. Called by the projector after parsing the companion file.</summary>
    internal void SetLoadDiagnostic(BlackboardLoadState state, string? message)
    {
        LoadState = state;
        LoadDiagnosticMessage = message;
    }

    /// <summary>
    /// Replaces the current variable list and fires Changed.
    /// Call this when the editor commits an updated set of variables.
    /// </summary>
    public void SetBlackboardVariables(IEnumerable<BlackboardVariableEntry> vars)
    {
        _blackboardVariables.Clear();
        _blackboardVariables.AddRange(vars);
        MarkDirty();
    }

    /// <summary>Appends a new variable at the end of the canonical order. Fires Changed.</summary>
    public void AddVariable(BlackboardVariableEntry entry)
    {
        _blackboardVariables.Add(entry);
        MarkDirty();
    }

    /// <summary>Removes the variable with the given name. No-op if not found. Fires Changed.</summary>
    public void RemoveVariable(string name)
    {
        int idx = _blackboardVariables.FindIndex(v => v.Name == name);
        if (idx < 0) return;
        _blackboardVariables.RemoveAt(idx);
        _aliases.Remove(name);
        MarkDirty();
    }

    /// <summary>
    /// Removes all variables whose names appear in <paramref name="names"/>.
    /// Fires Changed exactly once if any variables were removed; no-op (no Changed) when none match.
    /// </summary>
    public void RemoveVariables(IReadOnlyList<string> names)
    {
        if (names.Count == 0) return;
        bool removed = false;
        foreach (var name in names)
        {
            int idx = _blackboardVariables.FindIndex(v => v.Name == name);
            if (idx < 0) continue;
            _blackboardVariables.RemoveAt(idx);
            _aliases.Remove(name);
            removed = true;
        }
        if (removed) MarkDirty();
    }

    /// <summary>Replaces the comment on an existing variable. No-op if not found. Fires Changed.</summary>
    public void UpdateVariableComment(string name, string? comment)
    {
        int idx = _blackboardVariables.FindIndex(v => v.Name == name);
        if (idx < 0) return;
        _blackboardVariables[idx] = _blackboardVariables[idx] with { Comment = comment };
        MarkDirty();
    }

    /// <summary>Moves a variable from sourceIndex to destIndex in canonical order. Fires Changed.</summary>
    public void MoveVariable(int sourceIndex, int destIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= _blackboardVariables.Count) return;
        if (destIndex   < 0 || destIndex   >= _blackboardVariables.Count) return;
        if (sourceIndex == destIndex) return;
        var entry = _blackboardVariables[sourceIndex];
        _blackboardVariables.RemoveAt(sourceIndex);
        _blackboardVariables.Insert(destIndex, entry);
        MarkDirty();
    }

    /// <summary>Renames a variable. No-op if not found. Fires Changed.</summary>
    public void RenameVariable(string oldName, string newName)
    {
        int idx = _blackboardVariables.FindIndex(v => v.Name == oldName);
        if (idx < 0) return;
        _blackboardVariables[idx] = _blackboardVariables[idx] with { Name = newName };
        if (_aliases.TryGetValue(oldName, out var list))
        {
            _aliases.Remove(oldName);
            _aliases[newName] = list;
        }
        MarkDirty();
    }

    /// <summary>Returns 0; HSM does not use ExpressionTargetField in this phase.</summary>
    public int CountNodesReferencingVariable(string name) => 0;

    /// <summary>Returns all alias bindings recorded against the named variable. Empty list if none.</summary>
    public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName) =>
        _aliases.TryGetValue(variableName, out var list)
            ? list.AsReadOnly()
            : Array.Empty<BlackboardAliasBinding>();

    /// <summary>Binds an unbound sub-tree requirement to a defined variable. Fires Changed.</summary>
    public void AddAlias(string variableName, BlackboardAliasBinding binding)
    {
        if (!_aliases.TryGetValue(variableName, out var list))
        {
            list = new List<BlackboardAliasBinding>();
            _aliases[variableName] = list;
        }
        // Prevent duplicate by (RequiringAssetId, RequiringElementId) pair.
        if (list.Exists(a => a.RequiringAssetId == binding.RequiringAssetId
                          && a.RequiringElementId == binding.RequiringElementId))
            return;
        list.Add(binding);
        MarkDirty();
    }

    /// <summary>
    /// Removes an alias binding from the named variable. No-op if not found. Fires Changed.
    /// </summary>
    public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId)
    {
        if (!_aliases.TryGetValue(variableName, out var list)) return;
        int idx = list.FindIndex(a => a.RequiringAssetId == requiringAssetId
                                   && a.RequiringElementId == requiringElementId);
        if (idx < 0) return;
        list.RemoveAt(idx);
        MarkDirty();
    }

    /// <summary>
    /// Removes alias bindings whose RequiringAssetId is not present in knownAssetIds.
    /// Fires Changed once if any bindings were removed.
    /// </summary>
    public void PruneStaleAliasBindings(IReadOnlyCollection<Guid> knownAssetIds)
    {
        bool removed = false;
        foreach (var varName in new List<string>(_aliases.Keys))
        {
            var list = _aliases[varName];
            int before = list.Count;
            list.RemoveAll(a => !knownAssetIds.Contains(a.RequiringAssetId));
            if (list.Count < before) removed = true;
            if (list.Count == 0) _aliases.Remove(varName);
        }
        if (removed) MarkDirty();
    }

    /// <summary>
    /// Returns the set of all distinct RequiringAssetId GUIDs currently referenced
    /// across all alias binding lists.
    /// </summary>
    public IReadOnlyCollection<Guid> GetKnownSubAssetIds()
    {
        var ids = new HashSet<Guid>();
        foreach (var list in _aliases.Values)
            foreach (var a in list)
                ids.Add(a.RequiringAssetId);
        return ids;
    }

    /// <summary>
    /// Returns true if the variable conflict is suppressed for the given writer pair.
    /// </summary>
    public bool IsConflictSuppressed(string variableName, string writerPairKey) =>
        _conflictSuppressions.Contains((variableName, writerPairKey));

    /// <summary>
    /// </summary>
    public void SetConflictSuppressed(string variableName, string writerPairKey, bool suppressed)
    {
        bool wasSuppressed = _conflictSuppressions.Contains((variableName, writerPairKey));
        if (wasSuppressed == suppressed) return;
        if (suppressed) _conflictSuppressions.Add((variableName, writerPairKey));
        else            _conflictSuppressions.Remove((variableName, writerPairKey));
        MarkDirty();
    }

    public bool IsUnusedWarningSuppressed(string variableName) =>
        _unusedSuppressions.Contains(variableName);

    public IEnumerable<(string VariableName, string WriterPairKey)> GetConflictSuppressions() => _conflictSuppressions;
    public IEnumerable<string> GetUnusedSuppressions() => _unusedSuppressions;

    public void SetUnusedWarningSuppressed(string variableName, bool suppressed)
    {
        bool wasSuppressed = _unusedSuppressions.Contains(variableName);
        if (wasSuppressed == suppressed) return;
        if (suppressed) _unusedSuppressions.Add(variableName);
        else            _unusedSuppressions.Remove(variableName);
        MarkDirty();
    }

    /// <summary>
    /// Returns a map of StateId -> RegionIndex for all states that are direct children
    /// of a parallel composite, enabling the shared window to check cross-region conflicts
    /// without a circular project reference.
    /// </summary>
    public IReadOnlyDictionary<Guid, int>? GetParallelRegionMap()
    {
        var map = new Dictionary<Guid, int>();
        foreach (var s in AllStates)
        {
            if (s.Parent?.IsParallel == true)
                map[s.StableId] = s.RegionIndex;
        }
        return map.Count > 0 ? map : null;
    }

    // Identity bridges (built once on projection; rebuilt on reload)
    private readonly Dictionary<Guid, StateNode> _stableIdToState;
    private readonly Dictionary<Guid, TransitionNode> _visualIdToTransition;
    private readonly Dictionary<Guid, RegionNode> _stableIdToRegion;
    private readonly Dictionary<ushort, StateNode> _flatIndexToState;
    private readonly Dictionary<ushort, TransitionNode> _flatIndexToTransition;
    private readonly Dictionary<ushort, EventDefinition> _eventIdToEvent;

    // Mutable backing lists for regions, global transitions, and attachments.
    private readonly List<RegionNode>           _allRegionsList;
    private readonly List<GlobalTransitionNode> _allGlobalTransitionsList;
    private readonly Dictionary<AttachmentId, HsmAttachment> _attachments = new();

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
        BlackboardTypeName = SanitizeIdentifier(name) + "_Blackboard";
        _blob = blob;
        _metadata = metadata;
        RootState = rootState;
        AllStates = allStates.AsReadOnly();
        AllTransitions = allTransitions.AsReadOnly();
        AllGlobalTransitions = allGlobalTransitions.AsReadOnly();
        AllRegions = allRegions.AsReadOnly();
        AllEvents = allEvents.AsReadOnly();
        _allRegionsList           = allRegions;
        _allGlobalTransitionsList = allGlobalTransitions;

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

    // ── PU-302: Post-reload stitch ───────────────────────────────────────────

    /// <summary>
    /// Replaces the runtime blob and metadata references after a hot reload.
    /// Used by <see cref="StitchKernelIndices"/> to point the asset at the freshly
    /// recompiled blob without replacing the JSON-authoritative topology/layout.
    /// Must NOT call <see cref="MarkDirty"/> (PU-602 constraint).
    /// </summary>
    internal void UpdateBlob(HsmDefinitionBlob blob, MachineMetadata metadata)
    {
        _blob     = blob;
        _metadata = metadata;
    }

    /// <summary>
    /// Stitches runtime indices from a freshly assembly-projected asset onto this
    /// JSON-loaded editor model (design §6.6 / D13).
    /// <para>
    /// For each <see cref="StateNode"/> in <see cref="AllStates"/>, the matching state
    /// in <paramref name="fresh"/> is found by <c>StableId</c> via
    /// <see cref="MachineMetadata.StateStableIds"/> and its <c>FlatIndex</c> is copied
    /// across.  For each <see cref="TransitionNode"/>, the match is by <c>VisualId</c>
    /// via <see cref="MachineMetadata.TransitionVisualIds"/>.
    /// </para>
    /// <para>
    /// Unmatched nodes remain visible with <c>FlatIndex = 0</c> (sentinel) and a
    /// <see cref="BlackboardLoadState.Warning"/> diagnostic is set on the asset.
    /// </para>
    /// <para>
    /// <b>Must NOT call <see cref="MarkDirty"/></b> (PU-602 constraint).
    /// </para>
    /// </summary>
    public void StitchKernelIndices(HsmAsset? fresh)
    {
        if (fresh is null || fresh.AssetId != AssetId)
        {
            SetLoadDiagnostic(BlackboardLoadState.AssemblyFailed,
                "Assembly blob unavailable; runtime indices unset (debug overlay inert).");
            return;
        }

        var freshMeta = fresh.Metadata;

        // Build reverse maps: StableId → FlatIndex, VisualId string → FlatIndex
        var stableIdToFlatIndex    = new Dictionary<Guid, ushort>(freshMeta.StateStableIds.Count);
        var visualIdToTransFlat    = new Dictionary<Guid, ushort>(freshMeta.TransitionVisualIds.Count);

        foreach (var kv in freshMeta.StateStableIds)
            stableIdToFlatIndex[kv.Value] = kv.Key;

        foreach (var kv in freshMeta.TransitionVisualIds)
            visualIdToTransFlat[kv.Value] = kv.Key;

        bool anyUnmatched = false;

        foreach (var state in AllStates)
        {
            if (stableIdToFlatIndex.TryGetValue(state.StableId, out var flatIdx))
            {
                state.FlatIndex = flatIdx;
            }
            else
            {
                state.FlatIndex = 0; // sentinel
                anyUnmatched = true;
            }
        }

        foreach (var transition in AllTransitions)
        {
            if (visualIdToTransFlat.TryGetValue(transition.VisualId, out var flatIdx))
            {
                transition.FlatIndex = flatIdx;
            }
            else
            {
                transition.FlatIndex = 0; // sentinel
                anyUnmatched = true;
            }
        }

        // Update blob/metadata references to the recompiled versions
        UpdateBlob(fresh.Blob, fresh.Metadata);

        if (anyUnmatched)
        {
            SetLoadDiagnostic(BlackboardLoadState.StructParseFailed,
                "One or more states/transitions have no blob match; debug overlay partially inert.");
        }
        else
        {
            SetLoadDiagnostic(BlackboardLoadState.Clean, null);
        }
        // Do NOT call MarkDirty — stitch is a reload-only operation (PU-602 constraint).
    }

    // ---- IStitchableAsset ----

    /// <inheritdoc/>
    void IStitchableAsset.StitchRuntimeIndices(IEditableAsset? fresh)
        => StitchKernelIndices(fresh as HsmAsset);

    internal void MarkDirty()
    {
        IsDirty = true;
        Changed?.Invoke();
    }

    /// <summary>
    /// Clears the in-memory dirty flag after a successful save/emit.
    /// Called by the <c>RegenerationScheduler</c> flush action in <c>EditorSubsystem</c>.
    /// </summary>
    public void ClearDirty() => IsDirty = false;

    // Converts a name into a valid C# identifier (strips non-alphanumeric chars,
    // prepends '_' when the first char is a digit, falls back to "HsmAsset").
    private static string SanitizeIdentifier(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in name)
            if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
        if (sb.Length == 0) return "HsmAsset";
        if (char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }

    // ---- Region mutation helpers (called by HsmCommandSink) ----

    // Registers a new region in the asset-level lookup dictionary and backing list.
    // The caller is responsible for inserting the region into the parent StateNode.RegionNodes.
    internal void RegisterRegion(RegionNode region)
    {
        _allRegionsList.Add(region);
        _stableIdToRegion[region.StableId] = region;
    }

    // Unregisters a region from the asset-level lookup dictionary and backing list.
    // The caller is responsible for removing the region from the parent StateNode.RegionNodes.
    internal void UnregisterRegion(RegionNode region)
    {
        _allRegionsList.Remove(region);
        _stableIdToRegion.Remove(region.StableId);
    }

    // ---- Global transition mutation helpers ----

    /// <summary>Removes the global transition with the given VisualId, if it exists.</summary>
    internal bool RemoveGlobalTransition(Guid visualId)
    {
        int idx = _allGlobalTransitionsList.FindIndex(g => g.VisualId == visualId);
        if (idx < 0) return false;
        _allGlobalTransitionsList.RemoveAt(idx);
        return true;
    }

    // ---- Attachment mutation helpers (called by HsmCommandSink) ----

    internal void AddAttachment(HsmAttachment attachment)
    {
        _attachments[attachment.Id] = attachment;
    }

    internal void RemoveAttachments(IReadOnlyList<AttachmentId> ids)
    {
        foreach (var id in ids)
            _attachments.Remove(id);
    }

    internal HsmAttachment? FindAttachmentById(AttachmentId id) =>
        _attachments.GetValueOrDefault(id);

    internal IEnumerable<HsmAttachment> AllAttachments => _attachments.Values;

    internal IReadOnlyList<HsmAttachment> GetAttachmentsForNode(NodeId hostId)
    {
        var result = new List<HsmAttachment>();
        foreach (var att in _attachments.Values)
            if (att.HostNodeId == hostId) result.Add(att);
        return result;
    }
}

// Editor-side representation of a single state.
// Augments the kernel-side StateDef with editor-only fields (layout, comments, etc.).
public sealed class StateNode : IContainerNodeModel
{
    // Primary editor identity (stable across hot reloads if layout method is present)
    public Guid StableId;
    // Re-derived on each reload; index into HsmDefinitionBlob.States
    public ushort FlatIndex;

    public string Name;
    public StateNode? Parent;
    public List<StateNode> Children { get; } = new();
    public List<TransitionNode> OutgoingTransitions { get; } = new();
    public List<RegionNode> RegionNodes { get; } = new();

    // State configuration (from StateDef.Flags)
    public bool IsInitial;
    public bool IsHistory;
    public bool IsDeepHistory;
    public bool IsParallel;
    public bool IsFinal;

    // True when this state is a pseudo-state (History, Deep-History, or Final).
    // Pseudo-states are rendered exclusively via HsmHistoryGlyphsRenderer;
    // the node body background is drawn transparent.
    public bool IsPseudostate => IsHistory || IsDeepHistory || IsFinal;

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

    // Zero-based index of the orthogonal region this state belongs to within its parent parallel composite.
    // 0 for states that are not children of a parallel state.
    public int RegionIndex;

    // Editor-only (persisted in layout method)
    public Vector2 Position { get; set; }
    public Vector2? SizeOverride { get; set; }
    public string? Comment;
    public bool IsCollapsed { get; set; }
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

    // ---- INodeModel ----

    public NodeId Id => new NodeId(StableId);

    // Resolve the catalog kind key from state flags.
    public NodeKindKey Kind
    {
        get
        {
            if (IsFinal)       return new NodeKindKey(HsmKinds.Final);
            if (IsDeepHistory) return new NodeKindKey(HsmKinds.DeepHistory);
            if (IsHistory)     return new NodeKindKey(HsmKinds.History);
            if (IsParallel)    return new NodeKindKey(HsmKinds.Parallel);
            if (Children.Count > 0) return new NodeKindKey(HsmKinds.Composite);
            return new NodeKindKey(HsmKinds.Simple);
        }
    }

    public string Title => Name;
    public string? Subtitle => null;
    public NodeCategory Category => NodeCategory.Custom;
    public NodeState State => IsBreakpoint ? NodeState.Warning : NodeState.Normal;
    public string? StatusTooltip => null;
    public bool ShowAdvancedPins => false;

    // Two hidden pins: output (source of transitions FROM this state) and input (target TO this state).
    // Lazy-initialized to avoid allocation on non-pinned code paths.
    private IReadOnlyList<IPinModel>? _pins;
    public IReadOnlyList<IPinModel> Pins => _pins ??= BuildPins();

    private IReadOnlyList<IPinModel> BuildPins()
    {
        return new IPinModel[]
        {
            new HsmPinModel(new PinId(HiddenOutputPinId), new NodeId(StableId), PinDirection.Output),
            new HsmPinModel(new PinId(HiddenInputPinId),  new NodeId(StableId), PinDirection.Input),
        };
    }

    // ParentContainerId is null for top-level states (Parent is RootState which has no parent).
    public NodeId? ParentContainerId =>
        Parent?.Parent != null ? new NodeId(Parent!.StableId) : (NodeId?)null;

    // ---- IContainerNodeModel ----

    public bool IsContainer => Children.Count > 0 || IsParallel;

    public IReadOnlyList<NodeId> ChildNodeIds =>
        Children.Select(c => new NodeId(c.StableId)).ToList();

    // For parallel composites, expose region descriptors.
    // For non-parallel composites, return empty.
    public IReadOnlyList<RegionDescriptor> Regions
    {
        get
        {
            if (!IsParallel || RegionNodes.Count == 0)
                return Array.Empty<RegionDescriptor>();
            return RegionNodes
                .Select(r => new RegionDescriptor(r.RegionIndex, r.Name, r.Priority, null))
                .ToList();
        }
    }

    public int GetRegionIndexForChild(NodeId childId)
    {
        // Find the child StateNode with matching StableId
        var child = Children.FirstOrDefault(c => c.StableId == childId.Value);
        if (child == null) return -1;
        return child.RegionIndex;
    }

    public ContainerPadding Padding => ContainerPadding.Default;

    public Vector2 MinimumInteriorSize =>
        IsParallel ? new Vector2(280f, 120f) : new Vector2(200f, 80f);

	public RegionLayoutOrientation RegionOrientation => RegionLayoutOrientation.VerticalStack;
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
    /// <summary>
    /// Blackboard field that receives the expression result of <see cref="ActionFunction"/>.
    /// Null when no field binding is authored. Persisted.
    /// </summary>
    public string? ExpressionTargetField;
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
    /// <summary>
    /// Blackboard field that receives the expression result of <see cref="ActionFunction"/>.
    /// Null when no field binding is authored. Persisted.
    /// </summary>
    public string? ExpressionTargetField;
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
