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
    public IReadOnlyList<DetailsViewDescriptor> OfferSet(DetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<DetailsViewDescriptor>? offered = null;
        foreach (var d in _descriptors)
        {
            // ⛔ Deliberately UNGUARDED: a predicate that throws is a defect in that view, and
            //    swallowing it here would turn a broken view into a silently missing one.
            if (d.AppliesTo(context)) (offered ??= new List<DetailsViewDescriptor>()).Add(d);
        }

        if (offered is null) return Array.Empty<DetailsViewDescriptor>();

        // ⛔⛔ NOT List.Sort — it is INTROSORT and therefore UNSTABLE, so equal ranks would come back
        //    in an order that depends on list internals and could differ between runs. ⚠ That is
        //    exactly the flip a designer must never see.
        // ⭐ LINQ's OrderByDescending IS documented stable ⇒ equal ranks keep registration order.
        return System.Linq.Enumerable.ToList(
            System.Linq.Enumerable.OrderByDescending(offered, static d => d.Rank));
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
