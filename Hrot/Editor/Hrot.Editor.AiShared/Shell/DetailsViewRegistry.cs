using System;
using System.Collections.Generic;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L1.1</c> — THE CATALOGUE OF DETAILS VIEWS for one perspective.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §2's <c>classDiagram</c> — <c>Add</c>,
/// <c>OfferSet</c>, <c>Default</c> are that diagram's three members.
///
/// <para>⭐⭐ <b>It decides AVAILABILITY, never CONTENT.</b> 📌 <c>R-116</c>: the predicate ships with
/// the view, so this type never learns what a Blueprint node or a variable row is. ⛔ 📌 <c>R-112</c>:
/// <b><c>AssetKind</c> is never a view key</b> — that is precisely the mistake
/// <c>RuntimeInspectorWindow</c>'s <c>_panes.Find(p =&gt; p.TargetKind == asset.Kind)</c> made, and why
/// §4 dissolves it.</para>
///
/// <para>⭐ <b>Descriptors, not instances</b> *(<c>R-120</c>)* — this hands back what COULD be shown;
/// the window composes its own instance from <see cref="DetailsViewDescriptor.Create"/>.
/// ⇒ ⛔ nothing to arbitrate between two windows.</para>
///
/// <para>⚠ <b>Not thread-safe, deliberately.</b> Registration happens at composition time and reads
/// happen on the UI thread — ⛔ a lock here would buy nothing and suggest a concurrency story that does
/// not exist.</para>
/// </summary>
public sealed class DetailsViewRegistry
{
    private readonly List<DetailsViewDescriptor> _descriptors = new();

    /// <summary>⭐ Every registered descriptor, in registration order. ⚠ Exposed so a rail can assert
    /// what the PRODUCTION composition actually registered — 📌 <c>R-67</c>: a rail that builds its own
    /// registry cannot see a registration defect.</summary>
    public IReadOnlyList<DetailsViewDescriptor> All => _descriptors;

    /// <summary>
    /// ⭐ Add a view to this perspective's catalogue.
    ///
    /// <para>⛔⛔ <b>A duplicate <see cref="DetailsViewDescriptor.Id"/> THROWS.</b> ⚠ Not "last wins"
    /// and not "first wins": both are silent, and the symptom would be a view that mysteriously never
    /// appears. 📌 The same reasoning as <c>G4</c>'s duplicate-name guard on <c>BehaviorRegistry</c> —
    /// an id collision is a wiring bug, and it should fail where the wiring is.</para>
    /// </summary>
    public void Add(DetailsViewDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        // ⭐ The descriptor's own guards live HERE rather than in its constructor: a positional record
        //   cannot carry a validation block, and ⭐⭐ this is the better home anyway — a null predicate
        //   is a WIRING bug, and it should fail where the wiring is, not on the first frame that draws.
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Id,        nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Title,     nameof(descriptor));
        ArgumentNullException.ThrowIfNull(descriptor.AppliesTo,         nameof(descriptor));
        ArgumentNullException.ThrowIfNull(descriptor.Create,            nameof(descriptor));

        foreach (var existing in _descriptors)
        {
            if (!string.Equals(existing.Id, descriptor.Id, StringComparison.Ordinal)) continue;
            throw new InvalidOperationException(
                $"A details view with id '{descriptor.Id}' is already registered "
                + $"('{existing.Title}'). Ids must be unique within a perspective, or one view would "
                + "silently shadow the other.");
        }

        _descriptors.Add(descriptor);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Which views are about THIS context — highest <see cref="DetailsViewDescriptor.Rank"/>
    /// first.</b> 📄 §2b's first sequence: the shell calls this, loops the predicates, and may get
    /// <b>an empty list</b>.
    ///
    /// <para>⭐⭐ <b>EMPTY IS A REAL, EXPECTED ANSWER</b> — 📌 <c>R-117</c>: <i>"a blank panel is a
    /// defect"</i>, and the answer to an empty offer set is the GREY LINE, not a blank. ⛔ Callers must
    /// not treat empty as an error; §2b draws it as <i>"intentionally empty for the current
    /// selection"</i>.</para>
    ///
    /// <para>⚠ <b>Ties keep registration order</b> — the sort is stable, so two views of equal rank
    /// resolve deterministically instead of by list-internals. ⛔ A designer must not see the default
    /// flip between runs.</para>
    /// </summary>
    public IReadOnlyList<DetailsViewDescriptor> OfferSet(DetailsContext context) => Evaluate(context).Offered;

    /// <summary>
    /// ⭐⭐ B-11 A7 — a tab that applies to the selection but is shown GREYED, with the reason (a mode that greys it, a seat below its level, a host
    /// that does not bind what it needs). Hidden tabs are in neither list.
    /// </summary>
    public readonly record struct GreyedTab(DetailsViewDescriptor View, string Reason);

    /// <summary>⭐ B-11 A7 — the registry's answer for one context: the tabs offered (<c>Rank</c>-ordered) and the tabs greyed (same order).</summary>
    public readonly record struct Offer(IReadOnlyList<DetailsViewDescriptor> Offered, IReadOnlyList<GreyedTab> Greyed);

    private static readonly IReadOnlyList<GreyedTab> NoGreyed = Array.Empty<GreyedTab>();

    /// <summary>
    /// ⭐⭐ B-11 A7 — how the registry asks whether a selected entity carries a component (AND over the selection, <see cref="DetailsViewDescriptor.Requires"/>).
    /// Set by the host's bundle from its world. ⚠ Null ⇒ the registry cannot ask, so a tab that declares a requirement is not offered.
    /// </summary>
    public Func<Fdp.Core.Entity, Type, bool>? ComponentProbe { get; set; }

