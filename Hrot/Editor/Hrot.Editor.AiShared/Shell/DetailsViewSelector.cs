using System;
using System.Collections.Generic;
using System.Text;
using Hrot.Editor.AiShared.Selection;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L2.2</c> — WHICH VIEW THE SHELL SHOWS, and what the toolbar remembers.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §2b's <c>stateDiagram</c> *(the four states)* ·
/// §2's <i>"The context key"</i> · §3's <c>R-98</c> row *(<i>"offer set from the predicates; default by
/// `Rank`; the user's pick remembered per §2's key"</i>)*.
///
/// <para>⭐⭐ <b>The MODEL, not the toolbar.</b> The ImGui toggles are a draw and are unrailed by
/// construction *(§6's limits)*; ⭐ everything that DECIDES is here, so §2b's state machine is a value
/// assertion.</para>
///
/// <para>⭐⭐⭐ <b>The key is <c>(Perspective, AssetId, selection SHAPE)</c></b> — §2, verbatim.
/// ⚠ <b>SHAPE, not identity</b>: <i>"node A → node B keeps the view; a variable pick remembers its
/// own."</i> ⇒ ⭐ the shape is the ordered list of selection TYPES, so swapping one node for another is
/// the same shape and the pick survives, while switching to a variable row is a different shape and
/// gets its own memory.</para>
/// </summary>
public sealed class DetailsViewSelector
{
    private readonly Dictionary<string, string> _pickByKey = new(StringComparer.Ordinal);

    /// <summary>⭐ §2b's four states, named so a rail asserts the STATE rather than inferring it from
    /// which id came back.</summary>
    public enum Mode
    {
        /// <summary>⭐ Nothing applies — 📌 <c>R-117</c>'s grey line, ⛔ never a blank.</summary>
        EmptyOffer,
        /// <summary>⭐ The highest <c>Rank</c> that applies *(<c>R-98</c>)*.</summary>
        RankDefault,
        /// <summary>⭐ The designer picked this one, and it still applies.</summary>
        UserPick,
    }

    /// <summary>⭐ What the shell should draw this frame, and why.</summary>
    /// <param name="View">⚠ <see langword="null"/> exactly when <paramref name="State"/> is
    /// <see cref="Mode.EmptyOffer"/> — ⛔ the two never disagree.</param>
    /// <param name="State">⭐ §2b's state, named — ⛔ so a rail asserts the STATE rather than inferring
    /// it from which id came back.</param>
    /// <param name="Offered">
    ///   ⭐⭐ <b>The whole offer set, <c>Rank</c>-ordered</b> — the toolbar's buttons ARE this list
    ///   *(<c>R-98</c>: "toolbar is a panel switch")*. ⛔ Never null; empty exactly when
    ///   <see cref="Mode.EmptyOffer"/>.
    ///   <para>⚠ Carried on the choice rather than re-queried by the toolbar, deliberately: two
    ///   <c>OfferSet</c> calls in one frame could disagree if a predicate is not pure, and the button
    ///   row would then not match the view being drawn.</para>
    /// </param>
    public readonly record struct Choice(
        DetailsViewDescriptor? View,
        Mode State,
        IReadOnlyList<DetailsViewDescriptor> Offered);

    /// <summary>
    /// ⭐⭐⭐ <b>Resolve this frame's view.</b>
    ///
    /// <para>⭐ §2b's transitions, all four:
    /// <list type="bullet">
    ///   <item><c>[*] → RankDefault</c> — no pick yet</item>
    ///   <item><c>RankDefault → UserPick</c> — the designer clicks a toggle</item>
    ///   <item><c>UserPick → UserPick</c> — <b>context changes, pick still applies</b></item>
    ///   <item><c>UserPick → RankDefault</c> — <b>pick no longer applies</b>. ⛔ §2: <i>"never to a
    ///   blank panel"</i></item>
    ///   <item><c>→ EmptyOffer</c> — the offer set is empty; ⭐ the grey line</item>
    /// </list></para>
    ///
    /// <para>⚠ <b>A pick that stops applying is FORGOTTEN, not merely ignored.</b> ⛔ Keeping it would
    /// make the panel jump back to a view the designer last saw three selections ago the moment it
    /// happened to apply again — ⭐ surprising, and indistinguishable from a bug.</para>
    /// </summary>
    public Choice Resolve(DetailsViewRegistry registry, DetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(context);

        var offered = registry.OfferSet(context);
        if (offered.Count == 0) return new Choice(null, Mode.EmptyOffer, offered);

        var key = KeyOf(context);
        if (_pickByKey.TryGetValue(key, out var pickedId))
        {
            foreach (var d in offered)
                if (string.Equals(d.Id, pickedId, StringComparison.Ordinal))
                    return new Choice(d, Mode.UserPick, offered);

            // ⭐ It no longer applies ⇒ forget it and fall back by Rank.
            _pickByKey.Remove(key);
        }

        return new Choice(offered[0], Mode.RankDefault, offered);   // ⭐ offered is Rank-ordered
    }

    /// <summary>
    /// ⭐ The designer clicked a toggle. ⚠ Recorded against the CURRENT context's key, so the same
    /// pick is restored for the same shape and not carried onto an unrelated one.
    /// </summary>
    public void Pick(DetailsContext context, string viewId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(viewId);
        _pickByKey[KeyOf(context)] = viewId;
    }

    /// <summary>⭐ Forget the pick for this context — the toolbar's <i>"back to default"</i>.</summary>
    public void ClearPick(DetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _pickByKey.Remove(KeyOf(context));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>§2's context key: <c>(Perspective, AssetId, selection SHAPE)</c>.</b>
    ///
    /// <para>⭐⭐ <b>SHAPE means the ordered list of selection TYPES</b>, ⛔ not their ids. §2:
    /// <i>"node A → node B keeps the view."</i> ⇒ two <c>BlueprintNodeSelection</c>s hash the same,
    /// and a <c>HsmStateSelection</c> does not.</para>
    ///
    /// <para>⚠ <b>ORDER is part of the shape</b>, deliberately — it is part of the SET's identity too
    /// *(<c>L0.1</c>'s elementwise guard)*, so the two agree rather than disagreeing by one being
    /// order-blind.</para>
    /// </summary>
    internal static string KeyOf(DetailsContext context)
    {
        var sb = new StringBuilder(context.Perspective);
        sb.Append('|').Append(context.Asset?.AssetId.ToString() ?? "-");
        sb.Append('|');
        foreach (var s in context.Selection) sb.Append(s.GetType().Name).Append(',');
        return sb.ToString();
    }
}
