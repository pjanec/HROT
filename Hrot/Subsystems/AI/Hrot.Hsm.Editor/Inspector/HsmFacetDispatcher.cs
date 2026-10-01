using System;
using System.Linq;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Selection;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Inspector;

/// <summary>
/// Implements <see cref="IFacetDispatcher"/> for the HSM perspective.
/// Delegates read access to the existing <see cref="HsmFacetMapper"/> and applies
/// edited facets back to the <see cref="HsmAsset"/> model, marking it dirty.
/// Constructed per open asset from the composition root.
/// </summary>
public sealed class HsmFacetDispatcher : IFacetDispatcher
{
    private readonly HsmAsset            _asset;
    private readonly HsmFacetMapper      _mapper;
    private readonly HsmFacetFqnContext? _fqnContext;

    /// <summary>⭐ §11.1a — needed to turn a PICKED NAME into the stable Guid at the moment of the
    /// pick. ⚠ Optional: a headless fixture may have no catalogue, and then only the name is
    /// written. ⛔ A production host has one and must pass it.</summary>
    private readonly Hrot.Editor.AiShared.Catalog.IAssetCatalog? _catalog;

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-414</c> — what turns a picked BLUEPRINT NAME into its generated <c>Params</c>
    /// type, so the pick can compose a struct-typed blackboard variable instead of leaving the author
    /// to hand-mirror a byte layout.</b>
    /// ⚠ Optional for the same reason <see cref="_catalog"/> is: a headless fixture may have none, and
    /// then the pick behaves exactly as it did before. ⛔ A production host HAS one and must pass it.
    /// </summary>
    private readonly Hrot.Editor.AiShared.Blackboard.IActionSchemaExporter? _actionSchema;

    public HsmFacetDispatcher(HsmAsset asset)
        : this(asset, null)
    {
    }

    /// <summary>
    /// Constructs a dispatcher that shares <paramref name="fqnContext"/> with the
    /// <see cref="HsmFacetMapper"/> so the blackboard-field picker drawer can read the
    /// current transition action FQN.
    /// </summary>
    public HsmFacetDispatcher(HsmAsset asset, HsmFacetFqnContext? fqnContext)
        : this(asset, fqnContext, catalog: null)
    {
    }

