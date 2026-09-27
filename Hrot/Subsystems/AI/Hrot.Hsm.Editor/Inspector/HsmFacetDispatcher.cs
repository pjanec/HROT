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
    {
        _asset      = asset      ?? throw new ArgumentNullException(nameof(asset));
        _fqnContext = fqnContext;
        _catalog    = catalog;
        _mapper     = new HsmFacetMapper(asset, fqnContext);
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
        s.OnEntryAction  = f.OnEntryAction;
        s.OnExitAction   = f.OnExitAction;
        s.ActivityAction = f.ActivityAction;
        s.TimerAction    = f.TimerAction;
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
        if (string.IsNullOrWhiteSpace(f.ActivityBlueprintName))
        {
            s.ActivityBlueprintName    = null;
            s.ActivityBlueprintAssetId = Guid.Empty;
        }
        else
        {
            s.ActivityBlueprintName    = f.ActivityBlueprintName;
            s.ActivityBlueprintAssetId = ResolvePickedAssetId(
                f.ActivityBlueprintName,
                Hrot.Editor.AiShared.AssetKind.Blueprint,
                s.ActivityBlueprintAssetId).Id;
        }

        _asset.MarkDirty();
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
            s.SubtreeName       = null;
            s.SubtreeAssetId    = Guid.Empty;
            s.IsSubtreeResolved = false;
            return;
        }

        s.SubtreeName = pickedName;
        (s.SubtreeAssetId, s.IsSubtreeResolved) =
            ResolvePickedAssetId(pickedName, Hrot.Editor.AiShared.AssetKind.BTree, s.SubtreeAssetId);
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
    {
        var picked = _catalog?.FindByName(pickedName);
        return picked != null && picked.Kind == kind
            ? (picked.AssetId, true)
            : (currentId, false);
    }

    private void ApplyTransitionFacet(Guid visualId, TransitionFacet f)
    {
        var t = _asset.FindTransitionByVisualId(visualId);
        if (t is null) return;

        t.EventId               = f.EventId;
        t.GuardFunction         = f.GuardFunction;
        t.IsPolled              = f.IsPolled;             // CE-381
        t.ActionFunction        = f.ActionFunction;
        t.ExpressionTargetField = f.ExpressionTargetField;
        t.Priority              = f.Priority;
        t.Kind                  = f.Kind;
        t.SyncGroupId           = f.SyncGroupId;
        t.Comment               = f.Comment;
        t.IsBreakpoint          = f.IsBreakpoint;

        // ⭐⭐ CE-385 — the blueprint-hosted guard, same pick rule as everything else.
        if (string.IsNullOrWhiteSpace(f.GuardBlueprintName))
        {
            t.GuardBlueprintName    = null;
            t.GuardBlueprintAssetId = Guid.Empty;
        }
        else
        {
            t.GuardBlueprintName    = f.GuardBlueprintName;
            t.GuardBlueprintAssetId = ResolvePickedAssetId(
                f.GuardBlueprintName,
                Hrot.Editor.AiShared.AssetKind.Blueprint,
                t.GuardBlueprintAssetId).Id;
        }

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

        g.GuardFunction         = f.GuardFunction;
        g.ActionFunction        = f.ActionFunction;
        g.ExpressionTargetField = f.ExpressionTargetField;
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
