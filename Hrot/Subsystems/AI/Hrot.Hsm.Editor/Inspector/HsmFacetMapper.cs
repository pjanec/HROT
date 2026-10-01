using System;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Kernel.Data;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Inspector;

// Maps HSM sub-selection identifiers to inspector facet structs.
// Constructed once per loaded HsmAsset and held alive while the asset is open.
public sealed class HsmFacetMapper
{
    private readonly HsmAsset             _asset;
    private readonly HsmFacetFqnContext?  _fqnContext;

    public HsmFacetMapper(HsmAsset asset)
        : this(asset, null)
    {
    }

    /// <summary>
    /// Constructs a mapper that writes the current transition action FQN to
    /// <paramref name="fqnContext"/> before returning transition/global-transition facets,
    /// so the <see cref="HsmBlackboardFieldPickerDrawer"/> can filter variables by DtoType.
    /// </summary>
    public HsmFacetMapper(HsmAsset asset, HsmFacetFqnContext? fqnContext)
    {
        _asset      = asset;
        _fqnContext = fqnContext;
    }

    public StateFacet GetStateFacet(Guid stableId)
    {
        var s = _asset.FindStateByStableId(stableId)
            ?? throw new KeyNotFoundException($"State {stableId} not found");
        return new StateFacet
        {
            Name                    = s.Name,
            // ⭐ CE-417 (slice 4a): the model holds bindings; this facet keeps its flat shape until 4b.
            OnEntryAction           = s.OnEntry?.MethodFqn,
            OnExitAction            = s.OnExit?.MethodFqn,
            ActivityAction          = s.Activity?.MethodFqn,
            TimerAction             = s.Timer?.MethodFqn,
            // ⭐ §11.1a — the authored name, plus its two derived companions.
            SubtreeName             = s.SubtreeName,
            SubtreeAssetId          = s.SubtreeAssetId == Guid.Empty ? string.Empty
                                                                     : s.SubtreeAssetId.ToString(),
            IsSubtreeResolved       = s.IsSubtreeResolved,
            // ⭐ CE-385 — the blueprint-hosted activity, same name+id shape as the subtree pair.
            ActivityBlueprintName    = s.Activity?.BlueprintName,
            ActivityBlueprintAssetId = (s.Activity?.BlueprintAssetId ?? Guid.Empty) == Guid.Empty
                                         ? string.Empty
                                         : s.Activity!.BlueprintAssetId.ToString(),
            ExpressionTargetField    = s.StateWideTargetField,   // CE-387 / CE-417
            Flags                   = BuildStateFlags(s),
            DeferredEventIds        = new List<ushort>(s.DeferredEventIds),
            OutputLanesSummary      = "",  // populated by HS-S1-19
            Comment                 = s.Comment,
            IsBreakpoint            = s.IsBreakpoint,
            StableId                = s.StableId.ToString(),
            IncomingTransitionCount = _asset.AllTransitions.Count(t => t.Target == s),
            OutgoingTransitionCount = s.OutgoingTransitions.Count,
        };
    }

    public TransitionFacet GetTransitionFacet(Guid visualId)
    {
        var t = _asset.FindTransitionByVisualId(visualId)
            ?? throw new KeyNotFoundException($"Transition {visualId} not found");
        var lca     = FindLca(t.Source, t.Target);
        var lcaCost = (ushort)(DepthOf(t.Source) + DepthOf(t.Target) - 2 * DepthOf(lca));
        if (_fqnContext is not null)
        {
            _fqnContext.CurrentActionFqn = string.IsNullOrEmpty(t.Action?.MethodFqn) ? null : t.Action!.MethodFqn;
            _fqnContext.CurrentVisualId  = t.VisualId.ToString();
        }
        return new TransitionFacet
        {
            SourceStateName       = t.Source.Name,
            TargetStateName       = t.Target.Name,
            EventId               = t.EventId,
            GuardFunction         = t.Guard?.MethodFqn,
            // ⭐ CE-385 / CE-381 — the blueprint-hosted guard and the polled marker.
            GuardBlueprintName    = t.Guard?.BlueprintName,
            GuardBlueprintAssetId = (t.Guard?.BlueprintAssetId ?? Guid.Empty) == Guid.Empty
                                       ? string.Empty
                                       : t.Guard!.BlueprintAssetId.ToString(),
            IsPolled              = t.IsPolled,
            ActionFunction        = t.Action?.MethodFqn,
            ExpressionTargetField = HsmFacetBindings.TransitionTargetField(t.Guard, t.Action),   // CE-417 (4a)
            Priority              = t.Priority,
            Kind                  = t.Kind,
            SyncGroupId           = t.SyncGroupId,
            Comment               = t.Comment,
            IsBreakpoint          = t.IsBreakpoint,
            VisualId              = t.VisualId.ToString(),
            LcaStateName          = lca.Name,
            LcaCost               = lcaCost,
        };
    }

