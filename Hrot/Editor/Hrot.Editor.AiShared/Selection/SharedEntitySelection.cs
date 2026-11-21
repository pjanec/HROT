using Fdp.Core;

namespace Hrot.Editor.AiShared.Selection;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-301</c> — THE AI EDITORS' VIEW OF THE ONE SELECTION. ⛔ NOT a second store.</b>
/// 📄 <c>docs/blueprints/DESIGN_Editor_Entity_Selection_Source.md</c> §2/§3.1.
///
/// <para>⭐⭐ <b>Its ONE production writer is <c>SelectionNotificationSystem</c></b>, from
/// <c>SelectionChangedNotification.Primary</c> — so every cause that can move the host's selection
/// moves this, by construction: a map click, the entity inspector, the orbat, a context-menu
/// <i>Select</i>, a remote <c>CMD_SET_SELECTION</c>. ⛔ <b>Nothing else in production may assign
/// <see cref="Selected"/></b>; a second writer would make this a store again.</para>
///
/// <para>🔴 <b>What it replaced, <c>2026-09-21</c>.</b> The writer used to be
/// <c>CallbackSelectionBridge</c>, subscribed to <c>SelectionInteractionSystem.OnSelectionChanged</c>
/// — <b>a MAP GESTURE</b>. ⇒ the five other causes never reached this cell, so every Watch/Details
/// live-value row kept projecting the PREVIOUS entity. ⭐⭐⭐ <b>The third instance of one shape</b> —
/// <c>UXI-11</c> <c>S-3</c> fixed it inbound, <c>S-6</c> outbound, this one here: <i>a consequence
/// hung off ONE cause instead of the announcement.</i></para>
///
/// <para>⭐ <b>Why this type still exists after becoming a view.</b> It is the CHANGE EDGE, not the
/// storage: it raises <see cref="Changed"/> only on a real transition and bumps
/// <c>EntityBindingFrame</c>, which no ECS component read can do. ⛔ Deleting it and having every
/// store read <c>ISelectionState</c> per-draw would lose the edge that repaints the panels — and
/// <c>Hrot.Editor.AiShared</c> cannot reference the selection assembly anyway.</para>
///
/// <para>⚠ <b>PINNING does not go through here.</b> 🔒 User, <c>2026-09-21</c>: a docked view follows
/// this cell; a PINNED one reads its own frozen <c>DetailsContext</c> snapshot and never consults it
/// (<c>R-100</c>, 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §L4). ⛔ There is deliberately no
/// "pinned" flag on this class — that fact would then live in two places.</para>
///
/// <para>⛔ <b>HISTORY — the Batch 95 (<c>95b</c>) finding that created this type.</b> It is kept
/// because the shape recurs, ⚠ but its "held ONCE for every selection store" framing is superseded by
/// the paragraphs above: the cell is now fed from outside, so <i>which</i> store you write is moot.</para>
///
/// <para>🔴🔴 <b>The defect.</b> 📐 Measured: the editor holds <b>FOUR</b>
/// <see cref="EditorSelectionStore"/>s — one per perspective plus the bridge's own — and
/// <c>CallbackSelectionBridge.Connect</c> is called <b>exactly once</b>, on the fourth. ⇒
/// <c>SelectedEntity</c> was <c>null</c> on all three perspective stores, always ⇒ every live-value
/// provider returned <c>null</c> on its second line ⇒ ⛔ <b>every Details/Watch row on every host
/// rendered <c>(pending)</c> for ever</b>, which is exactly what the user saw.</para>
///
/// <para>⚠ <b>And the composition root already believed otherwise.</b> Its own comment above the two
/// AI providers reads <i>"Both selection stores share the same entity selection (global), so we use
/// one provider instance per perspective; both read the same entity via their respective store."</i>
/// 🔴 <b>That sentence was false</b>, and it is why nothing looked wrong.</para>
///
/// <para>⭐⭐⭐ <b>The design record, cited rather than inferred</b> —
/// 📄 <c>AI_Editor_Shared_Infrastructure.md:450</c>: <i>"SelectedEntity stays global because entities
/// exist independently of which asset is being edited — the same entity is selectable while looking at
/// any of its associated assets."</i> and <c>:45</c>: <c>EditorSelectionStore</c> is <i>"the single
/// selection bus all three editors subscribe to"</i>. ⇒ <b>the entity was never meant to be
/// per-perspective</b>; the split arrived later, for <c>ActiveAsset</c>, and took the entity with it.</para>
///
/// <para>⛔⛔ <b>Why NOT three more <c>Connect</c> calls</b> *(the handoff forbids them, and it is
/// right)*: 📌 that is the shape <c>PerspectiveWorkspaceServices</c> exists to abolish — <i>"the next
/// shared service is one more thing three call sites must remember, and the third one has now
/// forgotten three times."</i> ⭐ The selected entity is <b>ONE FACT ABOUT THE WORLD</b>, so it is
/// stored once and every store reads it.</para>
///
/// <para>⭐ <b>Optional at the store, by design.</b> A store built with none gets its own private cell,
/// so every existing standalone and test construction behaves exactly as before. ⚠ The control that a
/// production store actually joins the shared cell is a rail on the CONSTRUCTED composition root —
/// 📌 <c>M-22</c>: <i>"'is it connected?' is not 'does anything flow?'"</i>.</para>
/// </summary>
public sealed class SharedEntitySelection
{
    private Entity? _selected;

    /// <summary>⭐ Raised when, and only when, the entity actually changes.</summary>
    public event Action? Changed;

    /// <summary>
    /// ⭐ The globally-selected entity, or <c>null</c> when nothing is selected.
    /// ⚠ <c>null</c> is a real state — 📌 the providers treat it as <i>"I cannot project"</i> and every
    /// row falls back to <c>(pending)</c>, which is the honest answer rather than a wrong one.
    /// </summary>
    public Entity? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;

            // ⭐⭐⭐ Batch 97 (97d) — THE BINDING CLOCK. 📌 R-76's second clock: a chameleon row is
            //    bound to "whoever is selected", so a selection change is the ONLY thing that can
            //    move its binding — ⛔ and the brain pulse cannot say so, least of all while time is
            //    stopped. ⭐ Bumped BEFORE the subscribers run, so a panel that repaints inside
            //    Changed already sees the new generation.
            Variables.EntityBindingFrame.Advance();

            Changed?.Invoke();
        }
    }
}
