using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Vis2D.Abstractions
{
    /// <summary>
    /// <b>The selection changed. Published by the one writer, consumed by everything that paints it.</b>
    /// 📄 <c>docs/UX/UX_Feature_Selection.md</c> §2.7.1 / §2.7.2 — <c>UXI-11</c> slice <c>S-3</c>.
    ///
    /// <para>⭐⭐⭐ <b>This is the OTHER half of the request/notify protocol</b> (§2.7.3 rule 4): a surface
    /// publishes a <see cref="SelectionChangeRequest"/> and never writes the store; it learns what the
    /// selection became from this. ⇒ every surface shows the same selection because they all read the
    /// same announcement — ⛔ not because each remembered to update itself.</para>
    ///
    /// <para>🔒 <b><c>R-134</c> — this is an FDP-INTERNAL record and MUST STAY ONE.</b> Two
    /// selection-changed types already exist and ⛔ <b>neither may be used here</b>:
    /// <c>SelectionChangedEvent</c> (<c>[DdsTopic]</c>) and <c>SelectionChangedEventDto</c> are
    /// NETWORK types. 🔒 The ruling is explicit that duplication across the boundary is the correct
    /// pattern, not debt — <i>"even at the cost of keeping the same enum duplicated in two namespaces"</i>.
    /// ⇒ the egress translator converts; nothing carries a DDS type inward.</para>
    ///
    /// <para>⚠ <b>It reports the WHOLE selection, not a delta.</b> A consumer that missed a frame is
    /// still correct after the next one, and a repaint needs the set anyway. ⛔ A delta would make every
    /// subscriber keep its own running copy — which is the parallel-store disease <c>S-1</c> removed.</para>
    ///
    /// <para>⚠ <b>Published AFTER the store is written, in the same tick</b>, so a consumer that reads
    /// <see cref="ISelectionState"/> while handling this sees the new value, never the old one.</para>
    /// </summary>
    public sealed class SelectionChangedNotification
    {
        /// <summary>The complete selection after the change. Empty means nothing is selected.</summary>
        public IReadOnlyList<Entity> Selected { get; init; } = System.Array.Empty<Entity>();

        /// <summary>
        /// The primary entity, or <c>null</c> when the selection is empty.
        /// ⚠ When <see cref="Selected"/> is non-empty this is never <c>null</c> — acceptance 11.3.
        /// </summary>
        public Entity? Primary { get; init; }

        /// <summary>
        /// Free-text origin carried over from the request, for diagnostics only.
        /// ⛔ Nothing may branch on it — a change is a change, whoever caused it (§2.7.3 rule 2).
        /// </summary>
        public string? Reason { get; init; }
    }
}
