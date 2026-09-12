using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared.Blackboard;
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
    private readonly IActionSchemaExporter? _schema;

    /// <summary>
    /// Returns true when the referenced asset (identified by its asset GUID) is "stateful" —
    /// i.e. it contains at least one stateful node that maintains per-instance WorkingState.
    /// Defaults to <c>_ => false</c> (all Subtrees treated as stateless) so existing callers
    /// compile and run unchanged.  Production should wire this to check the referenced
    /// BTree/HSM asset for any <c>ThreeParamReusableStateful</c> action (or WorkingState).
    /// </summary>
    private readonly Func<Guid, bool> _isStatefulSubtree;

    /// <summary>
    /// (S3-6) Resolver: given a subtree asset id, returns the set of <b>shared</b> (Behavior/Entity)
    /// scope keys its stateful bindings resolve to (FNV-1a scope keys per AIB-DD §4.4). Node-scoped
    /// and stateless subtrees contribute no shared keys (empty). Injected like
    /// <see cref="_isStatefulSubtree"/> so the validator stays decoupled from the key algorithm;
    /// defaults to "no shared keys" so existing callers are unaffected.
    /// </summary>
    private readonly Func<Guid, IReadOnlyCollection<int>> _sharedScopeKeys;

    public HsmValidator(IActionSchemaExporter? schema = null,
        Func<Guid, bool>? isStatefulSubtree = null,
        Func<Guid, IReadOnlyCollection<int>>? sharedScopeKeys = null)
    {
        _schema = schema;
        _isStatefulSubtree = isStatefulSubtree ?? (_ => false);
        _sharedScopeKeys = sharedScopeKeys ?? (_ => System.Array.Empty<int>());
    }

    public IReadOnlyList<HsmDiagnostic> Validate(HsmAsset asset,
        IBlackboardManagedAsset? blackboard = null)
    {
        var diagnostics = new List<HsmDiagnostic>();

        CheckInitialChildren(asset, diagnostics);
        CheckHistoryOutsideComposite(asset, diagnostics);
        CheckFinalStateWithChildren(asset, diagnostics);
        CheckFinalStateWithOutgoingTransitions(asset, diagnostics);
        CheckStateDepthExceeded(asset, diagnostics);
        CheckEventReferenceDangling(asset, diagnostics);
        CheckOutputLaneConflicts(asset, diagnostics);
        CheckConcurrentStatefulSubtrees(asset, diagnostics);
        CheckConcurrentSharedScopeKeys(asset, diagnostics);

        if (blackboard != null)
            CheckBlackboardRegionConflicts(asset, blackboard, diagnostics);

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

    // Rule 8 (S2-4): ConcurrentStatefulSubtree.
    // For each parallel composite with ≥2 regions, collect every state that has a non-empty
    // SubtreeAssetId, grouped by (compositeId, referencedAssetId) → set of distinct RegionIndex
    // values.  If the same SubtreeAssetId appears in ≥2 distinct regions AND
    // _isStatefulSubtree(id) is true → hard error.
    // A stateless Subtree (resolver returns false) in multiple regions is harmless.
    private void CheckConcurrentStatefulSubtrees(HsmAsset asset, List<HsmDiagnostic> out_)
    {
        foreach (var composite in asset.AllStates)
        {
            if (!composite.IsParallel || composite.RegionNodes.Count < 2) continue;

            // Collect (subtreeAssetId → HashSet<regionIndex>) for every state ANYWHERE under this
            // composite that carries a non-empty SubtreeAssetId.
            //
            // ⭐⭐ DEBT-AIB-029 (Batch 76): this used to walk DIRECT CHILDREN ONLY, so a host nested
            //    one level deeper escaped the rule entirely. ⚠ Theoretical until Batch 75 persisted
            //    SubtreeAssetId — after which a designer could author the escape and SAVE it.
            var subtreeRegions = new Dictionary<Guid, HashSet<int>>();
            foreach (var child in composite.Children)
            {
                // ⭐⭐⭐ The REGION INDEX comes from the DIRECT CHILD and is carried down.
                // ⛔ Reading RegionIndex off a deep descendant would be the wrong space: inside a
                //    NESTED parallel composite that field means the INNER composite's region, so a
                //    descendant could report region 0 while living in this composite's region 1.
                foreach (var host in SubtreeHostsUnder(child))
                {
                    if (!subtreeRegions.TryGetValue(host, out var regionSet))
                    {
                        regionSet = new HashSet<int>();
                        subtreeRegions[host] = regionSet;
                    }
                    regionSet.Add(child.RegionIndex);
                }
            }

            // Emit a hard error for every stateful subtree that spans ≥2 distinct regions.
            foreach (var (subtreeId, regions) in subtreeRegions)
            {
                if (regions.Count < 2) continue;
                if (!_isStatefulSubtree(subtreeId)) continue;

                var sortedRegions = regions.OrderBy(r => r).ToList();
                out_.Add(new HsmDiagnostic(
                    HsmDiagnosticCode.ConcurrentStatefulSubtree,
                    HsmDiagnosticSeverity.Error,
                    $"Parallel composite '{composite.Name}' runs the same stateful Subtree " +
                    $"({subtreeId:D}) concurrently in regions " +
                    $"{string.Join(", ", sortedRegions)}. " +
                    $"Concurrent execution of the same stateful Subtree produces synthetic " +
                    $"key collisions and race-write corruption.",
                    new[] { composite.StableId }));
            }
        }
    }

    /// <summary>
    /// ⭐⭐ Every non-empty <c>SubtreeAssetId</c> on <paramref name="root"/> or any of its descendants.
    ///
    /// <para>
    /// ⚠ <b>On the CYCLE question the handoff asked to have named.</b> This walk is over the STATE
    /// TREE, which is a tree — so it cannot cycle by construction, and no depth cap is wanted. ⭐ The
    /// visited set is here for a different reason: a <b>malformed</b> model (a hand-edited or
    /// corrupted file whose parent/child wiring disagrees) would otherwise hang the editor, and a
    /// validator that hangs on bad input is worse than one that misses a case.
    /// </para>
    ///
    /// <para>
    /// ⛔ <b>The REAL cycle question is a different one and this rule does not answer it:</b> asset
    /// <c>A</c> hosting <c>B</c> which hosts <c>A</c>. That is a walk over ASSETS, needs a resolver
    /// this validator does not have, and belongs to whoever builds subtree hosting for real — ⭐ it is
    /// named here rather than half-handled.
    /// </para>
    /// </summary>
    private static IEnumerable<Guid> SubtreeHostsUnder(StateNode root)
    {
        var seen  = new HashSet<Guid>();
        var stack = new Stack<StateNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!seen.Add(node.StableId)) continue;   // malformed graph guard, not a depth cap

            if (node.SubtreeAssetId != Guid.Empty) yield return node.SubtreeAssetId;

            foreach (var child in node.Children) stack.Push(child);
        }
    }

    // Rule 8b (S3-6): ConcurrentSharedScopeKey — the shared-slot analogue of Rule 8.
    // Rule 8 catches the SAME subtree asset in ≥2 regions (its per-node Node-scope keys collide).
    // This rule catches DIFFERENT subtree assets (or nodes) that resolve to the SAME Behavior/
    // Entity shared-slot key: because Behavior/Entity-scoped working state is shared per entity,
    // two orthogonal parallel regions writing the same scope key race on one slot.
    // Purely-sequential Behavior use (same var across non-concurrent nodes) never reaches a
    // parallel composite here, so it stays valid — the rule only fires across distinct regions.
    private void CheckConcurrentSharedScopeKeys(HsmAsset asset, List<HsmDiagnostic> out_)
    {
        foreach (var composite in asset.AllStates)
        {
            if (!composite.IsParallel || composite.RegionNodes.Count < 2) continue;

            // scopeKey → set of distinct region indices whose direct-child subtree writes it.
            var keyRegions = new Dictionary<int, HashSet<int>>();
            // ⭐ DEBT-AIB-029: the same deep walk as rule 8, with the same region-index rule —
            //   the direct child fixes the region, the descendants contribute the hosts.
            foreach (var child in composite.Children)
            {
                foreach (var host in SubtreeHostsUnder(child))
                foreach (var scopeKey in _sharedScopeKeys(host))
                {
                    if (!keyRegions.TryGetValue(scopeKey, out var regionSet))
                    {
                        regionSet = new HashSet<int>();
                        keyRegions[scopeKey] = regionSet;
                    }
                    regionSet.Add(child.RegionIndex);
                }
            }

            foreach (var (scopeKey, regions) in keyRegions)
            {
                if (regions.Count < 2) continue;

                var sortedRegions = regions.OrderBy(r => r).ToList();
                out_.Add(new HsmDiagnostic(
                    HsmDiagnosticCode.ConcurrentSharedScopeKey,
                    HsmDiagnosticSeverity.Error,
                    $"Parallel composite '{composite.Name}' writes the same shared working-state " +
                    $"slot (scope key {scopeKey}) concurrently in regions " +
                    $"{string.Join(", ", sortedRegions)}. Behavior/Entity-scoped state is shared " +
                    $"per entity, so concurrent writes from orthogonal regions race and corrupt the slot.",
                    new[] { composite.StableId }));
            }
        }
    }

    /// <summary>
    /// Rule 9: <c>CrossRegionBlackboardConflict</c> — two writers of one blackboard variable in
    /// DIFFERENT parallel regions of the same composite state.
    ///
    /// <para>
    /// ⭐⭐⭐ <b><c>W7c</c> — the writer set is the UNION of binding styles, not just aliases.</b>
    /// 📄 §9.2 says the writer set is <i>"every action method that mutates this variable"</i> —
    /// ⛔ <b>not "every alias"</b>. This walked <c>GetAliasesFor</c> alone, so the
    /// <b>locally-bound</b> style was invisible: a transition carrying an
    /// <c>ExpressionTargetField</c> writes the named variable directly and records NO alias.
    /// ⇒ 🔴 <b>the rule read as guarded while leaving the other style open</b> — <c>BP-240</c>'s
    /// shape a third time: green because of what it happens to look at.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>Only the ENUMERATION was short, not the classification.</b> <see cref="HasWritingAction"/>
    /// already implements §9.6 correctly (conservative on a null schema, an unknown FQN, and any
    /// non-<c>ReadOnly</c> access), so it is reused for both styles rather than re-derived.
    /// </para>
    ///
    /// <para>
    /// ⛔⛔ <b>The §9.5 Approach-B Sync-Out arm is NOT here, and it is BLOCKED — not skipped.</b>
    /// 📐 Measured: <c>SubtreeSyncBinding</c> hangs off <c>IBTreeSyncableAsset</c>, which only
    /// <c>BehaviorTreeAsset</c> implements, and this validator is handed the <c>HsmAsset</c> plus its
    /// own blackboard — it cannot reach a hosted sub-tree's bindings.
    /// </para>
    ///
    /// <para>
    /// ⚠⚠ <b>Batch 92 (<c>92e</c>) — the SECOND half of this note had ROTTED and is corrected here.</b>
    /// It used to add <i>"and it could not resolve one if it could: <c>StateNode.SubtreeAssetId</c>
    /// has no counterpart on <c>StateNodeDto</c>"</i>. ⛔ <b>That is false.</b> 📐 The field exists at
    /// <c>HsmAssetDto.cs:73</c> and <c>DEBT-AIB-028(a)</c> was <b>resolved in Batch 75</b> — a reloaded
    /// asset DOES know which sub-tree a state hosts.
    /// </para>
    ///
    /// <para>
    /// ⭐⭐ <b>The real blocker is the FIRST half alone, and it has a name: <c>M-24</c></b> —
    /// <c>HsmAsset</c> does not implement <c>IBTreeSyncableAsset</c>, so there is no API on an HSM
    /// through which a sub-tree's sync bindings could be reached at all. ⇒ ⭐ widening this arm is a
    /// question about that interface, ⛔ not about persistence.
    /// </para>
    /// </summary>
    private void CheckBlackboardRegionConflicts(
        HsmAsset asset,
        IBlackboardManagedAsset blackboard,
        List<HsmDiagnostic> out_)
    {
        // Build a map: StateNode.StableId -> StateNode for fast lookup.
        var stateById = asset.AllStates.ToDictionary(s => s.StableId);

        foreach (var variable in blackboard.BlackboardVariables)
        {
            // ⭐⭐⭐ W7b (§9.4) — the designer has said the race on THIS VARIABLE is intended:
            //     "they go to the variable's ⋮ menu → 'Allow concurrent writes' → checkbox."
            // ⛔⛔ This is PER VARIABLE and is checked BEFORE the writer set is even built, which is
            //     precisely what distinguishes it from W7a's per-PAIR suppression below. §9.3: a per-pair
            //     suppression is deliberately narrow, so "a new aliasing relationship on the same
            //     variable would surface a fresh diagnostic"; W7b is the wide one, and covers pairs that
            //     do not exist yet. ⇒ two mechanisms, on purpose. Collapsing them would either silence
            //     future writers nobody reviewed, or make "allow" need re-clicking per new alias.
            if (blackboard.IsConcurrentWritesAllowed(variable.Name)) continue;

            // ⭐ W7c: the union. A "writer site" is anything that mutates this variable and sits in a
            //   region of a parallel composite, whichever binding style put it there.
            var writers = new List<WriterSite>();

            // ── style 1: Approach-A alias bindings, keyed by variable name ──────────────
            foreach (var binding in blackboard.GetAliasesFor(variable.Name))
            {
                if (!stateById.TryGetValue(binding.RequiringElementId, out var state)) continue;
                if (!TryRegionOf(state, out var compositeId, out int regionIndex)) continue;
                writers.Add(new WriterSite(
                    compositeId, regionIndex, state.Name, "alias",
                    IsWriter: HasWritingAction(state), ElementId: binding.RequiringElementId));
            }

            // ── style 2: locally-bound writers — ExpressionTargetField names the variable ──
            // ⚠ The region of a TRANSITION is the region of its SOURCE state: that is where the
            //   transition is live. A global transition has no source, so it belongs to no region
            //   and cannot participate in a cross-region conflict — excluded, deliberately.
            foreach (var t in asset.AllTransitions)
            {
                if (!IsLocallyBoundTo(t.ExpressionTargetField, variable.Name)) continue;
                if (t.Source == null) continue;
                if (!TryRegionOf(t.Source, out var compositeId, out int regionIndex)) continue;
                writers.Add(new WriterSite(
                    compositeId, regionIndex, $"{t.Source.Name} → {t.Target?.Name}", "expression target",
                    IsWriter: IsWritingFqn(t.ActionFunction), ElementId: t.VisualId));
            }

            // ── the pair check, over the UNION ──────────────────────────────────────────
            foreach (var group in writers.GroupBy(w => w.CompositeId))
            {
                var sites = group.ToList();
                if (sites.Count < 2) continue;

                // ⚠⚠ The writer test is per-GROUP, not per-site, and that is the SHIPPED semantics —
                //    restored after W7c's first draft filtered per site and reddened
                //    Validate_MixedAccess_OneReadOnlyOneReadWrite_ProducesConflict.
                //    §9.6 permits concurrent READERS ("only read by both regions and never written");
                //    it does NOT permit a reader racing a writer, which is equally non-deterministic.
                //    ⇒ at least one writer in the composite, then any cross-region PAIR conflicts.
                if (!sites.Any(w => w.IsWriter)) continue;

                for (int i = 0; i < sites.Count; i++)
                for (int j = i + 1; j < sites.Count; j++)
                {
                    if (sites[i].RegionIndex == sites[j].RegionIndex) continue;

                    // ⭐⭐ W7a — the suppression is authored, persisted, emitted… and this rule never
                    //    read it. `IsConflictSuppressed` was consulted ONLY by
                    //    BlackboardAliasDropValidator, so clicking Suppress silenced the DROP TARGET
                    //    while the PANEL WARNING persisted. ⚠ An affordance that half-works is worse
                    //    than one that is absent: the designer clicks and nothing appears to happen.
                    // ⛔ PER-PAIR, never per-variable (§9.3) — hence `continue`, not `goto`: another
                    //    pair on the SAME variable must still be reported.
                    if (blackboard.IsConflictSuppressed(
                            variable.Name,
                            BlackboardConflictKey.ForWriterPair(sites[i].ElementId, sites[j].ElementId)))
                        continue;

                    var composite = stateById[group.Key];
                    out_.Add(new HsmDiagnostic(
                        HsmDiagnosticCode.CrossRegionBlackboardConflict,
                        HsmDiagnosticSeverity.Warning,
                        $"Variable '{variable.Name}' is written by sub-trees in regions " +
                        $"{sites[i].RegionIndex} and {sites[j].RegionIndex} of " +
                        $"parallel composite '{composite.Name}' -- concurrent writes are " +
                        $"non-deterministic.",
                        new[] { composite.StableId }));
                    goto nextVariable;   // One diagnostic per variable is enough.
                }
            }
            nextVariable:;
        }
    }

    /// <summary>⭐ <c>W7c</c> — one writer of one variable, wherever it came from.</summary>
    private readonly record struct WriterSite(
        Guid CompositeId, int RegionIndex, string Label, string Style, bool IsWriter, Guid ElementId);

    /// <summary>
    /// The parallel composite and region a state sits in, or <c>false</c> when it is not a direct
    /// child of a parallel composite — in which case there is no concurrency to conflict over.
    /// </summary>
    private static bool TryRegionOf(StateNode state, out Guid compositeId, out int regionIndex)
    {
        compositeId = Guid.Empty;
        regionIndex = 0;
        if (state.Parent == null || !state.Parent.IsParallel) return false;
        compositeId = state.Parent.StableId;
        regionIndex = state.RegionIndex;
        return true;
    }

    /// <summary>
    /// ⭐ Locally bound to THIS variable. ⚠ Ordinal, case-insensitive: the field picker writes the
    /// variable's own name, and a case difference between the two would silently mean "not a writer"
    /// — the failure mode this whole rule exists to remove.
    /// </summary>
    // ⭐ E7b — ONE definition, on the model. This rule and HsmAsset.CountNodesReferencingVariable ask
    //   the same question ("is this ExpressionTargetField bound to that variable"), and two spellings
    //   of one predicate is how a conflict rule and a usage count drift apart.
    private static bool IsLocallyBoundTo(string? expressionTargetField, string variableName)
        => Model.HsmAsset.IsExpressionTargetOf(expressionTargetField, variableName);

    // ---- Helpers -----------------------------------------------

    // Returns true if the state has at least one action that writes to the blackboard.
    // Conservative: unknown schema, unknown FQN, or Unknown access -> treat as writer.
    // A state with ONLY ReadOnly actions is safe to skip from conflict detection.
    private bool HasWritingAction(StateNode state)
    {
        // ⚠⚠ RESTORED after W7c's refactor briefly dropped it, and the existing suite caught it.
        //    NO SCHEMA ⇒ EVERY state is a writer — maximally conservative, and it short-circuits
        //    BEFORE the per-FQN walk. ⛔ Without it a state with ZERO actions stops being a writer
        //    even when nothing is known, which silently narrows the rule exactly where it has the
        //    least information. (The comment below about "no FQNs ⇒ not a writer" only ever applied
        //    when a schema WAS present.)
        if (_schema == null) return true;

        string?[] fqns = {
            state.OnEntryAction,
            state.OnExitAction,
            state.ActivityAction,
            state.TimerAction,
        };
        foreach (var fqn in fqns)
            if (IsWritingFqn(fqn)) return true;

        // Either no FQNs (zero actions) or all known FQNs are ReadOnly -> not a writer.
        return false;
    }

    /// <summary>
    /// ⭐ <c>W7c</c> — §9.6's classification for ONE action FQN, extracted so the alias walk and the
    /// locally-bound walk share it. ⛔ <b>Extracted, not re-derived</b>: the conservative arms (null
    /// schema · unknown FQN · any non-<c>ReadOnly</c> access) are the design's, and a second copy is
    /// how the two styles would come to disagree about what a writer is.
    ///
    /// <para>⚠ A <c>null</c> FQN is <b>not</b> a writer — that is "no action here", not "unknown".</para>
    /// </summary>
    private bool IsWritingFqn(string? fqn)
    {
        if (fqn == null) return false;
        if (_schema == null) return true;                            // no schema -> conservative
        var entry = _schema.Lookup(fqn);
        if (entry == null) return true;                              // unknown -> conservative
        return entry.Access != BlackboardAccess.ReadOnly;            // non-ReadOnly -> writer
    }

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