    /// <summary>
    /// ⭐⭐ §11.1a — the production overload. <paramref name="catalog"/> is what turns a PICKED
    /// SUBTREE NAME into the stable Guid at pick time.
    /// ⛔ A host that has a catalogue must use THIS constructor; the two above exist for headless
    /// fixtures, and a dispatcher without one writes the name only.
    /// </summary>
    public HsmFacetDispatcher(
        HsmAsset asset,
        HsmFacetFqnContext? fqnContext,
        Hrot.Editor.AiShared.Catalog.IAssetCatalog? catalog)
        : this(asset, fqnContext, catalog, actionSchema: null)
    {
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-414</c> — the production overload. <paramref name="actionSchema"/> is what lets a
    /// blueprint pick COMPOSE its params variable.</b>
    ///
    /// <para>🔒 Without it the pick writes the blueprint's name and id and stops, which is what it did
    /// before <c>CE-414</c> — and left the author to declare scalar variables whose packed layout had
    /// to coincide, field for field, with the blueprint's generated <c>Params</c> struct.</para>
    /// </summary>
    public HsmFacetDispatcher(
        HsmAsset asset,
        HsmFacetFqnContext? fqnContext,
        Hrot.Editor.AiShared.Catalog.IAssetCatalog? catalog,
        Hrot.Editor.AiShared.Blackboard.IActionSchemaExporter? actionSchema)
    {
        _asset         = asset ?? throw new ArgumentNullException(nameof(asset));
        _fqnContext    = fqnContext;
        _catalog       = catalog;
        _actionSchema  = actionSchema;
        _mapper        = new HsmFacetMapper(asset, fqnContext);
    }

    // ── IFacetDispatcher ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public object? GetFacet(IAssetSubSelection subSelection)
    {
        // Clear the FQN context for non-transition selections so the blackboard picker
        // shows all variables when a state, region, or event is selected.
        if (_fqnContext is not null &&
            subSelection is not HsmTransitionSelection &&
            subSelection is not HsmGlobalTransitionSelection)
        {
            _fqnContext.CurrentActionFqn = null;
            _fqnContext.CurrentVisualId  = null;
        }

        return subSelection switch
        {
            HsmStateSelection st              => _mapper.GetStateFacet(st.StableId),
            HsmTransitionSelection tr         => _mapper.GetTransitionFacet(tr.VisualId),
            HsmRegionSelection rg             => _mapper.GetRegionFacet(rg.StableId, rg.RegionIndex),
            HsmEventSelection ev              => _mapper.GetEventFacet(ev.EventId),
            HsmGlobalTransitionSelection gt   => _mapper.GetGlobalTransitionFacet(gt.VisualId),
            _                                 => null,
        };
    }

    /// <inheritdoc/>
    public void ApplyFacet(IAssetSubSelection subSelection, object facet)
    {
        switch (subSelection, facet)
        {
            case (HsmStateSelection st, StateFacet sf):
                ApplyStateFacet(st.StableId, sf);
                break;

            case (HsmTransitionSelection tr, TransitionFacet tf):
                ApplyTransitionFacet(tr.VisualId, tf);
                break;

            case (HsmRegionSelection rg, RegionFacet rf):
                ApplyRegionFacet(rg.StableId, rg.RegionIndex, rf);
                break;

            case (HsmEventSelection ev, EventFacet ef):
                ApplyEventFacet(ev.EventId, ef);
                break;

            case (HsmGlobalTransitionSelection gt, GlobalTransitionFacet gtf):
                ApplyGlobalTransitionFacet(gt.VisualId, gtf);
                break;
        }
    }

    // ── Private appliers ─────────────────────────────────────────────────────

    private void ApplyStateFacet(Guid stableId, StateFacet f)
    {
        var s = _asset.FindStateByStableId(stableId);
        if (s is null) return;

        s.Name           = f.Name;
        // ⭐ CE-417 (slice 4a): the facet is still flat; the slots are bindings (HsmFacetBindings — retired by 4b).
        s.OnEntry        = HsmFacetBindings.WithMethod(s.OnEntry,  f.OnEntryAction);
        s.OnExit         = HsmFacetBindings.WithMethod(s.OnExit,   f.OnExitAction);
        s.Activity       = HsmFacetBindings.WithMethod(s.Activity, f.ActivityAction);
        s.Timer          = HsmFacetBindings.WithMethod(s.Timer,    f.TimerAction);
        s.Comment        = f.Comment;
        s.IsBreakpoint   = f.IsBreakpoint;
        s.DeferredEventIds.Clear();
        if (f.DeferredEventIds is not null)
            s.DeferredEventIds.AddRange(f.DeferredEventIds);

        // ⭐⭐⭐ §11.1a — THE GUID IS CAPTURED AT PICK TIME, while the catalogue entry is in hand.
        // ⛔ Deriving it only on load would mean a rename between the pick and the first reload
        //    leaves NOTHING to heal from — and the whole point of storing both would be lost.
        // ⚠ `_catalog` is optional so a headless fixture need not supply one; a production host
        //   HAS one and passes it (the silent-default rule).
        ApplySubtreePick(s, f.SubtreeName);

        // ⭐⭐ CE-385 — the blueprint-hosted activity, captured by the SAME rule as the subtree pick.
        // 📄 DESIGN_Hsm_Blueprint_Behaviour_Authoring.md §3.2.
        string? previousActivity = s.Activity?.BlueprintName;
        s.Activity = HsmFacetBindings.WithBlueprint(
            s.Activity, f.ActivityBlueprintName,
            string.IsNullOrWhiteSpace(f.ActivityBlueprintName)
                ? Guid.Empty
                : ResolvePickedAssetId(f.ActivityBlueprintName!, Hrot.Editor.AiShared.AssetKind.Blueprint,
                                       s.Activity?.BlueprintAssetId ?? Guid.Empty).Id);

        // ⭐ CE-387 — the state's ONE seed field, written by the slice-2 rule (every bound slot; else an Activity that names nothing).
        s.OnEntry  = HsmFacetBindings.DropIfEmpty(s.OnEntry);
        s.OnExit   = HsmFacetBindings.DropIfEmpty(s.OnExit);
        s.Activity = HsmFacetBindings.DropIfEmpty(s.Activity);
        s.Timer    = HsmFacetBindings.DropIfEmpty(s.Timer);
        s.StateWideTargetField = f.ExpressionTargetField;

        // ⭐⭐⭐ CE-414 — COMPOSE. The state's ExpressionTargetField becomes ONE variable whose TYPE is
        //   the picked blueprint's generated Params struct.
        ComposeBlueprintParams(
            previousActivity, s.Activity?.BlueprintName, "bpActivityParams",
            () => s.StateWideTargetField, v => s.StateWideTargetField = v);

        _asset.MarkDirty();
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-414</c> — THE COMPOSE STEP: a picked blueprint brings its own params variable.</b>
    /// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §28.6c ·
    /// <c>DESIGN_Hsm_Blueprint_Behaviour_Authoring.md</c> §3.2.
    ///
    /// <para>🔒 <b>This is the step the HSM authoring path was missing.</b> The BTree editor has had it
    /// since <c>E2</c> — <c>BTreeCommandSink.ComposeAiPrimitiveAction</c> auto-creates ONE
    /// <c>IsAutoManaged</c> variable typed from the blueprint's generated <c>Params</c> and points
    /// <c>ExpressionTargetField</c> at it (see <c>T33_ComposedParamBlueprint.btree.json</c>). ⛔ Picking
    /// a blueprint on an HSM state wrote the name and the id and stopped, so the author had to declare
    /// SCALAR variables whose packed layout coincided, field for field and pad for pad, with the
    /// struct — order load-bearing, and nothing checking it.</para>
    ///
    /// <para>⭐⭐ <b>With one struct-typed variable there is nothing left to check.</b> The seed's byte
    /// offset is that variable's offset and every field offset inside it comes from the DTO, which is
    /// what <c>DESIGN_Parameter_Model.md</c> §4 means by <i>"the compiler owns the layout"</i>.</para>
    ///
    /// <para>⚠ <b>Runs only on a CHANGE of pick.</b> A facet apply round-trips the current value on
    /// every inspector edit, so composing unconditionally would churn a fresh variable per keystroke.
    /// ⛔ And it never touches a variable the author owns — only one this step marked
    /// <c>IsAutoManaged</c>.</para>
    /// </summary>
    private void ComposeBlueprintParams(
        string? previousName,
        string? pickedName,
        string baseVariableName,
        Func<string?> getTargetField,
        Action<string?> setTargetField)
    {
        if (string.Equals(previousName, pickedName, StringComparison.Ordinal)) return;

        // ⭐ Drop the OUTGOING pick's variable first — on a re-pick as well as on a clear, because the
        //   new blueprint's Params is a different type and reusing the row would mis-type the seed.
        //   ⭐⭐ SHARED rule: only a variable the EDITOR owns is removed.
        if (Hrot.Editor.AiShared.Blackboard.AutoManagedVariables
                .RemoveIfAutoManaged(_asset, getTargetField()))
            setTargetField(null);

        if (string.IsNullOrWhiteSpace(pickedName)) return;

        // ⛔ No exporter (headless fixture) or no matching AiPrimitive ⇒ leave the site unbound. That
        //   is the pre-CE-414 behaviour, not a corruption — and a parameterless blueprint legitimately
        //   has nothing to bind.
        if (!Hrot.Editor.AiShared.Blackboard.AiPrimitiveNaming.TryFindAiPrimitiveByName(
                _actionSchema, pickedName, out var entry))
            return;

        // ⭐⭐⭐ THE SAME COMPOSE THE BTree HOST CALLS — AutoManagedVariables.ComposeForAiPrimitive.
        //   ⚠ workingStateBaseName: null is the ONE deliberate difference, and it is not an omission:
        //     an HSM-hosted occurrence's working state lives in its occurrence slot, keyed
        //     (machine, region, state, childAsset), so there is no variable to bind. The BTree host
        //     binds one so its Scope can be widened to Behavior and two nodes can share a slot.
        var composed = Hrot.Editor.AiShared.Blackboard.AutoManagedVariables.ComposeForAiPrimitive(
            _asset, entry, paramsBaseName: baseVariableName, workingStateBaseName: null);

        setTargetField(composed.ParamsVariable);
    }


    /// <summary>
    /// ⭐⭐ Writes a picked subtree name onto the state and captures the matching asset id.
    /// 📄 <c>HSM_Editor_NodeEditor_Host_Design.md</c> §11.1a.
    /// </summary>
    private void ApplySubtreePick(Hrot.Hsm.Editor.Model.StateNode s, string? pickedName)
    {
        // ⭐ Clearing the field UNSETS the host entirely — both halves go, because an empty name
        //   with a live Guid would be a reference the designer cannot see or edit.
        if (string.IsNullOrWhiteSpace(pickedName))
        {
            s.SubtreeName           = null;
            s.SubtreeAssetId        = Guid.Empty;
            s.IsSubtreeResolved     = false;
            s.SubtreeParamsVariable = null;   // CE-439: no child, no binding (the variable stays — no rush removals)
            return;
        }

        bool changed = !string.Equals(s.SubtreeName, pickedName, StringComparison.Ordinal);
        s.SubtreeName = pickedName;
        (s.SubtreeAssetId, s.IsSubtreeResolved) =
            ResolvePickedAssetId(pickedName, Hrot.Editor.AiShared.AssetKind.BTree, s.SubtreeAssetId);

        // ⭐⭐ CE-439 (Q76 §12.28) — a NEWLY picked child brings its params variable (the shared compose step). ⚠ Only on a
        //   change: this runs on every facet apply, and re-composing an unchanged pick would resurrect a variable the
        //   author deleted.
        if (changed)
            s.SubtreeParamsVariable = Hrot.Editor.AiShared.Blackboard.AutoManagedVariables.ComposeForSubtree(
                _asset, pickedName!,
                (_catalog?.FindByAssetId(s.SubtreeAssetId) as Hrot.Editor.AiShared.IBehaviorInputsContract)?.InputsTypeId);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-385</c> — the ONE rule for "a picked catalogue NAME becomes a stable Guid, at
    /// pick time".</b> Shared by the subtree pick (§11.1a) and both blueprint picks, because three
    /// spellings of one rule is how the never-erase branch quietly stops being true in one of them.
    ///
    /// <para>⭐⭐ <b>The Guid is captured while the catalogue entry is in hand.</b> ⛔ Deriving it
    /// only on load would mean a rename between the pick and the first reload leaves NOTHING to heal
    /// from.</para>
    ///
    /// <para>⚠ <b>The never-erase branch (§7.1a ③):</b> a typed or stale name with no catalogue
    /// match keeps <paramref name="currentId"/> — ⛔ a missing catalogue (headless fixture) must
    /// never destroy a reference it simply cannot see.</para>
    /// </summary>
    private (Guid Id, bool Resolved) ResolvePickedAssetId(
        string pickedName, Hrot.Editor.AiShared.AssetKind kind, Guid currentId)
        => Hrot.Editor.AiShared.References.SubtreeReferenceResolver.ResolvePick(_catalog, pickedName, kind, currentId);   // CE-439: shared

    private void ApplyTransitionFacet(Guid visualId, TransitionFacet f)
    {
        var t = _asset.FindTransitionByVisualId(visualId);
        if (t is null) return;

        t.EventId               = f.EventId;
        t.Guard                 = HsmFacetBindings.WithMethod(t.Guard, f.GuardFunction);   // CE-417 (4a)
        t.IsPolled              = f.IsPolled;             // CE-381
        t.Action                = HsmFacetBindings.WithMethod(t.Action, f.ActionFunction);
        t.Priority              = f.Priority;
        t.Kind                  = f.Kind;
        t.SyncGroupId           = f.SyncGroupId;
        t.Comment               = f.Comment;
        t.IsBreakpoint          = f.IsBreakpoint;

        // ⭐⭐ CE-385 — the blueprint-hosted guard, same pick rule as everything else.
        string? previousGuard = t.Guard?.BlueprintName;
        t.Guard = HsmFacetBindings.WithBlueprint(
            t.Guard, f.GuardBlueprintName,
            string.IsNullOrWhiteSpace(f.GuardBlueprintName)
                ? Guid.Empty
                : ResolvePickedAssetId(f.GuardBlueprintName!, Hrot.Editor.AiShared.AssetKind.Blueprint,
                                       t.Guard?.BlueprintAssetId ?? Guid.Empty).Id);
        HsmFacetBindings.SetTransitionTargetField(ref t.Guard, ref t.Action, f.ExpressionTargetField);

        // ⭐⭐⭐ CE-414 / CE-413 — COMPOSE the guard's own params variable.
        //
        //   🔴 This is the case that made the SITE necessary at all: the kernel stamps a polled guard
        //      with its SOURCE STATE, so before CE-414 this variable and the source state's activity
        //      variable were the same bytes read through two different Params types.
        ComposeBlueprintParams(
            previousGuard, t.Guard?.BlueprintName, "bpGuardParams",
            () => HsmFacetBindings.TransitionTargetField(t.Guard, t.Action),
            v => HsmFacetBindings.SetTransitionTargetField(ref t.Guard, ref t.Action, v));

        // TargetStateName: find the state by name and rewire.
        if (!string.IsNullOrWhiteSpace(f.TargetStateName))
        {
            var target = _asset.AllStates.FirstOrDefault(s => s.Name == f.TargetStateName);
            if (target is not null) t.Target = target;
        }

        _asset.MarkDirty();
    }

    private void ApplyRegionFacet(Guid parentStableId, int regionIndex, RegionFacet f)
    {
        var parent = _asset.FindStateByStableId(parentStableId);
        if (parent is null) return;
        var r = parent.RegionNodes.FirstOrDefault(x => x.RegionIndex == regionIndex);
        if (r is null) return;

        r.Name         = f.Name;
        r.Priority     = f.Priority;
        r.Comment      = f.Comment;
        r.ColorOverride = f.ColorOverride;

        // InitialChildName: rewire the initial child.
        if (!string.IsNullOrWhiteSpace(f.InitialChildName))
        {
            var child = parent.Children.FirstOrDefault(c => c.Name == f.InitialChildName);
            if (child is not null) r.InitialChild = child;
        }
        else
        {
            r.InitialChild = null;
        }

        _asset.MarkDirty();
    }

    private void ApplyEventFacet(ushort eventId, EventFacet f)
    {
        var e = _asset.FindEventById(eventId);
        if (e is null) return;

        e.Name        = f.Name;
        e.PayloadSize = f.PayloadSize;
        e.IsIndirect  = f.IsIndirect;
        // Priority is stub — EventDefinition doesn't store it yet.

        _asset.MarkDirty();
    }

    private void ApplyGlobalTransitionFacet(Guid visualId, GlobalTransitionFacet f)
    {
        var g = _asset.AllGlobalTransitions.FirstOrDefault(x => x.VisualId == visualId);
        if (g is null) return;

        g.Guard                 = HsmFacetBindings.WithMethod(g.Guard, f.GuardFunction);   // CE-417 (4a)
        g.Action                = HsmFacetBindings.WithMethod(g.Action, f.ActionFunction);
        HsmFacetBindings.SetTransitionTargetField(ref g.Guard, ref g.Action, f.ExpressionTargetField);
        g.Priority              = f.Priority;
        g.Comment               = f.Comment;

        // TargetStateName: find state by name.
        if (!string.IsNullOrWhiteSpace(f.TargetStateName))
        {
            var target = _asset.AllStates.FirstOrDefault(s => s.Name == f.TargetStateName);
            if (target is not null) g.Target = target;
        }

        _asset.MarkDirty();
    }
}
