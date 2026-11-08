using System;
using System.Collections.Generic;
using Hrot.Common.Actions;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L1.1</c> — ONE DETAILS VIEW, described but not built.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §2's <c>classDiagram</c> — the five members are
/// that diagram's, unchanged; §1 places this in <c>Hrot.Editor.AiShared/Shell/</c>.
///
/// <para>⭐⭐ <b>A DESCRIPTOR, not an instance</b> — 📌 <c>R-120</c>: <i>"a view owns no shared
/// state"</i>. §2: <i>"the registry holds DESCRIPTORS; each window COMPOSES its own instance ⇒ no view
/// instance is shared ⇒ no arbitration."</i> ⇒ ⛔ there is no <c>Instanceable</c> flag and nothing to
/// arbitrate, because two windows showing the same view simply build two instances.</para>
///
/// <para>⭐⭐⭐ <b>The predicate SHIPS WITH THE VIEW</b> *(<c>R-116</c>)* — <see cref="AppliesTo"/> is a
/// field of the descriptor, so the host that knows when its view is relevant is the one that says so.
/// ⛔ The registry never asks what KIND of asset it is: 📌 <c>R-112</c>, <i>"`AssetKind` is never a view
/// key — a host says so in its own predicate."</i></para>
/// </summary>
/// <param name="Id">
///   ⭐ Stable identity, used as the layout key and to remember the designer's pick.
///   ⚠ Must be unique within a registry — <see cref="DetailsViewRegistry.Add"/> refuses a duplicate
///   loudly rather than letting one view silently shadow another.
/// </param>
/// <param name="Title">⭐ What the toolbar shows.</param>
/// <param name="Rank">
///   ⭐ Higher wins when several views apply and the designer has expressed no preference
///   *(<c>R-98</c>: "default by `Rank`")*.
/// </param>
/// <param name="AppliesTo">
///   ⭐⭐ <b>Is this view about THIS context?</b> ⚠ Called every frame, so it must be cheap and free of
///   side effects. ⛔ It must not throw: a predicate that throws would take down the whole offer set,
///   and <see cref="DetailsViewRegistry.OfferSet"/> does not guard it — 📌 a swallowed exception here
///   would turn a broken view into a silently missing one.
/// </param>
/// <param name="Create">
///   ⭐ Builds a fresh instance for ONE window. ⚠ Called by the window, never by the registry
///   *(§2)* — the registry stays a catalogue.
/// </param>
public sealed record DetailsViewDescriptor(
    string                        Id,
    string                        Title,
    int                           Rank,
    Func<DetailsContext, bool>    AppliesTo,
    Func<IDetailsViewInstance>    Create)
{
    /// <summary>
    /// ⭐⭐ B-11 A7 (📄 docs/UX/DESIGN_Action_Catalogue.md §2.1, Editing UX §3.2 / D2) — the components EVERY selected entity must carry for this tab
    /// to apply (AND over the selection, AQ26); empty = none required. Declared as DATA, so a rail reads what a tab needs, and checked by
    /// <see cref="DetailsViewRegistry.Evaluate"/> through the registry's component probe — ⛔ not re-spelled in <see cref="AppliesTo"/>. A registry
    /// with no probe cannot ask, so a tab with a requirement is NOT offered there (a host that cannot ask must not claim yes).
    /// </summary>
    public IReadOnlyList<Type> Requires { get; init; } = Array.Empty<Type>();

    /// <summary>
    /// ⭐⭐ B-11 A7 — the modes the tab is offered / greyed in, and the lowest station level that may use it: the SAME declaration an
    /// <c>ActionDecl</c> carries (<c>ActionAvailability</c>; the mode is <c>Hrot.Common.Editing.EditingMode</c> read from <c>StationMode</c>).
    /// Default: every mode, to every station.
    /// </summary>
    public ActionAvailability Availability { get; init; } = ActionAvailability.Always;

    /// <summary>
    /// ⭐ B-11 A7 (D2: "host services become capabilities every host passes or the tab greys") — whether THIS host binds what the tab needs (the
    /// mission facade for Orders). False ⇒ a tab that otherwise applies is shown GREYED, "not available on this station" (the A2 rule for an
    /// action with no handler). Null = always bound.
    /// </summary>
    public Func<bool>? Bound { get; init; }

    /// <summary>The reason a tab with no binding on this host is greyed — the same words as an action with no handler (A2).</summary>
    public const string NotBoundReason = "Not available on this station";
}

/// <summary>
/// ⭐⭐ <b><c>L1.1</c> — one LIVE view, owned by one window.</b> 📄 §2's <c>classDiagram</c>.
///
/// <para>⭐ <b>It may legitimately hold state</b> — §1: <i>"an uncommitted edit buffer, a cache, a
/// scroll position ⇒ the view instance, legitimately"</i>. ⛔ What it must NOT hold is state SHARED
/// with another instance of the same view *(<c>R-120</c>)*, which is why the registry hands out
/// factories rather than objects.</para>
/// </summary>
public interface IDetailsViewInstance : IDisposable
{
    /// <summary>
    /// ⭐ Draw this view for <paramref name="context"/>.
    /// <param name="idScope">⚠ A per-window ImGui id scope — ⛔ two windows showing the same view must
    /// not collide on widget ids, and the WINDOW is what knows which one it is.</param>
    /// </summary>
    void Draw(DetailsContext context, string idScope);
}
