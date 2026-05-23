using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Validation;

// Validates an HsmAsset and returns a list of diagnostics.
// Implements the rules from HSM_Editor_NodeEditor_Host_Design.md section 12.
// Rules that require external context (UnboundAction, UnboundGuard, RegionCountExceedsTier,
// TransitionPriorityCycle, ActionSignatureMismatch, DanglingReferenceAfterReload)
// are deferred to a later slice and emit no diagnostics here.
public sealed class HsmValidator
{
    private const int MaxAllowedDepth = 16;

    public HsmValidator() { }

    public IReadOnlyList<HsmDiagnostic> Validate(HsmAsset asset)
    {
        var diagnostics = new List<HsmDiagnostic>();

        CheckInitialChildren(asset, diagnostics);
        CheckHistoryOutsideComposite(asset, diagnostics);
        CheckFinalStateWithChildren(asset, diagnostics);
        CheckFinalStateWithOutgoingTransitions(asset, diagnostics);
        CheckStateDepthExceeded(asset, diagnostics);
        CheckEventReferenceDangling(asset, diagnostics);
        CheckOutputLaneConflicts(asset, diagnostics);

        return diagnostics;
    }

    // ---- Rule implementations -----------------------------------------------

    // Rule 1: CompositeWithoutInitialChild / MultipleInitialChildrenInSameParent.
    // For each composite state (Children.Count > 0): count children with IsInitial=true.
    // Zero children initial -> CompositeWithoutInitialChild (Error).
    // More than one -> MultipleInitialChildrenInSameParent (Error).
    private static void CheckInitialChildren(HsmAsset asset, List<HsmDiagnostic> out_)
    {
        foreach (var s in asset.AllStates)
        {
            if (s.Children.Count == 0) continue;
            int initialCount = s.Children.Count(c => c.IsInitial);
            if (initialCount == 0)
            {
                out_.Add(new HsmDiagnostic(
                    HsmDiagnosticCode.CompositeWithoutInitialChild,
                    HsmDiagnosticSeverity.Error,
                    $"Composite state '{s.Name}' has no child marked as initial.",
                    new[] { s.StableId }));
            }
            else if (initialCount > 1)
            {
                out_.Add(new HsmDiagnostic(
                    HsmDiagnosticCode.MultipleInitialChildrenInSameParent,
                    HsmDiagnosticSeverity.Error,
                    $"Composite state '{s.Name}' has {initialCount} children marked as initial; only one is allowed.",
                    new[] { s.StableId }));
            }
        }
    }

    // Rule 2: HistoryOutsideComposite.
    // A history pseudo-state is only meaningful inside a composite with at least one other child.
    // Flag if: parent is null, parent is RootState, or parent has <= 1 child total.
    private static void CheckHistoryOutsideComposite(HsmAsset asset, List<HsmDiagnostic> out_)
    {
        foreach (var s in asset.AllStates)
        {
            if (!s.IsHistory && !s.IsDeepHistory) continue;
            if (s.Parent == null || s.Parent == asset.RootState || s.Parent.Children.Count <= 1)
            {
                out_.Add(new HsmDiagnostic(
                    HsmDiagnosticCode.HistoryOutsideComposite,
                    HsmDiagnosticSeverity.Warning,
                    $"History state '{s.Name}' is not inside a composite with multiple children.",
                    new[] { s.StableId }));
            }
        }
    }

    // Rule 3: FinalStateWithChildren.
    // A final state must be a leaf; having children is invalid.
    private static void CheckFinalStateWithChildren(HsmAsset asset, List<HsmDiagnostic> out_)
    {
        foreach (var s in asset.AllStates)
        {
            if (s.IsFinal && s.Children.Count > 0)
            {
                out_.Add(new HsmDiagnostic(
                    HsmDiagnosticCode.FinalStateWithChildren,
                    HsmDiagnosticSeverity.Error,
                    $"Final state '{s.Name}' must not have child states.",
                    new[] { s.StableId }));
            }
        }
    }

