using System;
using System.Linq;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Inspector.ActionBinding;
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

    /// <summary>⭐ §11.1a — needed to turn a PICKED NAME into the stable Guid at the moment of the
    /// pick. ⚠ Optional: a headless fixture may have no catalogue, and then only the name is
    /// written. ⛔ A production host has one and must pass it.</summary>
    private readonly Hrot.Editor.AiShared.Catalog.IAssetCatalog? _catalog;

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-414</c> — what turns a picked BLUEPRINT NAME into its generated <c>Params</c>
    /// type, so the pick can compose a struct-typed blackboard variable instead of leaving the author
    /// to hand-mirror a byte layout.</b>
    /// ⚠ Optional for the same reason <see cref="_catalog"/> is: a headless fixture may have none, and
    /// then the pick composes nothing. ⛔ A production host HAS one and must pass it.
    /// </summary>
    private readonly Hrot.Editor.AiShared.Blackboard.IActionSchemaExporter? _actionSchema;

    /// <summary>
    /// ⭐⭐⭐ The production constructor (<c>AiFacetPickerBinder</c>). <paramref name="catalog"/> turns a picked subtree or
    /// blueprint NAME into its Guid at pick time; <paramref name="actionSchema"/> lets a blueprint pick COMPOSE its params
    /// variable (<c>CE-414</c>). ⚠ Both optional for headless fixtures; ⛔ a host that has them must pass them.
    /// <para>⭐ <c>CE-417</c> slice 4b: no <c>HsmFacetFqnContext</c> — every binding facet carries its own method.</para>
    /// </summary>
    public HsmFacetDispatcher(
        HsmAsset asset,
        Hrot.Editor.AiShared.Catalog.IAssetCatalog? catalog = null,
        Hrot.Editor.AiShared.Blackboard.IActionSchemaExporter? actionSchema = null)
    {
        _asset         = asset ?? throw new ArgumentNullException(nameof(asset));
        _catalog       = catalog;
        _actionSchema  = actionSchema;
        _mapper        = new HsmFacetMapper(asset);
    }

    // ── IFacetDispatcher ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public object? GetFacet(IAssetSubSelection subSelection)
    {
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
        s.Comment        = f.Comment;
        s.IsBreakpoint   = f.IsBreakpoint;
        s.DeferredEventIds.Clear();
        if (f.DeferredEventIds is not null)
            s.DeferredEventIds.AddRange(f.DeferredEventIds);

        // ⭐⭐⭐ §11.1a — THE GUID IS CAPTURED AT PICK TIME, while the catalogue entry is in hand.
        // ⛔ Deriving it only on load would mean a rename between the pick and the first reload
        //    leaves NOTHING to heal from — and the whole point of storing both would be lost.
        ApplySubtreePick(s, f.SubtreeName);

        // ⭐⭐⭐ CE-417 slice 4b — every slot through THE shared applier, each with its own variable (B-2). The activity
        //   blueprint pick captures its Guid and COMPOSES its params variable there (CE-385 / CE-414), exactly as before.
        s.OnEntry  = BehaviorActionBindingEditor.Apply(s.OnEntry,  f.OnEntry,  ApplyContext(composeBaseName: null));
        s.OnExit   = BehaviorActionBindingEditor.Apply(s.OnExit,   f.OnExit,   ApplyContext(composeBaseName: null));
        s.Activity = BehaviorActionBindingEditor.Apply(s.Activity, f.Activity, ApplyContext("bpActivityParams"));
        s.Timer    = BehaviorActionBindingEditor.Apply(s.Timer,    f.Timer,    ApplyContext(composeBaseName: null));

        _asset.MarkDirty();
    }

    /// <summary>The HSM apply rules: an empty slot is unbound; only a blueprint-capable slot composes (<c>CE-414</c>).</summary>
    private ActionBindingApplyContext ApplyContext(string? composeBaseName)
        => new(_asset, _catalog, _actionSchema, composeBaseName, KeepWhenEmpty: false);

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
    /// pick time".</b> The subtree pick (§11.1a) calls it here; the blueprint picks call the same shared
    /// <c>ResolvePick</c> through <c>BehaviorActionBindingEditor</c> (<c>CE-417</c> slice 4b), because several
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
        t.IsPolled              = f.IsPolled;             // CE-381
        t.Priority              = f.Priority;
        t.Kind                  = f.Kind;
        t.SyncGroupId           = f.SyncGroupId;
        t.Comment               = f.Comment;
        t.IsBreakpoint          = f.IsBreakpoint;

        // ⭐⭐⭐ CE-417 slice 4b — guard and action through THE shared applier, each with its OWN variable (B-2).
        //   The guard blueprint composes its own params variable (CE-414 / CE-413): the kernel stamps a polled guard with
        //   its SOURCE STATE, so before CE-414 this variable and the source state's activity variable were the same bytes
        //   read through two different Params types.
        t.Guard  = BehaviorActionBindingEditor.Apply(t.Guard,  f.Guard,  ApplyContext("bpGuardParams"));
        t.Action = BehaviorActionBindingEditor.Apply(t.Action, f.Action, ApplyContext(composeBaseName: null));

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

        // ⭐ CE-417 slice 4b — the same two bindings as a transition (B-3); no blueprint on a global transition.
        g.Guard  = BehaviorActionBindingEditor.Apply(g.Guard,  f.Guard,  ApplyContext(composeBaseName: null));
        g.Action = BehaviorActionBindingEditor.Apply(g.Action, f.Action, ApplyContext(composeBaseName: null));
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