    public RegionFacet GetRegionFacet(Guid parentStableId, int regionIndex)
    {
        var parent = _asset.FindStateByStableId(parentStableId)
            ?? throw new KeyNotFoundException($"State {parentStableId} not found");
        var r = parent.RegionNodes.FirstOrDefault(x => x.RegionIndex == regionIndex)
            ?? throw new KeyNotFoundException($"Region {regionIndex} in state {parentStableId} not found");
        return new RegionFacet
        {
            Name             = r.Name,
            Priority         = r.Priority,
            InitialChildName = r.InitialChild?.Name,
            Comment          = r.Comment,
            ColorOverride    = r.ColorOverride,
            StableId         = r.StableId.ToString(),
        };
    }

    public EventFacet GetEventFacet(ushort eventId)
    {
        var e = _asset.FindEventById(eventId)
            ?? throw new KeyNotFoundException($"Event {eventId} not found");
        return new EventFacet
        {
            Name                     = e.Name,
            EventId                  = e.EventId,
            PayloadSize              = e.PayloadSize,
            IsIndirect               = e.IsIndirect,
            Priority                 = EventPriority.Normal,  // not stored in EventDefinition; stub
            DeferredByStatesSummary  = string.Join(", ", _asset.AllStates
                .Where(s => s.DeferredEventIds.Contains(eventId))
                .Select(s => s.Name)),
            TransitionReferenceCount = _asset.AllTransitions.Count(t => t.EventId == eventId),
            GlobalTransitionTarget   = _asset.AllGlobalTransitions
                .FirstOrDefault(g => g.EventId == eventId)?.Target.Name,
        };
    }

    public GlobalTransitionFacet GetGlobalTransitionFacet(Guid visualId)
    {
        var g = _asset.AllGlobalTransitions.FirstOrDefault(x => x.VisualId == visualId)
            ?? throw new KeyNotFoundException($"Global transition {visualId} not found");
        if (_fqnContext is not null)
        {
            _fqnContext.CurrentActionFqn = string.IsNullOrEmpty(g.Action?.MethodFqn) ? null : g.Action!.MethodFqn;
            _fqnContext.CurrentVisualId  = g.VisualId.ToString();
        }
        return new GlobalTransitionFacet
        {
            EventId               = g.EventId,
            TargetStateName       = g.Target.Name,
            GuardFunction         = g.Guard?.MethodFqn,
            ActionFunction        = g.Action?.MethodFqn,
            ExpressionTargetField = HsmFacetBindings.TransitionTargetField(g.Guard, g.Action),   // CE-417 (4a)
            Priority              = g.Priority,
            Comment               = g.Comment,
            VisualId              = g.VisualId.ToString(),
        };
    }

    // Finds the least common ancestor of two states in the tree.
    // The LCA is the deepest state that is an ancestor of both a and b (inclusive).
    public StateNode FindLca(StateNode a, StateNode b)
    {
        var aPath = AncestorPathFromRoot(a);
        var bPath = AncestorPathFromRoot(b);
        StateNode lca = _asset.RootState;
        for (int i = 0; i < Math.Min(aPath.Count, bPath.Count); i++)
        {
            if (aPath[i] == bPath[i]) lca = aPath[i];
            else break;
        }
        return lca;
    }

    // Returns the path from RootState down to the given state (inclusive at both ends).
    // E.g. for a leaf state in RootState -> A -> B -> Leaf, returns [RootState, A, B, Leaf].
    private static List<StateNode> AncestorPathFromRoot(StateNode state)
    {
        var path = new List<StateNode>();
        var current = state;
        while (current != null)
        {
            path.Add(current);
            current = current.Parent;
        }
        path.Reverse();
        return path;
    }

    // Returns the depth of a state in the tree (RootState = 0, top-level = 1, ...).
    private static int DepthOf(StateNode s)
    {
        int depth = 0;
        var cur = s;
        while (cur.Parent != null) { depth++; cur = cur.Parent; }
        return depth;
    }

    private static StateFlags BuildStateFlags(StateNode s)
    {
        var f = StateFlags.None;
        if (s.Children.Count > 0)     f |= StateFlags.IsComposite;
        if (s.IsHistory)              f |= StateFlags.IsHistory;
        if (s.IsDeepHistory)          f |= StateFlags.IsDeepHistory;
        if (s.IsParallel)             f |= StateFlags.IsParallel;
        if (s.OnEntry?.MethodFqn  != null) f |= StateFlags.HasOnEntry;
        if (s.OnExit?.MethodFqn   != null) f |= StateFlags.HasOnExit;
        if (s.Activity?.MethodFqn != null) f |= StateFlags.HasOnUpdate;
        if (s.IsInitial)              f |= StateFlags.IsInitial;
        if (s.IsFinal)                f |= StateFlags.IsFinal;
        return f;
    }
}