    /// <summary>
    /// ⭐⭐⭐ B-11 A7 — <b>the ONE evaluation of a tab's offer</b>: <c>AppliesTo</c> (the view's own predicate) → <c>Requires</c> (AND over the selection)
    /// → <c>Availability</c> (the station's <c>EditingMode</c> and seat level, from the context) → <c>Bound</c> (does this host bind what the tab needs).
    /// A tab that fails the first two is not about this selection (hidden); the last two can only GREY it. Same for every host, because every host
    /// registers the same descriptors in <c>EntityDetailsBundle</c>.
    /// <para>⚠ Cheap: plain loops over the descriptor list and the selection, constant reasons — the only allocations are the result lists, and the
    /// greyed list exists only when something is greyed.</para>
    /// </summary>
    public Offer Evaluate(DetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<DetailsViewDescriptor>? offered = null;
        List<GreyedTab>? greyed = null;
        for (int i = 0; i < _descriptors.Count; i++)
        {
            var d = _descriptors[i];
            // ⛔ Deliberately UNGUARDED: a predicate that throws is a defect in that view, and
            //    swallowing it here would turn a broken view into a silently missing one.
            if (!d.AppliesTo(context)) continue;
            if (d.Requires.Count > 0 && !RequirementsMet(d, context)) continue;
            var verdict = d.Availability.Evaluate(context.Station, context.Level);
            if (verdict.State == Hrot.Common.Actions.ActionOffer.Hidden) continue;
            if (verdict.State == Hrot.Common.Actions.ActionOffer.Offered && d.Bound is { } bound && !bound())
                verdict = new Hrot.Common.Actions.AvailabilityVerdict(Hrot.Common.Actions.ActionOffer.Greyed, DetailsViewDescriptor.NotBoundReason);
            if (verdict.State == Hrot.Common.Actions.ActionOffer.Offered) (offered ??= new List<DetailsViewDescriptor>(_descriptors.Count)).Add(d);   // ⭐ sized once: no growth copies
            else (greyed ??= new List<GreyedTab>()).Add(new GreyedTab(d, verdict.Reason ?? DetailsViewDescriptor.NotBoundReason));
        }

        if (offered is null && greyed is null) return new Offer(Array.Empty<DetailsViewDescriptor>(), NoGreyed);

        // ⛔⛔ NOT List.Sort — it is INTROSORT and therefore UNSTABLE, so equal ranks would come back in an order that depends on list internals and
        //    could differ between runs. ⚠ That is exactly the flip a designer must never see. ⭐ A stable insertion sort (a handful of tabs; no LINQ).
        if (offered != null)
            for (int i = 1; i < offered.Count; i++)
            {
                var x = offered[i]; int j = i - 1;
                while (j >= 0 && offered[j].Rank < x.Rank) { offered[j + 1] = offered[j]; j--; }
                offered[j + 1] = x;
            }
        if (greyed != null)
            for (int i = 1; i < greyed.Count; i++)
            {
                var x = greyed[i]; int j = i - 1;
                while (j >= 0 && greyed[j].View.Rank < x.View.Rank) { greyed[j + 1] = greyed[j]; j--; }
                greyed[j + 1] = x;
            }
        return new Offer(offered ?? (IReadOnlyList<DetailsViewDescriptor>)Array.Empty<DetailsViewDescriptor>(), greyed ?? NoGreyed);
    }

    /// <summary>
    /// ⭐ B-11 A7 — the standard <see cref="ComponentProbe"/> over a host's world (re-asked per call: a replay swaps the world). Allocation-free:
    /// the type's registry id, then one bit of the entity's component mask. An unregistered type, a dead entity or no world answers false.
    /// </summary>
    public static Func<Fdp.Core.Entity, Type, bool> ProbeFor(Func<Fdp.Core.EntityRepository?> world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return (entity, type) =>
        {
            if (world() is not { } w) return false;
            int id = Fdp.Core.ComponentTypeRegistry.GetId(type);
            return id >= 0 && w.HasComponentByTypeId(entity, id);
        };
    }

    /// <summary>⭐ AND over the selection: every selected entity carries every required component (an empty selection carries nothing).</summary>
    private bool RequirementsMet(DetailsViewDescriptor d, DetailsContext context)
    {
        if (ComponentProbe is not { } probe || context.Entities.Count == 0) return false;
        for (int e = 0; e < context.Entities.Count; e++)
            if (!Hrot.Core.Tkb.ComponentRequirements.Met(probe, context.Entities[e], d.Requires)) return false;   // ⭐ CE-4350 — the ONE AND (behaviours' Requires ask it too)
        return true;
    }

    /// <summary>
    /// ⭐ <b>What to show when the designer has expressed no preference</b> — 📌 <c>R-98</c>: the
    /// highest <see cref="DetailsViewDescriptor.Rank"/> that applies.
    ///
    /// <para>⚠ <b>Nullable, and that is the honest signature</b> — §2b's <c>stateDiagram</c> has an
    /// explicit <c>EmptyOffer</c> state. ⛔ Returning some fallback descriptor would be a view that
    /// claims to be about a context it rejected.</para>
    /// </summary>
    public DetailsViewDescriptor? Default(DetailsContext context)
    {
        var offered = OfferSet(context);
        return offered.Count == 0 ? null : offered[0];
    }
}