    // Rule 4: FinalStateWithOutgoingTransition.
    // A final state must not have outgoing transitions.
    private static void CheckFinalStateWithOutgoingTransitions(HsmAsset asset, List<HsmDiagnostic> out_)
    {
        foreach (var s in asset.AllStates)
        {
            if (s.IsFinal && s.OutgoingTransitions.Count > 0)
            {
                out_.Add(new HsmDiagnostic(
                    HsmDiagnosticCode.FinalStateWithOutgoingTransition,
                    HsmDiagnosticSeverity.Error,
                    $"Final state '{s.Name}' must not have outgoing transitions.",
                    new[] { s.StableId }));
            }
        }
    }

    // Rule 5: StateDepthExceeded.
    // A state's depth (distance from RootState) must not exceed 16.
    private static void CheckStateDepthExceeded(HsmAsset asset, List<HsmDiagnostic> out_)
    {
        foreach (var s in asset.AllStates)
        {
            int depth = ComputeDepth(s, asset.RootState);
            if (depth > MaxAllowedDepth)
            {
                out_.Add(new HsmDiagnostic(
                    HsmDiagnosticCode.StateDepthExceeded,
                    HsmDiagnosticSeverity.Error,
                    $"State '{s.Name}' is at depth {depth}, which exceeds the maximum of {MaxAllowedDepth}.",
                    new[] { s.StableId }));
            }
        }
    }

    // Rule 6: EventReferenceDangling.
    // A transition that references a non-zero EventId must point to an event in AllEvents.
    private static void CheckEventReferenceDangling(HsmAsset asset, List<HsmDiagnostic> out_)
    {
        foreach (var t in asset.AllTransitions)
        {
            if (t.EventId != 0 && asset.FindEventById(t.EventId) == null)
            {
                out_.Add(new HsmDiagnostic(
                    HsmDiagnosticCode.EventReferenceDangling,
                    HsmDiagnosticSeverity.Error,
                    $"Transition references event ID {t.EventId} which is not present in the event table.",
                    new[] { t.VisualId }));
            }
        }
        foreach (var g in asset.AllGlobalTransitions)
        {
            if (g.EventId != 0 && asset.FindEventById(g.EventId) == null)
            {
                out_.Add(new HsmDiagnostic(
                    HsmDiagnosticCode.EventReferenceDangling,
                    HsmDiagnosticSeverity.Error,
                    $"Global transition references event ID {g.EventId} which is not present in the event table.",
                    new[] { g.VisualId }));
            }
        }
    }

    // Rule 7: OutputLaneConflict.
    // For each parallel state with at least two regions, check whether any two regions
    // produce commands on the same CommandLane (overlapping OutputLaneMask bits).
    private static void CheckOutputLaneConflicts(HsmAsset asset, List<HsmDiagnostic> out_)
    {
        foreach (var s in asset.AllStates)
        {
            if (!s.IsParallel || s.RegionNodes.Count < 2) continue;

            // Compute OR-mask per region index from direct children.
            var regionMasks = new Dictionary<int, byte>();
            foreach (var child in s.Children)
            {
                if (!regionMasks.TryGetValue(child.RegionIndex, out byte existing))
                    regionMasks[child.RegionIndex] = child.OutputLaneMask;
                else
                    regionMasks[child.RegionIndex] = (byte)(existing | child.OutputLaneMask);
            }

            var indices = regionMasks.Keys.ToList();
            for (int i = 0; i < indices.Count; i++)
            {
                for (int j = i + 1; j < indices.Count; j++)
                {
                    if ((regionMasks[indices[i]] & regionMasks[indices[j]]) != 0)
                    {
                        out_.Add(new HsmDiagnostic(
                            HsmDiagnosticCode.OutputLaneConflict,
                            HsmDiagnosticSeverity.Warning,
                            $"Parallel state '{s.Name}' has regions {indices[i]} and {indices[j]} writing to the same output lane.",
                            new[] { s.StableId }));
                    }
                }
            }
        }
    }

    // ---- Helpers -----------------------------------------------

    // Computes the depth of state s relative to rootState.
    // depth(rootState) = 0; depth(direct child of rootState) = 1.
    private static int ComputeDepth(StateNode s, StateNode rootState)
    {
        int depth = 0;
        StateNode current = s;
        while (current != rootState && current.Parent != null)
        {
            depth++;
            current = current.Parent;
        }
        return depth;
    }
}
