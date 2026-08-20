using Fdp.Core;

namespace Hrot.Editor.AiShared.Selection;

/// <summary>
/// ⭐⭐⭐ <b>Batch 95 (<c>95b</c>) — THE SELECTED ENTITY, held ONCE for every selection store.</b>
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
            Changed?.Invoke();
        }
    }
}
