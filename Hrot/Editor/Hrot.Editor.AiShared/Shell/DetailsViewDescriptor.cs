using System;

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
    Func<IDetailsViewInstance>    Create);

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
